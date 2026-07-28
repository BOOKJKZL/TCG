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
        Match normalRule = Regex.Match(styles, @"(?ms)^\.content-button\s*\{(?<body>.*?)\}");
        Match pressedRule = Regex.Match(styles, @"(?ms)^\.content-button\.is-pressed\s*\{(?<body>.*?)\}");

        Assert.That(normalRule.Success, Is.True);
        Assert.That(pressedRule.Success, Is.True);
        Assert.That(view, Does.Not.Contain("<ui:Button"),
            "The Android content page must use stable VisualElement + Label action controls.");
        Assert.That(styles, Does.Not.Contain(".content-button:active"),
            "Native :active state has reproduced stale Android text/background geometry.");
        Assert.That(normalRule.Groups["body"].Value, Does.Not.Contain("scale"),
            "Android UI Toolkit can retain stale button text geometry after a scale transition.");
        Assert.That(pressedRule.Groups["body"].Value, Does.Not.Contain("scale"),
            "Content button press feedback must not use a render transform on Android.");
        Assert.That(normalRule.Groups["body"].Value,
            Does.Contain("transition-property: background-color, border-color;"));
        Assert.That(pressedRule.Groups["body"].Value, Does.Contain("border-color:"),
            "Manual pressed-state color feedback should replace native Button pseudo state.");
    }

    [Test]
    public void OfflineCatalogLocalization_HasEnglishAndChineseEntries()
    {
        StringTable english = AssetDatabase.LoadAssetAtPath<StringTable>(
            "Assets/Resources/Data/Localization/Card_UI_en.asset");
        StringTable chinese = AssetDatabase.LoadAssetAtPath<StringTable>(
            "Assets/Resources/Data/Localization/Card_UI_zh.asset");

        foreach (string key in new[] { "content.catalog.cached", "content.catalog.cache_warning" })
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
