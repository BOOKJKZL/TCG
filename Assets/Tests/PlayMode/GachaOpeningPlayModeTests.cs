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
            string originalUiLanguage = null;
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
                int productCount = (int)GetProperty(controller, "ProductCount");
                Assert.That(productCount, Is.GreaterThanOrEqualTo(5));

                UIDocument document = controller.GetComponent<UIDocument>();
                ListView productList = document.rootVisualElement.Q<ListView>("product-list");
                Assert.That(productList.itemsSource.Count, Is.EqualTo(productCount));
                Assert.That(productList.virtualizationMethod, Is.EqualTo(CollectionVirtualizationMethod.FixedHeight));
                int initialBaseIndex = productList.itemsSource.Cast<ProductDefinition>()
                    .Select((product, index) => new { product, index })
                    .Single(pair => pair.product.SetId.EndsWith(":base1", StringComparison.Ordinal))
                    .index;
                productList.SetSelection(initialBaseIndex);
                yield return null;

                Assert.That(GetProperty(controller, "SelectedRuleTrust").ToString(), Is.EqualTo("HistoricallyVerified"));
                Assert.That(GetProperty(controller, "SelectedRuleConfidence").ToString(), Is.EqualTo("Corroborated"));
                Assert.That((string)GetProperty(controller, "SelectedRuleRegionId"),
                    Is.EqualTo("pokemon-international-en"));
                Assert.That((int)GetProperty(controller, "SelectedRuleEvidenceCount"), Is.EqualTo(2));

                Assert.That((string)GetProperty(controller, "SelectedThemeId"),
                    Is.EqualTo("pokemon-base1-vintage"));
                Assert.That((string)GetProperty(controller, "SelectedThemePackAudioKey"),
                    Is.EqualTo("pack.open.vintage"));
                Assert.That((string)GetProperty(controller, "SelectedThemeArtworkResourcePath"),
                    Is.EqualTo("Gacha/Themes/vintage-pack"));
                Assert.That((bool)GetProperty(controller, "HasSelectedThemeArtwork"), Is.True);
                Assert.That(document.rootVisualElement.Q<VisualElement>("gacha-opening")
                    .ClassListContains("gacha-theme--vintage"), Is.True);
                originalUiLanguage = ApplicationServices.Languages.UiLanguageId;
                ApplicationServices.Languages.SelectUiLanguage("zh");
                yield return null;
                Assert.That(document.rootVisualElement.Q<Label>("gacha-title").text, Is.EqualTo("开启卡包"));
                Assert.That(document.rootVisualElement.Q<Button>("prepare-pack-button").text, Is.EqualTo("开 1 包"));
                Assert.That(document.rootVisualElement.Q<Button>("prepare-ten-button").text, Is.EqualTo("十连开包"));
                Assert.That(document.rootVisualElement.Q<Label>("rule-evidence-summary").text,
                    Does.Contain("已佐证").And.Contain("2026-07-23"));
                Assert.That(document.rootVisualElement.Q<VisualElement>("rule-source-list").childCount,
                    Is.EqualTo(2));
                Assert.That(document.rootVisualElement.Q<VisualElement>("rule-source-list")
                    .Children().OfType<Button>().First().text, Does.StartWith("来源 1："));
                ApplicationServices.Languages.SelectUiLanguage("en");
                yield return null;
                Assert.That(document.rootVisualElement.Q<Label>("gacha-title").text, Is.EqualTo("Open a Pack"));
                Assert.That(document.rootVisualElement.Q<Button>("prepare-pack-button").text, Is.EqualTo("Open 1 pack"));
                Assert.That(document.rootVisualElement.Q<Button>("prepare-ten-button").text, Is.EqualTo("Open 10 packs"));
                Assert.That(document.rootVisualElement.Q<Label>("rule-evidence-summary").text,
                    Does.Contain("Corroborated").And.Contain("2026-07-23"));
                Assert.That(document.rootVisualElement.Q<VisualElement>("rule-source-list")
                    .Children().OfType<Button>().First().text, Does.StartWith("Source 1:"));

                int exIndex = productList.itemsSource.Cast<ProductDefinition>()
                    .Select((product, index) => new { product, index })
                    .Single(pair => pair.product.SetId.EndsWith(":ex1", StringComparison.Ordinal))
                    .index;
                productList.SetSelection(exIndex);
                yield return null;
                Assert.That((string)GetProperty(controller, "SelectedRuleProfileId"),
                    Is.EqualTo("pokemon-ex1-psa-empirical-v1"));
                Assert.That((int)GetProperty(controller, "SelectedRuleEvidenceCount"), Is.EqualTo(1));
                Assert.That(document.rootVisualElement.Q<Label>("rule-notice").text,
                    Does.Contain("1 Reverse Holo"));
                Assert.That((string)GetProperty(controller, "SelectedThemeId"),
                    Is.EqualTo("pokemon-ex1-ruby"));
                Assert.That(document.rootVisualElement.Q<VisualElement>("gacha-opening")
                    .ClassListContains("gacha-theme--ruby"), Is.True);

                int sourcedIndex = productList.itemsSource.Cast<ProductDefinition>()
                    .Select((product, index) => new { product, index })
                    .Single(pair => pair.product.SetId.EndsWith(":swsh1", StringComparison.Ordinal))
                    .index;
                productList.SetSelection(sourcedIndex);
                yield return null;
                Assert.That(GetProperty(controller, "SelectedRuleTrust").ToString(),
                    Is.EqualTo("SourceInformedSimulation"));
                Assert.That(GetProperty(controller, "SelectedRuleConfidence").ToString(),
                    Is.EqualTo("Corroborated"));
                Assert.That(document.rootVisualElement.Q<Label>("rule-badge").text,
                    Is.EqualTo("SOURCED SIMULATION"));
                Assert.That(document.rootVisualElement.Q<Label>("rule-notice").text,
                    Does.Contain("5 Common").And.Contain("Basic Energy"));
                Assert.That(document.rootVisualElement.Q<Label>("rule-evidence-summary").text,
                    Does.Contain("Corroborated").And.Contain("2026-07-25"));
                Assert.That(document.rootVisualElement.Q<VisualElement>("rule-source-list").childCount,
                    Is.EqualTo(3));
                Assert.That((string)GetProperty(controller, "SelectedThemeId"),
                    Is.EqualTo("pokemon-swsh1-electric"));
                Assert.That((string)GetProperty(controller, "SelectedThemeArtworkResourcePath"),
                    Is.EqualTo("Gacha/Themes/electric-pack"));
                Assert.That((bool)GetProperty(controller, "HasSelectedThemeArtwork"), Is.True);
                Assert.That(document.rootVisualElement.Q<VisualElement>("gacha-opening")
                    .ClassListContains("gacha-theme--electric"), Is.True);

                int scarletVioletIndex = productList.itemsSource.Cast<ProductDefinition>()
                    .Select((product, index) => new { product, index })
                    .Single(pair => pair.product.SetId.EndsWith(":sv01", StringComparison.Ordinal))
                    .index;
                productList.SetSelection(scarletVioletIndex);
                yield return null;
                Assert.That((string)GetProperty(controller, "SelectedRuleProfileId"),
                    Is.EqualTo("pokemon-sv01-sourced-simulation-v1"));
                Assert.That(GetProperty(controller, "SelectedRuleTrust").ToString(),
                    Is.EqualTo("SourceInformedSimulation"));
                Assert.That(GetProperty(controller, "SelectedRuleConfidence").ToString(),
                    Is.EqualTo("Corroborated"));
                Assert.That(document.rootVisualElement.Q<Label>("rule-badge").text,
                    Is.EqualTo("SOURCED SIMULATION"));
                Assert.That(document.rootVisualElement.Q<Label>("rule-notice").text,
                    Does.Contain("4 Common").And.Contain("2 foil slots"));
                Assert.That(document.rootVisualElement.Q<Label>("rule-evidence-summary").text,
                    Does.Contain("Corroborated").And.Contain("2026-07-25"));
                Assert.That(document.rootVisualElement.Q<VisualElement>("rule-source-list").childCount,
                    Is.EqualTo(2));
                Assert.That((string)GetProperty(controller, "SelectedThemeId"),
                    Is.EqualTo("pokemon-sv01-gallery"));
                Assert.That(document.rootVisualElement.Q<VisualElement>("gacha-opening")
                    .ClassListContains("gacha-theme--gallery"), Is.True);

                int baseIndex = productList.itemsSource.Cast<ProductDefinition>()
                    .Select((product, index) => new { product, index })
                    .Single(pair => pair.product.SetId.EndsWith(":base1", StringComparison.Ordinal))
                    .index;
                productList.SetSelection(baseIndex);
                yield return null;
                Assert.That(GetProperty(controller, "SelectedRuleTrust").ToString(), Is.EqualTo("HistoricallyVerified"));
                Assert.That(document.rootVisualElement.Q<VisualElement>("rule-source-list").childCount,
                    Is.EqualTo(2));
                Assert.That((string)GetProperty(controller, "SelectedThemeId"),
                    Is.EqualTo("pokemon-base1-vintage"));
                Assert.That(document.rootVisualElement.Q<VisualElement>("gacha-opening")
                    .ClassListContains("gacha-theme--vintage"), Is.True);
                Assert.That(document.rootVisualElement.Q<VisualElement>("gacha-opening")
                    .ClassListContains("gacha-theme--gallery"), Is.False);

                Assert.That(InvokeBool(controller, "PrepareSelectedProduct"), Is.True);
                Assert.That(document.rootVisualElement.Q<VisualElement>("pack-stage").resolvedStyle.display,
                    Is.EqualTo(DisplayStyle.Flex));
                Assert.That(document.rootVisualElement.Q<VisualElement>("pack-theme-artwork")
                    .ClassListContains("is-visible"), Is.True);
                Assert.That((bool)GetProperty(controller, "ArePackParticlesRunning"), Is.True);
                Assert.That((int)GetProperty(controller, "PackParticleCount"), Is.EqualTo(6));
                Assert.That(document.rootVisualElement.Q<VisualElement>("pack-particle-layer").childCount,
                    Is.EqualTo(ThemeParticleField.MaximumParticleCount));
                Assert.That(InvokeBool(controller, "TearPack"), Is.True);

                deadline = Time.realtimeSinceStartup + 3f;
                VisualElement revealStage = document.rootVisualElement.Q<VisualElement>("reveal-stage");
                while (revealStage.resolvedStyle.display != DisplayStyle.Flex && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That((bool)GetProperty(controller, "ArePackParticlesRunning"), Is.False);

                int openedCardCount = (int)GetProperty(controller, "LastOpenedCardCount");
                Assert.That(openedCardCount, Is.EqualTo(11));
                ApplicationServices.Languages.SelectUiLanguage("zh");
                yield return null;
                Assert.That(document.rootVisualElement.Q<Label>("reveal-progress").text, Is.EqualTo("第 0 / 11 张"));
                Assert.That(document.rootVisualElement.Q<Button>("reveal-next-button").text, Is.EqualTo("翻开第一张"));
                ApplicationServices.Languages.SelectUiLanguage("en");
                yield return null;
                Assert.That(document.rootVisualElement.Q<Label>("reveal-progress").text, Is.EqualTo("0 of 11 cards"));
                Assert.That(document.rootVisualElement.Q<Button>("reveal-next-button").text, Is.EqualTo("Reveal first card"));
                Assert.That(store.ProductsOpened, Is.EqualTo(1));
                Assert.That(store.TotalCards, Is.EqualTo(openedCardCount));
                Assert.That(cues, Does.Contain(FeedbackCue.PackOpen));

                for (int index = 0; index < openedCardCount; index++)
                {
                    Assert.That(InvokeBool(controller, "RevealNextCard"), Is.True);
                    yield return new WaitForSecondsRealtime(0.28f);
                }

                deadline = Time.realtimeSinceStartup + 4f;
                while ((int)GetProperty(controller, "CachedTextureCount") == 0 && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That((int)GetProperty(controller, "RevealedCount"), Is.EqualTo(openedCardCount));
                Assert.That((int)GetProperty(controller, "CachedTextureCount"), Is.GreaterThan(0));
                long cachedBytes = (long)GetProperty(controller, "CachedTextureBytes");
                long budgetBytes = (long)GetProperty(controller, "CachedTextureBudgetBytes");
                Assert.That(cachedBytes, Is.GreaterThan(0L));
                Assert.That(cachedBytes, Is.LessThanOrEqualTo(budgetBytes));
                Assert.That((bool)GetProperty(controller, "IsCurrentRevealHighlighted"), Is.True);
                Assert.That((bool)GetProperty(controller, "AreRevealParticlesRunning"), Is.True);
                Assert.That((int)GetProperty(controller, "RevealParticleCount"), Is.EqualTo(10));
                Assert.That(document.rootVisualElement.Q<VisualElement>("reveal-aura")
                    .ClassListContains("is-highlighted"), Is.True);
                Assert.That(cues.Count(cue => cue == FeedbackCue.CardFlip), Is.EqualTo(openedCardCount));
                Assert.That(cues, Does.Contain(FeedbackCue.RareReveal));

                Assert.That(InvokeBool(controller, "RevealNextCard"), Is.True);
                yield return null;
                Assert.That((bool)GetProperty(controller, "IsSummaryVisible"), Is.True);
                Assert.That((bool)GetProperty(controller, "AreRevealParticlesRunning"), Is.False);
                Assert.That(cues, Does.Contain(FeedbackCue.CollectionNew));
                ApplicationServices.Languages.SelectUiLanguage("zh");
                yield return null;
                Assert.That(document.rootVisualElement.Q<Label>("summary-title").text, Is.EqualTo("开包完成"));
                Assert.That(document.rootVisualElement.Q<Label>("summary-metadata").text, Does.Contain("张卡牌"));
                ApplicationServices.Languages.SelectUiLanguage("en");
                yield return null;
                Assert.That(document.rootVisualElement.Q<Label>("summary-title").text, Is.EqualTo("Pack complete"));
                Assert.That(document.rootVisualElement.Q<Label>("summary-metadata").text, Does.Contain("cards"));

                int neoIndex = productList.itemsSource.Cast<ProductDefinition>()
                    .Select((product, index) => new { product, index })
                    .Single(pair => pair.product.SetId.EndsWith(":neo1", StringComparison.Ordinal))
                    .index;
                productList.SetSelection(neoIndex);
                yield return null;
                Assert.That((string)GetProperty(controller, "SelectedRuleProfileId"),
                    Is.EqualTo("pokemon-neo1-first-edition-psa-v1"));
                Assert.That((int)GetProperty(controller, "SelectedRuleEvidenceCount"), Is.EqualTo(1));
                Assert.That(document.rootVisualElement.Q<VisualElement>("rule-source-list").childCount,
                    Is.EqualTo(1));
                string ruleNotice = document.rootVisualElement.Q<Label>("rule-notice").text;
                Assert.That(ruleNotice.Contains("First Edition") || ruleNotice.Contains("第一版"), Is.True);
                Assert.That((string)GetProperty(controller, "SelectedThemeId"),
                    Is.EqualTo("pokemon-neo1-forest"));
                Assert.That(document.rootVisualElement.Q<VisualElement>("gacha-opening")
                    .ClassListContains("gacha-theme--forest"), Is.True);

                Assert.That(InvokeBool(controller, "PrepareSelectedProduct"), Is.True);
                Assert.That(InvokeBool(controller, "TearPack"), Is.True);
                deadline = Time.realtimeSinceStartup + 3f;
                while (revealStage.resolvedStyle.display != DisplayStyle.Flex && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That((int)GetProperty(controller, "LastOpenedCardCount"), Is.EqualTo(11));
                Assert.That(store.LastCommittedIds.Count, Is.EqualTo(11));
                Assert.That(store.LastCommittedIds.All(id => id.Contains("first-edition")), Is.True);

                Button revealAll = document.rootVisualElement.Q<Button>("reveal-all-button");
                Assert.That(revealAll, Is.Not.Null);
                Assert.That(revealAll.ClassListContains("gacha-button--quiet"), Is.True);
                Assert.That(InvokeBool(controller, "RevealAllCards"), Is.True);
                yield return null;
                Assert.That((int)GetProperty(controller, "RevealedCount"), Is.EqualTo(11));
                Assert.That((bool)GetProperty(controller, "IsSummaryVisible"), Is.True);
                Assert.That(InvokeBool(controller, "RevealAllCards"), Is.False);

                Assert.That(InvokeBool(controller, "PrepareTenProducts"), Is.True);
                Assert.That((int)GetProperty(controller, "PreparedProductCount"), Is.EqualTo(10));
                Assert.That(document.rootVisualElement.Q<Label>("pack-hint").text,
                    Does.Contain("first of 10 packs"));
                Assert.That(InvokeBool(controller, "TearPack"), Is.True);
                deadline = Time.realtimeSinceStartup + 3f;
                while (revealStage.resolvedStyle.display != DisplayStyle.Flex && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That((int)GetProperty(controller, "LastOpenedProductCount"), Is.EqualTo(10));
                Assert.That((int)GetProperty(controller, "LastOpenedCardCount"), Is.EqualTo(110));
                Assert.That(store.ProductsOpened, Is.EqualTo(12));
                Assert.That(InvokeBool(controller, "RevealAllCards"), Is.True);
                yield return null;
                Assert.That(document.rootVisualElement.Q<Label>("summary-title").text,
                    Is.EqualTo("Batch complete"));
                Assert.That(document.rootVisualElement.Q<Label>("summary-metadata").text,
                    Does.Contain("10 packs").And.Contain("110 cards"));
                Assert.That((int)GetProperty(controller, "RecentHistoryCount"), Is.EqualTo(3));
                Assert.That(document.rootVisualElement.Q<Label>("opening-statistics").text,
                    Does.Contain("12 packs").And.Contain("132 cards"));
                Assert.That(document.rootVisualElement.Q<ScrollView>("opening-history").contentContainer.childCount,
                    Is.EqualTo(3));
                Assert.That(cues.Count(cue => cue == FeedbackCue.PackOpen), Is.EqualTo(3));
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(originalUiLanguage) && ApplicationServices.IsConfigured)
                    ApplicationServices.Languages.SelectUiLanguage(originalUiLanguage);
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
            private readonly List<ProductOpeningHistoryEntry> history = new List<ProductOpeningHistoryEntry>();
            private readonly Dictionary<string, int> productsByLanguage = new Dictionary<string, int>();
            private readonly Dictionary<string, int> productsBySet = new Dictionary<string, int>();
            private readonly Dictionary<string, int> cardsByRarity = new Dictionary<string, int>();
            public int ProductsOpened { get; private set; }
            public int TotalCards => cards.Values.Sum();
            public IReadOnlyList<string> LastCommittedIds { get; private set; } = Array.Empty<string>();

            public int GetProductsOpened(string productId)
            {
                return ProductsOpened;
            }

            public ProductInventoryBatchCommit CommitBatch(ProductOpeningBatchCommitRequest request)
            {
                var commits = new List<ProductInventoryCommit>();
                LastCommittedIds = request.Draws.SelectMany(draw => draw.Printings)
                    .Select(printing => printing.PrintingId).ToArray();
                foreach (ProductDrawResult result in request.Draws)
                {
                    var awards = new List<InventoryAward>();
                    foreach (DrawnPrinting printing in result.Printings)
                    {
                        int previous = cards.TryGetValue(printing.PrintingId, out int count) ? count : 0;
                        cards[printing.PrintingId] = previous + 1;
                        awards.Add(new InventoryAward(printing.PrintingId, previous, previous + 1));
                    }
                    ProductsOpened++;
                    commits.Add(new ProductInventoryCommit(result.ProductId, ProductsOpened, awards.AsReadOnly()));
                }
                Add(productsByLanguage, request.LanguageId, request.Draws.Count);
                Add(productsBySet, request.SetId, request.Draws.Count);
                var rarityCounts = new Dictionary<string, int>();
                foreach (DrawnPrinting drawn in request.Draws.SelectMany(draw => draw.Printings))
                {
                    string rarityId = request.RarityByPrintingId[drawn.PrintingId];
                    Add(cardsByRarity, rarityId, 1);
                    Add(rarityCounts, rarityId, 1);
                }
                history.Add(new ProductOpeningHistoryEntry(
                    request.TransactionId,
                    request.OpenedAtUtc,
                    request.ProductId,
                    request.SetId,
                    request.LanguageId,
                    request.ProfileId,
                    request.Draws.Count,
                    request.Draws.Sum(draw => draw.Printings.Count),
                    commits.Sum(commit => commit.NewPrintingCount),
                    rarityCounts));
                return new ProductInventoryBatchCommit(request.TransactionId, commits.AsReadOnly());
            }

            public IReadOnlyList<ProductOpeningHistoryEntry> GetOpeningHistory(int maximumCount) => history
                .AsEnumerable()
                .Reverse()
                .Take(maximumCount)
                .ToList()
                .AsReadOnly();

            public ProductOpeningStatistics GetOpeningStatistics() =>
                new ProductOpeningStatistics(productsByLanguage, productsBySet, cardsByRarity);

            private static void Add(Dictionary<string, int> counts, string id, int amount)
            {
                counts[id] = counts.TryGetValue(id, out int current) ? current + amount : amount;
            }
        }
    }
}
