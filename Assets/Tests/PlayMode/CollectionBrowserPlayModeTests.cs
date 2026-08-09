using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Gacha.Application;
using Gacha.Domain;
using Gacha.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Gacha.Tests.PlayMode
{
    public class CollectionBrowserPlayModeTests
    {
        [UnityTest]
        public IEnumerator EmptyCatalog_KeepsMobileNavigationAndManageContentReachable()
        {
            Type controllerType = FindControllerType();
            PropertyInfo catalogOverride = StaticProperty(controllerType, "CatalogOverride");
            PropertyInfo storeOverride = StaticProperty(controllerType, "CollectionProgressStoreOverride");
            PropertyInfo sceneOverride = StaticProperty(controllerType, "SceneLoaderOverride");
            string routedScene = null;
            catalogOverride.SetValue(null, EmptyCatalog());
            storeOverride.SetValue(null, new MemoryCollectionProgressStore());
            sceneOverride.SetValue(null, (Action<string>)(scene => routedScene = scene));
            try
            {
                yield return SceneManager.LoadSceneAsync("004_CollectionScene", LoadSceneMode.Single);
                Component controller = FindController(controllerType);
                yield return WaitUntilReady(controller);
                UIDocument document = controller.GetComponent<UIDocument>();
                VisualElement root = document.rootVisualElement;

                Assert.That((int)GetProperty(controller, "InstalledSetCount"), Is.Zero);
                Assert.That(root.Q<VisualElement>("mobile-bottom-navigation").childCount, Is.EqualTo(5));
                Assert.That(root.Q<VisualElement>("nav-collection")
                    .Q<VisualElement>("action-selection-indicator").ClassListContains("is-selected"), Is.True);
                VisualElement manage = root.Q<VisualElement>("collection-manage-content-button");
                Assert.That(manage.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(manage.worldBound.height, Is.GreaterThanOrEqualTo(48f));
                Assert.That(root.Q<Label>("collection-zero-content-text").text, Is.Not.Empty);
                Assert.That(controller.gameObject.scene.GetRootGameObjects()
                    .SelectMany(sceneRoot => sceneRoot.GetComponentsInChildren<Canvas>(true))
                    .All(canvas => !canvas.gameObject.activeInHierarchy), Is.True);

                SendTap(manage);
                SendTap(root.Q<VisualElement>("nav-settings"));
                yield return null;
                Assert.That(routedScene, Is.EqualTo("006_ContentScene"));
                Assert.That((bool)GetProperty(controller, "NavigationPending"), Is.True);
                Assert.That(root.Q<VisualElement>("nav-content")
                    .Q<VisualElement>("action-selection-indicator").ClassListContains("is-selected"), Is.True);
            }
            finally
            {
                catalogOverride.SetValue(null, null);
                storeOverride.SetValue(null, null);
                sceneOverride.SetValue(null, null);
            }
        }

        [UnityTest]
        public IEnumerator CollectionScene_UsesSafeVirtualizedGridAndRealTouchActions()
        {
            Type controllerType = FindControllerType();
            var progressStore = new MemoryCollectionProgressStore();
            PropertyInfo storeOverride = StaticProperty(controllerType, "CollectionProgressStoreOverride");
            PropertyInfo sceneOverride = StaticProperty(controllerType, "SceneLoaderOverride");
            storeOverride.SetValue(null, progressStore);
            var routedScenes = new List<string>();
            sceneOverride.SetValue(null, (Action<string>)(scene => routedScenes.Add(scene)));
            string originalUiLanguage = null;
            bool originalReduceMotion = UIFeedbackService.ReduceMotion;
            bool originalHaptics = UIFeedbackService.HapticsEnabled;
            bool originalSound = UIFeedbackService.SoundEnabled;
            float originalAnimationSpeed = UIFeedbackService.AnimationSpeed;
            UIFeedbackService.Configure(true, false, 1f, false);
            try
            {
                yield return SceneManager.LoadSceneAsync("004_CollectionScene", LoadSceneMode.Single);
                Component controller = FindController(controllerType);
                yield return WaitUntilReady(controller);
                UIDocument document = controller.GetComponent<UIDocument>();
                VisualElement documentRoot = document.rootVisualElement;
                originalUiLanguage = ApplicationServices.Languages.UiLanguageId;

                Assert.That(documentRoot.Q<VisualElement>("safe-area")
                    .ClassListContains("safe-area-bound"), Is.True);
                Assert.That(documentRoot.Q<VisualElement>("mobile-bottom-navigation").childCount, Is.EqualTo(5));
                ListView setList = documentRoot.Q<ListView>("set-list");
                ListView cardList = documentRoot.Q<ListView>("card-list");
                Assert.That(setList.virtualizationMethod, Is.EqualTo(CollectionVirtualizationMethod.DynamicHeight));
                Assert.That(cardList.virtualizationMethod, Is.EqualTo(CollectionVirtualizationMethod.DynamicHeight));

                var viewport = new VisualElement { name = "collection-contract-viewport" };
                viewport.style.position = Position.Relative;
                viewport.style.width = 720f;
                viewport.style.height = 1600f;
                VisualElement host = documentRoot.Q<VisualElement>("collection-browser");
                VisualElement pageRoot = host.Q<VisualElement>("collection-page-shell");
                documentRoot.Clear();
                documentRoot.Add(viewport);
                viewport.Add(host);
                object pageShell = GetPrivateField(controller, "mobilePageShell");
                var safeBinding = GetPrivateField(pageShell, "safeAreaBinding") as UiToolkitSafeAreaBinding;
                Assert.That(safeBinding, Is.Not.Null);
                safeBinding.Suspend();
                VisualElement safeArea = pageRoot.Q<VisualElement>("safe-area");
                safeArea.AddToClassList("mobile-layout--compact");
                safeArea.style.paddingLeft = 48f;
                safeArea.style.paddingTop = 60f;
                safeArea.style.paddingRight = 12f;
                safeArea.style.paddingBottom = 84f;
                yield return null;
                yield return null;

                Rect safeContent = InsetRect(safeArea.worldBound, safeArea.resolvedStyle);
                AssertContained(safeContent, pageRoot.Q<VisualElement>("mobile-top-bar").worldBound, "top bar");
                AssertContained(safeContent,
                    pageRoot.Q<VisualElement>("mobile-bottom-navigation").worldBound,
                    "bottom navigation");
                foreach (string destination in new[] { "home", "gacha", "collection", "content", "settings" })
                {
                    VisualElement nav = pageRoot.Q<VisualElement>("nav-" + destination);
                    Assert.That(nav.worldBound.height, Is.GreaterThanOrEqualTo(48f), destination);
                    AssertContained(safeContent, nav.worldBound, destination);
                }

                ApplicationServices.Languages.SelectUiLanguage("ja");
                float deadline = Time.realtimeSinceStartup + 3f;
                while (pageRoot.Q<Label>("collection-title").text != "カードコレクション" &&
                       Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That(pageRoot.Q<Label>("collection-title").text, Is.EqualTo("カードコレクション"));
                Assert.That(pageRoot.Q<TextField>("card-search").label, Is.EqualTo("名前またはカード番号を検索"));
                Assert.That(pageRoot.Q<DropdownField>("card-sort").label, Is.EqualTo("並び順"));

                deadline = Time.realtimeSinceStartup + 3f;
                while (setList.Q<VisualElement>(className: "set-row") == null && Time.realtimeSinceStartup < deadline)
                    yield return null;
                VisualElement setAction = setList.Q<VisualElement>(className: "set-row");
                Assert.That(setAction, Is.Not.Null);
                SendTap(setAction);
                yield return null;
                Assert.That((int)GetProperty(controller, "CurrentCardCount"), Is.GreaterThan(0));

                var cardsBySet = (Dictionary<string, List<PrintingDefinition>>)GetPrivateField(
                    controller,
                    "cardsBySet");
                string populatedSet = cardsBySet
                    .OrderByDescending(pair => pair.Value.Count)
                    .First(pair => pair.Value.Count > 2)
                    .Key;
                Invoke(controller, "OpenSet", populatedSet);
                yield return null;
                yield return null;

                var visibleCards = ((IEnumerable<PrintingDefinition>)GetPrivateField(controller, "cards")).ToArray();
                progressStore.Set(visibleCards[0].Id, 2, true);
                progressStore.Set(visibleCards[1].Id, 1, false);
                Invoke(controller, "RefreshCollectionProgress");
                yield return null;
                Assert.That(cardList.itemsSource.Count, Is.EqualTo((visibleCards.Length + 1) / 2));
                VisualElement[] visibleTiles = cardList.Query<VisualElement>(className: "card-tile").ToList()
                    .Where(tile => tile.resolvedStyle.display == DisplayStyle.Flex &&
                                   tile.worldBound.width > 0f && tile.worldBound.height > 0f)
                    .Take(2)
                    .ToArray();
                Assert.That(visibleTiles.Length, Is.EqualTo(2));
                foreach (VisualElement tile in visibleTiles)
                {
                    Assert.That(tile.worldBound.xMin, Is.GreaterThanOrEqualTo(safeContent.xMin - 1f),
                        "Each virtualized column must stay inside the asymmetric Safe Area horizontally.");
                    Assert.That(tile.worldBound.xMax, Is.LessThanOrEqualTo(safeContent.xMax + 1f),
                        "Each virtualized column must stay inside the asymmetric Safe Area horizontally.");
                }
                VisualElement firstTile = visibleTiles[0];
                Assert.That(firstTile, Is.Not.Null);
                Assert.That(firstTile.worldBound.height, Is.GreaterThanOrEqualTo(48f));
                Label firstTileName = firstTile.Q<Label>(className: "card-tile__name");
                SendTap(firstTile, firstTile.worldBound.center, () =>
                    Assert.That(firstTileName.ClassListContains("is-pressed"), Is.True,
                        "The visible card name must own pressed feedback."));
                Assert.That(firstTileName.ClassListContains("is-pressed"), Is.False);
                yield return null;
                var detailSheet = GetPrivateField(controller, "detailSheet") as MobileSheetPresenter;
                Assert.That(detailSheet, Is.Not.Null);
                Assert.That(detailSheet.IsVisible, Is.True);
                Assert.That(progressStore.GetProgress(visibleCards[0].Id).IsNew, Is.False);

                var detailSafeBinding = GetPrivateField(detailSheet, "safeAreaBinding") as UiToolkitSafeAreaBinding;
                Assert.That(detailSafeBinding, Is.Not.Null);
                detailSafeBinding.Suspend();
                VisualElement detailSafeArea = pageRoot.Q<VisualElement>("collection-details-panel")
                    .Q<VisualElement>("sheet-safe-area");
                detailSafeArea.AddToClassList("mobile-layout--compact");
                detailSafeArea.style.paddingLeft = 48f;
                detailSafeArea.style.paddingTop = 60f;
                detailSafeArea.style.paddingRight = 12f;
                detailSafeArea.style.paddingBottom = 84f;
                yield return null;
                yield return null;
                Rect detailSafeContent = InsetRect(detailSafeArea.worldBound, detailSafeArea.resolvedStyle);
                VisualElement detailPanel = pageRoot.Q<VisualElement>("collection-details-panel")
                    .Q<VisualElement>("sheet-panel");
                AssertContained(detailSafeContent, detailPanel.worldBound, "collection detail panel");
                VisualElement close = pageRoot.Q<VisualElement>("details-close-button");
                Assert.That(close.worldBound.height, Is.GreaterThanOrEqualTo(48f));
                AssertContained(detailPanel.worldBound, close.worldBound, "detail close action");
                SendTap(close);
                yield return null;
                Assert.That(detailSheet.IsVisible, Is.False);

                SendTap(pageRoot.Q<VisualElement>("open-filters-button"));
                yield return null;
                var filterSheet = GetPrivateField(controller, "filterSheet") as MobileSheetPresenter;
                Assert.That(filterSheet, Is.Not.Null);
                Assert.That(filterSheet.IsVisible, Is.True);
                var filterSafeBinding = GetPrivateField(filterSheet, "safeAreaBinding") as UiToolkitSafeAreaBinding;
                Assert.That(filterSafeBinding, Is.Not.Null);
                filterSafeBinding.Suspend();
                VisualElement filterSafeArea = pageRoot.Q<VisualElement>("collection-filter-sheet")
                    .Q<VisualElement>("sheet-safe-area");
                filterSafeArea.style.paddingLeft = 48f;
                filterSafeArea.style.paddingTop = 60f;
                filterSafeArea.style.paddingRight = 12f;
                filterSafeArea.style.paddingBottom = 84f;
                yield return null;
                Rect filterSafeContent = InsetRect(filterSafeArea.worldBound, filterSafeArea.resolvedStyle);
                VisualElement filterPanel = pageRoot.Q<VisualElement>("collection-filter-sheet")
                    .Q<VisualElement>("sheet-panel");
                AssertContained(filterSafeContent, filterPanel.worldBound, "collection filter panel");
                VisualElement owned = pageRoot.Q<VisualElement>("owned-only-button");
                SendTap(owned);
                yield return null;
                Assert.That((int)GetProperty(controller, "CurrentCardCount"), Is.EqualTo(2));
                SendTap(pageRoot.Q<VisualElement>("clear-filters-button"));
                yield return null;
                Assert.That((int)GetProperty(controller, "CurrentCardCount"), Is.GreaterThan(2));
                SendTap(pageRoot.Q<VisualElement>("close-filters-button"));
                yield return null;
                Assert.That(filterSheet.IsVisible, Is.False);

                SendTap(pageRoot.Q<VisualElement>("nav-collection"));
                Assert.That(routedScenes, Is.Empty);
                SendTap(pageRoot.Q<VisualElement>("nav-content"));
                SendTap(pageRoot.Q<VisualElement>("nav-settings"));
                yield return null;
                Assert.That(routedScenes, Is.EqualTo(new[] { "006_ContentScene" }));
                Assert.That((bool)GetProperty(controller, "NavigationPending"), Is.True);
                VisualElement pendingTile = cardList.Query<VisualElement>(className: "card-tile").ToList()
                    .First(tile => tile.resolvedStyle.display == DisplayStyle.Flex);
                Assert.That(pendingTile.Q<Label>(className: "card-tile__name")
                    .ClassListContains("is-disabled"), Is.True,
                    "Navigation pending must leave visible disabled feedback on the card name.");
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(originalUiLanguage) && ApplicationServices.IsConfigured)
                    ApplicationServices.Languages.SelectUiLanguage(originalUiLanguage);
                storeOverride.SetValue(null, null);
                sceneOverride.SetValue(null, null);
                UIFeedbackService.Configure(
                    originalReduceMotion,
                    originalHaptics,
                    originalAnimationSpeed,
                    originalSound);
            }
        }

        [UnityTest]
        public IEnumerator DetailLanguageActions_SwitchOnlyInstalledPrintingWithoutChangingGlobalLanguages()
        {
            Type controllerType = FindControllerType();
            PropertyInfo catalogOverride = StaticProperty(controllerType, "CatalogOverride");
            PropertyInfo storeOverride = StaticProperty(controllerType, "CollectionProgressStoreOverride");
            UniversalCatalog testCatalog = BuildCardLanguageCatalog();
            var progressStore = new MemoryCollectionProgressStore();
            progressStore.Set("card-025-en", 2, false);
            progressStore.Set("card-025-ja", 5, false);
            catalogOverride.SetValue(null, testCatalog);
            storeOverride.SetValue(null, progressStore);
            string originalUiLanguage = null;
            string originalCardLanguage = null;
            bool originalReduceMotion = UIFeedbackService.ReduceMotion;
            bool originalHaptics = UIFeedbackService.HapticsEnabled;
            bool originalSound = UIFeedbackService.SoundEnabled;
            float originalAnimationSpeed = UIFeedbackService.AnimationSpeed;
            var cues = new List<FeedbackCue>();
            UIFeedbackService.FeedbackPlayed += cues.Add;
            UIFeedbackService.Configure(true, false, 1f, false);
            try
            {
                yield return SceneManager.LoadSceneAsync("004_CollectionScene", LoadSceneMode.Single);
                Component controller = FindController(controllerType);
                yield return WaitUntilReady(controller);
                originalUiLanguage = ApplicationServices.Languages.UiLanguageId;
                originalCardLanguage = ApplicationServices.Languages.RequestedContentLanguageId;
                ApplicationServices.Languages.SelectContentLanguage("en", testCatalog);
                yield return null;
                UIDocument document = controller.GetComponent<UIDocument>();
                Assert.That(((IEnumerable<SetDefinition>)GetPrivateField(controller, "sets"))
                    .Select(set => set.Id), Is.EqualTo(new[] { "language-set" }),
                    "The current card language must exclude sets with no matching installed printings.");
                Invoke(controller, "OpenSet", "language-set");
                yield return null;
                yield return null;
                Assert.That(((IEnumerable<PrintingDefinition>)GetPrivateField(controller, "cards"))
                    .Select(card => card.Identity.CardNumber), Is.EqualTo(new[] { "001", "002", "010", "025" }));
                DropdownField sort = document.rootVisualElement.Q<DropdownField>("card-sort");
                sort.index = 1;
                yield return null;
                Assert.That(((IEnumerable<PrintingDefinition>)GetPrivateField(controller, "cards"))
                    .Select(card => card.Id), Is.EqualTo(new[]
                    {
                        "card-025-en", "trainer-001-en", "trainer-002-en", "trainer-010-en"
                    }));

                VisualElement retryTile = document.rootVisualElement.Query<VisualElement>("card-tile-0").ToList()
                    .First(tile => tile.resolvedStyle.display == DisplayStyle.Flex &&
                                   tile.Q<VisualElement>("card-image-retry").resolvedStyle.display == DisplayStyle.Flex);
                var detailSheetBeforeRetry = GetPrivateField(controller, "detailSheet") as MobileSheetPresenter;
                cues.Clear();
                SendTap(retryTile.Q<VisualElement>("card-image-retry"));
                yield return null;
                Assert.That(cues, Does.Contain(FeedbackCue.Confirm),
                    "The image retry action must receive the tap.");
                Assert.That(detailSheetBeforeRetry.IsVisible, Is.False,
                    "Retrying card art must not open the enclosing card detail action.");

                Assert.That((bool)controllerType.GetMethod("ShowPrintingDetails")
                    .Invoke(controller, new object[] { "card-025-en" }), Is.True);
                yield return null;

                VisualElement switcher = document.rootVisualElement.Q<VisualElement>("detail-language-switcher");
                VisualElement[] languageActions = switcher.Children().ToArray();
                Assert.That((bool)GetProperty(controller, "HasDetailLanguageSwitcher"), Is.True);
                Assert.That((int)GetProperty(controller, "DetailLanguageCount"), Is.EqualTo(3));
                Assert.That(languageActions.Select(action => action.Q<Label>().text),
                    Is.EqualTo(new[] { "中", "EN", "日" }));
                Assert.That(languageActions.All(action => action.worldBound.height >= 48f), Is.True);

                SendTap(document.rootVisualElement.Q<VisualElement>("detail-language-ja"));
                yield return null;
                Assert.That(GetProperty(controller, "DetailPrintingId"), Is.EqualTo("card-025-ja"));
                Assert.That(document.rootVisualElement.Q<Label>("detail-name").text, Is.EqualTo("ピカチュウ"));
                Assert.That(document.rootVisualElement.Q<Label>("detail-metadata").text, Does.Contain("日本語セット"));
                Assert.That(document.rootVisualElement.Q<Label>("detail-progress").text, Does.Contain("5"));
                Assert.That(document.rootVisualElement.Q<VisualElement>("detail-language-ja")
                    .ClassListContains("is-selected"), Is.True);
                Assert.That(ApplicationServices.Languages.UiLanguageId, Is.EqualTo(originalUiLanguage));
                Assert.That(ApplicationServices.Languages.RequestedContentLanguageId, Is.EqualTo("en"));
                Assert.That(ApplicationServices.Languages.ContentLanguage.ResolvedLanguageId, Is.EqualTo("en"));

                Assert.That((bool)controllerType.GetMethod("ShowPrintingDetails")
                    .Invoke(controller, new object[] { "trainer-001-en" }), Is.True);
                yield return null;
                Assert.That((bool)GetProperty(controller, "HasDetailLanguageSwitcher"), Is.False);
                Assert.That(switcher.childCount, Is.Zero);
                Assert.That(switcher.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
            }
            finally
            {
                catalogOverride.SetValue(null, null);
                storeOverride.SetValue(null, null);
                UIFeedbackService.FeedbackPlayed -= cues.Add;
                if (ApplicationServices.IsConfigured)
                {
                    if (!string.IsNullOrWhiteSpace(originalUiLanguage))
                        ApplicationServices.Languages.SelectUiLanguage(originalUiLanguage);
                    UniversalCatalog installed = ApplicationServices.Catalog.EnsureLoaded().Catalog;
                    if (!string.IsNullOrWhiteSpace(originalCardLanguage))
                        ApplicationServices.Languages.SelectContentLanguage(originalCardLanguage, installed);
                }
                UIFeedbackService.Configure(
                    originalReduceMotion,
                    originalHaptics,
                    originalAnimationSpeed,
                    originalSound);
            }
        }

        private static Type FindControllerType() => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("CollectionViewController"))
            .First(type => type != null);

        private static PropertyInfo StaticProperty(Type type, string name) => type.GetProperty(
            name,
            BindingFlags.Static | BindingFlags.Public);

        private static Component FindController(Type type) => UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None)
            .Single(component => component.GetType() == type);

        private static IEnumerator WaitUntilReady(Component controller)
        {
            float deadline = Time.realtimeSinceStartup + 6f;
            while (!(bool)GetProperty(controller, "IsReady") && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That((bool)GetProperty(controller, "IsReady"), Is.True,
                GetProperty(controller, "InitializationError") as string);
            yield return null;
        }

        private static object GetProperty(object target, string name) => target.GetType()
            .GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(target);

        private static object GetPrivateField(object target, string name) => target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(target);

        private static void Invoke(object target, string name, params object[] arguments) => target.GetType()
            .GetMethod(name, BindingFlags.Instance | BindingFlags.Public)?.Invoke(target, arguments);

        private static Rect InsetRect(Rect bounds, IResolvedStyle style) => new Rect(
            bounds.xMin + style.paddingLeft,
            bounds.yMin + style.paddingTop,
            bounds.width - style.paddingLeft - style.paddingRight,
            bounds.height - style.paddingTop - style.paddingBottom);

        private static void AssertContained(Rect outer, Rect inner, string label)
        {
            Assert.That(inner.xMin, Is.GreaterThanOrEqualTo(outer.xMin - 1f), label);
            Assert.That(inner.yMin, Is.GreaterThanOrEqualTo(outer.yMin - 1f), label);
            Assert.That(inner.xMax, Is.LessThanOrEqualTo(outer.xMax + 1f), label);
            Assert.That(inner.yMax, Is.LessThanOrEqualTo(outer.yMax + 1f), label);
        }

        private static void SendTap(VisualElement control)
        {
            SendTap(control, control.worldBound.center);
        }

        private static void SendTap(VisualElement control, Vector2 position, Action afterPointerDown = null)
        {
            Assert.That(control, Is.Not.Null);
            using (PointerDownEvent down = PointerDownEvent.GetPooled(new Event
                   {
                       type = EventType.MouseDown,
                       button = 0,
                       mousePosition = position
                   }))
                control.SendEvent(down);
            afterPointerDown?.Invoke();
            using (PointerUpEvent up = PointerUpEvent.GetPooled(new Event
                   {
                       type = EventType.MouseUp,
                       button = 0,
                       mousePosition = position
                   }))
                control.SendEvent(up);
        }

        private static UniversalCatalog EmptyCatalog() => new UniversalCatalog(
            Array.Empty<LanguageDefinition>(),
            Array.Empty<GameDefinition>(),
            Array.Empty<SetDefinition>(),
            Array.Empty<CollectibleItemDefinition>(),
            Array.Empty<RarityDefinition>(),
            Array.Empty<VariantDefinition>(),
            Array.Empty<PrintingDefinition>(),
            Array.Empty<ProductDefinition>());

        private static UniversalCatalog BuildCardLanguageCatalog()
        {
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = "Pokemon TCG",
                ["ja"] = "ポケモンカード",
                ["zh-cn"] = "宝可梦卡牌"
            };
            var languages = new[]
            {
                new LanguageDefinition("en", new Dictionary<string, string> { ["en"] = "English" }),
                new LanguageDefinition("ja", new Dictionary<string, string> { ["ja"] = "日本語" }),
                new LanguageDefinition("zh-cn", new Dictionary<string, string> { ["zh-cn"] = "简体中文" })
            };
            var game = new GameDefinition("pokemon", names, languages.Select(value => value.Id));
            var set = new SetDefinition("language-set", game.Id, new Dictionary<string, string>
            {
                ["en"] = "English Set",
                ["ja"] = "日本語セット",
                ["zh-cn"] = "简体中文系列"
            });
            var japaneseOnlySet = new SetDefinition("japanese-only-set", game.Id,
                new Dictionary<string, string> { ["ja"] = "Japanese-only set" });
            var pokemon = new CollectibleItemDefinition("pikachu", game.Id, new Dictionary<string, string>
            {
                ["en"] = "Pikachu",
                ["ja"] = "ピカチュウ",
                ["zh-cn"] = "皮卡丘"
            }, "pokemon");
            var trainer = new CollectibleItemDefinition("trainer", game.Id,
                new Dictionary<string, string> { ["en"] = "Trainer" }, "trainer");
            var japaneseOnlyItem = new CollectibleItemDefinition("japanese-only-item", game.Id,
                new Dictionary<string, string> { ["ja"] = "Japanese-only card" }, "pokemon");
            var rarity = new RarityDefinition("rare", game.Id, new Dictionary<string, string>
            {
                ["en"] = "Rare",
                ["ja"] = "レア",
                ["zh-cn"] = "稀有"
            }, 1);
            var variant = new VariantDefinition("normal", game.Id, new Dictionary<string, string>
            {
                ["en"] = "Normal",
                ["ja"] = "通常",
                ["zh-cn"] = "普通"
            });
            var printings = new[]
            {
                new PrintingDefinition("card-025-en", pokemon.Id,
                    new PrintingIdentity(game.Id, set.Id, "025", "en", variant.Id), rarity.Id,
                    new Dictionary<string, string> { ["en"] = "Pikachu" }),
                new PrintingDefinition("card-025-ja", pokemon.Id,
                    new PrintingIdentity(game.Id, set.Id, "025", "ja", variant.Id), rarity.Id,
                    new Dictionary<string, string> { ["ja"] = "ピカチュウ" }),
                new PrintingDefinition("card-025-zh-cn", pokemon.Id,
                    new PrintingIdentity(game.Id, set.Id, "025", "zh-cn", variant.Id), rarity.Id,
                    new Dictionary<string, string> { ["zh-cn"] = "皮卡丘" }),
                new PrintingDefinition("trainer-001-en", trainer.Id,
                    new PrintingIdentity(game.Id, set.Id, "001", "en", variant.Id), rarity.Id,
                    new Dictionary<string, string> { ["en"] = "Trainer" }),
                new PrintingDefinition("trainer-002-en", trainer.Id,
                    new PrintingIdentity(game.Id, set.Id, "002", "en", variant.Id), rarity.Id,
                    new Dictionary<string, string> { ["en"] = "Trainer" }),
                new PrintingDefinition("trainer-010-en", trainer.Id,
                    new PrintingIdentity(game.Id, set.Id, "010", "en", variant.Id), rarity.Id,
                    new Dictionary<string, string> { ["en"] = "Trainer" }),
                new PrintingDefinition("japanese-only-card", japaneseOnlyItem.Id,
                    new PrintingIdentity(game.Id, japaneseOnlySet.Id, "001", "ja", variant.Id), rarity.Id,
                    new Dictionary<string, string> { ["ja"] = "Japanese-only Pikachu" })
            };
            return new UniversalCatalog(
                languages,
                new[] { game },
                new[] { set, japaneseOnlySet },
                new[] { pokemon, trainer, japaneseOnlyItem },
                new[] { rarity },
                new[] { variant },
                printings,
                Array.Empty<ProductDefinition>());
        }

        private sealed class MemoryCollectionProgressStore : ICollectionProgressStore
        {
            private readonly Dictionary<string, CollectionItemProgress> progress =
                new Dictionary<string, CollectionItemProgress>(StringComparer.Ordinal);

            public void Set(string printingId, int count, bool isNew)
            {
                progress[printingId] = new CollectionItemProgress(printingId, count, isNew);
            }

            public CollectionItemProgress GetProgress(string printingId)
            {
                return progress.TryGetValue(printingId, out CollectionItemProgress value)
                    ? value
                    : new CollectionItemProgress(printingId, 0, false);
            }

            public bool MarkSeen(string printingId)
            {
                CollectionItemProgress current = GetProgress(printingId);
                if (!current.IsNew)
                    return false;
                Set(printingId, current.OwnedCount, false);
                return true;
            }
        }
    }
}
