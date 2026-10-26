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
    /// <summary>
    /// Bounded S4 business matrix using production scenes, controllers, UXML and interactions.
    /// Catalog, inventory, preferences, image source and package operations are memory fixtures.
    /// Layout dimensions/insets are simulated panel units, not physical safe-area or Android evidence.
    /// Missing art is deliberate; this suite makes no artwork or screenshot acceptance claim.
    /// </summary>
    public sealed class RemainingUiStateMatrixPlayModeTests
    {
        private const string PackageId = "ja.matrix-pack";
        private const string Sentinel = "private-fixture-path exception-detail must-not-reach-players";
        private const string LongJapanese = "遠い星々を旅する仲間たちと集める特別なコレクション・日本語の長い名前でも確認と取消を選択できる拡張パック";
        private Locale originalLocale;
        private bool originalMotion, originalHaptics, originalSound;
        private float originalSpeed;
        private UniversalCatalog catalog;
        private MemoryInventory inventory;
        private MemoryPackages packages;
        private PackageFactory factory;
        private PackageCatalogProvider packageProvider;
        private LayoutViewport viewport;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return LocalizationSettings.InitializationOperation;
            originalLocale = LocalizationSettings.SelectedLocale;
            originalMotion = UIFeedbackService.ReduceMotion;
            originalHaptics = UIFeedbackService.HapticsEnabled;
            originalSound = UIFeedbackService.SoundEnabled;
            originalSpeed = UIFeedbackService.AnimationSpeed;
            catalog = CreateCatalog();
            inventory = new MemoryInventory();
            packages = new MemoryPackages();
            factory = new PackageFactory(packages);
            packageProvider = new PackageCatalogProvider(CreatePackageCatalog());
            var policy = new ContentDownloadPolicyService(
                new Storage(), new Network(), new Preferences(), safetyReserveBytes: 0);
            ApplicationServices.Configure(
                new CatalogSession(new CatalogProvider(catalog)),
                new LanguageSelectionService(new Preferences(), new[] { "en", "zh", "ja" }),
                images: new MissingImages(),
                productRules: new UniformSimulationRuleProvider(2),
                experienceSettings: new ExperienceSettingsService(new Preferences()),
                contentPackageOperations: factory,
                contentPackageCatalogs: packageProvider,
                contentPackageLifecycle: packages,
                contentDownloadPolicy: policy,
                contentPackageQueueState: new QueueStore());
            SetStatic("GachaViewController", "InventoryStoreOverride", inventory);
            SetStatic("CollectionViewController", "CollectionProgressStoreOverride", new CollectionStore());
            SettingsReturnNavigation.Clear();
            ContentReturnNavigation.Clear();
            UIFeedbackService.Configure(true, false, 1f, false);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            viewport?.Dispose();
            viewport = null;
            // Dispose live consumers before removing their injected services, including after failure.
            Scene active = SceneManager.GetActiveScene();
            Scene cleanup = SceneManager.CreateScene("Remaining UI Matrix Cleanup");
            SceneManager.SetActiveScene(cleanup);
            if (active.IsValid() && active.isLoaded)
                yield return SceneManager.UnloadSceneAsync(active);
            yield return null;
            SetStatic("GachaViewController", "InventoryStoreOverride", null);
            SetStatic("GachaViewController", "CatalogProviderOverride", null);
            SetStatic("GachaViewController", "SceneLoaderOverride", null);
            SetStatic("CollectionViewController", "CollectionProgressStoreOverride", null);
            SetStatic("CollectionViewController", "CatalogProviderOverride", null);
            SetStatic("CollectionViewController", "CatalogOverride", null);
            SetStatic("CollectionViewController", "SceneLoaderOverride", null);
            ContentManagementController.CatalogProviderOverride = null;
            ContentManagementController.OperationFactoryOverride = null;
            ContentManagementController.LifecycleOverride = null;
            ContentManagementController.DownloadPolicyOverride = null;
            ContentManagementController.QueueStateStoreOverride = null;
            SettingsReturnNavigation.Clear();
            ContentReturnNavigation.Clear();
            ApplicationServices.Reset();
            if (originalLocale != null)
                LocalizationSettings.SelectedLocale = originalLocale;
            UIFeedbackService.Configure(originalMotion, originalHaptics, originalSpeed, originalSound);
        }

        [UnityTest]
        public IEnumerator Gacha_LongJapaneseConfirmationAndReveal_ActionsRemainReachable()
        {
            bool verifiedCommittedBack = false;
            foreach (LayoutCase sample in Cases("en"))
            {
                yield return Load("003_GachaScene", "GachaViewController", sample);
                Component controller = Controller("GachaViewController");
                yield return Ready(controller);
                BindViewport(controller, sample);
                var confirmation = Field<MobileConfirmationPresenter>(controller, "confirmationPresenter");
                viewport.Bind(confirmation.Sheet);
                yield return Settle();
                int committedBefore = inventory.CommitCalls;
                Assert.That(Invoke<bool>(controller, "RequestOpenConfirmation", 1), Is.True);
                yield return Settle();
                Assert.That(confirmation.Sheet.Body.text, Does.Contain(LongJapanese));
                Assert.That(confirmation.Sheet.Body.resolvedStyle.whiteSpace, Is.EqualTo(WhiteSpace.Normal));
                yield return Reach(confirmation.Cancel.Root);
                Tap(confirmation.Cancel.Root);
                Assert.That(confirmation.IsVisible, Is.False);
                Assert.That(inventory.CommitCalls, Is.EqualTo(committedBefore));
                Assert.That(Invoke<bool>(controller, "RequestOpenConfirmation", 1), Is.True);
                yield return Reach(confirmation.Confirm.Root);
                Tap(confirmation.Confirm.Root);
                yield return Until(() => Property<string>(controller, "CurrentStage") == "Prepared", "prepared pack");
                Assert.That(inventory.CommitCalls, Is.EqualTo(committedBefore), "Confirmation must not commit inventory.");
                yield return Until(() => !Field<bool>(controller, "packAnimating"), "pack entrance");
                Assert.That(Invoke<bool>(controller, "TearPack"), Is.True);
                yield return Until(() => Property<string>(controller, "CurrentStage") == "Revealing", "reveal stage");
                VisualElement next = Root(controller).Q<VisualElement>("reveal-next-button");
                yield return Reach(next);
                Tap(next);
                yield return Until(() => Property<int>(controller, "RevealedCount") == 1, "first reveal");
                Assert.That(Root(controller).Q<Label>("reveal-name").text, Does.Contain(LongJapanese));
                VisualElement revealAll = Root(controller).Q<VisualElement>("reveal-all-button");
                yield return Reach(revealAll);
                if (!verifiedCommittedBack)
                {
                    Assert.That(Invoke<bool>(controller, "TryHandleBack"), Is.True);
                    verifiedCommittedBack = true;
                }
                else
                    Tap(revealAll);
                yield return Until(() => Property<bool>(controller, "IsSummaryVisible"), "summary");
                Assert.That(inventory.CommitCalls, Is.EqualTo(committedBefore + 1));
                Assert.That(Invoke<bool>(controller, "TearPack"), Is.False,
                    "The committed result cannot be drawn again from its summary.");
                Assert.That(inventory.CommitCalls, Is.EqualTo(committedBefore + 1));
                Assert.That(ApplicationServices.Languages.RequestedContentLanguageId, Is.EqualTo("ja"));
                ReleaseViewport();
            }
        }

        [UnityTest]
        public IEnumerator Collection_LongJapaneseDetailsAndFilters_CloseWithoutLosingTheSet()
        {
            foreach (LayoutCase sample in Cases("zh"))
            {
                yield return Load("004_CollectionScene", "CollectionViewController", sample);
                Component controller = Controller("CollectionViewController");
                yield return Ready(controller);
                BindViewport(controller, sample);
                var filters = Field<MobileSheetPresenter>(controller, "filterSheet");
                var details = Field<MobileSheetPresenter>(controller, "detailSheet");
                viewport.Bind(filters);
                viewport.Bind(details);
                Assert.That(Invoke<bool>(controller, "OpenSet", "matrix-set"), Is.True);
                yield return Reach(Root(controller).Q<VisualElement>("open-filters-button"));
                Tap(Root(controller).Q<VisualElement>("open-filters-button"));
                yield return Settle();
                Assert.That(filters.IsVisible, Is.True);
                yield return Reach(Root(controller).Q<VisualElement>("owned-only-button"));
                Tap(Root(controller).Q<VisualElement>("owned-only-button"));
                yield return Reach(Root(controller).Q<VisualElement>("clear-filters-button"));
                Tap(Root(controller).Q<VisualElement>("clear-filters-button"));
                yield return Reach(Root(controller).Q<VisualElement>("close-filters-button"));
                Tap(Root(controller).Q<VisualElement>("close-filters-button"));
                Assert.That(filters.IsVisible, Is.False);
                Assert.That(Invoke<bool>(controller, "ShowPrintingDetails", "matrix-printing-ja"), Is.True);
                yield return Settle();
                Assert.That(details.IsVisible, Is.True);
                Assert.That(Root(controller).Q<Label>("detail-name").text, Does.Contain(LongJapanese));
                yield return Reach(Root(controller).Q<VisualElement>("details-close-button"));
                Tap(Root(controller).Q<VisualElement>("details-close-button"));
                Assert.That(details.IsVisible, Is.False);
                Assert.That(Root(controller).Q<ListView>("card-list").resolvedStyle.display, Is.Not.EqualTo(DisplayStyle.None));
                Assert.That(ApplicationServices.Languages.RequestedContentLanguageId, Is.EqualTo("ja"));
                ReleaseViewport();
            }
        }

        [UnityTest]
        public IEnumerator Content_LongJapaneseDownloadAndRemoveConfirmations_CancelWithoutSideEffects()
        {
            foreach (LayoutCase sample in Cases("zh"))
            {
                packages.Installed = null;
                yield return Load("006_ContentScene", typeof(ContentManagementController).FullName, sample);
                var controller = Object.FindFirstObjectByType<ContentManagementController>();
                yield return Ready(controller);
                BindViewport(controller, sample);
                var confirmation = Field<MobileConfirmationPresenter>(controller, "confirmationPresenter");
                viewport.Bind(confirmation.Sheet);
                yield return Until(() => Root(controller).Q<VisualElement>("package-" + PackageId) != null, "package row");
                VisualElement download = Root(controller).Q<VisualElement>("package-" + PackageId).Q<VisualElement>("download-button");
                yield return Reach(download);
                Tap(download);
                yield return Settle();
                Assert.That(confirmation.IsVisible, Is.True);
                if (sample.Locale == "ja")
                    Assert.That(confirmation.Sheet.Body.text, Does.Contain(LongJapanese));
                yield return Reach(confirmation.Confirm.Root);
                yield return Reach(confirmation.Cancel.Root);
                Tap(confirmation.Cancel.Root);
                Assert.That(factory.TransferCalls, Is.Zero, "Review/cancel must never start a package transfer.");
                ReleaseViewport();

                // Reload with an installed receipt. Only memory metadata changes; no package is downloaded.
                ContentPackageDescriptor package = packageProvider.Catalog.Find(PackageId).Package;
                packages.Installed = new InstalledContentPackage(package.PackageId, package.InstallRelativePath,
                    package.Revision, package.Version, package.InstalledBytes, package.Sha256);
                yield return Load("006_ContentScene", typeof(ContentManagementController).FullName, sample);
                controller = Object.FindFirstObjectByType<ContentManagementController>();
                yield return Ready(controller);
                BindViewport(controller, sample);
                confirmation = Field<MobileConfirmationPresenter>(controller, "confirmationPresenter");
                viewport.Bind(confirmation.Sheet);
                yield return Until(() => Root(controller).Q<VisualElement>("package-" + PackageId) != null, "installed package row");
                VisualElement remove = Root(controller).Q<VisualElement>("package-" + PackageId).Q<VisualElement>("remove-button");
                yield return Reach(remove);
                Tap(remove);
                yield return Settle();
                Assert.That(confirmation.IsVisible, Is.True);
                if (sample.Locale == "ja")
                    Assert.That(confirmation.Sheet.Body.text, Does.Contain(LongJapanese));
                yield return Reach(confirmation.DangerConfirm.Root);
                yield return Reach(confirmation.Cancel.Root);
                Tap(confirmation.Cancel.Root);
                Assert.That(packages.RemoveCalls, Is.Zero);
                Assert.That(controller.IsPackageInstalled(PackageId), Is.True);
                ReleaseViewport();
            }
        }

        [UnityTest]
        public IEnumerator Settings_RecoveryConfirmation_IsReachableAndRestoresOnlyAfterExplicitConfirmation()
        {
            foreach (LayoutCase sample in Cases("en"))
            {
                yield return Load("005_SettingScene", "MainMenuBackController", sample);
                Component controller = Controller("MainMenuBackController");
                var page = Property<MobileSettingsPresenter>(controller, "SettingsPresenter");
                int restores = 0;
                SetField(controller, "operationOverridesForTests", new MobileSettingsOperationOverrides
                {
                    RecoveryAvailable = () => true,
                    PickerBusy = () => false,
                    ChooseImport = completed => completed(new MobileSettingsOperationResult(true, path: Sentinel)),
                    PreviewRecovery = _ => new MobileSettingsRecoveryPreviewData(
                        new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc), 123456, 999999, 8888, 7777, "ja", "ja"),
                    RestoreRecovery = _ => { restores++; return true; },
                    CancelPendingPicker = () => { }
                });
                Invoke<object>(controller, "RefreshAll");
                viewport = new LayoutViewport(page.Document, sample);
                viewport.Bind(page.Shell);
                viewport.Bind(page.Confirmation.Sheet);
                yield return Reach(page.ImportAction.Root);
                Tap(page.ImportAction.Root);
                yield return Until(() => Property<bool>(controller, "HasPendingImport"), "recovery preview");
                yield return Reach(page.ConfirmImportAction.Root);
                Tap(page.ConfirmImportAction.Root);
                yield return Settle();
                Assert.That(page.Confirmation.IsVisible, Is.True);
                Assert.That(page.Confirmation.Sheet.Body.resolvedStyle.whiteSpace, Is.EqualTo(WhiteSpace.Normal));
                yield return Reach(page.Confirmation.DangerConfirm.Root);
                yield return Reach(page.Confirmation.Cancel.Root);
                Tap(page.Confirmation.Cancel.Root);
                Assert.That(restores, Is.Zero);
                Assert.That(Property<bool>(controller, "HasPendingImport"), Is.True);
                yield return Reach(page.ConfirmImportAction.Root);
                Tap(page.ConfirmImportAction.Root);
                yield return Reach(page.Confirmation.DangerConfirm.Root);
                Tap(page.Confirmation.DangerConfirm.Root);
                Assert.That(restores, Is.EqualTo(1));
                Assert.That(Property<bool>(controller, "HasPendingImport"), Is.False);
                AssertNoSentinel(page.Document.rootVisualElement);
                ReleaseViewport();
            }
        }

        [UnityTest]
        public IEnumerator Gacha_CatalogFailure_SettingsAndBack_ActuallyReturnToFailedSource() =>
            FailureRoundTrip("003_GachaScene", "GachaViewController", MobileDestination.Gacha, "gacha-error-panel", "ja");

        [UnityTest]
        public IEnumerator Collection_CatalogFailure_SettingsAndBack_ActuallyReturnToFailedSource() =>
            FailureRoundTrip("004_CollectionScene", "CollectionViewController", MobileDestination.Collection, "collection-error-panel", "zh");

        [UnityTest]
        public IEnumerator Content_CatalogFailure_SettingsAndBack_ActuallyPreserveOriginalCaller() =>
            FailureRoundTrip("006_ContentScene", typeof(ContentManagementController).FullName, MobileDestination.Content, "content-error-panel", "en");

        private IEnumerator FailureRoundTrip(string scene, string type, MobileDestination source, string errorName, string locale)
        {
            var failedCatalog = new CatalogProvider(catalog) { Fail = true };
            if (source == MobileDestination.Content)
            {
                packageProvider.Fail = true;
                ContentReturnNavigation.RememberCurrentScene("003_GachaScene");
            }
            else
                SetStatic(type, "CatalogProviderOverride", failedCatalog);
            yield return Load(scene, type, new LayoutCase(locale, 720, 1600));
            Component sourceController = Controller(type);
            yield return Until(() => !string.IsNullOrWhiteSpace(Property<string>(sourceController, "InitializationError")), "catalog failure");
            yield return Settle();
            Assert.That(Property<bool>(sourceController, "IsReady"), Is.False);
            Assert.That(Root(sourceController).Q<VisualElement>(errorName).resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            AssertNoSentinel(Root(sourceController));
            VisualElement settings = Root(sourceController).Q<VisualElement>("nav-settings");
            yield return Reach(settings);
            Tap(settings);
            yield return Until(() => SceneManager.GetActiveScene().name == "005_SettingScene", "real settings transition");
            yield return Until(() => TryController("MainMenuBackController") != null &&
                Property<MobileSettingsPresenter>(Controller("MainMenuBackController"), "SettingsPresenter") != null, "settings presenter");
            Assert.That(SettingsReturnNavigation.PeekOrDefault(), Is.EqualTo(source));
            var page = Property<MobileSettingsPresenter>(Controller("MainMenuBackController"), "SettingsPresenter");
            yield return Reach(page.BackAction.Root);
            Tap(page.BackAction.Root);
            yield return Until(() => SceneManager.GetActiveScene().name == scene, "real return transition");
            yield return Until(() => TryController(type) != null &&
                !string.IsNullOrWhiteSpace(Property<string>(Controller(type), "InitializationError")), "returned failure state");
            Assert.That(Controller(type), Is.Not.SameAs(sourceController), "A new scene/controller must actually be loaded.");
            yield return Settle();
            Assert.That(Root(Controller(type)).Q<VisualElement>(errorName).resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(SettingsReturnNavigation.PeekOrDefault(), Is.EqualTo(MobileDestination.Home));
            AssertNoSentinel(Root(Controller(type)));
            if (source == MobileDestination.Content)
                Assert.That(ContentReturnNavigation.PeekOrDefault("002_MainMenuScene"), Is.EqualTo("003_GachaScene"));
            Assert.That(source == MobileDestination.Content ? packageProvider.Calls : failedCatalog.Calls, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator Content_SuspendFailure_DoesNotNavigateAndRestoresPendingControlsForRetry()
        {
            yield return Load("006_ContentScene", typeof(ContentManagementController).FullName, new LayoutCase("ja", 720, 1600));
            var controller = Object.FindFirstObjectByType<ContentManagementController>();
            yield return Ready(controller);
            var navigation = Field<MobilePrimaryNavigation>(controller, "primaryNavigation");
            var pending = new TaskCompletionSource<bool>();
            int requests = 0;
            SetField(controller, "suspendOperationsOverrideForTests", new Func<Task>(() => { requests++; return pending.Task; }));
            ContentReturnNavigation.RememberCurrentScene("004_CollectionScene");
            try
            {
                yield return Reach(navigation.GetAction(MobileDestination.Settings).Root);
                Tap(navigation.GetAction(MobileDestination.Settings).Root);
                yield return null;
                Assert.That(requests, Is.EqualTo(1));
                Assert.That(navigation.GetAction(MobileDestination.Home).IsEnabled, Is.False);
                Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("006_ContentScene"));
                pending.SetException(new InvalidOperationException(Sentinel));
                yield return Until(() => navigation.GetAction(MobileDestination.Home).IsEnabled, "pending controls restored");
                Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("006_ContentScene"));
                Assert.That(navigation.GetAction(MobileDestination.Content).IsSelected, Is.True);
                Assert.That(navigation.GetAction(MobileDestination.Settings).IsEnabled, Is.True);
                Assert.That(SettingsReturnNavigation.PeekOrDefault(), Is.EqualTo(MobileDestination.Home),
                    "A failed departure must not create a settings return destination.");
                Assert.That(ContentReturnNavigation.PeekOrDefault("002_MainMenuScene"), Is.EqualTo("004_CollectionScene"));
                Assert.That(Root(controller).Q<Label>("catalog-status").text,
                    Is.EqualTo(CardUiText.Get("common.navigation.failed")));
                AssertNoSentinel(Root(controller));
                SetField(controller, "suspendOperationsOverrideForTests", null);
                yield return Reach(navigation.GetAction(MobileDestination.Settings).Root);
                Tap(navigation.GetAction(MobileDestination.Settings).Root);
                yield return Until(() => SceneManager.GetActiveScene().name == "005_SettingScene", "retry uses production suspension and navigation");
                Assert.That(SettingsReturnNavigation.PeekOrDefault(), Is.EqualTo(MobileDestination.Content));
            }
            finally
            {
                if (controller != null)
                    SetField(controller, "suspendOperationsOverrideForTests", null);
                pending.TrySetResult(true);
            }
        }

        private IEnumerator Load(string scene, string type, LayoutCase sample)
        {
            ApplicationServices.Languages.SelectUiLanguage(sample.Locale);
            ApplicationServices.Languages.SelectContentLanguage("ja", catalog);
            LocalizationSettings.SelectedLocale = LocalizationSettings.AvailableLocales.Locales.Single(
                locale => locale.Identifier.Code == sample.Locale);
            yield return SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single);
            yield return Until(() => TryController(type) != null && Root(TryController(type)) != null, "controller shell " + type);
            yield return Settle();
        }

        private void BindViewport(Component controller, LayoutCase sample)
        {
            viewport = new LayoutViewport(controller.GetComponent<UIDocument>(), sample);
            viewport.Bind(Field<MobilePageShell>(controller, "mobilePageShell"));
        }

        private void ReleaseViewport() { viewport?.Dispose(); viewport = null; }
        private static IEnumerator Ready(Component controller) => Until(() => Property<bool>(controller, "IsReady"),
            "ready " + controller.GetType().Name);
        private static IEnumerator Settle() { for (int frame = 0; frame < 4; frame++) yield return null; }
        private static IEnumerator Until(Func<bool> condition, string description)
        {
            float deadline = Time.realtimeSinceStartup + 8f;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, description);
        }

        private static IEnumerator Reach(VisualElement action)
        {
            Assert.That(action, Is.Not.Null, "Action must exist in the production hierarchy.");
            yield return Settle();
            // Real scroll containers, innermost first: no position overrides or direct callback calls.
            for (VisualElement ancestor = action.parent; ancestor != null; ancestor = ancestor.parent)
                if (ancestor is ScrollView scroll)
                {
                    scroll.ScrollTo(action);
                    yield return Settle();
                }
            Rect bounds = action.worldBound;
            Assert.That(bounds.height, Is.GreaterThanOrEqualTo(47.5f), action.name + " touch height");
            Assert.That(bounds.width, Is.GreaterThan(0f), action.name + " width");
            Assert.That(action.enabledInHierarchy, Is.True, action.name + " enabled");
            for (VisualElement ancestor = action; ancestor != null; ancestor = ancestor.parent)
            {
                Assert.That(ancestor.resolvedStyle.display, Is.Not.EqualTo(DisplayStyle.None), action.name + " visible ancestor");
                if (ancestor is ScrollView scroll)
                    Contained(scroll.contentViewport.worldBound, bounds, action.name + " scroll viewport");
                if (ancestor.name == "safe-area" || ancestor.name == "sheet-safe-area")
                {
                    IResolvedStyle style = ancestor.resolvedStyle;
                    Rect safe = ancestor.worldBound;
                    Contained(Rect.MinMaxRect(safe.xMin + style.paddingLeft, safe.yMin + style.paddingTop,
                        safe.xMax - style.paddingRight, safe.yMax - style.paddingBottom), bounds,
                        action.name + " simulated safe area");
                }
            }
            Assert.That(action.panel, Is.Not.Null, action.name + " attached panel");
            VisualElement picked = action.panel.Pick(bounds.center);
            Assert.That(picked == action || action.Contains(picked), Is.True,
                $"{action.name} must receive real panel picking; bounds={bounds}, picked={picked?.name}");
            TestContext.WriteLine($"Reach locale={LocalizationSettings.SelectedLocale?.Identifier.Code} " +
                $"action={action.name} bounds={bounds} picked={picked?.name}");
        }

        private static void Tap(VisualElement action)
        {
            using (PointerDownEvent down = PointerDownEvent.GetPooled(new Event
                   { type = EventType.MouseDown, button = 0, mousePosition = action.worldBound.center }))
                action.SendEvent(down);
            using (PointerUpEvent up = PointerUpEvent.GetPooled(new Event
                   { type = EventType.MouseUp, button = 0, mousePosition = action.worldBound.center }))
                action.SendEvent(up);
        }

        private static void Contained(Rect outer, Rect inner, string description)
        {
            Assert.That(inner.xMin, Is.GreaterThanOrEqualTo(outer.xMin - 1f), description);
            Assert.That(inner.yMin, Is.GreaterThanOrEqualTo(outer.yMin - 1f), description);
            Assert.That(inner.xMax, Is.LessThanOrEqualTo(outer.xMax + 1f), description);
            Assert.That(inner.yMax, Is.LessThanOrEqualTo(outer.yMax + 1f), description);
        }

        private static void AssertNoSentinel(VisualElement root) => Assert.That(
            string.Join("\n", root.Query<Label>().ToList().Select(label => label.text)), Does.Not.Contain(Sentinel));
        private static Type RuntimeType(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name, false)).First(type => type != null);
        private static Component TryController(string name) => Object.FindFirstObjectByType(RuntimeType(name)) as Component;
        private static Component Controller(string name) => TryController(name) ?? throw new AssertionException("Missing " + name);
        private static VisualElement Root(Component owner) => owner?.GetComponent<UIDocument>()?.rootVisualElement;
        private static T Property<T>(object owner, string name) => (T)owner.GetType().GetProperty(name).GetValue(owner);
        private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
        private static void SetField(object owner, string name, object value)
        {
            FieldInfo field = owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            field.SetValue(owner, value);
        }
        private static T Invoke<T>(object owner, string name, params object[] arguments) => (T)owner.GetType().GetMethod(
            name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(owner, arguments);
        private static void SetStatic(string type, string name, object value) => RuntimeType(type).GetProperty(name).SetValue(null, value);

        private static IEnumerable<LayoutCase> Cases(string representative) => new[]
        {
            new LayoutCase("ja", 720, 1600),
            new LayoutCase("ja", 1000, 600),
            new LayoutCase(representative, 720, 1600)
        };

        private readonly struct LayoutCase
        {
            public LayoutCase(string locale, int width, int height) { Locale = locale; Width = width; Height = height; }
            public string Locale { get; }
            public int Width { get; }
            public int Height { get; }
        }

        private sealed class LayoutViewport : IDisposable
        {
            private readonly UIDocument document;
            private readonly PanelSettings original, panel;
            private readonly RenderTexture target;
            public LayoutViewport(UIDocument document, LayoutCase sample)
            {
                this.document = document;
                original = document.panelSettings;
                panel = Object.Instantiate(original);
                panel.hideFlags = HideFlags.HideAndDontSave;
                panel.scaleMode = PanelScaleMode.ConstantPixelSize;
                panel.scale = 1f;
                target = new RenderTexture(sample.Width, sample.Height, 24) { hideFlags = HideFlags.HideAndDontSave };
                Assert.That(target.Create(), Is.True);
                panel.targetTexture = target;
                document.panelSettings = panel;
                TestContext.WriteLine($"Layout fixture locale={sample.Locale} logical={sample.Width}x{sample.Height} " +
                    "padding L24 T48 R24 B72; cloned runtime panel, simulated insets, no physical-device claim.");
            }
            public void Bind(MobilePageShell shell) { Suspend(shell); Pad(shell.SafeArea); }
            public void Bind(MobileSheetPresenter sheet) { Suspend(sheet); Pad(sheet.SafeArea); }
            private static void Suspend(object presenter) => Field<UiToolkitSafeAreaBinding>(presenter, "safeAreaBinding").Suspend();
            private static void Pad(VisualElement safe)
            {
                safe.AddToClassList("mobile-layout--compact");
                safe.style.paddingLeft = 24;
                safe.style.paddingRight = 24;
                safe.style.paddingTop = 48;
                safe.style.paddingBottom = 72;
            }
            public void Dispose()
            {
                if (document != null) document.panelSettings = original;
                target.Release();
                Object.Destroy(target);
                Object.Destroy(panel);
            }
        }

        private static Dictionary<string, string> Names(string english = "A long collection title with room for readable actions") =>
            new Dictionary<string, string> { ["en"] = english, ["ja"] = LongJapanese, ["zh"] = "长名称收藏扩展包与可读的确认操作", ["zh-cn"] = "长名称收藏扩展包与可读的确认操作" };

        private static UniversalCatalog CreateCatalog()
        {
            var languages = new[] { "en", "ja", "zh-cn" }.Select(id => new LanguageDefinition(id, Names(id))).ToArray();
            var game = new GameDefinition("matrix-game", Names(), languages.Select(language => language.Id));
            var set = new SetDefinition("matrix-set", game.Id, Names());
            var item = new CollectibleItemDefinition("matrix-item", game.Id, Names(), "card");
            var rarity = new RarityDefinition("matrix-rare", game.Id, Names("Rare"), 1);
            var variant = new VariantDefinition("matrix-foil", game.Id, Names("Foil"));
            var printings = languages.Select(language => new PrintingDefinition("matrix-printing-" + language.Id,
                item.Id, new PrintingIdentity(game.Id, set.Id, "001", language.Id, variant.Id), rarity.Id, Names())).ToArray();
            var product = new ProductDefinition("matrix-product", game.Id, set.Id, Names(), "booster", printings.Select(printing => printing.Id));
            return new UniversalCatalog(languages, new[] { game }, new[] { set }, new[] { item }, new[] { rarity },
                new[] { variant }, printings, new[] { product });
        }

        private static ContentPackageCatalog CreatePackageCatalog()
        {
            string hash = new string('a', 64);
            var package = new ContentPackageDescriptor(PackageId, "ja/matrix", 1, "1.0.0", 100, 200, hash);
            return new ContentPackageCatalog(ContentPackageCatalog.SupportedSchemaVersion, 1, new[]
            {
                new ContentPackageCatalogEntry(package, new Uri("https://fixture.invalid/" + hash + ".zip"),
                    new ContentPackageMetadata("cards", Names(), "matrix-game", "ja", "matrix-set"))
            });
        }

        private sealed class CatalogProvider : ICatalogProvider
        {
            private readonly UniversalCatalog value;
            public CatalogProvider(UniversalCatalog value) { this.value = value; }
            public bool Fail;
            public int Calls;
            public CatalogLoadResult Load()
            {
                Calls++;
                return Fail ? CatalogLoadResult.Failure(Sentinel) : CatalogLoadResult.Success(value, 1, 1, value.Printings.Count);
            }
        }
        private sealed class PackageCatalogProvider : IContentPackageCatalogProvider
        {
            public PackageCatalogProvider(ContentPackageCatalog catalog) { Catalog = catalog; }
            public ContentPackageCatalog Catalog { get; }
            public bool Fail;
            public int Calls;
            public Task<ContentPackageCatalogLoadResult> LoadAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Calls++;
                return Task.FromResult(Fail ? ContentPackageCatalogLoadResult.Failure(Sentinel) : ContentPackageCatalogLoadResult.Success(Catalog));
            }
        }
        private sealed class MissingImages : IContentImageSource
        {
            public Task<ContentImageLoadResult> LoadAsync(string path, string expectedSha256 = null, CancellationToken cancellationToken = default) =>
                Task.FromResult(ContentImageLoadResult.Failure(ContentImageLoadStatus.NotFound, path, "Matrix intentionally has no card artwork."));
        }
        private sealed class Preferences : ILanguagePreferenceStore, IExperienceSettingsStore, IContentDownloadPreferenceStore
        {
            private LanguagePreferences languages = new LanguagePreferences("en", "ja");
            private ExperienceSettings experience = new ExperienceSettings(false, true, false);
            private ContentDownloadPreferences downloads = new ContentDownloadPreferences(true);
            LanguagePreferences ILanguagePreferenceStore.Load() => languages;
            void ILanguagePreferenceStore.Save(LanguagePreferences value) => languages = value;
            ExperienceSettings IExperienceSettingsStore.Load() => experience;
            void IExperienceSettingsStore.Save(ExperienceSettings value) => experience = value;
            ContentDownloadPreferences IContentDownloadPreferenceStore.Load() => downloads;
            void IContentDownloadPreferenceStore.Save(ContentDownloadPreferences value) => downloads = value;
        }
        private sealed class CollectionStore : ICollectionProgressStore
        {
            public CollectionItemProgress GetProgress(string id) => new CollectionItemProgress(id, 1, false);
            public bool MarkSeen(string id) => false;
        }
        private sealed class MemoryInventory : IInventoryProgressStore
        {
            public int CommitCalls;
            private readonly Dictionary<string, int> counts = new Dictionary<string, int>();
            public int GetProductsOpened(string productId) => CommitCalls;
            public ProductInventoryBatchCommit CommitBatch(ProductOpeningBatchCommitRequest request)
            {
                CommitCalls++;
                var commits = new List<ProductInventoryCommit>();
                foreach (ProductDrawResult draw in request.Draws)
                {
                    var awards = new List<InventoryAward>();
                    foreach (DrawnPrinting printing in draw.Printings)
                    {
                        counts.TryGetValue(printing.PrintingId, out int count);
                        counts[printing.PrintingId] = count + 1;
                        awards.Add(new InventoryAward(printing.PrintingId, count, count + 1));
                    }
                    commits.Add(new ProductInventoryCommit(draw.ProductId, CommitCalls, awards));
                }
                return new ProductInventoryBatchCommit(request.TransactionId, commits);
            }
            public IReadOnlyList<ProductOpeningHistoryEntry> GetOpeningHistory(int maximumCount) => Array.Empty<ProductOpeningHistoryEntry>();
            public ProductOpeningStatistics GetOpeningStatistics() => new ProductOpeningStatistics(null, null, null);
        }
        private sealed class Storage : IContentStorageProbe { public long GetAvailableBytes() => 1024 * 1024 * 1024; }
        private sealed class Network : IContentNetworkProbe { public ContentNetworkType GetNetworkType() => ContentNetworkType.WifiOrEthernet; }
        private sealed class QueueStore : IContentPackageQueueStateStore
        {
            private ContentPackageQueueResumeState state;
            public ContentPackageQueueResumeState Load() => state;
            public void Save(ContentPackageQueueResumeState value) => state = value;
            public void Clear() => state = null;
        }
        private sealed class MemoryPackages : IInstalledContentPackageRegistry, IContentPackageLifecycleService
        {
            public InstalledContentPackage Installed;
            public int RemoveCalls;
            public InstalledContentPackage Find(string packageId) => Installed?.PackageId == packageId ? Installed : null;
            public InstalledContentPackage FindInstalled(string packageId) => Find(packageId);
            public Task<ContentPackageRemovalResult> RemoveAsync(string packageId, CancellationToken cancellationToken = default)
            {
                RemoveCalls++;
                throw new AssertionException("This matrix only cancels removal; no lifecycle mutation is allowed.");
            }
        }
        private sealed class PackageFactory : IContentPackageInstallCoordinatorFactory, IContentPackageTransfer, IContentPackageInstaller
        {
            private readonly MemoryPackages packages;
            public PackageFactory(MemoryPackages packages) { this.packages = packages; }
            public int TransferCalls;
            public ContentPackageInstallCoordinator Create(ContentPackageCatalog catalog, string packageId) => new ContentPackageInstallCoordinator(
                catalog.Find(packageId).Package, new ContentPackagePlanner(packages, new Storage(), 0), this, this);
            public long GetDownloadedBytes(ContentPackageDescriptor package) => 0;
            public Task DownloadAsync(ContentPackageDescriptor package, long offset, IProgress<long> progress, CancellationToken cancellationToken)
            {
                TransferCalls++;
                throw new AssertionException("Matrix confirmation cancellation must not start downloads.");
            }
            public void DeletePartial(ContentPackageDescriptor package) { }
            public string GetArchivePath(ContentPackageDescriptor package) => null;
            public Task<ContentPackageInstallResult> InstallAsync(ContentInstallPlan plan, string archivePath, CancellationToken cancellationToken = default) =>
                throw new AssertionException("This matrix must not install content.");
        }
    }
}
