using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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
    public string uiLanguageId = "en";
    public string contentLanguageId = "en";

    [Header("Accessibility")]
    public bool reduceMotion = false;
    public bool hapticsEnabled = true;
    [Range(0.5f, 2f)] public float uiAnimationSpeed = 1f;

    [Header("User")]
    public string username = "Player";
}
