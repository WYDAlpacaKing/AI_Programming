Shader "Custom/MyWater_FBM"
{
    Properties {
        _Albedo ("Diffuse", Color) = (1, 1, 1, 1)
        _ShininessExp ("Gloss", Float) = 1.0
        _SpecMult ("Specular Power", Float) = 1.0
        _AmpBase ("Wave Intensity", Float) = 1.0
        _FreqBase ("Wave Freqency", Float) = 1.0
        _SpdBase ("Wave Speed", Float) = 1.0
        _Octaves ("Wave Amount", Int) = 1
        _HeightScale ("Peek Sharpness", Float) = 1.0
        _BumpStrength ("Normal Intensity", Float) = 1.0
        _GainFactor ("FBM Intensity", Range(0.0, 1.0)) = 0.82
        _Lacunarity ("FBM Frequent", Range(1.0, 2.0)) = 1.18
        _SpdFactor ("FBM Speed", Range(0.0, 2.0)) = 1.2
    }

    SubShader {
        Pass {
            Tags { "LightMode" = "ForwardBase" }
            Tags { "RenderType" = "Opaque" }
            LOD 200

            CGPROGRAM
                #pragma vertex vprog
                #pragma fragment fprog
                #pragma target 3.0
                #include "Lighting.cginc"

                float4 _Albedo;
                float _ShininessExp, _SpecMult;
                float _AmpBase, _FreqBase, _SpdBase;
                int _Octaves;
                float _HeightScale, _BumpStrength;
                float _GainFactor, _Lacunarity, _SpdFactor;

                float hash(float2 p){
                    return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453) * 360;
                }

                struct appdata{
                    float4 vertex : POSITION;
                    float3 normal : NORMAL;
                };

                struct interpolators{
                    float4 pos : SV_POSITION;
                    float3 wNormal : TEXCOORD0;
                    float3 wPos : TEXCOORD1;
                };

                interpolators vprog(appdata v) {
                    interpolators o;

                    float totalH = 0;
                    float curAmp = _AmpBase * 0.1;
                    float curFreq = _FreqBase;
                    float curSpd = _SpdBase;
                    float gradX = 0;
                    float gradZ = 0;
                    
                    float t = _Time.y * curSpd;
                    
                    for (int k = 1; k <= _Octaves; k++) {
                        float ang = radians(hash(float2(k * 0.2, k * 0.2)));
                        float ca = cos(ang);
                        float sa = sin(ang);

                        float rotPos = v.vertex.x * ca + v.vertex.z * sa;
                        
                        totalH += curAmp * sin(rotPos * curFreq + t);

                        float drv = curAmp * curFreq * cos(rotPos * curFreq + t);
                        gradX += drv * ca;
                        gradZ += drv * sa;

                        curAmp *= _GainFactor;
                        curFreq *= _Lacunarity;
                    }

                    v.normal = normalize(float3(-gradX * _BumpStrength, 1, -gradZ * _BumpStrength));
                    v.vertex.y += totalH * _HeightScale;

                    o.pos = UnityObjectToClipPos(v.vertex);
                    o.wPos = mul(unity_ObjectToWorld, v.vertex);
                    o.wNormal = normalize(mul((float3x3)unity_ObjectToWorld, v.normal));

                    return o;
                }

                float4 fprog(interpolators i) : SV_Target {
                    float3 N = normalize(i.wNormal);
                    float3 L = normalize(_WorldSpaceLightPos0.xyz);
                    float3 V = normalize(_WorldSpaceCameraPos.xyz - i.wPos);
                    float3 H = normalize(L + V);

                    float3 specColor = _SpecMult * pow(max(0, dot(N, H)), _ShininessExp) * _LightColor0.rgb;
                    float3 diffColor = _Albedo.rgb * _LightColor0.rgb * saturate(dot(N, L));
                    float3 envColor = ShadeSH9(float4(N, 1.0));

                    float3 finalColor = diffColor + specColor + envColor * 0.1;
                    return float4 (finalColor, 1.0);
                }
            ENDCG
        }
    }
    Fallback "Diffuse"
}