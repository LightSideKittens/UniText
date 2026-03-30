Shader "Hidden/UniText/AtlasPreview"
{
    Properties
    {
        _MainTex ("", 2DArray) = "" {}
        _SliceIndex ("Slice", Float) = 0
        _Mode ("Mode", Float) = 0
        _Rendered ("Rendered", Float) = 0
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"

            UNITY_DECLARE_TEX2DARRAY(_MainTex);
            float _SliceIndex;
            float _Mode;
            float _Rendered;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float median(float r, float g, float b)
            {
                return max(min(r, g), min(max(r, g), b));
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float4 val = UNITY_SAMPLE_TEX2DARRAY(_MainTex, float3(i.uv, _SliceIndex));

                if (_Mode < 0.5)
                {
                    // SDF
                    if (_Rendered > 0.5)
                    {
                        float d = val.r;
                        float w = fwidth(d) * 0.5;
                        float a = smoothstep(0.5 - w, 0.5 + w, d);
                        return fixed4(1 - a, 1 - a, 1 - a, 1);
                    }
                    return fixed4(1 - val.r, 1 - val.r, 1 - val.r, 1);
                }
                else if (_Mode < 1.5)
                {
                    // MSDF
                    if (_Rendered > 0.5)
                    {
                        float d = median(val.r, val.g, val.b);
                        float w = fwidth(d) * 0.5;
                        float a = smoothstep(0.5 - w, 0.5 + w, d);
                        return fixed4(1 - a, 1 - a, 1 - a, 1);
                    }
                    return fixed4(1 - val.r, 1 - val.g, 1 - val.b, 1);
                }
                else
                {
                    // Emoji
                    float3 bg = float3(0.22, 0.22, 0.22);
                    float3 srgb = pow(max(val.rgb, 0), 0.4545);
                    float3 c = lerp(bg, srgb, val.a);
                    return fixed4(c, 1);
                }
            }
            ENDCG
        }
    }
}