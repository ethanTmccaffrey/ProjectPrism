using System;
using UnityEngine;

//TimbralProfile holds all acoustic measurements for the loaded track and derives the
//generator weights from them.
//
//ARCHITECTURE NOTE - this class no longer MEASURES anything.
//
//All acoustic analysis now happens offline, before playback, in prism_analyse.py using
//librosa (McFee et al. 2015). Unity loads the resulting JSON and plays it back in sync
//with the audio, so at runtime this class is a LOOKUP TABLE, not a DSP engine.
//
//Why the change: the previous realtime layer computed every measurement from a single
//1024-bin FFT frame - a photograph of one instant of sound. Validated against four
//tracks of known character, no frame-local spectral measure could separate them:
//flatness, inharmonicity, ZCR and centroid all had overlapping ranges on tracks that
//sound nothing alike. Freeze any dense mix at any instant and it looks the same:
//"loud, energy everywhere". The information that distinguishes songs is TEMPORAL -
//attack, articulation, regularity, whether the music breathes or never lets up - and it
//cannot be read from a single frame by construction.
//
//The measures that DID separate the tracks (rhythmic regularity, onset density,
//percussive ratio, dynamic range) are all computed over a WINDOW of time. That is the
//finding, and it is why analysis moved offline: given the whole track up front, these
//can be computed properly rather than approximated frame by frame.
//
//PRISM is a persistent canvas and the song is fully known before playback begins, so
//nothing about the architecture ever required live measurement.
//
//References:
//  McFee et al. (2015)      - librosa
//  Dubnov (2004)            - spectral flatness
//  Schubert et al. (2004)   - spectral centroid
//  Fitzgerald (2010)        - harmonic/percussive separation
//  Ellis (2007)             - beat tracking
//  Kluver (1926)            - form constants
//  Cytowic (2002), Marks (1974) - cross-modal correspondence

public class TimbralProfile
{
    //Static measurements: the character of the track as a whole//
    public float StaticFlatness { get; private set; }            //0 = tonal, 1 = noisy//
    public float StaticCentroid { get; private set; }            //0 = dark, 1 = bright//
    public float StaticHarmonicComplexity { get; private set; }  //0 = simple, 1 = complex//
    public float StaticPercussiveness { get; private set; }      //0 = sustained, 1 = percussive//
    public float StaticRhythmicRegularity { get; private set; }  //0 = irregular, 1 = metronomic//
    public float StaticStereoWidth { get; private set; }         //0 = mono, 1 = wide//
    public float StaticDynamicRange { get; private set; }        //0 = compressed, 1 = wide swings//
    public float StaticOnsetDensity { get; private set; }        //0 = sparse, 1 = relentless//
    public float StaticTempo { get; private set; }               //BPM//

    //Realtime measurements: sampled from the timeline at the current playback position//
    public float RealtimeFlatness { get; private set; }
    public float RealtimeCentroid { get; private set; }
    public float RealtimeHarmonicComplexity { get; private set; }
    public float RealtimeFlux { get; private set; }
    public float RealtimeStereoWidth { get; private set; }
    public float RealtimeEnergy { get; private set; }

    //The three temporal measures. These are the ones that actually distinguish tracks.//
    public float RealtimePercussiveRatio { get; private set; }    //HPSS: transient vs tonal//
    public float RealtimeOnsetDensity { get; private set; }       //attacks in the recent past//
    public float RealtimeRhythmicRegularity { get; private set; } //rolling beat autocorrelation//

    //Frequency band balance, used for colour derivation//
    public float RealtimeBandLow { get; private set; }
    public float RealtimeBandMid { get; private set; }
    public float RealtimeBandHigh { get; private set; }

    //Raw flux for anything still doing its own onset detection. Prefer IsBeat()/IsOnset()//
    public float RealtimeFluxRaw => RealtimeFlux;

    //True on the frame a beat/onset occurs. Replaces every generator's private adaptive//
    //onset detector: they each kept their own flux history, their own threshold and their//
    //own refractory period, and they all disagreed with each other. One source of truth.//
    public bool BeatThisFrame { get; private set; }
    public bool OnsetThisFrame { get; private set; }

    //ZCR: REMOVED.
    //Tested as a grit/roughness measure and rejected with evidence. ZCR counts sign
    //changes in the waveform, so it reports "noisy", not "distorted" - and an orchestra
    //is acoustically very noisy (dozens of slightly detuned players, bow noise, hall
    //reverb). Measured across four tracks it ranked Sogno di Volare (orchestral) as the
    //ROUGHEST of the set, above Duality (metal). Energy-gating reduced but did not fix
    //this, because the problem is not quiet frames - loud massed strings genuinely have a
    //high zero-crossing rate.
    //
    //Spectral flatness and spectral inharmonicity were tested for the same role and also
    //rejected: on Duality vs Sandstorm their ranges overlapped almost completely (raw
    //inharmonicity ratio 0.19-0.44 vs 0.22-0.33). A detuned supersaw stack fills the
    //valleys between its partials just as thoroughly as guitar distortion does.
    //
    //Three measures, three failures, one conclusion: no frame-local spectral statistic
    //isolates distortion from dense synthesis. The angular/jagged quality ZCR was serving
    //is better carried by RealtimePercussiveRatio (HPSS-derived, ranks Duality highest and
    //Sogno lowest - the correct order) combined with rhythmic irregularity.

    //Generator weights, recomputed every frame from the sampled measurements//
    public float[] Weights { get; private set; } = new float[(int)GeneratorID.Count];

    //Category 1: Tunnels and Funnels//
    public float WeightConcentricRings { get; private set; }
    public float WeightTunnelDepth { get; private set; }

    //Category 2: Spirals//
    public float WeightSpiralGrowth { get; private set; }
    public float WeightRotationField { get; private set; }
    public float WeightDrift { get; private set; }

    //Category 3: Lattices and Honeycombs//
    public float WeightHoneycomb { get; private set; }
    public float WeightGridGrating { get; private set; }
    public float WeightFiligree { get; private set; }
    public float WeightReduplication { get; private set; }

    //Category 4: Cobwebs and Radial Forms//
    public float WeightRadiationBurst { get; private set; }
    public float WeightFracture { get; private set; }
    public float WeightCobwebSpline { get; private set; }

    //Category 5: Parallel Figures//
    public float WeightZigzagParallel { get; private set; }
    public float WeightWavyParallel { get; private set; }
    public float WeightHatching { get; private set; }

    //Category 6: Wavy Lines and Amorphous Forms//
    public float WeightFluidTendril { get; private set; }
    public float WeightAmorphousSpeck { get; private set; }
    public float WeightBilateralDuplication { get; private set; }

    //Category 7: Small Circular Figures//
    public float WeightSpeckCluster { get; private set; }
    public float WeightOrganicCluster { get; private set; }

    //The loaded timeline//
    private AnalysisData _data;
    private float _frameRate;
    private int _frameCount;
    public bool Loaded { get; private set; } = false;
    public float Duration => _data != null ? _data.source.duration : 0f;

    //Beat/onset playheads: both lists are sorted, so we walk them forward rather than//
    //searching. O(1) per frame.//
    private int _beatCursor = 0;
    private int _onsetCursor = 0;
    private float _lastSampleTime = 0f;

    //Loading//

    public bool LoadFromJson(string json)
    {
        try
        {
            _data = JsonUtility.FromJson<AnalysisData>(json);
        }
        catch (Exception e)
        {
            Debug.LogError("PRISM TimbralProfile: failed to parse analysis JSON - " + e.Message);
            return false;
        }

        if (_data == null || _data.frames == null || _data.frames.energy == null || _data.frames.energy.Length == 0)
        {
            Debug.LogError("PRISM TimbralProfile: analysis JSON is empty or malformed");
            return false;
        }

        _frameRate = _data.source.frame_rate;
        _frameCount = _data.frames.energy.Length;

        StaticFlatness = _data.stat.flatness;
        StaticCentroid = _data.stat.centroid;
        StaticHarmonicComplexity = _data.stat.harmonic_complexity;
        StaticPercussiveness = _data.stat.percussiveness;
        StaticRhythmicRegularity = _data.stat.rhythmic_regularity;
        StaticStereoWidth = _data.stat.stereo_width;
        StaticDynamicRange = _data.stat.dynamic_range;
        StaticOnsetDensity = _data.stat.onset_density;
        StaticTempo = _data.stat.tempo;

        _beatCursor = 0;
        _onsetCursor = 0;
        _lastSampleTime = 0f;
        Loaded = true;

        Debug.Log("=== PRISM Timbral Profile (Static) ===");
        Debug.Log($"Duration: {_data.source.duration:F1}s  ({_frameCount} frames @ {_frameRate:F1}/s)");
        Debug.Log($"Tempo: {StaticTempo:F1} BPM");
        Debug.Log($"Flatness: {StaticFlatness:F3}  (0=tonal, 1=noisy)");
        Debug.Log($"Centroid: {StaticCentroid:F3}  (0=dark, 1=bright)");
        Debug.Log($"Harmonic Complexity: {StaticHarmonicComplexity:F3}");
        Debug.Log($"Percussiveness: {StaticPercussiveness:F3}  (0=sustained, 1=percussive)");
        Debug.Log($"Rhythmic Regularity: {StaticRhythmicRegularity:F3}  (0=irregular, 1=metronomic)");
        Debug.Log($"Onset Density: {StaticOnsetDensity:F3}  (0=sparse, 1=relentless)");
        Debug.Log($"Dynamic Range: {StaticDynamicRange:F3}  (0=compressed, 1=wide)");
        Debug.Log($"Stereo Width: {StaticStereoWidth:F3}  (0=mono, 1=wide)");
        Debug.Log($"Beats: {(_data.beats != null ? _data.beats.Length : 0)}  " +
                  $"Onsets: {(_data.onsets != null ? _data.onsets.Length : 0)}");

        return true;
    }

    //Sampling//

    //Called once per frame by PRISMGenerator with the current playback position.//
    //Reads the timeline, populates the Realtime* fields, recomputes all weights.//
    public void SampleAt(float time)
    {
        if (!Loaded) return;

        //Linear interpolation between frames, so a 60fps render reading an 86fps timeline//
        //gets a smooth value rather than stepping.//
        float exact = time * _frameRate;
        int i = Mathf.Clamp(Mathf.FloorToInt(exact), 0, _frameCount - 1);
        int j = Mathf.Min(i + 1, _frameCount - 1);
        float t = Mathf.Clamp01(exact - i);

        Frames f = _data.frames;

        RealtimeEnergy = Lerp(f.energy, i, j, t);
        RealtimeFlatness = Lerp(f.flatness, i, j, t);
        RealtimeCentroid = Lerp(f.centroid, i, j, t);
        RealtimeHarmonicComplexity = Lerp(f.harmonic_complexity, i, j, t);
        RealtimeFlux = Lerp(f.flux, i, j, t);
        RealtimeStereoWidth = Lerp(f.stereo_width, i, j, t);
        RealtimePercussiveRatio = Lerp(f.percussive_ratio, i, j, t);
        RealtimeOnsetDensity = Lerp(f.onset_density, i, j, t);
        RealtimeRhythmicRegularity = Lerp(f.rhythmic_regularity, i, j, t);
        RealtimeBandLow = Lerp(f.band_low, i, j, t);
        RealtimeBandMid = Lerp(f.band_mid, i, j, t);
        RealtimeBandHigh = Lerp(f.band_high, i, j, t);

        //Beat and onset flags: true if an event falls between the last sample and now.//
        BeatThisFrame = Advance(_data.beats, ref _beatCursor, _lastSampleTime, time);
        OnsetThisFrame = Advance(_data.onsets, ref _onsetCursor, _lastSampleTime, time);
        _lastSampleTime = time;

        UpdateWeights();
    }

    //Call if playback is scrubbed or restarted, so the beat cursors don't get stranded.//
    public void ResetPlayhead(float time = 0f)
    {
        _beatCursor = 0;
        _onsetCursor = 0;
        if (_data != null)
        {
            while (_data.beats != null && _beatCursor < _data.beats.Length && _data.beats[_beatCursor] < time) _beatCursor++;
            while (_data.onsets != null && _onsetCursor < _data.onsets.Length && _data.onsets[_onsetCursor] < time) _onsetCursor++;
        }
        _lastSampleTime = time;
    }

    private static float Lerp(float[] a, int i, int j, float t)
    {
        if (a == null || a.Length == 0) return 0f;
        i = Mathf.Clamp(i, 0, a.Length - 1);
        j = Mathf.Clamp(j, 0, a.Length - 1);
        return Mathf.Lerp(a[i], a[j], t);
    }

    //Walks a sorted timestamp list forward, returning true if any event fell in (from, to].//
    private static bool Advance(float[] times, ref int cursor, float from, float to)
    {
        if (times == null || cursor >= times.Length) return false;
        if (to < from) return false; //looped or scrubbed backwards//

        bool hit = false;
        while (cursor < times.Length && times[cursor] <= to)
        {
            if (times[cursor] > from) hit = true;
            cursor++;
        }
        return hit;
    }

    //Weight Derivation//

    //Each weight maps the measurements to a 0-1 activation strength for one generator.
    //Grounded in Kluver's form constants and cross-modal correspondence research.
    private void UpdateWeights()
    {
        float F = RealtimeFlatness;              //noisy / dense//
        float C = RealtimeCentroid;              //bright//
        float H = RealtimeHarmonicComplexity;    //harmonically complex//
        float X = RealtimeFlux;                  //spectral change//
        float S = RealtimeStereoWidth;           //wide//
        float E = RealtimeEnergy;                //loud//
        float P = RealtimePercussiveRatio;       //transient / attack-driven//
        float D = RealtimeOnsetDensity;          //relentless//
        float R = RealtimeRhythmicRegularity;    //metronomic//

        float tF = 1f - F;   //tonal//
        float tC = 1f - C;   //dark//
        float tP = 1f - P;   //sustained//
        float tD = 1f - D;   //sparse, breathing//
        float tR = 1f - R;   //irregular//

        //Category 1: Tunnels and Funnels//
        //Sustained, tonal, dynamic - orchestral, ambient, drone//
        WeightConcentricRings = Sat(tP * tF * E);
        WeightTunnelDepth = Sat(tP * tF * tD * (1f - E + 0.2f));

        //Category 2: Spirals//
        //Harmonic movement and momentum - jazz, classical, complex music//
        WeightSpiralGrowth = Sat(H * tF * tP);
        WeightRotationField = Sat(H * R * Mathf.Abs(0.5f - F));
        WeightDrift = Sat(tP * tD * (1f - E + 0.3f));

        //Category 3: Lattices and Honeycombs//
        //Regular, synthetic, gridded - electronic, EDM//
        //Honeycomb wants the CLEAN synthetic wall: regular, dense, relentless.//
        WeightHoneycomb = Sat(R * F * D);
        //GridGrating is the architectural sibling: regular AND percussive - the beat//
        //literally builds the structure.//
        WeightGridGrating = Sat(R * P * Mathf.Lerp(0.6f, 1f, C));
        WeightFiligree = Sat(H * Mathf.Lerp(0f, 1f, F * 0.5f + 0.2f));
        WeightReduplication = Sat(R * R * tF);

        //Category 4: Cobwebs and Radial Forms//
        //Fracture is aggression: it HITS HARD at UNPREDICTABLE intervals. Percussive but
        //irregular. That is Duality (P=0.46, R=0.18) and explicitly NOT Sandstorm
        //(P=0.40, R=0.72), which hits just as hard but on a perfect grid - that belongs
        //to GridGrating. This is the carve-up that flatness, inharmonicity and ZCR all
        //failed to make: the difference is not tone colour, it is whether the music is
        //predictable.
        WeightFracture = Sat(P * tR);
        WeightRadiationBurst = Sat(P * tR * E);
        WeightCobwebSpline = Sat(H * Mathf.Sqrt(F) * tP);

        //Category 5: Parallel Figures//
        WeightZigzagParallel = Sat(P * R * Mathf.Sqrt(F));
        WeightWavyParallel = Sat(tF * tP * tD);
        WeightHatching = Sat(tF * P * R);

        //Category 6: Wavy Lines and Amorphous Forms//
        //Organic, flowing, sustained - folk, soul, strings//
        WeightFluidTendril = Sat(tF * tP);
        WeightAmorphousSpeck = Sat(tR * tF * E);
        WeightBilateralDuplication = Sat(S * E);

        //Category 7: Small Circular Figures//
        //The negative space of the system: populates the quiet gaps of any song//
        WeightSpeckCluster = Sat((1f - E) * (1f - E));
        WeightOrganicCluster = Sat(tF * tP * (1f - E + 0.1f));

        //Mirror into the indexable array. Order MUST match the GeneratorID enum.//
        Weights[(int)GeneratorID.ConcentricRings] = WeightConcentricRings;
        Weights[(int)GeneratorID.TunnelDepth] = WeightTunnelDepth;
        Weights[(int)GeneratorID.SpiralGrowth] = WeightSpiralGrowth;
        Weights[(int)GeneratorID.RotationField] = WeightRotationField;
        Weights[(int)GeneratorID.Drift] = WeightDrift;
        Weights[(int)GeneratorID.Honeycomb] = WeightHoneycomb;
        Weights[(int)GeneratorID.GridGrating] = WeightGridGrating;
        Weights[(int)GeneratorID.Filigree] = WeightFiligree;
        Weights[(int)GeneratorID.Reduplication] = WeightReduplication;
        Weights[(int)GeneratorID.RadiationBurst] = WeightRadiationBurst;
        Weights[(int)GeneratorID.Fracture] = WeightFracture;
        Weights[(int)GeneratorID.CobwebSpline] = WeightCobwebSpline;
        Weights[(int)GeneratorID.ZigzagParallel] = WeightZigzagParallel;
        Weights[(int)GeneratorID.WavyParallel] = WeightWavyParallel;
        Weights[(int)GeneratorID.Hatching] = WeightHatching;
        Weights[(int)GeneratorID.FluidTendril] = WeightFluidTendril;
        Weights[(int)GeneratorID.AmorphousSpeck] = WeightAmorphousSpeck;
        Weights[(int)GeneratorID.BilateralDuplication] = WeightBilateralDuplication;
        Weights[(int)GeneratorID.SpeckCluster] = WeightSpeckCluster;
        Weights[(int)GeneratorID.OrganicCluster] = WeightOrganicCluster;
    }

    private static float Sat(float v) => Mathf.Clamp01(v);

    //JSON schema. Field names must match prism_analyse.py exactly.//
    //"static" is a C# keyword, so the field is named "stat" - see AudioAnalyser, which//
    //rewrites the key before parsing.//

    [Serializable]
    public class AnalysisData
    {
        public int version;
        public Source source;
        public Static stat;
        public Frames frames;
        public float[] beats;
        public float[] onsets;
    }

    [Serializable]
    public class Source
    {
        public string filename;
        public float duration;
        public int sample_rate;
        public int hop_length;
        public float frame_rate;
        public int frame_count;
    }

    [Serializable]
    public class Static
    {
        public float tempo;
        public float flatness;
        public float centroid;
        public float harmonic_complexity;
        public float percussiveness;
        public float rhythmic_regularity;
        public float stereo_width;
        public float dynamic_range;
        public float onset_density;
    }

    [Serializable]
    public class Frames
    {
        public float[] energy;
        public float[] centroid;
        public float[] flatness;
        public float[] harmonic_complexity;
        public float[] flux;
        public float[] stereo_width;
        public float[] percussive_ratio;
        public float[] onset_density;
        public float[] rhythmic_regularity;
        public float[] band_low;
        public float[] band_mid;
        public float[] band_high;
    }
}
