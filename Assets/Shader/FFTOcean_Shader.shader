Shader "MasterProject/FFTOcean_Shader"
{
    // Tessellation-related calculations
    CGINCLUDE
    // #define _EdgeLength 10
    int _EdgeLength;

    struct TessFactors {
        float edgeFactors[3] : SV_TESSFACTOR;
        float insideFactor : SV_INSIDETESSFACTOR;
    };
    
    // Tessellation Heuristic
    float CalculateTessellationFactor(float3 p1, float3 p2) {
        float edgeLen = distance(p1, p2);
        float3 center = (p1 + p2) * 0.5;
        float distToCam = distance(center, _WorldSpaceCameraPos);

        return edgeLen * _ScreenParams.y / (_EdgeLength * pow(distToCam * 0.5f, 1.2f));
    }

    bool IsTriangleBelowClipPlane(float3 v0, float3 v1, float3 v2, int planeIdx, float bias) {
        float4 plane = unity_CameraWorldClipPlanes[planeIdx];

        return dot(float4(v0, 1), plane) < bias && dot(float4(v1, 1), plane) < bias && dot(float4(v2, 1), plane) < bias;
    }

    bool ShouldCullTriangle(float3 v0, float3 v1, float3 v2, float bias) {
        return IsTriangleBelowClipPlane(v0, v1, v2, 0, bias) ||
               IsTriangleBelowClipPlane(v0, v1, v2, 1, bias) ||
               IsTriangleBelowClipPlane(v0, v1, v2, 2, bias) ||
               IsTriangleBelowClipPlane(v0, v1, v2, 3, bias);
    }

    ENDCG

    Properties
    {

    }
    SubShader
    {
        // Base shading pass
        pass {
            Tags { "LightMode" = "ForwardBase" }
            Tags { "RenderType"="Opaque" }
            LOD 200

            CGPROGRAM
            #pragma target 5.0
            #pragma multi_compile_fwdbase

            #include "UnityPBSLighting.cginc"
            #include "AutoLight.cginc"

            #pragma vertex VertexProgram
            #pragma hull HullProgram
            #pragma domain DomainProgram
            #pragma geometry GeometryProgram
            #pragma fragment FragmentProgram

            UNITY_DECLARE_TEX2DARRAY(_DispTex);
            UNITY_DECLARE_TEX2DARRAY(_SlopeTex);
            UNITY_DECLARE_TEX2D(_VarMaskTex);

            sampler2D _CamDepthNormalsTex;

            float _DispDepthAtten, _FoamDepthAtten;

            float _Tiling0, _Tiling1, _Tiling2, _Tiling3, _LayerContrib0, _LayerContrib1, _LayerContrib2, _LayerContrib3;

            float _NormStr, _HeightStr;

            float3 _ScatCol, _ScatPeakCol, _FoamCol, _AmbCol, _FogCol;

            float _AmbDens;

            float _WavePeakScatStr, _ScatStr, _ScatShadowStr;

            float _FoamRough, _Rough, _EnvLightStr;

            float _EdgeFoamPow, _ShadowInt, _FogDens, _FogPow;

            float _VarMaskRng, _VarMaskPow, _VarMaskScale;

            struct AppData
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : NORMAL;
                float4 _ShadowCoord : TEXCOORD1;
            };

            struct HullInput
            {
                float4 vertex : INTERNALTESSPOS;
                float2 uv : TEXCOORD0;
                float3 normal : NORMAL;
                float4 _ShadowCoord : TEXCOORD1;
            };

            struct VertexOutput
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 wPos : TEXCOORD1;
                float3 wNormal : TEXCOORD2;
                float clipDepth : TEXCOORD3;
                float viewDepth : TEXCOORD4;
                float2 screenUV : TEXCOORD5;
                float4 _ShadowCoord : TEXCOORD6;
            };

            struct GeometryOutput
            {
                VertexOutput data;
                float2 baryCoords : TEXCOORD9;
                float4 _ShadowCoord : TEXCOORD10;
            };

            HullInput VertexProgram(AppData v)
            {
                HullInput o;
                o.vertex = v.vertex;
                o.uv = v.uv;
                o.normal = v.normal;
                o._ShadowCoord = v._ShadowCoord;

                return o;
            }

            float ClampedDot(float3 a, float3 b) {
                return saturate(dot(a, b));
            }

            VertexOutput VertexProcess(HullInput h)
            {
                VertexOutput o;

                o.wPos = mul(unity_ObjectToWorld, h.vertex);

                float3 disp0 = UNITY_SAMPLE_TEX2DARRAY_LOD(_DispTex, float3(o.wPos.xz * _Tiling0, 0), 0) * _LayerContrib0;
                float3 disp1 = UNITY_SAMPLE_TEX2DARRAY_LOD(_DispTex, float3(o.wPos.xz * _Tiling1, 1), 0) * _LayerContrib1;
                float3 disp2 = UNITY_SAMPLE_TEX2DARRAY_LOD(_DispTex, float3(o.wPos.xz * _Tiling2, 2), 0) * _LayerContrib2;
                float3 disp3 = UNITY_SAMPLE_TEX2DARRAY_LOD(_DispTex, float3(o.wPos.xz * _Tiling3, 3), 0) * _LayerContrib3;
                float3 totalDisp = disp0 + disp1 + disp2 + disp3;

                float4 clipPos = UnityObjectToClipPos(h.vertex);
                float cDepth = 1 - Linear01Depth(clipPos.z / clipPos.w);
                
                totalDisp = lerp(0.0f, totalDisp, pow(saturate(cDepth), _DispDepthAtten));
                h.vertex.xyz += mul(unity_WorldToObject, totalDisp.xyz);

                clipPos = UnityObjectToClipPos(h.vertex);
                float2 sUV = ((clipPos.xy / clipPos.w) + 1) / 2;
                sUV.y = 1 - sUV.y;
                float vDepth = -mul(UNITY_MATRIX_MV, h.vertex).z * _ProjectionParams.w;
                
                o.pos = UnityObjectToClipPos(h.vertex);
                o.wNormal = normalize(mul((float3x3)unity_ObjectToWorld, h.normal));
                o.uv = o.wPos.xz;
                o.clipDepth = cDepth;
                o.viewDepth = vDepth;
                o.screenUV = sUV;

                TRANSFER_SHADOW(o);
            
                return o;
            }

            TessFactors PatchConstantFunction(InputPatch < HullInput, 3 > patch)
            {
                TessFactors f;

                float3 p0 = mul(unity_ObjectToWorld, patch[0].vertex);
                float3 p1 = mul(unity_ObjectToWorld, patch[1].vertex);
                float3 p2 = mul(unity_ObjectToWorld, patch[2].vertex);

                float bias = -0.5 * 100;

                if(ShouldCullTriangle(p0, p1, p2, bias))
                {
                    f.edgeFactors[0] = f.edgeFactors[1] = f.edgeFactors[2] = f.insideFactor = 0;
                }else{
                    f.edgeFactors[0] = CalculateTessellationFactor(p1, p2);
                    f.edgeFactors[1] = CalculateTessellationFactor(p2, p0);
                    f.edgeFactors[2] = CalculateTessellationFactor(p0, p1);
                    f.insideFactor = (CalculateTessellationFactor(p1, p2) +
                                CalculateTessellationFactor(p2, p0) +
                                CalculateTessellationFactor(p0, p1)) * (1.0f / 3.0f);
                }

                return f;
            }

            [UNITY_domain("tri")]
            [UNITY_outputcontrolpoints(3)]
            [UNITY_outputtopology("triangle_cw")]
            [UNITY_partitioning("integer")]
            [UNITY_patchconstantfunc("PatchConstantFunction")]

            HullInput HullProgram(InputPatch < HullInput, 3 > patch, uint id : SV_OUTPUTCONTROLPOINTID)
            {
                return patch[id];
            }

            #define INTERPOLATE_FIELD(field) data.field = \
                                    patch[0].field * baryCoords.x + \
                                    patch[1].field * baryCoords.y + \
                                    patch[2].field * baryCoords.z;


            [UNITY_domain("tri")]
            VertexOutput DomainProgram(TessFactors factors, OutputPatch < HullInput, 3 > patch, float3 baryCoords : SV_DOMAINLOCATION)
            {
                AppData data = (AppData)0;
                INTERPOLATE_FIELD(vertex)
                INTERPOLATE_FIELD(uv)
                INTERPOLATE_FIELD(normal)
                INTERPOLATE_FIELD(_ShadowCoord)

                return VertexProcess(data);
            }

            [maxvertexcount(3)]
            void GeometryProgram(triangle VertexOutput input[3], inout TriangleStream<GeometryOutput> stream)
            {
                GeometryOutput g0, g1, g2;
                g0.data = input[0];
                g1.data = input[1];
                g2.data = input[2];

                g0.baryCoords = float2(1, 0);
                g1.baryCoords = float2(0, 1);
                g2.baryCoords = float2(0, 0);

                g0._ShadowCoord = input[0]._ShadowCoord;
                g1._ShadowCoord = input[1]._ShadowCoord;
                g2._ShadowCoord = input[2]._ShadowCoord;

                stream.Append(g0);
                stream.Append(g1);
                stream.Append(g2);
            }

            float BeckmannDist(float nDoth, float r)
            {
                float expVal = (nDoth * nDoth - 1) / (r * r * nDoth * nDoth);
                return exp(expVal) / (UNITY_PI * r * r * nDoth * nDoth * nDoth * nDoth);
            }

            float SmithMask(float3 h, float3 v, float r)
            {
                float hDotv = max(0.001f, ClampedDot(h, v));
                float a = hDotv / (r * sqrt(1 - hDotv * hDotv));

                float a2 = a * a;
                return a < 1.6f ? (1.0f - 1.259f * a + 0.396f * a2) / (3.535f * a + 2.181 * a2) : 0.0f;
            }

            float CalculateFogFactor(float depth, float density)
            {
                return saturate(pow(1.0 - exp(-depth * density), _FogPow));
            }

            float4 FragmentProgram(GeometryOutput i) : SV_TARGET
            {
                float3 wPos = i.data.wPos;
                float3 wNormal = i.data.wNormal;
                float4 cPos = i.data.pos;
                float cDepth = i.data.clipDepth;
                float vDepth = i.data.viewDepth;
                float2 sUV = i.data.screenUV;

                float4 sCoord = mul(unity_WorldToShadow[0], float4(wPos, 1));
                sCoord.xyz /= sCoord.w;

                fixed shadowVal = SHADOW_ATTENUATION(i);
                
                float sDepth = DecodeFloatRG(tex2D(_CamDepthNormalsTex, sUV).zw);
                float dDiff =  sDepth - vDepth;
                float intersectVal = 0;
                if(dDiff > 0){
                    intersectVal = 1 - smoothstep(0, _ProjectionParams.w, dDiff);
                }
            
                float4 dispF0 = UNITY_SAMPLE_TEX2DARRAY_LOD(_DispTex, float3(wPos.xz * _Tiling0, 0), 0) * _LayerContrib0;
                float4 dispF1 = UNITY_SAMPLE_TEX2DARRAY_LOD(_DispTex, float3(wPos.xz * _Tiling1, 1), 0) * _LayerContrib1;
                float4 dispF2 = UNITY_SAMPLE_TEX2DARRAY_LOD(_DispTex, float3(wPos.xz * _Tiling2, 2), 0) * _LayerContrib2;
                float4 dispF3 = UNITY_SAMPLE_TEX2DARRAY_LOD(_DispTex, float3(wPos.xz * _Tiling3, 3), 0) * _LayerContrib3;
                float4 totalDispF = dispF0 + dispF1 + dispF2 + dispF3;

                float foamFactor = lerp(0.0f, saturate(totalDispF.a), pow(cDepth, _FoamDepthAtten));
                foamFactor = foamFactor + intersectVal * pow(foamFactor, _EdgeFoamPow);
                foamFactor *= saturate(shadowVal + _ShadowInt);

                float2 slopeVal0 = UNITY_SAMPLE_TEX2DARRAY_LOD(_SlopeTex, float3(i.data.uv * _Tiling0, 0), 0) * _LayerContrib0;
                float2 slopeVal1 = UNITY_SAMPLE_TEX2DARRAY_LOD(_SlopeTex, float3(i.data.uv * _Tiling1, 1), 0) * _LayerContrib1;
                float2 slopeVal2 = UNITY_SAMPLE_TEX2DARRAY_LOD(_SlopeTex, float3(i.data.uv * _Tiling2, 2), 0) * _LayerContrib2;
                float2 slopeVal3 = UNITY_SAMPLE_TEX2DARRAY_LOD(_SlopeTex, float3(i.data.uv * _Tiling3, 3), 0) * _LayerContrib3;
                float2 slopeSumA = slopeVal0 + slopeVal1 + slopeVal2 + slopeVal3;
                float2 slopeSumB = slopeVal2 + slopeVal3;

                float invUVDepth = saturate(pow(length(i.data.uv / 500 * _VarMaskRng), _VarMaskPow));
                float normMask = UNITY_SAMPLE_TEX2D(_VarMaskTex, float2(i.data.uv / 1000 * _VarMaskScale)).r * invUVDepth;
                normMask = saturate(normMask * 4);

                float2 finalSlopeVal = lerp(slopeSumA, slopeSumB, normMask) * _NormStr;

                // Macro and Meso normals
                float3 macroN = float3(0.0, 1.0, 0.0);
                float3 mesoN = normalize(float3(-finalSlopeVal.x, 1.0, -finalSlopeVal.y));
                mesoN = lerp(macroN, mesoN, pow(saturate(cDepth), _DispDepthAtten));
                mesoN = normalize(UnityObjectToWorldNormal(mesoN));

                // High precision calculations in fragment shader suitable for water effects
                float3 lDir = normalize(_WorldSpaceLightPos0.xyz);
                float3 vDir = normalize(_WorldSpaceCameraPos - wPos);
                float3 hDir = normalize(lDir + vDir);
                float3 rDir = reflect(-vDir, mesoN);
                float3 lCol = _LightColor0.rgb;

                float rVal = _Rough + foamFactor * _FoamRough;
                float vMask = SmithMask(hDir, vDir, rVal);
                float lMask = SmithMask(hDir, lDir, rVal);
                float geoMask = rcp(1 + vMask + lMask);

                float nDotL = max(0.001f, ClampedDot(mesoN, lDir));
                float nDotH = max(0.001, ClampedDot(mesoN, hDir));

                float ior = 1.33f;
                float refRate = (ior - 1) * (ior - 1) / (ior + 1) /(ior + 1);

                float numVal = pow(1 - dot(mesoN, vDir), 5 * exp(-2.69 * rVal));
                float fresnelVal = saturate(refRate + (1 - refRate) * numVal / (1.0f + 22.7f * pow(rVal, 1.5f)));

                float3 spec = lCol * fresnelVal * geoMask * BeckmannDist(nDotH, rVal);
                spec /= 4.0f * max(0.001f, ClampedDot(macroN, lDir));
                spec *= ClampedDot(mesoN, lDir);
                spec *= shadowVal;
                
                float varH = max(0.0f, dispF0.y) * _HeightStr;

                float k1Val = _WavePeakScatStr * varH * pow(ClampedDot(lDir, -vDir), 4.0f) * pow(0.5f - 0.5f * dot(lDir, mesoN), 3.0f);
                k1Val = lerp(0.0f, k1Val, pow(saturate(cDepth), _DispDepthAtten));
                float k2Val = _ScatStr * pow(ClampedDot(vDir, mesoN), 2.0f);
                float k3Val = _ScatShadowStr * nDotL;
                float k4Val = _AmbDens;

                float3 ambCol = ShadeSH9(float4(wNormal, 1.0));
                half3 envRef = DecodeHDR(UNITY_SAMPLE_TEXCUBE(unity_SpecCube0, rDir), unity_SpecCube0_HDR);
                envRef *= _EnvLightStr;
                envRef *= saturate(shadowVal + _ShadowInt + 0.5);

                float3 scat = (k1Val * _ScatPeakCol + k2Val * _ScatCol) * lCol * rcp(1 + lMask) * saturate(shadowVal + _ShadowInt);
                scat += k3Val * _ScatCol * lCol * saturate(shadowVal + _ShadowInt);
                scat += k4Val * ambCol;

                float fogVal = CalculateFogFactor(vDepth, _FogDens);

                float3 finalOut = ((1.0f - fresnelVal) * scat) + spec + fresnelVal * envRef;
                finalOut = lerp(finalOut, _FoamCol * lCol, saturate(foamFactor));
                finalOut = lerp(finalOut, _FogCol, fogVal);

                return float4(finalOut, 1);
            }

            ENDCG
        }

    }
    FallBack "Specular"
}