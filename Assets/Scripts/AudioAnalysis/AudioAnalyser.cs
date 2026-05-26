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
    private bool _spectrumAnalysisComplete = false;
    private int _spectrumSampleCount = 0;
    private const int SPECTRUM_SAMPLES = 50; //Sample 50 frams then average//
    private float[] _lowAccum = new float[1];
    private float[] _midAccum = new float[1];
    private float[] _highAccum = new float[1];
    private const int FFT_SIZE = 1024;

    private float _startDelay = 1f; // Wait half a second before sampling//
    private float _timer = 0f;

    public bool AnalysisComplete { get; private set; } = false;
    public float[] LowEnergyOverTime { get; private set; }
    public float[] MidEnergyOverTime { get; private set; }
    public float[] HighEnergyOverTime { get; private set; }


    public void Init(AudioSource source)
    {
        _audioSource = source;
    }

    void Update()
    {
        if (_audioSource == null || _spectrumAnalysisComplete) return;
        if(!_audioSource.isPlaying) return;

        //Wait for audio to properly start before sampling//
        _timer += Time.deltaTime;
        if (_timer < _startDelay) return;

        float[] spectrum = new float[FFT_SIZE];
        _audioSource.GetSpectrumData(spectrum, 0, FFTWindow.BlackmanHarris);

        //Split spectrum into three bands//
        //Low: bins 0-10 (~10~500Hz)//
        //Mid: bins 10-100 (~500~4.5kHz)//
        //High: bins 100-512 (~4.5kHz~24kHz)//
        float low = 0f, mid = 0f, high = 0f;

        for (int i = 0; i < 10; i++) low += spectrum[i];
        for (int i = 10; i < 100; i++) mid += spectrum[i];
        for (int i = 100; i < FFT_SIZE / 2; i++) high += spectrum[i];

        _lowAccum[0] += low / 10f;
        _midAccum[0] += mid / 90f;
        _highAccum[0] += high / 412f;

        _spectrumSampleCount++;

        if(_spectrumSampleCount >= SPECTRUM_SAMPLES)
        {
            LowFrequencyEnergy = _lowAccum[0] / SPECTRUM_SAMPLES;
            MidFrequencyEnergy = _midAccum[0] / SPECTRUM_SAMPLES;
            HighFrequencyEnergy = _highAccum[0] / SPECTRUM_SAMPLES;

            //Calculate the pitch register as a 0-1 value//
            //0 = all energy in low frequencies, 1 = all energy in high//
            float total = LowFrequencyEnergy + MidFrequencyEnergy + HighFrequencyEnergy;
            if(total > 0)
            {
                PitchRegister = (MidFrequencyEnergy + HighFrequencyEnergy * 2f) / (total + HighFrequencyEnergy);
            }

            _spectrumAnalysisComplete = true;
            LogFrequencyResults();
        }
    }

    public void Analyse(AudioClip clip)
    {
        Debug.Log("PRISM: Beginning audio analysis...");

        float[] samples = GetSamples(clip);

        EnergyOverTime = CalculateEnergyOverTime(samples, clip);
        EstimatedTempo = EstimateTempo(samples, clip);

        LogResults();
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

    //Basic tempo estimation from energy peaks//
    private float EstimateTempo(float[] samples, AudioClip clip)
    {
        //Step 1: Build onset strength envelope//
        //Instead of raw energy, measure sudden increases in energy//
        //These correspond to note onsets and beats regardless of overall volume//
        int beatSegmentSize = clip.frequency / 100;
        int totalBeatSegments = samples.Length / beatSegmentSize;

        float[] rawEnergy = new float[totalBeatSegments];
        float[] fineEnergy = new float[totalBeatSegments];

        //First Pass: Get raw energy per segment//
        for (int i = 0; i < totalBeatSegments; i++)
        {
            float sum = 0f;
            int start = i * beatSegmentSize;
            int end = Mathf.Min(start + beatSegmentSize, samples.Length);

            for (int j = start; j < end; j++)
            {
                sum += samples[j] * samples[j];
            }

            rawEnergy[i] = Mathf.Sqrt(sum / (end - start));
        }

        //Second Pass: onset strength = positive energy flux only//
        //We only care about energy INCREASING not decreasing//
        for(int i = 1; i < totalBeatSegments; i++)
        {
            float diff = rawEnergy[i] - rawEnergy[i - 1];
            fineEnergy[i] = Mathf.Max(0f, diff); //Only positive changes//
        }

        //Step 2: Auto correlation//
        //Looking for lags (delays) that correspond to BPM range 60-200//
        //Convert BPM range to segment lag range//
        //At 10ms per segment: 60 BPM = beat every 1000ms = 100 segments, 200 BPM = beat every 300ms = 30 segments//

        int minLag = 30; //200 BPM//
        int maxLag = 200; //30 BPM - Wider range//

        float[] correlations = new float[maxLag + 1];
        
        for (int lag = minLag; lag <= maxLag; lag++)
        {
            float correlation = 0f;
            int count = 0;

            for(int i = 0; i < fineEnergy.Length - lag; i++)
            {
                correlation += fineEnergy[i] * fineEnergy[i + lag];
                count++;
            }

            correlations[lag] = correlation / count;
        }
       

        //Step 3: Find peaks in the correlation curve//
        //A true beat period will have a strong peak and its multiples will also be strong//
        //We score each lag by how well it predicts its own multiples//
        float bestScore = -1f;
        int bestLag = minLag;

        for (int lag = minLag; lag <= maxLag / 2; lag++)
        {
            float score = correlations[lag];

            //Check if double and triple this lag also have strong correlations//
            //if so this is likely the fundamental beat period//
            int doubleLag = lag * 2;
            int tripleLag = lag * 3;

            if (doubleLag <= maxLag) score += correlations[doubleLag] * 0.5f;
            if (tripleLag <= maxLag) score += correlations[tripleLag] * 0.25f;

            //Favour longer periods - subdivisions cluster at short lags//
            float lagBias = (float)(lag - minLag) / (maxLag / 2 - minLag);
            score *= (1f + lagBias * 0.2f);

            if (score > bestScore)
            {
                bestScore = score;
                bestLag = lag;
            }

        }


        //Step 4: convert best lag to BPM//
        float periodMs = bestLag * 10f;
        float bpm = 60000f / periodMs;

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

    private void LogFrequencyResults()
    {
        Debug.Log("=== PRSIM Frequency Analysis ===");
        Debug.Log("Low Frequency Energy: " + LowFrequencyEnergy.ToString("F6"));
        Debug.Log("Mid Frequency Energy: " + MidFrequencyEnergy.ToString("F6"));
        Debug.Log("High Frequency Energy: " + HighFrequencyEnergy.ToString("F6"));
        Debug.Log("Pitch Register (0=Low, 1=High): " + PitchRegister.ToString("F3"));
        AnalysisComplete = true;
    }
}
