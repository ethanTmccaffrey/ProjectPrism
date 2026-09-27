using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

//AudioLoader//
//PRISM's entry point Loads an audio file, runs analysis to completion, then starts synced playback and hands the loaded profile to PRISMGenerator//
//Analysis must finish before audio starts or visuals desync//

public class AudioLoader : MonoBehaviour
{
    [Header("Load Mode")]
    [SerializeField] private bool useDynamicPath = true;

    [Header("Inspector Mode (Editor only)")]
    [SerializeField] private AudioClip inspectorClip;

    [Header("Dynamic Mode")]
    [SerializeField] private string filePath = "";

    [Header("References")]
    [SerializeField] private AudioAnalyser analyser;
    [SerializeField] private PRISMGenerator prism;

    private AudioClip _loadedClip;
    private AudioSource _audioSource;

    public bool IsAnalysing { get; private set; } = false;
    public string Status => analyser != null ? analyser.StatusMessage : "";

    private void Start()
    {
        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null) _audioSource = gameObject.AddComponent<AudioSource>();
        if (analyser == null) analyser = GetComponent<AudioAnalyser>();
        if (prism == null) prism = GetComponent<PRISMGenerator>();

        if (analyser == null)
        {
            return;
        }

        string path = filePath;

#if UNITY_EDITOR
        if (!useDynamicPath && inspectorClip != null)
        {
            string assetPath = UnityEditor.AssetDatabase.GetAssetPath(inspectorClip);
            if (!string.IsNullOrEmpty(assetPath))
                path = System.IO.Path.GetFullPath(assetPath);
        }
#endif

        if (!string.IsNullOrEmpty(path))
        {
            Load(path);
        }
        else
        {
            Debug.LogWarning("PRISM: no audio assigned - drag a clip into Inspector Clip, " + "or set a file path");
        }
    }

    //Public entry point. Call this from a file picker to load any track.//
    public void Load(string path)
    {
        if (IsAnalysing)
        {
            Debug.LogWarning("PRISM: already analysing, ignoring load request");
            return;
        }

        if (!File.Exists(path))
        {
            Debug.LogError("PRISM: file not found - " + path);
            return;
        }

        StopAllCoroutines();
        StartCoroutine(LoadAndAnalyse(path));
    }

    private IEnumerator LoadAndAnalyse(string path)
    {
        IsAnalysing = true;

        yield return StartCoroutine(analyser.AnalyseFile(path));

        if (analyser.AnalysisFailed || !analyser.AnalysisComplete)
        {
            Debug.LogError("PRISM: analysis failed, aborting");
            IsAnalysing = false;
            yield break;
        }

        yield return StartCoroutine(LoadClip(path));

        if (_loadedClip == null)
        {
            Debug.LogError("PRISM: audio failed to load, aborting");
            IsAnalysing = false;
            yield break;
        }

        LogClipInfo(_loadedClip);

        _audioSource.clip = _loadedClip;

        if (prism != null)
        {
            analyser.TimbralProfile.ResetPlayhead(0f);
            prism.Init(analyser, _audioSource);
        }
        else
        {
            Debug.LogWarning("PRISM: no PRISMGenerator assigned - audio will play with no visuals");
        }

        _audioSource.Play();
        IsAnalysing = false;

        Debug.Log("PRISM: playback started");
    }

    private IEnumerator LoadClip(string path)
    {
        AudioType type = GuessAudioType(path);
        string url = "file:///" + path.Replace("\\", "/");

        using (var request = UnityWebRequestMultimedia.GetAudioClip(url, type))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("PRISM: failed to load audio - " + request.error);
                yield break;
            }

            _loadedClip = DownloadHandlerAudioClip.GetContent(request);
            if (_loadedClip != null) _loadedClip.name = Path.GetFileNameWithoutExtension(path);
        }
    }

    private AudioType GuessAudioType(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        switch (ext)
        {
            case ".mp3": return AudioType.MPEG;
            case ".wav": return AudioType.WAV;
            case ".ogg": return AudioType.OGGVORBIS;
            case ".aiff":
            case ".aif": return AudioType.AIFF;
            default:
                Debug.LogWarning("PRISM: unknown audio extension " + ext + ", assuming MP3");
                return AudioType.MPEG;
        }
    }

    private void LogClipInfo(AudioClip clip)
    {
        Debug.Log("=== PRISM Audio ===");
        Debug.Log("Name: " + clip.name);
        Debug.Log("Duration: " + clip.length.ToString("F1") + "s");
        Debug.Log("Sample Rate: " + clip.frequency + " Hz");
        Debug.Log("Channels: " + clip.channels);
    }

    public AudioClip GetLoadedClip() => _loadedClip;
}
