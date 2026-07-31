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

namespace Gacha.Tests.PlayMode
{
    public class SceneTransitionSoakPlayModeTests
    {
        [UnityTest]
        [Category("Performance")]
        [Timeout(45000)]
        public IEnumerator CoreScenes_RepeatTransitionsWithoutRetainedControllersOrLargeManagedGrowth()
        {
            Type gachaControllerType = FindType("GachaViewController");
            Type collectionControllerType = FindType("CollectionViewController");
            PropertyInfo inventoryOverride = gachaControllerType.GetProperty(
                "InventoryStoreOverride",
                BindingFlags.Static | BindingFlags.Public);
            PropertyInfo collectionOverride = collectionControllerType.GetProperty(
                "CollectionProgressStoreOverride",
                BindingFlags.Static | BindingFlags.Public);
            inventoryOverride.SetValue(null, new MemoryInventoryStore());
            collectionOverride.SetValue(null, new MemoryCollectionStore());

            long warmedMemory = 0L;
            try
            {
                for (int cycle = 0; cycle < 3; cycle++)
                {
                    yield return LoadScene("003_GachaScene");
                    MonoBehaviour gacha = FindController(gachaControllerType);
                    yield return WaitUntilReady(gacha, 6f);
                    Assert.That((bool)GetProperty(gacha, "IsReady"), Is.True,
                        GetProperty(gacha, "InitializationError") as string);

                    yield return LoadScene("004_CollectionScene");
                    Assert.That(FindControllerCount(gachaControllerType), Is.Zero);
                    MonoBehaviour collection = FindController(collectionControllerType);
                    yield return WaitUntilReady(collection, 6f);
                    Assert.That((bool)GetProperty(collection, "IsReady"), Is.True,
                        GetProperty(collection, "InitializationError") as string);

                    yield return LoadScene("005_SettingScene");
                    Assert.That(FindControllerCount(gachaControllerType), Is.Zero);
                    Assert.That(FindControllerCount(collectionControllerType), Is.Zero);
                    Assert.That(UnityEngine.Object.FindObjectsByType<LanguageSettingsPanel>(
                        FindObjectsInactive.Include,
                        FindObjectsSortMode.None), Has.Length.EqualTo(1));
                    Assert.That(UnityEngine.Object.FindObjectsByType<ExperienceSettingsPanel>(
                        FindObjectsInactive.Include,
                        FindObjectsSortMode.None), Has.Length.EqualTo(1));
                    Assert.That(UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
                            FindObjectsInactive.Include,
                            FindObjectsSortMode.None)
                        .Count(component => component.GetType().Name == "SaveRecoverySettingsPanel"), Is.EqualTo(1));

                    yield return Resources.UnloadUnusedAssets();
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    if (cycle == 0)
                        warmedMemory = GC.GetTotalMemory(true);
                }

                long retainedGrowth = Math.Max(0L, GC.GetTotalMemory(true) - warmedMemory);
                TestContext.WriteLine(
                    $"SceneTransition cycles=3 retained={retainedGrowth / 1024f / 1024f:0.000}MiB");
                Assert.That(retainedGrowth, Is.LessThan(64L * 1024L * 1024L),
                    $"Managed memory retained {retainedGrowth / 1024f / 1024f:0.00} MiB after warmed scene cycles.");
            }
            finally
            {
                inventoryOverride.SetValue(null, null);
                collectionOverride.SetValue(null, null);
            }
        }

        private static IEnumerator LoadScene(string sceneName)
        {
            AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            yield return operation;
            yield return null;
        }

        private static IEnumerator WaitUntilReady(MonoBehaviour controller, float timeoutSeconds)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!(bool)GetProperty(controller, "IsReady") && Time.realtimeSinceStartup < deadline)
                yield return null;
        }

        private static Type FindType(string typeName)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(typeName))
                .First(type => type != null);
        }

        private static MonoBehaviour FindController(Type controllerType)
        {
            return UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None)
                .Single(component => component.GetType() == controllerType);
        }

        private static int FindControllerCount(Type controllerType)
        {
            return UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None)
                .Count(component => component.GetType() == controllerType);
        }

        private static object GetProperty(object target, string name)
        {
            return target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(target);
        }

        private sealed class MemoryInventoryStore : IInventoryProgressStore
        {
            private readonly Dictionary<string, int> cards = new Dictionary<string, int>();
            private readonly Dictionary<string, int> products = new Dictionary<string, int>();

            public int GetProductsOpened(string productId)
            {
                return products.TryGetValue(productId, out int value) ? value : 0;
            }

            public ProductInventoryBatchCommit CommitBatch(ProductOpeningBatchCommitRequest request)
            {
                var commits = new List<ProductInventoryCommit>(request.Draws.Count);
                foreach (ProductDrawResult result in request.Draws)
                {
                    var awards = new List<InventoryAward>(result.Printings.Count);
                    foreach (DrawnPrinting printing in result.Printings)
                    {
                        int previous = cards.TryGetValue(printing.PrintingId, out int count) ? count : 0;
                        cards[printing.PrintingId] = previous + 1;
                        awards.Add(new InventoryAward(printing.PrintingId, previous, previous + 1));
                    }

                    int opened = GetProductsOpened(result.ProductId) + 1;
                    products[result.ProductId] = opened;
                    commits.Add(new ProductInventoryCommit(result.ProductId, opened, awards.AsReadOnly()));
                }
                return new ProductInventoryBatchCommit(request.TransactionId, commits.AsReadOnly());
            }

            public IReadOnlyList<ProductOpeningHistoryEntry> GetOpeningHistory(int maximumCount) =>
                Array.Empty<ProductOpeningHistoryEntry>();

            public ProductOpeningStatistics GetOpeningStatistics() =>
                new ProductOpeningStatistics(null, null, null);
        }

        private sealed class MemoryCollectionStore : ICollectionProgressStore
        {
            public CollectionItemProgress GetProgress(string printingId)
            {
                return new CollectionItemProgress(printingId, 0, false);
            }

            public bool MarkSeen(string printingId)
            {
                return false;
            }
        }
    }
}
