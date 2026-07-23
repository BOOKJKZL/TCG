using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gacha.Application;
using Gacha.Presentation;
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
    public TMP_Dropdown contentLangDD;

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
            GameApplicationBootstrap.EnsureConfigured();
            SyncLanguageSettings();
            ConfigureFeedbackSettings();
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
        ConfigureFeedbackSettings();
        GetFPS();

        GetMusicVolume();
        GetSFXVolume();
        GetEffectVolume();
        GetMasterVolume();

        RefreshLanguageDropdowns();

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
        if (!ApplicationServices.IsConfigured)
            return;

        IReadOnlyList<string> languages = ApplicationServices.Languages.AvailableUiLanguageIds;
        if (choose < 0 || choose >= languages.Count)
            return;

        ApplicationServices.Languages.SelectUiLanguage(languages[choose]);
        SyncLanguageSettings();
    }

    public void GetTextLangDD(int choose)
    {
        if (textLangDD == null || !ApplicationServices.IsConfigured)
            return;

        List<string> newOptions = ApplicationServices.Languages.AvailableUiLanguageIds.ToList();
        textLangDD.ClearOptions();
        textLangDD.AddOptions(newOptions);
        textLangDD.SetValueWithoutNotify(Mathf.Clamp(choose, 0, Mathf.Max(0, newOptions.Count - 1)));
    }

    public void SetContentLanguage(int choose)
    {
        if (!ApplicationServices.IsConfigured || !ApplicationServices.Catalog.IsReady)
            return;

        string[] languages = ApplicationServices.Catalog.Catalog.Languages.Keys
            .OrderBy(value => value, System.StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (choose < 0 || choose >= languages.Length)
            return;

        ApplicationServices.Languages.SelectContentLanguage(languages[choose], ApplicationServices.Catalog.Catalog);
        SyncLanguageSettings();
    }

    public void GetUsername()
    {
        usernameInput.text = settingData.username;
    }

    public void OnUsernameChanged(string change)
    {
        settingData.username = usernameInput.text;
    }

    public void SetReduceMotion(bool enabled)
    {
        settingData.reduceMotion = enabled;
        ConfigureFeedbackSettings();
    }

    public void SetHapticsEnabled(bool enabled)
    {
        settingData.hapticsEnabled = enabled;
        ConfigureFeedbackSettings();
    }

    public void SetUIAnimationSpeed(float speed)
    {
        settingData.uiAnimationSpeed = Mathf.Clamp(speed, 0.5f, 2f);
        ConfigureFeedbackSettings();
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

    private void ConfigureFeedbackSettings()
    {
        if (settingData == null)
        {
            return;
        }

        UIFeedbackService.Configure(
            settingData.reduceMotion,
            settingData.hapticsEnabled,
            settingData.uiAnimationSpeed);
    }

    private void RefreshLanguageDropdowns()
    {
        if (!ApplicationServices.IsConfigured)
            return;

        IReadOnlyList<string> uiLanguages = ApplicationServices.Languages.AvailableUiLanguageIds;
        int uiIndex = uiLanguages.ToList().FindIndex(value => string.Equals(
            value,
            ApplicationServices.Languages.UiLanguageId,
            System.StringComparison.OrdinalIgnoreCase));
        GetTextLangDD(Mathf.Max(0, uiIndex));

        if (contentLangDD == null || !ApplicationServices.Catalog.IsReady)
            return;

        string[] contentLanguages = ApplicationServices.Catalog.Catalog.Languages.Keys
            .OrderBy(value => value, System.StringComparer.OrdinalIgnoreCase)
            .ToArray();
        contentLangDD.ClearOptions();
        contentLangDD.AddOptions(contentLanguages.ToList());
        int contentIndex = System.Array.FindIndex(contentLanguages, value => string.Equals(
            value,
            ApplicationServices.Languages.ContentLanguage.ResolvedLanguageId,
            System.StringComparison.OrdinalIgnoreCase));
        contentLangDD.SetValueWithoutNotify(Mathf.Max(0, contentIndex));
    }

    private void SyncLanguageSettings()
    {
        if (settingData == null || !ApplicationServices.IsConfigured)
            return;

        settingData.uiLanguageId = ApplicationServices.Languages.UiLanguageId;
        settingData.contentLanguageId = ApplicationServices.Languages.RequestedContentLanguageId;
    }
}
