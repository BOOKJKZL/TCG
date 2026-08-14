using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Gacha.Application;
using Gacha.Presentation;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Gacha.Tests.PlayMode
{
    public class LegacySceneLocalizationPlayModeTests
    {
        private Scene loadedScene;

        [UnityTearDown]
        public IEnumerator UnloadLegacyScene()
        {
            if (!loadedScene.IsValid() || !loadedScene.isLoaded)
                yield break;
            Scene cleanup = SceneManager.CreateScene("Legacy Localization Cleanup");
            Assert.That(SceneManager.SetActiveScene(cleanup), Is.True);
            yield return SceneManager.UnloadSceneAsync(loadedScene);
            yield return null;
            yield return null;
            loadedScene = default;
        }

        [UnityTest]
        public IEnumerator LegacyUguiScenes_RefreshMappedTextForRuntimeLanguageChanges()
        {
            string originalUiLanguage = null;
            try
            {
                yield return LoadScene("002_MainMenuScene");
                Assert.That(ApplicationServices.IsConfigured, Is.True);
                originalUiLanguage = ApplicationServices.Languages.UiLanguageId;

                LegacySceneTextLocalizer mainMenuLocalizer = Object.FindFirstObjectByType<LegacySceneTextLocalizer>(
                    FindObjectsInactive.Include);
                Assert.That(mainMenuLocalizer, Is.Not.Null);
                Assert.That(mainMenuLocalizer.BindingCount, Is.EqualTo(4));

                yield return SelectLanguageAndWait("zh", new[] { "抽卡", "收藏", "内容", "设置" });
                yield return SelectLanguageAndWait("ja", new[] { "パック開封", "コレクション", "コンテンツ", "設定" });
                yield return SelectLanguageAndWait("en", new[] { "Gacha", "Collection", "Content", "Settings" });

                yield return LoadScene("005_SettingScene");
                LegacySceneTextLocalizer settingsLocalizer = Object.FindFirstObjectByType<LegacySceneTextLocalizer>(
                    FindObjectsInactive.Include);
                Assert.That(settingsLocalizer, Is.Not.Null);
                Assert.That(settingsLocalizer.BindingCount, Is.EqualTo(1));
                yield return SelectLanguageAndWait("zh", new[] { "设置" });
                yield return SelectLanguageAndWait("ja", new[] { "設定" });
                yield return SelectLanguageAndWait("en", new[] { "Settings" });

                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(originalUiLanguage) && ApplicationServices.IsConfigured)
                    ApplicationServices.Languages.SelectUiLanguage(originalUiLanguage);
            }
        }

        private IEnumerator LoadScene(string sceneName)
        {
            AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            yield return operation;
            loadedScene = SceneManager.GetSceneByName(sceneName);
            yield return null;
        }

        private static IEnumerator SelectLanguageAndWait(string languageId, IReadOnlyCollection<string> expected)
        {
            ApplicationServices.Languages.SelectUiLanguage(languageId);
            float deadline = Time.realtimeSinceStartup + 3f;
            while (!ContainsAll(SceneManager.GetActiveScene(), expected) && Time.realtimeSinceStartup < deadline)
                yield return null;

            IReadOnlyCollection<string> actual = SceneTexts(SceneManager.GetActiveScene());
            Assert.That(actual, Is.SupersetOf(expected));
        }

        private static bool ContainsAll(Scene scene, IEnumerable<string> expected)
        {
            IReadOnlyCollection<string> actual = SceneTexts(scene);
            return expected.All(actual.Contains);
        }

        private static IReadOnlyCollection<string> SceneTexts(Scene scene)
        {
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<TMP_Text>(true))
                .Select(text => text.text)
                .ToArray();
        }
    }
}
