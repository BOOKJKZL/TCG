using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Gacha.Application;
using Gacha.Presentation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.Localization.Tables;
using UnityEngine.UIElements;

public class ContentManagementPresentationTests
{
    private sealed class QueuedContext : SynchronizationContext
    {
        private readonly Queue<Action> work = new Queue<Action>();

        public int Count
        {
            get
            {
                lock (work)
                    return work.Count;
            }
        }

        public override void Post(SendOrPostCallback callback, object state)
        {
            lock (work)
                work.Enqueue(() => callback(state));
        }

        public void Drain()
        {
            while (true)
            {
                Action next;
                lock (work)
                {
                    if (work.Count == 0)
                        return;
                    next = work.Dequeue();
                }
                next();
            }
        }
    }

    [TestCase(ContentPackageOperationState.Idle, null, ContentPackagePrimaryAction.Install, "content.status.ready")]
    [TestCase(ContentPackageOperationState.Planning, null, ContentPackagePrimaryAction.None, "content.status.checking")]
    [TestCase(ContentPackageOperationState.Downloading, null, ContentPackagePrimaryAction.None, "content.status.downloading")]
    [TestCase(ContentPackageOperationState.Paused, null, ContentPackagePrimaryAction.Resume, "content.status.paused")]
    [TestCase(ContentPackageOperationState.Installing, null, ContentPackagePrimaryAction.None, "content.status.installing")]
    [TestCase(ContentPackageOperationState.Succeeded, null, ContentPackagePrimaryAction.None, "content.status.installed")]
    [TestCase(ContentPackageOperationState.AlreadyCurrent, null, ContentPackagePrimaryAction.None, "content.status.current")]
    [TestCase(ContentPackageOperationState.Cancelled, null, ContentPackagePrimaryAction.Install, "content.status.cancelled")]
    [TestCase(ContentPackageOperationState.Failed, null, ContentPackagePrimaryAction.Retry, "content.status.failed")]
    [TestCase(ContentPackageOperationState.Blocked, ContentInstallPlanStatus.InsufficientSpace, ContentPackagePrimaryAction.Retry, "content.status.insufficient_space")]
    public void Resolve_MapsOperationToStableUiState(
        ContentPackageOperationState state,
        ContentInstallPlanStatus? planStatus,
        ContentPackagePrimaryAction action,
        string statusKey)
    {
        ContentPackageUiState result = ContentPackageItemPresentation.Resolve(state, planStatus);

        Assert.That(result.PrimaryAction, Is.EqualTo(action));
        Assert.That(result.StatusKey, Is.EqualTo(statusKey));
        Assert.That(result.IsBusy, Is.EqualTo(
            state == ContentPackageOperationState.Planning ||
            state == ContentPackageOperationState.Downloading ||
            state == ContentPackageOperationState.Installing));
        Assert.That(result.IsError, Is.EqualTo(
            state == ContentPackageOperationState.Failed ||
            state == ContentPackageOperationState.Blocked));
    }

    [TestCase(ContentInstallAction.Install, ContentPackagePrimaryAction.Install, "content.action.install")]
    [TestCase(ContentInstallAction.Update, ContentPackagePrimaryAction.Update, "content.action.update")]
    [TestCase(ContentInstallAction.Repair, ContentPackagePrimaryAction.Repair, "content.action.repair")]
    public void Resolve_UsesInstallPlanActionForReadyPackage(
        ContentInstallAction installAction,
        ContentPackagePrimaryAction expected,
        string key)
    {
        ContentPackageUiState result = ContentPackageItemPresentation.Resolve(
            ContentPackageOperationState.Idle,
            null,
            installAction);

        Assert.That(result.PrimaryAction, Is.EqualTo(expected));
        Assert.That(result.PrimaryActionKey, Is.EqualTo(key));
    }

    [Test]
    public void SharedRuntimePanel_UsesDesignReferenceWithoutFixedViewportCropping()
    {
        PanelSettings settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(
            "Assets/Resources/UI/Collection Panel Settings.asset");

        Assert.That(settings, Is.Not.Null);
        Assert.That(settings.scaleMode, Is.EqualTo(PanelScaleMode.ScaleWithScreenSize));
        Assert.That(settings.referenceResolution.x, Is.EqualTo(1000f));
        Assert.That(settings.referenceResolution.y, Is.EqualTo(2000f));
        Assert.That(settings.screenMatchMode, Is.EqualTo(PanelScreenMatchMode.MatchWidthOrHeight));
        Assert.That(settings.match, Is.EqualTo(0f),
            "Portrait UI must match width so tall Android screens gain vertical room instead of oversized controls.");
    }

    [Test]
    public void ContentActions_AvoidNativeAndroidButtonRenderPath()
    {
        string styles = File.ReadAllText("Assets/UI/Styles.uss").Replace("\r\n", "\n");
        string viewSource = File.ReadAllText("Assets/UI/ContentManagementView.uxml");
        string controllerSource = File.ReadAllText(
            "Assets/Scripts/Modules/Gacha.Presentation/ContentManagementController.cs");
        Assert.That(viewSource, Does.Not.Contain("style="));
        Assert.That(viewSource, Does.Not.Contain("<ui:Button"));
        Assert.That(controllerSource, Does.Contain("new MobilePageShell"));
        Assert.That(controllerSource, Does.Contain("new MobileTopBar"));
        Assert.That(controllerSource, Does.Contain("new MobilePrimaryNavigation"));
        Assert.That(controllerSource, Does.Contain("new MobileConfirmationPresenter"));
        string view = File.ReadAllText("Assets/UI/ContentManagementView.uxml");
        string controller = File.ReadAllText(
            "Assets/Scripts/Modules/Gacha.Presentation/ContentManagementController.cs");
        string sharedAction = File.ReadAllText(
            "Assets/Scripts/Modules/Gacha.Presentation/MobileGameDesignSystem.cs");
        Match normalRule = Regex.Match(styles, @"(?ms)^\.content-button\s*\{(?<body>.*?)\}");
        Match pressedRule = Regex.Match(styles, @"(?ms)^\.content-button__label\.is-pressed\s*\{(?<body>.*?)\}");
        Match disabledRule = Regex.Match(styles, @"(?ms)^\.content-button__label\.is-disabled\s*\{(?<body>.*?)\}");
        Match focusRule = Regex.Match(styles, @"(?ms)^\.content-button:focus \.content-button__label\s*\{(?<body>.*?)\}");
        Match disabledFocusRule = Regex.Match(styles,
            @"(?ms)^\.content-button:focus \.content-button__label\.is-disabled\s*\{(?<body>.*?)\}");

        Assert.That(normalRule.Success, Is.True);
        Assert.That(pressedRule.Success, Is.True);
        Assert.That(disabledRule.Success, Is.True,
            "Unavailable shared actions need visible child-label feedback without disabling the root.");
        Assert.That(focusRule.Success, Is.True,
            "Keyboard focus must remain visible without mutating the root render background.");
        Assert.That(disabledFocusRule.Success, Is.True,
            "Focus must not override the unavailable action's disabled appearance.");
        Assert.That(disabledFocusRule.Groups["body"].Value, Does.Contain("rgba(171, 190, 204, 0.58)"));
        Assert.That(view, Does.Not.Contain("<ui:Button"),
            "The Android content page must use stable VisualElement + Label action controls.");
        Assert.That(styles, Does.Not.Contain(".content-button:active"),
            "Native :active state has reproduced stale Android text/background geometry.");
        Assert.That(normalRule.Groups["body"].Value, Does.Not.Contain("scale"),
            "Android UI Toolkit can retain stale button text geometry after a scale transition.");
        Assert.That(normalRule.Groups["body"].Value, Does.Not.Contain("transition"),
            "Android content action backgrounds and borders must remain immutable after attachment.");
        Assert.That(pressedRule.Groups["body"].Value, Does.Not.Contain("scale"),
            "Content button press feedback must not use a render transform on Android.");
        Assert.That(pressedRule.Groups["body"].Value, Does.Not.Contain("background-color:"));
        Assert.That(pressedRule.Groups["body"].Value, Does.Not.Contain("border-color:"));
        Assert.That(pressedRule.Groups["body"].Value, Does.Contain("color:"),
            "Manual pressed feedback should repaint only the child label.");
        Assert.That(controller, Does.Contain("MobileActionControl"),
            "Content must exercise the shared Android-safe action binding.");
        Assert.That(controller, Does.Not.Contain("class StableActionControl"));
        Assert.That(sharedAction, Does.Contain("Label.EnableInClassList(\"is-pressed\", value);"));
        Assert.That(sharedAction, Does.Not.Contain("Root.EnableInClassList(\"is-pressed\", value);"),
            "Pointer input must never mutate the Android background render node.");
    }

    [Test]
    public void ContentLibrary_UsesDynamicHeightVirtualizedListAndPlayerFilters()
    {
        VisualTreeAsset asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
            "Assets/UI/ContentManagementView.uxml");
        VisualElement root = asset.CloneTree();
        ListView list = root.Q<ListView>("package-list");

        Assert.That(list, Is.Not.Null);
        Assert.That(list.virtualizationMethod, Is.EqualTo(CollectionVirtualizationMethod.DynamicHeight));
        string styles = File.ReadAllText("Assets/UI/Styles.uss").Replace("\r\n", "\n");
        Match rowRule = Regex.Match(styles, @"(?ms)^\.content-package-row\s*\{(?<body>.*?)\}");
        Assert.That(rowRule.Success, Is.True);
        Assert.That(rowRule.Groups["body"].Value, Does.Contain("height: auto;"),
            "DynamicHeight cannot grow localized rows while the default row has a fixed height.");
        Assert.That(styles, Does.Contain(".content-management .mobile-layout--compact .content-package-row__actions"));
        Assert.That(styles, Does.Contain("flex-wrap: wrap;"));
        Assert.That(root.Q<TextField>("content-search"), Is.Not.Null);
        Assert.That(root.Q<DropdownField>("content-language-filter"), Is.Not.Null);
        Assert.That(root.Q<DropdownField>("content-generation-filter"), Is.Not.Null);
        Assert.That(root.Q<DropdownField>("content-install-filter"), Is.Not.Null);
        Assert.That(root.Q<Toggle>("content-wifi-only"), Is.Not.Null);
        Assert.That(root.Q<Label>("content-selection-summary"), Is.Not.Null);
        foreach (string name in new[]
                 {
                     "select-filtered-button", "clear-selection-button", "download-selected-button",
                     "queue-pause-button", "queue-resume-button", "queue-retry-button",
                     "queue-cancel-button"
                 })
        {
            VisualElement action = root.Q<VisualElement>(name);
            Assert.That(action, Is.Not.Null, name);
            Assert.That(action, Is.Not.TypeOf<Button>(), name);
        }
        Assert.That(root.Q<ScrollView>("package-list"), Is.Null);
    }

    [Test]
    public void PrimaryToolkitViews_DeclareSharedSafeAreaRoots()
    {
        foreach (string path in new[]
                 {
                     "Assets/UI/GachaView.uxml",
                     "Assets/UI/CollectionView.uxml",
                     "Assets/Resources/UI/PokedexView.uxml"
                 })
        {
            string source = File.ReadAllText(path);
            Assert.That(source, Does.Contain("safe-area-root"), path);
        }

        string contentView = File.ReadAllText("Assets/UI/ContentManagementView.uxml");
        string contentController = File.ReadAllText(
            "Assets/Scripts/Modules/Gacha.Presentation/ContentManagementController.cs");
        Assert.That(contentView, Does.Not.Contain("safe-area-root"));
        Assert.That(contentController, Does.Contain("new MobilePageShell"));

        string helper = File.ReadAllText(
            "Assets/Scripts/Modules/Gacha.Presentation/UiToolkitSafeArea.cs");
        Assert.That(helper, Does.Contain("Screen.safeArea"));
        Assert.That(helper, Does.Contain("RuntimePanelUtils.ScreenToPanel"));
        Assert.That(helper, Does.Contain("UnregisterCallback"));
        Assert.That(helper, Does.Contain("poll?.Pause()"));
        Assert.That(helper, Does.Contain("paddingTop"));
        Assert.That(helper, Does.Contain("paddingBottom"));
    }

    [Test]
    public void ContentLifecycle_TriggersQueueAndIndependentSuspensionBeforeAwaitingEither()
    {
        string source = File.ReadAllText(
            "Assets/Scripts/Modules/Gacha.Presentation/ContentManagementController.cs");
        int helper = source.IndexOf("private async Task SuspendAllOperationsAsync()", StringComparison.Ordinal);
        int queueStart = source.IndexOf("Task queueSuspension =", helper, StringComparison.Ordinal);
        int independentStart = source.IndexOf("Task[] independentSuspensions =", helper, StringComparison.Ordinal);
        int combinedAwait = source.IndexOf(
            "await Task.WhenAll(new[] { queueSuspension, Task.WhenAll(independentSuspensions) });",
            helper,
            StringComparison.Ordinal);

        Assert.That(helper, Is.GreaterThanOrEqualTo(0));
        Assert.That(queueStart, Is.GreaterThan(helper));
        Assert.That(independentStart, Is.GreaterThan(queueStart));
        Assert.That(combinedAwait, Is.GreaterThan(independentStart),
            "Queue installation waiting must not delay suspension of an independent row download.");
    }

    [Test]
    public void CleanInstallSetup_IsLocalizedAndCatalogOnlyUntilPlayerConfirmation()
    {
        VisualTreeAsset setup = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
            "Assets/Resources/UI/FirstRunContentSetup.uxml");
        VisualElement root = setup.CloneTree();
        string controller = File.ReadAllText(
            "Assets/Scripts/004_Controller/FirstRunContentSetupController.cs");

        Assert.That(root.Query<Button>().ToList(), Is.Empty);
        foreach (string name in new[]
                 {
                     "setup-language-en", "setup-language-zh", "setup-language-ja",
                     "setup-content-language-en", "setup-content-language-zh",
                     "setup-content-language-ja", "setup-recommended",
                     "setup-manage", "setup-retry", "setup-later"
                 })
        {
            VisualElement action = root.Q<VisualElement>(name);
            Assert.That(action, Is.Not.Null, name);
            Assert.That(action.Q<Label>(), Is.Not.Null, name + " label");
        }
        Assert.That(controller, Does.Contain("ContentPackageCatalogs"));
        Assert.That(controller, Does.Contain("provider.LoadAsync(token)"));
        Assert.That(controller, Does.Not.Contain("DownloadAsync("),
            "First-run refresh may fetch catalog metadata but must never start a package ZIP download.");
        Assert.That(controller, Does.Not.Contain("persistentDataPath"),
            "Player copy describes app-managed storage without exposing an absolute path.");
    }

    [Test]
    public void ZeroContentActions_AreAvailableInAllThreeCardLanguages()
    {
        StringTable english = AssetDatabase.LoadAssetAtPath<StringTable>(
            "Assets/Resources/Data/Localization/Card_UI_en.asset");
        StringTable chinese = AssetDatabase.LoadAssetAtPath<StringTable>(
            "Assets/Resources/Data/Localization/Card_UI_zh.asset");
        StringTable japanese = AssetDatabase.LoadAssetAtPath<StringTable>(
            "Assets/Resources/Data/Localization/Card_UI_ja.asset");

        foreach (string key in new[]
                 {
                     "common.action.manage_content", "collection.status.no_content",
                     "content.catalog.empty", "content.filter.empty",
                     "content.recommended.title", "content.recommended.body",
                     "content.confirm.download.title", "content.confirm.download.body",
                     "content.confirm.download.package_body", "content.confirm.remove.title",
                     "content.confirm.remove.body", "content.action.confirm_download",
                     "content.action.confirm_remove", "content.action.cancel_confirmation",
                     "content.status.queued"
                 })
        {
            Assert.That(english.GetEntry(key)?.LocalizedValue, Is.Not.Empty, key + " en");
            Assert.That(chinese.GetEntry(key)?.LocalizedValue, Is.Not.Empty, key + " zh");
            Assert.That(japanese.GetEntry(key)?.LocalizedValue, Is.Not.Empty, key + " ja");
        }
    }

    [Test]
    public void ZeroContentViews_KeepManageAndReturnNavigationAvailable()
    {
        VisualTreeAsset pokedex = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
            "Assets/Resources/UI/PokedexView.uxml");
        VisualElement root = pokedex.CloneTree();
        string pokedexController = File.ReadAllText(
            "Assets/Scripts/Modules/Gacha.Pokemon.Presentation/PokemonPokedexController.cs");
        string contentController = File.ReadAllText(
            "Assets/Scripts/Modules/Gacha.Presentation/ContentManagementController.cs");

        Assert.That(root.Q<Button>("pokedex-empty-manage-button"), Is.Not.Null);
        Assert.That(pokedexController, Does.Contain("emptyManageDownloadsButton.style.display"));
        Assert.That(pokedexController, Does.Contain("!catalogLoad.HasInstalledContent"));
        Assert.That(pokedexController, Does.Contain("PokemonPokedexText.Get(\"content_missing\""));
        Assert.That(contentController, Does.Contain("ContentReturnNavigation.PeekOrDefault"));
        Assert.That(contentController, Does.Contain("catalog.Packages.Count == 0"));
        Assert.That(contentController, Does.Contain("L(\"content.filter.empty\")"));
    }

    [Test]
    public void OfflineCatalogLocalization_HasEnglishAndChineseEntries()
    {
        StringTable english = AssetDatabase.LoadAssetAtPath<StringTable>(
            "Assets/Resources/Data/Localization/Card_UI_en.asset");
        StringTable chinese = AssetDatabase.LoadAssetAtPath<StringTable>(
            "Assets/Resources/Data/Localization/Card_UI_zh.asset");

        foreach (string key in new[]
                 {
                     "content.catalog.cached", "content.catalog.cache_warning",
                     "content.filter.search", "content.filter.language", "content.filter.generation",
                     "content.filter.install", "content.filter.all", "content.filter.language.en",
                     "content.filter.language.ja", "content.filter.language.zh-cn",
                     "content.filter.installed", "content.filter.not_installed", "content.filter.update",
                     "content.action.select_filtered", "content.action.clear_selection",
                     "content.action.download_selected", "content.selection.none",
                     "content.selection.summary", "content.queue.summary",
                     "content.policy.wifi_only", "content.action.confirm_cellular",
                     "content.preflight.ready", "content.preflight.current",
                     "content.preflight.offline", "content.preflight.waiting_wifi",
                     "content.preflight.cellular_confirmation", "content.preflight.insufficient_space",
                     "content.preflight.storage_unavailable", "content.preflight.network_unavailable",
                     "content.network.wifi", "content.network.mobile",
                     "content.network.offline", "content.network.unknown",
                     "content.queue.restore_warning"
                 })
        {
            Assert.That(english.GetEntry(key)?.LocalizedValue, Is.Not.Empty, "Missing English key: " + key);
            Assert.That(chinese.GetEntry(key)?.LocalizedValue, Is.Not.Empty, "Missing Chinese key: " + key);
        }
        Assert.That(chinese.GetEntry("content.catalog.cached").LocalizedValue, Does.Contain("离线模式"));
    }

    [Test]
    public async Task Dispatcher_QueuesWorkerCallbackUntilUiThreadDrainsContext()
    {
        var context = new QueuedContext();
        var dispatcher = new SynchronizationContextUiThreadDispatcher(context);
        int uiThread = Environment.CurrentManagedThreadId;
        int observedThread = -1;

        await Task.Run(() => dispatcher.Post(() => observedThread = Environment.CurrentManagedThreadId));

        Assert.That(context.Count, Is.EqualTo(1));
        Assert.That(observedThread, Is.EqualTo(-1));
        context.Drain();
        Assert.That(observedThread, Is.EqualTo(uiThread));
        Assert.That(dispatcher.IsDispatchThread, Is.True);
    }

    [Test]
    public void Dispatcher_RunsUiThreadCallbackImmediately()
    {
        var dispatcher = new SynchronizationContextUiThreadDispatcher(new QueuedContext());
        bool called = false;

        dispatcher.Post(() => called = true);

        Assert.That(called, Is.True);
    }
}
