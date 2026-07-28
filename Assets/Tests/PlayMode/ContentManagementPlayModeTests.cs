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

            public OperationFactory(Lifecycle lifecycle)
            {
                this.lifecycle = lifecycle;
                transfers[SuccessId] = new Transfer();
                transfers[RetryId] = new Transfer { FailNext = true };
            }

            public ContentPackageInstallCoordinator Create(ContentPackageCatalog catalog, string packageId)
            {
                ContentPackageCatalogEntry entry = catalog.Find(packageId) ??
                    throw new InvalidOperationException("Fixture package was not found.");
                return new ContentPackageInstallCoordinator(
                    entry.Package,
                    new ContentPackagePlanner(lifecycle, new Storage(), 0),
                    transfers[packageId],
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
            int mainThreadId = Environment.CurrentManagedThreadId;
            var lifecycle = new Lifecycle();
            var factory = new OperationFactory(lifecycle);
            ContentManagementController.CatalogProviderOverride = new CatalogProvider(CreateCatalog(), true);
            ContentManagementController.OperationFactoryOverride = factory;
            ContentManagementController.LifecycleOverride = lifecycle;
            var cues = new List<FeedbackCue>();
            var haptic = new HapticSink();
            UIFeedbackService.FeedbackPlayed += cues.Add;
            UIFeedbackService.RegisterHapticSink(haptic);

            ExperienceSettings originalExperience = null;
            string originalLanguage = null;
            try
            {
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

                Assert.That(controller.IsReady, Is.True, controller.InitializationError);
                Assert.That(controller.PackageCount, Is.EqualTo(2));
                Assert.That(controller.LastAppliedThreadId, Is.EqualTo(mainThreadId));
                Assert.That(controller.GetPackageState(SuccessId), Is.EqualTo(ContentPackageOperationState.Idle));

                UIDocument document = controller.GetComponent<UIDocument>();
                Assert.That(document, Is.Not.Null);
                Assert.That(document.visualTreeAsset, Is.Not.Null);
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
                AssertNoInlineVisibilityTransform(contentShell);
                Assert.That(document.rootVisualElement.Q<VisualElement>("package-" + SuccessId), Is.Not.Null);
                Assert.That(document.rootVisualElement.Q<VisualElement>("package-" + RetryId), Is.Not.Null);
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
                int initialCueCount = cues.Count;
                SendPointerDown(pause);
                Assert.That(pause.ClassListContains("is-pressed"), Is.True,
                    "Manual pointer feedback should be visible without native :active state.");
                SendPointerUp(pause);
                Assert.That(pause.ClassListContains("is-pressed"), Is.False);
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
                Assert.That(download.ClassListContains("is-pressed"), Is.True);
                SendPointerUp(download);
                Assert.That(download.ClassListContains("is-pressed"), Is.False);
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
                    Assert.That(ActionText(download), Is.EqualTo("下载"));
                    AssertPersistentActionBar(actions, download, pause, remove, cancel, false, persistentGeometry);
                }

                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
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
            AssertStableActionGeometry(actions, cancel);
            AssertStableActionGeometry(actions, remove);
            AssertStableActionGeometry(actions, pause);
            AssertStableActionGeometry(actions, download);
            Assert.That(cancel.worldBound.xMax, Is.LessThanOrEqualTo(remove.worldBound.xMin + 2f));
            Assert.That(remove.worldBound.xMax, Is.LessThanOrEqualTo(pause.worldBound.xMin + 2f));
            Assert.That(pause.worldBound.xMax, Is.LessThanOrEqualTo(download.worldBound.xMin + 2f));

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
            return control?.Q<Label>()?.text;
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
            VisualElement control)
        {
            Assert.That(control.resolvedStyle.position, Is.EqualTo(Position.Absolute));
            Assert.That(control.resolvedStyle.top, Is.EqualTo(0f).Within(2f));
            Assert.That(control.worldBound.width, Is.InRange(80f, 170f));
            Assert.That(control.worldBound.xMin, Is.GreaterThanOrEqualTo(actions.worldBound.xMin - 2f));
            Assert.That(
                control.worldBound.xMax,
                Is.LessThanOrEqualTo(actions.worldBound.xMax + 2f),
                $"{control.name}: actions={actions.worldBound} layout={control.layout} " +
                $"world={control.worldBound} local={control.localBound} resolvedWidth={control.resolvedStyle.width} " +
                $"padding={control.resolvedStyle.paddingLeft}/{control.resolvedStyle.paddingRight}");
            Assert.That(control.worldBound.yMin, Is.EqualTo(actions.worldBound.yMin).Within(2f));
            Assert.That(control.worldBound.yMax, Is.LessThanOrEqualTo(actions.worldBound.yMax + 2f));
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

        private static ContentPackageDescriptor Package(string id, string path, string hash)
        {
            return new ContentPackageDescriptor(id, path, 1, "1.0.0", 100, 200, hash);
        }
    }
}
