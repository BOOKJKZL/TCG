using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum Language { en, zh }
[CreateAssetMenu(fileName = "SettingData", menuName = "Basic/SettingData", order = 100)]
[System.Serializable]
public class SettingData : ScriptableObject
{
    [Header("Display")]
    public int fps = 30;

    [Header("Sound")]
    public float masterVolume = 1f;
    public float musicVolume = 1f;
    public float sfxVolume = 1f;
    public float effectsVolume = 1f;

    [Header("Language")]
    public Language langMode = Language.en;

    [Header("User")]
    public string username = "Player";
}
