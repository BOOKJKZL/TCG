using System;
using Gacha.Application;
using NUnit.Framework;
using UnityEngine;

public class CollectionProgressStoreTests
{
    [Test]
    public void FirstAcquisition_RemainsNewUntilSuccessfullyMarkedSeen()
    {
        var gameObject = new GameObject("Collection Progress Test");
        Inventory inventory = gameObject.AddComponent<Inventory>();
        int saves = 0;
        try
        {
            inventory.AddPrinting("printing-a", 2);
            var store = new PlayerCollectionProgressStore(inventory, _ => saves++);

            CollectionItemProgress before = store.GetProgress("printing-a");
            Assert.That(before.OwnedCount, Is.EqualTo(2));
            Assert.That(before.IsNew, Is.True);
            Assert.That(store.MarkSeen("printing-a"), Is.True);
            Assert.That(saves, Is.EqualTo(1));
            Assert.That(store.GetProgress("printing-a").IsNew, Is.False);
            Assert.That(store.MarkSeen("printing-a"), Is.False);
            Assert.That(saves, Is.EqualTo(1));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(gameObject);
        }
    }

    [Test]
    public void MarkSeen_RollsBackWhenLocalSaveFails()
    {
        var gameObject = new GameObject("Collection Progress Rollback Test");
        Inventory inventory = gameObject.AddComponent<Inventory>();
        try
        {
            inventory.AddPrinting("printing-a");
            var store = new PlayerCollectionProgressStore(
                inventory,
                _ => throw new InvalidOperationException("disk full"));

            Assert.Throws<InvalidOperationException>(() => store.MarkSeen("printing-a"));
            Assert.That(store.GetProgress("printing-a").IsNew, Is.True);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(gameObject);
        }
    }
}
