using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

public class SettingManager : MonoBehaviour
{
    public static SettingManager Instance { get; private set; }
    
    public SettingData settingData;
    public GameObject settingPage;
    Fade fade;

    [Header("Display")]
    public TMP_Dropdown fpsDD;

    [Header("Sound")]
    public Slider masterSlider;
    public TMP_Text masterVolume;
    public Slider musicSlider;
    public TMP_Text musicVolume;
    public Slider sfxSlider;
    public TMP_Text sfxVolume;
    public Slider effectSlider;
    public TMP_Text effectVolume;

    [Header("Language")]
    public TMP_Dropdown textLangDD;

    [Header("User")]
    public TMP_InputField usernameInput;
    public SettingData resetSettingData;
    //public LevelData resetLevelData;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);

            settingData = SaveFileHelper.ReadFileFirstTime(settingData);
        }
        else
        {
            Destroy(gameObject);
        }

        if(fade == null)
        {
            fade = settingPage.GetComponent<Fade>();
        }
    }

    void Start()
    {
        GetStartData();
    }

    public void GetStartData()
    {
        GetFPS();

        GetMusicVolume();
        GetSFXVolume();
        GetEffectVolume();
        GetMasterVolume();

        //LocalizationManager.Instance.OnLanguageChanged += () =>
        //{
        //    GetTextLangDD((int)LocalizationManager.Instance.CurrentLanguage);
        //};

        GetUsername();
    }

    //Display
    public void SetFPS(int choose)
    {
        switch (choose)
        {
            case 0:
                settingData.fps = 120;
                break;
            case 1:
                settingData.fps = 90;
                break;
            case 2:
                settingData.fps = 60;
                break;
            case 3:
                settingData.fps = 30;
                break;
        }

        Application.targetFrameRate = settingData.fps;
    }

    public void GetFPS()
    {
        switch (settingData.fps)
        {
            case 30:
                fpsDD.value = 3;
                break;
            case 60:
                fpsDD.value = 2;
                break;
            case 90:
                fpsDD.value = 1;
                break;
            case 120:
                fpsDD.value = 0;
                break;
        }

        Application.targetFrameRate = settingData.fps;
    }

    //Sound
    public void SetMasterVolume(float volume)
    {
        masterVolume.text = ((int)volume).ToString();
        GameManager.Instance.audioManager.SetMasterVolume(volume/100f);
        settingData.masterVolume = volume/100f;
    }

    public void GetMasterVolume()
    {
        masterSlider.value = settingData.masterVolume * 100;
        SetMasterVolume(settingData.masterVolume * 100);
    }

    public void SetMusicVolume(float volume)
    {
        musicVolume.text = ((int)volume).ToString();
        GameManager.Instance.audioManager.SetMusicVolume(volume / 100f);
        settingData.musicVolume = volume/100f;
    }

    public void GetMusicVolume()
    {
        musicSlider.value = settingData.musicVolume * 100;
        SetMusicVolume(settingData.musicVolume * 100);
    }

    public void SetSFXVolume(float volume)
    {
        sfxVolume.text = ((int)volume).ToString();
        GameManager.Instance.audioManager.SetSFXVolume(volume / 100f);
        settingData.sfxVolume = volume/100f;
    }

    public void GetSFXVolume()
    {
        sfxSlider.value = settingData.sfxVolume * 100;
        SetSFXVolume(settingData.sfxVolume * 100);
    }

    public void SetEffectVolume(float volume)
    {
        effectVolume.text = ((int)volume).ToString();
        GameManager.Instance.audioManager.SetEffectsVolume(volume / 100f);
        settingData.effectsVolume = volume/100f;
    }

    public void GetEffectVolume()
    {
        effectSlider.value = settingData.effectsVolume * 100;
        SetEffectVolume(settingData.effectsVolume * 100);
    }

    //Language
    public void SetLanguage(int choose)
    {
        switch (choose)
        {
            case 0:
                settingData.langMode = Language.en; 
                break;
            case 1:
                settingData.langMode = Language.zh; 
                break;
        }
        //if(settingData.langMode != LocalizationManager.Instance.CurrentLanguage)
        //{
        //    LocalizationManager.Instance.ChangeLanguage(settingData.langMode);
        //}
    }

    public void GetTextLangDD(int choose)
    {
        // Create a new list of options
        List<string> newOptions = new List<string>
        {
            //LocalizationManager.Instance.GetLocalizedText("en"),
            //LocalizationManager.Instance.GetLocalizedText("zh")
        };

        // Clear the old options
        textLangDD.ClearOptions();

        // Add the new options
        textLangDD.AddOptions(newOptions);

        textLangDD.value = choose;
    }

    public void GetUsername()
    {
        usernameInput.text = settingData.username;
    }

    public void OnUsernameChanged(string change)
    {
        settingData.username = usernameInput.text;
    }

    public void OnResetClick()
    {
        settingData = Instantiate(resetSettingData);
        SaveFileHelper.WriteFile(settingData);
        //GameManager.Instance.levelData = Instantiate(resetLevelData);
        //SaveFileHelper.WriteFile(GameManager.Instance.levelData);
        // Quit the application
        Application.Quit();

        // For testing in Unity Editor
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    public void OpenSetting()
    {
        GameManager.Instance.audioManager.PlayEffect("setting_click", 1);
        settingPage.SetActive(true);
        fade.StartFadeIn();
    }

    public void CloseSetting()
    {
        GameManager.Instance.audioManager.PlayEffect("pause_click", 1);
        SaveFileHelper.WriteFile(settingData);
        SettingPanelControl.Instance.DisableSettingPage();
        fade.StartFadeOut(() => {
            settingPage.SetActive(false);
        }); 
    }
}
