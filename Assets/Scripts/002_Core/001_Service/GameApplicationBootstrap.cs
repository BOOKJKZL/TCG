using System;
using System.IO;
using Gacha.Application;
using Gacha.Infrastructure.Content;
using Gacha.Infrastructure.Rules;
using Gacha.Presentation;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.ResourceManagement.AsyncOperations;

public static class GameApplicationBootstrap
{
    private const string UiLanguageKey = "settings.ui-language";
    private const string ContentLanguageKey = "settings.content-language";
    private const string SoundEnabledKey = "settings.sound-enabled";
    private const string ReduceMotionKey = "settings.reduce-motion";
    private const string HapticsEnabledKey = "settings.haptics-enabled";
    private const string AnimationSpeedKey = "settings.animation-speed";
    private static bool configured;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        configured = false;
        ApplicationServices.Reset();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ConfigureBeforeFirstScene()
    {
        EnsureConfigured();
    }

    public static void EnsureConfigured()
    {
        if (configured && ApplicationServices.IsConfigured)
            return;

        var languageStore = new PlayerPrefsLanguagePreferenceStore(UiLanguageKey, ContentLanguageKey);
        var languages = new LanguageSelectionService(languageStore, new[] { "en", "zh" });
        var experienceSettings = new ExperienceSettingsService(new PlayerPrefsExperienceSettingsStore());
        string contentRoot = ResolveContentRoot();
        var catalog = new CatalogSession(new PrivateContentCatalogProvider(contentRoot));
        var images = new PrivateContentImageSource(contentRoot);
        var contentPackages = new ContentPackagePlanner(
            new FileSystemInstalledContentPackageRegistry(contentRoot),
            new FileSystemContentStorageProbe(contentRoot));
        ApplicationServices.Configure(
            catalog,
            languages,
            images,
            new PokemonHistoricalRuleProvider(),
            experienceSettings,
            contentPackages,
            new FileSystemContentPackageInstaller(contentRoot));
        languages.UiLanguageChanged += ApplyUiLocale;
        experienceSettings.Changed += ApplyExperienceSettings;
        ApplyExperienceSettings(experienceSettings.Current);
        configured = true;

        if (Directory.Exists(contentRoot))
        {
            CatalogLoadResult result = catalog.EnsureLoaded();
            if (result.Succeeded)
                languages.RefreshContentLanguage(result.Catalog);
        }

        AsyncOperationHandle<LocalizationSettings> initialization = LocalizationSettings.InitializationOperation;
        if (initialization.IsDone)
            ApplyUiLocale(languages.UiLanguageId);
        else
            initialization.Completed += _ => ApplyUiLocale(languages.UiLanguageId);
    }

    private static string ResolveContentRoot()
    {
#if UNITY_EDITOR
        string projectRoot = Directory.GetParent(UnityEngine.Application.dataPath)?.FullName ?? UnityEngine.Application.dataPath;
        return Path.Combine(projectRoot, "LocalContent", "Imports");
#else
        return Path.Combine(UnityEngine.Application.persistentDataPath, "Content");
#endif
    }

    private static void ApplyUiLocale(string languageId)
    {
        Locale locale = FindLocale(languageId) ?? FindLocale("en");
        if (locale == null)
        {
            Debug.LogWarning($"No Unity Localization locale is installed for '{languageId}' or the English fallback.");
            return;
        }

        if (LocalizationSettings.SelectedLocale != locale)
            LocalizationSettings.SelectedLocale = locale;
    }

    private static Locale FindLocale(string languageId)
    {
        if (string.IsNullOrWhiteSpace(languageId))
            return null;

        Locale locale = LocalizationSettings.AvailableLocales.GetLocale(languageId);
        if (locale != null)
            return locale;

        string normalized = languageId.Replace('_', '-');
        int separator = normalized.IndexOf('-');
        return separator > 0
            ? LocalizationSettings.AvailableLocales.GetLocale(normalized.Substring(0, separator))
            : null;
    }

    private static void ApplyExperienceSettings(ExperienceSettings settings)
    {
        if (settings == null)
            return;

        UIFeedbackService.Configure(
            settings.ReduceMotion,
            settings.HapticsEnabled,
            settings.AnimationSpeed,
            settings.SoundEnabled);
    }

    private sealed class PlayerPrefsLanguagePreferenceStore : ILanguagePreferenceStore
    {
        private readonly string uiLanguageKey;
        private readonly string contentLanguageKey;

        public PlayerPrefsLanguagePreferenceStore(string uiLanguageKey, string contentLanguageKey)
        {
            this.uiLanguageKey = uiLanguageKey;
            this.contentLanguageKey = contentLanguageKey;
        }

        public LanguagePreferences Load()
        {
            return new LanguagePreferences(
                PlayerPrefs.GetString(uiLanguageKey, "en"),
                PlayerPrefs.GetString(contentLanguageKey, "en"));
        }

        public void Save(LanguagePreferences preferences)
        {
            if (preferences == null)
                throw new ArgumentNullException(nameof(preferences));

            PlayerPrefs.SetString(uiLanguageKey, preferences.UiLanguageId);
            PlayerPrefs.SetString(contentLanguageKey, preferences.ContentLanguageId);
            PlayerPrefs.Save();
        }
    }

    private sealed class PlayerPrefsExperienceSettingsStore : IExperienceSettingsStore
    {
        public ExperienceSettings Load()
        {
            return new ExperienceSettings(
                PlayerPrefs.GetInt(SoundEnabledKey, 1) != 0,
                PlayerPrefs.GetInt(ReduceMotionKey, 0) != 0,
                PlayerPrefs.GetInt(HapticsEnabledKey, 1) != 0,
                PlayerPrefs.GetFloat(AnimationSpeedKey, 1f));
        }

        public void Save(ExperienceSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            PlayerPrefs.SetInt(SoundEnabledKey, settings.SoundEnabled ? 1 : 0);
            PlayerPrefs.SetInt(ReduceMotionKey, settings.ReduceMotion ? 1 : 0);
            PlayerPrefs.SetInt(HapticsEnabledKey, settings.HapticsEnabled ? 1 : 0);
            PlayerPrefs.SetFloat(AnimationSpeedKey, settings.AnimationSpeed);
            PlayerPrefs.Save();
        }
    }
}
