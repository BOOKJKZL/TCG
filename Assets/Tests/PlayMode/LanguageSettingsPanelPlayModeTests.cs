using System.Collections;
using System.Linq;
using Gacha.Application;
using Gacha.Presentation;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Gacha.Tests.PlayMode
{
    public class LanguageSettingsPanelPlayModeTests
    {
        private sealed class AudioSink : IAudioFeedbackSink
        {
            public int PlayCount { get; private set; }

            public bool TryPlay(string cueKey)
            {
                PlayCount++;
                return true;
            }
        }

        [UnityTest]
        public IEnumerator SettingsScene_InstallsPanelAndChangesOnlyUiLanguage()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("005_SettingScene", LoadSceneMode.Single);
            yield return load;
            yield return null;

            LanguageSettingsPanel panel = Object.FindFirstObjectByType<LanguageSettingsPanel>(FindObjectsInactive.Include);
            ExperienceSettingsPanel experiencePanel = Object.FindFirstObjectByType<ExperienceSettingsPanel>(FindObjectsInactive.Include);
            Assert.That(panel, Is.Not.Null);
            Assert.That(experiencePanel, Is.Not.Null);
            RectTransform languageRect = panel.GetComponent<RectTransform>();
            RectTransform experienceRect = experiencePanel.GetComponent<RectTransform>();
            float languageBottom = languageRect.anchoredPosition.y - languageRect.rect.height * 0.5f;
            float experienceTop = experienceRect.anchoredPosition.y + experienceRect.rect.height * 0.5f;
            Assert.That(experienceTop, Is.LessThan(languageBottom), "Settings panels must not overlap.");
            Assert.That(LocalizationSettings.HasSettings, Is.True);
            yield return LocalizationSettings.InitializationOperation;

            Button uiButton = panel.GetComponentsInChildren<Button>(true)
                .Single(button => button.name == "UiLanguageButton");
            Assert.That(uiButton.interactable, Is.True);
            Assert.That(uiButton.GetComponent<GameFeedbackButton>(), Is.Not.Null);

            string originalUi = ApplicationServices.Languages.UiLanguageId;
            string originalContent = ApplicationServices.Languages.RequestedContentLanguageId;
            string expectedUi = originalUi == "en" ? "zh" : "en";
            string expectedTitle = expectedUi == "zh" ? "语言设置" : "Language";

            uiButton.onClick.Invoke();
            TMP_Text title = panel.GetComponentsInChildren<TMP_Text>(true)
                .Single(text => text.name == "Title");
            float timeout = Time.realtimeSinceStartup + 3f;
            while (title.text != expectedTitle && Time.realtimeSinceStartup < timeout)
                yield return null;

            Assert.That(ApplicationServices.Languages.UiLanguageId, Is.EqualTo(expectedUi));
            Assert.That(ApplicationServices.Languages.RequestedContentLanguageId, Is.EqualTo(originalContent));
            Assert.That(LocalizationSettings.SelectedLocale.Identifier.Code, Is.EqualTo(expectedUi));
            Assert.That(title.text, Is.EqualTo(expectedTitle));

            ApplicationServices.Languages.SelectUiLanguage(originalUi);
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.That(panel.GetComponent<CanvasGroup>().alpha, Is.EqualTo(1f).Within(0.01f));

            ExperienceSettingsService experience = ApplicationServices.ExperienceSettings;
            Assert.That(experience, Is.Not.Null);
            ExperienceSettings original = experience.Current;
            experience.SetSoundEnabled(true);
            experience.SetReduceMotion(false);
            experience.SetHapticsEnabled(true);
            experience.SetAnimationSpeed(1f);

            Button soundButton = experiencePanel.GetComponentsInChildren<Button>(true)
                .Single(button => button.name == "SoundButton");
            Button motionButton = experiencePanel.GetComponentsInChildren<Button>(true)
                .Single(button => button.name == "ReduceMotionButton");
            Button hapticsButton = experiencePanel.GetComponentsInChildren<Button>(true)
                .Single(button => button.name == "HapticsButton");
            Button speedButton = experiencePanel.GetComponentsInChildren<Button>(true)
                .Single(button => button.name == "AnimationSpeedButton");

            soundButton.onClick.Invoke();
            Assert.That(experience.Current.SoundEnabled, Is.False);
            Assert.That(UIFeedbackService.SoundEnabled, Is.False);

            var sink = new AudioSink();
            UIFeedbackService.RegisterAudioSink(sink);
            Assert.That(UIFeedbackService.Play(FeedbackCue.Confirm), Is.False);
            Assert.That(sink.PlayCount, Is.Zero);
            UIFeedbackService.UnregisterAudioSink(sink);

            motionButton.onClick.Invoke();
            Assert.That(experience.Current.ReduceMotion, Is.True);
            Assert.That(UIFeedbackService.ReduceMotion, Is.True);

            hapticsButton.onClick.Invoke();
            Assert.That(experience.Current.HapticsEnabled, Is.False);
            Assert.That(UIFeedbackService.HapticsEnabled, Is.False);

            speedButton.onClick.Invoke();
            Assert.That(experience.Current.AnimationSpeed, Is.EqualTo(1.5f));
            Assert.That(UIFeedbackService.AnimationSpeed, Is.EqualTo(1.5f));

            experience.SetSoundEnabled(original.SoundEnabled);
            experience.SetReduceMotion(original.ReduceMotion);
            experience.SetHapticsEnabled(original.HapticsEnabled);
            experience.SetAnimationSpeed(original.AnimationSpeed);
            yield return null;

            Assert.That(UIFeedbackService.SoundEnabled, Is.EqualTo(original.SoundEnabled));
            Assert.That(UIFeedbackService.ReduceMotion, Is.EqualTo(original.ReduceMotion));
            Assert.That(UIFeedbackService.HapticsEnabled, Is.EqualTo(original.HapticsEnabled));
            Assert.That(UIFeedbackService.AnimationSpeed, Is.EqualTo(original.AnimationSpeed));
        }

    }
}
