// Common structures and functions for Mobile SDF shaders
// Used by both SDF-Mobile-Face and SDF-Mobile-Base

#ifndef UNITEXT_SDF_MOBILE_COMMON_INCLUDED
#define UNITEXT_SDF_MOBILE_COMMON_INCLUDED

#include "UnityCG.cginc"
#include "UnityUI.cginc"
#include "UniText_Properties.cginc"

// Reference spreadRatio for effect normalization (Padding=9, PointSize=90 -> 9/90 = 0.1)
#define REFERENCE_SPREAD_RATIO 0.1

// Input vertex structure
struct sdf_vertex_t
{
    UNITY_VERTEX_INPUT_INSTANCE_ID
    float4 vertex    : POSITION;
    float3 normal    : NORMAL;
    fixed4 color     : COLOR;
    float4 texcoord0 : TEXCOORD0;  // xy = UV, z = gradientScale, w = xScaleVal
    float4 texcoord1 : TEXCOORD1;  // x = spreadRatio (Padding / PointSize)
};

// Common uniforms
float _UIMaskSoftnessX;
float _UIMaskSoftnessY;
int _UIVertexColorAlwaysGammaSpace;

// Compute SDF scale and normalization factor from vertex data
// Returns float2(scale, normFactor)
// scale: for anti-aliasing sharpness
// normFactor: for effect normalization (makes effects independent of Padding/PointSize)
float2 ComputeScaleAndNorm(float4 vPosition, float3 normal, float4 vert, float4 texcoord0, float4 texcoord1)
{
    float2 pixelSize = vPosition.w;
    pixelSize /= float2(_ScaleX, _ScaleY) * abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));

    float baseScale = rsqrt(dot(pixelSize, pixelSize)) * (_Sharpness + 1) * 0.85;

    if (UNITY_MATRIX_P[3][3] == 0)
    {
        float perspective = abs(dot(UnityObjectToWorldNormal(normal), normalize(WorldSpaceViewDir(vert))));
        baseScale = lerp(abs(baseScale) * (1 - _PerspectiveFilter), baseScale, perspective);
    }

    float xScaleVal = abs(texcoord0.w);
    float gradientScale = texcoord0.z;
    float spreadRatio = texcoord1.x;

    // Derive actual padding from gradientScale and spreadRatio:
    // gradientScale = PointSize * Padding / 72, spreadRatio = Padding / PointSize
    // → gradientScale * spreadRatio = Padding² / 72 → Padding = sqrt(72 * gradientScale * spreadRatio)
    // Using Padding (like TMP's _GradientScale) makes scale independent of PointSize
    float atlasPadding = sqrt(72.0 * gradientScale * max(spreadRatio, 0.001));

    // SDF scale for anti-aliasing (independent of PointSize)
    float scale = baseScale * xScaleVal * atlasPadding;

    // Normalization factor for effects
    // spreadRatio = Padding / PointSize (how much of glyph size is the spread zone)
    // normFactor compensates for different spreadRatios to keep visual effect constant
    float normFactor = REFERENCE_SPREAD_RATIO / max(spreadRatio, 0.001);

    return float2(scale, normFactor);
}

// Compute base weight (boldness) from vertex data - WITHOUT dilate
float ComputeBaseWeight(float4 texcoord0)
{
    float bold = step(texcoord0.w, 0);
    float weight = lerp(_WeightNormal, _WeightBold, bold) / 4.0;
    return weight * _ScaleRatioA * 0.5;
}

// Compute bias with effects normalized by normFactor (independent of Padding/PointSize)
// scale: controls anti-aliasing sharpness
// normFactor: normalizes effect strength to be independent of atlas settings
float ComputeBias(float baseWeight, float dilate, float scale, float normFactor)
{
    // Effects are multiplied by normFactor to compensate for different spreadRatios
    // Then multiplied by scale to work in the same coordinate system as the SDF
    float normalizedEffect = (baseWeight + dilate * _ScaleRatioA * 0.5) * normFactor;
    return (0.5 - normalizedEffect) * scale - 0.5;
}

// Compute mask softness
half4 ComputeMask(float4 vert, float2 pixelSize)
{
    float4 clampedRect = clamp(_ClipRect, -2e10, 2e10);
    half2 maskSoftness = half2(max(_UIMaskSoftnessX, _MaskSoftnessX), max(_UIMaskSoftnessY, _MaskSoftnessY));
    return half4(vert.xy * 2 - clampedRect.xy - clampedRect.zw, 0.25 / (0.25 * maskSoftness + pixelSize));
}

// Apply vertex offset
float4 ApplyVertexOffset(float4 vertex)
{
    vertex.x += _VertexOffsetX;
    vertex.y += _VertexOffsetY;
    return vertex;
}

// Convert color from gamma to linear if needed
fixed4 GammaToLinearIfNeeded(fixed4 color)
{
    if (_UIVertexColorAlwaysGammaSpace && !IsGammaSpace())
    {
        color.rgb = UIGammaToLinear(color.rgb);
    }
    return color;
}

// ============================================
// SDF Layer rendering functions
// ============================================

// Render a single SDF layer
// d = sampled SDF value * scale
// threshold = bias value
// color = premultiplied alpha color
half4 SDFLayer(half d, float threshold, half4 color)
{
    return color * saturate(d - threshold);
}

// Sample SDF and render layer
half4 SDFLayerSample(float2 uv, float scale, float threshold, half4 color)
{
    half d = tex2D(_MainTex, uv).a * scale;
    return color * saturate(d - threshold);
}

// Render outline layer (with inner and outer edges)
// Returns color with proper alpha for blending
half4 SDFOutlineLayer(half d, float biasOuter, float biasInner, half4 color)
{
    half outerFade = saturate(d - biasOuter);  // 0 outside, 1 in outline and face
    half innerFade = 1 - saturate(d - biasInner);  // 1 in outline, 0 inside face
    return color * outerFade * innerFade;
}

// Apply UI clipping
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

// Blend layer on top of existing result (premultiplied alpha)
half4 BlendOver(half4 dst, half4 src)
{
    dst.rgb = dst.rgb * (1 - src.a) + src.rgb;
    dst.a = saturate(dst.a + src.a);
    return dst;
}

// Compute underlay UV offset factor (independent of Padding, proportional to PointSize)
// gradientScale ≈ PointSize * Padding / 72
// spreadRatio = Padding / PointSize
// From these: PointSize = sqrt(72 * gradientScale / spreadRatio)
//                       = sqrt(72 * gradientScale * normFactor / REFERENCE_SPREAD_RATIO)
// Returns offset factor that produces consistent visual offset regardless of atlas settings
float ComputeUnderlayOffsetFactor(float gradientScale, float normFactor)
{
    // Derive approximate PointSize from gradientScale and normFactor
    float pointSizeApprox = sqrt(72.0 * gradientScale * normFactor / REFERENCE_SPREAD_RATIO);

    // Scale factor: at reference (PointSize=90, Padding=9), offsetFactor=10
    // This maintains backwards compatibility with existing offset slider values
    // 90 / 9 = 10, so we divide by 9 (reference Padding)
    return pointSizeApprox / 9.0;
}

#endif // UNITEXT_SDF_MOBILE_COMMON_INCLUDED
