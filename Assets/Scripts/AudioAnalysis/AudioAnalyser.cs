using UnityEditor.Rendering.Universal;
using UnityEngine;

public class AudioAnalyser : MonoBehaviour
{
    //How many chunks to split the audio into for analysis//
    private const int SEGMENTS = 64;

    //Stores the results - other systems will read from these//
    public float[] EnergyOverTime {  get; private set; }
    public float PeakEnergy { get; private set; }
    public float AverageEnergy { get; private set; }
    public float EstimatedTempo {  get; private set; }
    public float LowFrequencyEnergy { get; private set; }
    public float MidFrequencyEnergy {get; private set; }
    public float HighFrequencyEnergy { get; private set; }
    public float PitchRegister {  get; private set; } //0 = very low, 1 = very high//

    private AudioSource _audioSource;
    private const int SPECTRUM_SAMPLES = 50; //Sample 50 frams then average//

    public bool AnalysisComplete { get; private set; } = false;
    public float[] LowEnergyOverTime { get; private set; }
    public float[] MidEnergyOverTime { get; private set; }
    public float[] HighEnergyOverTime { get; private set; }


    public void Init(AudioSource source)
    {
        _audioSource = source;
    }

    public void Analyse(AudioClip clip)
    {
        Debug.Log("PRISM: Beginning audio analysis...");

        float[] samples = GetSamples(clip);

        EnergyOverTime = CalculateEnergyOverTime(samples, clip);
        EstimatedTempo = EstimateTempo(samples, clip);

        LogResults();
        AnalysisComplete = true;
    }

    //Pull all raw sample data out of the clip//
    private float[] GetSamples(AudioClip clip)
    {
        float[] samples = new float[clip.samples * clip.channels];
        clip.GetData(samples, 0);
        return samples;
    }

    //Split audio into segments, calculate RMS energy for each//
    private float[] CalculateEnergyOverTime(float[] samples, AudioClip clip)
    {
        float[] energy = new float[SEGMENTS];
        LowEnergyOverTime = new float[SEGMENTS];
        MidEnergyOverTime = new float[SEGMENTS];
        HighEnergyOverTime = new float[SEGMENTS];

        int samplesPerSegment = samples.Length / SEGMENTS;
        int channels = clip.channels;
        int sampleRate = clip.frequency;

        float peak = 0f;
        float total = 0f;

        for(int i = 0; i < SEGMENTS; i++)
        {
            int start = i * samplesPerSegment;
            int end = Mathf.Min(start + samplesPerSegment, samples.Length);
            int segLength = end - start;

            //Calculate RMS energy overall energy//
            float sumAll = 0f;
            for(int j = start; j < end; ++j)
            {
               sumAll += samples[j] * samples[j];
            }

            float rms = Mathf.Sqrt(sumAll / segLength);
            energy[i] = rms;

            //FFT to get frequency content//
            //use next power of 2 up to 4096 for accuracy//
            int fftSize = 4096;
            float[] fftInput = new float[fftSize];

            //Copy segment samples into FFT buffer (mono mix if stereo)//
            for(int j = 0; j < fftSize; j++)
            {
                int sampleIndex = start + (j * channels);
                if(sampleIndex < samples.Length) fftInput[j] = samples[sampleIndex];
            }

            //Apply Hanning window to reduce spectural leakage//
            for (int j = 0; j < fftSize; j++)
            {
                float window = 0.5f * (1f - Mathf.Cos(2f * Mathf.PI * j / (fftSize - 1)));
                fftInput[j] *= window;
            }

            float[] spectrum = FFT(fftInput);

            //Frequency resolution = sampleRate / fftSize//
            float freqResolution = (float)sampleRate / fftSize;

            //Band boundaies in Hz//
            //Low: 20-250Hz//
            //Mid: 250-4000Hz//
            //High: 4000-20000Hz//
            int lowMaxBin = Mathf.RoundToInt(250f /  freqResolution);
            int midMaxBin = Mathf.RoundToInt(4000f / freqResolution);
            int highMaxBin = Mathf.RoundToInt(20000f / freqResolution);
            highMaxBin = Mathf.Min(highMaxBin, spectrum.Length - 1);

            float sumLow = 0f, sumMid = 0f, sumHigh = 0f;

            for(int b = 1; b < lowMaxBin; b++) sumLow += spectrum[b];
            for(int b = lowMaxBin; b < midMaxBin; b++) sumMid += spectrum[b];
            for (int b = midMaxBin; b < highMaxBin; b++) sumHigh += spectrum[b];

            //Normalise by bin count//
            LowEnergyOverTime[i] = (sumLow / Mathf.Max(1, lowMaxBin - 1) * 3.5f);
            MidEnergyOverTime[i] = (sumMid / Mathf.Max(1, midMaxBin - lowMaxBin) * 1.0f);
            HighEnergyOverTime[i] = (sumHigh / Mathf.Max(1, highMaxBin - midMaxBin) * 8.0f);

            if (rms > peak) peak = rms;
            total += rms;
        }

        PeakEnergy = peak;
        AverageEnergy = total / SEGMENTS;

        return energy;

    }

    
    private float EstimateTempo(float[] samples, AudioClip clip)
    {
        //Approach Inspired by:
        //Dixon, S. (2001) - Automatic Extraction of Tempo and Beat from Expressive Performances//
        // McFee et al. (2015) - librosa: Audio and Music Signal Analysis in Python//

        int sampleRate = clip.frequency;
        int channels = clip.channels;

        //10ms hop size//
        int hopSize = sampleRate / 100;
        int totalHops = samples.Length / (hopSize * channels);

        //Step 1: Multi-band onset detection//
        //Analyse bass, mid and high bands seperately//
        //Inspired by Dixon's multi-band approach//
        int fftSize = 1024;
        float freqResolution = (float)sampleRate / fftSize;

        int bassBin = Mathf.RoundToInt(250f / freqResolution);
        int midBin = Mathf.RoundToInt(4000f / freqResolution);
        int highBin = Mathf.Min(Mathf.RoundToInt(16000f / freqResolution), fftSize / 2 - 1);

        float[] onsetBass = new float[totalHops];
        float[] onsetMid = new float[totalHops];
        float[] onsetHigh = new float[totalHops];

        float[] prevBass = new float[1];
        float[] prevMid = new float[1];
        float[] prevHigh = new float[1];

        for(int i = 0; i < totalHops; i++)
        {
            int start = i * hopSize * channels;

            float[] fftInput = new float[fftSize];
            for(int j = 0; j < fftSize; j++)
            {
                int idx = start + j * channels;
                if (idx < samples.Length) fftInput[j] = samples[idx];
            }

            //Hanning window//
            for(int j = 0; j < fftSize; j++)
            {
                float w = 0.5f * (1f - Mathf.Cos(2f * Mathf.PI * j / (fftSize - 1)));
                fftInput[j] *= w;
            }

            float[] spectrum = FFT(fftInput);

            //Sum each band//
            float bass = 0f, mid = 0f, high = 0f;
            for (int b = 1; b < bassBin; b++) bass += spectrum[b];
            for (int b = bassBin; b < midBin; b++) mid += spectrum[b];
            for (int b = midBin; b < highBin; b++) high += spectrum[b];

            bass /= Mathf.Max(1, bassBin - 1);
            mid /= Mathf.Max(1, midBin - bassBin);
            high /= Mathf.Max(1, highBin - midBin);

            //Positive flux only - onset = energy increase//
            onsetBass[i] = Mathf.Max(0f, bass - prevBass[0]);
            onsetMid[i] = Mathf.Max(0f, mid - prevMid[0]);
            onsetHigh[i] = Mathf.Max(0f, high - prevHigh[0]);

            prevBass[0] = bass;
            prevMid[0] = mid;
            prevHigh[0] = high;
        }

        //Step 2: Adaptive normalisation//
        //Normalise each band's onset envelope against its local mean//
        //Prevents quiet sections from being drowned out by loud ones//
        //Inspired by Librosa's onset_strength normalisation//
        int windowSize = 50;// 500ms local window//
        float[] onsetCombined = new float[totalHops];

        for(int i = 0; i < totalHops; i++)
        {
            int wStart = Mathf.Max(0, i - windowSize / 2);
            int wEnd = Mathf.Min(totalHops, i + windowSize / 2);

            float meanBass = 0f, meanMid = 0f, meanHigh = 0f;
            int count = wEnd - wStart;

            for(int w = wStart; w < wEnd; w++)
            {
                meanBass += onsetBass[w];
                meanMid += onsetMid[w];
                meanHigh += onsetHigh[w];
            }

            meanBass /= count;
            meanMid /= count;
            meanHigh /= count;

            //Normalise against local mean, weight bass heavily - kick drives tempo//
            float normBass = meanBass > 0 ? onsetBass[i] / meanBass : 0f;
            float normMid = meanMid > 0 ? onsetMid[i] / meanMid : 0f;
            float normHigh = meanHigh > 0 ? onsetHigh[i] / meanHigh : 0f;

            onsetCombined[i] = normBass * 0.5f + normMid * 0.3f + normHigh * 0.2f;
        }

        //Step 3: Autocorrelation on combined onset envelope//
        int minLag = 30; //200 BPM//
        int maxLag = 200; //30 BPM//

        float[] correlations = new float[maxLag + 1];

        for(int lag = minLag; lag <= maxLag; lag++)
        {
            float correlation = 0f;
            int n = 0;

            for(int i = 0; i < onsetCombined.Length - lag; i++)
            {
                correlation += onsetCombined[i] * onsetCombined[i + lag];
                n++;
            }

            correlations[lag] = n > 0 ? correlation / n : 0f;
        }

        //Step 4: Harmonic Scoring//
        //A true beat period scores strongly at its multiples//
        float bestScore = -1f;
        int bestLag = minLag;

        for(int lag = minLag; lag <= maxLag / 2; lag++)
        {
            float score = correlations[lag];
            int doubleLag = lag * 2;
            int tripleLag = lag * 3;

            if (doubleLag <= maxLag) score += correlations[doubleLag] * 0.5f;
            if (tripleLag <= maxLag) score += correlations[tripleLag] * 0.25f;

            //Slight bias toward longer periods//
            float lagBias = (float)(lag - minLag) / (maxLag / 2 - minLag);
            score *= (1f + lagBias * 0.15f);

            if(score > bestScore)
            {
                bestScore = score;
                bestLag = lag;
            }
        }

        //Step 5: Convert to BPM//
        float periodMs = bestLag * 10f;
        float bpm = 60000f / periodMs;

        //Half tempo correction - only when result is suspiciously low and doubling lands in a realistice range//
        float doubleBPM = bpm * 2f;
        if (bpm < 75f && doubleBPM >= 90f && doubleBPM <= 160) bpm = doubleBPM;

        return bpm;
    }

    private float[] FFT(float[] input)
    {
        int n = input.Length;
        float[] real = new float[n];
        float[] imag = new float[n];
        float[] output = new float[n];

        for(int i = 0; i < n; i++) real[i] = input[i];

        //Cooley-Tukey iterative FFT//
        int j = 0;
        for(int i = 1; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if(i < j) { float tmp = real[i]; real[i] = real[j]; real[j] = tmp; }
        }

        for(int len = 2; len <= n; len <<=1)
        {
            float angle = -2f * Mathf.PI / len;
            float wRe = Mathf.Cos(angle);
            float wIm = Mathf.Sin(angle);

            for(int i = 0; i < n; i += len)
            {
                float curRe = 1f, curIm = 0f;
                for(int k = 0; k < len / 2; k++)
                {
                    float uRe = real[i + k];
                    float uIm = imag[i + k];
                    float vRe = real[i + k + len / 2] * curRe - imag[i + k + len / 2] * curIm;
                    float vIm = real[i + k + len / 2] * curIm + imag[i + k + len / 2] * curRe;

                    real[i + k] = uRe + vRe;
                    imag[i + k] = uIm + vIm;
                    real[i + k + len / 2] = uRe - vRe;
                    imag[i + k + len / 2] = uIm - vIm;

                    float newCurRe = curRe * wRe - curIm * wIm;
                    curIm = curRe * wIm + curIm * wRe;
                    curRe = newCurRe;
                }
            }
        }

        //Return magnitude specturm//
        for (int i = 0; i < n / 2; i++) output[i] = Mathf.Sqrt(real[i] * real[i] + imag[i] * imag[i]);

        return output;
    }
    private void LogResults()
    {
        Debug.Log("=== PRISM Analysis Results ===");
        Debug.Log("Peak Energy: " + PeakEnergy.ToString("F4"));
        Debug.Log("Average Energy: " + AverageEnergy.ToString("F4"));
        Debug.Log("Estimated BPM: " + EstimatedTempo.ToString("F1"));
        Debug.Log("Energy over time (64 segments):");

        string energyMap = "";
        for (int i = 0; i < EnergyOverTime.Length; i++)
        {
            float normalized = EnergyOverTime[i] / PeakEnergy;
            if (normalized > 0.75f) energyMap += "█";
            else if (normalized > 0.5f) energyMap += "▓";
            else if (normalized > 0.25f) energyMap += "▒";
            else energyMap += "░";
        }
        Debug.Log(energyMap);

       
    }
}
