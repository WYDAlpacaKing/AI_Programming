using UnityEngine;

public class WaterSystemController : MonoBehaviour
{
    // --- Singleton Protection ---
    public static WaterSystemController Instance;

    public enum WaterAlgo { FBM_SineWave, Gerstner_Wave, FFT_Ocean }

    [Header("Scene References")]
    public GameObject simpleWaterObject;
    public FFTOcean_Script fftScript;
    public ProceduralGrid gridScript;

    [Header("Shaders (Built-in)")]
    public Shader fbmShader;
    public Shader gerstnerShader;

    // Logic Variables
    private WaterAlgo currentAlgo = WaterAlgo.FBM_SineWave;
    private Material simpleWaterMat;
    private MeshRenderer simpleRenderer;

    // GUI Variables
    private Rect panelRect = new Rect(20, 20, 360, 700);
    private Vector2 scrollPos;
    private string[] algoNames = new string[] { "FBM Sine", "Gerstner", "FFT Ocean" };

    // --- Params Definition ---
    private int gridResolution = 128;
    private Color diffuseColor = new Color(0, 0.5f, 1f, 1f);
    private float gloss = 256f;
    private float specularPower = 1f;
    private float waveIntensity = 1f;
    private float waveFrequency = 1.5f;
    private float waveSpeed_Sine = 4f;
    private int waveAmount_Sine = 32;
    private float peekSharpness_Sine = 0.6f;
    private float normalIntensity = 1f;
    private float fbmIntensity = 0.5f;
    private float fbmFrequent = 2.0f;
    private float fbmSpeed = 1.2f;
    private float g_PeekSharpness = 0.5f;
    private float g_Wavelength = 10.0f;
    private int g_WaveAmount = 16;
    private float g_WaveSpeed = 0.5f;
    private float g_NormalIntensity = 1.0f;
    private float g_FBMAmplitude = 0.5f;
    private float g_FBMFrequent = 1.2f;
    private float g_FBMTime = 1.0f;
    private float fft_Speed = 1.0f;
    private float fft_WindSpeed = 100.0f;
    private float fft_BaseTiling = 0.05f;
    private float fft_HeightMult = 1.0f;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void Start()
    {
        if (simpleWaterObject != null)
        {
            simpleRenderer = simpleWaterObject.GetComponent<MeshRenderer>();
            if (gridScript == null) gridScript = simpleWaterObject.GetComponent<ProceduralGrid>();
        }
        if (fftScript == null) fftScript = FindObjectOfType<FFTOcean_Script>();

        // Force Init
        SwitchAlgorithm(currentAlgo);
    }

    void Update()
    {
        if (currentAlgo == WaterAlgo.FFT_Ocean && fftScript != null)
        {
            ApplyFFTParams();
        }
        else if (simpleWaterMat != null)
        {
            simpleWaterMat.SetColor("_BaseColor", diffuseColor);
            simpleWaterMat.SetFloat("_Smoothness", gloss);
            simpleWaterMat.SetFloat("_SpecIntensity", specularPower);

            if (currentAlgo == WaterAlgo.FBM_SineWave) ApplyFBMParams();
            else if (currentAlgo == WaterAlgo.Gerstner_Wave) ApplyGerstnerParams();
        }
    }

    // --- Core Switching Logic ---
    void SwitchAlgorithm(WaterAlgo newAlgo)
    {
        currentAlgo = newAlgo;
        bool isFFT = (newAlgo == WaterAlgo.FFT_Ocean);

        if (simpleWaterObject) simpleWaterObject.SetActive(!isFFT);
        if (fftScript)
        {
            fftScript.gameObject.SetActive(isFFT);
            fftScript.enabled = isFFT;
        }

        if (!isFFT && simpleRenderer != null)
        {
            if (newAlgo == WaterAlgo.FBM_SineWave && fbmShader != null)
                simpleRenderer.material = new Material(fbmShader);
            else if (newAlgo == WaterAlgo.Gerstner_Wave && gerstnerShader != null)
                simpleRenderer.material = new Material(gerstnerShader);

            simpleWaterMat = simpleRenderer.material;
        }

        // Reset GUI state on switch to prevent ghosting
        GUIUtility.hotControl = 0;
        GUIUtility.keyboardControl = 0;
    }

    // ===========================
    // GUI Logic (Refactored for Safety)
    // ===========================
    void OnGUI()
    {
        // Draw the background box
        GUI.Box(panelRect, "Water System Control");

        // Define inner area for content
        Rect contentRect = new Rect(panelRect.x + 10, panelRect.y + 30, panelRect.width - 20, panelRect.height - 40);

        GUILayout.BeginArea(contentRect);

        // 1. Toolbar for switching
        int selected = GUILayout.Toolbar((int)currentAlgo, algoNames, GUILayout.Height(30));

        // Handle switching logic *after* drawing to prevent layout errors in current frame
        if (selected != (int)currentAlgo)
        {
            // We do NOT call SwitchAlgorithm here immediately.
            // We wait for the end of the frame or force a repaint.
            // However, simply calling it here usually works if we don't change the layout flow immediately.
            SwitchAlgorithm((WaterAlgo)selected);
        }

        GUILayout.Space(10);

        // 2. Scroll View
        scrollPos = GUILayout.BeginScrollView(scrollPos);

        // Draw UI based on *currentAlgo*
        // IMPORTANT: We use if/else if/else structure to ensure ONLY ONE path is taken per frame
        if (currentAlgo == WaterAlgo.FBM_SineWave)
        {
            DrawGridControl();
            DrawCommonLightingGUI();
            DrawFBMGUI();
        }
        else if (currentAlgo == WaterAlgo.Gerstner_Wave)
        {
            DrawGridControl();
            DrawCommonLightingGUI();
            DrawGerstnerGUI();
        }
        else if (currentAlgo == WaterAlgo.FFT_Ocean)
        {
            if (fftScript != null) DrawFFTGUI();
            else GUILayout.Label("<color=red>FFT Script Missing!</color>");
        }

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    // --- Modular Draw Functions ---

    void DrawGridControl()
    {
        if (gridScript == null) return;
        GUILayout.Label("<b>[Mesh Quality]</b>");
        DrawIntSlider("Res", ref gridResolution, 10, 400);
        // Only update if changed to save performance
        if (GUI.changed) gridScript.UpdateMesh(gridResolution);
        DrawLine();
    }

    void DrawCommonLightingGUI()
    {
        GUILayout.Label("<b>[Common Lighting]</b>");
        diffuseColor = DrawColor("Diffuse", diffuseColor);
        DrawSlider("Gloss", ref gloss, 1f, 512f);
        DrawSlider("Spec Pow", ref specularPower, 0f, 5f);
        GUILayout.Space(5);
        DrawLine();
    }

    void DrawFBMGUI()
    {
        GUILayout.Label("<b>[FBM Settings]</b>");
        DrawSlider("Intensity", ref waveIntensity, 0.1f, 5f);
        DrawSlider("Frequency", ref waveFrequency, 0.1f, 5f);
        DrawSlider("Speed", ref waveSpeed_Sine, 0f, 10f);
        DrawIntSlider("Amount", ref waveAmount_Sine, 1, 32);
        DrawSlider("Sharpness", ref peekSharpness_Sine, 0f, 2f);
        DrawSlider("Normal", ref normalIntensity, 0f, 3f);
        GUILayout.Space(5);
        GUILayout.Label("- Fractal -");
        DrawSlider("FBM Int", ref fbmIntensity, 0f, 1f);
        DrawSlider("FBM Freq", ref fbmFrequent, 1f, 3f);
        DrawSlider("FBM Spd", ref fbmSpeed, 0f, 3f);
    }

    void DrawGerstnerGUI()
    {
        GUILayout.Label("<b>[Gerstner Settings]</b>");
        DrawSlider("Sharpness", ref g_PeekSharpness, 0f, 1f);
        DrawSlider("Wavelength", ref g_Wavelength, 0.1f, 20f);
        DrawIntSlider("Amount", ref g_WaveAmount, 1, 32);
        DrawSlider("Speed", ref g_WaveSpeed, 0f, 2f);
        DrawSlider("Normal", ref g_NormalIntensity, 0f, 3f);
        GUILayout.Space(5);
        GUILayout.Label("- Fractal -");
        DrawSlider("FBM Amp", ref g_FBMAmplitude, 0f, 1f);
        DrawSlider("FBM Freq", ref g_FBMFrequent, 1f, 2f);
        DrawSlider("FBM Time", ref g_FBMTime, 0f, 2f);
    }

    void DrawFFTGUI()
    {
        GUILayout.Label("<b>[FFT Ocean]</b>");
        GUILayout.Space(5);
        DrawSlider("Speed", ref fftScript.SimSpeed, 0f, 5f);
        DrawSlider("Gravity", ref fftScript.GravAccel, 1f, 20f);
        DrawIntSlider("Mesh Res", ref fftScript.meshResolution, 1, 50);
        DrawIntSlider("Tessell", ref fftScript.TessEdgeLen, 1, 50);

        GUILayout.Space(5);
        GUILayout.Label("<b>[Easy Controls]</b>");
        DrawSlider("Wind Spd", ref fft_WindSpeed, 10f, 2000f);
        DrawSlider("Tiling", ref fft_BaseTiling, 0.01f, 0.2f);
        DrawSlider("Height", ref fft_HeightMult, 0.0f, 3.0f);

        GUILayout.Space(5);
        DrawLine();
        GUILayout.Label("<b>[Advanced Details]</b>");

        GUILayout.Label("- Lighting -");
        fftScript.ColScatter = DrawColor("Scatter", fftScript.ColScatter);
        fftScript.ColScatterPeak = DrawColor("Peak", fftScript.ColScatterPeak);
        DrawSlider("Roughness", ref fftScript.SurfaceRough, 0f, 1f);
        DrawSlider("SSS", ref fftScript.ScatterStr, 0f, 1f);
        DrawSlider("Env Ref", ref fftScript.EnvReflectStr, 0f, 2f);

        GUILayout.Label("- Foam -");
        fftScript.ColFoam = DrawColor("Color", fftScript.ColFoam);
        DrawSlider("Bias", ref fftScript.FoamOffset, -1f, 1f);
        DrawSlider("Decay", ref fftScript.FoamDecay, 0f, 0.2f);
    }

    void ApplyFBMParams()
    {
        simpleWaterMat.SetFloat("_AmpBase", waveIntensity);
        simpleWaterMat.SetFloat("_FreqBase", waveFrequency);
        simpleWaterMat.SetFloat("_SpdBase", waveSpeed_Sine);
        simpleWaterMat.SetInt("_Octaves", waveAmount_Sine);
        simpleWaterMat.SetFloat("_HeightScale", peekSharpness_Sine);
        simpleWaterMat.SetFloat("_BumpStrength", normalIntensity);
        simpleWaterMat.SetFloat("_GainFactor", fbmIntensity);
        simpleWaterMat.SetFloat("_Lacunarity", fbmFrequent);
        simpleWaterMat.SetFloat("_SpdFactor", fbmSpeed);
    }

    void ApplyGerstnerParams()
    {
        simpleWaterMat.SetFloat("_Steepness", g_PeekSharpness);
        simpleWaterMat.SetFloat("_WavelengthBase", g_Wavelength);
        simpleWaterMat.SetInt("_IterNum", g_WaveAmount);
        simpleWaterMat.SetFloat("_SpdMult", g_WaveSpeed);
        simpleWaterMat.SetFloat("_BumpScale", g_NormalIntensity);
        simpleWaterMat.SetFloat("_AmpFactor", g_FBMAmplitude);
        simpleWaterMat.SetFloat("_FreqFactor", g_FBMFrequent);
        simpleWaterMat.SetFloat("_TimeFactor", g_FBMTime);
    }

    void ApplyFFTParams()
    {
        fftScript.SimSpeed = fft_Speed;
        var ds0 = fftScript.SpecSettings0; ds0.windSpd = fft_WindSpeed; fftScript.SpecSettings0 = ds0;
        var ds1 = fftScript.SpecSettings1; ds1.windSpd = fft_WindSpeed * 0.8f; fftScript.SpecSettings1 = ds1;
        fftScript.Tiling0 = fft_BaseTiling;
        fftScript.Tiling1 = fft_BaseTiling * 2.0f;
        fftScript.Tiling2 = fft_BaseTiling * 4.0f;
        fftScript.Tiling3 = fft_BaseTiling * 8.0f;
        fftScript.LayerContrib0 = fft_HeightMult;
        fftScript.LayerContrib1 = fft_HeightMult * 0.5f;
        fftScript.LayerContrib2 = fft_HeightMult * 0.25f;
        fftScript.LayerContrib3 = fft_HeightMult * 0.1f;
    }

    // --- Helpers ---
    void DrawLine() { GUILayout.Box("", GUILayout.Height(2), GUILayout.ExpandWidth(true)); GUILayout.Space(5); }

    void DrawSlider(string label, ref float value, float min, float max)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(70));
        value = GUILayout.HorizontalSlider(value, min, max);
        GUILayout.EndHorizontal();
    }
    void DrawIntSlider(string label, ref int value, int min, int max)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(70));
        value = (int)GUILayout.HorizontalSlider((float)value, (float)min, (float)max);
        GUILayout.EndHorizontal();
    }
    Color DrawColor(string label, Color c)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(70));
        GUILayout.Label("R", GUILayout.Width(10)); c.r = GUILayout.HorizontalSlider(c.r, 0, 1);
        GUILayout.Label("G", GUILayout.Width(10)); c.g = GUILayout.HorizontalSlider(c.g, 0, 1);
        GUILayout.Label("B", GUILayout.Width(10)); c.b = GUILayout.HorizontalSlider(c.b, 0, 1);
        GUILayout.EndHorizontal();
        return c;
    }
}