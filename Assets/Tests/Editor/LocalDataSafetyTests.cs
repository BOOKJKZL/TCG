using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

public sealed class LocalSaveServiceTests
{
    private string root;

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), "gacha-local-save-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    [Test]
    public void LoadFromPath_MigratesGoldOnlyLegacySave()
    {
        string path = Path.Combine(root, "save.json");
        File.WriteAllText(path, "{\"Gold\":37}");

        LocalSaveLoadResult result = LocalSaveService.LoadFromPath(path);

        Assert.That(result.Status, Is.EqualTo(LocalSaveLoadStatus.Migrated));
        Assert.That(result.Data.Gold, Is.EqualTo(37));
        Assert.That(result.Revision, Is.Zero);
    }

    [TestCase(2, false)]
    [TestCase(3, true)]
    [TestCase(4, true)]
    public void LoadFromPath_MigratesSupportedRawSnapshots(int version, bool unseenPreserved)
    {
        string path = Path.Combine(root, $"save-v{version}.json");
        var snapshot = new InventorySnapshot { Version = version, Gold = 9 };
        snapshot.Cards.Add(new InventoryEntry("card-a", 2));
        snapshot.UnseenPrintings.Add("card-a");
        File.WriteAllText(path, JsonConvert.SerializeObject(snapshot));

        LocalSaveLoadResult result = LocalSaveService.LoadFromPath(path);

        Assert.That(result.Status, Is.EqualTo(LocalSaveLoadStatus.Migrated));
        Assert.That(result.Data.Cards["card-a"], Is.EqualTo(2));
        Assert.That(result.Data.UnseenPrintings.Contains("card-a"), Is.EqualTo(unseenPreserved));
    }

    [Test]
    public void LoadFromPath_RejectsUnknownFutureSnapshotWithoutOverwritingIt()
    {
        string path = Path.Combine(root, "future.json");
        string original = "{\"Version\":5,\"Cards\":[],\"Gold\":99}";
        File.WriteAllText(path, original);

        LocalSaveLoadResult result = LocalSaveService.LoadFromPath(path);

        Assert.That(result.Status, Is.EqualTo(LocalSaveLoadStatus.Unrecoverable));
        Assert.That(result.DiagnosticCode, Is.EqualTo("SAVE_SNAPSHOT_UNSUPPORTED"));
        Assert.That(File.ReadAllText(path), Is.EqualTo(original));
        Assert.That(
            () => LocalSaveService.SaveToPath(path, Data("new-card", 1)),
            Throws.TypeOf<LocalSaveException>().With.Property("DiagnosticCode").EqualTo("SAVE_OVERWRITE_BLOCKED"));
    }

    [Test]
    public void SaveToPath_WritesCompatibleEnvelopeChecksumAndMonotonicRevision()
    {
        string path = Path.Combine(root, "save.json");

        long firstRevision = LocalSaveService.SaveToPath(path, Data("card-a", 2));
        long secondRevision = LocalSaveService.SaveToPath(path, Data("card-b", 5));
        JObject envelope = JObject.Parse(File.ReadAllText(path));
        string payload = envelope.Value<string>("PayloadJson");

        Assert.That(firstRevision, Is.EqualTo(1));
        Assert.That(secondRevision, Is.EqualTo(2));
        Assert.That(envelope.Value<int>("SaveSchemaVersion"), Is.EqualTo(LocalSaveService.SaveSchemaVersion));
        Assert.That(envelope.Value<string>("Namespace"), Is.EqualTo(LocalSaveService.SaveNamespace));
        Assert.That(envelope.Value<int>("ContentSchemaVersion"), Is.EqualTo(LocalSaveService.ContentSchemaVersion));
        Assert.That(envelope.Value<int>("RuleSchemaVersion"), Is.EqualTo(LocalSaveService.RuleSchemaVersion));
        Assert.That(envelope.Value<long>("Revision"), Is.EqualTo(2));
        Assert.That(envelope.Value<string>("PayloadSha256"), Is.EqualTo(Sha256(payload)));
        Assert.That(File.Exists(path + ".backup"), Is.True);
        Assert.That(LocalSaveService.LoadFromPath(path).Data.Cards, Does.ContainKey("card-b"));

        InventorySnapshot previousAppView = JsonUtility.FromJson<InventorySnapshot>(File.ReadAllText(path));
        Assert.That(previousAppView.Version, Is.EqualTo(4));
        Assert.That(previousAppView.Cards.Single(entry => entry.Id == "card-b").Amount, Is.EqualTo(5));
    }

    [Test]
    public void LoadFromPath_RejectsTamperedEnvelopeChecksum()
    {
        string path = Path.Combine(root, "save.json");
        LocalSaveService.SaveToPath(path, Data("card-a", 2));
        JObject envelope = JObject.Parse(File.ReadAllText(path));
        envelope["PayloadJson"] = envelope.Value<string>("PayloadJson").Replace("card-a", "card-b");
        File.WriteAllText(path, envelope.ToString(Formatting.Indented));

        LocalSaveLoadResult result = LocalSaveService.LoadFromPath(path);

        Assert.That(result.Status, Is.EqualTo(LocalSaveLoadStatus.Unrecoverable));
        Assert.That(result.DiagnosticCode, Is.EqualTo("SAVE_CHECKSUM_MISMATCH"));
    }

    [Test]
    public void LoadFromPath_RejectsTamperedRollbackMirrorEvenWhenPayloadIsIntact()
    {
        string path = Path.Combine(root, "save.json");
        LocalSaveService.SaveToPath(path, Data("card-a", 2));
        JObject envelope = JObject.Parse(File.ReadAllText(path));
        envelope["Gold"] = 999;
        File.WriteAllText(path, envelope.ToString(Formatting.Indented));

        LocalSaveLoadResult result = LocalSaveService.LoadFromPath(path);

        Assert.That(result.Status, Is.EqualTo(LocalSaveLoadStatus.Unrecoverable));
        Assert.That(result.DiagnosticCode, Is.EqualTo("SAVE_LEGACY_MIRROR_MISMATCH"));
    }

    [TestCase(LocalSaveWriteStage.TemporaryDurable)]
    [TestCase(LocalSaveWriteStage.BackupCreated)]
    [TestCase(LocalSaveWriteStage.ActiveReplaced)]
    public void SaveToPath_WhenInterrupted_RollsBackToPreviousVerifiedSave(LocalSaveWriteStage failedStage)
    {
        string path = Path.Combine(root, failedStage + ".json");
        LocalSaveService.SaveToPath(path, Data("old-card", 3));

        Assert.That(
            () => LocalSaveService.SaveToPath(
                path,
                Data("new-card", 7),
                stage =>
                {
                    if (stage == failedStage) throw new InvalidOperationException("fixture interruption");
                }),
            Throws.TypeOf<InvalidOperationException>());

        LocalSaveLoadResult restored = LocalSaveService.LoadFromPath(path);
        Assert.That(restored.CanSave, Is.True);
        Assert.That(restored.Data.Cards, Does.ContainKey("old-card"));
        Assert.That(restored.Data.Cards, Does.Not.ContainKey("new-card"));
        Assert.That(File.Exists(path + ".tmp"), Is.False);
    }

    [Test]
    public void LoadFromPath_WhenActiveIsCorrupt_RestoresBackupAndQuarantinesEvidence()
    {
        string path = Path.Combine(root, "save.json");
        LocalSaveService.SaveToPath(path, Data("old-card", 1));
        LocalSaveService.SaveToPath(path, Data("new-card", 2));
        File.WriteAllText(path, "{broken-json");

        LocalSaveLoadResult result = LocalSaveService.LoadFromPath(path);

        Assert.That(result.Status, Is.EqualTo(LocalSaveLoadStatus.RecoveredFromBackup));
        Assert.That(result.Data.Cards, Does.ContainKey("old-card"));
        Assert.That(result.Data.Cards, Does.Not.ContainKey("new-card"));
        Assert.That(File.Exists(path + ".corrupt"), Is.True);
        Assert.That(File.Exists(path + ".backup"), Is.True);
    }

    [Test]
    public void InventorySnapshotMigrator_RejectsUnsupportedPastAndFutureVersions()
    {
        Assert.That(
            () => InventorySnapshotMigrator.Migrate(new InventorySnapshot { Version = 1 }),
            Throws.TypeOf<InvalidOperationException>());
        Assert.That(
            () => InventorySnapshotMigrator.Migrate(new InventorySnapshot { Version = 5 }),
            Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void LoadFromPath_RejectsChecksumValidButStructurallyInvalidInventory()
    {
        string path = Path.Combine(root, "invalid-inventory.json");
        var snapshot = new InventorySnapshot { Version = 4, Gold = -1 };
        File.WriteAllText(path, JsonConvert.SerializeObject(snapshot));

        LocalSaveLoadResult result = LocalSaveService.LoadFromPath(path);

        Assert.That(result.Status, Is.EqualTo(LocalSaveLoadStatus.Unrecoverable));
        Assert.That(result.DiagnosticCode, Is.EqualTo("SAVE_SNAPSHOT_UNSUPPORTED"));
    }

    private static InventoryData Data(string cardId, int amount)
    {
        var data = new InventoryData { Gold = 10, LastModifiedUtcTicks = 1234 };
        data.Cards[cardId] = amount;
        data.UnseenPrintings.Add(cardId);
        return data;
    }

    private static string Sha256(string value)
    {
        using (SHA256 algorithm = SHA256.Create())
        {
            byte[] hash = algorithm.ComputeHash(Encoding.UTF8.GetBytes(value));
            return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}

public sealed class LocalDiagnosticLogStoreTests
{
    private string root;
    private DateTime now;

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), "gacha-diagnostics-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        now = new DateTime(2026, 8, 26, 8, 0, 0, DateTimeKind.Utc);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    [Test]
    public void Sanitize_RemovesCredentialsEmailAbsolutePathAndLineBreaks()
    {
        string safe = LocalDiagnosticLogStore.Sanitize(
            "token=abc password:qwerty Bearer ey.secret alice@example.com C:\\Users\\Alice\\save.json\nprivate");

        Assert.That(safe, Does.Not.Contain("abc"));
        Assert.That(safe, Does.Not.Contain("qwerty"));
        Assert.That(safe, Does.Not.Contain("ey.secret"));
        Assert.That(safe, Does.Not.Contain("alice@example.com"));
        Assert.That(safe, Does.Not.Contain("C:\\Users"));
        Assert.That(safe, Does.Not.Contain("\n"));
        Assert.That(safe, Does.Contain("[redacted"));
    }

    [Test]
    public void AppendAndExport_ResanitizeTamperedLinesAndRemainLocalOnly()
    {
        var store = Store();
        store.Append("cloud sync", "token failed", "token=first C:\\Private\\save.json");
        string managed = Directory.GetFiles(root, "diagnostic-*.jsonl").Single();
        File.AppendAllText(
            managed,
            JsonConvert.SerializeObject(new
            {
                UtcTicks = now.Ticks,
                Category = "manual",
                Code = "RAW",
                Summary = "password=second bob@example.com"
            }) + Environment.NewLine);
        string exportPath = Path.Combine(Path.GetDirectoryName(root), Path.GetFileName(root) + ".json");

        try
        {
            int count = store.Export(exportPath);
            string exported = File.ReadAllText(exportPath);

            Assert.That(count, Is.EqualTo(2));
            Assert.That(exported, Does.Contain(LocalDiagnosticLogStore.ExportNamespace));
            Assert.That(exported, Does.Not.Contain("first"));
            Assert.That(exported, Does.Not.Contain("second"));
            Assert.That(exported, Does.Not.Contain("bob@example.com"));
            Assert.That(exported, Does.Not.Contain("C:\\\\Private"));
        }
        finally
        {
            if (File.Exists(exportPath)) File.Delete(exportPath);
            if (File.Exists(exportPath + ".backup")) File.Delete(exportPath + ".backup");
        }
    }

    [Test]
    public void Append_PrunesEntriesOlderThanSevenCalendarDays()
    {
        now = new DateTime(2026, 8, 18, 8, 0, 0, DateTimeKind.Utc);
        var store = Store();
        store.Append("save", "OLD", "old diagnostic");
        Assert.That(Directory.GetFiles(root, "diagnostic-20260818-*.jsonl"), Has.Length.EqualTo(1));

        now = new DateTime(2026, 8, 26, 8, 0, 0, DateTimeKind.Utc);
        store.Append("save", "CURRENT", "current diagnostic");

        Assert.That(Directory.GetFiles(root, "diagnostic-20260818-*.jsonl"), Is.Empty);
        Assert.That(Directory.GetFiles(root, "diagnostic-20260826-*.jsonl"), Has.Length.EqualTo(1));
    }

    [Test]
    public void Append_EnforcesTotalByteCapByDeletingOldestControlledSegments()
    {
        var store = new LocalDiagnosticLogStore(root, () => now, 7, 700, 350);
        for (int index = 0; index < 20; index++)
            store.Append("save", "ENTRY_" + index, new string('x', 180));

        long total = Directory.GetFiles(root, "diagnostic-*.jsonl")
            .Sum(path => new FileInfo(path).Length);

        Assert.That(total, Is.LessThanOrEqualTo(700));
        Assert.That(Directory.GetFiles(root, "diagnostic-*.jsonl"), Is.Not.Empty);
    }

    [Test]
    public void Clear_DeletesOnlyStrictlyControlledFiles()
    {
        var store = Store();
        store.Append("save", "ONE", "one");
        string sentinel = Path.Combine(root, "diagnostic-user-notes.jsonl");
        File.WriteAllText(sentinel, "keep");

        int deleted = store.Clear();

        Assert.That(deleted, Is.EqualTo(1));
        Assert.That(File.Exists(sentinel), Is.True);
    }

    [Test]
    public void Export_RejectsDestinationInsideManagedLogDirectory()
    {
        var store = Store();
        Assert.That(
            () => store.Export(Path.Combine(root, "export.json")),
            Throws.TypeOf<InvalidOperationException>());
    }

    private LocalDiagnosticLogStore Store()
    {
        return new LocalDiagnosticLogStore(root, () => now);
    }
}
