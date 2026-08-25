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
            foreach (string printingId in UnseenPrintings.OrderBy(id => id, StringComparer.Ordinal))
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

        snapshot = InventorySnapshotMigrator.Migrate(snapshot);
        InventorySnapshotValidator.Validate(snapshot);

        data.Gold = snapshot.Gold;
        data.LastModifiedUtcTicks = snapshot.LastModifiedUtcTicks;
        CopyEntries(snapshot.Cards, data.Cards);
        CopyEntries(snapshot.PacksOpened, data.PacksOpened);
        if (snapshot.UnseenPrintings != null)
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

public static class InventorySnapshotMigrator
{
    public const int MinimumSupportedVersion = 2;
    public const int CurrentVersion = 4;

    public static InventorySnapshot Migrate(InventorySnapshot snapshot)
    {
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));
        if (snapshot.Version < MinimumSupportedVersion || snapshot.Version > CurrentVersion)
        {
            throw new InvalidOperationException(
                $"Inventory snapshot version {snapshot.Version} is not supported.");
        }

        NormalizeCommonLists(snapshot);
        while (snapshot.Version < CurrentVersion)
        {
            switch (snapshot.Version)
            {
                case 2:
                    // Existing collections pre-date the unseen-card badge and must
                    // not be presented to the player as newly obtained cards.
                    snapshot.UnseenPrintings = new List<string>();
                    snapshot.Version = 3;
                    break;
                case 3:
                    snapshot.OpeningHistory = new List<OpeningHistorySnapshot>();
                    snapshot.ProductsOpenedByLanguage = new List<InventoryEntry>();
                    snapshot.ProductsOpenedBySet = new List<InventoryEntry>();
                    snapshot.CardsDrawnByRarity = new List<InventoryEntry>();
                    snapshot.Version = 4;
                    break;
                default:
                    throw new InvalidOperationException(
                        $"No migration is defined for inventory snapshot version {snapshot.Version}.");
            }
        }

        NormalizeCommonLists(snapshot);
        return snapshot;
    }

    private static void NormalizeCommonLists(InventorySnapshot snapshot)
    {
        snapshot.Cards = snapshot.Cards ?? new List<InventoryEntry>();
        snapshot.PacksOpened = snapshot.PacksOpened ?? new List<InventoryEntry>();
        snapshot.UnseenPrintings = snapshot.UnseenPrintings ?? new List<string>();
        snapshot.OpeningHistory = snapshot.OpeningHistory ?? new List<OpeningHistorySnapshot>();
        snapshot.ProductsOpenedByLanguage = snapshot.ProductsOpenedByLanguage ?? new List<InventoryEntry>();
        snapshot.ProductsOpenedBySet = snapshot.ProductsOpenedBySet ?? new List<InventoryEntry>();
        snapshot.CardsDrawnByRarity = snapshot.CardsDrawnByRarity ?? new List<InventoryEntry>();
    }
}

public static class InventorySnapshotValidator
{
    private const int MaximumIdLength = 512;
    private const int MaximumInventoryEntries = 200000;
    private const int MaximumCounterEntries = 10000;
    private const int MaximumHistoryEntries = 250;

    public static void Validate(InventorySnapshot snapshot)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        if (snapshot.Version != InventorySnapshotMigrator.CurrentVersion)
            throw new InvalidOperationException("The inventory snapshot must be migrated before validation.");
        if (snapshot.Gold < 0 || snapshot.LastModifiedUtcTicks < 0 ||
            snapshot.LastModifiedUtcTicks > DateTime.MaxValue.Ticks)
        {
            throw new InvalidOperationException("The inventory scalar metadata is invalid.");
        }

        Dictionary<string, int> cards = ValidateEntries(
            snapshot.Cards, MaximumInventoryEntries, "card", false);
        ValidateEntries(snapshot.PacksOpened, MaximumCounterEntries, "product", false);
        ValidateEntries(snapshot.ProductsOpenedByLanguage, MaximumCounterEntries, "language statistic", false);
        ValidateEntries(snapshot.ProductsOpenedBySet, MaximumCounterEntries, "set statistic", false);
        ValidateEntries(snapshot.CardsDrawnByRarity, MaximumCounterEntries, "rarity statistic", false);

        if (snapshot.UnseenPrintings == null || snapshot.UnseenPrintings.Count > MaximumInventoryEntries)
            throw new InvalidOperationException("The unseen-card list is invalid.");
        var unseen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string printingId in snapshot.UnseenPrintings)
        {
            if (!ValidId(printingId) || !unseen.Add(printingId) ||
                !cards.TryGetValue(printingId, out int count) || count <= 0)
            {
                throw new InvalidOperationException("The unseen-card list contains an invalid reference.");
            }
        }

        if (snapshot.OpeningHistory == null || snapshot.OpeningHistory.Count > MaximumHistoryEntries)
            throw new InvalidOperationException("The opening history is invalid.");
        var transactionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (OpeningHistorySnapshot entry in snapshot.OpeningHistory)
        {
            if (entry == null || !ValidId(entry.TransactionId) || !transactionIds.Add(entry.TransactionId) ||
                !ValidId(entry.ProductId) || !ValidId(entry.SetId) || !ValidId(entry.LanguageId) ||
                !ValidId(entry.ProfileId) || entry.OpenedAtUtcTicks <= 0 ||
                entry.OpenedAtUtcTicks > DateTime.MaxValue.Ticks || entry.ProductCount <= 0 ||
                entry.CardCount <= 0 || entry.NewPrintingCount < 0 || entry.NewPrintingCount > entry.CardCount)
            {
                throw new InvalidOperationException("The opening history contains an invalid entry.");
            }
            ValidateEntries(entry.RarityCounts, MaximumCounterEntries, "history rarity", true);
        }
    }

    private static Dictionary<string, int> ValidateEntries(
        List<InventoryEntry> entries,
        int maximumCount,
        string label,
        bool requirePositive)
    {
        if (entries == null || entries.Count > maximumCount)
            throw new InvalidOperationException($"The {label} entry count is invalid.");
        var values = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (InventoryEntry entry in entries)
        {
            if (entry == null || !ValidId(entry.Id) || values.ContainsKey(entry.Id) ||
                entry.Amount < 0 || requirePositive && entry.Amount == 0)
            {
                throw new InvalidOperationException($"The {label} data contains an invalid entry.");
            }
            values.Add(entry.Id, entry.Amount);
        }
        return values;
    }

    private static bool ValidId(string value)
    {
        return !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= MaximumIdLength;
    }
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
