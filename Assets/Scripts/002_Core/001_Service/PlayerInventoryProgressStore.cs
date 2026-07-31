using System;
using System.Collections.Generic;
using System.Linq;
using Gacha.Application;
using Gacha.Domain;

public sealed class PlayerInventoryProgressStore : IInventoryProgressStore
{
    private const int MaximumHistoryEntries = 250;
    private readonly Inventory inventory;
    private readonly Action<InventoryData> saveLocal;

    public PlayerInventoryProgressStore()
        : this(null, LocalSaveService.Save)
    {
    }

    public PlayerInventoryProgressStore(Inventory inventory, Action<InventoryData> saveLocal)
    {
        this.inventory = inventory;
        this.saveLocal = saveLocal ?? throw new ArgumentNullException(nameof(saveLocal));
    }

    public int GetProductsOpened(string productId)
    {
        return RequiredInventory().GetProductsOpened(productId);
    }

    public ProductInventoryBatchCommit CommitBatch(ProductOpeningBatchCommitRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        Inventory target = RequiredInventory();
        InventoryData rollback = InventoryData.FromSnapshot(target.Data.ToSnapshot());
        try
        {
            if (target.Data.OpeningHistory.Any(entry =>
                string.Equals(entry.TransactionId, request.TransactionId, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    $"Opening transaction '{request.TransactionId}' was already committed.");
            }

            var productCommits = new List<ProductInventoryCommit>(request.Draws.Count);
            var batchRarityCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            int newPrintingCount = 0;
            foreach (ProductDrawResult draw in request.Draws)
            {
                var awards = new List<InventoryAward>(draw.Printings.Count);
                foreach (DrawnPrinting drawn in draw.Printings)
                {
                    int previous = target.GetPrintingCount(drawn.PrintingId);
                    target.AddPrinting(drawn.PrintingId);
                    var award = new InventoryAward(drawn.PrintingId, previous, previous + 1);
                    awards.Add(award);
                    if (award.IsNew) newPrintingCount++;
                    Increment(batchRarityCounts, request.RarityByPrintingId[drawn.PrintingId], 1);
                }

                target.IncrementProductCounter(draw.ProductId);
                productCommits.Add(new ProductInventoryCommit(
                    draw.ProductId,
                    target.GetProductsOpened(draw.ProductId),
                    awards.AsReadOnly()));
            }

            Increment(target.Data.ProductsOpenedByLanguage, request.LanguageId, request.Draws.Count);
            Increment(target.Data.ProductsOpenedBySet, request.SetId, request.Draws.Count);
            foreach (KeyValuePair<string, int> pair in batchRarityCounts)
                Increment(target.Data.CardsDrawnByRarity, pair.Key, pair.Value);
            target.Data.OpeningHistory.Add(new OpeningHistoryData
            {
                TransactionId = request.TransactionId,
                OpenedAtUtcTicks = request.OpenedAtUtc.Ticks,
                ProductId = request.ProductId,
                SetId = request.SetId,
                LanguageId = request.LanguageId,
                ProfileId = request.ProfileId,
                ProductCount = request.Draws.Count,
                CardCount = request.Draws.Sum(draw => draw.Printings.Count),
                NewPrintingCount = newPrintingCount,
                RarityCounts = batchRarityCounts
            });
            if (target.Data.OpeningHistory.Count > MaximumHistoryEntries)
            {
                target.Data.OpeningHistory.RemoveRange(
                    0,
                    target.Data.OpeningHistory.Count - MaximumHistoryEntries);
            }
            target.Data.Touch();
            saveLocal(target.Data);
            return new ProductInventoryBatchCommit(request.TransactionId, productCommits.AsReadOnly());
        }
        catch
        {
            target.ReplaceData(rollback);
            throw;
        }
    }

    public IReadOnlyList<ProductOpeningHistoryEntry> GetOpeningHistory(int maximumCount)
    {
        if (maximumCount < 1 || maximumCount > MaximumHistoryEntries)
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        InventoryData data = RequiredInventory().Data;
        return (data.OpeningHistory ?? new List<OpeningHistoryData>())
            .AsEnumerable()
            .Reverse()
            .Take(maximumCount)
            .Select(entry => new ProductOpeningHistoryEntry(
                entry.TransactionId,
                new DateTime(entry.OpenedAtUtcTicks, DateTimeKind.Utc),
                entry.ProductId,
                entry.SetId,
                entry.LanguageId,
                entry.ProfileId,
                entry.ProductCount,
                entry.CardCount,
                entry.NewPrintingCount,
                entry.RarityCounts))
            .ToList()
            .AsReadOnly();
    }

    public ProductOpeningStatistics GetOpeningStatistics()
    {
        InventoryData data = RequiredInventory().Data;
        return new ProductOpeningStatistics(
            data.ProductsOpenedByLanguage,
            data.ProductsOpenedBySet,
            data.CardsDrawnByRarity);
    }

    private static void Increment(Dictionary<string, int> counts, string id, int amount)
    {
        if (counts == null) throw new ArgumentNullException(nameof(counts));
        counts[id] = counts.TryGetValue(id, out int current) ? current + amount : amount;
    }

    private Inventory RequiredInventory()
    {
        Inventory target = inventory != null ? inventory : Inventory.Instance;
        if (target == null)
            throw new InvalidOperationException("Inventory is not initialized.");
        return target;
    }
}
