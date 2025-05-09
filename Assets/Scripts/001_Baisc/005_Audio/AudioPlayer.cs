using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AudioPlayer : MonoBehaviour
{
    public List<AudioClip> musicClip;
    public List<AudioClip> sfxClip;
    public List<AudioClip> effectsClip;

    private void Start()
    {
        PlayAudio(musicClip, AudioType.music);
        PlayAudio(sfxClip, AudioType.sfx);
        PlayAudio(effectsClip,AudioType.effect);
    }

    public void PlayAudio(List<AudioClip> clipList,AudioType type)
    {
        for (int i = 0; i < clipList.Count; i++){
            if (clipList[i] != null)
            {
                switch (type)
                {
                    case AudioType.music:
                        GameManager.Instance.audioManager.PlayMusic(clipList[i], i); break;
                    case AudioType.sfx:
                        GameManager.Instance.audioManager.PlaySFX(clipList[i], i); break;
                    case AudioType.effect:
                        GameManager.Instance.audioManager.PlayEffect(clipList[i], i); break;
                }
            }
        }
    }
}
