using System.Collections;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using Debug = UnityEngine.Debug;

//AudioAnalyser//
//Runs the offline analysis subprocess (prism_analyse.exe) on the chosen track, caches the JSON result and loads it into the TimbralProfile//
//Acoustic analysis is performed by librosa (McFee et al. 2015); at runtime PRISM only reads the result//

public class AudioAnalyser : MonoBehaviour
{
    [Header("Analyser")]
    [SerializeField] private string analyserExecutable = "prism_analyse.exe";
    [SerializeField] private bool cacheAnalysis = true;
    [SerializeField] private float timeoutSeconds = 300f;

    public TimbralProfile TimbralProfile { get; private set; } = new TimbralProfile();
    public bool AnalysisComplete { get; private set; } = false;
    public bool AnalysisFailed { get; private set; } = false;
    public string StatusMessage { get; private set; } = "";

    public float EstimatedTempo => TimbralProfile.StaticTempo;
    public float PeakEnergy { get; private set; } = 1f;
    public float AverageEnergy { get; private set; } = 0.5f;
    public float DynamicRange => TimbralProfile.StaticDynamicRange;
    public float RawLowAverage { get; private set; }
    public float RawMidAverage { get; private set; }
    public float RawHighAverage { get; private set; }

    private Process _process;

    public IEnumerator AnalyseFile(string audioPath)
    {
        AnalysisComplete = false;
        AnalysisFailed = false;

        if (!File.Exists(audioPath))
        {
            Fail("Audio file not found: " + audioPath);
            yield break;
        }

        string jsonPath = Path.ChangeExtension(audioPath, ".prism.json");

        if (cacheAnalysis && File.Exists(jsonPath))
        {
            Debug.Log("PRISM: cached analysis found, skipping analysis pass");
            StatusMessage = "Loading cached analysis...";
            if (LoadJson(jsonPath)) AnalysisComplete = true;
            yield break;
        }

        string exePath = Path.Combine(Application.streamingAssetsPath, analyserExecutable);
        if (!File.Exists(exePath))
        {
            Fail("Analyser not found at " + exePath + " - build it with PyInstaller and place it in StreamingAssets.");
            yield break;
        }

        StatusMessage = "Analysing audio...";
        Debug.Log("PRISM: launching analyser - " + exePath);

        var info = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = $"\"{audioPath}\" \"{jsonPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        _process = new Process { StartInfo = info };

        bool started;
        try
        {
            started = _process.Start();
        }
        catch (System.Exception e)
        {
            Fail("Could not start analyser: " + e.Message);
            yield break;
        }

        if (!started)
        {
            Fail("Analyser failed to start");
            yield break;
        }

        float elapsed = 0f;
        while (!_process.HasExited)
        {
            elapsed += Time.deltaTime;
            if (elapsed > timeoutSeconds)
            {
                try { _process.Kill(); } catch { }
                Fail($"Analyser timed out after {timeoutSeconds:F0}s");
                yield break;
            }
            yield return null;
        }

        string stdout = _process.StandardOutput.ReadToEnd();
        string stderr = _process.StandardError.ReadToEnd();
        int exit = _process.ExitCode;
        _process = null;

        if (!string.IsNullOrEmpty(stdout)) Debug.Log("PRISM analyser:\n" + stdout.Trim());

        if (exit != 0)
        {
            Fail($"Analyser exited with code {exit}\n{stderr}");
            yield break;
        }

        if (!File.Exists(jsonPath))
        {
            Fail("Analyser reported success but produced no output file");
            yield break;
        }

        StatusMessage = "Loading analysis...";
        if (LoadJson(jsonPath)) AnalysisComplete = true;
    }

    private bool LoadJson(string jsonPath)
    {
        string json;
        try
        {
            json = File.ReadAllText(jsonPath);
        }
        catch (System.Exception e)
        {
            Fail("Could not read analysis file: " + e.Message);
            return false;
        }

        json = json.Replace("\"static\":", "\"stat\":");

        if (!TimbralProfile.LoadFromJson(json))
        {
            Fail("Analysis file could not be parsed");
            return false;
        }

        RawLowAverage = TimbralProfile.RealtimeBandLow;
        RawMidAverage = TimbralProfile.RealtimeBandMid;
        RawHighAverage = TimbralProfile.RealtimeBandHigh;

        TimbralProfile.SampleAt(0f);
        RawLowAverage = TimbralProfile.RealtimeBandLow;
        RawMidAverage = TimbralProfile.RealtimeBandMid;
        RawHighAverage = TimbralProfile.RealtimeBandHigh;

        PeakEnergy = 1f;  
        AverageEnergy = Mathf.Clamp01(1f - TimbralProfile.StaticDynamicRange * 0.5f);

        StatusMessage = "Ready";
        return true;
    }

    private void Fail(string message)
    {
        AnalysisFailed = true;
        AnalysisComplete = false;
        StatusMessage = "Analysis failed";
        Debug.LogError("PRISM: " + message);
    }

    private void OnDestroy()
    {
        if (_process != null && !_process.HasExited)
        {
            try { _process.Kill(); } catch { }
        }
    }
}