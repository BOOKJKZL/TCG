using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum AudioType { music, sfx, effect }

public class AudioManager : MonoBehaviour
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
       LoadAudioClipsFromConfig();
    }

    private void LoadAudioClipsFromConfig()
    {
        foreach (var entry in audioConfig.audioEntries)
        {
            if (!audioClips.ContainsKey(entry.key))
            {
                audioClips.Add(entry.key, entry.clip);
            }
        }
    }

    public AudioClip GetAudioClip(string key)
    {
        return audioClips[key];
    }

    // Method to add audio clips to the dictionary
    public void AddAudioClip(string name, AudioClip clip)
    {
        if (!audioClips.ContainsKey(name))
        {
            audioClips.Add(name, clip);
        }
    }

    public void AddAudioClip(AudioClip clip)
    {
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
        if (audioClips.ContainsKey(name) && sourceIndex >= 0 && sourceIndex < effectsSources.Count)
        {
            effectsSources[sourceIndex].PlayOneShot(audioClips[name]);
        }
        else
        {
            Debug.LogWarning("Effect clip '" + name + "' not found or invalid source index.");
        }
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
}
