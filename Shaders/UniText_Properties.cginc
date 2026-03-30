#ifndef UNITEXT_PROPERTIES_INCLUDED
#define UNITEXT_PROPERTIES_INCLUDED

// Face texture (optional overlay)
uniform sampler2D	_FaceTex;
uniform float		_FaceUVSpeedX;
uniform float		_FaceUVSpeedY;

// Outline texture (optional overlay)
uniform sampler2D	_OutlineTex;
uniform float		_OutlineUVSpeedX;
uniform float		_OutlineUVSpeedY;

// Bevel / shading (desktop only, stays in material)
uniform float		_Bevel;
uniform float		_BevelOffset;
uniform float		_BevelWidth;
uniform float		_BevelClamp;
uniform float		_BevelRoundness;

uniform sampler2D	_BumpMap;
uniform float		_BumpOutline;
uniform float		_BumpFace;

uniform samplerCUBE	_Cube;
uniform fixed4 		_ReflectFaceColor;
uniform fixed4		_ReflectOutlineColor;
uniform float3      _EnvMatrixRotation;
uniform float4x4	_EnvMatrix;

uniform fixed4		_SpecularColor;
uniform float		_LightAngle;
uniform float		_SpecularPower;
uniform float		_Reflectivity;
uniform float		_Diffuse;
uniform float		_Ambient;

// Glow (desktop only, stays in material)
uniform fixed4 		_GlowColor;
uniform float 		_GlowOffset;
uniform float 		_GlowOuter;
uniform float 		_GlowInner;
uniform float 		_GlowPower;

// API-editable properties (set by code, not user)
uniform float 		_ShaderFlags;
uniform float		_WeightNormal;
uniform float		_WeightBold;

uniform float		_ScaleRatioA;
uniform float		_ScaleRatioB;
uniform float		_ScaleRatioC;

uniform float		_VertexOffsetX;
uniform float		_VertexOffsetY;

// Masking / clipping
uniform float		_MaskID;
uniform sampler2D	_MaskTex;
uniform float4		_MaskCoord;
uniform float4		_ClipRect;
uniform float		_MaskSoftnessX;
uniform float		_MaskSoftnessY;
uniform float		_MaskInverse;
uniform fixed4		_MaskEdgeColor;
uniform float		_MaskEdgeSoftness;
uniform float		_MaskWipeControl;

// Font Atlas (Texture2DArray — one slice per atlas page)
UNITY_DECLARE_TEX2DARRAY(_MainTex);
uniform float		_ScaleX;
uniform float		_ScaleY;
uniform float		_PerspectiveFilter;
uniform float		_Sharpness;

#endif // UNITEXT_PROPERTIES_INCLUDED
