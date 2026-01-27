using System;
using System.Collections;
using System.Collections.Generic;
using static System.Runtime.InteropServices.Marshal;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class FFTOcean_Script : MonoBehaviour
{
    // Texture declarations
    public RenderTexture TexInitSpectrum,
                         TexSpectrum,
                         TexDisp,
                         TexSlope,
                         TexBuoyancy,
                         TexVarMask;

    [Header("Shaders")]
    public ComputeShader CompShaderFFT;
    public Shader ShaderFFTWater;

    [Header("Water Surface Mesh Settings")]
    public int meshSize = 200;
    public int meshResolution = 10;

    [Range(10, 20)]
    public int TessEdgeLen = 16;

    private Mesh meshSurface;
    private Material matWater;
    private int ResSize, groupsX, groupsY;

    [Header("General Settings")]
    public float DispDepthAtten = 10;
    public float FoamDepthAtten = 20;

    [Range(0.0f, 5.0f)]
    public float SimSpeed = 0.5f;
    public int RandSeed = 28;
    [Range(2.0f, 20.0f)]
    public float GravAccel = 9.81f;

    private float WaterDepth = 10.0f;
    private float LoopPeriod = 200.0f;
    private float LowFreqCutoff = 0.0001f;
    private float HighFreqCutoff = 9000.0f;

    [Header("Layer 01")]
    [Range(0.0f, 1.0f)]
    public float LayerContrib0 = 0.8f;
    [Range(0.0f, 0.25f)]
    public float Tiling0 = 0.04f;

    [SerializeField]
    public JONSWAP_Params SpecSettings0;
    [SerializeField]
    public JONSWAP_Params SpecSettings1;

    private int Scale0 = 4;

    [Header("Layer 02")]
    [Range(0.0f, 1.0f)]
    public float LayerContrib1 = 0.8f;
    [Range(0.0f, 0.25f)]
    public float Tiling1 = 0.06f;

    [SerializeField]
    public JONSWAP_Params SpecSettings2;
    public JONSWAP_Params SpecSettings3;
    private int Scale1 = 4;

    [Header("Layer 03")]
    [Range(0.0f, 1.0f)]
    public float LayerContrib2 = 0.6f;
    [Range(0.0f, 0.25f)]
    public float Tiling2 = 0.12f;

    [SerializeField]
    public JONSWAP_Params SpecSettings4;
    public JONSWAP_Params SpecSettings5;
    private int Scale2 = 4;

    [Header("Layer 04")]
    [Range(0.0f, 1.0f)]
    public float LayerContrib3 = 0.4f;
    [Range(0.0f, 0.25f)]
    public float Tiling3 = 0.18f;

    [SerializeField]
    public JONSWAP_Params SpecSettings6;
    public JONSWAP_Params SpecSettings7;
    private int Scale3 = 4;

    [Header("Shader Settings")]
    public Color ColScatter = new Color(0.0f, 0.67f, 1.0f, 1.0f);
    public Color ColScatterPeak = new Color(0.0f, 0.67f, 1.0f, 1.0f);
    public float PeakScatterStr = 2.0f;
    public float ScatterStr = 0.1f;
    public float ScatterShadowStr = 0.1f;
    [Space(10)]

    public float AmbDensity = 0.2f;
    public float EnvReflectStr = 1.0f;
    [Space(10)]
    public float FoamRough = 0.2f;
    public float SurfaceRough = 0.1f;
    [Space(10)]
    public float NormStr = 0.2f;
    public float HeightStr = 1.0f;

    [Space(10)]
    [Range(0.0f, 1.0f)]
    public float ShadowInt = 0.2f;

    [Header("Foam Settings")]
    public Color ColFoam = new Color(1, 1, 1, 1);
    [Space(10)]
    public Vector2 WaveChop = new Vector2(0.4f, 0.4f);
    [Range(-1.0f, 1.0f)]
    public float FoamOffset = 0.2f;

    [Range(-0.0f, 4.0f)]
    public float FoamExp = 1.5f;

    [Range(0.0f, 1.0f)]
    public float FoamAmount = 0.1f;

    [Range(0.0f, 1.0f)]
    public float FoamDecay = 0.05f;
    [Range(0.01f, 1.0f)]
    public float EdgeFoamPow;

    [Header("Normal Variation")]
    [Range(0.01f, 10.0f)]
    public float VarMaskRng = 3.0f;
    [Range(0.01f, 10.0f)]
    public float VarMaskPow = 3.0f;
    [Range(0.01f, 10.0f)]
    public float VarMaskScale = 2.0f;

    [Header("Fog Settings")]
    public Color ColFog = new Color(0.5f, 0.75f, 0.0f);

    [Range(0.0f, 20.0f)]
    public float FogDens = 1.0f;
    [Range(0.0f, 10.0f)]
    public float FogPow = 4.0f;

    [System.Serializable]
    // Struct passed to compute shader
    public struct JONSWAP_CompParams
    {
        public float scaleFactor;
        public float windDir;
        public float spreadFactor;
        public float swellFactor;
        public float alphaVal;
        public float peakFreq;
        public float gammaVal;
        public float fadeFactor;
    }
    JONSWAP_CompParams[] CompSpectrums = new JONSWAP_CompParams[8];

    // Public struct params. Alpha and peakOmega calculated dynamically from windSpeed and windDirection.
    // Angle and gamma renamed for clarity.
    [System.Serializable]
    public struct JONSWAP_Params
    {
        [Range(0, 5)]
        public float scaleFactor;
        public float windSpd;

        [Range(0, 360)]
        public float windDir;
        public float fetchDist;
        [Range(0, 1)]
        public float spreadFactor;
        [Range(0, 1)]
        public float swellFactor;
        public float peakEnhance;

        [Range(0, 1)]
        public float fadeFactor;
    }

    // Buffer for JONSWAP struct
    private ComputeBuffer BufferJonswap;

    // Kernel declarations
    private int Kernal_InitSpectrum;
    private int Kernal_PackSpectrumConjugate;
    private int Kernal_UpdateSpectrum;
    private int Kernal_HorizontalIFFT;
    private int Kernal_VerticalIFFT;
    private int Kernal_AssembleTextures;

    public RenderTexture FetchBuoyancyData()
    {
        return TexBuoyancy;
    }

    // Set default values
    private void Reset()
    {
        // 00
        SpecSettings0.scaleFactor = 0.4f;
        SpecSettings0.windSpd = 1200.0f;
        SpecSettings0.windDir = 130.0f;
        SpecSettings0.fetchDist = 600.0f;
        SpecSettings0.spreadFactor = 1.0f;
        SpecSettings0.swellFactor = 0.9f;
        SpecSettings0.peakEnhance = 5.0f;
        SpecSettings0.fadeFactor = 0.8f;
        // 01
        SpecSettings1.scaleFactor = 0.4f;
        SpecSettings1.windSpd = 1000.0f;
        SpecSettings0.windDir = 50.0f;
        SpecSettings1.fetchDist = 500.0f;
        SpecSettings1.spreadFactor = 1.0f;
        SpecSettings1.swellFactor = 0.9f;
        SpecSettings1.peakEnhance = 5.0f;
        SpecSettings1.fadeFactor = 0.8f;

        // 02
        SpecSettings2.scaleFactor = 0.1f;
        SpecSettings2.windSpd = 800.0f;
        SpecSettings2.windDir = 45.0f;
        SpecSettings2.fetchDist = 400.0f;
        SpecSettings2.spreadFactor = 0.98f;
        SpecSettings2.swellFactor = 0.9f;
        SpecSettings2.peakEnhance = 5.0f;
        SpecSettings2.fadeFactor = 0.4f;
        // 03
        SpecSettings3.scaleFactor = 0.1f;
        SpecSettings3.windSpd = 800.0f;
        SpecSettings3.windDir = 135.0f;
        SpecSettings3.fetchDist = 350.0f;
        SpecSettings3.spreadFactor = 0.98f;
        SpecSettings3.swellFactor = 0.9f;
        SpecSettings3.peakEnhance = 5.0f;
        SpecSettings3.fadeFactor = 0.4f;

        // 04
        SpecSettings4.scaleFactor = 0.04f;
        SpecSettings4.windSpd = 100.0f;
        SpecSettings4.windDir = 260.0f;
        SpecSettings4.fetchDist = 100.0f;
        SpecSettings4.spreadFactor = 0.95f;
        SpecSettings4.swellFactor = 0.8f;
        SpecSettings4.peakEnhance = 3.0f;
        SpecSettings4.fadeFactor = 0.4f;
        // 05
        SpecSettings5.scaleFactor = 0.04f;
        SpecSettings5.windSpd = 50.0f;
        SpecSettings5.windDir = 280.0f;
        SpecSettings5.fetchDist = 100.0f;
        SpecSettings5.spreadFactor = 0.95f;
        SpecSettings5.swellFactor = 0.8f;
        SpecSettings5.peakEnhance = 3.0f;
        SpecSettings5.fadeFactor = 0.4f;

        // 06
        SpecSettings6.scaleFactor = 0.1f;
        SpecSettings6.windSpd = 10.0f;
        SpecSettings6.windDir = 0.0f;
        SpecSettings6.fetchDist = 40.0f;
        SpecSettings6.spreadFactor = 0.8f;
        SpecSettings6.swellFactor = 0.6f;
        SpecSettings6.peakEnhance = 1.0f;
        SpecSettings6.fadeFactor = 0.2f;
        // 07
        SpecSettings7.scaleFactor = 0.1f;
        SpecSettings7.windSpd = 10.0f;
        SpecSettings7.windDir = 0.0f;
        SpecSettings7.fetchDist = 20.0f;
        SpecSettings7.spreadFactor = 0.6f;
        SpecSettings7.swellFactor = 0.4f;
        SpecSettings7.peakEnhance = 1.0f;
        SpecSettings7.fadeFactor = 0.2f;
    }

    // Generate water mesh
    private void BuildWaterMesh()
    {
        GetComponent<MeshFilter>().mesh = meshSurface = new Mesh();
        meshSurface.name = "Water Surface";
        meshSurface.indexFormat = IndexFormat.UInt32;

        float halfLen = meshSize / 2.0f;
        int sideVerts = meshSize * meshResolution / 100;

        Vector3[] verts = new Vector3[(sideVerts + 1) * (sideVerts + 1)];
        Vector2[] uvs = new Vector2[verts.Length];
        Vector4[] tans = new Vector4[verts.Length];
        Vector4 tanVal = new Vector4(1f, 0f, 0f, -1f);
        int[] tris = new int[sideVerts * sideVerts * 6];

        // Assign vertices, uvs, tangents
        for (int i = 0, x = 0; x <= sideVerts; ++x)
        {
            for (int z = 0; z <= sideVerts; ++z, ++i)
            {
                verts[i] = new Vector3(((float)x / sideVerts * meshSize) - halfLen,
                                        0,
                                        ((float)z / sideVerts * meshSize) - halfLen);
                uvs[i] = new Vector2((float)x / sideVerts, (float)z / sideVerts);
                tans[i] = tanVal;
            }
        }

        // Assign triangles
        for (int tIdx = 0, vIdx = 0, x = 0; x < sideVerts; ++vIdx, ++x)
        {
            for (int z = 0; z < sideVerts; tIdx += 6, ++vIdx, ++z)
            {
                tris[tIdx] = vIdx;
                tris[tIdx + 1] = vIdx + 1;
                tris[tIdx + 2] = vIdx + sideVerts + 2;
                tris[tIdx + 3] = vIdx;
                tris[tIdx + 4] = vIdx + sideVerts + 2;
                tris[tIdx + 5] = vIdx + sideVerts + 1;
            }
        }

        meshSurface.vertices = verts;
        meshSurface.uv = uvs;
        meshSurface.tangents = tans;
        meshSurface.triangles = tris;
        meshSurface.RecalculateNormals();
        Vector3[] norms = meshSurface.normals;
    }

    private void SetupWaterMat()
    {
        if (ShaderFFTWater == null) return;

        matWater = new Material(ShaderFFTWater);

        MeshRenderer rend = GetComponent<MeshRenderer>();

        rend.material = matWater;
    }

    // -----------------------------------------------------------------------
    // Utility Functions

    // Convert Alpha Data
    float CalcJonswapAlpha(float fetch, float windSpd)
    {
        return 0.076f * Mathf.Pow(GravAccel * fetch / windSpd / windSpd, -0.22f); // Dynamically calculate Alpha from fetch and windSpeed (need reference)
    }

    // Convert PeakOmega Data
    float CalcJonswapPeakFreq(float fetch, float windSpd)
    {
        return 22 * Mathf.Pow(windSpd * fetch / GravAccel / GravAccel, -0.33f); // Dynamically calculate peakOmega from fetch and windSpeed (need reference)
    }

    // Pass user data to struct
    void PopulateSpecStruct(JONSWAP_Params displaySettings, ref JONSWAP_CompParams computeSettings)
    {
        computeSettings.scaleFactor = displaySettings.scaleFactor;
        computeSettings.windDir = displaySettings.windDir / 180 * Mathf.PI;
        computeSettings.spreadFactor = displaySettings.spreadFactor;
        computeSettings.swellFactor = Mathf.Clamp(displaySettings.swellFactor, 0.01f, 1);
        computeSettings.alphaVal = CalcJonswapAlpha(displaySettings.fetchDist, displaySettings.windSpd);
        computeSettings.peakFreq = CalcJonswapPeakFreq(displaySettings.fetchDist, displaySettings.windSpd);
        computeSettings.gammaVal = displaySettings.peakEnhance;
        computeSettings.fadeFactor = displaySettings.fadeFactor;
    }

    // Create buffer and pass data
    void InitSpecBuffers()
    {
        PopulateSpecStruct(SpecSettings0, ref CompSpectrums[0]);
        PopulateSpecStruct(SpecSettings1, ref CompSpectrums[1]);
        PopulateSpecStruct(SpecSettings2, ref CompSpectrums[2]);
        PopulateSpecStruct(SpecSettings3, ref CompSpectrums[3]);
        PopulateSpecStruct(SpecSettings4, ref CompSpectrums[4]);
        PopulateSpecStruct(SpecSettings5, ref CompSpectrums[5]);
        PopulateSpecStruct(SpecSettings6, ref CompSpectrums[6]);
        PopulateSpecStruct(SpecSettings7, ref CompSpectrums[7]);

        BufferJonswap.SetData(CompSpectrums);
        CompShaderFFT.SetBuffer(0, "_BufferJonswap", BufferJonswap);
    }

    // Create and set texture
    RenderTexture GenerateRenderTexArray(int w, int h, int d, RenderTextureFormat fmt, bool useMips)
    {
        RenderTexture rt = new RenderTexture(w, h, 0, fmt, RenderTextureReadWrite.Linear);
        rt.dimension = UnityEngine.Rendering.TextureDimension.Tex2DArray;
        rt.filterMode = FilterMode.Bilinear;
        rt.wrapMode = TextureWrapMode.Repeat;
        rt.enableRandomWrite = true;
        rt.volumeDepth = d;
        rt.useMipMap = useMips;
        rt.autoGenerateMips = false;
        rt.anisoLevel = 16;
        rt.Create();

        return rt;
    }

    RenderTexture GenerateRenderTex(int w, int h, RenderTextureFormat fmt, bool useMips)
    {
        RenderTexture rt = new RenderTexture(w, h, 0, fmt, RenderTextureReadWrite.Linear);
        rt.filterMode = FilterMode.Bilinear;
        rt.wrapMode = TextureWrapMode.Repeat;
        rt.enableRandomWrite = true;
        rt.useMipMap = useMips;
        rt.autoGenerateMips = false;
        rt.anisoLevel = 16;
        rt.Create();

        return rt;
    }

    void UpdateCompParams()
    {
        CompShaderFFT.SetFloat("_Depth", WaterDepth);
        CompShaderFFT.SetFloat("_GravAccel", GravAccel);
        CompShaderFFT.SetFloat("_SimTime", Time.time * SimSpeed);
        CompShaderFFT.SetFloat("_LoopPeriod", LoopPeriod);
        CompShaderFFT.SetFloat("_LowFreqCutoff", LowFreqCutoff);
        CompShaderFFT.SetFloat("_HighFreqCutoff", HighFreqCutoff);
        CompShaderFFT.SetVector("_Choppiness", WaveChop);

        CompShaderFFT.SetInt("_GridSize", ResSize);
        CompShaderFFT.SetInt("_Scale0", Scale0);
        CompShaderFFT.SetInt("_Scale1", Scale1);
        CompShaderFFT.SetInt("_Scale2", Scale2);
        CompShaderFFT.SetInt("_Scale3", Scale3);
        CompShaderFFT.SetInt("_RandSeed", RandSeed);

        CompShaderFFT.SetFloat("_FoamOffset", FoamOffset);
        CompShaderFFT.SetFloat("_FoamExp", FoamExp);
        CompShaderFFT.SetFloat("_FoamAmount", FoamAmount);
        CompShaderFFT.SetFloat("_FoamDecay", FoamDecay);
    }

    void UpdateMatParams()
    {
        matWater.SetFloat("_DispDepthAtten", DispDepthAtten);
        matWater.SetFloat("_FoamDepthAtten", FoamDepthAtten);

        matWater.SetFloat("_EdgeLength", TessEdgeLen);
        matWater.SetFloat("_Tiling0", Tiling0);
        matWater.SetFloat("_Tiling1", Tiling1);
        matWater.SetFloat("_Tiling2", Tiling2);
        matWater.SetFloat("_Tiling3", Tiling3);

        matWater.SetFloat("_LayerContrib0", LayerContrib0);
        matWater.SetFloat("_LayerContrib1", LayerContrib1);
        matWater.SetFloat("_LayerContrib2", LayerContrib2);
        matWater.SetFloat("_LayerContrib3", LayerContrib3);

        matWater.SetFloat("_NormStr", NormStr);
        matWater.SetFloat("_HeightStr", HeightStr);

        matWater.SetColor("_ScatCol", ColScatter);
        matWater.SetColor("_ScatPeakCol", ColScatterPeak);
        matWater.SetColor("_FoamCol", ColFoam);

        matWater.SetFloat("_AmbDens", AmbDensity);

        matWater.SetFloat("_WavePeakScatStr", PeakScatterStr);
        matWater.SetFloat("_ScatStr", ScatterStr);
        matWater.SetFloat("_ScatShadowStr", ScatterShadowStr);

        matWater.SetFloat("_FoamRough", FoamRough);
        matWater.SetFloat("_Rough", SurfaceRough);
        matWater.SetFloat("_EnvLightStr", EnvReflectStr);

        matWater.SetFloat("_EdgeFoamPow", EdgeFoamPow);
        matWater.SetFloat("_ShadowInt", ShadowInt);

        matWater.SetFloat("_VarMaskRng", VarMaskRng);
        matWater.SetFloat("_VarMaskPow", VarMaskPow);
        matWater.SetFloat("_VarMaskScale", VarMaskScale);

        matWater.SetFloat("_FogDens", FogDens);
        matWater.SetFloat("_FogPow", FogPow);
        matWater.SetColor("_FogCol", ColFog);
    }

    void OnEnable()
    {
        BuildWaterMesh();
        SetupWaterMat();

        // Private param assignment
        ResSize = 1024;
        groupsX = Mathf.CeilToInt(ResSize / 8.0f);
        groupsY = Mathf.CeilToInt(ResSize / 8.0f);

        // Get ComputeShader kernel IDs
        Kernal_InitSpectrum = CompShaderFFT.FindKernel("Kernal_InitSpectrum");
        Kernal_PackSpectrumConjugate = CompShaderFFT.FindKernel("Kernal_PackSpectrumConjugate");
        Kernal_UpdateSpectrum = CompShaderFFT.FindKernel("Kernal_UpdateSpectrum");
        Kernal_HorizontalIFFT = CompShaderFFT.FindKernel("Kernal_HorizontalIFFT");
        Kernal_VerticalIFFT = CompShaderFFT.FindKernel("Kernal_VerticalIFFT");
        Kernal_AssembleTextures = CompShaderFFT.FindKernel("Kernal_AssembleTextures");

        // Create Textures
        TexInitSpectrum = GenerateRenderTexArray(ResSize, ResSize, 4, RenderTextureFormat.ARGBHalf, true);
        TexSpectrum = GenerateRenderTexArray(ResSize, ResSize, 8, RenderTextureFormat.ARGBHalf, true);
        TexDisp = GenerateRenderTexArray(ResSize, ResSize, 4, RenderTextureFormat.ARGBHalf, true);
        TexSlope = GenerateRenderTexArray(ResSize, ResSize, 4, RenderTextureFormat.RGHalf, true);
        TexBuoyancy = GenerateRenderTex(ResSize, ResSize, RenderTextureFormat.RHalf, false);
        TexVarMask = GenerateRenderTex(ResSize, ResSize, RenderTextureFormat.ARGBHalf, false);

        TexInitSpectrum.Create();
        TexSpectrum.Create();

        // Pass JONSWAP struct data
        BufferJonswap = new ComputeBuffer(8, 8 * sizeof(float));
        InitSpecBuffers();

        // Assign values
        UpdateCompParams();
    }

    void Update()
    {
        // Assign values
        UpdateCompParams();
        InitSpecBuffers();
        UpdateMatParams();

        // Init spectrum
        CompShaderFFT.SetTexture(Kernal_InitSpectrum, "_TexInitSpectrum", TexInitSpectrum);
        CompShaderFFT.Dispatch(Kernal_InitSpectrum, groupsX, groupsY, 1);

        // Conjugate
        CompShaderFFT.SetTexture(Kernal_PackSpectrumConjugate, "_TexInitSpectrum", TexInitSpectrum);
        CompShaderFFT.Dispatch(Kernal_PackSpectrumConjugate, groupsX, groupsY, 1);

        // Update spectrum for IFFT
        CompShaderFFT.SetTexture(Kernal_UpdateSpectrum, "_TexInitSpectrum", TexInitSpectrum);
        CompShaderFFT.SetTexture(Kernal_UpdateSpectrum, "_TexSpectrum", TexSpectrum);
        CompShaderFFT.SetTexture(Kernal_UpdateSpectrum, "_TexVarMask", TexVarMask);
        CompShaderFFT.Dispatch(Kernal_UpdateSpectrum, groupsX, groupsY, 1);


        // Horizontal IFFT
        CompShaderFFT.SetTexture(Kernal_HorizontalIFFT, "_TargetFourier", TexSpectrum);
        CompShaderFFT.SetTexture(Kernal_HorizontalIFFT, "_TargetFourierExtra", TexVarMask);
        CompShaderFFT.Dispatch(Kernal_HorizontalIFFT, 1, ResSize, 1);

        // Vertical IFFT
        CompShaderFFT.SetTexture(Kernal_VerticalIFFT, "_TargetFourier", TexSpectrum);
        CompShaderFFT.SetTexture(Kernal_VerticalIFFT, "_TargetFourierExtra", TexVarMask);
        CompShaderFFT.Dispatch(Kernal_VerticalIFFT, 1, ResSize, 1);


        // Assemble textures
        CompShaderFFT.SetTexture(Kernal_AssembleTextures, "_TexSpectrum", TexSpectrum);
        CompShaderFFT.SetTexture(Kernal_AssembleTextures, "_TexDisp", TexDisp);
        CompShaderFFT.SetTexture(Kernal_AssembleTextures, "_TexSlope", TexSlope);
        CompShaderFFT.SetTexture(Kernal_AssembleTextures, "_TexBuoyancy", TexBuoyancy);
        CompShaderFFT.SetTexture(Kernal_AssembleTextures, "_TexVarMask", TexVarMask);
        CompShaderFFT.Dispatch(Kernal_AssembleTextures, groupsX, groupsY, 1);

        // Pass results to Shader
        matWater.SetTexture("_DispTex", TexDisp);
        matWater.SetTexture("_SlopeTex", TexSlope);
        matWater.SetTexture("_VarMaskTex", TexVarMask);
    }

    void OnDestroy()
    {
        if (BufferJonswap != null)
        {
            BufferJonswap.Release();
        }
    }
}