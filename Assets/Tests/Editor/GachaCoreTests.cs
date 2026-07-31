using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

public class GachaCoreTests
{
    [Test]
    public void InventorySnapshot_RoundTrip_PreservesDictionaryData()
    {
        var source = new InventoryData { Gold = 123, LastModifiedUtcTicks = 456 };
        source.Cards["card-a"] = 2;
        source.PacksOpened["pack-a"] = 7;
        source.UnseenPrintings.Add("card-a");
        source.ProductsOpenedByLanguage["en"] = 10;
        source.ProductsOpenedBySet["set-a"] = 10;
        source.CardsDrawnByRarity["rare-a"] = 1;
        source.OpeningHistory.Add(new OpeningHistoryData
        {
            TransactionId = "transaction-a",
            OpenedAtUtcTicks = 789,
            ProductId = "pack-a",
            SetId = "set-a",
            LanguageId = "en",
            ProfileId = "profile-a",
            ProductCount = 10,
            CardCount = 30,
            NewPrintingCount = 1,
            RarityCounts = new System.Collections.Generic.Dictionary<string, int>
            {
                ["rare-a"] = 1,
                ["common-a"] = 29
            }
        });

        string json = JsonUtility.ToJson(source.ToSnapshot());
        InventorySnapshot snapshot = JsonUtility.FromJson<InventorySnapshot>(json);
        InventoryData restored = InventoryData.FromSnapshot(snapshot);

        Assert.That(restored.Gold, Is.EqualTo(123));
        Assert.That(restored.Cards["card-a"], Is.EqualTo(2));
        Assert.That(restored.PacksOpened["pack-a"], Is.EqualTo(7));
        Assert.That(restored.UnseenPrintings, Does.Contain("card-a"));
        Assert.That(restored.LastModifiedUtcTicks, Is.EqualTo(456));
        Assert.That(snapshot.Version, Is.EqualTo(4));
        Assert.That(restored.OpeningHistory, Has.Count.EqualTo(1));
        Assert.That(restored.OpeningHistory[0].TransactionId, Is.EqualTo("transaction-a"));
        Assert.That(restored.OpeningHistory[0].RarityCounts["common-a"], Is.EqualTo(29));
        Assert.That(restored.ProductsOpenedByLanguage["en"], Is.EqualTo(10));
        Assert.That(restored.ProductsOpenedBySet["set-a"], Is.EqualTo(10));
        Assert.That(restored.CardsDrawnByRarity["rare-a"], Is.EqualTo(1));
    }

    [Test]
    public void VersionTwoSnapshot_MigratesExistingCardsAsAlreadySeen()
    {
        var snapshot = new InventorySnapshot { Version = 2 };
        snapshot.Cards.Add(new InventoryEntry("legacy-card", 3));

        InventoryData restored = InventoryData.FromSnapshot(snapshot);

        Assert.That(restored.Cards["legacy-card"], Is.EqualTo(3));
        Assert.That(restored.UnseenPrintings, Is.Empty);
    }

    [Test]
    public void ConflictResolver_PrefersMostRecentlyModifiedProgress()
    {
        var local = new InventoryData { Gold = 1, LastModifiedUtcTicks = 200 };
        var remote = new InventoryData { Gold = 2, LastModifiedUtcTicks = 100 };
        var resolver = new LatestWriteWinsInventoryConflictResolver();

        InventoryData result = resolver.Resolve(local, remote);

        Assert.That(result, Is.SameAs(local));
    }

    [Test]
    public void CloudConflict_BothDifferentProgressSnapshotsRequireExplicitChoice()
    {
        InventoryData local = CreateConflictInventory("local-card", 2, 200, "local-transaction");
        InventoryData cloud = CreateConflictInventory("cloud-card", 3, 100, "cloud-transaction");
        var coordinator = new CloudInventoryConflictCoordinator();

        InventoryConflictPreparation preparation = coordinator.Prepare(local, cloud, true);

        Assert.That(preparation.RequiresChoice, Is.True);
        Assert.That(preparation.Selected.Cards, Does.ContainKey("local-card"));
        Assert.That(preparation.Selected.Cards, Does.Not.ContainKey("cloud-card"));
        Assert.That(coordinator.HasPending, Is.True);
        Assert.That(coordinator.PendingPreview.Local.TotalCardCount, Is.EqualTo(2));
        Assert.That(coordinator.PendingPreview.Cloud.TotalCardCount, Is.EqualTo(3));
    }

    [Test]
    public void CloudConflict_EquivalentContentDoesNotPromptForTimestampOnlyDifference()
    {
        InventoryData local = CreateConflictInventory("card-a", 2, 100, "transaction-a");
        InventoryData cloud = CreateConflictInventory("card-a", 2, 200, "transaction-a");
        cloud.OpeningHistory[0].OpenedAtUtcTicks = local.OpeningHistory[0].OpenedAtUtcTicks;
        var coordinator = new CloudInventoryConflictCoordinator();

        InventoryConflictPreparation preparation = coordinator.Prepare(local, cloud, true);

        Assert.That(preparation.RequiresChoice, Is.False);
        Assert.That(preparation.Selected.LastModifiedUtcTicks, Is.EqualTo(200));
        Assert.That(coordinator.HasPending, Is.False);
    }

    [Test]
    public void SafeMerge_UsesMaximumCountsAndUnitesDistinctTransactions()
    {
        InventoryData local = CreateConflictInventory("shared-card", 2, 100, "local-transaction");
        local.Cards["local-only"] = 1;
        InventoryData cloud = CreateConflictInventory("shared-card", 5, 200, "cloud-transaction");
        cloud.Cards["cloud-only"] = 4;

        InventoryData merged = SafeInventoryConflictMerger.Merge(local, cloud);

        Assert.That(merged.Cards["shared-card"], Is.EqualTo(5), "Merge must not add duplicate snapshot counts.");
        Assert.That(merged.Cards["local-only"], Is.EqualTo(1));
        Assert.That(merged.Cards["cloud-only"], Is.EqualTo(4));
        Assert.That(merged.OpeningHistory, Has.Count.EqualTo(2));
        Assert.That(merged.OpeningHistory, Has.Exactly(1).Matches<OpeningHistoryData>(
            entry => entry.TransactionId == "local-transaction"));
        Assert.That(merged.OpeningHistory, Has.Exactly(1).Matches<OpeningHistoryData>(
            entry => entry.TransactionId == "cloud-transaction"));
    }

    [Test]
    public async Task CloudConflict_FailedCloudWriteRollsBackLocalAndKeepsChoicePending()
    {
        InventoryData local = CreateConflictInventory("local-card", 2, 200, "local-transaction");
        InventoryData cloud = CreateConflictInventory("cloud-card", 3, 100, "cloud-transaction");
        var coordinator = new CloudInventoryConflictCoordinator();
        coordinator.Prepare(local, cloud, true);
        var target = new MemoryConflictTarget(local, false);

        InventoryConflictResolutionResult result = await coordinator.ResolveAsync(
            InventoryConflictChoice.UseCloud,
            target);

        Assert.That(result.Succeeded, Is.False);
        Assert.That(target.Current.Cards, Does.ContainKey("local-card"));
        Assert.That(target.Current.Cards, Does.Not.ContainKey("cloud-card"));
        Assert.That(target.ApplyCount, Is.EqualTo(2));
        Assert.That(coordinator.HasPending, Is.True);
    }

    [Test]
    public async Task CloudConflict_SuccessfulSafeMergeSavesOnceAndClearsPendingChoice()
    {
        InventoryData local = CreateConflictInventory("local-card", 2, 200, "local-transaction");
        InventoryData cloud = CreateConflictInventory("cloud-card", 3, 100, "cloud-transaction");
        var coordinator = new CloudInventoryConflictCoordinator();
        coordinator.Prepare(local, cloud, true);
        var target = new MemoryConflictTarget(local, true);

        InventoryConflictResolutionResult result = await coordinator.ResolveAsync(
            InventoryConflictChoice.SafeMerge,
            target);

        Assert.That(result.Succeeded, Is.True);
        Assert.That(target.Current.Cards, Does.ContainKey("local-card"));
        Assert.That(target.Current.Cards, Does.ContainKey("cloud-card"));
        Assert.That(target.CloudSaveCount, Is.EqualTo(1));
        Assert.That(coordinator.HasPending, Is.False);
    }

    private static InventoryData CreateConflictInventory(
        string cardId,
        int count,
        long modified,
        string transactionId)
    {
        var inventory = new InventoryData { LastModifiedUtcTicks = modified };
        inventory.Cards[cardId] = count;
        inventory.UnseenPrintings.Add(cardId);
        inventory.PacksOpened["product-a"] = 1;
        inventory.OpeningHistory.Add(new OpeningHistoryData
        {
            TransactionId = transactionId,
            OpenedAtUtcTicks = modified,
            ProductId = "product-a",
            SetId = "set-a",
            LanguageId = "en",
            ProfileId = "profile-a",
            ProductCount = 1,
            CardCount = count,
            NewPrintingCount = 1,
            RarityCounts = new Dictionary<string, int> { ["common"] = count }
        });
        return inventory;
    }

    private sealed class MemoryConflictTarget : IInventoryConflictTarget
    {
        private readonly bool cloudSaveResult;

        public MemoryConflictTarget(InventoryData current, bool cloudSaveResult)
        {
            Current = CloudInventoryConflictCoordinator.Clone(current);
            this.cloudSaveResult = cloudSaveResult;
        }

        public InventoryData Current { get; private set; }
        public int ApplyCount { get; private set; }
        public int CloudSaveCount { get; private set; }

        public InventoryData CaptureLocal() => CloudInventoryConflictCoordinator.Clone(Current);

        public void ApplyLocal(InventoryData inventory)
        {
            ApplyCount++;
            Current = CloudInventoryConflictCoordinator.Clone(inventory);
        }

        public Task<bool> SaveCloudAsync(InventoryData inventory)
        {
            CloudSaveCount++;
            return Task.FromResult(cloudSaveResult);
        }
    }

}
