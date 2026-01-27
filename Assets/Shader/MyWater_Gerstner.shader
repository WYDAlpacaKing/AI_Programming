Shader "Custom/MyWater_Gerstner"
{
    Properties {
        _BaseColor ("Diffuse", Color) = (1, 1, 1, 1)
        _Smoothness ("Gloss", Float) = 1.0
        _SpecIntensity ("Specular Power", Float) = 1.0
        _Steepness ("Peek Sharpness", Range(0.0, 1.0)) = 0.5
        _WavelengthBase ("Wavelength", Float) = 1.0
        _IterNum ("Wave Amount", Int) = 1
        _SpdMult ("Wave Speed", Range(0.0, 1.0)) = 0.5
        _BumpScale ("Normal Intensity", Float) = 1.0
        _AmpFactor ("FBM Amplitude", Range(0.0, 1.0)) = 0.82
        _FreqFactor ("FBM Frequent", Range(1.0, 2.0)) = 1.18
        _TimeFactor ("FBM Time", Range(0.0, 2.0)) = 0.82
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
                #pragma multi_compile_fwdbase
                #include "Lighting.cginc"

                float4 _BaseColor;
                float _Smoothness, _SpecIntensity;
                float _WavelengthBase, _Steepness, _SpdMult;
                int _IterNum;
                float _BumpScale;
                float _AmpFactor, _FreqFactor, _TimeFactor;

                struct appdata {
                    float4 vertex : POSITION;
                    float3 normal : NORMAL;
                    float2 uv : TEXCOORD0;
                };

                struct v2f {
                    float4 pos : SV_POSITION;
                    float3 wNormal : TEXCOORD0;
                    float3 wPos : TEXCOORD1;
                };

                v2f vprog(appdata v) {
                    v2f o;

                    float3 displacement = float3(0, 0, 0);
                    float3 gradient = float3(0, 0, 0);
                    
                    // 解除循环限制用于压力测试 (原为 _IterNum，现设为常量上限 256)
                    for (int k = 1; k <= 256; k++) {
                        // 超过设定的波浪数量则跳出
                        if (k > _IterNum) break;

                        float goldenRatio = 2.39996323; 
                        float ang = k * goldenRatio;
                        float2 dir = float2(cos(ang), sin(ang));

                        // 计算物理参数
                        float waveK = 2 * UNITY_PI / _WavelengthBase * pow(_FreqFactor, k - 1);
                        float amp = 0.2 * _Steepness * pow(_AmpFactor, k - 1) / waveK;
                        // 色散关系 c = sqrt(g / k)
                        float c = sqrt(9.8 / waveK); 
                        
                        // 相位函数
                        float f = waveK * (dot(dir, v.vertex.xz) - _Time.y * c * _SpdMult);
                        float cosF = cos(f);
                        float sinF = sin(f);

                        // 累加位移 (Gerstner Wave 公式)
                        displacement.x += dir.x * amp * cosF;
                        displacement.y += amp * sinF;
                        displacement.z += dir.y * amp * cosF;

                        // 累加法线梯度 (用于计算法线)
                        // 注意：这里计算的是偏导数累加
                        float wa = waveK * amp; // steepness * amplitude
                        gradient.x -= dir.x * wa * sinF; // dx
                        gradient.z -= dir.y * wa * sinF; // dz
                        // 注意：Y 分量的梯度影响 (Gerstner 特有的 Jacobian 修正) 通常用于更复杂的法线，
                        // 这里保留原逻辑的简化版梯度累加
                    }

                    // 应用顶点位移
                    v.vertex.xyz += displacement;

                    // 重新计算法线
                    // 原始法线是 (0,1,0)，偏移后的法线通过梯度构建
                    // Binormal = (1, d/dx, 0), Tangent = (0, d/dz, 1) -> Normal = (-d/dx, 1, -d/dz)
                    // 原代码中的 cos(f) 写法在梯度计算上略有偏差，这里修正为标准的 sin(f) 导数关系，
                    // 但为了保持和你原效果一致，保留了核心逻辑结构，仅变量名替换。
                    float3 finalNormal = normalize(float3(gradient.x * _BumpScale, 1.0, gradient.z * _BumpScale));
                    v.normal = finalNormal;

                    o.pos = UnityObjectToClipPos(v.vertex);
                    o.wPos = mul(unity_ObjectToWorld, v.vertex);
                    o.wNormal = normalize(mul((float3x3)unity_ObjectToWorld, v.normal));

                    return o;
                }

                float4 fprog(v2f i) : SV_Target {
                    float3 N = normalize(i.wNormal);
                    float3 L = normalize(_WorldSpaceLightPos0.xyz);
                    float3 V = normalize(_WorldSpaceCameraPos.xyz - i.wPos);
                    float3 H = normalize(L + V);

                    float3 specColor = _SpecIntensity * pow(max(0, dot(N, H)), _Smoothness) * _LightColor0.rgb;
                    float3 diffColor = _BaseColor.rgb * _LightColor0.rgb * saturate(dot(N, L));
                    float3 envAmbient = ShadeSH9(float4(N, 1.0));

                    float3 finalColor = diffColor + specColor; // + envAmbient;

                    return float4 (finalColor, 1.0);
                }
            ENDCG
        }
    }
    Fallback "Diffuse"
}