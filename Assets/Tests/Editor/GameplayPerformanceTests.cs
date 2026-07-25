using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Gacha.Application;
using Gacha.Domain;
using Gacha.Infrastructure.Content;
using Gacha.Infrastructure.Rules;
using NUnit.Framework;
using UnityEngine;

public class GameplayPerformanceTests
{
    [Test]
    [Category("Performance")]
    [Timeout(15000)]
    public void InstalledCatalog_ContinuousOpeningKeepsRetainedStateBounded()
    {
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
        string contentRoot = Path.Combine(projectRoot, "LocalContent", "Imports");
        CatalogLoadResult loaded = new PrivateContentCatalogProvider(contentRoot).Load();
        Assert.That(loaded.Succeeded, Is.True, loaded.ErrorMessage);
        Assert.That(loaded.Catalog.Products.Count, Is.EqualTo(5));

        var inventory = new MemoryInventory();
        var rules = new FallbackProductRuleProvider(
            new PokemonHistoricalRuleProvider(),
            new UniformSimulationRuleProvider(11, "en"));
        var service = new ProductOpeningService(
            loaded.Catalog,
            rules,
            inventory,
            contentLanguageId: "en");
        var random = new SystemGachaRandomSource(20260724);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        long memoryBefore = GC.GetTotalMemory(true);
        var stopwatch = Stopwatch.StartNew();

        const int packsPerProduct = 100;
        int expectedTotalCards = 0;
        foreach (ProductDefinition product in loaded.Catalog.Products.Values)
        {
            int expectedCardsPerPack = service.GetProfile(product.Id).Rules.Slots.Sum(slot => slot.DrawCount);
            expectedTotalCards += expectedCardsPerPack * packsPerProduct;
            for (int pack = 0; pack < packsPerProduct; pack++)
            {
                ProductOpeningOutcome outcome = service.Open(product.Id, random);
                Assert.That(outcome.Draw.Printings, Has.Count.EqualTo(expectedCardsPerPack));
            }
        }

        stopwatch.Stop();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        long retainedGrowth = Math.Max(0L, GC.GetTotalMemory(true) - memoryBefore);
        TestContext.WriteLine(
            $"ContinuousOpening packs={inventory.TotalPacks} cards={inventory.TotalCards} " +
            $"elapsed={stopwatch.Elapsed.TotalSeconds:0.000}s retained={retainedGrowth / 1024f / 1024f:0.000}MiB");

        Assert.That(inventory.TotalPacks, Is.EqualTo(loaded.Catalog.Products.Count * packsPerProduct));
        Assert.That(inventory.TotalCards, Is.EqualTo(expectedTotalCards));
        Assert.That(inventory.DistinctCards, Is.LessThanOrEqualTo(loaded.Catalog.Printings.Count));
        Assert.That(inventory.TrackedProducts, Is.EqualTo(loaded.Catalog.Products.Count));
        Assert.That(retainedGrowth, Is.LessThan(32L * 1024L * 1024L),
            $"Retained managed memory grew by {retainedGrowth / 1024f / 1024f:0.00} MiB.");
        Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)),
            $"Opening 500 packs took {stopwatch.Elapsed.TotalSeconds:0.00}s.");
    }

    [Test]
    [Category("Performance")]
    [Timeout(10000)]
    public void LargeInventory_SnapshotRoundTripRemainsLinearAndCompact()
    {
        const int printingCount = 10000;
        var inventory = new InventoryData();
        for (int index = 0; index < printingCount; index++)
        {
            string id = $"printing-{index:D5}";
            inventory.Cards[id] = index + 1;
            if ((index & 1) == 0)
                inventory.UnseenPrintings.Add(id);
        }
        inventory.PacksOpened["product-soak"] = 25000;

        var stopwatch = Stopwatch.StartNew();
        string json = JsonUtility.ToJson(inventory.ToSnapshot());
        InventorySnapshot snapshot = JsonUtility.FromJson<InventorySnapshot>(json);
        InventoryData restored = InventoryData.FromSnapshot(snapshot);
        stopwatch.Stop();
        TestContext.WriteLine(
            $"InventoryRoundTrip printings={printingCount} jsonBytes={json.Length} " +
            $"elapsed={stopwatch.Elapsed.TotalSeconds:0.000}s");

        Assert.That(restored.Cards, Has.Count.EqualTo(printingCount));
        Assert.That(restored.UnseenPrintings, Has.Count.EqualTo(printingCount / 2));
        Assert.That(restored.PacksOpened["product-soak"], Is.EqualTo(25000));
        Assert.That(json.Length, Is.LessThan(1024 * 1024), "A 10k-card inventory should stay below 1 MiB.");
        Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)),
            $"10k snapshot round-trip took {stopwatch.Elapsed.TotalSeconds:0.00}s.");
    }

    private sealed class MemoryInventory : IInventoryProgressStore
    {
        private readonly Dictionary<string, int> cards = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> products = new Dictionary<string, int>(StringComparer.Ordinal);

        public int TotalPacks => products.Values.Sum();
        public int TotalCards => cards.Values.Sum();
        public int DistinctCards => cards.Count;
        public int TrackedProducts => products.Count;

        public int GetProductsOpened(string productId)
        {
            return products.TryGetValue(productId, out int count) ? count : 0;
        }

        public ProductInventoryCommit Commit(ProductDrawResult result)
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
            return new ProductInventoryCommit(result.ProductId, opened, awards.AsReadOnly());
        }
    }
}
