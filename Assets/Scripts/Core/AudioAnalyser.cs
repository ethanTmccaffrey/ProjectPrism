using System.Collections;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using Debug = UnityEngine.Debug;

//AudioAnalyser: runs the offline analysis pass and loads the result.
//
//This class used to be ~500 lines of DSP: a hand-written Cooley-Tukey FFT, spectral
//flatness, centroid, inharmonicity, zero-crossing rate, an autocorrelation tempo
//estimator. All of that is gone. Analysis now happens in prism_analyse.exe (librosa,
//McFee et al. 2015), which is called as a subprocess before playback begins.
//
//Two reasons for the change:
//
//1. CORRECTNESS. The hand-rolled measures were repeatedly found to be measuring
//   something other than what they claimed. Validated against tracks of known
//   character, spectral flatness could not separate distorted guitar from dense
//   synthesis; nor could spectral inharmonicity; and zero-crossing rate ranked an
//   orchestral piece as rougher than metal. librosa's implementations are standard,
//   peer-reviewed and citable, and the contribution of this project is the MAPPING from
//   acoustic measurement to visual form, not the DSP underneath it.
//
//2. ARCHITECTURE. PRISM is a persistent canvas: the entire song is known before the
//   first mark is drawn. Nothing ever required the analysis to happen live. Doing it
//   offline means measures can be computed over a WINDOW of time rather than from a
//   single spectral frame - which matters, because the measures that actually
//   distinguish one song from another (rhythmic regularity, onset density, percussive
//   ratio, dynamic range) are all temporal and cannot be read from an instant.
//
//Playback is now a lookup rather than a computation, so it is also considerably cheaper.

public class AudioAnalyser : MonoBehaviour
{
    [Header("Analyser")]
    //Path to the bundled analyser executable, relative to StreamingAssets.//
    [SerializeField] private string analyserExecutable = "prism_analyse.exe";
    //Where the generated JSON is cached. Re-analysing a track is skipped if it exists.//
    [SerializeField] private bool cacheAnalysis = true;
    [SerializeField] private float timeoutSeconds = 300f;

    public TimbralProfile TimbralProfile { get; private set; } = new TimbralProfile();
    public bool AnalysisComplete { get; private set; } = false;
    public bool AnalysisFailed { get; private set; } = false;
    public string StatusMessage { get; private set; } = "";

    //Kept because PRISMGenerator.DeriveQualities() reads them. Sourced from the analysis//
    //rather than computed here.//
    public float EstimatedTempo => TimbralProfile.StaticTempo;
    public float PeakEnergy { get; private set; } = 1f;
    public float AverageEnergy { get; private set; } = 0.5f;
    public float DynamicRange => TimbralProfile.StaticDynamicRange;
    public float RawLowAverage { get; private set; }
    public float RawMidAverage { get; private set; }
    public float RawHighAverage { get; private set; }

    private Process _process;

    //Analyses the file at audioPath, then loads the result. Yields until done.//
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

        //Skip the analysis pass if we have already done this track.//
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
            Fail("Analyser not found at " + exePath +
                 " - build it with PyInstaller and place it in StreamingAssets.");
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

        //Wait without blocking the main thread, so Unity keeps rendering a loading screen.//
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

        //Unity's JsonUtility maps JSON keys to field names, and "static" is a C# keyword
        //so it cannot be a field. Rename the key before parsing rather than complicating
        //the Python side - the JSON is a contract and should read naturally.
        json = json.Replace("\"static\":", "\"stat\":");

        if (!TimbralProfile.LoadFromJson(json))
        {
            Fail("Analysis file could not be parsed");
            return false;
        }

        //Populate the values PRISMGenerator still expects.//
        //Band averages come straight from the static profile's spectral balance; the old
        //code derived these from its own FFT.
        RawLowAverage = TimbralProfile.RealtimeBandLow;
        RawMidAverage = TimbralProfile.RealtimeBandMid;
        RawHighAverage = TimbralProfile.RealtimeBandHigh;

        //Sample frame zero so the band averages are populated before Init() reads them.//
        TimbralProfile.SampleAt(0f);
        RawLowAverage = TimbralProfile.RealtimeBandLow;
        RawMidAverage = TimbralProfile.RealtimeBandMid;
        RawHighAverage = TimbralProfile.RealtimeBandHigh;

        PeakEnergy = 1f;   //frame energy is already normalised 0-1 by the analyser//
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
        //Don't leave an orphaned analyser running if play mode is exited mid-analysis.//
        if (_process != null && !_process.HasExited)
        {
            try { _process.Kill(); } catch { }
        }
    }
}