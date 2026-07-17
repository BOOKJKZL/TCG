using System;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class InventoryData
{
    public Dictionary<string, int> Cards = new Dictionary<string, int>();
    public Dictionary<string, int> PacksOpened = new Dictionary<string, int>();
    public int Gold = 0;
    public long LastModifiedUtcTicks;

    public bool HasProgress => Gold != 0 ||
                               (Cards != null && Cards.Count != 0) ||
                               (PacksOpened != null && PacksOpened.Count != 0);

    public void Touch()
    {
        LastModifiedUtcTicks = DateTime.UtcNow.Ticks;
    }

    public InventorySnapshot ToSnapshot()
    {
        var snapshot = new InventorySnapshot
        {
            Gold = Gold,
            LastModifiedUtcTicks = LastModifiedUtcTicks
        };

        if (Cards != null)
        {
            foreach (KeyValuePair<string, int> pair in Cards)
                snapshot.Cards.Add(new InventoryEntry(pair.Key, pair.Value));
        }

        if (PacksOpened != null)
        {
            foreach (KeyValuePair<string, int> pair in PacksOpened)
                snapshot.PacksOpened.Add(new InventoryEntry(pair.Key, pair.Value));
        }

        return snapshot;
    }

    public static InventoryData FromSnapshot(InventorySnapshot snapshot)
    {
        var data = new InventoryData();
        if (snapshot == null)
            return data;

        data.Gold = snapshot.Gold;
        data.LastModifiedUtcTicks = snapshot.LastModifiedUtcTicks;
        CopyEntries(snapshot.Cards, data.Cards);
        CopyEntries(snapshot.PacksOpened, data.PacksOpened);
        return data;
    }

    private static void CopyEntries(List<InventoryEntry> source, Dictionary<string, int> target)
    {
        if (source == null)
            return;

        foreach (InventoryEntry entry in source)
        {
            if (entry != null && !string.IsNullOrWhiteSpace(entry.Id) && entry.Amount >= 0)
                target[entry.Id] = entry.Amount;
        }
    }
}

[System.Serializable]
public sealed class InventoryEntry
{
    public string Id;
    public int Amount;

    public InventoryEntry() { }

    public InventoryEntry(string id, int amount)
    {
        Id = id;
        Amount = amount;
    }
}

[System.Serializable]
public sealed class InventorySnapshot
{
    public int Version = 2;
    public List<InventoryEntry> Cards = new List<InventoryEntry>();
    public List<InventoryEntry> PacksOpened = new List<InventoryEntry>();
    public int Gold;
    public long LastModifiedUtcTicks;
}

public class Inventory : MonoBehaviour
{
    public static Inventory Instance { get; private set; }
    public InventoryData Data { get; private set; } = new InventoryData();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void AddPrinting(string printingId, int amount = 1)
    {
        if (string.IsNullOrWhiteSpace(printingId))
            throw new ArgumentException("Printing id cannot be empty.", nameof(printingId));
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
        if (Data.Cards == null)
            Data.Cards = new Dictionary<string, int>();
        if (!Data.Cards.ContainsKey(printingId))
            Data.Cards[printingId] = 0;
        Data.Cards[printingId] += amount;
        Data.Touch();
    }

    public void IncrementProductCounter(string productId)
    {
        if (string.IsNullOrWhiteSpace(productId))
            throw new ArgumentException("Product id cannot be empty.", nameof(productId));
        if (Data.PacksOpened == null)
            Data.PacksOpened = new Dictionary<string, int>();
        if (!Data.PacksOpened.ContainsKey(productId))
            Data.PacksOpened[productId] = 0;
        Data.PacksOpened[productId]++;
        Data.Touch();
    }

    public int GetProductsOpened(string productId) =>
        Data.PacksOpened != null && Data.PacksOpened.ContainsKey(productId) ? Data.PacksOpened[productId] : 0;

    public void ReplaceData(InventoryData data)
    {
        Data = data ?? new InventoryData();
        Data.Cards = Data.Cards ?? new Dictionary<string, int>();
        Data.PacksOpened = Data.PacksOpened ?? new Dictionary<string, int>();
    }

}
