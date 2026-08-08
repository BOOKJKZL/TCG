using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Gacha.Application;
using Gacha.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Gacha.Tests.PlayMode
{
    public class ContentManagementPlayModeTests
    {
        private const string SuccessId = "en.success";
        private const string RetryId = "zh.retry";
        private const string HashA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        private const string HashB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

        private sealed class CatalogProvider : IContentPackageCatalogProvider
        {
            private readonly ContentPackageCatalog catalog;
            private readonly bool cached;

            public CatalogProvider(ContentPackageCatalog catalog, bool cached = false)
            {
                this.catalog = catalog;
                this.cached = cached;
            }

            public async Task<ContentPackageCatalogLoadResult> LoadAsync(CancellationToken cancellationToken)
            {
                return await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return ContentPackageCatalogLoadResult.Success(
                        catalog,
                        cached ? "fixture network offline" : null,
                        cached);
                }, cancellationToken);
            }
        }

        private sealed class Lifecycle : IInstalledContentPackageRegistry, IContentPackageLifecycleService
        {
            private readonly Dictionary<string, InstalledContentPackage> installed =
                new Dictionary<string, InstalledContentPackage>(StringComparer.Ordinal);

            public InstalledContentPackage Find(string packageId)
            {
                installed.TryGetValue(packageId, out InstalledContentPackage package);
                return package;
            }

            public InstalledContentPackage FindInstalled(string packageId) => Find(packageId);

            public void Install(InstalledContentPackage package)
            {
                installed[package.PackageId] = package;
            }

            public Task<ContentPackageRemovalResult> RemoveAsync(
                string packageId,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!installed.TryGetValue(packageId, out InstalledContentPackage package))
                    return Task.FromResult(ContentPackageRemovalResult.NotInstalled());
                installed.Remove(packageId);
                return Task.FromResult(ContentPackageRemovalResult.Removed(package));
            }
        }

        private sealed class Storage : IContentStorageProbe
        {
            public long GetAvailableBytes() => 1024 * 1024;
        }

        private sealed class Network : IContentNetworkProbe
        {
            public ContentNetworkType Type = ContentNetworkType.WifiOrEthernet;
            public ContentNetworkType GetNetworkType() => Type;
        }

        private sealed class DownloadPreferenceStore : IContentDownloadPreferenceStore
        {
            public DownloadPreferenceStore(bool wifiOnly)
            {
                Current = new ContentDownloadPreferences(wifiOnly);
            }

            public ContentDownloadPreferences Current { get; private set; }
            public ContentDownloadPreferences Load() => Current;
            public void Save(ContentDownloadPreferences preferences) => Current = preferences;
        }

        private sealed class QueueStateStore : IContentPackageQueueStateStore
        {
            private readonly object gate = new object();
            private ContentPackageQueueResumeState state;

            public ContentPackageQueueResumeState Load()
            {
                lock (gate) return state;
            }

            public void Save(ContentPackageQueueResumeState value)
            {
                lock (gate) state = value;
            }

            public void Clear()
            {
                lock (gate) state = null;
            }
        }

        private sealed class Transfer : IContentPackageTransfer
        {
            private long bytes;
            public bool FailNext { get; set; }

            public long GetDownloadedBytes(ContentPackageDescriptor package) => bytes;

            public async Task DownloadAsync(
                ContentPackageDescriptor package,
                long offset,
                IProgress<long> persistedBytesProgress,
                CancellationToken cancellationToken)
            {
                await Task.Run(async () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (FailNext)
                    {
                        FailNext = false;
                        throw new InvalidOperationException("fixture transfer failed");
                    }

                    bytes = Math.Max(offset, package.DownloadBytes / 2);
                    persistedBytesProgress?.Report(bytes);
                    await Task.Delay(300, cancellationToken);
                    bytes = package.DownloadBytes;
                    persistedBytesProgress?.Report(bytes);
                }, cancellationToken);
            }

            public void DeletePartial(ContentPackageDescriptor package)
            {
                bytes = 0;
            }

            public string GetArchivePath(ContentPackageDescriptor package)
            {
                return bytes == package.DownloadBytes ? "fixture-package.zip" : null;
            }
        }

        private sealed class Installer : IContentPackageInstaller
        {
            private readonly Lifecycle lifecycle;

            public Installer(Lifecycle lifecycle)
            {
                this.lifecycle = lifecycle;
            }

            public Task<ContentPackageInstallResult> InstallAsync(
                ContentInstallPlan plan,
                string archivePath,
                CancellationToken cancellationToken = default)
            {
                var installed = new InstalledContentPackage(
                    plan.Package.PackageId,
                    plan.Package.InstallRelativePath,
                    plan.Package.Revision,
                    plan.Package.Version,
                    plan.Package.InstalledBytes,
                    plan.Package.Sha256);
                lifecycle.Install(installed);
                return Task.FromResult(ContentPackageInstallResult.Success(installed));
            }
        }

        private sealed class OperationFactory : IContentPackageInstallCoordinatorFactory
        {
            private readonly Dictionary<string, Transfer> transfers =
                new Dictionary<string, Transfer>(StringComparer.Ordinal);
            private readonly Lifecycle lifecycle;

            public OperationFactory(Lifecycle lifecycle, bool failRetry = true)
            {
                this.lifecycle = lifecycle;
                transfers[SuccessId] = new Transfer();
                transfers[RetryId] = new Transfer { FailNext = failRetry };
            }

            public int CreateCalls { get; private set; }

            public ContentPackageInstallCoordinator Create(ContentPackageCatalog catalog, string packageId)
            {
                CreateCalls++;
                ContentPackageCatalogEntry entry = catalog.Find(packageId) ??
                    throw new InvalidOperationException("Fixture package was not found.");
                if (!transfers.TryGetValue(packageId, out Transfer transfer))
                {
                    transfer = new Transfer();
                    transfers.Add(packageId, transfer);
                }
                return new ContentPackageInstallCoordinator(
                    entry.Package,
                    new ContentPackagePlanner(lifecycle, new Storage(), 0),
                    transfer,
                    new Installer(lifecycle));
            }
        }

        private sealed class HapticSink : IHapticFeedbackSink
        {
            public int Pulses { get; private set; }
            public void Pulse() => Pulses++;
        }

        [UnityTest]
        public IEnumerator ContentScene_LoadsLocalizedPackagesAndSurvivesFailureRetry()
        {
            int originalScreenWidth = Screen.width;
            int originalScreenHeight = Screen.height;
            int mainThreadId = Environment.CurrentManagedThreadId;
            var lifecycle = new Lifecycle();
            var factory = new OperationFactory(lifecycle);
            ContentManagementController.CatalogProviderOverride = new CatalogProvider(CreateCatalog(), true);
            ContentManagementController.OperationFactoryOverride = factory;
            ContentManagementController.LifecycleOverride = lifecycle;
            ContentManagementController.DownloadPolicyOverride = Policy();
            ContentManagementController.QueueStateStoreOverride = new QueueStateStore();
            var cues = new List<FeedbackCue>();
            var haptic = new HapticSink();
            UIFeedbackService.FeedbackPlayed += cues.Add;
            UIFeedbackService.RegisterHapticSink(haptic);

            ExperienceSettings originalExperience = null;
            string originalLanguage = null;
            try
            {
                Screen.SetResolution(720, 1600, false);
                yield return null;
                if (ApplicationServices.Languages != null)
                {
                    originalLanguage = ApplicationServices.Languages.UiLanguageId;
                    ApplicationServices.Languages.SelectUiLanguage("en");
                    yield return null;
                }
                LogAssert.Expect(LogType.Warning, "Content package catalog warning: fixture network offline");
                AsyncOperation load = SceneManager.LoadSceneAsync("006_ContentScene", LoadSceneMode.Single);
                yield return load;
                yield return null;

                ContentManagementController controller = UnityEngine.Object.FindFirstObjectByType<ContentManagementController>();
                Assert.That(controller, Is.Not.Null);
                float deadline = Time.realtimeSinceStartup + 5f;
                while (!controller.IsReady && Time.realtimeSinceStartup < deadline)
                    yield return null;
                yield return null;

                Assert.That(controller.IsReady, Is.True, controller.InitializationError);
                Assert.That(controller.PackageCount, Is.EqualTo(2));
                Assert.That(controller.LastAppliedThreadId, Is.EqualTo(mainThreadId));
                Assert.That(controller.GetPackageState(SuccessId), Is.EqualTo(ContentPackageOperationState.Idle));

                UIDocument document = controller.GetComponent<UIDocument>();
                Assert.That(document, Is.Not.Null);
                Assert.That(document.visualTreeAsset, Is.Not.Null);
                VisualElement safeAreaRoot = document.rootVisualElement.Q<VisualElement>("content-management");
                Assert.That(safeAreaRoot.ClassListContains("safe-area-bound"), Is.True);
                float initialSafePaddingLeft = safeAreaRoot.resolvedStyle.paddingLeft;
                float initialSafePaddingTop = safeAreaRoot.resolvedStyle.paddingTop;
                float initialSafePaddingRight = safeAreaRoot.resolvedStyle.paddingRight;
                float initialSafePaddingBottom = safeAreaRoot.resolvedStyle.paddingBottom;
                VisualElement contentShell = document.rootVisualElement.Q<VisualElement>("content-shell");
                AssertNoInlineVisibilityTransform(contentShell);
                Assert.That(contentShell.resolvedStyle.opacity,
                    Is.EqualTo(1f).Within(0.01f),
                    "Entrance motion must never make the content controls unreadable.");
                yield return new WaitForSecondsRealtime(0.4f);
                Assert.That(contentShell.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(contentShell.resolvedStyle.visibility, Is.EqualTo(Visibility.Visible));
                Assert.That(contentShell.layout.width, Is.GreaterThan(0f));
                Assert.That(contentShell.layout.height, Is.GreaterThan(0f));
                Assert.That(contentShell.resolvedStyle.opacity,
                    Is.EqualTo(1f).Within(0.01f),
                    "The attached content shell must repaint visible without a resize or app resume.");
                Assert.That(safeAreaRoot.resolvedStyle.paddingLeft, Is.EqualTo(initialSafePaddingLeft).Within(0.01f));
                Assert.That(safeAreaRoot.resolvedStyle.paddingTop, Is.EqualTo(initialSafePaddingTop).Within(0.01f));
                Assert.That(safeAreaRoot.resolvedStyle.paddingRight, Is.EqualTo(initialSafePaddingRight).Within(0.01f));
                Assert.That(safeAreaRoot.resolvedStyle.paddingBottom, Is.EqualTo(initialSafePaddingBottom).Within(0.01f),
                    "Repeated Safe Area refreshes must not accumulate padding.");
                AssertNoInlineVisibilityTransform(contentShell);
                Assert.That(document.rootVisualElement.Q<VisualElement>("package-" + SuccessId), Is.Not.Null);
                Assert.That(document.rootVisualElement.Q<VisualElement>("package-" + RetryId), Is.Not.Null);
                document.rootVisualElement.Q<ListView>("package-list").ScrollToItem(0);
                yield return null;
                Label catalogStatus = document.rootVisualElement.Q<Label>("catalog-status");
                Assert.That(catalogStatus.text, Does.StartWith("Offline"));
                Assert.That(catalogStatus.ClassListContains("is-warning"), Is.True);
                Assert.That(document.rootVisualElement.Query<Button>().ToList(), Is.Empty,
                    "The content page must not enter UI Toolkit's unstable native Button render path on Android.");
                AssertStableActionControl(document.rootVisualElement.Q<VisualElement>("refresh-button"));
                AssertStableActionControl(document.rootVisualElement.Q<VisualElement>("back-button"));
                VisualElement successRow = document.rootVisualElement.Q<VisualElement>("package-" + SuccessId);
                VisualElement download = successRow.Q<VisualElement>("download-button");
                VisualElement pause = successRow.Q<VisualElement>("pause-button");
                VisualElement remove = successRow.Q<VisualElement>("remove-button");
                VisualElement cancel = successRow.Q<VisualElement>("cancel-button");
                VisualElement actions = download.parent;
                AssertPersistentActionBar(actions, download, pause, remove, cancel);
                Assert.That(download.enabledSelf, Is.True);
                Assert.That(pause.enabledSelf, Is.True);
                Assert.That(remove.enabledSelf, Is.True);
                Assert.That(cancel.enabledSelf, Is.True);
                ScrollView packageScroll = document.rootVisualElement.Q<ListView>("package-list").Q<ScrollView>();
                Assert.That(packageScroll, Is.Not.Null);
                packageScroll.ScrollTo(pause);
                yield return null;
                successRow = document.rootVisualElement.Q<VisualElement>("package-" + SuccessId);
                download = successRow.Q<VisualElement>("download-button");
                pause = successRow.Q<VisualElement>("pause-button");
                remove = successRow.Q<VisualElement>("remove-button");
                cancel = successRow.Q<VisualElement>("cancel-button");
                actions = download.parent;
                int initialCueCount = cues.Count;
                Assert.That(pause.panel, Is.Not.Null, "The compact dynamic row action must remain attached before input.");
                VisualElement pickedPause = pause.panel.Pick(pause.worldBound.center);
                Assert.That(pickedPause == pause || pause.Contains(pickedPause), Is.True,
                    $"The compact pause action is covered by {pickedPause?.name ?? "nothing"}.");
                SendPointerDown(pause);
                Assert.That(pause.ClassListContains("is-pressed"), Is.False,
                    "The Android background render node must remain immutable while pressed.");
                Assert.That(ActionLabel(pause).ClassListContains("is-pressed"), Is.True,
                    "Manual pointer feedback should be isolated to the label without native :active state.");
                SendPointerUp(pause);
                Assert.That(ActionLabel(pause).ClassListContains("is-pressed"), Is.False);
                yield return null;
                Assert.That(controller.CancelPackage(SuccessId), Is.False);
                Assert.That(controller.RequestRemovePackage(SuccessId), Is.False);
                Assert.That(controller.GetPackageState(SuccessId), Is.EqualTo(ContentPackageOperationState.Idle));
                Assert.That(cues.Count, Is.EqualTo(initialCueCount), "Unavailable actions must have no feedback side effects.");

                originalExperience = ApplicationServices.ExperienceSettings?.Current;
                if (ApplicationServices.ExperienceSettings != null)
                {
                    ApplicationServices.ExperienceSettings.SetReduceMotion(true);
                    yield return null;
                    Assert.That(document.rootVisualElement.Q<VisualElement>("content-management")
                        .ClassListContains("reduce-motion"), Is.True);
                    Assert.That(document.rootVisualElement.Q<VisualElement>("content-shell").resolvedStyle.opacity,
                        Is.EqualTo(1f).Within(0.01f));
                }
                yield return new WaitForSecondsRealtime(0.35f);
                AssertPersistentActionBar(actions, download, pause, remove, cancel);
                Rect[] persistentGeometry = CaptureActionGeometry(actions, download, pause, remove, cancel);

                SendPointerDown(download);
                Assert.That(download.ClassListContains("is-pressed"), Is.False);
                Assert.That(ActionLabel(download).ClassListContains("is-pressed"), Is.True);
                SendPointerUp(download);
                Assert.That(ActionLabel(download).ClassListContains("is-pressed"), Is.False);
                deadline = Time.realtimeSinceStartup + 5f;
                while (controller.GetPackageState(SuccessId) != ContentPackageOperationState.Downloading &&
                       controller.GetPackageState(SuccessId) != ContentPackageOperationState.Succeeded &&
                       Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That(controller.GetPackageState(SuccessId), Is.EqualTo(ContentPackageOperationState.Downloading));
                Assert.That(cancel.enabledSelf, Is.True);
                Assert.That(download.enabledSelf, Is.True);
                Assert.That(remove.enabledSelf, Is.True);
                AssertPersistentActionBar(actions, download, pause, remove, cancel,
                    expectedGeometry: persistentGeometry);
                Assert.That(controller.StartOrRetryPackage(SuccessId), Is.False);
                Assert.That(controller.RequestRemovePackage(SuccessId), Is.False);
                Assert.That(controller.PausePackage(SuccessId), Is.True);
                while (controller.GetPackageState(SuccessId) != ContentPackageOperationState.Paused &&
                       Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That(controller.GetPackageState(SuccessId), Is.EqualTo(ContentPackageOperationState.Paused));
                Assert.That(download.enabledSelf, Is.True);
                Assert.That(pause.enabledSelf, Is.True);
                Assert.That(cancel.enabledSelf, Is.True);
                AssertPersistentActionBar(actions, download, pause, remove, cancel,
                    expectedGeometry: persistentGeometry);
                Assert.That(controller.StartOrRetryPackage(SuccessId), Is.True);
                while (controller.GetPackageState(SuccessId) != ContentPackageOperationState.Succeeded &&
                       Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That(controller.GetPackageState(SuccessId), Is.EqualTo(ContentPackageOperationState.Succeeded));
                Assert.That(cues, Does.Contain(FeedbackCue.DownloadStart));
                Assert.That(cues, Does.Contain(FeedbackCue.DownloadComplete));
                Assert.That(haptic.Pulses, Is.EqualTo(1));

                LogAssert.Expect(
                    LogType.Warning,
                    "Content package operation failed for 'zh.retry' at Download: fixture transfer failed");
                Assert.That(controller.StartOrRetryPackage(RetryId), Is.True);
                deadline = Time.realtimeSinceStartup + 5f;
                while (controller.GetPackageState(RetryId) != ContentPackageOperationState.Failed &&
                       Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That(controller.GetPackageState(RetryId), Is.EqualTo(ContentPackageOperationState.Failed));
                Assert.That(cues.Count(cue => cue == FeedbackCue.Error), Is.EqualTo(1));

                Assert.That(controller.StartOrRetryPackage(RetryId), Is.True);
                deadline = Time.realtimeSinceStartup + 5f;
                while (controller.GetPackageState(RetryId) != ContentPackageOperationState.Succeeded &&
                       Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That(controller.GetPackageState(RetryId), Is.EqualTo(ContentPackageOperationState.Succeeded));
                Assert.That(cues.Count(cue => cue == FeedbackCue.Error), Is.EqualTo(1));
                Assert.That(controller.LastAppliedThreadId, Is.EqualTo(mainThreadId));

                Assert.That(controller.IsPackageInstalled(SuccessId), Is.True);
                Assert.That(remove, Is.Not.Null);
                Assert.That(ActionText(remove), Is.EqualTo("Remove"));
                Assert.That(download.enabledSelf, Is.True);
                Assert.That(pause.enabledSelf, Is.True);
                Assert.That(remove.enabledSelf, Is.True);
                Assert.That(cancel.enabledSelf, Is.True);
                Assert.That(actions.ClassListContains("content-package-row__actions"), Is.True);
                AssertPersistentActionBar(actions, download, pause, remove, cancel,
                    expectedGeometry: persistentGeometry);
                Assert.That(controller.StartOrRetryPackage(SuccessId), Is.False);
                Assert.That(controller.PausePackage(SuccessId), Is.False);
                Assert.That(controller.CancelPackage(SuccessId), Is.False);
                Assert.That(controller.RequestRemovePackage(SuccessId), Is.True);
                yield return null;
                Assert.That(ActionText(remove), Is.EqualTo("Remove"));
                Assert.That(remove.enabledSelf, Is.True);
                Assert.That(cancel.enabledSelf, Is.True);
                AssertPersistentActionBar(actions, download, pause, remove, cancel,
                    expectedGeometry: persistentGeometry);
                Assert.That(controller.CancelPackage(SuccessId), Is.True);
                yield return null;
                Assert.That(remove.enabledSelf, Is.True);
                Assert.That(cancel.enabledSelf, Is.True);
                AssertPersistentActionBar(actions, download, pause, remove, cancel,
                    expectedGeometry: persistentGeometry);
                Assert.That(controller.RequestRemovePackage(SuccessId), Is.True);
                yield return null;
                Assert.That(controller.RequestRemovePackage(SuccessId), Is.True);
                deadline = Time.realtimeSinceStartup + 5f;
                while ((controller.IsPackageInstalled(SuccessId) ||
                        controller.GetPackageState(SuccessId) != ContentPackageOperationState.Idle) &&
                       Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That(controller.IsPackageInstalled(SuccessId), Is.False);
                Assert.That(controller.GetPackageState(SuccessId), Is.EqualTo(ContentPackageOperationState.Idle));
                Assert.That(cues.Count(cue => cue == FeedbackCue.Confirm), Is.GreaterThanOrEqualTo(2));

                Assert.That(controller.StartOrRetryPackage(SuccessId), Is.True);
                deadline = Time.realtimeSinceStartup + 5f;
                while (controller.GetPackageState(SuccessId) != ContentPackageOperationState.Succeeded &&
                       Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That(controller.IsPackageInstalled(SuccessId), Is.True);
                Assert.That(haptic.Pulses, Is.GreaterThanOrEqualTo(4));

                if (ApplicationServices.Languages != null)
                {
                    ApplicationServices.Languages.SelectUiLanguage("zh");
                    Label title = document.rootVisualElement.Q<Label>("content-title");
                    deadline = Time.realtimeSinceStartup + 5f;
                    while (title.text != "内容库" && Time.realtimeSinceStartup < deadline)
                        yield return null;
                    Assert.That(title.text, Is.EqualTo("内容库"));
                    Assert.That(catalogStatus.text, Does.StartWith("离线模式"));
                    yield return null;
                    successRow = document.rootVisualElement.Q<VisualElement>("package-" + SuccessId);
                    download = successRow.Q<VisualElement>("download-button");
                    pause = successRow.Q<VisualElement>("pause-button");
                    remove = successRow.Q<VisualElement>("remove-button");
                    cancel = successRow.Q<VisualElement>("cancel-button");
                    actions = download.parent;
                    Assert.That(ActionText(download), Is.EqualTo("下载"));
                    AssertPersistentActionBar(actions, download, pause, remove, cancel, false);
                }

                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                Screen.SetResolution(originalScreenWidth, originalScreenHeight, false);
                if (originalLanguage != null && ApplicationServices.Languages != null)
                    ApplicationServices.Languages.SelectUiLanguage(originalLanguage);
                if (originalExperience != null && ApplicationServices.ExperienceSettings != null)
                {
                    ApplicationServices.ExperienceSettings.SetSoundEnabled(originalExperience.SoundEnabled);
                    ApplicationServices.ExperienceSettings.SetReduceMotion(originalExperience.ReduceMotion);
                    ApplicationServices.ExperienceSettings.SetHapticsEnabled(originalExperience.HapticsEnabled);
                    ApplicationServices.ExperienceSettings.SetAnimationSpeed(originalExperience.AnimationSpeed);
                }
                UIFeedbackService.FeedbackPlayed -= cues.Add;
                UIFeedbackService.RegisterHapticSink(null);
                ContentManagementController.CatalogProviderOverride = null;
                ContentManagementController.OperationFactoryOverride = null;
                ContentManagementController.LifecycleOverride = null;
                ContentManagementController.DispatcherOverride = null;
                ContentManagementController.DownloadPolicyOverride = null;
                ContentManagementController.QueueStateStoreOverride = null;
            }
        }

        [UnityTest]
        public IEnumerator ContentScene_BatchSelectionDownloadsAllPackagesAndUpdatesCapacity()
        {
            var lifecycle = new Lifecycle();
            var factory = new OperationFactory(lifecycle, false);
            var network = new Network();
            var policy = Policy(network, true, 100);
            var queueState = new QueueStateStore();
            ContentManagementController.CatalogProviderOverride = new CatalogProvider(CreateCatalog());
            ContentManagementController.OperationFactoryOverride = factory;
            ContentManagementController.LifecycleOverride = lifecycle;
            ContentManagementController.DownloadPolicyOverride = policy;
            ContentManagementController.QueueStateStoreOverride = queueState;
            string originalLanguage = null;
            try
            {
                if (ApplicationServices.Languages != null)
                {
                    originalLanguage = ApplicationServices.Languages.UiLanguageId;
                    ApplicationServices.Languages.SelectUiLanguage("en");
                    yield return null;
                }
                AsyncOperation load = SceneManager.LoadSceneAsync("006_ContentScene", LoadSceneMode.Single);
                yield return load;
                yield return null;

                ContentManagementController controller =
                    UnityEngine.Object.FindFirstObjectByType<ContentManagementController>();
                Assert.That(controller, Is.Not.Null);
                float deadline = Time.realtimeSinceStartup + 5f;
                while (!controller.IsReady && Time.realtimeSinceStartup < deadline)
                    yield return null;

                Assert.That(controller.IsReady, Is.True, controller.InitializationError);
                controller.SelectAllFilteredPackages();
                Assert.That(controller.SelectedPackageCount, Is.EqualTo(2));
                Assert.That(controller.SelectionSummary.DownloadBytes, Is.EqualTo(200));
                Assert.That(controller.SelectionSummary.InstalledBytes, Is.EqualTo(400));
                Assert.That(controller.DownloadPreflight.Status,
                    Is.EqualTo(ContentDownloadPreflightStatus.Ready));
                Assert.That(controller.StartSelectedPackages(), Is.True);
                while ((controller.QueueSnapshot == null || controller.QueueSnapshot.RunningCount == 0) &&
                       Time.realtimeSinceStartup < deadline)
                    yield return null;
                network.Type = ContentNetworkType.MobileData;
                Assert.That(controller.RefreshDownloadNetworkState(), Is.True,
                    "A Wi-Fi batch must pause before continuing on unconfirmed mobile data.");
                while ((controller.QueueSnapshot == null || !controller.QueueSnapshot.Paused) &&
                       Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That(controller.QueueSnapshot.Paused, Is.True);
                AsyncOperation reload = SceneManager.LoadSceneAsync("006_ContentScene", LoadSceneMode.Single);
                yield return reload;
                yield return null;
                controller = UnityEngine.Object.FindFirstObjectByType<ContentManagementController>();
                Assert.That(controller, Is.Not.Null);
                deadline = Time.realtimeSinceStartup + 5f;
                while (!controller.IsReady && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That(controller.IsReady, Is.True, controller.InitializationError);
                Assert.That(controller.SelectedPackageCount, Is.EqualTo(2));
                Assert.That(controller.QueueSnapshot.Paused, Is.True,
                    "A restarted content scene must restore the batch paused for player review.");
                Assert.That(controller.DownloadPreflight.Status,
                    Is.EqualTo(ContentDownloadPreflightStatus.WaitingForWifi));
                policy.SetWifiOnlyForLargeDownloads(false);
                Assert.That(controller.DownloadPreflight.Status,
                    Is.EqualTo(ContentDownloadPreflightStatus.CellularConfirmationRequired));
                Assert.That(controller.ResumeInstallQueue(), Is.False,
                    "The first mobile action must arm an explicit size confirmation.");
                VisualElement mobileDownloadAction = controller.GetComponent<UIDocument>()
                    .rootVisualElement.Q<VisualElement>("download-selected-button");
                Assert.That(mobileDownloadAction, Is.Not.Null);
                Assert.That(ActionText(mobileDownloadAction), Is.EqualTo("Confirm mobile download"));
                Assert.That(controller.ResumeInstallQueue(), Is.True);
                while ((controller.QueueSnapshot == null || !controller.QueueSnapshot.IsComplete) &&
                       Time.realtimeSinceStartup < deadline)
                    yield return null;
                yield return null;

                Assert.That(controller.QueueSnapshot, Is.Not.Null);
                Assert.That(controller.QueueSnapshot.IsComplete, Is.True);
                Assert.That(controller.QueueSnapshot.SucceededCount, Is.EqualTo(2));
                Assert.That(controller.QueueSnapshot.FailedCount, Is.Zero);
                Assert.That(controller.IsPackageInstalled(SuccessId), Is.True);
                Assert.That(controller.IsPackageInstalled(RetryId), Is.True);
                Assert.That(controller.SelectionSummary.DownloadBytes, Is.Zero);
                Assert.That(controller.SelectionSummary.InstalledBytes, Is.Zero);

                VisualElement root = controller.GetComponent<UIDocument>().rootVisualElement;
                Assert.That(root.Q<Label>("content-selection-summary").text, Does.Contain("2 selected"));
                foreach (string name in new[]
                         {
                             "select-filtered-button", "clear-selection-button", "download-selected-button",
                             "queue-pause-button", "queue-resume-button", "queue-retry-button",
                             "queue-cancel-button"
                         })
                    AssertStableActionControl(root.Q<VisualElement>(name));
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                if (originalLanguage != null && ApplicationServices.Languages != null)
                    ApplicationServices.Languages.SelectUiLanguage(originalLanguage);
                ContentManagementController.CatalogProviderOverride = null;
                ContentManagementController.OperationFactoryOverride = null;
                ContentManagementController.LifecycleOverride = null;
                ContentManagementController.DispatcherOverride = null;
                ContentManagementController.DownloadPolicyOverride = null;
                ContentManagementController.QueueStateStoreOverride = null;
            }
        }

        [UnityTest]
        public IEnumerator ContentScene_VirtualizesTwoThousandPackagesAndCreatesOnlyVisibleOperations()
        {
            const int packageCount = 2000;
            var lifecycle = new Lifecycle();
            var factory = new OperationFactory(lifecycle);
            ContentManagementController.CatalogProviderOverride = new CatalogProvider(CreateLargeCatalog(packageCount));
            ContentManagementController.OperationFactoryOverride = factory;
            ContentManagementController.LifecycleOverride = lifecycle;
            ContentManagementController.DownloadPolicyOverride = Policy();
            ContentManagementController.QueueStateStoreOverride = new QueueStateStore();
            try
            {
                AsyncOperation load = SceneManager.LoadSceneAsync("006_ContentScene", LoadSceneMode.Single);
                yield return load;
                yield return null;

                ContentManagementController controller = UnityEngine.Object.FindFirstObjectByType<ContentManagementController>();
                Assert.That(controller, Is.Not.Null);
                float deadline = Time.realtimeSinceStartup + 5f;
                while (!controller.IsReady && Time.realtimeSinceStartup < deadline)
                    yield return null;
                yield return null;

                Assert.That(controller.IsReady, Is.True, controller.InitializationError);
                Assert.That(controller.PackageCount, Is.EqualTo(packageCount));
                Assert.That(controller.FilteredPackageCount, Is.EqualTo(packageCount));
                Assert.That(controller.VisibleRowCount, Is.InRange(1, 24));
                Assert.That(factory.CreateCalls, Is.InRange(1, 24));
                controller.SelectAllFilteredPackages();
                Assert.That(controller.SelectedPackageCount, Is.EqualTo(packageCount));
                Assert.That(controller.SelectionSummary.SelectedCount, Is.EqualTo(packageCount));
                Assert.That(controller.SelectionSummary.DependencyCount, Is.Zero);
                Assert.That(controller.SelectionSummary.DownloadBytes, Is.EqualTo(packageCount * 100L));
                Assert.That(controller.SelectionSummary.InstalledBytes, Is.EqualTo(packageCount * 200L));
                Assert.That(factory.CreateCalls, Is.InRange(1, 24),
                    "Selecting 2,000 packages must not create offscreen install coordinators.");
                UIDocument document = controller.GetComponent<UIDocument>();
                Assert.That(document.rootVisualElement.Q<ListView>("package-list"), Is.Not.Null);
                Assert.That(document.rootVisualElement.Query<VisualElement>(className: "content-package-row").ToList(),
                    Has.Count.InRange(1, 24));
                controller.ClearPackageSelection();
                Assert.That(controller.SelectedPackageCount, Is.Zero);
                Assert.That(controller.SelectionSummary, Is.Null);
                TextField search = document.rootVisualElement.Q<TextField>("content-search");
                search.value = "large-1999";
                yield return null;
                Assert.That(controller.FilteredPackageCount, Is.EqualTo(1));
                Assert.That(document.rootVisualElement.Query<VisualElement>(className: "content-package-row").ToList(),
                    Has.Count.EqualTo(1));
            }
            finally
            {
                ContentManagementController.CatalogProviderOverride = null;
                ContentManagementController.OperationFactoryOverride = null;
                ContentManagementController.LifecycleOverride = null;
                ContentManagementController.DispatcherOverride = null;
                ContentManagementController.DownloadPolicyOverride = null;
                ContentManagementController.QueueStateStoreOverride = null;
            }
        }

        private static void AssertPersistentActionBar(
            VisualElement actions,
            VisualElement download,
            VisualElement pause,
            VisualElement remove,
            VisualElement cancel,
            bool assertEnglishLabels = true,
            Rect[] expectedGeometry = null)
        {
            Assert.That(actions.childCount, Is.EqualTo(4),
                "The Android action bar must keep exactly four permanent render nodes.");
            VisualElement[] controls = { download, pause, remove, cancel };
            foreach (VisualElement control in controls)
            {
                AssertStableActionControl(control);
            }
            if (assertEnglishLabels)
            {
                Assert.That(ActionText(download), Is.EqualTo("Download"));
                Assert.That(ActionText(pause), Is.EqualTo("Pause"));
                Assert.That(ActionText(remove), Is.EqualTo("Remove"));
                Assert.That(ActionText(cancel), Is.EqualTo("Cancel"));
            }
            bool compact = HasAncestorClass(actions, "mobile-layout--compact");
            AssertStableActionGeometry(actions, cancel, compact);
            AssertStableActionGeometry(actions, remove, compact);
            AssertStableActionGeometry(actions, pause, compact);
            AssertStableActionGeometry(actions, download, compact);
            if (compact)
            {
                AssertControlsDoNotOverlap(controls);
            }
            else
            {
                Assert.That(cancel.worldBound.xMax, Is.LessThanOrEqualTo(remove.worldBound.xMin + 2f));
                Assert.That(remove.worldBound.xMax, Is.LessThanOrEqualTo(pause.worldBound.xMin + 2f));
                Assert.That(pause.worldBound.xMax, Is.LessThanOrEqualTo(download.worldBound.xMin + 2f));
            }

            if (expectedGeometry != null)
            {
                Rect[] actual = CaptureActionGeometry(actions, download, pause, remove, cancel);
                Assert.That(actual.Length, Is.EqualTo(expectedGeometry.Length));
                for (int index = 0; index < actual.Length; index++)
                {
                    Assert.That(actual[index].x, Is.EqualTo(expectedGeometry[index].x).Within(2f), controls[index].name);
                    Assert.That(actual[index].y, Is.EqualTo(expectedGeometry[index].y).Within(2f), controls[index].name);
                    Assert.That(actual[index].width, Is.EqualTo(expectedGeometry[index].width).Within(2f), controls[index].name);
                    Assert.That(actual[index].height, Is.EqualTo(expectedGeometry[index].height).Within(2f), controls[index].name);
                }
            }
        }

        private static void AssertStableActionControl(VisualElement control)
        {
            Assert.That(control, Is.Not.Null);
            Assert.That(control, Is.Not.TypeOf<Button>(),
                control?.name + " must be a plain VisualElement, not a native Button.");
            Assert.That(control.enabledSelf, Is.True,
                control.name + " must never enter the Android disabled-state render path.");
            Assert.That(control.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex), control.name);
            Assert.That(control.resolvedStyle.visibility, Is.EqualTo(Visibility.Visible), control.name);
            Assert.That(control.resolvedStyle.opacity, Is.EqualTo(1f).Within(0.01f), control.name);
            Assert.That(control.pickingMode, Is.EqualTo(PickingMode.Position), control.name);
            Label label = control.Q<Label>();
            Assert.That(label, Is.Not.Null, control.name);
            Assert.That(label.pickingMode, Is.EqualTo(PickingMode.Ignore), control.name);
        }

        private static string ActionText(VisualElement control)
        {
            return ActionLabel(control)?.text;
        }

        private static Label ActionLabel(VisualElement control)
        {
            return control?.Q<Label>();
        }

        private static void SendPointerDown(VisualElement control)
        {
            using (PointerDownEvent evt = PointerDownEvent.GetPooled(new Event
                   {
                       type = EventType.MouseDown,
                       button = 0,
                       mousePosition = control.worldBound.center
                   }))
            {
                control.SendEvent(evt);
            }
        }

        private static void SendPointerUp(VisualElement control)
        {
            using (PointerUpEvent evt = PointerUpEvent.GetPooled(new Event
                   {
                       type = EventType.MouseUp,
                       button = 0,
                       mousePosition = control.worldBound.center
                   }))
            {
                control.SendEvent(evt);
            }
        }

        private static void AssertNoInlineVisibilityTransform(VisualElement shell)
        {
            Assert.That(
                shell.style.opacity.keyword,
                Is.EqualTo(StyleKeyword.Null).Or.EqualTo(StyleKeyword.Undefined),
                "Page entrance feedback must not write opacity on the Android shell.");
            Assert.That(
                shell.style.scale.keyword,
                Is.EqualTo(StyleKeyword.Null).Or.EqualTo(StyleKeyword.Undefined),
                "Page entrance feedback must not write a render transform on the Android shell.");
        }

        private static Rect[] CaptureActionGeometry(
            VisualElement actions,
            VisualElement download,
            VisualElement pause,
            VisualElement remove,
            VisualElement cancel)
        {
            VisualElement[] controls = { download, pause, remove, cancel };
            return controls.Select(control => new Rect(
                control.worldBound.xMin - actions.worldBound.xMin,
                control.worldBound.yMin - actions.worldBound.yMin,
                control.worldBound.width,
                control.worldBound.height)).ToArray();
        }

        private static void AssertStableActionGeometry(
            VisualElement actions,
            VisualElement control,
            bool compact)
        {
            Assert.That(control.resolvedStyle.position,
                Is.EqualTo(compact ? Position.Relative : Position.Absolute));
            if (!compact)
                Assert.That(control.resolvedStyle.top, Is.EqualTo(0f).Within(2f));
            Assert.That(control.worldBound.width, Is.GreaterThanOrEqualTo(80f));
            Assert.That(control.worldBound.height, Is.GreaterThanOrEqualTo(48f));
            Assert.That(control.worldBound.xMin, Is.GreaterThanOrEqualTo(actions.worldBound.xMin - 2f));
            Assert.That(
                control.worldBound.xMax,
                Is.LessThanOrEqualTo(actions.worldBound.xMax + 2f),
                $"{control.name}: actions={actions.worldBound} layout={control.layout} " +
                $"world={control.worldBound} local={control.localBound} resolvedWidth={control.resolvedStyle.width} " +
                $"padding={control.resolvedStyle.paddingLeft}/{control.resolvedStyle.paddingRight}");
            if (compact)
                Assert.That(control.worldBound.yMin, Is.GreaterThanOrEqualTo(actions.worldBound.yMin - 2f));
            else
                Assert.That(control.worldBound.yMin, Is.EqualTo(actions.worldBound.yMin).Within(2f));
            Assert.That(control.worldBound.yMax, Is.LessThanOrEqualTo(actions.worldBound.yMax + 2f));
        }

        private static bool HasAncestorClass(VisualElement element, string className)
        {
            for (VisualElement current = element; current != null; current = current.parent)
            {
                if (current.ClassListContains(className))
                    return true;
            }
            return false;
        }

        private static void AssertControlsDoNotOverlap(IReadOnlyList<VisualElement> controls)
        {
            for (int left = 0; left < controls.Count; left++)
            {
                for (int right = left + 1; right < controls.Count; right++)
                {
                    Rect overlap = Intersect(controls[left].worldBound, controls[right].worldBound);
                    Assert.That(overlap.width * overlap.height, Is.LessThanOrEqualTo(2f),
                        $"{controls[left].name} overlaps {controls[right].name} in compact layout.");
                }
            }
        }

        private static Rect Intersect(Rect left, Rect right)
        {
            float xMin = Mathf.Max(left.xMin, right.xMin);
            float yMin = Mathf.Max(left.yMin, right.yMin);
            float xMax = Mathf.Min(left.xMax, right.xMax);
            float yMax = Mathf.Min(left.yMax, right.yMax);
            return Rect.MinMaxRect(xMin, yMin, Mathf.Max(xMin, xMax), Mathf.Max(yMin, yMax));
        }

        private static ContentPackageCatalog CreateCatalog()
        {
            ContentPackageDescriptor first = Package(SuccessId, "en/success", HashA);
            ContentPackageDescriptor second = Package(RetryId, "zh/retry", HashB);
            return new ContentPackageCatalog(
                ContentPackageCatalog.SupportedSchemaVersion,
                1,
                new[]
                {
                    new ContentPackageCatalogEntry(first, new Uri("https://fixture.example/" + HashA + ".zip")),
                    new ContentPackageCatalogEntry(second, new Uri("https://fixture.example/" + HashB + ".zip"))
                });
        }

        private static ContentDownloadPolicyService Policy(
            Network network = null,
            bool wifiOnly = true,
            long threshold = ContentDownloadPolicyService.DefaultLargeDownloadThresholdBytes) =>
            new ContentDownloadPolicyService(
                new Storage(),
                network ?? new Network(),
                new DownloadPreferenceStore(wifiOnly),
                threshold,
                0);

        private static ContentPackageCatalog CreateLargeCatalog(int count)
        {
            var entries = new List<ContentPackageCatalogEntry>(count);
            for (int index = 0; index < count; index++)
            {
                string id = $"en.large-{index:D3}";
                string hash = index.ToString("x64");
                ContentPackageDescriptor descriptor = Package(id, $"en/large-{index:D3}", hash);
                entries.Add(new ContentPackageCatalogEntry(
                    descriptor,
                    new Uri("https://fixture.example/" + hash + ".zip")));
            }
            return new ContentPackageCatalog(ContentPackageCatalog.SupportedSchemaVersion, 1, entries);
        }

        private static ContentPackageDescriptor Package(string id, string path, string hash)
        {
            return new ContentPackageDescriptor(id, path, 1, "1.0.0", 100, 200, hash);
        }
    }
}
