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

        string json = JsonUtility.ToJson(source.ToSnapshot());
        InventorySnapshot snapshot = JsonUtility.FromJson<InventorySnapshot>(json);
        InventoryData restored = InventoryData.FromSnapshot(snapshot);

        Assert.That(restored.Gold, Is.EqualTo(123));
        Assert.That(restored.Cards["card-a"], Is.EqualTo(2));
        Assert.That(restored.PacksOpened["pack-a"], Is.EqualTo(7));
        Assert.That(restored.UnseenPrintings, Does.Contain("card-a"));
        Assert.That(restored.LastModifiedUtcTicks, Is.EqualTo(456));
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

}
