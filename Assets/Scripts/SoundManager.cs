using UnityEngine;
using UnityEngine.Audio;

public class SoundManager : MonoBehaviour
{
    [SerializeField] private AudioMixerGroup outputMixerGroup;

    private static AudioMixerGroup s_outputMixerGroup;

    private void Awake()
    {
        s_outputMixerGroup = outputMixerGroup;
    }

    public static void PlaySound(string soundName)
    {
        PlaySound(soundName, -1f, -1f);
    }

    public static void PlaySound(string soundName, float customVolume = -1f, float customPitch = -1f)
    {
        // Load the audio clip from Resources/Sounds
        AudioClip clip = Resources.Load<AudioClip>("Sounds/" + soundName);

        if (clip == null)
        {
            Debug.LogWarning($"Sound '{soundName}' not found in Resources/Sounds");
            return;
        }

        // Default values
        float volume = 1f;
        float pitch = 1f;
        bool loop = false;
        bool varyPitch = false;

        // Check for special behavior for specific sounds
        switch (soundName)
        {
            case "Example":
                break;
        }

        // Use custom pitch if provided, otherwise vary pitch randomly
        if (customPitch > 0)
        {
            pitch = customPitch;
        }
        else if (varyPitch)
        {
            pitch = Random.Range(0.85f, 1.15f);
        }

        Play(clip, (customVolume > 0) ? customVolume : volume, pitch, loop);
    }

    // Plays a clip directly through the output mixer group on its own AudioSource, so
    // overlapping plays can each have their own pitch.
    public static void PlayClip(AudioClip clip, float volume = 1f, float pitch = 1f)
    {
        if (clip == null) return;
        Play(clip, volume, pitch, false);
    }

    static void Play(AudioClip clip, float volume, float pitch, bool loop)
    {
        // Create a temporary GameObject with an AudioSource
        GameObject soundObject = new GameObject("Sound_" + clip.name);
        AudioSource audioSource = soundObject.AddComponent<AudioSource>();

        // Configure the AudioSource
        audioSource.clip = clip;
        audioSource.outputAudioMixerGroup = s_outputMixerGroup;
        audioSource.volume = volume;
        audioSource.pitch = pitch;
        audioSource.loop = loop;

        // Play the sound
        audioSource.Play();

        // Clean up: destroy the GameObject after the clip finishes (unless looping)
        if (!loop)
        {
            Destroy(soundObject, clip.length / Mathf.Abs(pitch));
        }
    }
}