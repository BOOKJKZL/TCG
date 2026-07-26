using System;
using System.IO;
using Gacha.Application;
using Gacha.Infrastructure.Content;
using Gacha.Infrastructure.Rules;
using Gacha.Pokemon.Presentation;
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
    private const string CatalogUrlEnvironmentKey = "GACHA_CONTENT_CATALOG_URL";
    private const string CatalogCachePathEnvironmentKey = "GACHA_CONTENT_CATALOG_CACHE_PATH";
    private const string BundledRemoteConfigResource = "Data/RemoteContent";
    private const string PrivateRemoteConfigFile = "remote-content.json";
    private static bool configured;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        configured = false;
        ApplicationServices.Reset();
        ProductOpeningThemeService.Reset();
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
        var catalog = new CatalogSession(new PrivateContentCatalogProvider(
            contentRoot,
            variantPolicy: new PokemonImportedCardVariantPolicy()));
        var images = new PrivateContentImageSource(contentRoot);
        var contentPackages = new ContentPackagePlanner(
            new FileSystemInstalledContentPackageRegistry(contentRoot),
            new FileSystemContentStorageProbe(contentRoot));
        var contentPackageInstaller = new FileSystemContentPackageInstaller(contentRoot);
        var contentPackageLifecycle = new FileSystemContentPackageLifecycleService(contentRoot);
        var contentPackageOperations = new HttpContentPackageInstallCoordinatorFactory(
            ResolveDownloadRoot(),
            contentPackages,
            contentPackageInstaller);
        IContentPackageCatalogProvider contentPackageCatalogs = CreateRemoteContentCatalogProvider();
        ApplicationServices.Configure(
            catalog,
            languages,
            images,
            new PokemonRuleProvider(),
            experienceSettings,
            contentPackages,
            contentPackageInstaller,
            contentPackageOperations,
            contentPackageCatalogs,
            contentPackageLifecycle);
        ProductOpeningThemeService.Configure(new PokemonProductOpeningThemeProvider());
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

    private static string ResolveDownloadRoot()
    {
#if UNITY_EDITOR
        string projectRoot = Directory.GetParent(UnityEngine.Application.dataPath)?.FullName ?? UnityEngine.Application.dataPath;
        return Path.Combine(projectRoot, "LocalContent", "Downloads");
#else
        return Path.Combine(UnityEngine.Application.persistentDataPath, "ContentDownloads");
#endif
    }

    private static IContentPackageCatalogProvider CreateRemoteContentCatalogProvider()
    {
        try
        {
            RemoteContentConfiguration configuration = LoadRemoteContentConfiguration();
            if (configuration == null || string.IsNullOrWhiteSpace(configuration.catalogUrl))
                return null;
            if (!Uri.TryCreate(configuration.catalogUrl.Trim(), UriKind.Absolute, out Uri catalogUri))
            {
                Debug.LogWarning("Remote content configuration has an invalid catalogUrl.");
                return null;
            }

            int maximumBytes = configuration.maxCatalogBytes > 0
                ? configuration.maxCatalogBytes
                : HttpContentPackageCatalogProvider.DefaultMaximumCatalogBytes;
            TimeSpan timeout = configuration.timeoutSeconds > 0
                ? TimeSpan.FromSeconds(configuration.timeoutSeconds)
                : HttpContentPackageCatalogProvider.DefaultTimeout;
            var provider = new HttpContentPackageCatalogProvider(
                catalogUri,
                maximumCatalogBytes: maximumBytes,
                timeout: timeout);
            var cachedProvider = new CachedContentPackageCatalogProvider(
                provider,
                ResolveCatalogCachePath(),
                catalogUri,
                maximumBytes);
            Debug.Log("Remote content catalog and its verified offline cache are configured from private runtime settings.");
            return cachedProvider;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Remote content configuration was ignored: " + exception.Message);
            return null;
        }
    }

    private static string ResolveCatalogCachePath()
    {
#if UNITY_EDITOR
        string overridePath = Environment.GetEnvironmentVariable(CatalogCachePathEnvironmentKey);
        if (!string.IsNullOrWhiteSpace(overridePath))
            return Path.GetFullPath(overridePath.Trim());
#endif
        return Path.Combine(ResolveDownloadRoot(), "catalog-cache-v1.json");
    }

    private static RemoteContentConfiguration LoadRemoteContentConfiguration()
    {
#if UNITY_EDITOR
        string environmentUrl = Environment.GetEnvironmentVariable(CatalogUrlEnvironmentKey);
        if (!string.IsNullOrWhiteSpace(environmentUrl))
            return new RemoteContentConfiguration { catalogUrl = environmentUrl };
#endif

        string privatePath = ResolveRemoteContentConfigurationPath();
        if (File.Exists(privatePath))
            return ParseRemoteContentConfiguration(File.ReadAllText(privatePath));

        TextAsset bundled = Resources.Load<TextAsset>(BundledRemoteConfigResource);
        return bundled == null ? null : ParseRemoteContentConfiguration(bundled.text);
    }

    private static RemoteContentConfiguration ParseRemoteContentConfiguration(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("Remote content configuration is empty.");
        RemoteContentConfiguration configuration = JsonUtility.FromJson<RemoteContentConfiguration>(json);
        return configuration ?? throw new InvalidDataException("Remote content configuration has no root object.");
    }

    private static string ResolveRemoteContentConfigurationPath()
    {
#if UNITY_EDITOR
        string projectRoot = Directory.GetParent(UnityEngine.Application.dataPath)?.FullName ?? UnityEngine.Application.dataPath;
        return Path.Combine(projectRoot, "LocalContent", PrivateRemoteConfigFile);
#else
        return Path.Combine(UnityEngine.Application.persistentDataPath, PrivateRemoteConfigFile);
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

    [Serializable]
    private sealed class RemoteContentConfiguration
    {
        public string catalogUrl;
        public int timeoutSeconds;
        public int maxCatalogBytes;
    }
}
