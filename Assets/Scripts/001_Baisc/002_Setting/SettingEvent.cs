using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SettingEvent : MonoBehaviour
{
    //Display
    public void OnFPSChanged(int choose)
    {
        SettingManager.Instance.SetFPS(choose);
    }

    //Sound
    public void SetMasterVolume(float volume)
    {
        SettingManager.Instance.SetMasterVolume(volume);
    }

    public void SetMusicVolume(float volume)
    {
        SettingManager.Instance.SetMusicVolume(volume);
    }

    public void SetSFXolume(float volume)
    {
        SettingManager.Instance.SetSFXVolume(volume);
    }

    public void SetEffectVolume(float volume)
    {
        SettingManager.Instance.SetEffectVolume(volume);
    }

    //Language
    public void OnLangChanged(int choose)
    {
        SettingManager.Instance.SetLanguage(choose);
    }

    public void OnUsernameChanged(string change)
    {
        SettingManager.Instance.OnUsernameChanged(change);
    }

    public void OnResetClick()
    {
        SettingManager.Instance.OnResetClick();
    }
}
