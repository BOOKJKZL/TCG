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

        string[] printingIds = profile.Rules.Pools.Values
            .SelectMany(pool => pool.Entries)
            .Select(entry => entry.PrintingId)
            .ToArray();
        Assert.That(printingIds, Does.Not.Contain(fixture.Japanese.Id));
        Assert.That(printingIds.All(id =>
            fixture.Catalog.Printings[id].Identity.LanguageId == "en"), Is.True);
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
                new PlayerInventoryProgressStore(inventory, _ => throw new InvalidOperationException("disk full")));

            Assert.Throws<InvalidOperationException>(() =>
                service.Open(fixture.Product.Id, new FixedRandom()));
            Assert.That(inventory.Data.Cards, Is.Empty);
            Assert.That(inventory.Data.PacksOpened, Is.Empty);
            Assert.That(inventory.Data.UnseenPrintings, Is.Empty);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(gameObject);
        }
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

    private sealed class MemoryInventory : IInventoryProgressStore
    {
        public readonly Dictionary<string, int> Counts = new Dictionary<string, int>();
        private readonly Dictionary<string, int> products = new Dictionary<string, int>();

        public int GetProductsOpened(string productId)
        {
            return products.TryGetValue(productId, out int value) ? value : 0;
        }

        public ProductInventoryCommit Commit(ProductDrawResult result)
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
            return new ProductInventoryCommit(result.ProductId, opened, awards.AsReadOnly());
        }
    }
}
