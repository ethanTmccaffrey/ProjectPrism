using UnityEngine;

//TimbralProfile holds all acoustic measurements: both static (full track analysis) and realtime (updated every frame) and derives generator weights from them//
//The four core measurements are grounded in MIR literature//
//Spectral Flatness: Dubnov (2004), Johnston (1988)//
//Spectral Flux: Lartillot & Toiviainen (2007)//
//Spectral Centroid: Schubert et al. (2004)//
//Zero Crossing Rate: Gouyon et al. (2000)//
//Generator weight derivations are grounded in Kluver's form constants (1926) and cross-modal correspondence research (Cytowic 2002, Marks 1974)//

public class TimbralProfile
{
    //Static measurements (computed once from full track)//
    //These describe the overall character of the song//
    public float StaticFlatness { get; private set; } //0 = tonal, 1 = noise//
    public float StaticFlux { get; private set; } //0 = Sustained, 1 = Percusive//
    public float StaticCentroid { get; private set; } //0 = dark/bass, 1 = bright/treble//
    public float StaticZCR { get; private set; } //0 = smooth, 1 = complex//
    public float StaticHarmonicComplexity { get; private set; } //0 = simple, 1 = complex//
    public float StaticRhythmicRegularity { get; private set; } //0 = irregular, 1 = metronomic//
    public float StaticStereoWidth { get; private set; } //0 = mono, 1 = wide stereo//   

    //Realtime measurements (updated every frame)//
    //These describe what the music sounds like right now//
    public float RealtimeFlatness { get; private set; }
    public float RealtimeFlux { get; private set; }
    public float RealtimeCentroid { get; private set; }
    public float RealtimeZCR { get; private set; }
    public float RealtimeHarmonicComplexity { get; private set; }
    public float RealtimeRhythmicRegularity { get; private set; }
    public float RealtimeStereoWidth { get; private set; }
    public float RealtimeEnergy { get; private set; }

    //Generator weights (0-1)//
    //Each weight descirbes how strongly that generator should be active//
    //currently based on the realtime timbral measurements//
    //Weights are recomputed every frame in UpdateRealtimeWeights()//

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

    //Previous spectrum frame for flux calculation//
    private float[] _prevSpectrum = null;

    //Smoothing for realtime measurments to prevent strobing//
    private float _smoothFlatness = 0f;
    private float _smoothFlux = 0f;
    private float _smoothCentroid = 0f;
    private float _smoothZCR = 0f;
    private float _smoothHarmonic = 0f;
    private float _smoothRhythmic = 0f;
    private float _smoothStereo = 0f;
    private float _smoothEnergy = 0f;

    private const float SMOOTH_SPEED = 5f; //Higher = more responsive, lower = smoother//

    //Static Analysis//
    //Called once by AudioAnalyser after full track analysis completes, Takes averaged spectrum data and raw samples to compute track-level character//
    public void ComputeStatic(float[] averageSpectrum, float[] samples, int sampleRate, float[] energyOverTime, float estimateTempo)
    {
        StaticFlatness = ComputeFlatness(averageSpectrum);
        StaticCentroid = ComputeCentroid(averageSpectrum, sampleRate);
        StaticFlux = ComputeAverageFlux(energyOverTime);
        StaticZCR = ComputeZCR(samples, sampleRate);
        StaticHarmonicComplexity = ComputeHarmonicComplexity(averageSpectrum);
        StaticRhythmicRegularity = ComputeRhythmicRegularity(energyOverTime, estimateTempo, sampleRate);
        StaticStereoWidth = 0f; //Computed from stereo data in UpdateRealtime//

        Debug.Log("=== PRISM Timbral Profile (Static) ===");
        Debug.Log($"Flatness: {StaticFlatness:F3}  (0=tonal, 1=noise)");
        Debug.Log($"Flux: {StaticFlux:F3}  (0=sustained, 1=percussive)");
        Debug.Log($"Centroid: {StaticCentroid:F3}  (0=dark, 1=bright)");
        Debug.Log($"ZCR: {StaticZCR:F3}  (0=smooth, 1=rough)");
        Debug.Log($"Harmonic Complexity: {StaticHarmonicComplexity:F3}");
        Debug.Log($"Rhythmic Regularity: {StaticRhythmicRegularity:F3}");
    }

    //Realtime Update//
    //Called every frame by PRSIMGenerator with current spectrum data//
    //Updates realtime measurements and recomputes all generator weights//
    public void UpdateRealtime(float[] spectrumLeft, float[] spectrumRight, float deltaTime)
    {
        //Build mono spectrum by averaging left and right//
        int len = spectrumLeft.Length;
        float[] monoSpectrum = new float[len];
        for (int i = 0; i < len; i++)
            monoSpectrum[i] = (spectrumLeft[i] + spectrumRight[i]) * 0.5f;

        int sampleRate = 44100; //Standard: actual rate doesn't affect normalised values//

        // Compute raw measurements this frame
        float rawFlatness = ComputeFlatness(monoSpectrum);
        float rawCentroid = ComputeCentroid(monoSpectrum, sampleRate);
        float rawFlux = _prevSpectrum != null ? ComputeFluxFrame(monoSpectrum, _prevSpectrum) : 0f;
        float rawHarmonic = ComputeHarmonicComplexity(monoSpectrum);
        float rawStereo = ComputeStereoWidth(spectrumLeft, spectrumRight);

        //Total energy for weight scaling//
        float rawEnergy = 0f;
        for (int i = 0; i < len; i++) rawEnergy += monoSpectrum[i];
        rawEnergy = Mathf.Clamp01(rawEnergy / len * 100f);

        //ZCR requires samples — approximated from spectrum shape in realtime//
        //High-frequency content correlates strongly with ZCR//
        float rawZCR = rawCentroid * 0.7f + rawFlatness * 0.3f;

        //Rhythmic regularity: approximate from flux variance//
        //Consistent flux peaks = regular rhythm//
        float rawRhythmic = _prevSpectrum != null ? Mathf.Clamp01(1f - Mathf.Abs(rawFlux - _smoothFlux) * 10f) : 0.5f;

        //Smooth all measurements//
        float s = deltaTime * SMOOTH_SPEED;
        _smoothFlatness = Mathf.Lerp(_smoothFlatness, rawFlatness, s);
        _smoothFlux = Mathf.Lerp(_smoothFlux, rawFlux, s);
        _smoothCentroid = Mathf.Lerp(_smoothCentroid, rawCentroid, s);
        _smoothZCR = Mathf.Lerp(_smoothZCR, rawZCR, s);
        _smoothHarmonic = Mathf.Lerp(_smoothHarmonic, rawHarmonic, s);
        _smoothRhythmic = Mathf.Lerp(_smoothRhythmic, rawRhythmic, s);
        _smoothStereo = Mathf.Lerp(_smoothStereo, rawStereo, s);
        _smoothEnergy = Mathf.Lerp(_smoothEnergy, rawEnergy, s);

        RealtimeFlatness = _smoothFlatness;
        RealtimeFlux = _smoothFlux;
        RealtimeCentroid = _smoothCentroid;
        RealtimeZCR = _smoothZCR;
        RealtimeHarmonicComplexity = _smoothHarmonic;
        RealtimeRhythmicRegularity = _smoothRhythmic;
        RealtimeStereoWidth = _smoothStereo;
        RealtimeEnergy = _smoothEnergy;

        //Store spectrum for next frame's flux calculation//
        _prevSpectrum = (float[])monoSpectrum.Clone();

        //Recompute all generator weights from current realtime measurements//
        UpdatedRealtimeWeights();
    }

    //Generator Weight Derivation//
    //Each weight maps the four core measurements to a 0-1 activation strength//
    //Mappings grounded in Kluver form constant research and timbral correspondance//
    private void UpdatedRealtimeWeights()
    {
        float F = RealtimeFlatness; //noise//
        float X = RealtimeFlux; //percussive//
        float C = RealtimeCentroid; //bright//
        float Z = RealtimeZCR; //rough//
        float H = RealtimeHarmonicComplexity; //complex//
        float R = RealtimeRhythmicRegularity; //regular//
        float S = RealtimeStereoWidth; //wide//
        float E = RealtimeEnergy; //loud//
        float tF = 1f - F; //tonal (inverse flatness)//
        float tX = 1f - X; //sustained (inverse flux)//
        float tC = 1f - C; //dark (inverse centroid)//
        float tZ = 1f - Z; //smooth (inverse ZCR)//
        float tR = 1f - R; //irregular (inverse regularity)//

        //Category 1: Tunnels and Funnels//
        //Low flux, tonal, dynamic — orchestral, classical, ambient//
        WeightConcentricRings = Saturate(tX * tF * E);
        WeightTunnelDepth = Saturate(tX * tF * tZ * (1f - E + 0.2f));

        //Category 2: Spirals//
        //Medium flatness, harmonic complexity — jazz, blues, complex music//
        WeightSpiralGrowth = Saturate(H * tF * tX);
        WeightRotationField = Saturate(H * R * Mathf.Abs(0.5f - F));
        WeightDrift = Saturate(tX * tX * tZ * (1f - E + 0.3f));

        //Category 3: Lattices and Honeycombs//
        //Regular rhythm, synthetic/precise — electronic, EDM, pop//
        WeightHoneycomb = Saturate(R * tF * C);
        WeightGridGrating = Saturate(R * X * C);
        WeightFiligree = Saturate(H * Mathf.Lerp(0f, 1f, F * 0.5f + 0.2f));
        WeightReduplication = Saturate(R * R * tF);

        //Category 4: Cobwebs and Radial Forms//
        //High ZCR, flatness — rock, metal, distorted//
        WeightRadiationBurst = Saturate(X * Z * F);
        WeightFracture = Saturate(F * F * Z * Z);  //Very high both = heavy metal//
        WeightCobwebSpline = Saturate(H * Mathf.Sqrt(F) * tX);

        //Category 5: Parallel Figures//
        //Various combinations of regularity and flatness//
        WeightZigzagParallel = Saturate(Z * R * Mathf.Sqrt(F));
        WeightWavyParallel = Saturate(tF * tZ * tX);
        WeightHatching = Saturate(tF * X * R);

        //Category 6: Wavy Lines and Amorphous Forms//
        //Organic, flowing, irregular — folk, soul, blues//
        WeightFluidTendril = Saturate(tF * tF * tZ);
        WeightAmorphousSpeck = Saturate(tR * tF * E);
        WeightBilateralDuplication = Saturate(S * E); //Stereo width drives bilateral//

        //Category 7: Small Circular Figures//
        //Low energy, sparse — quiet passages of anything//
        WeightSpeckCluster = Saturate((1f - E) * (1f - E));
        WeightOrganicCluster = Saturate(tF * tX * (1f - E + 0.1f));
    }

    //Clamps to 0-1 and applies a power curve so weights don't all fire weakly//
    private float Saturate(float value)
    {
        return Mathf.Clamp01(value);
    }

    //Measurement Implementation//

    //Spectrual Flatness: geometric mean / arithmetic mean//
    //Pure tone = 0, white noise = 1//
    //Dubnov (2004): "Generalization of Spectural Flatness Measure"//
    private float ComputeFlatness(float[] spectrum)
    {
        int n = spectrum.Length / 2; // Use only meaningful half
        if (n == 0) return 0f;

        float logSum = 0f;
        float linearSum = 0f;
        int count = 0;

        for (int i = 1; i < n; i++)
        {
            float val = spectrum[i];
            if (val > 1e-10f)
            {
                logSum += Mathf.Log(val);
                linearSum += val;
                count++;
            }
        }

        if (count == 0 || linearSum == 0) return 0f;

        float geometricMean = Mathf.Exp(logSum / count);
        float arithmeticMean = linearSum / count;

        return Mathf.Clamp01(geometricMean / arithmeticMean);
    }

    //Spectural Centroid: weighed mean frequency, normalised 0-1//
    //Schubert et al. (2004): "Spectural centroid and timbre"//
    private float ComputeCentroid(float[] spectrum, int sampleRate)
    {
        int n = spectrum.Length / 2;
        float weightedSum = 0f;
        float totalMag = 0f;

        for (int i = 0; i < n; i++)
        {
            float freq = (float)i * sampleRate / (spectrum.Length * 2f);
            weightedSum += freq * spectrum[i];
            totalMag += spectrum[i];
        }

        if (totalMag < 1e-10f) return 0f;

        float centroid = weightedSum / totalMag;
        //Normalise: 0Hz = 0, 8000Hz = 1 (most musical content below 8kHz)//
        return Mathf.Clamp01(centroid / 8000f);
    }

    //Spectral Flux per frame: sum of squared positive differences//
    //Lartillot & Toiviainen (2007): onset detection via flux//
    private float ComputeFluxFrame(float[] current, float[] previous)
    {
        if (current.Length != previous.Length) return 0f;

        float flux = 0f;
        int n = current.Length / 2;

        for (int i = 0; i < n; i++)
        {
            float diff = current[i] - previous[i];
            if (diff > 0) flux += diff * diff; //Positive flux only//
        }

        return Mathf.Clamp01(flux * 500f); //Scale to 0-1 range//
    }

    //Average flux from energy over time (static version)//
    private float ComputeAverageFlux(float[] energyOverTime)
    {
        if (energyOverTime == null || energyOverTime.Length < 2) return 0f;

        float totalFlux = 0f;
        for (int i = 1; i < energyOverTime.Length; i++)
        {
            float diff = energyOverTime[i] - energyOverTime[i - 1];
            if (diff > 0) totalFlux += diff;
        }

        return Mathf.Clamp01(totalFlux / energyOverTime.Length * 20f);
    }

    //Zero Crossing Rate: how many times waveform crosses zero per second//
    //Gouyon et al. (2000): "On the use of ZCR for musical genre classification"//
    private float ComputeZCR(float[] samples, int sampleRate)
    {
        if (samples == null || samples.Length < 2) return 0f;

        int crossings = 0;
        //Sample 10000 points for speed//
        int step = Mathf.Max(1, samples.Length / 10000);

        for (int i = step; i < samples.Length; i += step)
        {
            if ((samples[i] >= 0f) != (samples[i - step] >= 0f))
                crossings++;
        }

        float zcr = (float)crossings * step / samples.Length * sampleRate;
        //Normalise: 0 = 0 crossings, 1 = 4000+ crossings/sec (distorted signals)//
        return Mathf.Clamp01(zcr / 4000f);
    }

    //Harmonic Complexity: measures how many distinct spectral peaks exist//
    //More peaks = more complex harmonic content (jazz, classical)//
    //Fewer peaks = simpler content (electronic, drone)//
    private float ComputeHarmonicComplexity(float[] spectrum)
    {
        int n = spectrum.Length / 2;
        int peakCount = 0;
        float threshold = 0f;

        //Find mean amplitude for threshold//
        for (int i = 0; i < n; i++) threshold += spectrum[i];
        threshold /= n;
        threshold *= 2f; //Only count significant peaks//

        //Count peaks above threshold//
        for (int i = 1; i < n - 1; i++)
        {
            if (spectrum[i] > threshold &&
                spectrum[i] > spectrum[i - 1] &&
                spectrum[i] > spectrum[i + 1])
            {
                peakCount++;
            }
        }

        //Normalise: 0 = no peaks, 1 = 20+ distinct harmonic peaks//
        return Mathf.Clamp01(peakCount / 20f);
    }

    //Rhythmic Regularity: measures how consistent energy peaks are over time//
    //Regular beats = high regularity (electronic, pop)//
    //Irregular = low regularity (jazz, ambient, free-form)//
    private float ComputeRhythmicRegularity(float[] energyOverTime, float bpm, int sampleRate)
    {
        if (energyOverTime == null || energyOverTime.Length < 4) return 0.5f;

        //Variance in energy differences — low variance = regular//
        float mean = 0f;
        float[] diffs = new float[energyOverTime.Length - 1];

        for (int i = 0; i < diffs.Length; i++)
            diffs[i] = Mathf.Abs(energyOverTime[i + 1] - energyOverTime[i]);

        foreach (float d in diffs) mean += d;
        mean /= diffs.Length;

        float variance = 0f;
        foreach (float d in diffs) variance += (d - mean) * (d - mean);
        variance /= diffs.Length;

        //Low variance = regular rhythm//
        return Mathf.Clamp01(1f - Mathf.Sqrt(variance) * 10f);
    }

    //Stereo Width: measures difference between left and right channels//
    //Wide stereo = bilateral duplication generator activates//
    private float ComputeStereoWidth(float[] left, float[] right)
    {
        if (left == null || right == null || left.Length != right.Length) return 0f;

        float diff = 0f;
        int n = left.Length / 2;
        for (int i = 0; i < n; i++)
            diff += Mathf.Abs(left[i] - right[i]);

        return Mathf.Clamp01(diff / n * 200f);
    }
}
