using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Gacha.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Gacha.Tests.PlayMode
{
    public sealed class HomePlayModeTests
    {
        [UnityTest]
        public IEnumerator MainMenuScene_UsesOneHomeDocumentAndDisablesLegacyCanvas()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("002_MainMenuScene", LoadSceneMode.Single);
            yield return load;
            yield return null;
            yield return null;

            Scene scene = SceneManager.GetActiveScene();
            MonoBehaviour[] controllers = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<MonoBehaviour>(true))
                .Where(component => component != null && component.GetType().Name == "MainMenuController")
                .ToArray();
            Assert.That(controllers, Has.Length.EqualTo(1));

            UIDocument[] homeDocuments = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<UIDocument>(true))
                .Where(document => document.rootVisualElement.name == "mobile-home-document")
                .ToArray();
            Assert.That(homeDocuments, Has.Length.EqualTo(1));
            VisualElement homeRoot = homeDocuments[0].rootVisualElement;
            Assert.That(homeRoot.Q<VisualElement>("mobile-home-page"), Is.Not.Null);
            VisualElement homeNavigation = homeRoot.Q<VisualElement>("nav-home");
            Assert.That(homeNavigation, Is.Not.Null);
            Assert.That(homeNavigation.Q<VisualElement>("action-selection-indicator")
                .ClassListContains("is-selected"), Is.True);

            Canvas[] legacyCanvases = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Canvas>(true))
                .ToArray();
            Assert.That(legacyCanvases, Is.Not.Empty);
            Assert.That(legacyCanvases.All(canvas => !canvas.gameObject.activeInHierarchy), Is.True);

            Camera sceneCamera = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Camera>(true))
                .FirstOrDefault();
            Assert.That(sceneCamera, Is.Not.Null);
            Assert.That(sceneCamera.rect, Is.EqualTo(new Rect(0f, 0f, 1f, 1f)));

            var routes = new List<string>();
            FieldInfo loaderOverride = controllers[0].GetType().GetField(
                "sceneLoaderOverrideForTests",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(loaderOverride, Is.Not.Null);
            loaderOverride.SetValue(controllers[0], new System.Action<string>(routes.Add));
            controllers[0].GetType().GetMethod("GachaBtnClick")?.Invoke(controllers[0], null);
            controllers[0].GetType().GetMethod("GachaBtnClick")?.Invoke(controllers[0], null);
            controllers[0].GetType().GetMethod("CollectionBtnClick")?.Invoke(controllers[0], null);
            Assert.That(routes, Is.EqualTo(new[] { "003_GachaScene" }));
            Assert.That(homeRoot.Q<VisualElement>("nav-gacha")
                .Q<VisualElement>("action-selection-indicator").ClassListContains("is-selected"), Is.True);
            Assert.That(homeRoot.Query<VisualElement>(className: "mobile-action")
                .ToList().All(action => action.Q<Label>("action-label") == null ||
                                        action.Q<Label>("action-label").ClassListContains("is-disabled")), Is.True);

            Scene cleanup = SceneManager.CreateScene("Home PlayMode Cleanup");
            Assert.That(SceneManager.SetActiveScene(cleanup), Is.True);
            yield return SceneManager.UnloadSceneAsync(scene);
        }

        [UnityTest]
        public IEnumerator Home_ComposesStableLocalizedDestinationsAndBottomNavigation()
        {
            yield return LocalizationSettings.InitializationOperation;
            Locale originalLocale = LocalizationSettings.SelectedLocale;
            SelectLocale("en");
            GameObject host = new GameObject("Mobile Home Test Host");
            MobileHomePresenter presenter = null;
            int gachaClicks = 0;
            int collectionClicks = 0;
            int contentClicks = 0;
            int settingsClicks = 0;
            try
            {
                presenter = new MobileHomePresenter(
                    host,
                    () => gachaClicks++,
                    () => collectionClicks++,
                    () => contentClicks++,
                    () => settingsClicks++);
                yield return null;
                yield return null;

                VisualElement root = presenter.Document.rootVisualElement;
                Assert.That(root.Q<VisualElement>("mobile-home-page"), Is.SameAs(presenter.Shell.Root));
                Assert.That(presenter.Shell.SafeArea.ClassListContains("safe-area-bound"), Is.True);
                Assert.That(root.Query<Button>().ToList(), Is.Empty);
                Assert.That(presenter.PrimaryNavigation.Count, Is.EqualTo(5));
                Assert.That(presenter.HomeNavigation.IsSelected, Is.True);
                Assert.That(presenter.GachaNavigation.IsSelected, Is.False);
                Assert.That(presenter.SettingsNavigation.IsSelected, Is.False);
                Assert.That(presenter.HomeNavigation.Root.resolvedStyle.height,
                    Is.GreaterThanOrEqualTo(MobileUiTokens.MinimumTouchTarget - 0.5f));
                Assert.That(root.Q<VisualElement>("home-feature-grid"), Is.Not.Null);
                Assert.That(root.Q<VisualElement>("home-gacha-action"), Is.Not.Null);
                Assert.That(root.Q<VisualElement>("home-collection-action"), Is.Not.Null);
                Assert.That(root.Q<VisualElement>("home-content-action"), Is.Not.Null);
                Assert.That(root.Q<VisualElement>("home-settings-action"), Is.Not.Null);
                Assert.That(presenter.Title.text, Is.EqualTo(CardUiText.Get("home.title")));

                InvokeAction(presenter.GachaNavigation);
                yield return null;
                ScrollView scrollView = presenter.Content as ScrollView;
                Assert.That(scrollView, Is.Not.Null);
                yield return ScrollToAndInvoke(scrollView, presenter.CollectionFeatureAction);
                yield return ScrollToAndInvoke(scrollView, presenter.ContentFeatureAction);
                yield return ScrollToAndInvoke(scrollView, presenter.SettingsFeatureAction);
                Assert.That(gachaClicks, Is.EqualTo(1));
                Assert.That(collectionClicks, Is.EqualTo(1));
                Assert.That(contentClicks, Is.EqualTo(1));
                Assert.That(settingsClicks, Is.EqualTo(1));

                InvokeAction(presenter.CollectionNavigation);
                InvokeAction(presenter.ContentNavigation);
                InvokeAction(presenter.SettingsNavigation);
                yield return null;
                Assert.That(collectionClicks, Is.EqualTo(2));
                Assert.That(contentClicks, Is.EqualTo(2));
                Assert.That(settingsClicks, Is.EqualTo(2));
                scrollView.scrollOffset = new Vector2(0f, 100f);
                InvokeAction(presenter.HomeNavigation);
                Assert.That(scrollView.scrollOffset.y, Is.Zero);

                string englishTitle = presenter.Title.text;
                yield return SelectLanguageAndWait("zh", presenter, "一包一包");
                Assert.That(presenter.Title.text, Is.Not.EqualTo(englishTitle));
                Assert.That(presenter.HomeNavigation.Label.text, Is.EqualTo("主菜单"));
                yield return SelectLanguageAndWait("ja", presenter, "1パックずつ");
                Assert.That(presenter.HomeNavigation.Label.text, Is.EqualTo("ホーム"));
                Assert.That(presenter.Title.resolvedStyle.whiteSpace, Is.EqualTo(WhiteSpace.Normal));
                Assert.That(presenter.Shell.ContentSlot.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            }
            finally
            {
                if (originalLocale != null)
                    LocalizationSettings.SelectedLocale = originalLocale;
                presenter?.Dispose();
                Object.DestroyImmediate(host);
            }
        }

        [UnityTest]
        public IEnumerator Home_720By1600InsetsKeepFiveActionsAndCompactCardsReachable()
        {
            yield return LocalizationSettings.InitializationOperation;
            Locale originalLocale = LocalizationSettings.SelectedLocale;
            GameObject host = new GameObject("Mobile Home Contract Host");
            MobileHomePresenter presenter = null;
            try
            {
                presenter = new MobileHomePresenter(host, () => { }, () => { }, () => { }, () => { });
                yield return null;

                var viewport = new VisualElement { name = "home-contract-viewport" };
                viewport.style.position = Position.Relative;
                viewport.style.width = 720f;
                viewport.style.height = 1600f;
                presenter.Document.rootVisualElement.Clear();
                presenter.Document.rootVisualElement.Add(viewport);
                viewport.Add(presenter.Shell.Root);

                FieldInfo bindingField = typeof(MobilePageShell).GetField(
                    "safeAreaBinding",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(bindingField, Is.Not.Null);
                var binding = bindingField.GetValue(presenter.Shell) as UiToolkitSafeAreaBinding;
                Assert.That(binding, Is.Not.Null);
                binding.Suspend();
                presenter.Shell.SafeArea.AddToClassList("mobile-layout--compact");
                presenter.Shell.SafeArea.style.paddingLeft = 12f;
                presenter.Shell.SafeArea.style.paddingTop = 60f;
                presenter.Shell.SafeArea.style.paddingRight = 12f;
                presenter.Shell.SafeArea.style.paddingBottom = 84f;
                yield return null;
                yield return null;

                Rect safeContent = InsetRect(
                    presenter.Shell.SafeArea.worldBound,
                    presenter.Shell.SafeArea.resolvedStyle);
                AssertContained(safeContent, presenter.TopBar.Root.worldBound, "home top bar");
                AssertContained(safeContent, presenter.PrimaryNavigation.BottomNavigation.Root.worldBound,
                    "home bottom navigation");
                foreach (MobileDestination destination in System.Enum.GetValues(typeof(MobileDestination)))
                {
                    MobileActionControl action = presenter.PrimaryNavigation.GetAction(destination);
                    Assert.That(action.Root.resolvedStyle.height,
                        Is.GreaterThanOrEqualTo(MobileUiTokens.MinimumTouchTarget - 0.5f), destination.ToString());
                    AssertContained(safeContent, action.Root.worldBound, destination.ToString());
                }

                Rect gachaCard = presenter.Document.rootVisualElement
                    .Q<VisualElement>("home-gacha-card").worldBound;
                Rect collectionCard = presenter.Document.rootVisualElement
                    .Q<VisualElement>("home-collection-card").worldBound;
                Assert.That(collectionCard.xMin, Is.EqualTo(gachaCard.xMin).Within(1f));
                Assert.That(collectionCard.yMin, Is.GreaterThanOrEqualTo(gachaCard.yMax - 1f));

                foreach (string localeId in new[] { "en", "zh", "ja" })
                {
                    SelectLocale(localeId);
                    yield return null;
                    ScrollView scroll = presenter.Content as ScrollView;
                    Assert.That(scroll, Is.Not.Null);
                    scroll.ScrollTo(presenter.SettingsFeatureAction.Root);
                    yield return null;
                    yield return null;
                    Assert.That(scroll.contentViewport.worldBound.Contains(
                        presenter.SettingsFeatureAction.Root.worldBound.center), Is.True, localeId);
                    Assert.That(presenter.SettingsFeatureAction.Root.resolvedStyle.height,
                        Is.GreaterThanOrEqualTo(MobileUiTokens.MinimumTouchTarget - 0.5f), localeId);
                }
            }
            finally
            {
                if (originalLocale != null)
                    LocalizationSettings.SelectedLocale = originalLocale;
                presenter?.Dispose();
                Object.DestroyImmediate(host);
            }
        }

        private static IEnumerator SelectLanguageAndWait(
            string languageId,
            MobileHomePresenter presenter,
            string expectedTitleFragment)
        {
            SelectLocale(languageId);
            float deadline = Time.realtimeSinceStartup + 3f;
            while (!presenter.Title.text.Contains(expectedTitleFragment) && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(presenter.Title.text, Does.Contain(expectedTitleFragment));
        }

        private static void SelectLocale(string localeId)
        {
            Locale locale = LocalizationSettings.AvailableLocales.Locales.FirstOrDefault(
                candidate => candidate.Identifier.Code.StartsWith(localeId));
            Assert.That(locale, Is.Not.Null, localeId);
            LocalizationSettings.SelectedLocale = locale;
        }

        private static void InvokeAction(MobileActionControl control)
        {
            MethodInfo activate = typeof(MobileActionControl).GetMethod(
                "Activate",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(activate, Is.Not.Null);
            activate.Invoke(control, null);
        }

        private static IEnumerator ScrollToAndInvoke(ScrollView scrollView, MobileActionControl control)
        {
            scrollView.ScrollTo(control.Root);
            yield return null;
            yield return null;
            Assert.That(scrollView.contentViewport.worldBound.Contains(control.Root.worldBound.center), Is.True);
            InvokeAction(control);
            yield return null;
        }

        private static Rect InsetRect(Rect outer, IResolvedStyle style)
        {
            return new Rect(
                outer.xMin + style.paddingLeft,
                outer.yMin + style.paddingTop,
                outer.width - style.paddingLeft - style.paddingRight,
                outer.height - style.paddingTop - style.paddingBottom);
        }

        private static void AssertContained(Rect outer, Rect inner, string label)
        {
            Assert.That(inner.xMin, Is.GreaterThanOrEqualTo(outer.xMin - 1f), label + " left");
            Assert.That(inner.yMin, Is.GreaterThanOrEqualTo(outer.yMin - 1f), label + " top");
            Assert.That(inner.xMax, Is.LessThanOrEqualTo(outer.xMax + 1f), label + " right");
            Assert.That(inner.yMax, Is.LessThanOrEqualTo(outer.yMax + 1f), label + " bottom");
        }
    }
}
