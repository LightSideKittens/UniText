#ifndef UNITEXT_INCLUDED
#define UNITEXT_INCLUDED

// ============================================
// Effect Normalization Constants
// ============================================

// Reference spreadRatio for effect normalization (Padding=9, PointSize=90 -> 9/90 = 0.1)
#define REFERENCE_SPREAD_RATIO 0.1

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

// ============================================
// Unified SDF Layer rendering functions
// ============================================

// Render a single SDF layer (same as Mobile version)
// d = sampled SDF value * scale
// threshold = bias value
// color = premultiplied alpha color
half4 SDFLayer(half d, float threshold, half4 color)
{
	return color * saturate(d - threshold);
}

// Blend layer on top of existing result (premultiplied alpha)
half4 BlendOver(half4 dst, half4 src)
{
	dst.rgb = dst.rgb * (1 - src.a) + src.rgb;
	dst.a = saturate(dst.a + src.a);
	return dst;
}


float3 GetSurfaceNormal(float4 h, float bias, float gradientScale)
{
	bool raisedBevel = step(1, fmod(_ShaderFlags, 2));

	h += bias+_BevelOffset;

	float bevelWidth = max(.01, _OutlineWidth+_BevelWidth);

  // Track outline
	h -= .5;
	h /= bevelWidth;
	h = saturate(h+.5);

	if(raisedBevel) h = 1 - abs(h*2.0 - 1.0);
	h = lerp(h, sin(h*3.141592/2.0), _BevelRoundness);
	h = min(h, 1.0-_BevelClamp);
	h *= _Bevel * bevelWidth * gradientScale * -2.0;

	float3 va = normalize(float3(1.0, 0.0, h.y - h.x));
	float3 vb = normalize(float3(0.0, -1.0, h.w - h.z));

	return cross(va, vb);
}

float3 GetSurfaceNormal(float2 uv, float bias, float3 delta, float gradientScale)
{
	// Read "height field"
  float4 h = {tex2D(_MainTex, uv - delta.xz).a,
				tex2D(_MainTex, uv + delta.xz).a,
				tex2D(_MainTex, uv - delta.zy).a,
				tex2D(_MainTex, uv + delta.zy).a};

	return GetSurfaceNormal(h, bias, gradientScale);
}

float3 GetSpecular(float3 n, float3 l)
{
	float spec = pow(max(0.0, dot(n, l)), _Reflectivity);
	return _SpecularColor.rgb * spec * _SpecularPower;
}

float4 GetGlowColor(float d, float effectScale)
{
	float glow = d - (_GlowOffset*_ScaleRatioB) * 0.5 * effectScale;
	float t = lerp(_GlowInner, (_GlowOuter * _ScaleRatioB), step(0.0, glow)) * 0.5 * effectScale;
	glow = saturate(abs(glow/(1.0 + t)));
	glow = 1.0-pow(glow, _GlowPower);
	glow *= sqrt(min(1.0, t)); // Fade off glow thinner than 1 screen pixel
	return float4(_GlowColor.rgb, saturate(_GlowColor.a * glow * 2));
}

#endif // UNITEXT_INCLUDED
