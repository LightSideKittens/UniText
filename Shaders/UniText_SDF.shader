// Unified SDF text rendering — handles both face and effect in a single pass.
// Mode is determined per-vertex by UV2: zeros = face, non-zero = effect.
// One SDF sample per pixel, no overdraw, one draw call per material.

Shader "UniText/SDF" {

Properties {
	_ShaderFlags		("Flags", float) = 0
	_MainTex			("Font Atlas", 2DArray) = "" {}

	_ScaleX				("Scale X", float) = 1
	_ScaleY				("Scale Y", float) = 1
	_PerspectiveFilter	("Perspective Correction", Range(0, 1)) = 0.875
	_Sharpness			("Sharpness", Range(-1,1)) = 0

	_VertexOffsetX		("Vertex OffsetX", float) = 0
	_VertexOffsetY		("Vertex OffsetY", float) = 0

	_ClipRect			("Clip Rect", vector) = (-32767, -32767, 32767, 32767)
	_MaskSoftnessX		("Mask SoftnessX", float) = 0
	_MaskSoftnessY		("Mask SoftnessY", float) = 0

	_StencilComp		("Stencil Comparison", Float) = 8
	_Stencil			("Stencil ID", Float) = 0
	_StencilOp			("Stencil Operation", Float) = 0
	_StencilWriteMask	("Stencil Write Mask", Float) = 255
	_StencilReadMask	("Stencil Read Mask", Float) = 255

	_CullMode			("Cull Mode", Float) = 0
	_ColorMask			("Color Mask", Float) = 15
}

SubShader {
	Tags
	{
		"Queue"="Transparent"
		"IgnoreProjector"="True"
		"RenderType"="Transparent"
	}

	Stencil
	{
		Ref [_Stencil]
		Comp [_StencilComp]
		Pass [_StencilOp]
		ReadMask [_StencilReadMask]
		WriteMask [_StencilWriteMask]
	}

	Cull [_CullMode]
	ZWrite Off
	Lighting Off
	Fog { Mode Off }
	ZTest [unity_GUIZTestMode]
	Blend One OneMinusSrcAlpha
	ColorMask [_ColorMask]

	Pass {
		Name "SDF_UNIFIED"
		CGPROGRAM
		#pragma vertex VertShader
		#pragma fragment PixShader
		#pragma target 3.5

		#pragma multi_compile __ UNITY_UI_CLIP_RECT
		#pragma multi_compile __ UNITY_UI_ALPHACLIP
		#pragma multi_compile __ UNITEXT_MSDF

		#include "UniText_SDF-Common.cginc"

		struct pixel_t
		{
			UNITY_VERTEX_INPUT_INSTANCE_ID
			UNITY_VERTEX_OUTPUT_STEREO
			float4 vertex       : SV_POSITION;
			float3 atlasUV      : TEXCOORD0; // xy = atlas UV, z = page layer
			float2 glyphUV      : TEXCOORD1; // for fwidth AA
			half4  params       : TEXCOORD2; // x = faceDilate, y = effectDilate, z = softness, w = glyphH
			half4  mask         : TEXCOORD3;
			fixed4 faceColor    : TEXCOORD4; // premultiplied vertex color (face mode)
			fixed4 effectColor  : TEXCOORD5; // premultiplied effect color (effect mode); a=0 → face mode
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

			float2 pixelSize = vPosition.w;
			pixelSize /= float2(_ScaleX, _ScaleY) * abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));

			fixed4 color = GammaToLinearIfNeeded(input.color);
			float vertexAlpha = color.a;

			// Face color: premultiplied vertex color
			fixed4 faceColor = color;
			faceColor.rgb *= faceColor.a;

			// Effect data from UV2 (zeros = face mode)
			float effectDilate  = input.texcoord2.x;
			float effectColPack = input.texcoord2.y;
			float effectSoft    = input.texcoord2.w;

			half4 effectCol = UnpackColor(effectColPack);
			effectCol.a *= vertexAlpha;
			effectCol.rgb *= effectCol.a;

			// Pre-compute atlas UV
			float glyphH = input.texcoord0.w;
			float faceDilate = input.texcoord1.y;
			float2 sdfScale, sdfOffset;
			float pageLayer;
			ComputeSDFTransform(input.texcoord0.z, input.texcoord1.x, glyphH, sdfScale, sdfOffset, pageLayer);

			output.vertex = vPosition;
			output.atlasUV = float3(input.texcoord0.xy * sdfScale + sdfOffset, pageLayer);
			output.glyphUV = input.texcoord0.xy;
			output.params = half4(faceDilate, effectDilate, effectSoft, glyphH);
			output.mask = ComputeMask(vert, pixelSize);
			output.faceColor = faceColor;
			output.effectColor = effectCol;

			return output;
		}

		fixed4 PixShader(pixel_t input) : SV_Target
		{
			UNITY_SETUP_INSTANCE_ID(input);

			float glyphH = input.params.w;
			float faceDilate = input.params.x;

			float signedDist = SAMPLE_SDF(input.atlasUV);

			float2 dUV = fwidth(input.glyphUV);
			float aaWidth = max(dUV.x, dUV.y) * glyphH;

			half4 result;

			// Mode detection via UV2: when UV2 = zeros (face mode), packed color = 0,
			// which UnpackColor decodes as RGBA(0,0,0,0) → effectColor.a = 0 after premultiply.
			// When UV2 has effect data, packed color is non-zero → effectColor.a > 0.
			// Branch is coherent per-quad (all 4 vertices share the same UV2 mode).
			if (input.effectColor.a < 0.001)
			{
				// Face mode: render glyph with vertex color
				float faceDist = signedDist - faceDilate * DILATE_SCALE;
				float alpha = saturate(-faceDist / aaWidth + 0.5);
				result = input.faceColor * alpha;
			}
			else
			{
				// Effect mode: render effect (outline, shadow) with effect color
				float effectDilate = input.params.y * DILATE_SCALE;
				float threshold = -(faceDilate * DILATE_SCALE + effectDilate);
				float softEdge = max(aaWidth, input.params.z);
				float alpha = saturate((-signedDist - threshold) / softEdge);
				result = input.effectColor * alpha;
			}

			return ApplyClipping(result, input.mask);
		}
		ENDCG
	}
}
}
