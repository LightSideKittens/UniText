// Common functions for UniText SDF text rendering
// Glyph outlines are extracted at runtime from FreeType and rasterized into
// adaptively-sized SDF tiles (64/128/256) on CPU. The shader samples one tex2D
// per pixel — O(1).
//
// SDF stores signed distance (not coverage), so one tile serves all effect
// variations (outline, underlay) and all screen sizes (bilinear interpolation).
//
// Atlas encoding: signed distance in EM-space [-0.5, 0.5] mapped to R16F [0, 1].
//   CPU pipeline:    encoded = saturate(sign * dist_glyph * glyphH + 0.5)
//   This shader:     dist_em = tex.r - 0.5
//
// Isotropic layout: uniform scale in X and Y, glyph centered with padding.
//   maxDim = max(aspect, 1), totalExtent = maxDim + 2*padGlyph
//   scale = tileSize / totalExtent (same for both axes)
//   glyphOffset = ((maxDim - dim)/2 + padGlyph) per axis
//
// Shelf encoding (UV0.z = fullEncoded):
//   fullEncoded = tileCol + shelfRow * COL_SLOTS + sizeClass * 4096 + pageIndex * PAGE_STRIDE
//   Shader extracts: pageLayer = enc / PAGE_STRIDE, then tile decode from remainder.
//   sizeClass: 0=64px, 1=128px, 2=256px
//   tileSize = GRID_UNIT * (1 << sizeClass)

#ifndef UNITEXT_SDF_COMMON_INCLUDED
#define UNITEXT_SDF_COMMON_INCLUDED

#include "UnityCG.cginc"
#include "UnityUI.cginc"
#include "UniText_Properties.cginc"

// _MainTex is a Texture2DArray — one slice per atlas page (declared in UniText_Properties.cginc)
// All SDF text shares a single texture binding; page layer is encoded in UV0.z.

#define SDF_PAGE_SIZE 2048.0
#define SDF_PAD 0.5
#define DILATE_SCALE SDF_PAD
#define PAGE_STRIDE 16384

// Common uniforms
float _UIMaskSoftnessX;
float _UIMaskSoftnessY;
int _UIVertexColorAlwaysGammaSpace;

// Input vertex structure for SDF text
struct sdf_vertex_t
{
    UNITY_VERTEX_INPUT_INSTANCE_ID
    float4 vertex    : POSITION;
    float3 normal    : NORMAL;
    fixed4 color     : COLOR;
    float4 texcoord0 : TEXCOORD0; // xy = glyph UV, z = encodedTile, w = glyphH (em-space height for distance scaling)
    float4 texcoord1 : TEXCOORD1; // x = aspect (glyphW/glyphH), y = faceDilate, z = (free), w = (free)
    float4 texcoord2 : TEXCOORD2; // effect data: x = dilate, y = packedColor, z = (free), w = softness
    float4 texcoord3 : TEXCOORD3; // (free)
};

// ============================================
// Bit-unpacking helpers (match C# EffectPacking)
// ============================================

half4 UnpackColor(float packed)
{
    uint u = asuint(packed);
    return half4(
        ((u >> 24) & 0xFF) / 255.0,
        ((u >> 16) & 0xFF) / 255.0,
        ((u >> 8)  & 0xFF) / 255.0,
        (u         & 0xFF) / 255.0
    );
}

// ============================================
// SDF atlas lookup — vertex-side pre-computation
// ============================================

// Compute linear transform coefficients: atlasUV = glyphUV * sdfScale + sdfOffset.
// Called once per vertex; GPU interpolates atlasUV across the quad for free.
// Fragment shader reduces to: UNITY_SAMPLE_TEX2DARRAY(_MainTex, float3(atlasUV, pageLayer)).r - 0.5
//
// Decodes fullEncoded = tileEnc + pageIndex * PAGE_STRIDE:
//   pageLayer = enc / PAGE_STRIDE
//   sizeClass = tileEnc / 4096       → {0, 1, 2}
//   shelfRow  = (tileEnc % 4096) / COL_SLOTS
//   tileCol   = (tileEnc % 4096) % COL_SLOTS
//   tileSize  = GRID_UNIT * (1 << sizeClass)  → {64, 128, 256}
#define GRID_UNIT 64
#define COL_SLOTS 32
void ComputeSDFTransform(float fullEncoded, float aspect, float glyphH,
                         out float2 sdfScale, out float2 sdfOffset, out float pageLayer)
{
    uint enc = (uint)(fullEncoded + 0.5);
    uint page = enc >> 14u;           // PAGE_STRIDE = 16384 = 2^14
    enc &= 16383u;
    pageLayer = (float)page;

    uint sizeClass = enc >> 12u;      // 4096 = 2^12
    uint rem = enc & 4095u;
    uint shelfRow = rem >> 5u;        // COL_SLOTS = 32 = 2^5
    uint tileCol = rem & 31u;

    float tileSize = (float)GRID_UNIT * (float)(1 << sizeClass);
    float padGlyph = SDF_PAD / max(glyphH, 1e-6);

    // Isotropic layout: uniform scale, glyph centered in tile
    float maxDim = max(aspect, 1.0);
    float totalExtent = maxDim + 2.0 * padGlyph;

    float invPage = 1.0 / SDF_PAGE_SIZE;
    float2 tileOrigin = float2(tileCol * tileSize, shelfRow * (float)GRID_UNIT) * invPage;
    float s = tileSize * invPage / totalExtent;

    float2 glyphOffset = float2(
        (maxDim - aspect) * 0.5 + padGlyph,
        (maxDim - 1.0) * 0.5 + padGlyph
    );

    sdfScale = float2(s, s);
    sdfOffset = tileOrigin + glyphOffset * s;
}

// ============================================
// Mask / clipping
// ============================================

half4 ComputeMask(float4 vert, float2 pixelSize)
{
    float4 clampedRect = clamp(_ClipRect, -2e10, 2e10);
    half2 maskSoftness = half2(max(_UIMaskSoftnessX, _MaskSoftnessX), max(_UIMaskSoftnessY, _MaskSoftnessY));
    return half4(vert.xy * 2 - clampedRect.xy - clampedRect.zw, 0.25 / (0.25 * maskSoftness + pixelSize));
}

float4 ApplyVertexOffset(float4 vertex)
{
    vertex.x += _VertexOffsetX;
    vertex.y += _VertexOffsetY;
    return vertex;
}

fixed4 GammaToLinearIfNeeded(fixed4 color)
{
    if (_UIVertexColorAlwaysGammaSpace && !IsGammaSpace())
    {
        color.rgb = UIGammaToLinear(color.rgb);
    }
    return color;
}

half4 ApplyClipping(half4 color, half4 mask)
{
    #if UNITY_UI_CLIP_RECT
    half2 m = saturate((_ClipRect.zw - _ClipRect.xy - abs(mask.xy)) * mask.zw);
    color *= m.x * m.y;
    #endif

    #if UNITY_UI_ALPHACLIP
    clip(color.a - 0.001);
    #endif

    return color;
}

half4 BlendOver(half4 dst, half4 src)
{
    dst.rgb = dst.rgb * (1.0 - src.a) + src.rgb;
    dst.a = saturate(dst.a + src.a);
    return dst;
}

// ============================================
// SDF / MSDF sampling
// ============================================

float median3(float3 v)
{
    return max(min(v.r, v.g), min(max(v.r, v.g), v.b));
}

#ifdef UNITEXT_MSDF
    #define SAMPLE_SDF(uv) (median3(UNITY_SAMPLE_TEX2DARRAY(_MainTex, uv).rgb) - 0.5)
#else
    #define SAMPLE_SDF(uv) (UNITY_SAMPLE_TEX2DARRAY(_MainTex, uv).r - 0.5)
#endif

#endif // UNITEXT_VECTOR_COMMON_INCLUDED
