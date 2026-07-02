using UnityEngine;

public class PRISMGenerator : MonoBehaviour
{
    //Layer References//
    [Header("Visual Layers")]
    [SerializeField] private ParticleLayer particleLayer;
    [SerializeField] private MeshVolumeLayer meshVolumeLayer;
    [SerializeField] private PostProcessingLayer postProcessingLayer;
    [SerializeField] private SDFLayer sdfLayer;

    //Six dervied qualities//
    [Header("Derived Qualities (Read Only)")]
    public float Space { get; private set; } //0 = Intimate, 1 = Vast//
    public float Light { get; private set; } //0 = Cool/Dark, 1 = Warm/Bright//
    public float Form {  get; private set; } //0 = Smooth, 1 = Angular//
    public float Colour { get; private set; } //0 = Cool hue, 1 = Warm hue//
    public float Motion { get; private set; } //0 = Slow, 1 = Fast//
    public float Scale { get; private set; } //0 = Uniform, 1 = Extreme Contrast//

    //Derived colours, computed once from raw frequency analysis//
    //All layers use these instead of computing their own//
    public Color PrimaryColour { get; private set; }
    public Color SecondaryColour { get; private set; }

    //Real time colour - shifts each frame with the current frequency balance//
    //Used by burst particles and any layer that needs moment to moment colour response//
    public Color RealtimeColour { get; private set; }

    //Audio Data//
    private AudioAnalyser _analyser;
    private AudioSource _audioSource;
    private bool _generating = false;

    //Real-time spectrum//
    private float[] _spectrum = new float[256];
    public float RealtimeEnergy { get; private set; }
    public float RealtimeBass { get; private set; } 
    public float RealtimeMid { get; private set; }
    public float RealtimeHigh { get; private set; }
    public float[] Spectrum => _spectrum;

    public void Init(AudioAnalyser analyser, AudioSource audioSource)
    {
        _analyser = analyser;
        _audioSource = audioSource;

        //Derive the six static qualities from full track analysis//
        DeriveQualities();
        DeriveColours();

        //Initialise all layers with the derived qualities//
        if(particleLayer != null) particleLayer.Init(this);
        if (meshVolumeLayer != null) meshVolumeLayer.Init(this);
        if(postProcessingLayer != null) postProcessingLayer.Init(this);
        if(sdfLayer != null) sdfLayer.Init(this);

        _generating = true;

        Debug.Log("=== PRSIM Quality Derivation ===");
        Debug.Log($"Space: {Space:F3} (0 = Intimate, 1 = Vast)");
        Debug.Log($"Light: {Light:F3} (0 = Cool/Dark, 1 = Warm/Bright)");
        Debug.Log($"Form: {Form:F3} (0 = Smooth, 1 = Angular)");
        Debug.Log($"Colour: {Colour:F3} (0 = Cool hue, 1 = Warm hue)");
        Debug.Log($"Motion: {Motion:F3} (0 = Slow, 1 = Fast)");
        Debug.Log($"Scale: {Scale:F3} (0 = Uniform, 1 = Extreme Contrast)");
        Debug.Log($"Primary: #{ColorUtility.ToHtmlStringRGB(PrimaryColour)}");
        Debug.Log($"Secondary: #{ColorUtility.ToHtmlStringRGB(SecondaryColour)}");
    }

    private void DeriveQualities()
    {
        float peakEnergy = _analyser.PeakEnergy;
        float averageEnergy = _analyser.AverageEnergy;
        float bpm = _analyser.EstimatedTempo;

        //Use perceptually scaled values for spatial qualities//
        //These are intentionally boosted to make energy differences more visible//

        float low = Average(_analyser.LowEnergyOverTime);
        float mid = Average(_analyser.MidEnergyOverTime);
        float high = Average(_analyser.HighEnergyOverTime);
        float total = Mathf.Max(low + mid + high, 0.001f);

        float lowRatio = low / total;
        float midRatio = mid / total;
        float highRatio = high / total;

        //Derive dynamic range: how much does energy vary across the track//
        float dynamicRange = peakEnergy - averageEnergy;
        float normalizedDynamic = Mathf.Clamp01(dynamicRange / Mathf.Max(peakEnergy, 0.001f));

        //6 quality derivations//
        Space = normalizedDynamic;
        Light = Mathf.Clamp01((averageEnergy * 20f) * 0.5f + highRatio * 0.5f);
        Form = highRatio;
        Colour = lowRatio;
        Motion = Mathf.Clamp01(Mathf.InverseLerp(60f, 180f, bpm));
        Scale = normalizedDynamic;
    }

    private void DeriveColours()
    {
        //Use RAW unscaled band averages for colour mapping//
        //This preserves the true frequency character of the track without the perceptual scailing inflating bass//
        float rawLow = _analyser.RawLowAverage;
        float rawMid = _analyser.RawMidAverage;
        float rawHigh = _analyser.RawHighAverage;
        float rawTotal = Mathf.Max(rawLow + rawMid + rawHigh, 0.001f);

        float lowRatio = rawLow / rawTotal;
        float midRatio = rawMid / rawTotal;
        float highRatio = rawHigh / rawTotal;

        //Apply gentle perceptual weighting AFTER ratio calculation//
        //High frequencies need a boost to have perceptual colour impact, but we weight after normalising so bass can't dominate//

        //Grounded in cross-modal correspondence: Cytowic (2002), Marks (1974)//
        //Low - warm/red/orange (hue 0.00–0.10)//
        //Mid - green/teal (hue 0.28–0.45)//
        //High - violet/indigo (hue 0.65–0.80)//

        float weightedLow = lowRatio * 1.0f;
        float weightedMid = midRatio * 2.5f; //Mid underrepresnted in raw data//
        float weightedHigh = highRatio * 4.0f; //High needs the most boost//

        float weightedTotal = Mathf.Max(weightedLow + weightedMid + weightedHigh, 0.001f);

        float wLow = weightedLow / weightedTotal;
        float wMid = weightedMid / weightedTotal;
        float wHigh = weightedHigh / weightedTotal;

        //Hue: weighted blend across the full colour wheel//
        float primaryHue = wLow * 0.04f + wMid * 0.35f + wHigh * 0.72f;

        //Saturation: how strongly does one band dominate//
        //A flat spectrum gives grey, a dominant band gives vivid colour//
        float maxW = Mathf.Max(wLow,wMid, wHigh);
        float dominance = Mathf.Clamp01((maxW - 0.33f) / 0.67f);
        float primarySat = Mathf.Lerp(0.25f, 1f, dominance);

        //Brightness: driven by Light quality (overall warmth/energy)//
        float primaryBright = Mathf.Lerp(0.5f, 1f, Light);

        PrimaryColour = Color.HSVToRGB(primaryHue, primarySat, primaryBright);

        //Secondary: Complementary-ish offset — rotated ~150 degrees around the hue wheel.//
        //This gives contrast without being a harsh exact complement (180 degrees).//
        //Energy level shifts whether it leans warm or cool of the primary.//
        float secondaryHueOffset = Mathf.Lerp(0.3f, 0.45f, Motion); // faster = wider split//
        float secondaryHue = Mathf.Repeat(primaryHue + secondaryHueOffset, 1f);
        //Secondary is slightly less saturated and darker — supporting role//
        float secondarySat = primarySat * 0.65f;
        float secondaryBright = primaryBright * Mathf.Lerp(0.55f, 0.8f, Space);

        SecondaryColour = Color.HSVToRGB(secondaryHue, secondarySat, secondaryBright);

        Debug.Log($"Raw Ratios — Low: {lowRatio:F3}  Mid: {midRatio:F3}  High: {highRatio:F3}");
        Debug.Log($"Weighted  — Low: {wLow:F3}  Mid: {wMid:F3}  High: {wHigh:F3}");
        Debug.Log($"Primary Hue: {primaryHue:F3}");
    }

    private void Update()
    {
        if (!_generating) return;
        if(_audioSource == null || !_audioSource.isPlaying) return;

        //Sample real-time spectrum data every frame//
        _audioSource.GetSpectrumData(_spectrum, 0, FFTWindow.BlackmanHarris);

        //Derive real-time energy values//
        float bassSum = 0f; 
        float midSum = 0f; 
        float highSum = 0f; 
        float totalSum = 0f;
        int bassEnd = Mathf.RoundToInt(_spectrum.Length * 0.1f);
        int midEnd = Mathf.RoundToInt(_spectrum.Length * 0.5f);

        for(int i = 0; i < _spectrum.Length; i++)
        {
            totalSum += _spectrum[i];
            if(i < bassEnd) bassSum += _spectrum[i];
            else if(i < midEnd) midSum += _spectrum[i];
            else highSum += _spectrum[i];
        }

        RealtimeEnergy = totalSum / _spectrum.Length;
        RealtimeBass = bassSum / Mathf.Max(bassEnd, 1);
        RealtimeMid = midSum / Mathf.Max(midEnd - bassEnd, 1);
        RealtimeHigh = highSum / Mathf.Max(_spectrum.Length - midEnd, 1);

        //Derive realtime colour from current frequency balance//
        //This shifts moment to moment as the music changes//
        UpdateRealtimeColour();

        //Update all layers each frame//
        if (particleLayer != null) particleLayer.UpdateLayer(this);
        if (meshVolumeLayer != null) meshVolumeLayer.UpdateLayer(this);
        if (postProcessingLayer != null) postProcessingLayer.UpdateLayer(this);
        if (sdfLayer != null) sdfLayer.UpdateLayer(this);
    }

    private void UpdateRealtimeColour()
    {
        //Same hue mapping as static derivation but applied to realtime bands//
        float rtTotal = Mathf.Max(RealtimeBass + RealtimeMid + RealtimeHigh, 0.001f);
        float rtLow = RealtimeBass / rtTotal;
        float rtMid = RealtimeMid / rtTotal;
        float rtHigh = RealtimeHigh / rtTotal;

        //Apply same perceptual weighting//
        float wL = rtLow * 1.0f;
        float wM = rtMid * 2.5f;
        float wH = rtHigh * 4.0f;
        float wT = Mathf.Max(wL + wM + wH, 0.001f);

        float realtimeHue = (wL / wT) * 0.04f + (wM / wT) * 0.35f + (wH / wT) * 0.72f;

        //Saturation and brightness from realtime energy//
        float rtMax = Mathf.Max(wL / wT, wM / wT, wH / wT);
        float rtDominance = Mathf.Clamp01((rtMax - 0.33f) / 0.67f);
        float rtSat = Mathf.Lerp(0.3f, 1f, rtDominance);
        float rtBright = Mathf.Lerp(0.4f, 1f, RealtimeEnergy * 50f);
        rtBright = Mathf.Clamp01(rtBright);


        //Smooth the realtime colour to avoid strobing//
        Color targetColour = Color.HSVToRGB(realtimeHue, rtSat, rtBright);
        RealtimeColour = Color.Lerp(RealtimeColour, targetColour, Time.deltaTime * 6f);
    }
    private float Average(float[] values)
    {
        if(values == null || values.Length == 0) return 0;
        float sum = 0;
        for(int i = 0; i < values.Length; i++) sum += values[i];
        return sum / values.Length;
    }

    public void Stop()
    {
        _generating = false;
    }
}

