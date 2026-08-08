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
    public sealed class PlayerErrorRecoveryPlayModeTests
    {
        private const string Sentinel = "/storage/private/Content https://credential.example sentinel-stack";

        private sealed class FailOnceCatalogProvider : ICatalogProvider
        {
            public int Calls { get; private set; }

            public CatalogLoadResult Load()
            {
                Calls++;
                return Calls == 1
                    ? CatalogLoadResult.Failure(Sentinel)
                    : ApplicationServices.Catalog.EnsureLoaded();
            }
        }

        private sealed class FailOncePackageCatalogProvider : IContentPackageCatalogProvider
        {
            public int Calls { get; private set; }

            public Task<ContentPackageCatalogLoadResult> LoadAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Calls++;
                return Calls == 1
                    ? Task.FromResult(ContentPackageCatalogLoadResult.Failure(Sentinel))
                    : Task.FromResult(ContentPackageCatalogLoadResult.Success(
                        new ContentPackageCatalog(
                            ContentPackageCatalog.SupportedSchemaVersion,
                            1,
                            Array.Empty<ContentPackageCatalogEntry>())));
            }
        }

        private sealed class SuccessThenFailurePackageCatalogProvider : IContentPackageCatalogProvider
        {
            public int Calls { get; private set; }

            public Task<ContentPackageCatalogLoadResult> LoadAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Calls++;
                return Task.FromResult(Calls == 1
                    ? ContentPackageCatalogLoadResult.Success(new ContentPackageCatalog(
                        ContentPackageCatalog.SupportedSchemaVersion,
                        1,
                        Array.Empty<ContentPackageCatalogEntry>()))
                    : ContentPackageCatalogLoadResult.Failure(Sentinel));
            }
        }

        private sealed class EmptyInventoryStore : IInventoryProgressStore
        {
            private static readonly IReadOnlyDictionary<string, int> EmptyCounts =
                new Dictionary<string, int>();

            public int GetProductsOpened(string productId) => 0;
            public ProductInventoryBatchCommit CommitBatch(ProductOpeningBatchCommitRequest request) =>
                throw new InvalidOperationException("This recovery test does not open products.");
            public IReadOnlyList<ProductOpeningHistoryEntry> GetOpeningHistory(int maximumCount) =>
                Array.Empty<ProductOpeningHistoryEntry>();
            public ProductOpeningStatistics GetOpeningStatistics() =>
                new ProductOpeningStatistics(EmptyCounts, EmptyCounts, EmptyCounts);
        }

        private sealed class EmptyCollectionProgressStore : ICollectionProgressStore
        {
            public CollectionItemProgress GetProgress(string printingId) =>
                new CollectionItemProgress(printingId, 0, false);
            public bool MarkSeen(string printingId) => false;
        }

        [UnityTest]
        public IEnumerator GachaFailure_KeepsLocalizedNavigationAndRetriesInPlace()
        {
            var provider = new FailOnceCatalogProvider();
            Type controllerType = FindRuntimeType("GachaViewController");
            SetStaticProperty(controllerType, "CatalogProviderOverride", provider);
            SetStaticProperty(controllerType, "InventoryStoreOverride", new EmptyInventoryStore());
            string originalLanguage = null;
            try
            {
                yield return SceneManager.LoadSceneAsync("003_GachaScene", LoadSceneMode.Single);
                yield return null;
                Component controller = UnityEngine.Object.FindFirstObjectByType(controllerType) as Component;
                Assert.That(controller, Is.Not.Null);
                originalLanguage = ApplicationServices.Languages.UiLanguageId;
                UIDocument document = controller.GetComponent<UIDocument>();
                VisualElement root = document.rootVisualElement;
                VisualElement error = root.Q<VisualElement>("gacha-error-panel");

                Assert.That(GetProperty<bool>(controller, "IsReady"), Is.False);
                Assert.That(error.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
                AssertFailureShell(root, "gacha-error-retry", "gacha-error-manage", "gacha-error-home");
                ApplicationServices.Languages.SelectUiLanguage("en");
                yield return null;
                string englishTitle = root.Q<Label>("gacha-error-title").text;
                ApplicationServices.Languages.SelectUiLanguage("zh");
                yield return null;
                Assert.That(root.Q<Label>("gacha-error-title").text, Is.Not.EqualTo(englishTitle));

                Assert.That(InvokeBool(controller, "RetryInitialization"), Is.True,
                    GetProperty<string>(controller, "InitializationError"));
                float hideDeadline = Time.realtimeSinceStartup + 1f;
                while (error.resolvedStyle.display != DisplayStyle.None &&
                       Time.realtimeSinceStartup < hideDeadline)
                    yield return null;
                Assert.That(GetProperty<bool>(controller, "IsReady"), Is.True);
                Assert.That(error.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
                Assert.That(provider.Calls, Is.EqualTo(2));
            }
            finally
            {
                SetStaticProperty(controllerType, "CatalogProviderOverride", null);
                if (originalLanguage != null && ApplicationServices.IsConfigured)
                    ApplicationServices.Languages.SelectUiLanguage(originalLanguage);
                SetStaticProperty(controllerType, "InventoryStoreOverride", null);
            }
        }

        [UnityTest]
        public IEnumerator CollectionFailure_KeepsLocalizedNavigationAndRetriesInPlace()
        {
            var provider = new FailOnceCatalogProvider();
            Type controllerType = FindRuntimeType("CollectionViewController");
            SetStaticProperty(controllerType, "CatalogProviderOverride", provider);
            SetStaticProperty(
                controllerType,
                "CollectionProgressStoreOverride",
                new EmptyCollectionProgressStore());
            string originalLanguage = null;
            try
            {
                yield return SceneManager.LoadSceneAsync("004_CollectionScene", LoadSceneMode.Single);
                yield return null;
                Component controller = UnityEngine.Object.FindFirstObjectByType(controllerType) as Component;
                Assert.That(controller, Is.Not.Null);
                originalLanguage = ApplicationServices.Languages.UiLanguageId;
                UIDocument document = controller.GetComponent<UIDocument>();
                VisualElement root = document.rootVisualElement;
                VisualElement error = root.Q<VisualElement>("collection-error-panel");

                Assert.That(GetProperty<bool>(controller, "IsReady"), Is.False);
                Assert.That(error.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
                AssertFailureShell(
                    root,
                    "collection-error-retry",
                    "collection-error-manage",
                    "collection-error-home");
                ApplicationServices.Languages.SelectUiLanguage("en");
                yield return null;
                string englishTitle = root.Q<Label>("collection-error-title").text;
                ApplicationServices.Languages.SelectUiLanguage("ja");
                yield return null;
                Assert.That(root.Q<Label>("collection-error-title").text, Is.Not.EqualTo(englishTitle));

                Assert.That(InvokeBool(controller, "RetryInitialization"), Is.True,
                    GetProperty<string>(controller, "InitializationError"));
                float hideDeadline = Time.realtimeSinceStartup + 1f;
                while (error.resolvedStyle.display != DisplayStyle.None &&
                       Time.realtimeSinceStartup < hideDeadline)
                    yield return null;
                Assert.That(GetProperty<bool>(controller, "IsReady"), Is.True);
                Assert.That(error.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
                Assert.That(provider.Calls, Is.EqualTo(2));
            }
            finally
            {
                SetStaticProperty(controllerType, "CatalogProviderOverride", null);
                if (originalLanguage != null && ApplicationServices.IsConfigured)
                    ApplicationServices.Languages.SelectUiLanguage(originalLanguage);
                SetStaticProperty(controllerType, "CollectionProgressStoreOverride", null);
            }
        }


        [UnityTest]
        public IEnumerator ContentFailure_KeepsLocalizedNavigationAndRetriesInPlace()
        {
            var provider = new FailOncePackageCatalogProvider();
            ContentManagementController.CatalogProviderOverride = provider;
            string originalLanguage = null;
            try
            {
                yield return SceneManager.LoadSceneAsync("006_ContentScene", LoadSceneMode.Single);
                ContentManagementController controller =
                    UnityEngine.Object.FindFirstObjectByType<ContentManagementController>();
                Assert.That(controller, Is.Not.Null);
                originalLanguage = ApplicationServices.Languages.UiLanguageId;
                float deadline = Time.realtimeSinceStartup + 5f;
                while (string.IsNullOrWhiteSpace(controller.InitializationError) &&
                       Time.realtimeSinceStartup < deadline)
                    yield return null;

                VisualElement root = controller.GetComponent<UIDocument>().rootVisualElement;
                VisualElement error = root.Q<VisualElement>("content-error-panel");
                Assert.That(controller.IsReady, Is.False);
                Assert.That(error.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
                AssertFailureShell(root, "content-error-retry", "content-error-home");
                ApplicationServices.Languages.SelectUiLanguage("en");
                yield return null;
                string englishTitle = root.Q<Label>("content-error-title").text;
                ApplicationServices.Languages.SelectUiLanguage("zh");
                yield return null;
                Assert.That(root.Q<Label>("content-error-title").text, Is.Not.EqualTo(englishTitle));

                Assert.That(controller.RetryCatalog(), Is.True);
                deadline = Time.realtimeSinceStartup + 5f;
                while (!controller.IsReady && Time.realtimeSinceStartup < deadline)
                    yield return null;
                yield return new WaitForSecondsRealtime(0.15f);
                Assert.That(controller.IsReady, Is.True, controller.InitializationError);
                Assert.That(error.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
                Assert.That(provider.Calls, Is.EqualTo(2));
            }
            finally
            {
                ContentManagementController.CatalogProviderOverride = null;
                if (originalLanguage != null && ApplicationServices.IsConfigured)
                    ApplicationServices.Languages.SelectUiLanguage(originalLanguage);
            }
        }

        [UnityTest]
        public IEnumerator ContentRefreshFailure_FilterCannotReviveTheStaleCatalog()
        {
            var provider = new SuccessThenFailurePackageCatalogProvider();
            ContentManagementController.CatalogProviderOverride = provider;
            try
            {
                yield return SceneManager.LoadSceneAsync("006_ContentScene", LoadSceneMode.Single);
                ContentManagementController controller =
                    UnityEngine.Object.FindFirstObjectByType<ContentManagementController>();
                Assert.That(controller, Is.Not.Null);
                float deadline = Time.realtimeSinceStartup + 5f;
                while (!controller.IsReady && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That(controller.IsReady, Is.True, controller.InitializationError);

                Assert.That(controller.RetryCatalog(), Is.True);
                deadline = Time.realtimeSinceStartup + 5f;
                while (string.IsNullOrWhiteSpace(controller.InitializationError) &&
                       Time.realtimeSinceStartup < deadline)
                    yield return null;
                VisualElement root = controller.GetComponent<UIDocument>().rootVisualElement;
                TextField search = root.Q<TextField>("content-search");
                search.value = "stale-filter";
                yield return null;

                Assert.That(controller.IsReady, Is.False);
                Assert.That(root.Q<VisualElement>("content-error-panel").resolvedStyle.display,
                    Is.EqualTo(DisplayStyle.Flex));
                Assert.That(root.Q<ListView>("package-list").resolvedStyle.display,
                    Is.EqualTo(DisplayStyle.None));
                AssertFailureShell(root, "content-error-retry", "content-error-home");
                Assert.That(provider.Calls, Is.EqualTo(2));
            }
            finally
            {
                ContentManagementController.CatalogProviderOverride = null;
            }
        }

        private static void AssertFailureShell(
            VisualElement root,
            params string[] actionNames)
        {
            string playerCopy = string.Join("\n", root.Query<Label>().ToList().Select(label => label.text));
            Assert.That(playerCopy, Does.Not.Contain(Sentinel));
            Assert.That(playerCopy, Does.Not.Contain("/storage/"));
            Assert.That(playerCopy, Does.Not.Contain("credential.example"));
            foreach (string actionName in actionNames)
            {
                VisualElement action = root.Q<VisualElement>(actionName);
                Assert.That(action, Is.Not.Null, actionName);
                Assert.That(action.enabledInHierarchy, Is.True, actionName);
                string label = action is Button button ? button.text : action.Q<Label>()?.text;
                Assert.That(label, Is.Not.Empty, actionName);
            }
        }

        private static Type FindRuntimeType(string name)
        {
            Type type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(name, false))
                .FirstOrDefault(candidate => candidate != null);
            Assert.That(type, Is.Not.Null, name);
            return type;
        }

        private static void SetStaticProperty(Type type, string name, object value)
        {
            type.GetProperty(name)?.SetValue(null, value);
        }

        private static T GetProperty<T>(object target, string name)
        {
            return (T)target.GetType().GetProperty(name).GetValue(target);
        }

        private static bool InvokeBool(object target, string name)
        {
            return (bool)target.GetType().GetMethod(name).Invoke(target, null);
        }
    }
}
