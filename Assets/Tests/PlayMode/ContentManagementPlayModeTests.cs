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
                await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (FailNext)
                    {
                        FailNext = false;
                        throw new InvalidOperationException("fixture transfer failed");
                    }

                    bytes = Math.Max(offset, package.DownloadBytes / 2);
                    persistedBytesProgress?.Report(bytes);
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
                Assert.That(document.rootVisualElement.Q<VisualElement>("package-" + SuccessId), Is.Not.Null);
                Assert.That(document.rootVisualElement.Q<VisualElement>("package-" + RetryId), Is.Not.Null);
                Label catalogStatus = document.rootVisualElement.Q<Label>("catalog-status");
                Assert.That(catalogStatus.text, Does.StartWith("Offline"));
                Assert.That(catalogStatus.ClassListContains("is-warning"), Is.True);

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

                Assert.That(controller.StartOrRetryPackage(SuccessId), Is.True);
                deadline = Time.realtimeSinceStartup + 5f;
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
                Button remove = document.rootVisualElement
                    .Q<VisualElement>("package-" + SuccessId)
                    .Q<Button>("remove-button");
                Assert.That(remove, Is.Not.Null);
                Assert.That(remove.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(controller.RequestRemovePackage(SuccessId), Is.True);
                yield return null;
                Assert.That(remove.text, Is.EqualTo("Confirm remove"));
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
                    originalLanguage = ApplicationServices.Languages.UiLanguageId;
                    ApplicationServices.Languages.SelectUiLanguage("zh");
                    Label title = document.rootVisualElement.Q<Label>("content-title");
                    deadline = Time.realtimeSinceStartup + 5f;
                    while (title.text != "内容库" && Time.realtimeSinceStartup < deadline)
                        yield return null;
                    Assert.That(title.text, Is.EqualTo("内容库"));
                    Assert.That(catalogStatus.text, Does.StartWith("离线模式"));
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
