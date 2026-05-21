using UnityEngine;
using System.IO;

public class AudioLoader : MonoBehaviour
{
    [Header("Load Mode")]
    [SerializeField] private bool useDynamicPath = false;

    [Header("Inspector Mode")]
    [SerializeField] private AudioClip inspectorClip;

    [Header("Dynamic Mode")]
    [SerializeField] private string filePath = "";

    private AudioClip loadedClip;
    private AudioSource audioSource;

    private void Start()
    {
        audioSource = gameObject.AddComponent<AudioSource>();

        if(useDynamicPath)
        {
            loadFromPath(filePath);
        }
        else
        {
            if(inspectorClip != null)
            {
                loadedClip = inspectorClip;
                OnClipLoaded();
            }
            else
            {
                Debug.LogWarning("PRISM: No Clip assigned in Inspector");
            }
        }
    }

    private void loadFromPath(string path)
    {
        if(!File.Exists(path))
        {
            Debug.LogWarning("PRSIM: File not found at path: " + path);
            return;
        }

        StartCoroutine(LoadAudioCoroutine(path));
    }

    private System.Collections.IEnumerator LoadAudioCoroutine(string path)
    {
        string url = "file://" + path;
        using (var request = UnityEngine.Networking.UnityWebRequestMultimedia.GetAudioClip(url, AudioType.MPEG))
        {
            yield return request.SendWebRequest();

            if(request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                loadedClip = UnityEngine.Networking.DownloadHandlerAudioClip.GetContent(request);
                loadedClip.name = Path.GetFileNameWithoutExtension(path);
                OnClipLoaded();
            }
            else
            {
                Debug.LogWarning("PRSIM: Failed to load audio - " + request.error);
            }
        }
    }

    private void OnClipLoaded()
    {
        ReadBasicAudioData(loadedClip);
        audioSource.clip = loadedClip;
        audioSource.Play();

        //Hand over to analyser//
        AudioAnalyser analyser = GetComponent<AudioAnalyser>();
        if (analyser != null)
        {
            analyser.Init(audioSource);
            analyser.Analyse(loadedClip);
        }

        
        StartCoroutine(WaitForAnalysisThenGenerate(analyser));
        

    }
    void ReadBasicAudioData(AudioClip clip)
    {
        Debug.Log("=== PRISM Audio Analysis ===");
        Debug.Log("Name: "  + clip.name);
        Debug.Log("Duration: " + clip.length + " seconds");
        Debug.Log("Sample Rate: " + clip.frequency + " Hz");
        Debug.Log("Channels: " + clip.channels);
        Debug.Log("Total Samples: " + clip.samples);
    }

    private System.Collections.IEnumerator WaitForAnalysisThenGenerate(AudioAnalyser analyser)
    {
        //Wait until spectrum analysis is complete//
        while(!analyser.AnalysisComplete)
        {
            yield return null;
        }

        SpatialGenerator generator = GetComponent<SpatialGenerator>();
        if (generator != null)
        {
            generator.Generate(analyser);
        }
    }

    public AudioClip GetLoadedClip() => loadedClip;
}
