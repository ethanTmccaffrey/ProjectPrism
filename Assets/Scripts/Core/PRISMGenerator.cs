using UnityEngine;

public class PRISMGenerator : MonoBehaviour
{
    //Atmosphere layers: always present//
    [Header("Atmosphere Layers")]
    [SerializeField] private PostProcessingLayer postProcessingLayer;
    [SerializeField] private SDFLayer sdfLayer;

    //Generator references: populated by finding components on child GameObjects//
    //Each generator reads TimbralProfile weights to decide whether to activate//
    [Header("Generators")]
    [SerializeField] private ConcentricRingsGenerator concentricRings;
    [SerializeField] private TunnelDepthGenerator tunnelDepth;
    [SerializeField] private SpiralGrowthGenerator spiralGrowth;
    [SerializeField] private RotationFieldGenerator rotationField;
    [SerializeField] private DriftGenerator drift;
    [SerializeField] private HoneycombGenerator honeycomb;
    [SerializeField] private GridGratingGenerator gridGrating;
    [SerializeField] private FiligreeGenerator filigree;
    [SerializeField] private ReduplicationGenerator reduplication;
    [SerializeField] private RadiationBurstGenerator radiationBurst;
    [SerializeField] private FractureGenerator fracture;
    [SerializeField] private CobwebSplineGenerator cobwebSpline;
    [SerializeField] private ZigzagParallelGenerator zigzagParallel;
    [SerializeField] private WavyParallelGenerator wavyParallel;
    [SerializeField] private HatchingGenerator hatching;
    [SerializeField] private FluidTendrilGenerator fluidTendril;
    [SerializeField] private AmorphousSpeckGenerator amorphousSpeck;
    [SerializeField] private BilateralDuplicationGenerator bilateralDuplication;
    [SerializeField] private SpeckClusterGenerator speckCluster;
    [SerializeField] private OrganicClusterGenerator organicCluster;

    //Source Points: the two ears, origin of everything in the scene//
    [Header("Source Points")]
    [SerializeField] private Vector3 leftSourcePoint = new Vector3(-60f, 0f, 0f);
    [SerializeField] private Vector3 rightSourcePoint = new Vector3(60f, 0f, 0f);
    public Vector3 LeftSourcePoint => leftSourcePoint;
    public Vector3 RightSourcePoint => rightSourcePoint;

    //Six derived qualities: track-level character, computed once at Init//
    [Header("Derived Qualities (Read Only)")]
    public float Space { get; private set; } // 0 = Intimate, 1 = Vast//
    public float Light { get; private set; } // 0 = Dark, 1 = Bright//
    public float Form { get; private set; } // 0 = Smooth, 1 = Angular//
    public float Colour { get; private set; } // 0 = Cool hue, 1 = Warm hue//
    public float Motion { get; private set; } // 0 = Slow, 1 = Fast//
    public float Scale { get; private set; } // 0 = Uniform, 1 = Extreme Contrast//

    //Derived colours//
    public Color PrimaryColour { get; private set; }
    public Color SecondaryColour { get; private set; }
    public Color RealtimeColour { get; private set; }

    //The timbral profile: updated every frame, read by all generators//
    public TimbralProfile TimbralProfile { get; private set; }

    //Audio//
    private AudioAnalyser _analyser;
    private AudioSource _audioSource;
    private bool _generating = false;

    //Stereo spectrum//
    private float[] _spectrumLeft = new float[256];
    private float[] _spectrumRight = new float[256];
    public float[] Spectrum => _spectrumLeft;

    //Mono realtime values//
    public float RealtimeEnergy { get; private set; }
    public float RealtimeBass { get; private set; }
    public float RealtimeMid { get; private set; }
    public float RealtimeHigh { get; private set; }

    //Stereo realtime values//
    public float RealtimeEnergyLeft { get; private set; }
    public float RealtimeBassLeft { get; private set; }
    public float RealtimeMidLeft { get; private set; }
    public float RealtimeHighLeft { get; private set; }
    public float RealtimeEnergyRight { get; private set; }
    public float RealtimeBassRight { get; private set; }
    public float RealtimeMidRight { get; private set; }
    public float RealtimeHighRight { get; private set; }

    public void Init(AudioAnalyser analyser, AudioSource audioSource)
    {
        _analyser = analyser;
        _audioSource = audioSource;

        //Inherit the TimbralProfile already computed by AudioAnalyser//
        TimbralProfile = analyser.TimbralProfile;

        DeriveQualities();
        DeriveColours();

        //Init atmosphere layers//
        if (postProcessingLayer != null) postProcessingLayer.Init(this);
        if (sdfLayer != null) sdfLayer.Init(this);

        //Init all generators: each one reads TimbralProfile to decide its behaviour//
        InitGenerator(concentricRings);
        InitGenerator(tunnelDepth);
        InitGenerator(spiralGrowth);
        InitGenerator(rotationField);
        InitGenerator(drift);
        InitGenerator(honeycomb);
        InitGenerator(gridGrating);
        InitGenerator(filigree);
        InitGenerator(reduplication);
        InitGenerator(radiationBurst);
        InitGenerator(fracture);
        InitGenerator(cobwebSpline);
        InitGenerator(zigzagParallel);
        InitGenerator(wavyParallel);
        InitGenerator(hatching);
        InitGenerator(fluidTendril);
        InitGenerator(amorphousSpeck);
        InitGenerator(bilateralDuplication);
        InitGenerator(speckCluster);
        InitGenerator(organicCluster);

        _generating = true;

        Debug.Log("=== PRISM Quality Derivation ===");
        Debug.Log($"Space: {Space:F3} (0=Intimate, 1=Vast)");
        Debug.Log($"Light: {Light:F3} (0=Dark, 1=Bright)");
        Debug.Log($"Form: {Form:F3} (0=Smooth, 1=Angular)");
        Debug.Log($"Colour: {Colour:F3} (0=Cool, 1=Warm)");
        Debug.Log($"Motion: {Motion:F3} (0=Slow, 1=Fast)");
        Debug.Log($"Scale: {Scale:F3} (0=Uniform, 1=Contrast)");
        Debug.Log($"Primary: #{ColorUtility.ToHtmlStringRGB(PrimaryColour)}");
        Debug.Log($"Secondary: #{ColorUtility.ToHtmlStringRGB(SecondaryColour)}");
    }

    //Calls Init on a generator if it is assigned//
    private void InitGenerator(MonoBehaviour generator)
    {
        if (generator == null) return;
        var method = generator.GetType().GetMethod("Init");
        method?.Invoke(generator, new object[] { TimbralProfile });
    }

    private void DeriveQualities()
    {
        float peakEnergy = _analyser.PeakEnergy;
        float averageEnergy = _analyser.AverageEnergy;
        float bpm = _analyser.EstimatedTempo;

        float low = Average(_analyser.LowEnergyOverTime);
        float mid = Average(_analyser.MidEnergyOverTime);
        float high = Average(_analyser.HighEnergyOverTime);
        float total = Mathf.Max(low + mid + high, 0.001f);

        float lowRatio = low / total;
        float midRatio = mid / total;
        float highRatio = high / total;

        float dynamicRange = peakEnergy - averageEnergy;
        float normalizedDynamic = Mathf.Clamp01(dynamicRange / Mathf.Max(peakEnergy, 0.001f));

        Space = normalizedDynamic;
        Light = Mathf.Clamp01((averageEnergy * 20f) * 0.5f + highRatio * 0.5f);
        Form = highRatio;
        Colour = lowRatio;
        Motion = Mathf.Clamp01(Mathf.InverseLerp(60f, 180f, bpm));
        Scale = normalizedDynamic;
    }

    private void DeriveColours()
    {
        float rawLow = _analyser.RawLowAverage;
        float rawMid = _analyser.RawMidAverage;
        float rawHigh = _analyser.RawHighAverage;
        float rawTotal = Mathf.Max(rawLow + rawMid + rawHigh, 0.001f);

        float lowRatio = rawLow / rawTotal;
        float midRatio = rawMid / rawTotal;
        float highRatio = rawHigh / rawTotal;

        float wL = lowRatio * 1.0f;
        float wM = midRatio * 2.5f;
        float wH = highRatio * 4.0f;
        float wT = Mathf.Max(wL + wM + wH, 0.001f);

        wL /= wT; wM /= wT; wH /= wT;

        float primaryHue = wL * 0.04f + wM * 0.35f + wH * 0.72f;
        float maxW = Mathf.Max(wL, wM, wH);
        float dominance = Mathf.Clamp01((maxW - 0.33f) / 0.67f);
        float primarySat = Mathf.Lerp(0.25f, 1f, dominance);
        float primaryBright = Mathf.Lerp(0.5f, 1f, Light);

        PrimaryColour = Color.HSVToRGB(primaryHue, primarySat, primaryBright);

        float secOffset = Mathf.Lerp(0.3f, 0.45f, Motion);
        SecondaryColour = Color.HSVToRGB(Mathf.Repeat(primaryHue + secOffset, 1f), primarySat * 0.65f, primaryBright * Mathf.Lerp(0.55f, 0.8f, Space));
    }

    private void Update()
    {
        if (!_generating) return;
        if (_audioSource == null || !_audioSource.isPlaying) return;

        //Sample stereo spectrum//
        _audioSource.GetSpectrumData(_spectrumLeft, 0, FFTWindow.BlackmanHarris);
        _audioSource.GetSpectrumData(_spectrumRight, 1, FFTWindow.BlackmanHarris);

        //Process each channel//
        ProcessChannel(_spectrumLeft, out float bassL, out float midL, out float highL, out float energyL);
        RealtimeBassLeft = bassL;
        RealtimeMidLeft = midL;
        RealtimeHighLeft = highL;
        RealtimeEnergyLeft = energyL;

        ProcessChannel(_spectrumRight, out float bassR, out float midR, out float highR, out float energyR);
        RealtimeBassRight = bassR;
        RealtimeMidRight = midR;
        RealtimeHighRight = highR;
        RealtimeEnergyRight = energyR;

        //Mono averages//
        RealtimeBass = (bassL + bassR) * 0.5f;
        RealtimeMid = (midL + midR) * 0.5f;
        RealtimeHigh = (highL + highR) * 0.5f;
        RealtimeEnergy = (energyL + energyR) * 0.5f;

        //Update realtime colour//
        UpdateRealtimeColour();

        // Update timbral profile every frame — this is what makes mid-song timbral shifts (e.g. folk to metal transition) instantly reflected in generator weights//
        TimbralProfile.UpdateRealtime(_spectrumLeft, _spectrumRight, Time.deltaTime);

        //Update atmosphere layers//
        if (postProcessingLayer != null) postProcessingLayer.UpdateLayer(this);
        if (sdfLayer != null) sdfLayer.UpdateLayer(this);

        //Update all generators — each reads TimbralProfile weights to decide whether to spawn marks, how many, and what shape//
        UpdateGenerator(concentricRings);
        UpdateGenerator(tunnelDepth);
        UpdateGenerator(spiralGrowth);
        UpdateGenerator(rotationField);
        UpdateGenerator(drift);
        UpdateGenerator(honeycomb);
        UpdateGenerator(gridGrating);
        UpdateGenerator(filigree);
        UpdateGenerator(reduplication);
        UpdateGenerator(radiationBurst);
        UpdateGenerator(fracture);
        UpdateGenerator(cobwebSpline);
        UpdateGenerator(zigzagParallel);
        UpdateGenerator(wavyParallel);
        UpdateGenerator(hatching);
        UpdateGenerator(fluidTendril);
        UpdateGenerator(amorphousSpeck);
        UpdateGenerator(bilateralDuplication);
        UpdateGenerator(speckCluster);
        UpdateGenerator(organicCluster);
    }

    private void UpdateGenerator(MonoBehaviour generator)
    {
        if (generator == null) return;
        var method = generator.GetType().GetMethod("UpdateGenerator");
        method?.Invoke(generator, new object[] { TimbralProfile });
    }

    private void ProcessChannel(float[] spectrum, out float bass, out float mid, out float high, out float energy)
    {
        float bassSum = 0f, midSum = 0f, highSum = 0f, totalSum = 0f;
        int bassEnd = Mathf.RoundToInt(spectrum.Length * 0.1f);
        int midEnd = Mathf.RoundToInt(spectrum.Length * 0.5f);

        for (int i = 0; i < spectrum.Length; i++)
        {
            totalSum += spectrum[i];
            if (i < bassEnd) bassSum += spectrum[i];
            else if (i < midEnd) midSum += spectrum[i];
            else highSum += spectrum[i];
        }

        bass = bassSum / Mathf.Max(bassEnd, 1);
        mid = midSum / Mathf.Max(midEnd - bassEnd, 1);
        high = highSum / Mathf.Max(spectrum.Length - midEnd, 1);
        energy = totalSum / spectrum.Length;
    }

    private void UpdateRealtimeColour()
    {
        float rtTotal = Mathf.Max(RealtimeBass + RealtimeMid + RealtimeHigh, 0.001f);
        float rtLow = RealtimeBass / rtTotal;
        float rtMid = RealtimeMid / rtTotal;
        float rtHigh = RealtimeHigh / rtTotal;

        float wL = rtLow * 1.0f, wM = rtMid * 2.5f, wH = rtHigh * 4.0f;
        float wT = Mathf.Max(wL + wM + wH, 0.001f);

        float hue = (wL / wT) * 0.04f + (wM / wT) * 0.35f + (wH / wT) * 0.72f;
        float rtMax = Mathf.Max(wL / wT, wM / wT, wH / wT);
        float sat = Mathf.Lerp(0.3f, 1f, Mathf.Clamp01((rtMax - 0.33f) / 0.67f));
        float bright = Mathf.Clamp01(Mathf.Lerp(0.4f, 1f, RealtimeEnergy * 50f));

        RealtimeColour = Color.Lerp(RealtimeColour, Color.HSVToRGB(hue, sat, bright), Time.deltaTime * 6f);
    }

    private float Average(float[] values)
    {
        if (values == null || values.Length == 0) return 0;
        float sum = 0;
        foreach (float v in values) sum += v;
        return sum / values.Length;
    }

    public void Stop() => _generating = false;
}


