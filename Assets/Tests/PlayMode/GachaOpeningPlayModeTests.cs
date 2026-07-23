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
    public class GachaOpeningPlayModeTests
    {
        [UnityTest]
        public IEnumerator GachaScene_OpensAndRevealsASimulatedPack()
        {
            Type controllerType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("GachaViewController"))
                .First(type => type != null);
            var store = new MemoryProgressStore();
            PropertyInfo storeOverride = controllerType.GetProperty(
                "InventoryStoreOverride",
                BindingFlags.Static | BindingFlags.Public);
            storeOverride.SetValue(null, store);
            var cues = new List<FeedbackCue>();
            UIFeedbackService.FeedbackPlayed += cues.Add;
            try
            {
                AsyncOperation load = SceneManager.LoadSceneAsync("003_GachaScene", LoadSceneMode.Single);
                yield return load;
                yield return null;

                MonoBehaviour controller = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
                        FindObjectsInactive.Include,
                        FindObjectsSortMode.None)
                    .Single(component => component.GetType() == controllerType);
                float deadline = Time.realtimeSinceStartup + 6f;
                while (!(bool)GetProperty(controller, "IsReady") && Time.realtimeSinceStartup < deadline)
                    yield return null;

                Assert.That((bool)GetProperty(controller, "IsReady"), Is.True,
                    GetProperty(controller, "InitializationError") as string);
                Assert.That((int)GetProperty(controller, "ProductCount"), Is.EqualTo(5));
                Assert.That(GetProperty(controller, "SelectedRuleTrust").ToString(), Is.EqualTo("Simulated"));

                UIDocument document = controller.GetComponent<UIDocument>();
                ListView productList = document.rootVisualElement.Q<ListView>("product-list");
                Assert.That(productList.virtualizationMethod, Is.EqualTo(CollectionVirtualizationMethod.FixedHeight));

                Assert.That(InvokeBool(controller, "PrepareSelectedProduct"), Is.True);
                Assert.That(document.rootVisualElement.Q<VisualElement>("pack-stage").resolvedStyle.display,
                    Is.EqualTo(DisplayStyle.Flex));
                Assert.That(InvokeBool(controller, "TearPack"), Is.True);

                deadline = Time.realtimeSinceStartup + 3f;
                VisualElement revealStage = document.rootVisualElement.Q<VisualElement>("reveal-stage");
                while (revealStage.resolvedStyle.display != DisplayStyle.Flex && Time.realtimeSinceStartup < deadline)
                    yield return null;

                Assert.That((int)GetProperty(controller, "LastOpenedCardCount"), Is.EqualTo(5));
                Assert.That(store.ProductsOpened, Is.EqualTo(1));
                Assert.That(store.TotalCards, Is.EqualTo(5));
                Assert.That(cues, Does.Contain(FeedbackCue.PackOpen));

                for (int index = 0; index < 5; index++)
                {
                    Assert.That(InvokeBool(controller, "RevealNextCard"), Is.True);
                    yield return new WaitForSecondsRealtime(0.28f);
                }

                deadline = Time.realtimeSinceStartup + 4f;
                while ((int)GetProperty(controller, "CachedTextureCount") == 0 && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That((int)GetProperty(controller, "RevealedCount"), Is.EqualTo(5));
                Assert.That((int)GetProperty(controller, "CachedTextureCount"), Is.GreaterThan(0));
                Assert.That(cues.Count(cue => cue == FeedbackCue.CardFlip), Is.EqualTo(5));

                Assert.That(InvokeBool(controller, "RevealNextCard"), Is.True);
                yield return null;
                Assert.That((bool)GetProperty(controller, "IsSummaryVisible"), Is.True);
                Assert.That(cues, Does.Contain(FeedbackCue.CollectionNew));
            }
            finally
            {
                UIFeedbackService.FeedbackPlayed -= cues.Add;
                storeOverride.SetValue(null, null);
            }
        }

        private static object GetProperty(object target, string name)
        {
            return target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(target);
        }

        private static bool InvokeBool(object target, string name)
        {
            return (bool)target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public)?.Invoke(target, null);
        }

        private sealed class MemoryProgressStore : IInventoryProgressStore
        {
            private readonly Dictionary<string, int> cards = new Dictionary<string, int>();
            public int ProductsOpened { get; private set; }
            public int TotalCards => cards.Values.Sum();

            public int GetProductsOpened(string productId)
            {
                return ProductsOpened;
            }

            public ProductInventoryCommit Commit(ProductDrawResult result)
            {
                var awards = new List<InventoryAward>();
                foreach (DrawnPrinting printing in result.Printings)
                {
                    int previous = cards.TryGetValue(printing.PrintingId, out int count) ? count : 0;
                    cards[printing.PrintingId] = previous + 1;
                    awards.Add(new InventoryAward(printing.PrintingId, previous, previous + 1));
                }
                ProductsOpened++;
                return new ProductInventoryCommit(result.ProductId, ProductsOpened, awards.AsReadOnly());
            }
        }
    }
}
