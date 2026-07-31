using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Gacha.Application;
using Gacha.Domain;
using Newtonsoft.Json;
using UnityEngine;

public sealed class InventoryRecoveryException : Exception
{
    public InventoryRecoveryException(string message) : base(message) { }
    public InventoryRecoveryException(string message, Exception innerException) : base(message, innerException) { }
}

public sealed class PlayerRecoveryState
{
    public PlayerRecoveryState(
        InventoryData inventory,
        LanguagePreferences languages,
        ExperienceSettings experience)
    {
        Inventory = InventoryData.FromSnapshot((inventory ?? new InventoryData()).ToSnapshot());
        Languages = languages ?? new LanguagePreferences("en", "en");
        Experience = experience ?? new ExperienceSettings();
    }

    public InventoryData Inventory { get; }
    public LanguagePreferences Languages { get; }
    public ExperienceSettings Experience { get; }
}

public sealed class InventoryRecoveryPreview
{
    internal InventoryRecoveryPreview(
        int schemaVersion,
        DateTime createdAtUtc,
        string sourceInstallId,
        string payloadSha256,
        PlayerRecoveryState state)
    {
        SchemaVersion = schemaVersion;
        CreatedAtUtc = createdAtUtc;
        SourceInstallId = sourceInstallId;
        PayloadSha256 = payloadSha256;
        State = state;
        DistinctPrintingCount = state.Inventory.Cards.Count;
        TotalCardCount = state.Inventory.Cards.Values.Sum(value => (long)value);
        TotalProductsOpened = state.Inventory.PacksOpened.Values.Sum(value => (long)value);
        HistoryCount = state.Inventory.OpeningHistory.Count;
    }

    public int SchemaVersion { get; }
    public DateTime CreatedAtUtc { get; }
    public string SourceInstallId { get; }
    public string PayloadSha256 { get; }
    public int DistinctPrintingCount { get; }
    public long TotalCardCount { get; }
    public long TotalProductsOpened { get; }
    public int HistoryCount { get; }
    public string UiLanguageId => State.Languages.UiLanguageId;
    public string ContentLanguageId => State.Languages.ContentLanguageId;
    internal PlayerRecoveryState State { get; }
}

public interface IPlayerRecoveryTarget
{
    PlayerRecoveryState Capture();
    void Apply(PlayerRecoveryState state);
}

public sealed class InventoryRecoveryImportResult
{
    internal InventoryRecoveryImportResult(InventoryRecoveryPreview preview, string backupPath)
    {
        Preview = preview;
        BackupPath = backupPath;
    }

    public InventoryRecoveryPreview Preview { get; }
    public string BackupPath { get; }
}

public sealed class InventoryRecoveryService
{
    public const int SchemaVersion = 1;
    public const string SaveNamespace = "universal-gacha-simulator/player-state";
    public const int MaximumEnvelopeBytes = 16 * 1024 * 1024;
    private const int MaximumPayloadBytes = 12 * 1024 * 1024;
    private const int MaximumIdLength = 512;
    private const int MaximumInventoryEntries = 200000;
    private const int MaximumCounterEntries = 10000;
    private const int MaximumHistoryEntries = 250;

    private readonly Func<DateTime> utcNow;

    public InventoryRecoveryService(Func<DateTime> utcNow = null)
    {
        this.utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public InventoryRecoveryPreview Export(
        string path,
        PlayerRecoveryState state,
        string sourceInstallId)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("An export path is required.", nameof(path));
        ValidateInstallId(sourceInstallId);
        RecoveryPayloadDto payload = CreatePayload(state);
        string payloadJson = JsonConvert.SerializeObject(payload, Formatting.None);
        byte[] payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
        if (payloadBytes.Length > MaximumPayloadBytes)
            throw new InventoryRecoveryException("The player save is too large to export safely.");

        DateTime createdAtUtc = utcNow().ToUniversalTime();
        string sha256 = Sha256(payloadBytes);
        var envelope = new RecoveryEnvelopeDto
        {
            SchemaVersion = SchemaVersion,
            Namespace = SaveNamespace,
            CreatedAtUtcTicks = createdAtUtc.Ticks,
            SourceInstallId = sourceInstallId.Trim(),
            PayloadSha256 = sha256,
            PayloadJson = payloadJson
        };
        string envelopeJson = JsonConvert.SerializeObject(envelope, Formatting.Indented);
        if (Encoding.UTF8.GetByteCount(envelopeJson) > MaximumEnvelopeBytes)
            throw new InventoryRecoveryException("The recovery envelope exceeds the safe size limit.");
        LocalSaveService.WriteAtomic(path, envelopeJson);
        return CreatePreview(envelope, payload);
    }

    public InventoryRecoveryPreview Preview(string path)
    {
        return Read(path).Preview;
    }

    public InventoryRecoveryImportResult Restore(
        string importPath,
        string backupPath,
        IPlayerRecoveryTarget target,
        string currentInstallId)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        if (string.IsNullOrWhiteSpace(backupPath))
            throw new ArgumentException("A pre-import backup path is required.", nameof(backupPath));
        ReadResult incoming = Read(importPath);
        PlayerRecoveryState original = target.Capture() ?? new PlayerRecoveryState(null, null, null);
        Export(backupPath, original, currentInstallId);

        try
        {
            incoming.Preview.State.Inventory.Touch();
            target.Apply(incoming.Preview.State);
            return new InventoryRecoveryImportResult(incoming.Preview, Path.GetFullPath(backupPath));
        }
        catch (Exception applyException)
        {
            try
            {
                target.Apply(original);
            }
            catch (Exception rollbackException)
            {
                throw new InventoryRecoveryException(
                    $"Import failed and automatic rollback also failed. The pre-import backup remains at '{backupPath}'.",
                    new AggregateException(applyException, rollbackException));
            }

            throw new InventoryRecoveryException(
                "Import failed; the original player state was restored.",
                applyException);
        }
    }

    private static ReadResult Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("An import path is required.", nameof(path));
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new InventoryRecoveryException("The selected recovery file does not exist.");
        long length = new FileInfo(fullPath).Length;
        if (length <= 0 || length > MaximumEnvelopeBytes)
            throw new InventoryRecoveryException("The recovery file size is invalid.");

        RecoveryEnvelopeDto envelope;
        try
        {
            envelope = JsonConvert.DeserializeObject<RecoveryEnvelopeDto>(
                File.ReadAllText(fullPath, Encoding.UTF8),
                new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error });
        }
        catch (Exception exception)
        {
            throw new InventoryRecoveryException("The recovery envelope is not valid JSON.", exception);
        }
        if (envelope == null || envelope.SchemaVersion != SchemaVersion)
            throw new InventoryRecoveryException("The recovery schema is not supported.");
        if (!string.Equals(envelope.Namespace, SaveNamespace, StringComparison.Ordinal))
            throw new InventoryRecoveryException("This recovery file belongs to another application namespace.");
        ValidateInstallId(envelope.SourceInstallId);
        if (envelope.CreatedAtUtcTicks <= 0 || envelope.CreatedAtUtcTicks > DateTime.MaxValue.Ticks)
            throw new InventoryRecoveryException("The recovery timestamp is invalid.");
        if (string.IsNullOrWhiteSpace(envelope.PayloadJson))
            throw new InventoryRecoveryException("The recovery payload is missing.");
        byte[] payloadBytes = Encoding.UTF8.GetBytes(envelope.PayloadJson);
        if (payloadBytes.Length > MaximumPayloadBytes)
            throw new InventoryRecoveryException("The recovery payload exceeds the safe size limit.");
        string actualSha256 = Sha256(payloadBytes);
        if (!FixedTimeEquals(envelope.PayloadSha256, actualSha256))
            throw new InventoryRecoveryException("The recovery checksum does not match its payload.");

        RecoveryPayloadDto payload;
        try
        {
            payload = JsonConvert.DeserializeObject<RecoveryPayloadDto>(
                envelope.PayloadJson,
                new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error });
        }
        catch (Exception exception)
        {
            throw new InventoryRecoveryException("The recovery payload is not valid.", exception);
        }
        ValidatePayload(payload);
        return new ReadResult(CreatePreview(envelope, payload));
    }

    private static RecoveryPayloadDto CreatePayload(PlayerRecoveryState state)
    {
        state = state ?? new PlayerRecoveryState(null, null, null);
        InventorySnapshot snapshot = state.Inventory.ToSnapshot();
        snapshot.Cards = SortEntries(snapshot.Cards);
        snapshot.PacksOpened = SortEntries(snapshot.PacksOpened);
        snapshot.UnseenPrintings = snapshot.UnseenPrintings
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
        snapshot.ProductsOpenedByLanguage = SortEntries(snapshot.ProductsOpenedByLanguage);
        snapshot.ProductsOpenedBySet = SortEntries(snapshot.ProductsOpenedBySet);
        snapshot.CardsDrawnByRarity = SortEntries(snapshot.CardsDrawnByRarity);
        foreach (OpeningHistorySnapshot history in snapshot.OpeningHistory)
            history.RarityCounts = SortEntries(history.RarityCounts);

        var payload = new RecoveryPayloadDto
        {
            PayloadVersion = 1,
            Inventory = snapshot,
            UiLanguageId = state.Languages.UiLanguageId,
            ContentLanguageId = state.Languages.ContentLanguageId,
            SoundEnabled = state.Experience.SoundEnabled,
            ReduceMotion = state.Experience.ReduceMotion,
            HapticsEnabled = state.Experience.HapticsEnabled,
            AnimationSpeed = state.Experience.AnimationSpeed
        };
        ValidatePayload(payload);
        return payload;
    }

    private static InventoryRecoveryPreview CreatePreview(
        RecoveryEnvelopeDto envelope,
        RecoveryPayloadDto payload)
    {
        PlayerRecoveryState state = ToState(payload);
        return new InventoryRecoveryPreview(
            envelope.SchemaVersion,
            new DateTime(envelope.CreatedAtUtcTicks, DateTimeKind.Utc),
            envelope.SourceInstallId,
            envelope.PayloadSha256,
            state);
    }

    private static PlayerRecoveryState ToState(RecoveryPayloadDto payload)
    {
        return new PlayerRecoveryState(
            InventoryData.FromSnapshot(payload.Inventory),
            new LanguagePreferences(payload.UiLanguageId, payload.ContentLanguageId),
            new ExperienceSettings(
                payload.SoundEnabled,
                payload.ReduceMotion,
                payload.HapticsEnabled,
                payload.AnimationSpeed));
    }

    private static void ValidatePayload(RecoveryPayloadDto payload)
    {
        if (payload == null || payload.PayloadVersion != 1)
            throw new InventoryRecoveryException("The recovery payload version is not supported.");
        ValidateLanguageId(payload.UiLanguageId, "interface");
        ValidateLanguageId(payload.ContentLanguageId, "card");
        if (float.IsNaN(payload.AnimationSpeed) || float.IsInfinity(payload.AnimationSpeed) ||
            payload.AnimationSpeed < 0.5f || payload.AnimationSpeed > 2f)
        {
            throw new InventoryRecoveryException("The saved animation speed is invalid.");
        }
        ValidateInventory(payload.Inventory);
    }

    private static void ValidateInventory(InventorySnapshot snapshot)
    {
        if (snapshot == null || snapshot.Version < 2 || snapshot.Version > 4)
            throw new InventoryRecoveryException("The inventory snapshot version is not supported.");
        if (snapshot.Gold < 0)
            throw new InventoryRecoveryException("The inventory contains negative currency.");
        ValidateEntries(snapshot.Cards, MaximumInventoryEntries, "card", false);
        ValidateEntries(snapshot.PacksOpened, MaximumCounterEntries, "product", false);
        ValidateEntries(snapshot.ProductsOpenedByLanguage, MaximumCounterEntries, "language statistic", false);
        ValidateEntries(snapshot.ProductsOpenedBySet, MaximumCounterEntries, "set statistic", false);
        ValidateEntries(snapshot.CardsDrawnByRarity, MaximumCounterEntries, "rarity statistic", false);

        if (snapshot.UnseenPrintings == null || snapshot.UnseenPrintings.Count > MaximumInventoryEntries)
            throw new InventoryRecoveryException("The unseen-card list is invalid.");
        var cardCounts = snapshot.Cards.ToDictionary(entry => entry.Id, entry => entry.Amount, StringComparer.Ordinal);
        if (snapshot.UnseenPrintings.Any(id => !ValidId(id) ||
            !cardCounts.TryGetValue(id, out int count) || count <= 0) ||
            snapshot.UnseenPrintings.Distinct(StringComparer.Ordinal).Count() != snapshot.UnseenPrintings.Count)
        {
            throw new InventoryRecoveryException("The unseen-card list references invalid inventory entries.");
        }

        if (snapshot.OpeningHistory == null || snapshot.OpeningHistory.Count > MaximumHistoryEntries)
            throw new InventoryRecoveryException("The opening history is invalid.");
        var transactionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (OpeningHistorySnapshot entry in snapshot.OpeningHistory)
        {
            if (entry == null || !ValidId(entry.TransactionId) || !transactionIds.Add(entry.TransactionId) ||
                !ValidId(entry.ProductId) || !ValidId(entry.SetId) || !ValidId(entry.LanguageId) ||
                !ValidId(entry.ProfileId) || entry.OpenedAtUtcTicks <= 0 ||
                entry.OpenedAtUtcTicks > DateTime.MaxValue.Ticks || entry.ProductCount <= 0 ||
                entry.ProductCount > 10 || entry.CardCount <= 0 || entry.NewPrintingCount < 0 ||
                entry.NewPrintingCount > entry.CardCount)
            {
                throw new InventoryRecoveryException("The opening history contains an invalid entry.");
            }
            ValidateEntries(entry.RarityCounts, MaximumCounterEntries, "history rarity", true);
        }
    }

    private static void ValidateEntries(
        List<InventoryEntry> entries,
        int maximumCount,
        string label,
        bool requirePositive)
    {
        if (entries == null || entries.Count > maximumCount)
            throw new InventoryRecoveryException($"The {label} entry count is invalid.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (InventoryEntry entry in entries)
        {
            if (entry == null || !ValidId(entry.Id) || !ids.Add(entry.Id) ||
                entry.Amount < 0 || requirePositive && entry.Amount == 0)
            {
                throw new InventoryRecoveryException($"The {label} data contains an invalid entry.");
            }
        }
    }

    private static void ValidateInstallId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 128)
            throw new InventoryRecoveryException("The source install id is invalid.");
    }

    private static void ValidateLanguageId(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 32 || value.Any(character =>
            !char.IsLetterOrDigit(character) && character != '-' && character != '_'))
        {
            throw new InventoryRecoveryException($"The {label} language id is invalid.");
        }
    }

    private static bool ValidId(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= MaximumIdLength;

    private static List<InventoryEntry> SortEntries(IEnumerable<InventoryEntry> entries) =>
        (entries ?? Array.Empty<InventoryEntry>())
            .Where(entry => entry != null)
            .OrderBy(entry => entry.Id, StringComparer.Ordinal)
            .Select(entry => new InventoryEntry(entry.Id, entry.Amount))
            .ToList();

    private static string Sha256(byte[] bytes)
    {
        using (SHA256 algorithm = SHA256.Create())
            return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
    }

    private static bool FixedTimeEquals(string expected, string actual)
    {
        if (string.IsNullOrWhiteSpace(expected) || expected.Length != actual.Length)
            return false;
        int difference = 0;
        for (int index = 0; index < actual.Length; index++)
            difference |= char.ToLowerInvariant(expected[index]) ^ actual[index];
        return difference == 0;
    }

    private sealed class ReadResult
    {
        public ReadResult(InventoryRecoveryPreview preview) => Preview = preview;
        public InventoryRecoveryPreview Preview { get; }
    }

    [Serializable]
    private sealed class RecoveryEnvelopeDto
    {
        public int SchemaVersion;
        public string Namespace;
        public long CreatedAtUtcTicks;
        public string SourceInstallId;
        public string PayloadSha256;
        public string PayloadJson;
    }

    [Serializable]
    private sealed class RecoveryPayloadDto
    {
        public int PayloadVersion;
        public InventorySnapshot Inventory;
        public string UiLanguageId;
        public string ContentLanguageId;
        public bool SoundEnabled;
        public bool ReduceMotion;
        public bool HapticsEnabled;
        public float AnimationSpeed;
    }
}

public sealed class UnityPlayerRecoveryTarget : IPlayerRecoveryTarget
{
    private readonly Inventory inventory;
    private readonly Action<InventoryData> saveInventory;
    private readonly LanguageSelectionService languages;
    private readonly ExperienceSettingsService experience;
    private readonly UniversalCatalog catalog;

    public UnityPlayerRecoveryTarget(
        Inventory inventory,
        Action<InventoryData> saveInventory,
        LanguageSelectionService languages,
        ExperienceSettingsService experience,
        UniversalCatalog catalog)
    {
        this.inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        this.saveInventory = saveInventory ?? throw new ArgumentNullException(nameof(saveInventory));
        this.languages = languages ?? throw new ArgumentNullException(nameof(languages));
        this.experience = experience ?? throw new ArgumentNullException(nameof(experience));
        this.catalog = catalog;
    }

    public PlayerRecoveryState Capture()
    {
        return new PlayerRecoveryState(
            inventory.Data,
            new LanguagePreferences(languages.UiLanguageId, languages.RequestedContentLanguageId),
            experience.Current);
    }

    public void Apply(PlayerRecoveryState state)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        InventoryData nextInventory = InventoryData.FromSnapshot(state.Inventory.ToSnapshot());
        inventory.ReplaceData(nextInventory);
        saveInventory(nextInventory);
        languages.ApplyPreferences(state.Languages, catalog);
        ExperienceSettingsUpdateResult result = experience.Apply(state.Experience);
        if (!result.Succeeded)
            throw new InvalidOperationException(result.ErrorMessage);
    }
}

public static class RecoveryInstallIdentity
{
    private const string PlayerPrefsKey = "universal_gacha.recovery_install_id.v1";

    public static string GetOrCreate()
    {
        string existing = PlayerPrefs.GetString(PlayerPrefsKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(existing))
            return existing;
        string created = Guid.NewGuid().ToString("N");
        PlayerPrefs.SetString(PlayerPrefsKey, created);
        PlayerPrefs.Save();
        return created;
    }
}
