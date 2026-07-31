using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[System.Serializable]
public class InventoryData
{
    public Dictionary<string, int> Cards = new Dictionary<string, int>();
    public Dictionary<string, int> PacksOpened = new Dictionary<string, int>();
    public HashSet<string> UnseenPrintings = new HashSet<string>(StringComparer.Ordinal);
    public List<OpeningHistoryData> OpeningHistory = new List<OpeningHistoryData>();
    public Dictionary<string, int> ProductsOpenedByLanguage = new Dictionary<string, int>();
    public Dictionary<string, int> ProductsOpenedBySet = new Dictionary<string, int>();
    public Dictionary<string, int> CardsDrawnByRarity = new Dictionary<string, int>();
    public int Gold = 0;
    public long LastModifiedUtcTicks;

    public bool HasProgress => Gold != 0 ||
                               (Cards != null && Cards.Count != 0) ||
                               (PacksOpened != null && PacksOpened.Count != 0) ||
                               (UnseenPrintings != null && UnseenPrintings.Count != 0) ||
                               (OpeningHistory != null && OpeningHistory.Count != 0);

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

        if (UnseenPrintings != null)
        {
            foreach (string printingId in UnseenPrintings)
            {
                if (!string.IsNullOrWhiteSpace(printingId))
                    snapshot.UnseenPrintings.Add(printingId);
            }
        }

        if (OpeningHistory != null)
        {
            foreach (OpeningHistoryData entry in OpeningHistory)
            {
                if (entry != null)
                    snapshot.OpeningHistory.Add(entry.ToSnapshot());
            }
        }
        CopyDictionary(ProductsOpenedByLanguage, snapshot.ProductsOpenedByLanguage);
        CopyDictionary(ProductsOpenedBySet, snapshot.ProductsOpenedBySet);
        CopyDictionary(CardsDrawnByRarity, snapshot.CardsDrawnByRarity);

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
        if (snapshot.Version >= 3 && snapshot.UnseenPrintings != null)
        {
            foreach (string printingId in snapshot.UnseenPrintings)
            {
                if (!string.IsNullOrWhiteSpace(printingId) &&
                    data.Cards.TryGetValue(printingId, out int count) && count > 0)
                {
                    data.UnseenPrintings.Add(printingId);
                }
            }
        }
        if (snapshot.Version >= 4)
        {
            if (snapshot.OpeningHistory != null)
            {
                foreach (OpeningHistorySnapshot entry in snapshot.OpeningHistory)
                {
                    OpeningHistoryData restored = OpeningHistoryData.FromSnapshot(entry);
                    if (restored != null)
                        data.OpeningHistory.Add(restored);
                }
            }
            CopyEntries(snapshot.ProductsOpenedByLanguage, data.ProductsOpenedByLanguage);
            CopyEntries(snapshot.ProductsOpenedBySet, data.ProductsOpenedBySet);
            CopyEntries(snapshot.CardsDrawnByRarity, data.CardsDrawnByRarity);
        }
        return data;
    }

    private static void CopyDictionary(
        Dictionary<string, int> source,
        List<InventoryEntry> target)
    {
        if (source == null) return;
        foreach (KeyValuePair<string, int> pair in source.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!string.IsNullOrWhiteSpace(pair.Key) && pair.Value >= 0)
                target.Add(new InventoryEntry(pair.Key, pair.Value));
        }
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
public sealed class OpeningHistoryData
{
    public string TransactionId;
    public long OpenedAtUtcTicks;
    public string ProductId;
    public string SetId;
    public string LanguageId;
    public string ProfileId;
    public int ProductCount;
    public int CardCount;
    public int NewPrintingCount;
    public Dictionary<string, int> RarityCounts = new Dictionary<string, int>();

    public OpeningHistorySnapshot ToSnapshot()
    {
        var snapshot = new OpeningHistorySnapshot
        {
            TransactionId = TransactionId,
            OpenedAtUtcTicks = OpenedAtUtcTicks,
            ProductId = ProductId,
            SetId = SetId,
            LanguageId = LanguageId,
            ProfileId = ProfileId,
            ProductCount = ProductCount,
            CardCount = CardCount,
            NewPrintingCount = NewPrintingCount
        };
        if (RarityCounts != null)
        {
            foreach (KeyValuePair<string, int> pair in RarityCounts.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                snapshot.RarityCounts.Add(new InventoryEntry(pair.Key, pair.Value));
        }
        return snapshot;
    }

    public static OpeningHistoryData FromSnapshot(OpeningHistorySnapshot snapshot)
    {
        if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.TransactionId) ||
            string.IsNullOrWhiteSpace(snapshot.ProductId) || string.IsNullOrWhiteSpace(snapshot.SetId) ||
            string.IsNullOrWhiteSpace(snapshot.LanguageId) || string.IsNullOrWhiteSpace(snapshot.ProfileId) ||
            snapshot.OpenedAtUtcTicks <= 0 || snapshot.ProductCount <= 0 || snapshot.CardCount <= 0 ||
            snapshot.NewPrintingCount < 0 || snapshot.NewPrintingCount > snapshot.CardCount)
        {
            return null;
        }

        var data = new OpeningHistoryData
        {
            TransactionId = snapshot.TransactionId.Trim(),
            OpenedAtUtcTicks = snapshot.OpenedAtUtcTicks,
            ProductId = snapshot.ProductId.Trim(),
            SetId = snapshot.SetId.Trim(),
            LanguageId = snapshot.LanguageId.Trim(),
            ProfileId = snapshot.ProfileId.Trim(),
            ProductCount = snapshot.ProductCount,
            CardCount = snapshot.CardCount,
            NewPrintingCount = snapshot.NewPrintingCount
        };
        if (snapshot.RarityCounts != null)
        {
            foreach (InventoryEntry entry in snapshot.RarityCounts)
            {
                if (entry != null && !string.IsNullOrWhiteSpace(entry.Id) && entry.Amount > 0)
                    data.RarityCounts[entry.Id.Trim()] = entry.Amount;
            }
        }
        return data;
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
    public int Version = 4;
    public List<InventoryEntry> Cards = new List<InventoryEntry>();
    public List<InventoryEntry> PacksOpened = new List<InventoryEntry>();
    public List<string> UnseenPrintings = new List<string>();
    public List<OpeningHistorySnapshot> OpeningHistory = new List<OpeningHistorySnapshot>();
    public List<InventoryEntry> ProductsOpenedByLanguage = new List<InventoryEntry>();
    public List<InventoryEntry> ProductsOpenedBySet = new List<InventoryEntry>();
    public List<InventoryEntry> CardsDrawnByRarity = new List<InventoryEntry>();
    public int Gold;
    public long LastModifiedUtcTicks;
}

[System.Serializable]
public sealed class OpeningHistorySnapshot
{
    public string TransactionId;
    public long OpenedAtUtcTicks;
    public string ProductId;
    public string SetId;
    public string LanguageId;
    public string ProfileId;
    public int ProductCount;
    public int CardCount;
    public int NewPrintingCount;
    public List<InventoryEntry> RarityCounts = new List<InventoryEntry>();
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

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
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
        bool isFirstCopy = Data.Cards[printingId] == 0;
        Data.Cards[printingId] += amount;
        if (isFirstCopy)
        {
            Data.UnseenPrintings ??= new HashSet<string>(StringComparer.Ordinal);
            Data.UnseenPrintings.Add(printingId);
        }
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

    public int GetPrintingCount(string printingId) =>
        Data.Cards != null && Data.Cards.ContainsKey(printingId) ? Data.Cards[printingId] : 0;

    public bool IsPrintingUnseen(string printingId) =>
        Data.UnseenPrintings != null && Data.UnseenPrintings.Contains(printingId);

    public bool MarkPrintingSeen(string printingId)
    {
        if (string.IsNullOrWhiteSpace(printingId))
            throw new ArgumentException("Printing id cannot be empty.", nameof(printingId));
        if (Data.UnseenPrintings == null || !Data.UnseenPrintings.Remove(printingId))
            return false;
        Data.Touch();
        return true;
    }

    public void ReplaceData(InventoryData data)
    {
        Data = data ?? new InventoryData();
        Data.Cards = Data.Cards ?? new Dictionary<string, int>();
        Data.PacksOpened = Data.PacksOpened ?? new Dictionary<string, int>();
        Data.UnseenPrintings = Data.UnseenPrintings == null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(Data.UnseenPrintings.Where(id =>
                !string.IsNullOrWhiteSpace(id) &&
                Data.Cards.TryGetValue(id, out int count) && count > 0), StringComparer.Ordinal);
        Data.OpeningHistory = Data.OpeningHistory ?? new List<OpeningHistoryData>();
        Data.ProductsOpenedByLanguage = Data.ProductsOpenedByLanguage ?? new Dictionary<string, int>();
        Data.ProductsOpenedBySet = Data.ProductsOpenedBySet ?? new Dictionary<string, int>();
        Data.CardsDrawnByRarity = Data.CardsDrawnByRarity ?? new Dictionary<string, int>();
    }

}
