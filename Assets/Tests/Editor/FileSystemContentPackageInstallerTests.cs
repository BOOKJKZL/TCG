using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Gacha.Application;
using Gacha.Infrastructure.Content;
using NUnit.Framework;

public class FileSystemContentPackageInstallerTests
{
    private const string OldHash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private sealed class Registry : IInstalledContentPackageRegistry
    {
        public InstalledContentPackage Installed { get; set; }
        public InstalledContentPackage Find(string packageId) => Installed;
    }

    private sealed class UnlimitedStorage : IContentStorageProbe
    {
        public long GetAvailableBytes() => long.MaxValue;
    }

    private sealed class ArchiveFixture
    {
        public string Path;
        public long ArchiveBytes;
        public long InstalledBytes;
        public string Sha256;
    }

    private string temporaryRoot;
    private string contentRoot;

    [SetUp]
    public void SetUp()
    {
        temporaryRoot = Path.Combine(Path.GetTempPath(), "gacha-package-install-" + Guid.NewGuid().ToString("N"));
        contentRoot = Path.Combine(temporaryRoot, "Content");
        Directory.CreateDirectory(temporaryRoot);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(temporaryRoot))
            Directory.Delete(temporaryRoot, true);
    }

    [Test]
    public async Task Install_ValidArchive_PublishesContentAndReceipt()
    {
        ArchiveFixture archive = CreateArchive(new Dictionary<string, byte[]>
        {
            ["manifest.json"] = Encoding.UTF8.GetBytes("{\"SchemaVersion\":1}"),
            ["images/card.bin"] = new byte[] { 1, 2, 3, 4 }
        });
        ContentPackageDescriptor package = Package(archive);
        ContentInstallPlan plan = Plan(package);
        var installer = new FileSystemContentPackageInstaller(contentRoot);

        ContentPackageInstallResult result = await installer.InstallAsync(plan, archive.Path);

        Assert.That(result.Status, Is.EqualTo(ContentPackageInstallStatus.Succeeded));
        Assert.That(result.Succeeded, Is.True);
        Assert.That(File.ReadAllText(Path.Combine(contentRoot, "en", "base1", "manifest.json")),
            Is.EqualTo("{\"SchemaVersion\":1}"));
        Assert.That(File.ReadAllBytes(Path.Combine(contentRoot, "en", "base1", "images", "card.bin")),
            Is.EqualTo(new byte[] { 1, 2, 3, 4 }));

        InstalledContentPackage installed = new FileSystemInstalledContentPackageRegistry(contentRoot).Find("en.base1");
        Assert.That(installed, Is.Not.Null);
        Assert.That(installed.InstallRelativePath, Is.EqualTo("en/base1"));
        Assert.That(installed.Sha256, Is.EqualTo(archive.Sha256));
        Assert.That(File.Exists(archive.Path), Is.True, "The downloader owns archive cleanup.");
        Assert.That(Directory.Exists(Path.Combine(temporaryRoot, ".Content-installing")), Is.False);
    }

    [Test]
    public async Task Install_Update_ReplacesOldDirectoryAndReceipt()
    {
        string destination = CreateOldContent();
        WriteReceipt(revision: 1, sha256: OldHash);
        var oldPackage = new InstalledContentPackage("en.base1", "en/base1", 1, "1.0.0", 3, OldHash);
        ArchiveFixture archive = CreateArchive(new Dictionary<string, byte[]>
        {
            ["new.txt"] = Encoding.UTF8.GetBytes("new-content")
        });
        ContentPackageDescriptor package = Package(archive, revision: 2, version: "2.0.0");
        ContentInstallPlan plan = Plan(package, oldPackage);

        ContentPackageInstallResult result = await new FileSystemContentPackageInstaller(contentRoot)
            .InstallAsync(plan, archive.Path);

        Assert.That(result.Status, Is.EqualTo(ContentPackageInstallStatus.Succeeded));
        Assert.That(File.Exists(Path.Combine(destination, "old.txt")), Is.False);
        Assert.That(File.ReadAllText(Path.Combine(destination, "new.txt")), Is.EqualTo("new-content"));
        InstalledContentPackage installed = new FileSystemInstalledContentPackageRegistry(contentRoot).Find("en.base1");
        Assert.That(installed.Revision, Is.EqualTo(2));
        Assert.That(installed.Version, Is.EqualTo("2.0.0"));
    }

    [Test]
    public async Task Install_HashMismatch_LeavesOldContentUntouched()
    {
        string destination = CreateOldContent();
        ArchiveFixture archive = CreateArchive(new Dictionary<string, byte[]>
        {
            ["new.txt"] = Encoding.UTF8.GetBytes("new-content")
        });
        ContentPackageDescriptor package = Package(
            archive,
            sha256: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

        ContentPackageInstallResult result = await new FileSystemContentPackageInstaller(contentRoot)
            .InstallAsync(Plan(package), archive.Path);

        Assert.That(result.Status, Is.EqualTo(ContentPackageInstallStatus.IntegrityMismatch));
        Assert.That(File.ReadAllText(Path.Combine(destination, "old.txt")), Is.EqualTo("old"));
        Assert.That(File.Exists(Path.Combine(destination, "new.txt")), Is.False);
    }

    [Test]
    public async Task Install_ArchiveLengthMismatch_IsRejectedBeforeExtraction()
    {
        ArchiveFixture archive = CreateArchive(new Dictionary<string, byte[]>
        {
            ["manifest.json"] = Encoding.UTF8.GetBytes("content")
        });
        var package = new ContentPackageDescriptor(
            "en.base1",
            "en/base1",
            1,
            "1.0.0",
            archive.ArchiveBytes + 1,
            archive.InstalledBytes,
            archive.Sha256);

        ContentPackageInstallResult result = await new FileSystemContentPackageInstaller(contentRoot)
            .InstallAsync(Plan(package), archive.Path);

        Assert.That(result.Status, Is.EqualTo(ContentPackageInstallStatus.IntegrityMismatch));
        Assert.That(result.ErrorMessage, Does.Contain("Archive size"));
        Assert.That(Directory.Exists(Path.Combine(contentRoot, "en", "base1")), Is.False);
    }

    [Test]
    public async Task Install_DeclaredExtractedSizeMismatch_DoesNotPublishContent()
    {
        ArchiveFixture archive = CreateArchive(new Dictionary<string, byte[]>
        {
            ["manifest.json"] = Encoding.UTF8.GetBytes("content")
        });
        ContentPackageDescriptor package = Package(archive, installedBytes: archive.InstalledBytes + 1);

        ContentPackageInstallResult result = await new FileSystemContentPackageInstaller(contentRoot)
            .InstallAsync(Plan(package), archive.Path);

        Assert.That(result.Status, Is.EqualTo(ContentPackageInstallStatus.IntegrityMismatch));
        Assert.That(Directory.Exists(Path.Combine(contentRoot, "en", "base1")), Is.False);
        Assert.That(new FileSystemInstalledContentPackageRegistry(contentRoot).Find("en.base1"), Is.Null);
    }

    [Test]
    public async Task Install_ZipSlipPath_IsRejectedWithoutWritingOutsideContentRoot()
    {
        ArchiveFixture archive = CreateArchive(new Dictionary<string, byte[]>
        {
            ["../escape.txt"] = Encoding.UTF8.GetBytes("escape")
        });
        ContentPackageDescriptor package = Package(archive);

        ContentPackageInstallResult result = await new FileSystemContentPackageInstaller(contentRoot)
            .InstallAsync(Plan(package), archive.Path);

        Assert.That(result.Status, Is.EqualTo(ContentPackageInstallStatus.InvalidArchive));
        Assert.That(File.Exists(Path.Combine(temporaryRoot, "escape.txt")), Is.False);
        Assert.That(Directory.Exists(Path.Combine(contentRoot, "en", "base1")), Is.False);
    }

    [Test]
    public async Task Install_PreCancelledUpdate_PreservesOldContentAndReceipt()
    {
        string destination = CreateOldContent();
        WriteReceipt(revision: 1, sha256: OldHash);
        var oldPackage = new InstalledContentPackage("en.base1", "en/base1", 1, "1.0.0", 3, OldHash);
        ArchiveFixture archive = CreateArchive(new Dictionary<string, byte[]>
        {
            ["new.txt"] = Encoding.UTF8.GetBytes("new-content")
        });
        ContentPackageDescriptor package = Package(archive, revision: 2);
        var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        ContentPackageInstallResult result = await new FileSystemContentPackageInstaller(contentRoot)
            .InstallAsync(Plan(package, oldPackage), archive.Path, cancellation.Token);

        Assert.That(result.Status, Is.EqualTo(ContentPackageInstallStatus.Cancelled));
        Assert.That(File.ReadAllText(Path.Combine(destination, "old.txt")), Is.EqualTo("old"));
        Assert.That(new FileSystemInstalledContentPackageRegistry(contentRoot).Find("en.base1").Revision, Is.EqualTo(1));
    }

    [Test]
    public async Task Install_ReceiptPublicationFailure_RestoresPreviousDirectory()
    {
        string destination = CreateOldContent();
        string receiptDirectory = Path.Combine(
            contentRoot,
            FileSystemInstalledContentPackageRegistry.ReceiptDirectoryName,
            "en.base1.json");
        Directory.CreateDirectory(receiptDirectory);
        var oldPackage = new InstalledContentPackage("en.base1", "en/base1", 1, "1.0.0", 3, OldHash);
        ArchiveFixture archive = CreateArchive(new Dictionary<string, byte[]>
        {
            ["new.txt"] = Encoding.UTF8.GetBytes("new-content")
        });
        ContentPackageDescriptor package = Package(archive, revision: 2);

        ContentPackageInstallResult result = await new FileSystemContentPackageInstaller(contentRoot)
            .InstallAsync(Plan(package, oldPackage), archive.Path);

        Assert.That(result.Status, Is.EqualTo(ContentPackageInstallStatus.Failed));
        Assert.That(result.ErrorMessage, Does.Contain("previous package was restored"));
        Assert.That(File.ReadAllText(Path.Combine(destination, "old.txt")), Is.EqualTo("old"));
        Assert.That(File.Exists(Path.Combine(destination, "new.txt")), Is.False);
    }

    [Test]
    public async Task Install_NewPackageCannotReplaceUnregisteredDirectory()
    {
        string destination = CreateOldContent();
        ArchiveFixture archive = CreateArchive(new Dictionary<string, byte[]>
        {
            ["new.txt"] = Encoding.UTF8.GetBytes("new-content")
        });
        ContentPackageDescriptor package = Package(archive);

        ContentPackageInstallResult result = await new FileSystemContentPackageInstaller(contentRoot)
            .InstallAsync(Plan(package), archive.Path);

        Assert.That(result.Status, Is.EqualTo(ContentPackageInstallStatus.Failed));
        Assert.That(File.ReadAllText(Path.Combine(destination, "old.txt")), Is.EqualTo("old"));
        Assert.That(File.Exists(Path.Combine(destination, "new.txt")), Is.False);
    }

    [Test]
    public async Task Install_MissingArchive_ReturnsStructuredFailure()
    {
        var package = new ContentPackageDescriptor(
            "en.base1",
            "en/base1",
            1,
            "1.0.0",
            100,
            200,
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

        ContentPackageInstallResult result = await new FileSystemContentPackageInstaller(contentRoot)
            .InstallAsync(Plan(package), Path.Combine(temporaryRoot, "missing.zip"));

        Assert.That(result.Status, Is.EqualTo(ContentPackageInstallStatus.ArchiveNotFound));
        Assert.That(result.Succeeded, Is.False);
    }

    private ContentInstallPlan Plan(
        ContentPackageDescriptor package,
        InstalledContentPackage installed = null)
    {
        return new ContentPackagePlanner(
            new Registry { Installed = installed },
            new UnlimitedStorage(),
            0).Plan(package);
    }

    private static ContentPackageDescriptor Package(
        ArchiveFixture archive,
        long revision = 1,
        string version = "1.0.0",
        long? installedBytes = null,
        string sha256 = null)
    {
        return new ContentPackageDescriptor(
            "en.base1",
            "en/base1",
            revision,
            version,
            archive.ArchiveBytes,
            installedBytes ?? archive.InstalledBytes,
            sha256 ?? archive.Sha256);
    }

    private ArchiveFixture CreateArchive(IReadOnlyDictionary<string, byte[]> entries)
    {
        string path = Path.Combine(temporaryRoot, "package-" + Guid.NewGuid().ToString("N") + ".zip");
        long installedBytes = 0;
        using (FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, false))
        {
            foreach (KeyValuePair<string, byte[]> pair in entries)
            {
                ZipArchiveEntry entry = archive.CreateEntry(pair.Key, CompressionLevel.NoCompression);
                using (Stream output = entry.Open())
                    output.Write(pair.Value, 0, pair.Value.Length);
                installedBytes += pair.Value.Length;
            }
        }

        return new ArchiveFixture
        {
            Path = path,
            ArchiveBytes = new FileInfo(path).Length,
            InstalledBytes = installedBytes,
            Sha256 = ComputeSha256(path)
        };
    }

    private string CreateOldContent()
    {
        string destination = Path.Combine(contentRoot, "en", "base1");
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(destination, "old.txt"), "old");
        return destination;
    }

    private void WriteReceipt(long revision, string sha256)
    {
        string receiptDirectory = Path.Combine(
            contentRoot,
            FileSystemInstalledContentPackageRegistry.ReceiptDirectoryName);
        Directory.CreateDirectory(receiptDirectory);
        File.WriteAllText(
            Path.Combine(receiptDirectory, "en.base1.json"),
            "{\"PackageId\":\"en.base1\",\"InstallRelativePath\":\"en/base1\",\"Revision\":" + revision +
            ",\"Version\":\"1.0.0\",\"InstalledBytes\":3,\"Sha256\":\"" + sha256 + "\"}");
    }

    private static string ComputeSha256(string path)
    {
        using (FileStream stream = File.OpenRead(path))
        using (SHA256 sha256 = SHA256.Create())
            return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
    }
}
