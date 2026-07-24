using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Gacha.Application;
using Gacha.Infrastructure.Content;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;
using UnityEngine.Localization.Tables;

public class ContentPackageLifecycleTests
{
    private sealed class Storage : IContentStorageProbe
    {
        public long GetAvailableBytes() => long.MaxValue;
    }

    private string root;
    private string contentRoot;
    private string savePath;

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), "gacha-lifecycle-" + Guid.NewGuid().ToString("N"));
        contentRoot = Path.Combine(root, "Content");
        savePath = Path.Combine(root, "save.json");
        Directory.CreateDirectory(root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }

    [Test]
    public async Task RemoveAndReinstall_PreservesCollectionSnapshotAndRestoresContent()
    {
        PackageArchive archive = CreateArchive();
        ContentPackageDescriptor package = Descriptor(archive);
        await Install(package, archive);
        const string printingId = "pokemon-tcg:printing:base1:4:en:holo";
        var inventory = new InventoryData { Gold = 77, LastModifiedUtcTicks = 123456 };
        inventory.Cards[printingId] = 3;
        inventory.UnseenPrintings.Add(printingId);
        File.WriteAllText(savePath, JsonUtility.ToJson(inventory.ToSnapshot()), new UTF8Encoding(false));
        byte[] saveBefore = File.ReadAllBytes(savePath);
        var lifecycle = new FileSystemContentPackageLifecycleService(contentRoot);

        ContentPackageRemovalResult removed = await lifecycle.RemoveAsync(package.PackageId);

        Assert.That(removed.Succeeded, Is.True, removed.ErrorMessage);
        Assert.That(removed.Status, Is.EqualTo(ContentPackageRemovalStatus.Removed));
        Assert.That(lifecycle.FindInstalled(package.PackageId), Is.Null);
        Assert.That(Directory.Exists(Path.Combine(contentRoot, "en", "base1")), Is.False);
        Assert.That(File.ReadAllBytes(savePath), Is.EqualTo(saveBefore));
        InventoryData whileRemoved = InventoryData.FromSnapshot(
            JsonUtility.FromJson<InventorySnapshot>(File.ReadAllText(savePath)));
        Assert.That(whileRemoved.Cards[printingId], Is.EqualTo(3));
        Assert.That(whileRemoved.UnseenPrintings, Does.Contain(printingId));

        ContentPackageInstallResult reinstalled = await Install(package, archive);

        Assert.That(reinstalled.Succeeded, Is.True, reinstalled.ErrorMessage);
        Assert.That(File.ReadAllText(Path.Combine(contentRoot, "en", "base1", "manifest.json")),
            Is.EqualTo("fixture-manifest"));
        Assert.That(lifecycle.FindInstalled(package.PackageId), Is.Not.Null);
        Assert.That(File.ReadAllBytes(savePath), Is.EqualTo(saveBefore));
        Assert.That(Directory.Exists(Path.Combine(root, ".Content-removing")), Is.False);
    }

    [Test]
    public async Task Remove_NotInstalledLeavesUnregisteredFilesAndSaveUntouched()
    {
        string unregistered = Path.Combine(contentRoot, "en", "manual", "manifest.json");
        Directory.CreateDirectory(Path.GetDirectoryName(unregistered));
        File.WriteAllText(unregistered, "manual");
        File.WriteAllText(savePath, "collection");

        ContentPackageRemovalResult result = await new FileSystemContentPackageLifecycleService(contentRoot)
            .RemoveAsync("en.manual");

        Assert.That(result.Status, Is.EqualTo(ContentPackageRemovalStatus.NotInstalled));
        Assert.That(result.Succeeded, Is.True);
        Assert.That(File.ReadAllText(unregistered), Is.EqualTo("manual"));
        Assert.That(File.ReadAllText(savePath), Is.EqualTo("collection"));
    }

    [Test]
    public async Task Remove_CorruptEscapingReceiptFailsWithoutDeletingAnything()
    {
        string receiptDirectory = Path.Combine(
            contentRoot,
            FileSystemInstalledContentPackageRegistry.ReceiptDirectoryName);
        Directory.CreateDirectory(receiptDirectory);
        string outside = Path.Combine(root, "outside.txt");
        File.WriteAllText(outside, "keep");
        File.WriteAllText(
            Path.Combine(receiptDirectory, "en.fixture.json"),
            "{\"PackageId\":\"en.fixture\",\"InstallRelativePath\":\"../outside\"," +
            "\"Revision\":1,\"Version\":\"1.0.0\",\"InstalledBytes\":1," +
            "\"Sha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"}");

        ContentPackageRemovalResult result = await new FileSystemContentPackageLifecycleService(contentRoot)
            .RemoveAsync("en.fixture");

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("receipt is invalid"));
        Assert.That(File.ReadAllText(outside), Is.EqualTo("keep"));
        Assert.That(File.Exists(Path.Combine(receiptDirectory, "en.fixture.json")), Is.True);
    }

    [Test]
    public async Task Remove_PreCancelledRequestKeepsInstalledPackage()
    {
        PackageArchive archive = CreateArchive();
        ContentPackageDescriptor package = Descriptor(archive);
        await Install(package, archive);
        var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        ContentPackageRemovalResult result = await new FileSystemContentPackageLifecycleService(contentRoot)
            .RemoveAsync(package.PackageId, cancellation.Token);

        Assert.That(result.Status, Is.EqualTo(ContentPackageRemovalStatus.Cancelled));
        Assert.That(new FileSystemInstalledContentPackageRegistry(contentRoot).Find(package.PackageId), Is.Not.Null);
        Assert.That(File.Exists(Path.Combine(contentRoot, "en", "base1", "manifest.json")), Is.True);
    }

    [Test]
    public async Task Remove_ReceiptCommitFailureRollsContentBack()
    {
        PackageArchive archive = CreateArchive();
        ContentPackageDescriptor package = Descriptor(archive);
        await Install(package, archive);
        string receiptPath = Path.Combine(
            contentRoot,
            FileSystemInstalledContentPackageRegistry.ReceiptDirectoryName,
            package.PackageId + ".json");

        ContentPackageRemovalResult result = await new ReceiptCommitFailingLifecycleService(contentRoot)
            .RemoveAsync(package.PackageId);

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Status, Is.EqualTo(ContentPackageRemovalStatus.Failed));
        Assert.That(result.ErrorMessage, Does.Contain("Previous content was restored"));
        Assert.That(File.Exists(receiptPath), Is.True);
        Assert.That(File.ReadAllText(Path.Combine(contentRoot, "en", "base1", "manifest.json")),
            Is.EqualTo("fixture-manifest"));
        Assert.That(Directory.Exists(Path.Combine(root, ".Content-removing")), Is.False);
    }

    private sealed class ReceiptCommitFailingLifecycleService : FileSystemContentPackageLifecycleService
    {
        public ReceiptCommitFailingLifecycleService(string rootPath)
            : base(rootPath)
        {
        }

        protected override void BeforeReceiptCommit()
        {
            throw new IOException("fixture receipt commit failure");
        }
    }

    [Test]
    public void RemovalLocalization_HasEnglishAndChineseEntries()
    {
        StringTable english = AssetDatabase.LoadAssetAtPath<StringTable>(
            "Assets/Resources/Data/Localization/Card_UI_en.asset");
        StringTable chinese = AssetDatabase.LoadAssetAtPath<StringTable>(
            "Assets/Resources/Data/Localization/Card_UI_zh.asset");
        string[] keys =
        {
            "content.action.remove",
            "content.action.confirm_remove",
            "content.status.update_available",
            "content.status.remove_confirm",
            "content.status.removing",
            "content.status.removed",
            "content.status.remove_failed",
            "content.status.remove_warning"
        };

        Assert.That(english, Is.Not.Null);
        Assert.That(chinese, Is.Not.Null);
        foreach (string key in keys)
        {
            Assert.That(english.GetEntry(key)?.LocalizedValue, Is.Not.Empty, "Missing English key: " + key);
            Assert.That(chinese.GetEntry(key)?.LocalizedValue, Is.Not.Empty, "Missing Chinese key: " + key);
        }
        Assert.That(chinese.GetEntry("content.action.remove").LocalizedValue, Is.EqualTo("删除内容"));
        Assert.That(chinese.GetEntry("content.status.removed").LocalizedValue, Does.Contain("收藏进度"));
    }

    private async Task<ContentPackageInstallResult> Install(
        ContentPackageDescriptor package,
        PackageArchive archive)
    {
        string archivePath = Path.Combine(root, "fixture.zip");
        File.WriteAllBytes(archivePath, archive.Bytes);
        var planner = new ContentPackagePlanner(
            new FileSystemInstalledContentPackageRegistry(contentRoot),
            new Storage(),
            0);
        ContentPackageInstallResult result = await new FileSystemContentPackageInstaller(contentRoot)
            .InstallAsync(planner.Plan(package), archivePath);
        Assert.That(result.Succeeded, Is.True, result.ErrorMessage);
        return result;
    }

    private static ContentPackageDescriptor Descriptor(PackageArchive archive)
    {
        return new ContentPackageDescriptor(
            "en.fixture",
            "en/base1",
            1,
            "1.0.0",
            archive.Bytes.LongLength,
            archive.InstalledBytes,
            archive.Sha256);
    }

    private static PackageArchive CreateArchive()
    {
        byte[] manifest = Encoding.UTF8.GetBytes("fixture-manifest");
        byte[] image = Encoding.UTF8.GetBytes("fixture-image");
        using (var output = new MemoryStream())
        {
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            {
                Write(zip, "manifest.json", manifest);
                Write(zip, "images/card.bin", image);
            }
            byte[] bytes = output.ToArray();
            using (SHA256 sha = SHA256.Create())
            {
                return new PackageArchive(
                    bytes,
                    manifest.LongLength + image.LongLength,
                    BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant());
            }
        }
    }

    private static void Write(ZipArchive zip, string path, byte[] bytes)
    {
        ZipArchiveEntry entry = zip.CreateEntry(path, System.IO.Compression.CompressionLevel.NoCompression);
        using (Stream stream = entry.Open())
            stream.Write(bytes, 0, bytes.Length);
    }

    private sealed class PackageArchive
    {
        public PackageArchive(byte[] bytes, long installedBytes, string sha256)
        {
            Bytes = bytes;
            InstalledBytes = installedBytes;
            Sha256 = sha256;
        }

        public byte[] Bytes { get; }
        public long InstalledBytes { get; }
        public string Sha256 { get; }
    }
}
