using System;
using System.Collections.Generic;
using System.Linq;
using Gacha.Application;
using Gacha.Domain;
using NUnit.Framework;
using UnityEngine;

public class ProductOpeningApplicationTests
{
    [Test]
    public void OddsAnalyzer_ReportsWeightedAveragePerCardSlot()
    {
        Fixture fixture = CreateFixture();
        var commonPool = new WeightedPool("common-pool", new[]
        {
            new WeightedPoolEntry(fixture.Common.Id, 3d),
            new WeightedPoolEntry(fixture.Rare.Id, 1d)
        });
        var rarePool = new WeightedPool("rare-pool", new[]
        {
            new WeightedPoolEntry(fixture.Rare.Id, 1d)
        });
        var rules = new ProductDrawRules(
            fixture.Product.Id,
            new[] { commonPool, rarePool },
            new[]
            {
                new SlotRule("main", commonPool.Id, 3),
                new SlotRule("rare", rarePool.Id, 1, 10)
            });

        ProductOddsSummary summary = ProductOddsAnalyzer.Analyze(fixture.Catalog, rules);

        Assert.That(summary.TotalDrawCount, Is.EqualTo(4));
        Assert.That(summary.Rarities.Single(item => item.RarityId == fixture.Common.RarityId).ExpectedCount,
            Is.EqualTo(2.25d).Within(0.0001d));
        Assert.That(summary.Rarities.Single(item => item.RarityId == fixture.Rare.RarityId).ExpectedCount,
            Is.EqualTo(1.75d).Within(0.0001d));
        Assert.That(summary.Rarities.Sum(item => item.AverageSlotProbability),
            Is.EqualTo(1d).Within(0.0001d));
    }

    [Test]
    public void OpeningService_UsesReplaceableProfileAndCommitsAwards()
    {
        Fixture fixture = CreateFixture();
        var inventory = new MemoryInventory();
        var service = new ProductOpeningService(
            fixture.Catalog,
            new UniformSimulationRuleProvider(3),
            inventory);

        ProductOpeningOutcome outcome = service.Open(fixture.Product.Id, new FixedRandom());

        Assert.That(outcome.Profile.Trust, Is.EqualTo(ProductRuleTrust.Simulated));
        Assert.That(outcome.Draw.Printings, Has.Count.EqualTo(3));
        Assert.That(outcome.Inventory.ProductsOpened, Is.EqualTo(1));
        Assert.That(outcome.Inventory.Awards, Has.Count.EqualTo(3));
        Assert.That(outcome.Inventory.NewPrintingCount, Is.EqualTo(1));
        Assert.That(inventory.Counts.Values.Sum(), Is.EqualTo(3));
    }

    [Test]
    public void UniformProfile_FiltersPrintingsByContentLanguage()
    {
        Fixture fixture = CreateFixture();

        ProductRuleProfile profile = new UniformSimulationRuleProvider(3, "en")
            .GetProfile(fixture.Catalog, fixture.Product.Id);

        Assert.That(profile.Confidence, Is.EqualTo(ProductRuleConfidence.Unverified));
        Assert.That(profile.RegionId, Is.EqualTo("unspecified"));
        Assert.That(profile.Evidence, Is.Empty);
        Assert.That(profile.LastCheckedOn, Is.Null);
        string[] printingIds = profile.Rules.Pools.Values
            .SelectMany(pool => pool.Entries)
            .Select(entry => entry.PrintingId)
            .ToArray();
        Assert.That(printingIds, Does.Not.Contain(fixture.Japanese.Id));
        Assert.That(printingIds.All(id =>
            fixture.Catalog.Printings[id].Identity.LanguageId == "en"), Is.True);
    }

    [Test]
    public void VerifiedProfile_RejectsMissingEvidenceAndConfidence()
    {
        Fixture fixture = CreateFixture();
        ProductRuleProfile simulation = new UniformSimulationRuleProvider(3, "en")
            .GetProfile(fixture.Catalog, fixture.Product.Id);

        Assert.Throws<ArgumentException>(() => new ProductRuleProfile(
            "invalid-verified-profile",
            simulation.Rules,
            ProductRuleTrust.HistoricallyVerified,
            ProductRuleConfidence.Unverified,
            "test-region",
            new Dictionary<string, string> { ["en"] = "Test region" },
            Array.Empty<ProductRuleEvidence>(),
            new Dictionary<string, string> { ["en"] = "Invalid" }));
    }

    [Test]
    public void RuleEvidence_RequiresDatedHttpsSource()
    {
        Assert.Throws<ArgumentException>(() =>
            new ProductRuleEvidence("Insecure", "http://example.test/rules", new DateTime(2026, 7, 25)));
        Assert.Throws<ArgumentException>(() =>
            new ProductRuleEvidence("Undated", "https://example.test/rules", default));

        var evidence = new ProductRuleEvidence(
            "Fixture source",
            "https://example.test/rules",
            new DateTime(2026, 7, 25, 14, 30, 0, DateTimeKind.Utc));
        Assert.That(evidence.CheckedOn, Is.EqualTo(new DateTime(2026, 7, 25)));
        Assert.That(evidence.SourceReference, Is.EqualTo("https://example.test/rules"));
    }

    [Test]
    public void SourceInformedProfile_DeduplicatesEvidenceUsingLatestCheck()
    {
        Fixture fixture = CreateFixture();
        ProductRuleProfile simulation = new UniformSimulationRuleProvider(3, "en")
            .GetProfile(fixture.Catalog, fixture.Product.Id);
        var profile = new ProductRuleProfile(
            "source-informed-fixture",
            simulation.Rules,
            ProductRuleTrust.SourceInformedSimulation,
            ProductRuleConfidence.Corroborated,
            "test-region",
            new Dictionary<string, string> { ["en"] = "Test region" },
            new[]
            {
                new ProductRuleEvidence("Older check", "https://example.test/rules", new DateTime(2026, 7, 20)),
                new ProductRuleEvidence("Latest check", "https://example.test/rules", new DateTime(2026, 7, 25))
            },
            new Dictionary<string, string> { ["en"] = "Source-informed simulation" });

        Assert.That(profile.IsSimulation, Is.True);
        Assert.That(profile.Evidence, Has.Count.EqualTo(1));
        Assert.That(profile.Evidence.Single().Title, Is.EqualTo("Latest check"));
        Assert.That(profile.LastCheckedOn, Is.EqualTo(new DateTime(2026, 7, 25)));
    }

    [Test]
    public void PlayerInventoryStore_RollsBackWhenLocalSaveFails()
    {
        var gameObject = new GameObject("Inventory Test");
        Inventory inventory = gameObject.AddComponent<Inventory>();
        try
        {
            Fixture fixture = CreateFixture();
            var service = new ProductOpeningService(
                fixture.Catalog,
                new UniformSimulationRuleProvider(2),
                new PlayerInventoryProgressStore(inventory, _ => throw new InvalidOperationException("disk full")),
                contentLanguageId: "en");

            Assert.Throws<InvalidOperationException>(() =>
                service.OpenBatch(fixture.Product.Id, 10, new FixedRandom()));
            Assert.That(inventory.Data.Cards, Is.Empty);
            Assert.That(inventory.Data.PacksOpened, Is.Empty);
            Assert.That(inventory.Data.UnseenPrintings, Is.Empty);
            Assert.That(inventory.Data.OpeningHistory, Is.Empty);
            Assert.That(inventory.Data.ProductsOpenedByLanguage, Is.Empty);
            Assert.That(inventory.Data.ProductsOpenedBySet, Is.Empty);
            Assert.That(inventory.Data.CardsDrawnByRarity, Is.Empty);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(gameObject);
        }
    }

    [Test]
    public void OpeningService_TenPackBatchSavesOnceAndRecordsHistoryAndStatistics()
    {
        var gameObject = new GameObject("Batch Inventory Test");
        Inventory inventory = gameObject.AddComponent<Inventory>();
        try
        {
            Fixture fixture = CreateFixture();
            int saveCalls = 0;
            DateTime openedAt = new DateTime(2026, 7, 31, 6, 45, 0, DateTimeKind.Utc);
            var store = new PlayerInventoryProgressStore(inventory, _ => saveCalls++);
            var service = new ProductOpeningService(
                fixture.Catalog,
                new UniformSimulationRuleProvider(3),
                store,
                contentLanguageId: "en",
                utcNow: () => openedAt);

            ProductOpeningBatchOutcome outcome = service.OpenBatch(
                fixture.Product.Id,
                10,
                new FixedRandom());

            Assert.That(saveCalls, Is.EqualTo(1));
            Assert.That(outcome.Draws, Has.Count.EqualTo(10));
            Assert.That(outcome.Inventory.ProductCount, Is.EqualTo(10));
            Assert.That(outcome.Inventory.CardCount, Is.EqualTo(30));
            Assert.That(outcome.Inventory.ProductsOpened, Is.EqualTo(10));
            Assert.That(inventory.Data.PacksOpened[fixture.Product.Id], Is.EqualTo(10));
            Assert.That(inventory.Data.Cards.Values.Sum(), Is.EqualTo(30));

            ProductOpeningHistoryEntry history = service.GetOpeningHistory(10).Single();
            Assert.That(history.TransactionId, Is.EqualTo(outcome.Inventory.TransactionId));
            Assert.That(history.OpenedAtUtc, Is.EqualTo(openedAt));
            Assert.That(history.ProductCount, Is.EqualTo(10));
            Assert.That(history.CardCount, Is.EqualTo(30));
            Assert.That(history.LanguageId, Is.EqualTo("en"));
            Assert.That(history.SetId, Is.EqualTo(fixture.Product.SetId));
            Assert.That(history.RarityCounts[fixture.Common.RarityId], Is.EqualTo(30));

            ProductOpeningStatistics statistics = service.GetOpeningStatistics();
            Assert.That(statistics.TotalProductsOpened, Is.EqualTo(10));
            Assert.That(statistics.TotalCardsDrawn, Is.EqualTo(30));
            Assert.That(statistics.ProductsByLanguage["en"], Is.EqualTo(10));
            Assert.That(statistics.ProductsBySet[fixture.Product.SetId], Is.EqualTo(10));
            Assert.That(statistics.CardsByRarity[fixture.Common.RarityId], Is.EqualTo(30));

            InventoryData restored = InventoryData.FromSnapshot(inventory.Data.ToSnapshot());
            Assert.That(restored.OpeningHistory, Has.Count.EqualTo(1));
            Assert.That(restored.ProductsOpenedByLanguage["en"], Is.EqualTo(10));
            Assert.That(restored.CardsDrawnByRarity[fixture.Common.RarityId], Is.EqualTo(30));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(gameObject);
        }
    }

    [Test]
    public void OpeningService_RejectsBatchLargerThanTenBeforeMutation()
    {
        Fixture fixture = CreateFixture();
        var inventory = new MemoryInventory();
        var service = new ProductOpeningService(
            fixture.Catalog,
            new UniformSimulationRuleProvider(3),
            inventory,
            contentLanguageId: "en");

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            service.OpenBatch(fixture.Product.Id, 11, new FixedRandom()));
        Assert.That(inventory.Counts, Is.Empty);
    }

    [Test]
    public void OpeningService_CachesValidatedProfileAcrossRepeatedPacks()
    {
        Fixture fixture = CreateFixture();
        var provider = new CountingRuleProvider(new UniformSimulationRuleProvider(3));
        var service = new ProductOpeningService(fixture.Catalog, provider, new MemoryInventory());

        for (int index = 0; index < 100; index++)
            service.Open(fixture.Product.Id, new FixedRandom());

        Assert.That(provider.CallCount, Is.EqualTo(1));
    }

    private static Fixture CreateFixture()
    {
        var names = new Dictionary<string, string> { ["en"] = "Name" };
        var language = new LanguageDefinition("en", names);
        var japaneseNames = new Dictionary<string, string> { ["ja"] = "Name JA" };
        var japaneseLanguage = new LanguageDefinition("ja", japaneseNames);
        var game = new GameDefinition("game", names, new[] { language.Id, japaneseLanguage.Id });
        var set = new SetDefinition("set", game.Id, names);
        var commonRarity = new RarityDefinition("common", game.Id, names, 0);
        var rareRarity = new RarityDefinition("rare", game.Id, names, 1);
        var variant = new VariantDefinition("normal", game.Id, names);
        var commonItem = new CollectibleItemDefinition("common-item", game.Id, names, "card");
        var rareItem = new CollectibleItemDefinition("rare-item", game.Id, names, "card");
        var common = new PrintingDefinition(
            "common-printing",
            commonItem.Id,
            new PrintingIdentity(game.Id, set.Id, "1", language.Id, variant.Id),
            commonRarity.Id,
            names);
        var rare = new PrintingDefinition(
            "rare-printing",
            rareItem.Id,
            new PrintingIdentity(game.Id, set.Id, "2", language.Id, variant.Id),
            rareRarity.Id,
            names);
        var japanese = new PrintingDefinition(
            "common-printing-ja",
            commonItem.Id,
            new PrintingIdentity(game.Id, set.Id, "1", japaneseLanguage.Id, variant.Id),
            commonRarity.Id,
            japaneseNames);
        var product = new ProductDefinition(
            "product",
            game.Id,
            set.Id,
            names,
            "booster",
            new[] { common.Id, rare.Id, japanese.Id });
        var catalog = new UniversalCatalog(
            new[] { language, japaneseLanguage },
            new[] { game },
            new[] { set },
            new[] { commonItem, rareItem },
            new[] { commonRarity, rareRarity },
            new[] { variant },
            new[] { common, rare, japanese },
            new[] { product });
        return new Fixture(catalog, product, common, rare, japanese);
    }

    private sealed class Fixture
    {
        public Fixture(
            UniversalCatalog catalog,
            ProductDefinition product,
            PrintingDefinition common,
            PrintingDefinition rare,
            PrintingDefinition japanese)
        {
            Catalog = catalog;
            Product = product;
            Common = common;
            Rare = rare;
            Japanese = japanese;
        }

        public UniversalCatalog Catalog { get; }
        public ProductDefinition Product { get; }
        public PrintingDefinition Common { get; }
        public PrintingDefinition Rare { get; }
        public PrintingDefinition Japanese { get; }
    }

    private sealed class FixedRandom : IGachaRandomSource
    {
        public double Value => 0d;
        public int Range(int minInclusive, int maxExclusive) => minInclusive;
    }

    private sealed class CountingRuleProvider : IProductRuleProvider
    {
        private readonly IProductRuleProvider inner;

        public CountingRuleProvider(IProductRuleProvider inner)
        {
            this.inner = inner;
        }

        public int CallCount { get; private set; }

        public ProductRuleProfile GetProfile(UniversalCatalog catalog, string productId, string languageId = null)
        {
            CallCount++;
            return inner.GetProfile(catalog, productId, languageId);
        }
    }

    private sealed class MemoryInventory : IInventoryProgressStore
    {
        public readonly Dictionary<string, int> Counts = new Dictionary<string, int>();
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
                var awards = new List<InventoryAward>();
                foreach (DrawnPrinting printing in result.Printings)
                {
                    int previous = Counts.TryGetValue(printing.PrintingId, out int value) ? value : 0;
                    Counts[printing.PrintingId] = previous + 1;
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
}
