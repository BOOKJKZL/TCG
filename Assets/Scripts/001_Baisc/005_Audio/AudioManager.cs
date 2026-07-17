using System.Collections;
using System.Collections.Generic;
using Gacha.Presentation;
using UnityEngine;

public enum AudioType { music, sfx, effect }

public class AudioManager : MonoBehaviour, IAudioFeedbackSink
{
    public List<AudioSource> musicSources;
    public List<AudioSource> sfxSources;
    public List<AudioSource> effectsSources;

    public AudioClipConfig audioConfig;
    private Dictionary<string, AudioClip> audioClips = new Dictionary<string, AudioClip>();

    private float musicVolume = 1f;
    private float sfxVolume = 1f;
    private float effectsVolume = 1f;
    private float masterVolume = 1f;

    void Awake()
    {
        musicSources ??= new List<AudioSource>();
        sfxSources ??= new List<AudioSource>();
        effectsSources ??= new List<AudioSource>();

        LoadAudioClipsFromConfig();
        EnsureFeedbackSource();
        EnsureRuntimeFeedbackClips();
    }

    private void OnEnable()
    {
        UIFeedbackService.RegisterAudioSink(this);
    }

    private void OnDisable()
    {
        UIFeedbackService.UnregisterAudioSink(this);
    }

    private void LoadAudioClipsFromConfig()
    {
        if (audioConfig == null || audioConfig.audioEntries == null)
        {
            return;
        }

        foreach (var entry in audioConfig.audioEntries)
        {
            if (entry != null && !string.IsNullOrWhiteSpace(entry.key) && entry.clip != null && !audioClips.ContainsKey(entry.key))
            {
                audioClips.Add(entry.key, entry.clip);
            }
        }
    }

    public AudioClip GetAudioClip(string key)
    {
        audioClips.TryGetValue(key, out AudioClip clip);
        return clip;
    }

    // Method to add audio clips to the dictionary
    public void AddAudioClip(string name, AudioClip clip)
    {
        if (!string.IsNullOrWhiteSpace(name) && clip != null && !audioClips.ContainsKey(name))
        {
            audioClips.Add(name, clip);
        }
    }

    public void AddAudioClip(AudioClip clip)
    {
        if (clip == null)
        {
            return;
        }

        string name = clip.name;

        if (!audioClips.ContainsKey(name))
        {
            audioClips.Add(name, clip);
        }
    }

    // Method to play music on a specific source
    public void PlayMusic(string name, int sourceIndex)
    {
        if (audioClips.ContainsKey(name) && sourceIndex >= 0 && sourceIndex < musicSources.Count)
        {
            musicSources[sourceIndex].clip = audioClips[name];
            musicSources[sourceIndex].Play();
        }
        else
        {
            Debug.LogWarning("Music clip '" + name + "' not found or invalid source index.");
        }
    }

    // Method to play SFX on a specific source
    public void PlaySFX(string name, int sourceIndex)
    {
        if (audioClips.ContainsKey(name) && sourceIndex >= 0 && sourceIndex < sfxSources.Count)
        {
            sfxSources[sourceIndex].PlayOneShot(audioClips[name]);
        }
        else
        {
            Debug.LogWarning("SFX clip '" + name + "' not found or invalid source index.");
        }
    }

    // Method to play effects on a specific source
    public void PlayEffect(string name, int sourceIndex)
    {
        if (audioClips.TryGetValue(name, out AudioClip clip) && TryGetSource(effectsSources, sourceIndex, out AudioSource source))
        {
            source.PlayOneShot(clip);
        }
        else
        {
            Debug.LogWarning("Effect clip '" + name + "' not found or invalid source index.");
        }
    }

    public bool TryPlay(string cueKey)
    {
        if (!audioClips.TryGetValue(cueKey, out AudioClip clip) ||
            !TryGetSource(effectsSources, 0, out AudioSource source))
        {
            return false;
        }

        source.PlayOneShot(clip);
        return true;
    }

    // Method to play music on a specific source
    public void PlayMusic(AudioClip clip, int sourceIndex)
    {
        if (clip != null && sourceIndex >= 0 && sourceIndex < musicSources.Count)
        {
            musicSources[sourceIndex].clip = clip;
            musicSources[sourceIndex].Play();
        }
        else
        {
            Debug.LogWarning("Invalid audio clip or source index.");
        }
    }

    // Method to play SFX on a specific source
    public void PlaySFX(AudioClip clip, int sourceIndex)
    {
        if (clip != null && sourceIndex >= 0 && sourceIndex < sfxSources.Count)
        {
            sfxSources[sourceIndex].PlayOneShot(clip);
        }
        else
        {
            Debug.LogWarning("Invalid audio clip or source index.");
        }
    }

    // Method to play effects on a specific source
    public void PlayEffect(AudioClip clip, int sourceIndex)
    {
        if (clip != null && sourceIndex >= 0 && sourceIndex < effectsSources.Count)
        {
            effectsSources[sourceIndex].PlayOneShot(clip);
        }
        else
        {
            Debug.LogWarning("Invalid audio clip or source index.");
        }
    }

    // Method to stop specific music
    public void StopMusic(int sourceIndex)
    {
        if (sourceIndex >= 0 && sourceIndex < musicSources.Count)
        {
            musicSources[sourceIndex].Stop();
        }
        else
        {
            Debug.LogWarning("Invalid music source index.");
        }
    }

    // Method to stop specific SFX
    public void StopSFX(int sourceIndex)
    {
        if (sourceIndex >= 0 && sourceIndex < sfxSources.Count)
        {
            sfxSources[sourceIndex].Stop();
        }
        else
        {
            Debug.LogWarning("Invalid SFX source index.");
        }
    }

    // Method to stop specific effect
    public void StopEffect(int sourceIndex)
    {
        if (sourceIndex >= 0 && sourceIndex < effectsSources.Count)
        {
            effectsSources[sourceIndex].Stop();
        }
        else
        {
            Debug.LogWarning("Invalid effects source index.");
        }
    }

    // Method to stop all music
    public void StopAllMusic()
    {
        foreach (var source in musicSources)
        {
            source.Stop();
        }
    }

    // Method to stop all SFX
    public void StopAllSFX()
    {
        foreach (var source in sfxSources)
        {
            source.Stop();
        }
    }

    // Method to stop all effects
    public void StopAllEffects()
    {
        foreach (var source in effectsSources)
        {
            source.Stop();
        }
    }

    // Method to stop all audio
    public void StopAllAudio()
    {
        StopAllMusic();
        StopAllSFX();
        StopAllEffects();
    }

    // Method to set the music volume for all music sources
    public void SetMusicVolume(float volume)
    {
        musicVolume = Mathf.Clamp(volume, 0f, 1f);
        foreach (var source in musicSources)
        {
            source.volume = musicVolume * masterVolume;
        }
    }

    // Method to set the SFX volume for all SFX sources
    public void SetSFXVolume(float volume)
    {
        sfxVolume = Mathf.Clamp(volume, 0f, 1f);
        foreach (var source in sfxSources)
        {
            source.volume = sfxVolume * masterVolume;
        }
    }

    // Method to set the effects volume for all effects sources
    public void SetEffectsVolume(float volume)
    {
        effectsVolume = Mathf.Clamp(volume, 0f, 1f);
        foreach (var source in effectsSources)
        {
            source.volume = effectsVolume * masterVolume;
        }
    }

    // Method to set the master volume
    public void SetMasterVolume(float volume)
    {
        masterVolume = Mathf.Clamp(volume, 0f, 1f);
        SetMusicVolume(musicVolume);
        SetSFXVolume(sfxVolume);
        SetEffectsVolume(effectsVolume);
    }

    private static bool TryGetSource(List<AudioSource> sources, int requestedIndex, out AudioSource source)
    {
        source = null;
        if (sources == null || sources.Count == 0)
        {
            return false;
        }

        int index = requestedIndex >= 0 && requestedIndex < sources.Count ? requestedIndex : 0;
        source = sources[index];
        return source != null;
    }

    private void EnsureFeedbackSource()
    {
        if (effectsSources.Count > 0 && effectsSources[0] != null)
        {
            return;
        }

        GameObject sourceObject = new GameObject("Runtime UI Effects");
        sourceObject.transform.SetParent(transform, false);
        AudioSource source = sourceObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.volume = effectsVolume * masterVolume;
        effectsSources.RemoveAll(item => item == null);
        effectsSources.Insert(0, source);
    }

    private void EnsureRuntimeFeedbackClips()
    {
        AudioClip click = GetAudioClip(FeedbackCueKeys.ButtonClick);
        if (click == null)
        {
            click = CreateProceduralClick("Runtime UI Click", 950f, 0.035f);
            AddAudioClip(FeedbackCueKeys.ButtonClick, click);
        }

        AddAudioClip(FeedbackCueKeys.Confirm, click);
        AddAudioClip("setting_click", click);
        AddAudioClip("pause_click", click);

        if (GetAudioClip(FeedbackCueKeys.Back) == null)
        {
            AddAudioClip(FeedbackCueKeys.Back, CreateProceduralClick("Runtime UI Back", 650f, 0.045f));
        }
    }

    private static AudioClip CreateProceduralClick(string clipName, float frequency, float duration)
    {
        const int sampleRate = 44100;
        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            float progress = (float)i / sampleCount;
            float envelope = (1f - progress) * (1f - progress);
            samples[i] = Mathf.Sin(2f * Mathf.PI * frequency * i / sampleRate) * envelope * 0.18f;
        }

        AudioClip clip = AudioClip.Create(clipName, sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}
