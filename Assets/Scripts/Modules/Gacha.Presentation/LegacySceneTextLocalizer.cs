using System;
using System.Collections.Generic;
using System.Linq;
using Gacha.Application;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.SceneManagement;

namespace Gacha.Presentation
{
    public sealed class LegacySceneTextLocalizer : MonoBehaviour
    {
        private sealed class TextBinding
        {
            public TextBinding(TMP_Text target, string key)
            {
                Target = target;
                Key = key;
            }

            public TMP_Text Target { get; }
            public string Key { get; }
        }

        private readonly List<TextBinding> bindings = new List<TextBinding>();
        private LanguageSelectionService languages;

        public int BindingCount => bindings.Count;

        internal void Initialize(Scene scene, IReadOnlyDictionary<string, string> fallbackTextKeys)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                throw new ArgumentException("A loaded scene is required.", nameof(scene));
            if (fallbackTextKeys == null)
                throw new ArgumentNullException(nameof(fallbackTextKeys));

            bindings.Clear();
            foreach (TMP_Text target in scene.GetRootGameObjects()
                         .SelectMany(root => root.GetComponentsInChildren<TMP_Text>(true)))
            {
                if (target != null && !string.IsNullOrEmpty(target.text) &&
                    fallbackTextKeys.TryGetValue(target.text, out string key))
                    bindings.Add(new TextBinding(target, key));
            }

            AttachLanguageService();
            RefreshText();
        }

        private void OnEnable()
        {
            LocalizationSettings.SelectedLocaleChanged += OnSelectedLocaleChanged;
            AttachLanguageService();
        }

        private void Start()
        {
            AttachLanguageService();
            RefreshText();
        }

        private void OnDisable()
        {
            LocalizationSettings.SelectedLocaleChanged -= OnSelectedLocaleChanged;
            if (languages != null)
                languages.UiLanguageChanged -= OnUiLanguageChanged;
            languages = null;
        }

        private void AttachLanguageService()
        {
            if (languages != null || !ApplicationServices.IsConfigured)
                return;

            languages = ApplicationServices.Languages;
            languages.UiLanguageChanged += OnUiLanguageChanged;
        }

        private void OnUiLanguageChanged(string languageId)
        {
            RefreshText();
        }

        private void OnSelectedLocaleChanged(Locale locale)
        {
            RefreshText();
        }

        private void RefreshText()
        {
            foreach (TextBinding binding in bindings)
                if (binding.Target != null)
                    binding.Target.text = CardUiText.Get(binding.Key);
        }
    }

    public static class LegacySceneTextAutoInstaller
    {
        private const string LocalizerObjectName = "Legacy Scene Text Localizer";

        private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> SceneBindings =
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["002_MainMenuScene"] = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["Gacha"] = "main_menu.action.gacha",
                    ["Collection"] = "main_menu.action.collection",
                    ["CONTENT"] = "main_menu.action.content",
                    ["Setting"] = "main_menu.action.settings"
                },
                ["003_GachaScene"] = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["Gacha"] = "gacha.title"
                },
                ["004_CollectionScene"] = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["Collection"] = "collection.title"
                },
                ["005_SettingScene"] = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["Setting"] = "settings.title"
                }
            };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallInitialScene()
        {
            Install(SceneManager.GetActiveScene());
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Install(scene);
        }

        public static LegacySceneTextLocalizer Install(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded || !SceneBindings.TryGetValue(scene.name, out IReadOnlyDictionary<string, string> bindings))
                return null;

            LegacySceneTextLocalizer existing = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<LegacySceneTextLocalizer>(true))
                .FirstOrDefault();
            if (existing != null)
                return existing;

            var localizerObject = new GameObject(LocalizerObjectName);
            SceneManager.MoveGameObjectToScene(localizerObject, scene);
            LegacySceneTextLocalizer localizer = localizerObject.AddComponent<LegacySceneTextLocalizer>();
            localizer.Initialize(scene, bindings);
            if (localizer.BindingCount != bindings.Count)
            {
                Debug.LogWarning(
                    $"Legacy scene localization found {localizer.BindingCount}/{bindings.Count} expected text bindings in {scene.name}.");
            }
            return localizer;
        }
    }
}
