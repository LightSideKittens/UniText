// Simplified SDF shader - Face pass for 2-pass rendering (rendered second, on top of Base)
// - No Shading Option (bevel / bump / env map)
// - No Glow Option
// - Outline and underlay rendered by Base shader

Shader "UniText/Mobile/SDF-Face" {

Properties {
	_FaceColor          ("Face Color", Color) = (1,1,1,1)
	_FaceDilate			("Face Dilate", Range(-1,1)) = 0

	_WeightNormal		("Weight Normal", float) = 0
	_WeightBold			("Weight Bold", float) = 1

	_ShaderFlags		("Flags", float) = 0
	_ScaleRatioA		("Scale RatioA", float) = 1
	_ScaleRatioB		("Scale RatioB", float) = 1
	_ScaleRatioC		("Scale RatioC", float) = 1

	_MainTex			("Font Atlas", 2D) = "white" {}
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
		Name "FACE"
		CGPROGRAM
		#pragma vertex VertShader
		#pragma fragment PixShader

		#pragma multi_compile __ UNITY_UI_CLIP_RECT
		#pragma multi_compile __ UNITY_UI_ALPHACLIP

		#include "UniText_SDF-Mobile-Common.cginc"

		struct pixel_t
		{
			UNITY_VERTEX_INPUT_INSTANCE_ID
			UNITY_VERTEX_OUTPUT_STEREO
			float4 vertex    : SV_POSITION;
			fixed4 faceColor : COLOR;
			float2 uv        : TEXCOORD0;
			half2  param     : TEXCOORD1;  // scale, bias
			half4  mask      : TEXCOORD2;
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

			float2 scaleAndNorm = ComputeScaleAndNorm(vPosition, input.normal, vert, input.texcoord0, input.texcoord1);
			float scale = scaleAndNorm.x;
			float normFactor = scaleAndNorm.y;

			float baseWeight = ComputeBaseWeight(input.texcoord0);
			float bias = ComputeBias(baseWeight, _FaceDilate, scale, normFactor);

			fixed4 color = GammaToLinearIfNeeded(input.color);
			fixed4 faceColor = color * _FaceColor;
			faceColor.rgb *= faceColor.a;

			float2 pixelSize = vPosition.w;
			pixelSize /= float2(_ScaleX, _ScaleY) * abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));

			output.vertex = vPosition;
			output.faceColor = faceColor;
			output.uv = input.texcoord0.xy;
			output.param = half2(scale, bias);
			output.mask = ComputeMask(vert, pixelSize);

			return output;
		}

		fixed4 PixShader(pixel_t input) : SV_Target
		{
			UNITY_SETUP_INSTANCE_ID(input);

			half d = tex2D(_MainTex, input.uv).a * input.param.x;
			half4 result = SDFLayer(d, input.param.y, input.faceColor);

			return ApplyClipping(result, input.mask);
		}
		ENDCG
	}
}

CustomEditor "LightSide.UniText_SDFShaderGUI"
}
