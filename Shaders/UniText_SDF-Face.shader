// Face pass for 2-pass SDF rendering (rendered second, on top of Base)
// Renders the inner face area with bevel effects

Shader "UniText/SDF-Face" {

Properties {
	_FaceTex			("Face Texture", 2D) = "white" {}
	_FaceUVSpeedX		("Face UV Speed X", Range(-5, 5)) = 0.0
	_FaceUVSpeedY		("Face UV Speed Y", Range(-5, 5)) = 0.0
	_FaceColor		    ("Face Color", Color) = (1,1,1,1)
	_FaceDilate			("Face Dilate", Range(-1,1)) = 0

	_OutlineWidth		("Outline Thickness", Range(0, 1)) = 0

	_Bevel				("Bevel", Range(0,1)) = 0.5
	_BevelOffset		("Bevel Offset", Range(-0.5,0.5)) = 0
	_BevelWidth			("Bevel Width", Range(-.5,0.5)) = 0
	_BevelClamp			("Bevel Clamp", Range(0,1)) = 0
	_BevelRoundness		("Bevel Roundness", Range(0,1)) = 0

	_LightAngle			("Light Angle", Range(0.0, 6.2831853)) = 3.1416
	_SpecularColor	    ("Specular", Color) = (1,1,1,1)
	_SpecularPower		("Specular", Range(0,4)) = 2.0
	_Reflectivity		("Reflectivity", Range(5.0,15.0)) = 10
	_Diffuse			("Diffuse", Range(0,1)) = 0.5
	_Ambient			("Ambient", Range(1,0)) = 0.5

	_BumpMap 			("Normal map", 2D) = "bump" {}
	_BumpOutline		("Bump Outline", Range(0,1)) = 0
	_BumpFace			("Bump Face", Range(0,1)) = 0

	_ReflectFaceColor	("Reflection Color", Color) = (0,0,0,1)
	_ReflectOutlineColor("Reflection Color", Color) = (0,0,0,1)
	_Cube 				("Reflection Cubemap", Cube) = "black" { /* TexGen CubeReflect */ }
	_EnvMatrixRotation	("Texture Rotation", vector) = (0, 0, 0, 0)

	_WeightNormal		("Weight Normal", float) = 0
	_WeightBold			("Weight Bold", float) = 1

	_ShaderFlags		("Flags", float) = 0
	_ScaleRatioA		("Scale RatioA", float) = 1
	_ScaleRatioB		("Scale RatioB", float) = 1
	_ScaleRatioC		("Scale RatioC", float) = 1

	_MainTex			("Font Atlas", 2D) = "white" {}
	_ScaleX				("Scale X", float) = 1.0
	_ScaleY				("Scale Y", float) = 1.0
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
		#pragma target 3.0
		#pragma vertex VertShader
		#pragma fragment PixShader
		#pragma shader_feature __ BEVEL_ON

		#pragma multi_compile __ UNITY_UI_CLIP_RECT
		#pragma multi_compile __ UNITY_UI_ALPHACLIP

		#include "UnityCG.cginc"
		#include "UnityUI.cginc"
		#include "UniText_Properties.cginc"
		#include "UniText.cginc"

		float4 _MainTex_TexelSize; // Unity auto-provided: (1/width, 1/height, width, height)

		struct vertex_t
		{
			UNITY_VERTEX_INPUT_INSTANCE_ID
			float4	position		: POSITION;
			float3	normal			: NORMAL;
			fixed4	color			: COLOR;
			float4	texcoord0		: TEXCOORD0;  // xy = UV, z = gradientScale, w = xScaleVal (signed for bold)
			float4	texcoord1		: TEXCOORD1;  // x = spreadRatio (Padding / PointSize)
		};

		struct pixel_t
		{
			UNITY_VERTEX_INPUT_INSTANCE_ID
			UNITY_VERTEX_OUTPUT_STEREO
			float4	position		: SV_POSITION;
			fixed4	color			: COLOR;
			float4	atlas			: TEXCOORD0;		// xy = UV, z = gradientScale, w = normFactor
			float4	param			: TEXCOORD1;		// scale, bias, weight, unused
			float4	mask			: TEXCOORD2;
			float3	viewDir			: TEXCOORD3;
			float2	faceUV			: TEXCOORD4;
		};

		float4 _FaceTex_ST;
		float _UIMaskSoftnessX;
		float _UIMaskSoftnessY;
		int _UIVertexColorAlwaysGammaSpace;

		pixel_t VertShader(vertex_t input)
		{
			pixel_t output;

			UNITY_INITIALIZE_OUTPUT(pixel_t, output);
			UNITY_SETUP_INSTANCE_ID(input);
			UNITY_TRANSFER_INSTANCE_ID(input,output);
			UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

			float bold = step(input.texcoord0.w, 0);

			float4 vert = input.position;
			vert.x += _VertexOffsetX;
			vert.y += _VertexOffsetY;

			float4 vPosition = UnityObjectToClipPos(vert);

			float2 pixelSize = vPosition.w;
			pixelSize /= float2(_ScaleX, _ScaleY) * abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));
			float baseScale = rsqrt(dot(pixelSize, pixelSize)) * (_Sharpness + 1);
			if (UNITY_MATRIX_P[3][3] == 0) baseScale = lerp(abs(baseScale) * (1 - _PerspectiveFilter), baseScale, abs(dot(UnityObjectToWorldNormal(input.normal.xyz), normalize(WorldSpaceViewDir(vert)))));

			float xScaleVal = abs(input.texcoord0.w);
			float gradientScale = input.texcoord0.z;
			float spreadRatio = input.texcoord1.x;

			// SDF scale (includes xScaleVal and gradientScale for proper edge rendering)
			float scale = baseScale * xScaleVal * gradientScale;

			// Normalization factor for effects (independent of atlas settings)
			float normFactor = REFERENCE_SPREAD_RATIO / max(spreadRatio, 0.001);

			// Base weight (font weight only, without dilate)
			float baseWeight = lerp(_WeightNormal, _WeightBold, bold) / 4.0 * _ScaleRatioA * 0.5;

			// Face bias with dilate applied via normFactor (independent of atlas settings)
			float normalizedEffect = (baseWeight + _FaceDilate * _ScaleRatioA * 0.5) * normFactor;
			float bias = (0.5 - normalizedEffect) * scale - 0.5;

			// Generate UV for the Masking Texture
			float4 clampedRect = clamp(_ClipRect, -2e10, 2e10);

			// Support for texture tiling and offset
			float2 faceUV = TRANSFORM_TEX(input.texcoord1.yz, _FaceTex);

			if (_UIVertexColorAlwaysGammaSpace && !IsGammaSpace())
			{
				input.color.rgb = UIGammaToLinear(input.color.rgb);
			}

			output.position = vPosition;
			output.color = input.color;
			output.atlas = float4(input.texcoord0.xy, gradientScale, normFactor);
			output.param = float4(scale, bias, baseWeight, 0);
			output.faceUV = faceUV;
			const half2 maskSoftness = half2(max(_UIMaskSoftnessX, _MaskSoftnessX), max(_UIMaskSoftnessY, _MaskSoftnessY));
			output.mask = half4(vert.xy * 2 - clampedRect.xy - clampedRect.zw, 0.25 / (0.25 * maskSoftness + pixelSize.xy));
			output.viewDir = mul((float3x3)_EnvMatrix, _WorldSpaceCameraPos.xyz - mul(unity_ObjectToWorld, vert).xyz);

			return output;
		}


		fixed4 PixShader(pixel_t input) : SV_Target
		{
			UNITY_SETUP_INSTANCE_ID(input);

			half d = tex2D(_MainTex, input.atlas.xy).a * input.param.x;  // * scale

			float scale = input.param.x;
			float bias = input.param.y;
			float baseWeight = input.param.z;
			float normFactor = input.atlas.w;

			// Face layer
			half4 faceColor = _FaceColor;
			faceColor.rgb *= input.color.rgb;
			faceColor *= tex2D(_FaceTex, input.faceUV + float2(_FaceUVSpeedX, _FaceUVSpeedY) * _Time.y);
			faceColor.rgb *= faceColor.a;

			half4 result = SDFLayer(d, bias, faceColor);

			#if BEVEL_ON
			// Calculate sd for bevel (using normFactor for independence from atlas settings)
			float normalizedFaceEffect = (baseWeight + _FaceDilate * _ScaleRatioA * 0.5) * normFactor;
			float sd = (0.5 - normalizedFaceEffect + 0.5 / scale - tex2D(_MainTex, input.atlas.xy).a) * scale;
			float outlineRange = _OutlineWidth * _ScaleRatioA * 0.5 * normFactor * scale;

			float3 dxy = float3(0.5 * _MainTex_TexelSize.x, 0.5 * _MainTex_TexelSize.y, 0);
			float3 n = GetSurfaceNormal(input.atlas.xy, baseWeight, dxy, input.atlas.z);

			float3 bump = UnpackNormal(tex2D(_BumpMap, input.faceUV + float2(_FaceUVSpeedX, _FaceUVSpeedY) * _Time.y)).xyz;
			bump *= lerp(_BumpFace, _BumpOutline, saturate(sd + outlineRange));
			n = normalize(n - bump);

			float3 light = normalize(float3(sin(_LightAngle), cos(_LightAngle), -1.0));

			float3 col = GetSpecular(n, light);
			result.rgb += col * result.a;
			result.rgb *= 1 - (dot(n, light) * _Diffuse);
			result.rgb *= lerp(_Ambient, 1, n.z * n.z);

			fixed4 reflcol = texCUBE(_Cube, reflect(input.viewDir, -n));
			result.rgb += reflcol.rgb * _ReflectFaceColor.rgb * result.a;
			#endif

			// Apply vertex alpha
			result *= input.color.a;

			// Alternative implementation to UnityGet2DClipping with support for softness.
			#if UNITY_UI_CLIP_RECT
			half2 m = saturate((_ClipRect.zw - _ClipRect.xy - abs(input.mask.xy)) * input.mask.zw);
			result *= m.x * m.y;
			#endif

			#if UNITY_UI_ALPHACLIP
			clip(result.a - 0.001);
			#endif

			return result;
		}
		ENDCG
	}
}

CustomEditor "LightSide.UniText_SDFShaderGUI"
}
