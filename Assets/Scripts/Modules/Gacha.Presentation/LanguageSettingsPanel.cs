using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Gacha.Application;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Gacha.Presentation
{
    public sealed class LanguageSettingsPanel : MonoBehaviour
    {
        private const string StringTable = "Card_UI";
        private CanvasGroup canvasGroup;
        private TMP_Text titleText;
        private TMP_Text uiLabelText;
        private TMP_Text uiValueText;
        private TMP_Text contentLabelText;
        private TMP_Text contentValueText;
        private TMP_Text statusText;
        private Button uiLanguageButton;
        private Button contentLanguageButton;
        private Coroutine refreshRoutine;
        private Coroutine transitionRoutine;
        private LanguageSelectionService languages;

        public static LanguageSettingsPanel Create(Transform parent)
        {
            if (parent == null)
                throw new ArgumentNullException(nameof(parent));

            GameObject root = CreateUiObject("LanguageSettingsPanel", parent);
            root.SetActive(false);
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = rootRect.anchorMax = rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.anchoredPosition = new Vector2(0f, 455f);
            rootRect.sizeDelta = new Vector2(840f, 500f);

            Image background = root.AddComponent<Image>();
            background.color = new Color(0.055f, 0.075f, 0.12f, 0.94f);
            Outline outline = root.AddComponent<Outline>();
            outline.effectColor = new Color(0.22f, 0.65f, 0.95f, 0.75f);
            outline.effectDistance = new Vector2(3f, -3f);

            LanguageSettingsPanel panel = root.AddComponent<LanguageSettingsPanel>();
            panel.canvasGroup = root.AddComponent<CanvasGroup>();
            panel.titleText = CreateText(root.transform, "Title", new Vector2(0f, 210f), new Vector2(720f, 80f), 43f, FontStyles.Bold);
            panel.uiLabelText = CreateText(root.transform, "UiLanguageLabel", new Vector2(-220f, 75f), new Vector2(330f, 75f), 31f, FontStyles.Normal);
            panel.uiValueText = panel.CreateValueButton(root.transform, "UiLanguageButton", new Vector2(235f, 75f), out panel.uiLanguageButton);
            panel.contentLabelText = CreateText(root.transform, "ContentLanguageLabel", new Vector2(-220f, -65f), new Vector2(330f, 75f), 31f, FontStyles.Normal);
            panel.contentValueText = panel.CreateValueButton(root.transform, "ContentLanguageButton", new Vector2(235f, -65f), out panel.contentLanguageButton);
            panel.statusText = CreateText(root.transform, "LanguageStatus", new Vector2(0f, -205f), new Vector2(720f, 90f), 24f, FontStyles.Italic);
            panel.statusText.color = new Color(0.72f, 0.83f, 0.94f, 1f);

            panel.uiLanguageButton.onClick.AddListener(panel.CycleUiLanguage);
            panel.contentLanguageButton.onClick.AddListener(panel.CycleContentLanguage);
            root.SetActive(true);
            return panel;
        }

        private void OnEnable()
        {
            if (!ApplicationServices.IsConfigured)
            {
                ShowUnavailable("Language services are unavailable.");
                return;
            }

            languages = ApplicationServices.Languages;
            languages.UiLanguageChanged += OnUiLanguageChanged;
            languages.ContentLanguageChanged += OnContentLanguageChanged;
            LocalizationSettings.SelectedLocaleChanged += OnSelectedLocaleChanged;

            CatalogLoadResult load = ApplicationServices.Catalog.EnsureLoaded();
            if (load.Succeeded)
            {
                languages.RefreshContentLanguage(load.Catalog);
            }
            else
            {
                ShowUnavailable(load.ErrorMessage);
                UIFeedbackService.Play(FeedbackCue.Error);
            }

            PlayEntrance();
            RefreshView();
        }

        private void OnDisable()
        {
            if (languages != null)
            {
                languages.UiLanguageChanged -= OnUiLanguageChanged;
                languages.ContentLanguageChanged -= OnContentLanguageChanged;
            }
            LocalizationSettings.SelectedLocaleChanged -= OnSelectedLocaleChanged;
            languages = null;

            if (refreshRoutine != null)
                StopCoroutine(refreshRoutine);
            if (transitionRoutine != null)
                StopCoroutine(transitionRoutine);
            refreshRoutine = null;
            transitionRoutine = null;
        }

        private void CycleUiLanguage()
        {
            if (languages == null)
                return;

            IReadOnlyList<string> options = languages.AvailableUiLanguageIds;
            if (options.Count < 2)
                return;

            int current = IndexOf(options, languages.UiLanguageId);
            languages.SelectUiLanguage(options[(current + 1) % options.Count]);
            PlaySwitchTransition();
        }

        private void CycleContentLanguage()
        {
            if (languages == null || !ApplicationServices.Catalog.IsReady)
                return;

            string[] options = ApplicationServices.Catalog.Catalog.Languages.Keys
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (options.Length < 2)
            {
                RefreshView();
                return;
            }

            int current = Array.FindIndex(options, value => string.Equals(
                value,
                languages.RequestedContentLanguageId,
                StringComparison.OrdinalIgnoreCase));
            languages.SelectContentLanguage(options[(Math.Max(current, 0) + 1) % options.Length], ApplicationServices.Catalog.Catalog);
            PlaySwitchTransition();
        }

        private void OnUiLanguageChanged(string languageId)
        {
            RefreshView();
        }

        private void OnContentLanguageChanged(ContentLanguageSelection selection)
        {
            RefreshView();
        }

        private void OnSelectedLocaleChanged(Locale locale)
        {
            RefreshView();
        }

        private void RefreshView()
        {
            if (!isActiveAndEnabled || languages == null)
                return;

            if (refreshRoutine != null)
                StopCoroutine(refreshRoutine);
            refreshRoutine = StartCoroutine(RefreshLocalizedText());
        }

        private IEnumerator RefreshLocalizedText()
        {
            yield return LocalizationSettings.InitializationOperation;
            yield return SetLocalized(titleText, "settings.language.title", "Language");
            yield return SetLocalized(uiLabelText, "settings.language.ui", "Interface language");
            yield return SetLocalized(contentLabelText, "settings.language.content", "Card language");
            yield return SetLocalized(uiValueText, LanguageKey(languages.UiLanguageId), languages.UiLanguageId);
            yield return SetLocalized(
                contentValueText,
                LanguageKey(languages.ContentLanguage.ResolvedLanguageId),
                languages.ContentLanguage.ResolvedLanguageId);

            bool hasMultipleContentLanguages = ApplicationServices.Catalog.IsReady &&
                                               ApplicationServices.Catalog.Catalog.Languages.Count > 1;
            contentLanguageButton.interactable = hasMultipleContentLanguages;

            if (languages.ContentLanguage.UsedFallback)
            {
                string template = null;
                yield return GetLocalized("settings.language.fallback", "Requested {0}; using {1}.", value => template = value);
                statusText.text = string.Format(
                    template,
                    languages.ContentLanguage.RequestedLanguageId,
                    languages.ContentLanguage.ResolvedLanguageId);
            }
            else if (!hasMultipleContentLanguages)
            {
                yield return SetLocalized(statusText, "settings.language.only_installed", "Only one card language is installed.");
            }
            else
            {
                statusText.text = string.Empty;
            }

            refreshRoutine = null;
        }

        private IEnumerator SetLocalized(TMP_Text target, string key, string fallback)
        {
            string value = null;
            yield return GetLocalized(key, fallback, localized => value = localized);
            if (target != null)
                target.text = value;
        }

        private static IEnumerator GetLocalized(string key, string fallback, Action<string> completed)
        {
            var operation = LocalizationSettings.StringDatabase.GetLocalizedStringAsync(StringTable, key);
            yield return operation;
            string value = !string.IsNullOrWhiteSpace(operation.Result)
                ? operation.Result
                : fallback;
            completed(value);
        }

        private void ShowUnavailable(string message)
        {
            if (statusText != null)
                statusText.text = string.IsNullOrWhiteSpace(message) ? "Language services are unavailable." : message;
            if (uiLanguageButton != null)
                uiLanguageButton.interactable = false;
            if (contentLanguageButton != null)
                contentLanguageButton.interactable = false;
        }

        private void PlayEntrance()
        {
            if (canvasGroup == null)
                return;
            if (UIFeedbackService.ReduceMotion)
            {
                canvasGroup.alpha = 1f;
                transform.localScale = Vector3.one;
                return;
            }

            canvasGroup.alpha = 0f;
            transform.localScale = Vector3.one * 0.97f;
            transitionRoutine = StartCoroutine(AnimatePanel(0f, 1f, 0.24f));
        }

        private void PlaySwitchTransition()
        {
            if (UIFeedbackService.ReduceMotion || canvasGroup == null)
                return;
            if (transitionRoutine != null)
                StopCoroutine(transitionRoutine);
            transitionRoutine = StartCoroutine(AnimatePanel(0.62f, 1f, 0.18f));
        }

        private IEnumerator AnimatePanel(float fromAlpha, float toAlpha, float baseDuration)
        {
            canvasGroup.alpha = fromAlpha;
            Vector3 startScale = Vector3.one * 0.98f;
            transform.localScale = startScale;
            float duration = baseDuration / UIFeedbackService.AnimationSpeed;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                canvasGroup.alpha = Mathf.Lerp(fromAlpha, toAlpha, progress);
                transform.localScale = Vector3.LerpUnclamped(startScale, Vector3.one, progress);
                yield return null;
            }

            canvasGroup.alpha = toAlpha;
            transform.localScale = Vector3.one;
            transitionRoutine = null;
        }

        private TMP_Text CreateValueButton(Transform parent, string name, Vector2 position, out Button button)
        {
            GameObject buttonObject = CreateUiObject(name, parent);
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(300f, 92f);

            Image image = buttonObject.AddComponent<Image>();
            image.color = new Color(0.12f, 0.33f, 0.52f, 0.98f);
            button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(0.18f, 0.48f, 0.72f, 1f);
            colors.pressedColor = new Color(0.08f, 0.23f, 0.38f, 1f);
            colors.disabledColor = new Color(0.13f, 0.16f, 0.2f, 0.72f);
            button.colors = colors;
            buttonObject.AddComponent<GameFeedbackButton>().Configure(FeedbackCue.Confirm);

            return CreateText(buttonObject.transform, "Value", Vector2.zero, new Vector2(270f, 72f), 30f, FontStyles.Bold);
        }

        private static TMP_Text CreateText(
            Transform parent,
            string name,
            Vector2 position,
            Vector2 size,
            float fontSize,
            FontStyles fontStyle)
        {
            GameObject textObject = CreateUiObject(name, parent);
            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
            text.font = TMP_Settings.defaultFontAsset;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.enableWordWrapping = true;
            text.raycastTarget = false;
            return text;
        }

        private static GameObject CreateUiObject(string name, Transform parent)
        {
            GameObject result = new GameObject(name, typeof(RectTransform));
            result.layer = 5;
            result.transform.SetParent(parent, false);
            return result;
        }

        private static int IndexOf(IReadOnlyList<string> values, string target)
        {
            for (int i = 0; i < values.Count; i++)
                if (string.Equals(values[i], target, StringComparison.OrdinalIgnoreCase))
                    return i;
            return 0;
        }

        private static string LanguageKey(string languageId)
        {
            if (string.IsNullOrWhiteSpace(languageId))
                return "language.en";
            string normalized = languageId.Replace('_', '-');
            int separator = normalized.IndexOf('-');
            string baseLanguage = separator > 0 ? normalized.Substring(0, separator) : normalized;
            return $"language.{baseLanguage.ToLowerInvariant()}";
        }
    }

    public static class LanguageSettingsAutoInstaller
    {
        private const string SettingsSceneName = "005_SettingScene";

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

        private static void Install(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded || scene.name != SettingsSceneName)
                return;

            bool hasLanguagePanel = scene.GetRootGameObjects().Any(root =>
                root.GetComponentInChildren<LanguageSettingsPanel>(true) != null);
            bool hasExperiencePanel = scene.GetRootGameObjects().Any(root =>
                root.GetComponentInChildren<ExperienceSettingsPanel>(true) != null);
            if (hasLanguagePanel && hasExperiencePanel)
                return;

            Canvas canvas = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Canvas>(true))
                .FirstOrDefault(candidate => candidate.isRootCanvas);
            if (canvas == null)
            {
                Debug.LogWarning("The settings scene has no root Canvas for the language panel.");
                return;
            }

            if (!hasLanguagePanel)
                LanguageSettingsPanel.Create(canvas.transform);
            if (!hasExperiencePanel)
                ExperienceSettingsPanel.Create(canvas.transform);
        }
    }
}
