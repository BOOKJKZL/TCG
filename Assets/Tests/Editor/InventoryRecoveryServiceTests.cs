using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Gacha.Application;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

public sealed class InventoryRecoveryServiceTests
{
    private string root;
    private readonly DateTime fixedUtc =
        new DateTime(2026, 7, 31, 7, 10, 0, DateTimeKind.Utc);

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), "gacha-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    [Test]
    public void ExportAndPreview_PreserveInventorySettingsAndStablePayloadHash()
    {
        var service = new InventoryRecoveryService(() => fixedUtc);
        string firstPath = Path.Combine(root, "first.gachasave");
        string secondPath = Path.Combine(root, "second.gachasave");
        PlayerRecoveryState state = CreateState("card-a", 3, "zh", "ja");

        InventoryRecoveryPreview first = service.Export(firstPath, state, "install-a");
        InventoryRecoveryPreview second = service.Export(secondPath, state, "install-a");
        InventoryRecoveryPreview loaded = service.Preview(firstPath);

        Assert.That(first.PayloadSha256, Is.EqualTo(second.PayloadSha256));
        Assert.That(loaded.PayloadSha256, Is.EqualTo(first.PayloadSha256));
        Assert.That(loaded.CreatedAtUtc, Is.EqualTo(fixedUtc));
        Assert.That(loaded.SourceInstallId, Is.EqualTo("install-a"));
        Assert.That(loaded.DistinctPrintingCount, Is.EqualTo(1));
        Assert.That(loaded.TotalCardCount, Is.EqualTo(3));
        Assert.That(loaded.TotalProductsOpened, Is.EqualTo(2));
        Assert.That(loaded.HistoryCount, Is.EqualTo(1));
        Assert.That(loaded.UiLanguageId, Is.EqualTo("zh"));
        Assert.That(loaded.ContentLanguageId, Is.EqualTo("ja"));
    }

    [Test]
    public void Preview_RejectsTamperedPayloadAndOtherApplicationNamespace()
    {
        var service = new InventoryRecoveryService(() => fixedUtc);
        string path = Path.Combine(root, "tampered.gachasave");
        service.Export(path, CreateState("card-a", 3, "en", "en"), "install-a");

        string original = File.ReadAllText(path);
        File.WriteAllText(path, original.Replace("card-a", "card-b"));
        Assert.That(
            () => service.Preview(path),
            Throws.TypeOf<InventoryRecoveryException>().With.Message.Contains("checksum"));

        service.Export(path, CreateState("card-a", 3, "en", "en"), "install-a");
        JObject envelope = JObject.Parse(File.ReadAllText(path));
        envelope["Namespace"] = "novel-app/player-state";
        File.WriteAllText(path, envelope.ToString(Formatting.Indented));
        Assert.That(
            () => service.Preview(path),
            Throws.TypeOf<InventoryRecoveryException>().With.Message.Contains("another application"));
    }

    [Test]
    public void Preview_RejectsChecksumValidPayloadWithInvalidInventoryReference()
    {
        var service = new InventoryRecoveryService(() => fixedUtc);
        string path = Path.Combine(root, "invalid.gachasave");
        service.Export(path, CreateState("card-a", 3, "en", "en"), "install-a");
        JObject envelope = JObject.Parse(File.ReadAllText(path));
        JObject payload = JObject.Parse(envelope.Value<string>("PayloadJson"));
        ((JArray)payload["Inventory"]["UnseenPrintings"]).Add("missing-card");
        string payloadJson = payload.ToString(Formatting.None);
        envelope["PayloadJson"] = payloadJson;
        envelope["PayloadSha256"] = Sha256(Encoding.UTF8.GetBytes(payloadJson));
        File.WriteAllText(path, envelope.ToString(Formatting.Indented));

        Assert.That(
            () => service.Preview(path),
            Throws.TypeOf<InventoryRecoveryException>().With.Message.Contains("unseen-card"));
    }

    [Test]
    public void Restore_CreatesPreImportBackupAndAppliesWholeState()
    {
        var service = new InventoryRecoveryService(() => fixedUtc);
        string importPath = Path.Combine(root, "incoming.gachasave");
        string backupPath = Path.Combine(root, "backup.gachasave");
        PlayerRecoveryState original = CreateState("old-card", 1, "en", "en");
        PlayerRecoveryState incoming = CreateState("new-card", 7, "zh", "ja");
        service.Export(importPath, incoming, "other-install");
        var target = new MemoryTarget(original);

        InventoryRecoveryImportResult result = service.Restore(
            importPath,
            backupPath,
            target,
            "current-install");

        Assert.That(File.Exists(backupPath), Is.True);
        Assert.That(service.Preview(backupPath).SourceInstallId, Is.EqualTo("current-install"));
        Assert.That(target.Current.Inventory.Cards["new-card"], Is.EqualTo(7));
        Assert.That(target.Current.Languages.UiLanguageId, Is.EqualTo("zh"));
        Assert.That(target.Current.Languages.ContentLanguageId, Is.EqualTo("ja"));
        Assert.That(target.Current.Experience.ReduceMotion, Is.True);
        Assert.That(result.Preview.SourceInstallId, Is.EqualTo("other-install"));
    }

    [Test]
    public void Restore_WhenApplyFails_RestoresOriginalAndKeepsBackup()
    {
        var service = new InventoryRecoveryService(() => fixedUtc);
        string importPath = Path.Combine(root, "incoming.gachasave");
        string backupPath = Path.Combine(root, "backup.gachasave");
        PlayerRecoveryState original = CreateState("old-card", 1, "en", "en");
        service.Export(importPath, CreateState("new-card", 7, "zh", "ja"), "other-install");
        var target = new MemoryTarget(original) { FailNextApply = true };

        Assert.That(
            () => service.Restore(importPath, backupPath, target, "current-install"),
            Throws.TypeOf<InventoryRecoveryException>().With.Message.Contains("original player state was restored"));
        Assert.That(File.Exists(backupPath), Is.True);
        Assert.That(target.Current.Inventory.Cards.ContainsKey("old-card"), Is.True);
        Assert.That(target.Current.Inventory.Cards.ContainsKey("new-card"), Is.False);
        Assert.That(target.ApplyCount, Is.EqualTo(2));
    }

    private static PlayerRecoveryState CreateState(
        string printingId,
        int amount,
        string uiLanguage,
        string contentLanguage)
    {
        var inventory = new InventoryData { Gold = 10, LastModifiedUtcTicks = 12345 };
        inventory.Cards[printingId] = amount;
        inventory.UnseenPrintings.Add(printingId);
        inventory.PacksOpened["product-a"] = 2;
        inventory.OpeningHistory.Add(new OpeningHistoryData
        {
            TransactionId = "transaction-a",
            OpenedAtUtcTicks = new DateTime(2026, 7, 30, 0, 0, 0, DateTimeKind.Utc).Ticks,
            ProductId = "product-a",
            SetId = "set-a",
            LanguageId = contentLanguage,
            ProfileId = "profile-a",
            ProductCount = 2,
            CardCount = amount,
            NewPrintingCount = 1,
            RarityCounts = new Dictionary<string, int> { ["rare-a"] = amount }
        });
        inventory.ProductsOpenedByLanguage[contentLanguage] = 2;
        inventory.ProductsOpenedBySet["set-a"] = 2;
        inventory.CardsDrawnByRarity["rare-a"] = amount;
        return new PlayerRecoveryState(
            inventory,
            new LanguagePreferences(uiLanguage, contentLanguage),
            new ExperienceSettings(false, true, false, 1.5f));
    }

    private static string Sha256(byte[] bytes)
    {
        using (SHA256 algorithm = SHA256.Create())
            return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
    }

    private sealed class MemoryTarget : IPlayerRecoveryTarget
    {
        public MemoryTarget(PlayerRecoveryState current) => Current = current;
        public PlayerRecoveryState Current { get; private set; }
        public bool FailNextApply { get; set; }
        public int ApplyCount { get; private set; }

        public PlayerRecoveryState Capture() => Current;

        public void Apply(PlayerRecoveryState state)
        {
            ApplyCount++;
            Current = state;
            if (!FailNextApply) return;
            FailNextApply = false;
            throw new InvalidOperationException("fixture apply failure");
        }
    }
}
