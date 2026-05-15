// Common Mobile SDF shader code (Screen Space Derivative version)
// Used by Mobile SSD, Overlay, Masking shaders
// Uses ddx/ddy for scale calculation instead of vertex-computed scale

#include "UniText_SDF-Mobile-Common.cginc"

// Unity auto-provided: (1/width, 1/height, width, height)
float4 _MainTex_TexelSize;

struct pixel_t
{
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
    float4 position      : SV_POSITION;
    float4 faceColor     : COLOR;
    float4 outlineColor  : COLOR1;
    float4 texcoord0     : TEXCOORD0;  // xy = UV, zw = maskUV
    float4 param         : TEXCOORD1;  // weight, gradientScale, spreadRatio, unused
    float2 mask          : TEXCOORD2;
    #if (UNDERLAY_ON || UNDERLAY_INNER)
    float4 texcoord2     : TEXCOORD3;  // xy = underlayUV, z = alpha, w = unused
    float4 underlayColor : COLOR2;
    #endif
};

pixel_t VertShader(sdf_vertex_t input)
{
    pixel_t output;

    UNITY_INITIALIZE_OUTPUT(pixel_t, output);
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    float4 vert = ApplyVertexOffset(input.vertex);
    float4 vPosition = UnityObjectToClipPos(vert);

    float baseWeight = ComputeBaseWeight(input.texcoord0);
    float spreadRatio = input.texcoord1.x;
    float gradientScaleVal = input.texcoord0.z;

    // Generate UV for the Masking Texture
    float4 clampedRect = clamp(_ClipRect, -2e10, 2e10);
    float2 maskUV = (vert.xy - clampedRect.xy) / (clampedRect.zw - clampedRect.xy);

    float4 color = GammaToLinearIfNeeded(input.color);

    float opacity = color.a;
    #if (UNDERLAY_ON | UNDERLAY_INNER)
    opacity = 1.0;
    #endif

    float4 faceColor = float4(color.rgb, opacity) * _FaceColor;
    faceColor.rgb *= faceColor.a;

    float4 outlineColor = _OutlineColor;
    outlineColor.a *= opacity;
    outlineColor.rgb *= outlineColor.a;

    output.position = vPosition;
    output.faceColor = faceColor;
    output.outlineColor = outlineColor;
    output.texcoord0 = float4(input.texcoord0.xy, maskUV.xy);
    // Pass baseWeight, gradientScale, spreadRatio for pixel shader
    output.param = float4(0.5 - baseWeight, gradientScaleVal, spreadRatio, 0);

    float2 mask = float2(0, 0);
    #if UNITY_UI_CLIP_RECT
    mask = vert.xy * 2 - clampedRect.xy - clampedRect.zw;
    #endif
    output.mask = mask;

    #if (UNDERLAY_ON || UNDERLAY_INNER)
    float4 underlayColor = _UnderlayColor;
    underlayColor.rgb *= underlayColor.a;

    // Underlay UV offset (independent of atlas settings)
    float normFactor = REFERENCE_SPREAD_RATIO / max(spreadRatio, 0.001);
    float offsetFactor = ComputeUnderlayOffsetFactor(gradientScaleVal, normFactor);
    float x = -(_UnderlayOffsetX * _ScaleRatioC) * offsetFactor * _MainTex_TexelSize.x;
    float y = -(_UnderlayOffsetY * _ScaleRatioC) * offsetFactor * _MainTex_TexelSize.y;

    output.texcoord2 = float4(input.texcoord0.xy + float2(x, y), input.color.a, 0);
    output.underlayColor = underlayColor;
    #endif

    return output;
}

float4 PixShader(pixel_t input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);

    float d = tex2D(_MainTex, input.texcoord0.xy).a;

    // Screen-space derivative scale calculation
    float pixelSize = abs(ddx(input.texcoord0.y)) + abs(ddy(input.texcoord0.y));
    pixelSize *= _MainTex_TexelSize.w * 0.75;  // textureHeight
    float baseScale = 1 / pixelSize * (_Sharpness + 1);

    float baseWeightTerm = input.param.x;  // 0.5 - baseWeight
    float gradientScale = input.param.y;
    float spreadRatio = input.param.z;

    // SDF scale for anti-aliasing
    float scale = baseScale * gradientScale;

    // Normalization factor for effects (independent of atlas settings)
    float normFactor = REFERENCE_SPREAD_RATIO / max(spreadRatio, 0.001);

    #if (UNDERLAY_ON | UNDERLAY_INNER)
    float softnessFactor = _UnderlaySoftness * _ScaleRatioC * normFactor;
    float layerScale = scale / (1 + softnessFactor);
    float underlayDilate = _FaceDilate * _ScaleRatioA + _UnderlayDilate * _ScaleRatioC;
    float normalizedUnderlayEffect = (0.5 - baseWeightTerm + underlayDilate * 0.5) * normFactor;
    float layerBias = (0.5 - normalizedUnderlayEffect) * layerScale - 0.5;
    #endif

    float softnessFactor2 = _OutlineSoftness * _ScaleRatioA * normFactor;
    float scaleSoftness = scale / (1 + softnessFactor2);

    // Face layer (dilate normalized via normFactor)
    float normalizedFaceEffect = (0.5 - baseWeightTerm + _FaceDilate * _ScaleRatioA * 0.5) * normFactor;
    float faceBias = (0.5 - normalizedFaceEffect) * scaleSoftness - 0.5;
    float4 faceColor = input.faceColor * saturate(d * scaleSoftness - faceBias);

    // Outline layer (same logic as face, different dilate)
    #if OUTLINE_ON
    float normalizedOutlineEffect = (0.5 - baseWeightTerm + (_FaceDilate + _OutlineDilate) * _ScaleRatioA * 0.5) * normFactor;
    float outlineBias = (0.5 - normalizedOutlineEffect) * scaleSoftness - 0.5;
    float4 outlineResult = input.outlineColor * saturate(d * scaleSoftness - outlineBias);
    // Blend: outline behind face
    faceColor = BlendOver(outlineResult, faceColor);
    #endif

    // Underlay layer
    #if UNDERLAY_ON
    float ud = tex2D(_MainTex, input.texcoord2.xy).a * layerScale;
    float4 underlayResult = input.underlayColor * saturate(ud - layerBias);
    faceColor = BlendOver(underlayResult, faceColor);
    #endif

    #if UNDERLAY_INNER
    float ud = tex2D(_MainTex, input.texcoord2.xy).a * layerScale;
    float faceMask = saturate(d * scaleSoftness - faceBias);
    float4 underlayResult = input.underlayColor * (1 - saturate(ud - layerBias)) * faceMask;
    faceColor = BlendOver(underlayResult, faceColor);
    #endif

    #if MASKING
    float a = abs(_MaskInverse - tex2D(_MaskTex, input.texcoord0.zw).a);
    float t = a + (1 - _MaskWipeControl) * _MaskEdgeSoftness - _MaskWipeControl;
    a = saturate(t / _MaskEdgeSoftness);
    faceColor.rgb = lerp(_MaskEdgeColor.rgb * faceColor.a, faceColor.rgb, a);
    faceColor *= a;
    #endif

    // Alternative implementation to UnityGet2DClipping with support for softness
    #if UNITY_UI_CLIP_RECT
    half2 maskSoftness = half2(max(_UIMaskSoftnessX, _MaskSoftnessX), max(_UIMaskSoftnessY, _MaskSoftnessY));
    float2 maskZW = 0.25 / (0.25 * maskSoftness + 1 / scale);
    float2 m = saturate((_ClipRect.zw - _ClipRect.xy - abs(input.mask.xy)) * maskZW);
    faceColor *= m.x * m.y;
    #endif

    #if (UNDERLAY_ON | UNDERLAY_INNER)
    faceColor *= input.texcoord2.z;
    #endif

    #if UNITY_UI_ALPHACLIP
    clip(faceColor.a - 0.001);
    #endif

    return faceColor;
}
