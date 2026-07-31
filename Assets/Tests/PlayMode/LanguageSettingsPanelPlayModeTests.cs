using System.Collections;
using System.Collections.Generic;
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
using UnityEngine.Video;

namespace Gacha.Tests.PlayMode
{
    public class LanguageSettingsPanelPlayModeTests
    {
        private readonly List<string> unexpectedHeadlessErrors = new List<string>();
        private bool captureHeadlessErrors;

        private sealed class AudioSink : IAudioFeedbackSink
        {
            public int PlayCount { get; private set; }

            public bool TryPlay(string cueKey)
            {
                PlayCount++;
                return true;
            }
        }

        [SetUp]
        public void SetUpHeadlessLogIsolation()
        {
            unexpectedHeadlessErrors.Clear();
            captureHeadlessErrors = System.Environment.GetCommandLineArgs()
                .Any(argument => string.Equals(
                    argument,
                    "-nographics",
                    System.StringComparison.OrdinalIgnoreCase));
            if (!captureHeadlessErrors)
                return;

            // The legacy background VideoPlayer cannot create its render target under
            // Unity's -nographics runner. Capture logs ourselves so only those two known
            // graphics errors are tolerated and every game error still fails the test.
            LogAssert.ignoreFailingMessages = true;
            UnityEngine.Application.logMessageReceived += CaptureHeadlessError;
        }

        [TearDown]
        public void TearDownHeadlessLogIsolation()
        {
            if (!captureHeadlessErrors)
                return;

            UnityEngine.Application.logMessageReceived -= CaptureHeadlessError;
            LogAssert.ignoreFailingMessages = false;
            captureHeadlessErrors = false;
            Assert.That(unexpectedHeadlessErrors, Is.Empty,
                "Unexpected error logs escaped the headless VideoPlayer allowance:\n" +
                string.Join("\n", unexpectedHeadlessErrors));
        }

        private void CaptureHeadlessError(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)
                return;
            if (condition == "RenderTexture.Create failed" ||
                condition == "Failed to set the active render target, ensure that it is a valid render target.")
                return;
            unexpectedHeadlessErrors.Add(condition + "\n" + stackTrace);
        }

        [UnityTest]
        public IEnumerator SettingsScene_InstallsPanelAndChangesOnlyUiLanguage()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("005_SettingScene", LoadSceneMode.Single);
            yield return load;
            yield return null;
            if (captureHeadlessErrors)
            {
                foreach (VideoPlayer player in Object.FindObjectsByType<VideoPlayer>(
                             FindObjectsInactive.Include,
                             FindObjectsSortMode.None))
                {
                    player.Stop();
                    player.enabled = false;
                }
            }

            LanguageSettingsPanel panel = Object.FindFirstObjectByType<LanguageSettingsPanel>(FindObjectsInactive.Include);
            ExperienceSettingsPanel experiencePanel = Object.FindFirstObjectByType<ExperienceSettingsPanel>(FindObjectsInactive.Include);
            MonoBehaviour recoveryPanel = Object.FindObjectsByType<MonoBehaviour>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None)
                .SingleOrDefault(component => component.GetType().Name == "SaveRecoverySettingsPanel");
            MonoBehaviour conflictDialog = Object.FindObjectsByType<MonoBehaviour>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None)
                .SingleOrDefault(component => component.GetType().Name == "CloudConflictSettingsDialog");
            Assert.That(panel, Is.Not.Null);
            Assert.That(experiencePanel, Is.Not.Null);
            Assert.That(recoveryPanel, Is.Not.Null);
            Assert.That(conflictDialog, Is.Not.Null);
            RectTransform languageRect = panel.GetComponent<RectTransform>();
            RectTransform experienceRect = experiencePanel.GetComponent<RectTransform>();
            RectTransform recoveryRect = recoveryPanel.GetComponent<RectTransform>();
            float languageBottom = languageRect.anchoredPosition.y - languageRect.rect.height * 0.5f;
            float experienceTop = experienceRect.anchoredPosition.y + experienceRect.rect.height * 0.5f;
            float experienceBottom = experienceRect.anchoredPosition.y - experienceRect.rect.height * 0.5f;
            float recoveryTop = recoveryRect.anchoredPosition.y + recoveryRect.rect.height * 0.5f;
            float recoveryBottom = recoveryRect.anchoredPosition.y - recoveryRect.rect.height * 0.5f;
            Assert.That(experienceTop, Is.LessThan(languageBottom), "Settings panels must not overlap.");
            Assert.That(recoveryTop, Is.LessThan(experienceBottom), "Recovery and experience panels must not overlap.");
            Assert.That(recoveryBottom, Is.GreaterThanOrEqualTo(-1000f));
            Assert.That(LocalizationSettings.HasSettings, Is.True);
            yield return LocalizationSettings.InitializationOperation;

            Button[] recoveryButtons = recoveryPanel.GetComponentsInChildren<Button>(true);
            Assert.That(recoveryButtons.Select(button => button.name), Is.EquivalentTo(new[]
            {
                "ExportSaveButton",
                "ChooseImportButton",
                "ConfirmImportButton",
                "CloudConflictButton"
            }));
            Assert.That(recoveryButtons.Single(button => button.name == "ConfirmImportButton").interactable, Is.False);
            Button cloudButton = recoveryButtons.Single(button => button.name == "CloudConflictButton");
            cloudButton.onClick.Invoke();
            yield return null;
            Assert.That(conflictDialog.GetComponent<CanvasGroup>().blocksRaycasts, Is.True);
            Button[] conflictButtons = conflictDialog.GetComponentsInChildren<Button>(true);
            Assert.That(conflictButtons.Single(button => button.name == "KeepLocalButton").interactable, Is.False);
            Assert.That(conflictButtons.Single(button => button.name == "ConnectIdentityButton").interactable, Is.False,
                "Recoverable sign-in must stay disabled while the external Player Accounts client id is absent.");
            Assert.That(conflictDialog.GetComponentsInChildren<TMP_Text>(true)
                .Single(text => text.name == "PlayerIdentityStatus").text, Does.Contain("PLAYER ID"));
            conflictButtons.Single(button => button.name == "CloseConflictButton").onClick.Invoke();
            yield return new WaitForSecondsRealtime(0.25f);
            Assert.That(conflictDialog.GetComponent<CanvasGroup>().blocksRaycasts, Is.False);

            Button uiButton = panel.GetComponentsInChildren<Button>(true)
                .Single(button => button.name == "UiLanguageButton");
            Assert.That(uiButton.interactable, Is.True);
            Assert.That(uiButton.GetComponent<GameFeedbackButton>(), Is.Not.Null);

            string persistedUi = ApplicationServices.Languages.UiLanguageId;
            string originalContent = ApplicationServices.Languages.RequestedContentLanguageId;
            ApplicationServices.Languages.SelectUiLanguage("en");
            yield return null;
            const string expectedUi = "zh";
            const string expectedTitle = "语言设置";

            uiButton.onClick.Invoke();
            TMP_Text title = panel.GetComponentsInChildren<TMP_Text>(true)
                .Single(text => text.name == "Title");
            TMP_Text recoveryTitle = recoveryPanel.GetComponentsInChildren<TMP_Text>(true)
                .Single(text => text.name == "RecoveryTitle");
            float timeout = Time.realtimeSinceStartup + 3f;
            while ((title.text != expectedTitle || recoveryTitle.text != "\u4FDD\u5B58\u8BBE\u7F6E") &&
                   Time.realtimeSinceStartup < timeout)
                yield return null;

            Assert.That(ApplicationServices.Languages.UiLanguageId, Is.EqualTo(expectedUi));
            Assert.That(ApplicationServices.Languages.RequestedContentLanguageId, Is.EqualTo(originalContent));
            Assert.That(LocalizationSettings.SelectedLocale.Identifier.Code, Is.EqualTo(expectedUi));
            Assert.That(title.text, Is.EqualTo(expectedTitle));
            Assert.That(recoveryTitle.text, Is.EqualTo("\u4FDD\u5B58\u8BBE\u7F6E"));

            uiButton.onClick.Invoke();
            timeout = Time.realtimeSinceStartup + 3f;
            while ((title.text != "言語設定" || recoveryTitle.text != "セーブデータ復元") &&
                   Time.realtimeSinceStartup < timeout)
                yield return null;

            Assert.That(ApplicationServices.Languages.UiLanguageId, Is.EqualTo("ja"));
            Assert.That(ApplicationServices.Languages.RequestedContentLanguageId, Is.EqualTo(originalContent));
            Assert.That(LocalizationSettings.SelectedLocale.Identifier.Code, Is.EqualTo("ja"));
            Assert.That(title.text, Is.EqualTo("言語設定"));
            Assert.That(recoveryTitle.text, Is.EqualTo("セーブデータ復元"));

            ApplicationServices.Languages.SelectUiLanguage(persistedUi);
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
            if (!captureHeadlessErrors)
                LogAssert.NoUnexpectedReceived();
        }

    }
}
