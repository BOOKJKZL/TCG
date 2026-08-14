using System;
using System.Collections;
using Gacha.Application;
using TMPro;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;

namespace Gacha.Presentation
{
    public sealed class ExperienceSettingsPanel : MonoBehaviour
    {
        private const string StringTable = "Card_UI";
        private static readonly float[] AnimationSpeeds = { 0.5f, 1f, 1.5f, 2f };

        private CanvasGroup canvasGroup;
        private TMP_Text titleText;
        private TMP_Text soundLabelText;
        private TMP_Text soundValueText;
        private TMP_Text motionLabelText;
        private TMP_Text motionValueText;
        private TMP_Text hapticsLabelText;
        private TMP_Text hapticsValueText;
        private TMP_Text speedLabelText;
        private TMP_Text speedValueText;
        private TMP_Text statusText;
        private Button soundButton;
        private Button motionButton;
        private Button hapticsButton;
        private Button speedButton;
        private ExperienceSettingsService settings;
        private Coroutine refreshRoutine;
        private Coroutine transitionRoutine;
        private string statusKey;
        private string statusFallback;
        private string statusArgument;

        public static ExperienceSettingsPanel Create(Transform parent)
        {
            if (parent == null)
                throw new ArgumentNullException(nameof(parent));

            GameObject root = CreateUiObject("ExperienceSettingsPanel", parent);
            root.SetActive(false);
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = rootRect.anchorMax = rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.anchoredPosition = new Vector2(0f, -285f);
            rootRect.sizeDelta = new Vector2(840f, 720f);

            Image background = root.AddComponent<Image>();
            background.color = new Color(0.055f, 0.075f, 0.12f, 0.94f);
            Outline outline = root.AddComponent<Outline>();
            outline.effectColor = new Color(0.55f, 0.38f, 0.95f, 0.7f);
            outline.effectDistance = new Vector2(3f, -3f);

            ExperienceSettingsPanel panel = root.AddComponent<ExperienceSettingsPanel>();
            panel.canvasGroup = root.AddComponent<CanvasGroup>();
            panel.titleText = CreateText(root.transform, "ExperienceTitle", new Vector2(0f, 285f), new Vector2(720f, 70f), 40f, FontStyles.Bold);
            panel.soundLabelText = CreateText(root.transform, "SoundLabel", new Vector2(-215f, 165f), new Vector2(350f, 68f), 29f, FontStyles.Normal);
            panel.soundValueText = panel.CreateValueButton(root.transform, "SoundButton", new Vector2(235f, 165f), out panel.soundButton);
            panel.motionLabelText = CreateText(root.transform, "ReduceMotionLabel", new Vector2(-215f, 45f), new Vector2(350f, 68f), 29f, FontStyles.Normal);
            panel.motionValueText = panel.CreateValueButton(root.transform, "ReduceMotionButton", new Vector2(235f, 45f), out panel.motionButton);
            panel.hapticsLabelText = CreateText(root.transform, "HapticsLabel", new Vector2(-215f, -75f), new Vector2(350f, 68f), 29f, FontStyles.Normal);
            panel.hapticsValueText = panel.CreateValueButton(root.transform, "HapticsButton", new Vector2(235f, -75f), out panel.hapticsButton);
            panel.speedLabelText = CreateText(root.transform, "AnimationSpeedLabel", new Vector2(-215f, -195f), new Vector2(350f, 68f), 29f, FontStyles.Normal);
            panel.speedValueText = panel.CreateValueButton(root.transform, "AnimationSpeedButton", new Vector2(235f, -195f), out panel.speedButton);
            panel.statusText = CreateText(root.transform, "ExperienceStatus", new Vector2(0f, -300f), new Vector2(720f, 68f), 22f, FontStyles.Italic);
            panel.statusText.color = new Color(0.72f, 0.83f, 0.94f, 1f);

            panel.soundButton.onClick.AddListener(panel.ToggleSound);
            panel.motionButton.onClick.AddListener(panel.ToggleReduceMotion);
            panel.hapticsButton.onClick.AddListener(panel.ToggleHaptics);
            panel.speedButton.onClick.AddListener(panel.CycleAnimationSpeed);
            root.SetActive(true);
            return panel;
        }

        private void OnEnable()
        {
            settings = ApplicationServices.ExperienceSettings;
            if (settings == null)
            {
                SetInteractable(false);
                statusText.text = "Experience settings are unavailable.";
                return;
            }

            settings.Changed += OnSettingsChanged;
            LocalizationSettings.SelectedLocaleChanged += OnSelectedLocaleChanged;
            ApplyFeedbackSettings(settings.Current);
            PlayEntrance();
            RefreshView();
        }

        private void OnDisable()
        {
            if (settings != null)
                settings.Changed -= OnSettingsChanged;
            LocalizationSettings.SelectedLocaleChanged -= OnSelectedLocaleChanged;
            settings = null;

            if (refreshRoutine != null)
                StopCoroutine(refreshRoutine);
            if (transitionRoutine != null)
                StopCoroutine(transitionRoutine);
            refreshRoutine = null;
            transitionRoutine = null;
        }

        private void ToggleSound()
        {
            if (settings != null)
                HandleUpdate(settings.SetSoundEnabled(!settings.Current.SoundEnabled));
        }

        private void ToggleReduceMotion()
        {
            if (settings != null)
                HandleUpdate(settings.SetReduceMotion(!settings.Current.ReduceMotion));
        }

        private void ToggleHaptics()
        {
            if (settings == null)
                return;

            bool enabling = !settings.Current.HapticsEnabled;
            ExperienceSettingsUpdateResult result = settings.SetHapticsEnabled(enabling);
            HandleUpdate(result);
            if (result.Succeeded && enabling)
                UIFeedbackService.Play(FeedbackCue.Confirm, true);
        }

        private void CycleAnimationSpeed()
        {
            if (settings == null)
                return;

            float current = settings.Current.AnimationSpeed;
            int currentIndex = Array.FindIndex(AnimationSpeeds, value => Math.Abs(value - current) < 0.01f);
            float next = AnimationSpeeds[(Math.Max(currentIndex, 0) + 1) % AnimationSpeeds.Length];
            HandleUpdate(settings.SetAnimationSpeed(next));
        }

        private void HandleUpdate(ExperienceSettingsUpdateResult result)
        {
            if (result == null || !result.Succeeded)
            {
                Debug.LogWarning("Experience preference was not saved: " + result?.ErrorMessage);
                statusKey = "settings.experience.save_failed_safe";
                statusFallback = "The preference could not be saved. Nothing changed.";
                statusArgument = null;
                UIFeedbackService.Play(FeedbackCue.Error);
                RefreshView();
                return;
            }

            statusKey = "settings.experience.saved";
            statusFallback = "Saved automatically.";
            statusArgument = null;
            PlaySwitchTransition();
            RefreshView();
        }

        private void OnSettingsChanged(ExperienceSettings current)
        {
            ApplyFeedbackSettings(current);
            RefreshView();
        }

        private void OnSelectedLocaleChanged(UnityEngine.Localization.Locale locale)
        {
            RefreshView();
        }

        private static void ApplyFeedbackSettings(ExperienceSettings current)
        {
            if (current == null)
                return;

            UIFeedbackService.Configure(
                current.ReduceMotion,
                current.HapticsEnabled,
                current.AnimationSpeed,
                current.SoundEnabled);
        }

        private void RefreshView()
        {
            if (!isActiveAndEnabled || settings == null)
                return;

            if (refreshRoutine != null)
                StopCoroutine(refreshRoutine);
            refreshRoutine = StartCoroutine(RefreshLocalizedText());
        }

        private IEnumerator RefreshLocalizedText()
        {
            yield return LocalizationSettings.InitializationOperation;
            yield return SetLocalized(titleText, "settings.experience.title", "Experience");
            yield return SetLocalized(soundLabelText, "settings.experience.sound", "Sound cues");
            yield return SetLocalized(motionLabelText, "settings.experience.reduce_motion", "Reduce motion");
            yield return SetLocalized(hapticsLabelText, "settings.experience.haptics", "Haptics");
            yield return SetLocalized(speedLabelText, "settings.experience.animation_speed", "Animation speed");

            ExperienceSettings current = settings.Current;
            yield return SetLocalized(
                soundValueText,
                current.SoundEnabled ? "settings.experience.sound_on" : "settings.experience.muted",
                current.SoundEnabled ? "On" : "Muted");
            yield return SetLocalized(
                motionValueText,
                current.ReduceMotion ? "settings.experience.on" : "settings.experience.off",
                current.ReduceMotion ? "On" : "Off");
            yield return SetLocalized(
                hapticsValueText,
                current.HapticsEnabled ? "settings.experience.on" : "settings.experience.off",
                current.HapticsEnabled ? "On" : "Off");
            speedValueText.text = $"{current.AnimationSpeed:0.0}x";

            if (string.IsNullOrWhiteSpace(statusKey))
            {
                yield return SetLocalized(statusText, "settings.experience.auto_save", "Changes save automatically.");
            }
            else
            {
                string localized = null;
                yield return GetLocalized(statusKey, statusFallback, value => localized = value);
                statusText.text = string.IsNullOrWhiteSpace(statusArgument)
                    ? localized
                    : string.Format(localized, statusArgument);
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
            completed(!string.IsNullOrWhiteSpace(operation.Result) ? operation.Result : fallback);
        }

        private void SetInteractable(bool interactable)
        {
            soundButton.interactable = interactable;
            motionButton.interactable = interactable;
            hapticsButton.interactable = interactable;
            speedButton.interactable = interactable;
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
            rect.sizeDelta = new Vector2(300f, 88f);

            Image image = buttonObject.AddComponent<Image>();
            image.color = new Color(0.25f, 0.18f, 0.5f, 0.98f);
            button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(0.4f, 0.3f, 0.72f, 1f);
            colors.pressedColor = new Color(0.18f, 0.12f, 0.38f, 1f);
            button.colors = colors;
            buttonObject.AddComponent<GameFeedbackButton>().Configure(FeedbackCue.Confirm);

            return CreateText(buttonObject.transform, "Value", Vector2.zero, new Vector2(270f, 70f), 29f, FontStyles.Bold);
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
    }
}
