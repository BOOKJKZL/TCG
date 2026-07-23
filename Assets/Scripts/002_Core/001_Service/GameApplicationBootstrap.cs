using System;
using System.IO;
using Gacha.Application;
using Gacha.Infrastructure.Content;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.ResourceManagement.AsyncOperations;

public static class GameApplicationBootstrap
{
    private const string UiLanguageKey = "settings.ui-language";
    private const string ContentLanguageKey = "settings.content-language";
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
        string contentRoot = ResolveContentRoot();
        var catalog = new CatalogSession(new PrivateContentCatalogProvider(contentRoot));
        ApplicationServices.Configure(catalog, languages);
        languages.UiLanguageChanged += ApplyUiLocale;
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
}
