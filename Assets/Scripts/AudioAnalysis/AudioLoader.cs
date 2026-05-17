using UnityEngine;

public class AudioLoader : MonoBehaviour
{
    [SerializeField] private AudioClip testClip;
    private AudioSource audioSource;

    private void Start()
    {
        audioSource = gameObject.AddComponent<AudioSource>();

        if(testClip != null)
        {
            audioSource.clip = testClip;
            ReadBasicAudioData(testClip);
            audioSource.Play();
        }
        else
        {
            Debug.LogWarning("[Audio Loader] No audio clip assigned");
        }
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
}
