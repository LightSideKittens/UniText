// Simplified SDF shader (single-pass):
// - No Shading Option (bevel / bump / env map)
// - No Glow Option
// - Renders Face, Outline, and Underlay in one pass

Shader "UniText/Mobile/SDF" {

Properties {
	_FaceColor          ("Face Color", Color) = (1,1,1,1)
	_FaceDilate			("Face Dilate", Range(-1,1)) = 0

	_OutlineColor	    ("Outline Color", Color) = (0,0,0,1)
	_OutlineDilate		("Outline Dilate", Range(-1,1)) = 0
	_OutlineSoftness	("Outline Softness", Range(0,1)) = 0

	_UnderlayColor	    ("Border Color", Color) = (0,0,0,.5)
	_UnderlayOffsetX 	("Border OffsetX", Range(-1,1)) = 0
	_UnderlayOffsetY 	("Border OffsetY", Range(-1,1)) = 0
	_UnderlayDilate		("Border Dilate", Range(-1,1)) = 0
	_UnderlaySoftness 	("Border Softness", Range(0,1)) = 0

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
		CGPROGRAM
		#pragma vertex VertShader
		#pragma fragment PixShader
		#pragma shader_feature __ OUTLINE_ON
		#pragma shader_feature __ UNDERLAY_ON UNDERLAY_INNER

		#pragma multi_compile __ UNITY_UI_CLIP_RECT
		#pragma multi_compile __ UNITY_UI_ALPHACLIP

		#include "UniText_SDF-Mobile-Common.cginc"

		// Unity auto-generated: (1/width, 1/height, width, height)
		float4 _MainTex_TexelSize;

		struct pixel_t
		{
			UNITY_VERTEX_INPUT_INSTANCE_ID
			UNITY_VERTEX_OUTPUT_STEREO
			float4 vertex       : SV_POSITION;
			fixed4 faceColor    : COLOR;
			fixed4 outlineColor : COLOR1;
			float2 uv           : TEXCOORD0;
			half4  param        : TEXCOORD1;  // scale, faceBias, outlineBias, alpha
			half4  mask         : TEXCOORD2;
			#if (UNDERLAY_ON | UNDERLAY_INNER)
			float2 underlayUV   : TEXCOORD3;
			half2  underlayParam: TEXCOORD4;  // scale, bias
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

			float2 pixelSize = vPosition.w;
			pixelSize /= float2(_ScaleX, _ScaleY) * abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));

			// Get scale (for AA) and normFactor (for effect normalization)
			float2 scaleAndNorm = ComputeScaleAndNorm(vPosition, input.normal, vert, input.texcoord0, input.texcoord1);
			float scale = scaleAndNorm.x;
			float normFactor = scaleAndNorm.y;

			float baseWeight = ComputeBaseWeight(input.texcoord0);

			// Face bias (effects normalized via normFactor for independence from atlas settings)
			float faceBias = ComputeBias(baseWeight, _FaceDilate, scale, normFactor);

			// Outline bias (same logic, different dilate)
			float outlineBias = ComputeBias(baseWeight, _FaceDilate + _OutlineDilate, scale, normFactor);

			fixed4 color = GammaToLinearIfNeeded(input.color);
			float opacity = color.a;
			#if (UNDERLAY_ON | UNDERLAY_INNER)
			opacity = 1.0;
			#endif

			fixed4 faceColor = fixed4(color.rgb, opacity) * _FaceColor;
			faceColor.rgb *= faceColor.a;

			fixed4 outlineColor = _OutlineColor;
			outlineColor.a *= opacity;
			outlineColor.rgb *= outlineColor.a;

			#if (UNDERLAY_ON | UNDERLAY_INNER)
			// Underlay parameters (effects normalized via normFactor)
			float softnessFactor = _UnderlaySoftness * _ScaleRatioC * normFactor;
			float layerScale = scale / (1 + softnessFactor);
			float underlayDilate = _FaceDilate * _ScaleRatioA + _UnderlayDilate * _ScaleRatioC;
			float layerBias = ComputeBias(baseWeight, underlayDilate, layerScale, normFactor);

			// Underlay UV offset (independent of atlas settings)
			// Uses PointSize-proportional factor instead of spreadRatio-dependent
			float gradientScaleVal = input.texcoord0.z;
			float offsetFactor = ComputeUnderlayOffsetFactor(gradientScaleVal, normFactor);
			float2 layerOffset = float2(
				-(_UnderlayOffsetX * _ScaleRatioC) * offsetFactor * _MainTex_TexelSize.x,
				-(_UnderlayOffsetY * _ScaleRatioC) * offsetFactor * _MainTex_TexelSize.y
			);

			output.underlayUV = input.texcoord0.xy + layerOffset;
			output.underlayParam = half2(layerScale, layerBias);
			#endif

			output.vertex = vPosition;
			output.faceColor = faceColor;
			output.outlineColor = outlineColor;
			output.uv = input.texcoord0.xy;
			output.param = half4(scale, faceBias, outlineBias, color.a);
			output.mask = ComputeMask(vert, pixelSize);

			return output;
		}

		fixed4 PixShader(pixel_t input) : SV_Target
		{
			UNITY_SETUP_INSTANCE_ID(input);

			half d = tex2D(_MainTex, input.uv).a * input.param.x;
			half4 result = half4(0, 0, 0, 0);

			// Underlay layer (behind everything)
			#if UNDERLAY_ON
			half ud = tex2D(_MainTex, input.underlayUV).a * input.underlayParam.x;
			half4 underlayColor = float4(_UnderlayColor.rgb * _UnderlayColor.a, _UnderlayColor.a);
			result = SDFLayer(ud, input.underlayParam.y, underlayColor);
			#endif

			#if UNDERLAY_INNER
			half ud = tex2D(_MainTex, input.underlayUV).a * input.underlayParam.x;
			half4 underlayColor = float4(_UnderlayColor.rgb * _UnderlayColor.a, _UnderlayColor.a);
			half faceMask = saturate(d - input.param.y);
			result = underlayColor * (1 - saturate(ud - input.underlayParam.y)) * faceMask;
			#endif

			// Outline layer
			#ifdef OUTLINE_ON
			half4 outlineResult = SDFLayer(d, input.param.z, input.outlineColor);
			result = BlendOver(result, outlineResult);
			#endif

			// Face layer (on top)
			half4 faceResult = SDFLayer(d, input.param.y, input.faceColor);
			result = BlendOver(result, faceResult);

			// Apply vertex alpha for underlay
			#if (UNDERLAY_ON | UNDERLAY_INNER)
			result *= input.param.w;
			#endif

			return ApplyClipping(result, input.mask);
		}
		ENDCG
	}
}

CustomEditor "LightSide.UniText_SDFShaderGUI"
}
