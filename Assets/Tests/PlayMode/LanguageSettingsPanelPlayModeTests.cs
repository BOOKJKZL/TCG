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
        [UnityTest]
        public IEnumerator SettingsScene_InstallsPanelAndChangesOnlyUiLanguage()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("005_SettingScene", LoadSceneMode.Single);
            yield return load;
            yield return null;

            LanguageSettingsPanel panel = Object.FindFirstObjectByType<LanguageSettingsPanel>(FindObjectsInactive.Include);
            Assert.That(panel, Is.Not.Null);
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
        }
    }
}
