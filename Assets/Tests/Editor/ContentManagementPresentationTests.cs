using System;
using System.Collections.Generic;
using System.IO;
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
    public void SharedRuntimePanel_UsesPortraitScreenScaling()
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
        string view = File.ReadAllText("Assets/UI/ContentManagementView.uxml");
        string controller = File.ReadAllText(
            "Assets/Scripts/Modules/Gacha.Presentation/ContentManagementController.cs");
        Match normalRule = Regex.Match(styles, @"(?ms)^\.content-button\s*\{(?<body>.*?)\}");
        Match pressedRule = Regex.Match(styles, @"(?ms)^\.content-button__label\.is-pressed\s*\{(?<body>.*?)\}");

        Assert.That(normalRule.Success, Is.True);
        Assert.That(pressedRule.Success, Is.True);
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
        Assert.That(controller, Does.Contain("Label.EnableInClassList(\"is-pressed\", value);"));
        Assert.That(controller, Does.Not.Contain("Root.EnableInClassList(\"is-pressed\", value);"),
            "Pointer input must never mutate the Android background render node.");
    }

    [Test]
    public void ContentLibrary_UsesFixedHeightVirtualizedListAndPlayerFilters()
    {
        VisualTreeAsset asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
            "Assets/UI/ContentManagementView.uxml");
        VisualElement root = asset.CloneTree();
        ListView list = root.Q<ListView>("package-list");

        Assert.That(list, Is.Not.Null);
        Assert.That(list.virtualizationMethod, Is.EqualTo(CollectionVirtualizationMethod.FixedHeight));
        Assert.That(list.fixedItemHeight, Is.EqualTo(160f));
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
                     "content.network.offline", "content.network.unknown"
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
