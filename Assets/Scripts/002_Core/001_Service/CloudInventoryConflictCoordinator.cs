using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

public enum InventoryConflictChoice
{
    KeepLocal,
    UseCloud,
    SafeMerge
}

public sealed class InventoryProgressSummary
{
    internal InventoryProgressSummary(InventoryData data)
    {
        data = data ?? new InventoryData();
        LastModifiedUtcTicks = data.LastModifiedUtcTicks;
        DistinctPrintingCount = data.Cards?.Count(pair => pair.Value > 0) ?? 0;
        TotalCardCount = data.Cards?.Values.Sum(value => (long)Math.Max(0, value)) ?? 0L;
        TotalProductsOpened = data.PacksOpened?.Values.Sum(value => (long)Math.Max(0, value)) ?? 0L;
        HistoryCount = data.OpeningHistory?.Count ?? 0;
    }

    public long LastModifiedUtcTicks { get; }
    public DateTime LastModifiedUtc => LastModifiedUtcTicks > 0 && LastModifiedUtcTicks <= DateTime.MaxValue.Ticks
        ? new DateTime(LastModifiedUtcTicks, DateTimeKind.Utc)
        : DateTime.MinValue;
    public int DistinctPrintingCount { get; }
    public long TotalCardCount { get; }
    public long TotalProductsOpened { get; }
    public int HistoryCount { get; }
}

public sealed class InventoryConflictPreview
{
    internal InventoryConflictPreview(InventoryData local, InventoryData cloud)
    {
        Local = new InventoryProgressSummary(local);
        Cloud = new InventoryProgressSummary(cloud);
    }

    public InventoryProgressSummary Local { get; }
    public InventoryProgressSummary Cloud { get; }
}

public sealed class InventoryConflictPreparation
{
    internal InventoryConflictPreparation(InventoryData selected, bool requiresChoice)
    {
        Selected = selected;
        RequiresChoice = requiresChoice;
    }

    public InventoryData Selected { get; }
    public bool RequiresChoice { get; }
}

public sealed class InventoryConflictResolutionResult
{
    private InventoryConflictResolutionResult(bool succeeded, InventoryData resolved, string error)
    {
        Succeeded = succeeded;
        Resolved = resolved;
        Error = error;
    }

    public bool Succeeded { get; }
    public InventoryData Resolved { get; }
    public string Error { get; }

    public static InventoryConflictResolutionResult Success(InventoryData resolved) =>
        new InventoryConflictResolutionResult(true, resolved, null);

    public static InventoryConflictResolutionResult Failure(string error) =>
        new InventoryConflictResolutionResult(false, null, error);
}

public interface IInventoryConflictTarget
{
    InventoryData CaptureLocal();
    void ApplyLocal(InventoryData inventory);
    Task<bool> SaveCloudAsync(InventoryData inventory);
}

public sealed class CloudInventoryConflictCoordinator
{
    private InventoryData pendingLocal;
    private InventoryData pendingCloud;
    private bool resolving;

    public event Action Changed;
    public bool HasPending => pendingLocal != null && pendingCloud != null;
    public bool IsResolving => resolving;
    public InventoryConflictPreview PendingPreview => HasPending
        ? new InventoryConflictPreview(pendingLocal, pendingCloud)
        : null;

    public InventoryConflictPreparation Prepare(
        InventoryData local,
        InventoryData cloud,
        bool cloudFound)
    {
        local = Clone(local);
        cloud = Clone(cloud);
        ClearPending(false);

        if (!cloudFound)
            return new InventoryConflictPreparation(local, false);
        if (InventoryDataContentComparer.Equals(local, cloud))
        {
            return new InventoryConflictPreparation(
                local.LastModifiedUtcTicks >= cloud.LastModifiedUtcTicks ? local : cloud,
                false);
        }
        if (!local.HasProgress && cloud.HasProgress)
            return new InventoryConflictPreparation(cloud, false);
        if (local.HasProgress && !cloud.HasProgress)
            return new InventoryConflictPreparation(local, false);
        if (!local.HasProgress && !cloud.HasProgress)
            return new InventoryConflictPreparation(local, false);

        pendingLocal = local;
        pendingCloud = cloud;
        Changed?.Invoke();
        return new InventoryConflictPreparation(Clone(local), true);
    }

    public void RefreshLocal(InventoryData local)
    {
        if (!HasPending || resolving) return;
        pendingLocal = Clone(local);
        Changed?.Invoke();
    }

    public async Task<InventoryConflictResolutionResult> ResolveAsync(
        InventoryConflictChoice choice,
        IInventoryConflictTarget target)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        if (!HasPending)
            return InventoryConflictResolutionResult.Failure("There is no pending cloud conflict.");
        if (resolving)
            return InventoryConflictResolutionResult.Failure("A cloud conflict choice is already being saved.");

        resolving = true;
        Changed?.Invoke();
        InventoryData original = Clone(target.CaptureLocal());
        pendingLocal = Clone(original);
        InventoryData resolved = Resolve(choice, pendingLocal, pendingCloud);
        resolved.Touch();

        try
        {
            target.ApplyLocal(Clone(resolved));
            bool cloudSaved = await target.SaveCloudAsync(Clone(resolved));
            if (!cloudSaved)
                throw new InvalidOperationException("The cloud save did not confirm the selected progress.");

            ClearPending(false);
            resolving = false;
            Changed?.Invoke();
            return InventoryConflictResolutionResult.Success(Clone(resolved));
        }
        catch (Exception exception)
        {
            try
            {
                target.ApplyLocal(original);
            }
            catch (Exception rollbackException)
            {
                resolving = false;
                Changed?.Invoke();
                return InventoryConflictResolutionResult.Failure(
                    "Cloud resolution failed and local rollback also failed: " + rollbackException.Message);
            }

            pendingLocal = Clone(original);
            resolving = false;
            Changed?.Invoke();
            return InventoryConflictResolutionResult.Failure(exception.Message);
        }
    }

    public void Reset()
    {
        ClearPending(false);
        resolving = false;
        Changed?.Invoke();
    }

    private static InventoryData Resolve(
        InventoryConflictChoice choice,
        InventoryData local,
        InventoryData cloud)
    {
        switch (choice)
        {
            case InventoryConflictChoice.KeepLocal:
                return Clone(local);
            case InventoryConflictChoice.UseCloud:
                return Clone(cloud);
            case InventoryConflictChoice.SafeMerge:
                return SafeInventoryConflictMerger.Merge(local, cloud);
            default:
                throw new ArgumentOutOfRangeException(nameof(choice));
        }
    }

    private void ClearPending(bool notify)
    {
        pendingLocal = null;
        pendingCloud = null;
        if (notify) Changed?.Invoke();
    }

    public static InventoryData Clone(InventoryData source) =>
        InventoryData.FromSnapshot((source ?? new InventoryData()).ToSnapshot());
}

public static class GameCloudConflictSession
{
    public static CloudInventoryConflictCoordinator Current { get; } =
        new CloudInventoryConflictCoordinator();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => Current.Reset();
}

public static class SafeInventoryConflictMerger
{
    private const int MaximumHistoryEntries = 250;

    public static InventoryData Merge(InventoryData local, InventoryData cloud)
    {
        local = local ?? new InventoryData();
        cloud = cloud ?? new InventoryData();
        var merged = new InventoryData
        {
            Gold = Math.Max(local.Gold, cloud.Gold),
            LastModifiedUtcTicks = Math.Max(local.LastModifiedUtcTicks, cloud.LastModifiedUtcTicks)
        };

        MergeMaximum(local.Cards, cloud.Cards, merged.Cards);
        MergeMaximum(local.PacksOpened, cloud.PacksOpened, merged.PacksOpened);
        MergeMaximum(
            local.ProductsOpenedByLanguage,
            cloud.ProductsOpenedByLanguage,
            merged.ProductsOpenedByLanguage);
        MergeMaximum(local.ProductsOpenedBySet, cloud.ProductsOpenedBySet, merged.ProductsOpenedBySet);
        MergeMaximum(local.CardsDrawnByRarity, cloud.CardsDrawnByRarity, merged.CardsDrawnByRarity);

        foreach (string id in (local.UnseenPrintings ?? new HashSet<string>())
                     .Concat(cloud.UnseenPrintings ?? new HashSet<string>())
                     .Where(id => !string.IsNullOrWhiteSpace(id))
                     .Distinct(StringComparer.Ordinal))
        {
            if (merged.Cards.TryGetValue(id, out int count) && count > 0)
                merged.UnseenPrintings.Add(id);
        }

        var histories = new Dictionary<string, OpeningHistoryData>(StringComparer.Ordinal);
        AddHistory(histories, local.OpeningHistory);
        AddHistory(histories, cloud.OpeningHistory);
        merged.OpeningHistory = histories.Values
            .OrderByDescending(entry => entry.OpenedAtUtcTicks)
            .ThenBy(entry => entry.TransactionId, StringComparer.Ordinal)
            .Take(MaximumHistoryEntries)
            .Select(CloneHistory)
            .ToList();
        return merged;
    }

    private static void MergeMaximum(
        IDictionary<string, int> local,
        IDictionary<string, int> cloud,
        IDictionary<string, int> target)
    {
        foreach (KeyValuePair<string, int> pair in (local ?? new Dictionary<string, int>())
                     .Concat(cloud ?? new Dictionary<string, int>()))
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value < 0) continue;
            if (!target.TryGetValue(pair.Key, out int existing) || pair.Value > existing)
                target[pair.Key] = pair.Value;
        }
    }

    private static void AddHistory(
        IDictionary<string, OpeningHistoryData> target,
        IEnumerable<OpeningHistoryData> entries)
    {
        foreach (OpeningHistoryData entry in entries ?? Array.Empty<OpeningHistoryData>())
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.TransactionId)) continue;
            if (!target.ContainsKey(entry.TransactionId))
                target[entry.TransactionId] = CloneHistory(entry);
        }
    }

    private static OpeningHistoryData CloneHistory(OpeningHistoryData source)
    {
        return new OpeningHistoryData
        {
            TransactionId = source.TransactionId,
            OpenedAtUtcTicks = source.OpenedAtUtcTicks,
            ProductId = source.ProductId,
            SetId = source.SetId,
            LanguageId = source.LanguageId,
            ProfileId = source.ProfileId,
            ProductCount = source.ProductCount,
            CardCount = source.CardCount,
            NewPrintingCount = source.NewPrintingCount,
            RarityCounts = source.RarityCounts == null
                ? new Dictionary<string, int>()
                : new Dictionary<string, int>(source.RarityCounts, StringComparer.Ordinal)
        };
    }
}

internal static class InventoryDataContentComparer
{
    public static bool Equals(InventoryData left, InventoryData right)
    {
        left = left ?? new InventoryData();
        right = right ?? new InventoryData();
        return left.Gold == right.Gold &&
               DictionaryEquals(left.Cards, right.Cards) &&
               DictionaryEquals(left.PacksOpened, right.PacksOpened) &&
               SetEquals(left.UnseenPrintings, right.UnseenPrintings) &&
               DictionaryEquals(left.ProductsOpenedByLanguage, right.ProductsOpenedByLanguage) &&
               DictionaryEquals(left.ProductsOpenedBySet, right.ProductsOpenedBySet) &&
               DictionaryEquals(left.CardsDrawnByRarity, right.CardsDrawnByRarity) &&
               HistoryEquals(left.OpeningHistory, right.OpeningHistory);
    }

    private static bool DictionaryEquals(
        IDictionary<string, int> left,
        IDictionary<string, int> right)
    {
        left = left ?? new Dictionary<string, int>();
        right = right ?? new Dictionary<string, int>();
        return left.Count == right.Count && left.All(pair =>
            right.TryGetValue(pair.Key, out int value) && value == pair.Value);
    }

    private static bool SetEquals(ISet<string> left, ISet<string> right)
    {
        left = left ?? new HashSet<string>();
        right = right ?? new HashSet<string>();
        return left.SetEquals(right);
    }

    private static bool HistoryEquals(
        IEnumerable<OpeningHistoryData> left,
        IEnumerable<OpeningHistoryData> right)
    {
        OpeningHistoryData[] leftEntries = (left ?? Array.Empty<OpeningHistoryData>())
            .OrderBy(entry => entry?.TransactionId, StringComparer.Ordinal)
            .ToArray();
        OpeningHistoryData[] rightEntries = (right ?? Array.Empty<OpeningHistoryData>())
            .OrderBy(entry => entry?.TransactionId, StringComparer.Ordinal)
            .ToArray();
        if (leftEntries.Length != rightEntries.Length) return false;
        for (int index = 0; index < leftEntries.Length; index++)
        {
            OpeningHistoryData a = leftEntries[index];
            OpeningHistoryData b = rightEntries[index];
            if (a == null || b == null)
            {
                if (a != b) return false;
                continue;
            }
            if (!string.Equals(a.TransactionId, b.TransactionId, StringComparison.Ordinal) ||
                a.OpenedAtUtcTicks != b.OpenedAtUtcTicks ||
                !string.Equals(a.ProductId, b.ProductId, StringComparison.Ordinal) ||
                !string.Equals(a.SetId, b.SetId, StringComparison.Ordinal) ||
                !string.Equals(a.LanguageId, b.LanguageId, StringComparison.Ordinal) ||
                !string.Equals(a.ProfileId, b.ProfileId, StringComparison.Ordinal) ||
                a.ProductCount != b.ProductCount || a.CardCount != b.CardCount ||
                a.NewPrintingCount != b.NewPrintingCount ||
                !DictionaryEquals(a.RarityCounts, b.RarityCounts))
            {
                return false;
            }
        }
        return true;
    }
}
