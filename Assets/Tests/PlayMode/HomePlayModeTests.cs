using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Gacha.Application;
using Gacha.Domain;
using Gacha.Presentation;
using Gacha.Pokemon.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

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
            var presenter = (MobileHomePresenter)controllers[0].GetType()
                .GetProperty("HomePresenter").GetValue(controllers[0]);
            Assert.That(presenter.PrimaryNavigation.Count, Is.EqualTo(3));
            Assert.That(presenter.GachaFeatureAction.IsLoading, Is.True);
            // The hidden exit confirmation owns separate actions; verify every current page entry.
            MobileActionControl[] pageEntries =
            {
                presenter.HomeNavigation, presenter.CollectionNavigation, presenter.PokedexNavigation,
                presenter.GachaFeatureAction, presenter.CollectionFeatureAction,
                presenter.ContentFeatureAction, presenter.SettingsUtilityAction,
                presenter.PrevPackAction, presenter.NextPackAction, presenter.InspectPackAction
            };
            foreach (MobileActionControl action in pageEntries)
            {
                Assert.That(action.Label.ClassListContains("is-disabled"), Is.True, action.Root.name);
                Assert.That(action.IsAvailable, Is.False, action.Root.name);
                Assert.That(action.TryActivate(), Is.False, action.Root.name);
            }
            Assert.That(routes, Is.EqualTo(new[] { "003_GachaScene" }));
            Assert.That(presenter.ExitConfirmation.IsVisible, Is.False);

            Scene cleanup = SceneManager.CreateScene("Home PlayMode Cleanup");
            Assert.That(SceneManager.SetActiveScene(cleanup), Is.True);
            yield return SceneManager.UnloadSceneAsync(scene);
        }

        [UnityTest]
        public IEnumerator MainMenuScene_FailedProductNavigationClearsRequestAndAllowsPokedexRetry()
        {
            yield return SceneManager.LoadSceneAsync("002_MainMenuScene", LoadSceneMode.Single);
            yield return null;
            yield return null;
            Scene scene = SceneManager.GetActiveScene();
            MonoBehaviour controller = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<MonoBehaviour>(true))
                .Single(component => component != null && component.GetType().Name == "MainMenuController");
            var presenter = (MobileHomePresenter)controller.GetType().GetProperty("HomePresenter").GetValue(controller);
            FieldInfo loader = controller.GetType().GetField("sceneLoaderOverrideForTests", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(loader, Is.Not.Null);
            loader.SetValue(controller, new Action<string>(_ => throw new InvalidOperationException("Expected scene-load failure fixture")));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("^Home navigation failed:"));
            MethodInfo openProduct = controller.GetType().GetMethod("OpenProduct");
            Assert.That(openProduct, Is.Not.Null);
            openProduct.Invoke(controller, new object[] { "fixture-product-id", true });
            Assert.That(HomeSceneRequest.TryConsumeProduct(out _, out _), Is.False,
                "A failed scene load must not leave a product request for a later unrelated visit.");
            Assert.That(presenter.Body.text, Is.EqualTo(CardUiText.Get("common.navigation.failed")));

            var routes = new List<string>();
            loader.SetValue(controller, new Action<string>(routes.Add));
            MethodInfo pokedex = controller.GetType().GetMethod("PokedexBtnClick");
            Assert.That(pokedex, Is.Not.Null);
            pokedex.Invoke(controller, null);
            controller.GetType().GetMethod("CollectionBtnClick").Invoke(controller, null);
            Assert.That(routes, Is.EqualTo(new[] { "004_CollectionScene" }));
            Assert.That(HomeSceneRequest.TryConsumeCollection(out bool openPokedex, out string printingId), Is.True);
            Assert.That(openPokedex, Is.True);
            Assert.That(printingId, Is.Null);
            Assert.That(presenter.PokedexNavigation.IsLoading, Is.True);
            HomeSceneRequest.Clear();
            Scene cleanup = SceneManager.CreateScene("Home Retry PlayMode Cleanup");
            Assert.That(SceneManager.SetActiveScene(cleanup), Is.True);
            yield return SceneManager.UnloadSceneAsync(scene);
        }

        [UnityTest]
        public IEnumerator Home_EmptyContentKeepsThreeDestinationsAndLocalizedRecoveryReachable()
        {
            yield return LocalizationSettings.InitializationOperation;
            Locale originalLocale = LocalizationSettings.SelectedLocale;
            var host = new GameObject("Mobile Home Empty Content Host");
            MobileHomePresenter presenter = null;
            int gachaClicks = 0, collectionClicks = 0, contentClicks = 0, settingsClicks = 0, pokedexClicks = 0;
            try
            {
                presenter = new MobileHomePresenter(host, () => gachaClicks++, () => collectionClicks++,
                    () => contentClicks++, () => settingsClicks++, () => pokedexClicks++);
                presenter.BindContent(HomeShowcaseData.FromCatalog(EmptyCatalog(), new Dictionary<string, int>(), "en"),
                    new MissingImages());
                yield return null;
                yield return null;
                VisualElement root = presenter.Document.rootVisualElement;
                Assert.That(root.Q<VisualElement>("mobile-home-page"), Is.SameAs(presenter.Shell.Root));
                Assert.That(presenter.Shell.SafeArea.ClassListContains("safe-area-bound"), Is.True);
                Assert.That(root.Query<Button>().ToList(), Is.Empty);
                Assert.That(presenter.ContentState, Is.EqualTo(HomeContentState.Empty));
                Assert.That(presenter.PrimaryNavigation.Count, Is.EqualTo(3));
                foreach (string hidden in new[] { "nav-gacha", "nav-content", "nav-settings" })
                    Assert.That(presenter.PrimaryNavigation.BottomNavigation.Root.Q<VisualElement>(hidden), Is.Null, hidden);
                Assert.That(presenter.TopBar.Actions.Contains(presenter.SettingsUtilityAction.Root), Is.True);
                Assert.That(presenter.HomeNavigation.IsSelected, Is.True);
                Assert.That(presenter.CollectionNavigation.IsSelected, Is.False);
                Assert.That(presenter.PokedexNavigation.IsSelected, Is.False);
                Assert.That(presenter.SelectedProductId, Is.Null.Or.Empty);
                Assert.That(presenter.PrevPackAction.IsAvailable, Is.False);
                Assert.That(presenter.NextPackAction.IsAvailable, Is.False);
                Assert.That(presenter.InspectPackAction.IsAvailable, Is.False);

                Assert.That(presenter.GachaFeatureAction.TryActivate(), Is.True);
                Assert.That(contentClicks, Is.EqualTo(1), "An empty home offers content management, never a fake opening.");
                Assert.That(gachaClicks, Is.Zero);
                var scroll = presenter.Content as ScrollView;
                Assert.That(scroll, Is.Not.Null);
                yield return ScrollToAndInvoke(scroll, presenter.CollectionFeatureAction);
                yield return ScrollToAndInvoke(scroll, presenter.ContentFeatureAction);
                InvokeAction(presenter.CollectionNavigation);
                InvokeAction(presenter.PokedexNavigation);
                InvokeAction(presenter.SettingsUtilityAction);
                Assert.That(collectionClicks, Is.EqualTo(2));
                Assert.That(contentClicks, Is.EqualTo(2));
                Assert.That(pokedexClicks, Is.EqualTo(1));
                Assert.That(settingsClicks, Is.EqualTo(1));
                scroll.scrollOffset = new Vector2(0f, 100f);
                InvokeAction(presenter.HomeNavigation);
                Assert.That(scroll.scrollOffset.y, Is.Zero);

                foreach (string locale in new[] { "en", "zh", "ja" })
                {
                    yield return SelectLanguageAndWait(locale, presenter, "home.pack.choose");
                    Assert.That(presenter.SectionTitle.text, Is.EqualTo(CardUiText.Get("home.collection.title")));
                    Assert.That(presenter.HomeNavigation.Label.text, Is.EqualTo(CardUiText.Get("home.nav.showcase")));
                    Assert.That(presenter.PokedexNavigation.Label.text, Is.EqualTo(CardUiText.Get("home.nav.pokedex")));
                    Assert.That(presenter.Title.resolvedStyle.whiteSpace, Is.EqualTo(WhiteSpace.Normal));
                }
                Assert.That(presenter.Shell.ContentSlot.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            }
            finally
            {
                presenter?.Dispose();
                Object.DestroyImmediate(host);
                LocalizationSettings.SelectedLocale = originalLocale;
            }
        }

        [UnityTest]
        public IEnumerator Home_CarouselRoutesSelectedCatalogProductAndRebindClearsRemovedSelection()
        {
            var host = new GameObject("Mobile Home Product Selection Host");
            MobileHomePresenter presenter = null;
            var requests = new List<(string id, bool prepare)>();
            UniversalCatalog catalog = FixtureCatalog();
            HomeShowcaseData data = HomeShowcaseData.FromCatalog(catalog, new Dictionary<string, int>(), "en");
            try
            {
                presenter = new MobileHomePresenter(host, () => Assert.Fail("Selected product must use its explicit route."),
                    () => { }, () => { }, () => { }, openProduct: (id, prepare) => requests.Add((id, prepare)));
                presenter.BindContent(data, new MissingImages());
                yield return null;
                yield return null;
                Assert.That(data.Packs.Count, Is.EqualTo(2));
                Assert.That(presenter.ContentState, Is.EqualTo(HomeContentState.Ready));
                Assert.That(presenter.SelectedProductId, Is.EqualTo(data.Packs[0].Product.Id));
                Assert.That(presenter.Content.Query<VisualElement>(className: "is-failed").ToList(), Is.Not.Empty,
                    "Missing artwork must expose a recoverable image state while the installed pack stays usable.");
                foreach (Label status in presenter.Content.Query<Label>("card-image-status").ToList())
                {
                    Assert.That(status.text, Is.Not.Empty);
                    Assert.That(status.text, Does.Not.Contain("Intentional artwork-free"));
                }
                Assert.That(presenter.NextPackAction.TryActivate(), Is.True);
                Assert.That(presenter.SelectedProductId, Is.EqualTo(data.Packs[1].Product.Id));
                Assert.That(presenter.InspectPackAction.TryActivate(), Is.True);
                Assert.That(presenter.GachaFeatureAction.TryActivate(), Is.True);
                Assert.That(requests, Is.EqualTo(new[] { (data.Packs[1].Product.Id, false), (data.Packs[1].Product.Id, true) }));
                Assert.That(presenter.PrevPackAction.TryActivate(), Is.True);
                Assert.That(presenter.SelectedProductId, Is.EqualTo(data.Packs[0].Product.Id));

                presenter.BindContent(HomeShowcaseData.FromCatalog(EmptyCatalog(), new Dictionary<string, int>(), "en"),
                    new MissingImages());
                Assert.That(presenter.ContentState, Is.EqualTo(HomeContentState.Empty));
                Assert.That(presenter.SelectedProductId, Is.Null.Or.Empty);
                Assert.That(presenter.InspectPackAction.TryActivate(), Is.False);
                Assert.That(requests.Count, Is.EqualTo(2), "Removed content must not retain a stale opening route.");
            }
            finally { presenter?.Dispose(); Object.DestroyImmediate(host); }
        }

        [UnityTest]
        public IEnumerator Home_LoadingFailureRetryAndNavigationFailurePreserveUsableShell()
        {
            yield return LocalizationSettings.InitializationOperation;
            var host = new GameObject("Mobile Home Recovery Host");
            MobileHomePresenter presenter = null;
            int collection = 0, settings = 0, pokedex = 0, retry = 0, content = 0;
            try
            {
                presenter = new MobileHomePresenter(host, () => { }, () => collection++, () => content++,
                    () => settings++, () => pokedex++, () => retry++);
                presenter.ShowLoading();
                yield return null;
                yield return null;
                Assert.That(presenter.ContentState, Is.EqualTo(HomeContentState.Loading));
                Assert.That(presenter.GachaFeatureAction.IsAvailable, Is.False);
                foreach (MobileActionControl action in new[] { presenter.CollectionNavigation,
                             presenter.PokedexNavigation, presenter.SettingsUtilityAction, presenter.ContentFeatureAction })
                    Assert.That(action.TryActivate(), Is.True, action.Root.name);
                Assert.That(new[] { collection, settings, pokedex, content }, Is.EqualTo(new[] { 1, 1, 1, 1 }));

                const string detail = "fixture-secret C:/private/catalog.json https://internal.invalid/token";
                presenter.ShowContentError(PlayerUiErrorMapper.FromDetail(detail, isOffline: true));
                yield return null;
                Assert.That(presenter.ContentState, Is.EqualTo(HomeContentState.Error));
                Assert.That(presenter.Body.text, Does.Not.Contain(detail));
                Assert.That(presenter.RetryContentAction.TryActivate(), Is.True);
                Assert.That(retry, Is.EqualTo(1));
                Assert.That(presenter.GachaFeatureAction.TryActivate(), Is.True);
                Assert.That(content, Is.EqualTo(2));
                presenter.ShowLoading();
                presenter.BindContent(HomeShowcaseData.FromCatalog(FixtureCatalog(), new Dictionary<string, int>(), "en"),
                    new MissingImages());
                Assert.That(presenter.ContentState, Is.EqualTo(HomeContentState.Ready));
                presenter.SetNavigationPending(MobileDestination.Pokedex);
                Assert.That(presenter.PokedexNavigation.IsLoading, Is.True);
                foreach (MobileActionControl action in new[] { presenter.HomeNavigation, presenter.CollectionNavigation,
                             presenter.PokedexNavigation, presenter.GachaFeatureAction, presenter.ContentFeatureAction,
                             presenter.SettingsUtilityAction, presenter.PrevPackAction, presenter.NextPackAction })
                    Assert.That(action.TryActivate(), Is.False, action.Root.name);
                presenter.ShowNavigationFailure();
                Assert.That(presenter.Body.text, Is.EqualTo(CardUiText.Get("common.navigation.failed")));
                Assert.That(presenter.PokedexNavigation.IsLoading, Is.False);
                Assert.That(presenter.PokedexNavigation.TryActivate(), Is.True);
                Assert.That(pokedex, Is.EqualTo(2));
            }
            finally { presenter?.Dispose(); Object.DestroyImmediate(host); }
        }

        [UnityTest]
        public IEnumerator Home_RebindingAndDisposingReleaseEveryArtworkLease()
        {
            var host = new GameObject("Mobile Home Lease Host");
            var cache = new LeaseImages();
            MobileHomePresenter presenter = null;
            var opened = new List<string>();
            UniversalCatalog catalog = FixtureCatalog();
            var owned = catalog.Printings.Values.ToDictionary(printing => printing.Id, _ => 1);
            HomeShowcaseData data = HomeShowcaseData.FromCatalog(catalog, owned, "en");
            try
            {
                presenter = new MobileHomePresenter(host, () => { }, () => { }, () => { }, () => { }, openPrinting: opened.Add);
                presenter.BindContent(data, cache);
                yield return null;
                Assert.That(cache.ActiveLeases, Is.GreaterThan(0));
                for (int index = 0; index < data.OwnedCards.Count; index++)
                {
                    VisualElement details = presenter.Content.Q<VisualElement>("home-owned-details-" + index);
                    Assert.That(details, Is.Not.Null);
                    SendKeyboardActivate(details);
                }
                Assert.That(opened, Is.EqualTo(data.OwnedCards.Select(printing => printing.Id).ToArray()));
                VisualElement previousDetails = presenter.Content.Q<VisualElement>("home-owned-details-0");
                presenter.SetNavigationPending(MobileDestination.Collection);
                SendKeyboardActivate(previousDetails);
                Assert.That(opened.Count, Is.EqualTo(data.OwnedCards.Count), "Pending navigation also locks owned-card actions.");
                presenter.ClearNavigationPending();
                presenter.BindContent(HomeShowcaseData.FromCatalog(EmptyCatalog(), new Dictionary<string, int>(), "en"), cache);
                Assert.That(cache.ActiveLeases, Is.Zero, "Rebinding must release pack and owned-card leases.");
                SendKeyboardActivate(previousDetails);
                Assert.That(opened.Count, Is.EqualTo(data.OwnedCards.Count), "Disposed card callbacks cannot route stale printings.");
                presenter.BindContent(data, cache);
                yield return null;
                Assert.That(cache.ActiveLeases, Is.GreaterThan(0));
                presenter.Document.enabled = false;
                yield return null;
                presenter.Dispose();
                Assert.That(cache.ActiveLeases, Is.Zero, "Disposal after UIDocument.OnDisable must still release every image lease.");
                presenter.Dispose();
                Assert.That(cache.ActiveLeases, Is.Zero, "Repeated disposal must not release the same lease twice.");
                Assert.That(presenter.Shell.Root.parent, Is.Null);
            }
            finally { presenter?.Dispose(); cache.Dispose(); Object.DestroyImmediate(host); }
        }

        [UnityTest]
        public IEnumerator Home_NarrowInsetsAndDoubleFontKeepPrimaryActionsReachableInThreeLocales()
        {
            yield return LocalizationSettings.InitializationOperation;
            Locale originalLocale = LocalizationSettings.SelectedLocale;
            var source = new TestFontScaleSource();
            source.Set(2f);
            using (MobileTypography.OverrideSourceForTests(source))
            {
                var host = new GameObject("Mobile Home Large Text Host");
                MobileHomePresenter presenter = null;
                try
                {
                    presenter = new MobileHomePresenter(host, () => { }, () => { }, () => { }, () => { });
                    presenter.BindContent(HomeShowcaseData.FromCatalog(FixtureCatalog(), new Dictionary<string, int>(), "en"),
                        new MissingImages());
                    var viewport = new VisualElement { name = "home-contract-viewport" };
                    viewport.style.position = Position.Relative;
                    viewport.style.width = 720f;
                    viewport.style.height = 1600f;
                    presenter.Document.rootVisualElement.Clear();
                    presenter.Document.rootVisualElement.Add(viewport);
                    viewport.Add(presenter.Shell.Root);
                    FieldInfo field = typeof(MobilePageShell).GetField("safeAreaBinding", BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.That(field, Is.Not.Null);
                    ((UiToolkitSafeAreaBinding)field.GetValue(presenter.Shell)).Suspend();
                    presenter.Shell.SafeArea.AddToClassList("mobile-layout--compact");
                    presenter.Shell.SafeArea.style.paddingLeft = 12f;
                    presenter.Shell.SafeArea.style.paddingTop = 60f;
                    presenter.Shell.SafeArea.style.paddingRight = 12f;
                    presenter.Shell.SafeArea.style.paddingBottom = 84f;
                    foreach (string locale in new[] { "en", "zh", "ja" })
                    {
                        SelectLocale(locale);
                        yield return LocalizationSettings.StringDatabase.GetTableAsync(CardUiText.TableName,
                            LocalizationSettings.SelectedLocale);
                        yield return null;
                        yield return null;
                        Rect safe = InsetRect(presenter.Shell.SafeArea.worldBound, presenter.Shell.SafeArea.resolvedStyle);
                        AssertContained(safe, presenter.TopBar.Root.worldBound, locale + " top bar");
                        AssertContained(safe, presenter.PrimaryNavigation.BottomNavigation.Root.worldBound, locale + " navigation");
                        foreach (MobileActionControl action in new[] { presenter.HomeNavigation, presenter.CollectionNavigation,
                                     presenter.PokedexNavigation, presenter.SettingsUtilityAction })
                        {
                            AssertContained(safe, action.Root.worldBound, locale + " " + action.Root.name);
                            AssertReadableAction(action, locale);
                        }
                        var scroll = (ScrollView)presenter.Content;
                        foreach (MobileActionControl action in new[] { presenter.GachaFeatureAction,
                                     presenter.CollectionFeatureAction, presenter.ContentFeatureAction })
                        {
                            scroll.ScrollTo(action.Root);
                            yield return null;
                            yield return null;
                            AssertContained(scroll.contentViewport.worldBound, action.Root.worldBound, locale + " " + action.Root.name);
                            AssertReadableAction(action, locale);
                        }
                    }
                }
                finally { presenter?.Dispose(); Object.DestroyImmediate(host); LocalizationSettings.SelectedLocale = originalLocale; }
            }
        }

        private static void AssertReadableAction(MobileActionControl action, string description)
        {
            Assert.That(action.Root.resolvedStyle.height, Is.GreaterThanOrEqualTo(MobileUiTokens.MinimumTouchTarget - 0.5f), description);
            Assert.That(action.Label.text, Is.Not.Empty, description);
            AssertContained(action.Root.worldBound, action.Label.worldBound, description + " label");
        }

        private static IEnumerator SelectLanguageAndWait(string languageId, MobileHomePresenter presenter, string titleKey)
        {
            SelectLocale(languageId);
            var locale = LocalizationSettings.SelectedLocale;
            var table = LocalizationSettings.StringDatabase.GetTableAsync(CardUiText.TableName, locale);
            yield return table;
            Assert.That(table.Result, Is.Not.Null);
            Assert.That(table.Result.LocaleIdentifier.Code, Is.EqualTo(languageId));
            string expected = table.Result.GetEntry(titleKey).GetLocalizedString(locale, null, null);
            float deadline = Time.realtimeSinceStartup + 3f;
            while (presenter.Title.text != expected && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(presenter.Title.text, Is.EqualTo(expected));
        }

        private static UniversalCatalog EmptyCatalog() => new UniversalCatalog(
            Array.Empty<LanguageDefinition>(), Array.Empty<GameDefinition>(), Array.Empty<SetDefinition>(),
            Array.Empty<CollectibleItemDefinition>(), Array.Empty<RarityDefinition>(), Array.Empty<VariantDefinition>(),
            Array.Empty<PrintingDefinition>(), Array.Empty<ProductDefinition>());

        internal static UniversalCatalog FixtureCatalog()
        {
            Dictionary<string, string> Names(string text) => new Dictionary<string, string>
                { ["en"] = text, ["zh"] = "测试收藏卡包名称", ["ja"] = "テストコレクションパック" };
            var language = new LanguageDefinition("en", Names("English"));
            var game = new GameDefinition("home-fixture", Names("Home fixture"), new[] { "en" });
            var set = new SetDefinition("home-set", game.Id, Names("Home set"));
            var item = new CollectibleItemDefinition("home-item", game.Id, Names("Home item"), "card");
            var rarity = new RarityDefinition("home-rare", game.Id, Names("Rare"), 1);
            var variant = new VariantDefinition("home-normal", game.Id, Names("Normal"));
            var printings = new[] { "001", "002", "003" }.Select(number => new PrintingDefinition("home-printing-" + number,
                item.Id, new PrintingIdentity(game.Id, set.Id, number, "en", variant.Id), rarity.Id, Names("Test card " + number))).ToArray();
            var products = new[] { "first", "second" }.Select(id => new ProductDefinition("home-product-" + id,
                game.Id, set.Id, Names("A readable installed card pack " + id), "booster", printings.Select(printing => printing.Id))).ToArray();
            return new UniversalCatalog(new[] { language }, new[] { game }, new[] { set }, new[] { item }, new[] { rarity },
                new[] { variant }, printings, products);
        }

        private sealed class MissingImages : ICardTextureCache
        {
            public Task<CardTextureLoadResult> LoadAsync(PrintingDefinition printing, CancellationToken cancellationToken = default) =>
                Task.FromResult(CardTextureLoadResult.Failure(ContentImageLoadStatus.NotFound, "Intentional artwork-free behavior fixture."));
        }

        private sealed class LeaseImages : ICardTextureCache, IDisposable
        {
            private readonly Texture2D texture = new Texture2D(2, 2);
            public int ActiveLeases { get; private set; }
            public Task<CardTextureLoadResult> LoadAsync(PrintingDefinition printing, CancellationToken cancellationToken = default)
            {
                ActiveLeases++;
                return Task.FromResult(CardTextureLoadResult.Success(texture, false, () => ActiveLeases--));
            }
            public void Dispose() => Object.DestroyImmediate(texture);
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
            Assert.That(control.TryActivate(), Is.True, control.Root.name + " must be available.");
        }

        private static void SendKeyboardActivate(VisualElement control)
        {
            control.Focus();
            using (KeyDownEvent down = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Return }))
                control.SendEvent(down);
            using (KeyUpEvent up = KeyUpEvent.GetPooled(new Event { type = EventType.KeyUp, keyCode = KeyCode.Return }))
                control.SendEvent(up);
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

    /// <summary>Actual Home -> destination scene routing. Content and inventory are memory fixtures;
    /// the Pokédex uses the existing read-only local taxonomy snapshot. No player save is written.</summary>
    public sealed class HomeShowcaseRoutingPlayModeTests
    {
        private UniversalCatalog catalog;
        private GuardInventory inventory;
        private Locale originalLocale;
        private bool originalMotion, originalHaptics, originalSound;
        private float originalSpeed;
        private readonly List<(PropertyInfo property, object value)> overrides = new List<(PropertyInfo, object)>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return LocalizationSettings.InitializationOperation;
            originalLocale = LocalizationSettings.SelectedLocale;
            originalMotion = UIFeedbackService.ReduceMotion;
            originalHaptics = UIFeedbackService.HapticsEnabled;
            originalSound = UIFeedbackService.SoundEnabled;
            originalSpeed = UIFeedbackService.AnimationSpeed;
            yield return UnloadCurrentScene();
            catalog = HomePlayModeTests.FixtureCatalog();
            inventory = new GuardInventory();
            var preferences = new MemoryPreferences();
            ApplicationServices.Configure(new CatalogSession(new FixedCatalog(catalog)),
                new LanguageSelectionService(preferences, new[] { "en", "zh", "ja" }),
                images: new MissingArtwork(), productRules: new UniformSimulationRuleProvider(2, "en"),
                experienceSettings: new ExperienceSettingsService(preferences));
            Override("GachaViewController", "InventoryStoreOverride", inventory);
            Override("GachaViewController", "CatalogProviderOverride", null);
            Override("GachaViewController", "SceneLoaderOverride", null);
            Override("CollectionViewController", "CollectionProgressStoreOverride", new EmptyCollection());
            Override("CollectionViewController", "CatalogOverride", null);
            Override("CollectionViewController", "CatalogProviderOverride", null);
            Override("CollectionViewController", "SceneLoaderOverride", null);
            PropertyInfo snapshot = typeof(PokemonPokedexController).GetProperty("SnapshotOverride");
            overrides.Add((snapshot, snapshot.GetValue(null)));
            snapshot.SetValue(null, null);
            HomeSceneRequest.Clear();
            UIFeedbackService.Configure(true, false, 1f, false);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return UnloadCurrentScene();
            for (int index = overrides.Count - 1; index >= 0; index--)
                overrides[index].property.SetValue(null, overrides[index].value);
            overrides.Clear();
            HomeSceneRequest.Clear();
            SettingsReturnNavigation.Clear();
            ContentReturnNavigation.Clear();
            ApplicationServices.Reset();
            LocalizationSettings.SelectedLocale = originalLocale;
            UIFeedbackService.Configure(originalMotion, originalHaptics, originalSpeed, originalSound);
        }

        [UnityTest]
        public IEnumerator HomeProductRequest_SelectsExactNonFirstProductAndOnlyPreparesConfirmation()
        {
            ProductDefinition[] products = catalog.Products.Values.OrderBy(product => product.Id, StringComparer.Ordinal).ToArray();
            Assert.That(products.Length, Is.EqualTo(2));
            string requested = products[1].Id;
            foreach (bool prepare in new[] { false, true })
            {
                yield return RouteFromHome("OpenProduct", "003_GachaScene", requested, prepare);
                Component controller = Controller("GachaViewController");
                yield return WaitUntilReady(controller);
                Assert.That(Read<int>(controller, "ProductCount"), Is.EqualTo(2));
                Assert.That(Read<string>(controller, "SelectedProductId"), Is.EqualTo(requested),
                    "Home's explicit selection must override the destination's normal first-product selection.");
                var list = controller.GetComponent<UIDocument>().rootVisualElement.Q<ListView>("product-list");
                Assert.That(((ProductDefinition)list.selectedItem).Id, Is.EqualTo(requested));
                Assert.That(Read<bool>(controller, "IsConfirmationVisible"), Is.EqualTo(prepare));
                if (prepare)
                {
                    var confirmation = Field<MobileConfirmationPresenter>(controller, "confirmationPresenter");
                    Assert.That(confirmation.Sheet.Body.text, Does.Contain(products[1].GetDisplayName("en")));
                }
                Assert.That(Read<string>(controller, "CurrentStage"), Is.EqualTo("Selection"),
                    "Home may show confirmation, but cannot enter the Prepared opening stage before confirmation.");
                Assert.That(Read<int>(controller, "LastOpenedCardCount"), Is.Zero);
                Assert.That(inventory.CommitCalls, Is.Zero, "Inspect/open entry must not award inventory before confirmation and reveal.");
                Assert.That(HomeSceneRequest.TryConsumeProduct(out _, out _), Is.False,
                    "The destination controller must consume the request itself exactly once.");
            }
        }

        [UnityTest]
        public IEnumerator HomeMissingProductRequest_DoesNotFallBackToFirstInstalledPack()
        {
            yield return RouteFromHome("OpenProduct", "003_GachaScene", "removed-home-product", true);
            Component controller = Controller("GachaViewController");
            yield return WaitUntilReady(controller);
            Assert.That(Read<int>(controller, "ProductCount"), Is.EqualTo(2), "Other installed packs remain available.");
            Assert.That(Read<string>(controller, "SelectedProductId"), Is.Null);
            Assert.That(controller.GetComponent<UIDocument>().rootVisualElement.Q<ListView>("product-list").selectedItem, Is.Null);
            Assert.That(Read<bool>(controller, "IsConfirmationVisible"), Is.False);
            Assert.That(Field<MobileActionControl>(controller, "prepareAction").TryActivate(), Is.False);
            Assert.That((bool)controller.GetType().GetMethod("RequestOpenConfirmation").Invoke(controller, new object[] { 1 }), Is.False);
            Assert.That(inventory.CommitCalls, Is.Zero);
            VisualElement root = controller.GetComponent<UIDocument>().rootVisualElement;
            Assert.That(root.Q<Label>("gacha-status").text, Is.EqualTo(CardUiText.Get("home.request.product_unavailable")));
            var manage = Field<MobileActionControl>(controller, "manageContentAction");
            Assert.That(manage.Root.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            string destination = null;
            TypeOf("GachaViewController").GetProperty("SceneLoaderOverride").SetValue(null,
                new Action<string>(scene => destination = scene));
            Assert.That(manage.TryActivate(), Is.True);
            Assert.That(destination, Is.EqualTo("006_ContentScene"));
            Assert.That(HomeSceneRequest.TryConsumeProduct(out _, out _), Is.False);
        }

        [UnityTest]
        public IEnumerator HomePokedexRequest_CollectionAutomaticallyOpensRealPokedex()
        {
            yield return RouteFromHome("PokedexBtnClick", "004_CollectionScene");
            Component collection = Controller("CollectionViewController");
            yield return WaitUntilReady(collection);
            PokemonPokedexController pokedex = collection.GetComponent<PokemonPokedexController>();
            Assert.That(pokedex, Is.Not.Null);
            float deadline = Time.realtimeSinceStartup + 8f;
            while (!pokedex.IsReady && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(pokedex.IsReady, Is.True, pokedex.InitializationError);
            yield return null;
            VisualElement overlay = collection.GetComponent<UIDocument>().rootVisualElement.Q<VisualElement>("pokedex-overlay");
            Assert.That(overlay, Is.Not.Null);
            Assert.That(overlay.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex),
                "Collection must open the requested overlay without this test calling PokemonPokedexController.Open().");
            Assert.That(overlay.resolvedStyle.opacity, Is.EqualTo(1f).Within(0.01f));
            Assert.That(pokedex.VisibleSpeciesCount, Is.GreaterThan(0));
            Assert.That(pokedex.CurrentGenerationId, Is.EqualTo("generation-1"));
            Assert.That(HomeSceneRequest.TryConsumeCollection(out _, out _), Is.False);
            Assert.That(inventory.CommitCalls, Is.Zero);
        }

        private static IEnumerator RouteFromHome(string methodName, string expectedScene, params object[] arguments)
        {
            yield return SceneManager.LoadSceneAsync("002_MainMenuScene", LoadSceneMode.Single);
            yield return null;
            yield return null;
            Component home = Controller("MainMenuController");
            AsyncOperation transition = null;
            string requestedScene = null;
            FieldInfo loader = home.GetType().GetField("sceneLoaderOverrideForTests", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(loader, Is.Not.Null);
            // This seam observes the route but performs the real Unity scene transition.
            loader.SetValue(home, new Action<string>(scene =>
            {
                requestedScene = scene;
                transition = SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single);
            }));
            MethodInfo method = home.GetType().GetMethod(methodName);
            Assert.That(method, Is.Not.Null);
            method.Invoke(home, arguments);
            Assert.That(requestedScene, Is.EqualTo(expectedScene));
            Assert.That(transition, Is.Not.Null);
            yield return transition;
            yield return null;
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(expectedScene));
        }

        private static IEnumerator WaitUntilReady(Component controller)
        {
            float deadline = Time.realtimeSinceStartup + 6f;
            while (!Read<bool>(controller, "IsReady") && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(Read<bool>(controller, "IsReady"), Is.True, Read<string>(controller, "InitializationError"));
            yield return null;
        }

        private static IEnumerator UnloadCurrentScene()
        {
            Scene active = SceneManager.GetActiveScene();
            Scene cleanup = SceneManager.CreateScene("Home routing cleanup " + Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(cleanup);
            if (active.IsValid() && active.isLoaded)
                yield return SceneManager.UnloadSceneAsync(active);
            yield return null;
        }

        private void Override(string type, string name, object value)
        {
            PropertyInfo property = TypeOf(type).GetProperty(name, BindingFlags.Static | BindingFlags.Public);
            Assert.That(property, Is.Not.Null, type + "." + name);
            overrides.Add((property, property.GetValue(null)));
            property.SetValue(null, value);
        }
        private static Type TypeOf(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name)).First(type => type != null);
        private static Component Controller(string name) => Object.FindFirstObjectByType(TypeOf(name)) as Component;
        private static T Read<T>(Component controller, string name) => (T)controller.GetType().GetProperty(name).GetValue(controller);
        private static T Field<T>(Component controller, string name) => (T)controller.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);

        private sealed class FixedCatalog : ICatalogProvider
        {
            private readonly UniversalCatalog value;
            public FixedCatalog(UniversalCatalog value) { this.value = value; }
            public CatalogLoadResult Load() => CatalogLoadResult.Success(value, value.Sets.Count, value.Items.Count, value.Printings.Count);
        }
        private sealed class MissingArtwork : IContentImageSource
        {
            public Task<ContentImageLoadResult> LoadAsync(string path, string expectedSha256 = null,
                CancellationToken cancellationToken = default) => Task.FromResult(ContentImageLoadResult.Failure(
                    ContentImageLoadStatus.NotFound, path, "Routing-only fixture intentionally omits card artwork."));
        }
        private sealed class MemoryPreferences : ILanguagePreferenceStore, IExperienceSettingsStore
        {
            private LanguagePreferences languages = new LanguagePreferences("en", "en");
            private ExperienceSettings experience = new ExperienceSettings(false, true, false);
            LanguagePreferences ILanguagePreferenceStore.Load() => languages;
            void ILanguagePreferenceStore.Save(LanguagePreferences value) => languages = value;
            ExperienceSettings IExperienceSettingsStore.Load() => experience;
            void IExperienceSettingsStore.Save(ExperienceSettings value) => experience = value;
        }
        private sealed class EmptyCollection : ICollectionProgressStore
        {
            public CollectionItemProgress GetProgress(string id) => new CollectionItemProgress(id, 0, false);
            public bool MarkSeen(string id) => false;
        }
        private sealed class GuardInventory : IInventoryProgressStore
        {
            public int CommitCalls { get; private set; }
            public int GetProductsOpened(string productId) => 0;
            public IReadOnlyList<ProductOpeningHistoryEntry> GetOpeningHistory(int maximumCount) => Array.Empty<ProductOpeningHistoryEntry>();
            public ProductOpeningStatistics GetOpeningStatistics() => new ProductOpeningStatistics(null, null, null);
            public ProductInventoryBatchCommit CommitBatch(ProductOpeningBatchCommitRequest request)
            {
                CommitCalls++;
                throw new AssertionException("Home route/confirmation must not commit inventory.");
            }
        }
    }

}
