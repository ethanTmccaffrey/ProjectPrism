using UnityEngine;

//PRISMGenerator: the coordinator.
//
//Owns the TimbralProfile, samples it once per frame at the current playback position,
//ranks the generators by weight to decide which is dominant, and drives every generator
//and atmosphere layer.
//
//ARCHITECTURE NOTE - this class no longer performs any audio analysis.
//
//It used to call GetSpectrumData() every frame and hand a raw FFT buffer to
//TimbralProfile, which recomputed every measurement from that single frame. That is
//gone. Analysis now happens once, offline, before playback (see AudioAnalyser), and
//Update() simply asks "what is the music doing at time T" - a lookup into a pre-computed
//timeline rather than a per-frame computation.
//
//The generators are unaffected by this change. They read profile.WeightFracture,
//profile.RealtimeEnergy, _prism.RealtimeColour, exactly as before. Putting TimbralProfile
//between the audio and the generators is what made the measurement layer replaceable
//without touching the twelve things that depend on it.

public class PRISMGenerator : MonoBehaviour
{
    //Atmosphere layers: always present//
    [Header("Atmosphere Layers")]
    [SerializeField] private PostProcessingLayer postProcessingLayer;
    [SerializeField] private SDFLayer sdfLayer;

    //Generator references. Each reads TimbralProfile weights to decide whether to activate//
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
    [SerializeField] private Vector3 leftSourcePoint = new Vector3(-80f, 0f, 0f);
    [SerializeField] private Vector3 rightSourcePoint = new Vector3(80f, 0f, 0f);
    public Vector3 LeftSourcePoint => leftSourcePoint;
    public Vector3 RightSourcePoint => rightSourcePoint;

    //Six derived qualities: track-level character, computed once at Init//
    [Header("Derived Qualities (Read Only)")]
    public float Space { get; private set; }   //0 = Intimate, 1 = Vast//
    public float Light { get; private set; }   //0 = Dark, 1 = Bright//
    public float Form { get; private set; }    //0 = Smooth, 1 = Angular//
    public float Colour { get; private set; }  //0 = Cool hue, 1 = Warm hue//
    public float Motion { get; private set; }  //0 = Slow, 1 = Fast//
    public float Scale { get; private set; }   //0 = Uniform, 1 = Extreme Contrast//

    //Derived colours//
    public Color PrimaryColour { get; private set; }
    public Color SecondaryColour { get; private set; }
    public Color RealtimeColour { get; private set; }

    [SerializeField, Range(0f, 0.5f)] private float hueTextureShift = 0.3f;

    //The timbral profile: sampled every frame, read by all generators//
    public TimbralProfile TimbralProfile { get; private set; }

    //Prominence coordination//
    [SerializeField] private float activeFloor = 0.05f;
    [SerializeField] private float prominenceSmoothing = 1.5f;
    private GeneratorID _dominantId = GeneratorID.Count;
    private float _dominantWeight = 0f;
    private float[] _smoothWeights = new float[(int)GeneratorID.Count];
    private readonly bool[] _registered = new bool[(int)GeneratorID.Count];

    [Header("Debug")]
    [SerializeField] private bool logWeights = false;
    private float _debugTimer = 0f;

    //Audio//
    private AudioAnalyser _analyser;
    private AudioSource _audioSource;
    private bool _generating = false;

    public float TrackLength => _audioSource != null && _audioSource.clip != null
                                ? _audioSource.clip.length : 0f;
    public float PlaybackTime => _audioSource != null ? _audioSource.time : 0f;

    //Band levels, read from the analysed timeline rather than a live FFT.//
    //Kept because the atmosphere layers read them.//
    public float RealtimeEnergy { get; private set; }
    public float RealtimeBass { get; private set; }
    public float RealtimeMid { get; private set; }
    public float RealtimeHigh { get; private set; }

    public void RegisterGenerator(GeneratorID id)
    {
        _registered[(int)id] = true;
    }

    public void Init(AudioAnalyser analyser, AudioSource audioSource)
    {
        _analyser = analyser;
        _audioSource = audioSource;

        //Inherit the profile already loaded by AudioAnalyser//
        TimbralProfile = analyser.TimbralProfile;

        if (TimbralProfile == null || !TimbralProfile.Loaded)
        {
            Debug.LogError("PRISM: Init called with no loaded analysis - aborting");
            return;
        }

        //Sample frame zero so the derived qualities and colours have real values to read//
        TimbralProfile.SampleAt(0f);

        DeriveQualities();
        DeriveColours();

        //Seed the realtime colour so the first marks are not black//
        RealtimeColour = PrimaryColour;

        if (postProcessingLayer != null) postProcessingLayer.Init(this);
        if (sdfLayer != null) sdfLayer.Init(this);

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
        Debug.Log($"Space:  {Space:F3} (0=Intimate, 1=Vast)");
        Debug.Log($"Light:  {Light:F3} (0=Dark, 1=Bright)");
        Debug.Log($"Form:   {Form:F3} (0=Smooth, 1=Angular)");
        Debug.Log($"Colour: {Colour:F3} (0=Cool, 1=Warm)");
        Debug.Log($"Motion: {Motion:F3} (0=Slow, 1=Fast)");
        Debug.Log($"Scale:  {Scale:F3} (0=Uniform, 1=Contrast)");
        Debug.Log($"Primary:   #{ColorUtility.ToHtmlStringRGB(PrimaryColour)}");
        Debug.Log($"Secondary: #{ColorUtility.ToHtmlStringRGB(SecondaryColour)}");
    }

    private void InitGenerator(MonoBehaviour generator)
    {
        if (generator == null) return;

        var setPrism = generator.GetType().GetMethod("SetPrism");
        setPrism?.Invoke(generator, new object[] { this });

        var method = generator.GetType().GetMethod("Init");
        method?.Invoke(generator, new object[] { TimbralProfile });
    }

    //The six derived qualities, now sourced from the analysed static profile.
    //
    //Previously these were computed from the analyser's own band arrays and RMS energy.
    //Those measures are gone; these are their honest replacements, drawn from measures
    //that were validated against tracks of known character.
    private void DeriveQualities()
    {
        TimbralProfile p = TimbralProfile;

        //Vastness comes from dynamic range: music that swings between whisper and roar
        //feels large; a compressed wall feels close and intimate.
        Space = p.StaticDynamicRange;

        //Brightness is centroid, directly.
        Light = p.StaticCentroid;

        //Angularity is percussiveness: attack-driven music reads as hard-edged, sustained
        //music as smooth. This replaces the old high-frequency ratio, which conflated
        //"bright" with "sharp".
        Form = p.StaticPercussiveness;

        //Warmth is the inverse of brightness: energy low in the spectrum reads warm.
        Colour = 1f - p.StaticCentroid;

        Motion = Mathf.Clamp01(Mathf.InverseLerp(60f, 180f, p.StaticTempo));

        Scale = p.StaticDynamicRange;
    }

    private void DeriveColours()
    {
        //Band balance at the start of the track, from the analysed timeline.//
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
        SecondaryColour = Color.HSVToRGB(
            Mathf.Repeat(primaryHue + secOffset, 1f),
            primarySat * 0.65f,
            primaryBright * Mathf.Lerp(0.55f, 0.8f, Space));
    }

    private void Update()
    {
        if (!_generating) return;
        if (_audioSource == null || !_audioSource.isPlaying) return;
        if (TimbralProfile == null || !TimbralProfile.Loaded) return;

        //THE CORE CHANGE: sample the pre-computed timeline at the current playback
        //position. No FFT, no spectrum buffer, no per-frame measurement. Everything the
        //generators need was computed properly, offline, over the whole track.
        TimbralProfile.SampleAt(_audioSource.time);

        //Mirror the band values for the atmosphere layers//
        RealtimeEnergy = TimbralProfile.RealtimeEnergy;
        RealtimeBass = TimbralProfile.RealtimeBandLow;
        RealtimeMid = TimbralProfile.RealtimeBandMid;
        RealtimeHigh = TimbralProfile.RealtimeBandHigh;

        UpdateRealtimeColour();

        //Rank generators once per frame so every GetProminence() call reads the same result//
        RankProminence();

        if (logWeights)
        {
            _debugTimer += Time.deltaTime;
            if (_debugTimer >= 1f)
            {
                _debugTimer = 0f;
                var p = TimbralProfile;
                //Debug.Log($"[PRISM t={_audioSource.time:F1}] " +
                //          $"F={p.RealtimeFlatness:F2} C={p.RealtimeCentroid:F2} " +
                //          $"H={p.RealtimeHarmonicComplexity:F2} " +
                //          $"E={p.RealtimeEnergy:F2} P={p.RealtimePercussiveRatio:F2} " +
                //          $"D={p.RealtimeOnsetDensity:F2} R={p.RealtimeRhythmicRegularity:F2} " +
                //          $"| dominant={_dominantId} ({_dominantWeight:F2})");
            }
        }

        if (postProcessingLayer != null) postProcessingLayer.UpdateLayer(this);
        if (sdfLayer != null) sdfLayer.UpdateLayer(this);

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

    //Prominence coordination//
    //One pass over 20 floats per frame, cached so every generator reads the same ranking.//
    private void RankProminence()
    {
        float[] w = TimbralProfile.Weights;
        float s = Time.deltaTime * prominenceSmoothing;

        _dominantId = GeneratorID.Count;
        _dominantWeight = 0f;

        for (int i = 0; i < w.Length; i++)
        {
            //Smooth every weight toward its raw value - this filters out single-frame blips//
            _smoothWeights[i] = Mathf.Lerp(_smoothWeights[i], w[i], s);

            if (!_registered[i]) continue;
            if (_smoothWeights[i] > _dominantWeight)
            {
                _dominantWeight = _smoothWeights[i];
                _dominantId = (GeneratorID)i;
            }
        }
    }

    public Prominence GetProminence(GeneratorID id)
    {
        if (!_registered[(int)id]) return Prominence.Silent;

        float myWeight = _smoothWeights[(int)id];

        if (myWeight < activeFloor || _dominantWeight < activeFloor)
            return Prominence.Silent;

        bool dominant = (id == _dominantId);

        //Share of the dominant weight: 1 for the leader, less for everyone else.//
        //A strong supporter sits near centre, a faint trace sits far out.//
        float share = Mathf.Clamp01(myWeight / _dominantWeight);

        return new Prominence
        {
            isDominant = dominant,
            centrality = dominant ? 1f : share,
            //Never fully zero while active - even a trace should be visible//
            prominence = Mathf.Lerp(0.2f, 1f, share)
        };
    }

    private void UpdateRealtimeColour()
    {
        TimbralProfile p = TimbralProfile;

        //Absolute centroid mapping: the same brightness always produces the same hue, so
        //two different songs are directly comparable by colour. Relative (per-track
        //normalised) mapping was tried and abandoned - it made every song span the full
        //palette, which is exactly the convergence PRISM exists to avoid.
        float t = Mathf.InverseLerp(0.25f, 0.75f, p.RealtimeCentroid);
        float baseHue = Mathf.Lerp(0.02f, 0.75f, t);

        //Flatness as a texture-shift axis: noisier passages push the hue off the base
        //mapping, so a gritty moment and a clean moment at the same brightness are
        //distinguishable.
        float flat = p.RealtimeFlatness;
        float hue = Mathf.Repeat(baseHue + (flat - 0.5f) * hueTextureShift, 1f);

        float sat = Mathf.Lerp(1f, 0.35f, flat);
        float bright = Mathf.Clamp01(Mathf.Lerp(0.55f, 1f, p.RealtimeEnergy));

        RealtimeColour = Color.Lerp(RealtimeColour,
                                    Color.HSVToRGB(hue, sat, bright),
                                    Time.deltaTime * 6f);
    }

    public void Stop() => _generating = false;
}


