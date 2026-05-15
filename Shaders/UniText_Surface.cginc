// Used by Unity internally to handle Texture Tiling and Offset.
float4 _FaceTex_ST;
float4 _OutlineTex_ST;

void VertShader(inout appdata_full v, out Input data)
{
	v.vertex.x += _VertexOffsetX;
	v.vertex.y += _VertexOffsetY;

	UNITY_INITIALIZE_OUTPUT(Input, data);

	float bold = step(v.texcoord.w, 0);

	// Generate normal for backface
	float3 view = ObjSpaceViewDir(v.vertex);
	v.normal *= sign(dot(v.normal, view));

	float gradientScale = v.texcoord.z;
	float xScaleVal = abs(v.texcoord.w);
	float spreadRatio = v.texcoord1.x;

	// Texture UVs from texcoord1.yz (mesh generator puts spreadRatio in .x)
	float2 textureUV = v.texcoord1.yz;
	data.faceTexUV = TRANSFORM_TEX(textureUV, _FaceTex);
	data.outlineTexUV = TRANSFORM_TEX(textureUV, _OutlineTex);

#if USE_DERIVATIVE
	// Pass gradientScale and xScaleVal for pixel shader SSD calculation
	data.param.y = gradientScale;
	data.param.z = xScaleVal;
#else
	float4 vert = v.vertex;
	float4 vPosition = UnityObjectToClipPos(vert);
	float2 pixelSize = vPosition.w;

	pixelSize /= float2(_ScaleX, _ScaleY) * mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy);
	float baseScale = rsqrt(dot(pixelSize, pixelSize)) * (_Sharpness + 1);
	baseScale = lerp(baseScale * (1 - _PerspectiveFilter), baseScale, abs(dot(UnityObjectToWorldNormal(v.normal.xyz), normalize(WorldSpaceViewDir(vert)))));
	// SDF scale (includes xScaleVal and gradientScale)
	data.param.y = baseScale * xScaleVal * gradientScale;
	// xScaleVal for pixel shader (not used in non-derivative path, but needed for consistency)
	data.param.z = xScaleVal;
#endif

	// Base weight (font weight only, without dilate)
	data.param.x = lerp(_WeightNormal, _WeightBold, bold) / 4.0 * _ScaleRatioA * 0.5;
	// Pass spreadRatio for normFactor calculation in pixel shader
	data.param.w = spreadRatio;
	data.viewDirEnv = mul((float3x3)_EnvMatrix, WorldSpaceViewDir(v.vertex));
}

void PixShader(Input input, inout SurfaceOutput o)
{
	float baseWeight = input.param.x;
	float spreadRatio = input.param.w;

	// Normalization factor for effects (independent of atlas settings)
	float normFactor = REFERENCE_SPREAD_RATIO / max(spreadRatio, 0.001);

#if USE_DERIVATIVE
	float2 pixelSize = float2(ddx(input.uv_MainTex.y), ddy(input.uv_MainTex.y));
	pixelSize *= _MainTex_TexelSize.w * 0.75;  // textureHeight from Unity auto-provided
	float baseScale = rsqrt(dot(pixelSize, pixelSize)) * (_Sharpness + 1);
	float gradientScale = input.param.y;
	float xScaleVal = input.param.z;
	float scale = baseScale * xScaleVal * gradientScale;
#else
	float scale = input.param.y;
#endif

	// Sample SDF
	half d = tex2D(_MainTex, input.uv_MainTex).a * scale;

	// Face bias with dilate applied via normFactor (independent of atlas settings)
	float normalizedFaceEffect = (baseWeight + _FaceDilate * _ScaleRatioA * 0.5) * normFactor;
	float bias = (0.5 - normalizedFaceEffect) * scale - 0.5;

	// Outline parameters (width applied via normFactor)
	float normalizedOutlineEffect = (baseWeight + (_FaceDilate + _OutlineWidth) * _ScaleRatioA * 0.5) * normFactor;
	float outlineBias = (0.5 - normalizedOutlineEffect) * scale - 0.5;

	// Face color with texture (using manual texture UVs)
	float4 faceColor = _FaceColor;
	faceColor *= input.color;
	faceColor *= tex2D(_FaceTex, input.faceTexUV + float2(_FaceUVSpeedX, _FaceUVSpeedY) * _Time.y);
	faceColor.rgb *= faceColor.a;

	// Outline color with texture (using manual texture UVs)
	float4 outlineColor = _OutlineColor;
	outlineColor.a *= input.color.a;
	outlineColor *= tex2D(_OutlineTex, input.outlineTexUV + float2(_OutlineUVSpeedX, _OutlineUVSpeedY) * _Time.y);
	outlineColor.rgb *= outlineColor.a;

	// Render layers using unified SDFLayer approach
	half4 result = half4(0, 0, 0, 0);

	// Outline layer
	half4 outlineResult = SDFLayer(d, outlineBias, outlineColor);
	result = BlendOver(result, outlineResult);

	// Face layer
	half4 faceResult = SDFLayer(d, bias, faceColor);
	result = BlendOver(result, faceResult);

	// Calculate sd for bevel/glow (using normFactor for independence from atlas settings)
	float sd = (0.5 - normalizedFaceEffect + 0.5 / scale - tex2D(_MainTex, input.uv_MainTex).a) * scale;
	float outlineRange = _OutlineWidth * _ScaleRatioA * 0.5 * normFactor * scale;

	// Convert from premultiplied alpha for surface shader output
	result.rgb /= max(result.a, 0.0001);

#if BEVEL_ON
	// _MainTex_TexelSize.xy = (1/width, 1/height)
	float3 delta = float3(_MainTex_TexelSize.x, _MainTex_TexelSize.y, 0.0);

	float4 smp4x = {tex2D(_MainTex, input.uv_MainTex - delta.xz).a,
					tex2D(_MainTex, input.uv_MainTex + delta.xz).a,
					tex2D(_MainTex, input.uv_MainTex - delta.zy).a,
					tex2D(_MainTex, input.uv_MainTex + delta.zy).a };

#if USE_DERIVATIVE
	// Face Normal using gradientScale
	float3 n = GetSurfaceNormal(smp4x, baseWeight, gradientScale);
#else
	// Face Normal (use fixed gradientScale approximation for non-derivative path)
	float3 n = GetSurfaceNormal(smp4x, baseWeight, 20.0);
#endif

	// Bumpmap (using manual texture UVs)
	float3 bump = UnpackNormal(tex2D(_BumpMap, input.faceTexUV)).xyz;
	bump *= lerp(_BumpFace, _BumpOutline, saturate(sd + outlineRange));
	bump = lerp(float3(0, 0, 1), bump, result.a);
	n = normalize(n - bump);

	// Cubemap reflection
	fixed4 reflcol = texCUBE(_Cube, reflect(input.viewDirEnv, mul((float3x3)unity_ObjectToWorld, n)));
	float3 emission = reflcol.rgb * lerp(_ReflectFaceColor.rgb, _ReflectOutlineColor.rgb, saturate(sd + outlineRange)) * result.a;
#else
	float3 n = float3(0, 0, -1);
	float3 emission = float3(0, 0, 0);
#endif

#if GLOW_ON
	float4 glowColor = GetGlowColor(sd, normFactor * scale);
	glowColor.a *= input.color.a;
	emission += glowColor.rgb * glowColor.a;
	// Blend glow
	result.rgb = result.rgb * (1 - glowColor.a) + glowColor.rgb;
	result.a = saturate(result.a + glowColor.a);
	result.rgb /= max(result.a, 0.0001);
#endif

	// Set Standard output structure
	o.Albedo = result.rgb;
	o.Normal = -n;
	o.Emission = emission;
	o.Specular = lerp(_FaceShininess, _OutlineShininess, saturate(sd + outlineRange));
	o.Gloss = 1;
	o.Alpha = result.a;
}
