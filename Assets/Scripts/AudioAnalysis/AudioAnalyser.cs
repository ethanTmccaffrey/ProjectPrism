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
        int samplesPerSegment = samples.Length / SEGMENTS;

        float peak = 0f;
        float total = 0f;

        for(int i = 0; i < SEGMENTS; i++)
        {
            float sum = 0f;
            int start = i * samplesPerSegment;
            int end = Mathf.Min(start + samplesPerSegment, samples.Length);

            for (int j = start; j < end; j++)
            {
                sum += samples[j] * samples[j];
            }

            float rms = Mathf.Sqrt(sum / (end - start));
            energy[i] = rms;

            if(rms > peak) peak = rms;
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

        //Step 2: Auto correction//
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
