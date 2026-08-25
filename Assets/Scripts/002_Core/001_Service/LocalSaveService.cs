using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public enum LocalSaveLoadStatus
{
    Empty,
    Loaded,
    Migrated,
    RecoveredFromBackup,
    Unrecoverable
}

public enum LocalSaveWriteStage
{
    TemporaryDurable,
    BackupCreated,
    ActiveReplaced
}

public sealed class LocalSaveLoadResult
{
    internal LocalSaveLoadResult(
        InventoryData data,
        LocalSaveLoadStatus status,
        string diagnosticCode,
        long revision)
    {
        Data = data ?? new InventoryData();
        Status = status;
        DiagnosticCode = diagnosticCode ?? "SAVE_UNKNOWN";
        Revision = revision;
    }

    public InventoryData Data { get; }
    public LocalSaveLoadStatus Status { get; }
    public string DiagnosticCode { get; }
    public long Revision { get; }
    public bool CanSave => Status != LocalSaveLoadStatus.Unrecoverable;
}

public sealed class LocalSaveException : Exception
{
    public LocalSaveException(string diagnosticCode, string message)
        : base(message)
    {
        DiagnosticCode = diagnosticCode;
    }

    public LocalSaveException(string diagnosticCode, string message, Exception innerException)
        : base(message, innerException)
    {
        DiagnosticCode = diagnosticCode;
    }

    public string DiagnosticCode { get; }
}

public static class LocalSaveService
{
    public const int SaveSchemaVersion = 1;
    public const int ContentSchemaVersion = 2;
    public const int RuleSchemaVersion = 1;
    public const string SaveNamespace = "universal-gacha-simulator/local-save";
    public const int MaximumSaveBytes = 16 * 1024 * 1024;

    private const string FileName = "save.json";
    private const string TemporarySuffix = ".tmp";
    private const string BackupSuffix = ".backup";
    private const string CorruptSuffix = ".corrupt";
    private static bool automaticSaveEnabled = true;

    public static bool IsAutomaticSaveEnabled => automaticSaveEnabled;

    public static void Save(InventoryData data)
    {
        if (data == null || !automaticSaveEnabled)
        {
            if (!automaticSaveEnabled)
            {
                LocalDiagnosticLogStore.TryAppendDefault(
                    "save",
                    "SAVE_WRITE_BLOCKED",
                    "Automatic local save remains disabled until an explicit recovery succeeds.");
            }
            return;
        }

        try
        {
            SaveToPath(DefaultPath(), data);
        }
        catch (LocalSaveException exception)
        {
            LocalDiagnosticLogStore.TryAppendDefault(
                "save",
                exception.DiagnosticCode,
                "The local save could not be written safely.");
            Debug.LogWarning($"Local save was not written. Diagnostic code: {exception.DiagnosticCode}.");
        }
        catch (Exception)
        {
            LocalDiagnosticLogStore.TryAppendDefault(
                "save",
                "SAVE_WRITE_IO_FAILED",
                "The local save could not be written safely.");
            Debug.LogWarning("Local save was not written. Diagnostic code: SAVE_WRITE_IO_FAILED.");
        }
    }

    public static InventoryData Load()
    {
        return LoadDetailed().Data;
    }

    public static LocalSaveLoadResult LoadDetailed()
    {
        LocalSaveLoadResult result = LoadFromPath(DefaultPath());
        automaticSaveEnabled = result.CanSave;
        if (!result.CanSave)
        {
            LocalDiagnosticLogStore.TryAppendDefault(
                "save",
                result.DiagnosticCode,
                "The local save could not be validated; automatic overwrite is disabled.");
            Debug.LogWarning($"Local save validation failed. Diagnostic code: {result.DiagnosticCode}.");
        }
        return result;
    }

    public static LocalSaveLoadResult LoadFromPath(string path)
    {
        ValidatePath(path);
        string fullPath = Path.GetFullPath(path);
        try
        {
            RecoverInterruptedWrite(fullPath);
        }
        catch (Exception)
        {
            return Unrecoverable("SAVE_INTERRUPTED_RECOVERY_FAILED");
        }

        if (!File.Exists(fullPath))
            return new LocalSaveLoadResult(new InventoryData(), LocalSaveLoadStatus.Empty, "SAVE_EMPTY", 0);

        try
        {
            ParsedSave active = ReadAndValidate(fullPath);
            return new LocalSaveLoadResult(
                active.Data,
                active.RequiresMigration ? LocalSaveLoadStatus.Migrated : LocalSaveLoadStatus.Loaded,
                active.RequiresMigration ? "SAVE_MIGRATION_REQUIRED" : "SAVE_LOADED",
                active.Revision);
        }
        catch (LocalSaveException activeException)
        {
            string backupPath = fullPath + BackupSuffix;
            if (!File.Exists(backupPath))
                return Unrecoverable(activeException.DiagnosticCode);

            try
            {
                ParsedSave backup = ReadAndValidate(backupPath);
                QuarantineAndRestore(fullPath, backupPath);
                LocalDiagnosticLogStore.TryAppendDefault(
                    "save",
                    "SAVE_BACKUP_RESTORED",
                    "The active local save failed validation and the last verified backup was restored.");
                return new LocalSaveLoadResult(
                    backup.Data,
                    LocalSaveLoadStatus.RecoveredFromBackup,
                    "SAVE_BACKUP_RESTORED",
                    backup.Revision);
            }
            catch (Exception)
            {
                return Unrecoverable("SAVE_ACTIVE_AND_BACKUP_INVALID");
            }
        }
        catch (Exception)
        {
            return Unrecoverable("SAVE_READ_IO_FAILED");
        }
    }

    public static long SaveToPath(string path, InventoryData data)
    {
        return SaveToPath(path, data, null);
    }

    public static long SaveToPath(
        string path,
        InventoryData data,
        Action<LocalSaveWriteStage> afterStage)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        ValidatePath(path);
        string fullPath = Path.GetFullPath(path);
        LocalSaveLoadResult existing = LoadFromPath(fullPath);
        if (!existing.CanSave)
        {
            throw new LocalSaveException(
                "SAVE_OVERWRITE_BLOCKED",
                "An invalid local save must be recovered before it can be replaced.");
        }
        if (existing.Revision == long.MaxValue)
            throw new LocalSaveException("SAVE_REVISION_EXHAUSTED", "The local save revision cannot advance.");

        long revision = existing.Revision + 1;
        InventorySnapshot snapshot = InventorySnapshotMigrator.Migrate(data.ToSnapshot());
        InventorySnapshotValidator.Validate(snapshot);
        string payloadJson = JsonConvert.SerializeObject(snapshot, Formatting.None);
        byte[] payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
        var envelope = new LocalSaveEnvelope
        {
            SaveSchemaVersion = SaveSchemaVersion,
            Namespace = SaveNamespace,
            Revision = revision,
            SavedAtUtcTicks = DateTime.UtcNow.Ticks,
            ContentSchemaVersion = ContentSchemaVersion,
            RuleSchemaVersion = RuleSchemaVersion,
            PayloadVersion = snapshot.Version,
            PayloadSha256 = Sha256(payloadBytes),
            PayloadJson = payloadJson,
            // Keep a top-level v4 mirror so the previous application version can
            // still deserialize save.json during an application rollback.
            Version = snapshot.Version,
            Cards = snapshot.Cards,
            PacksOpened = snapshot.PacksOpened,
            UnseenPrintings = snapshot.UnseenPrintings,
            OpeningHistory = snapshot.OpeningHistory,
            ProductsOpenedByLanguage = snapshot.ProductsOpenedByLanguage,
            ProductsOpenedBySet = snapshot.ProductsOpenedBySet,
            CardsDrawnByRarity = snapshot.CardsDrawnByRarity,
            Gold = snapshot.Gold,
            LastModifiedUtcTicks = snapshot.LastModifiedUtcTicks
        };
        string envelopeJson = JsonConvert.SerializeObject(envelope, Formatting.Indented);
        if (Encoding.UTF8.GetByteCount(envelopeJson) > MaximumSaveBytes)
        {
            throw new LocalSaveException(
                "SAVE_SIZE_LIMIT_EXCEEDED",
                "The local save exceeds the safe size limit.");
        }

        WriteAtomic(fullPath, envelopeJson, afterStage);
        return revision;
    }

    public static void ResumeAutomaticSaveAfterRecovery()
    {
        automaticSaveEnabled = true;
    }

    internal static void WriteAtomic(string path, string text)
    {
        WriteAtomic(path, text, null);
    }

    public static void WriteAtomic(
        string path,
        string text,
        Action<LocalSaveWriteStage> afterStage)
    {
        ValidatePath(path);
        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        string temporaryPath = fullPath + TemporarySuffix;
        string backupPath = fullPath + BackupSuffix;
        bool hadActive = File.Exists(fullPath);
        bool backupCreated = false;
        bool activeReplaced = false;

        try
        {
            DeleteExactFileIfPresent(temporaryPath);
            WriteDurable(temporaryPath, text ?? string.Empty);
            afterStage?.Invoke(LocalSaveWriteStage.TemporaryDurable);

            if (hadActive)
            {
                DeleteExactFileIfPresent(backupPath);
                File.Move(fullPath, backupPath);
                backupCreated = true;
                afterStage?.Invoke(LocalSaveWriteStage.BackupCreated);
            }

            File.Move(temporaryPath, fullPath);
            activeReplaced = true;
            afterStage?.Invoke(LocalSaveWriteStage.ActiveReplaced);
            // Keep the previous verified active file as the rollback backup.
        }
        catch
        {
            try
            {
                if (hadActive && backupCreated && File.Exists(backupPath))
                    File.Copy(backupPath, fullPath, true);
                else if (!hadActive && activeReplaced && File.Exists(fullPath))
                    File.Delete(fullPath);
            }
            finally
            {
                DeleteExactFileIfPresent(temporaryPath);
            }
            throw;
        }
    }

    private static ParsedSave ReadAndValidate(string path)
    {
        long length = new FileInfo(path).Length;
        if (length <= 0 || length > MaximumSaveBytes)
            throw new LocalSaveException("SAVE_SIZE_INVALID", "The local save size is invalid.");

        JObject root;
        try
        {
            root = JObject.Parse(File.ReadAllText(path, Encoding.UTF8));
        }
        catch (Exception exception)
        {
            throw new LocalSaveException("SAVE_JSON_INVALID", "The local save is not valid JSON.", exception);
        }

        if (root.TryGetValue(nameof(LocalSaveEnvelope.SaveSchemaVersion), StringComparison.Ordinal, out _))
            return ReadEnvelope(root);
        if (root.TryGetValue(nameof(InventorySnapshot.Version), StringComparison.Ordinal, out _))
            return ReadRawSnapshot(root);
        if (root.TryGetValue(nameof(LegacyInventoryData.Gold), StringComparison.Ordinal, out JToken goldToken) &&
            goldToken.Type == JTokenType.Integer)
        {
            int gold = goldToken.Value<int>();
            if (gold < 0)
                throw new LocalSaveException("SAVE_LEGACY_INVALID", "The legacy save contains invalid currency.");
            return new ParsedSave(new InventoryData { Gold = gold }, 0, true);
        }

        throw new LocalSaveException("SAVE_FORMAT_UNKNOWN", "The local save format is not recognized.");
    }

    private static ParsedSave ReadEnvelope(JObject root)
    {
        LocalSaveEnvelope envelope;
        try
        {
            envelope = root.ToObject<LocalSaveEnvelope>();
        }
        catch (Exception exception)
        {
            throw new LocalSaveException("SAVE_ENVELOPE_INVALID", "The local save envelope is invalid.", exception);
        }
        if (envelope == null || envelope.SaveSchemaVersion != SaveSchemaVersion)
            throw new LocalSaveException("SAVE_SCHEMA_UNSUPPORTED", "The local save schema is not supported.");
        if (!string.Equals(envelope.Namespace, SaveNamespace, StringComparison.Ordinal))
            throw new LocalSaveException("SAVE_NAMESPACE_MISMATCH", "The local save belongs to another namespace.");
        if (envelope.Revision <= 0 || envelope.SavedAtUtcTicks <= 0 ||
            envelope.SavedAtUtcTicks > DateTime.MaxValue.Ticks)
        {
            throw new LocalSaveException("SAVE_METADATA_INVALID", "The local save metadata is invalid.");
        }
        if (envelope.ContentSchemaVersion != ContentSchemaVersion ||
            envelope.RuleSchemaVersion != RuleSchemaVersion)
        {
            throw new LocalSaveException("SAVE_COMPATIBILITY_UNSUPPORTED", "The local save compatibility contract is not supported.");
        }
        if (envelope.PayloadVersion < InventorySnapshotMigrator.MinimumSupportedVersion ||
            envelope.PayloadVersion > InventorySnapshotMigrator.CurrentVersion ||
            string.IsNullOrWhiteSpace(envelope.PayloadJson))
        {
            throw new LocalSaveException("SAVE_PAYLOAD_VERSION_UNSUPPORTED", "The local save payload version is not supported.");
        }

        byte[] payloadBytes = Encoding.UTF8.GetBytes(envelope.PayloadJson);
        if (!FixedTimeEquals(envelope.PayloadSha256, Sha256(payloadBytes)))
            throw new LocalSaveException("SAVE_CHECKSUM_MISMATCH", "The local save checksum does not match its payload.");

        InventorySnapshot legacyMirror;
        try
        {
            legacyMirror = root.ToObject<InventorySnapshot>();
            string mirrorJson = JsonConvert.SerializeObject(legacyMirror, Formatting.None);
            if (!FixedTimeEquals(envelope.PayloadSha256, Sha256(Encoding.UTF8.GetBytes(mirrorJson))))
            {
                throw new LocalSaveException(
                    "SAVE_LEGACY_MIRROR_MISMATCH",
                    "The rollback-compatible save mirror does not match its verified payload.");
            }
        }
        catch (LocalSaveException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new LocalSaveException(
                "SAVE_LEGACY_MIRROR_INVALID",
                "The rollback-compatible save mirror is invalid.",
                exception);
        }

        InventorySnapshot snapshot;
        try
        {
            snapshot = JsonConvert.DeserializeObject<InventorySnapshot>(envelope.PayloadJson);
        }
        catch (Exception exception)
        {
            throw new LocalSaveException("SAVE_PAYLOAD_INVALID", "The local save payload is invalid.", exception);
        }
        if (snapshot == null || snapshot.Version != envelope.PayloadVersion)
            throw new LocalSaveException("SAVE_PAYLOAD_VERSION_MISMATCH", "The local save payload version does not match its envelope.");
        try
        {
            int sourceVersion = snapshot.Version;
            InventoryData data = InventoryData.FromSnapshot(snapshot);
            return new ParsedSave(data, envelope.Revision, sourceVersion != InventorySnapshotMigrator.CurrentVersion);
        }
        catch (Exception exception)
        {
            throw new LocalSaveException("SAVE_PAYLOAD_UNSUPPORTED", "The local save payload cannot be migrated.", exception);
        }
    }

    private static ParsedSave ReadRawSnapshot(JObject root)
    {
        try
        {
            InventorySnapshot snapshot = root.ToObject<InventorySnapshot>();
            InventoryData data = InventoryData.FromSnapshot(snapshot);
            // Any raw snapshot is migrated to the envelope on its next save.
            return new ParsedSave(data, 0, true);
        }
        catch (Exception exception)
        {
            throw new LocalSaveException("SAVE_SNAPSHOT_UNSUPPORTED", "The inventory snapshot cannot be migrated.", exception);
        }
    }

    private static void RecoverInterruptedWrite(string path)
    {
        string temporaryPath = path + TemporarySuffix;
        string backupPath = path + BackupSuffix;
        if (!File.Exists(path) && File.Exists(backupPath))
            File.Copy(backupPath, path, false);
        DeleteExactFileIfPresent(temporaryPath);
    }

    private static void QuarantineAndRestore(string path, string backupPath)
    {
        string corruptPath = path + CorruptSuffix;
        if (File.Exists(corruptPath))
            corruptPath += "." + DateTime.UtcNow.Ticks;
        File.Move(path, corruptPath);
        try
        {
            File.Copy(backupPath, path, false);
        }
        catch
        {
            if (!File.Exists(path) && File.Exists(corruptPath))
                File.Move(corruptPath, path);
            throw;
        }
    }

    private static void WriteDurable(string path, string text)
    {
        byte[] bytes = new UTF8Encoding(false).GetBytes(text);
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(true);
        }
    }

    private static void DeleteExactFileIfPresent(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private static LocalSaveLoadResult Unrecoverable(string code)
    {
        return new LocalSaveLoadResult(new InventoryData(), LocalSaveLoadStatus.Unrecoverable, code, 0);
    }

    private static string DefaultPath()
    {
        return Path.Combine(Application.persistentDataPath, FileName);
    }

    private static void ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A save path is required.", nameof(path));
    }

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

    [Serializable]
    private sealed class LocalSaveEnvelope
    {
        public int SaveSchemaVersion;
        public string Namespace;
        public long Revision;
        public long SavedAtUtcTicks;
        public int ContentSchemaVersion;
        public int RuleSchemaVersion;
        public int PayloadVersion;
        public string PayloadSha256;
        public string PayloadJson;
        public int Version;
        public List<InventoryEntry> Cards;
        public List<InventoryEntry> PacksOpened;
        public List<string> UnseenPrintings;
        public List<OpeningHistorySnapshot> OpeningHistory;
        public List<InventoryEntry> ProductsOpenedByLanguage;
        public List<InventoryEntry> ProductsOpenedBySet;
        public List<InventoryEntry> CardsDrawnByRarity;
        public int Gold;
        public long LastModifiedUtcTicks;
    }

    [Serializable]
    private sealed class LegacyInventoryData
    {
        public int Gold;
    }

    private sealed class ParsedSave
    {
        public ParsedSave(InventoryData data, long revision, bool requiresMigration)
        {
            Data = data;
            Revision = revision;
            RequiresMigration = requiresMigration;
        }

        public InventoryData Data { get; }
        public long Revision { get; }
        public bool RequiresMigration { get; }
    }
}
