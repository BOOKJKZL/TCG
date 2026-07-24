using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Gacha.Application;
using Gacha.EditorTools.Content;
using Gacha.Infrastructure.Content;
using NUnit.Framework;

public class DeterministicContentPackagePublisherTests
{
    private sealed class EmptyRegistry : IInstalledContentPackageRegistry
    {
        public InstalledContentPackage Find(string packageId) => null;
    }

    private sealed class LargeStorage : IContentStorageProbe
    {
        public long GetAvailableBytes() => long.MaxValue;
    }

    private string root;
    private string source;
    private string output;

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), "gacha-publisher-" + Guid.NewGuid().ToString("N"));
        source = Path.Combine(root, "source", "en", "fixture");
        output = Path.Combine(root, "release");
        Directory.CreateDirectory(Path.Combine(source, "images"));
        Directory.CreateDirectory(Path.Combine(source, "raw"));
        File.WriteAllText(Path.Combine(source, "manifest.json"), "{\"fixture\":true}");
        File.WriteAllBytes(Path.Combine(source, "images", "card.bin"), Bytes(2048));
        File.WriteAllText(Path.Combine(source, "raw", "set.json"), "{\"id\":\"fixture\"}");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }

    [Test]
    public void Publish_UnchangedInputKeepsArchiveHashAndCatalogBytes()
    {
        DeterministicContentPackagePublisher publisher = Publisher();

        ContentPackagePublishResult first = publisher.Publish(Request());
        byte[] firstArchive = File.ReadAllBytes(first.Packages[0].ArchivePath);
        byte[] firstCatalog = File.ReadAllBytes(first.CatalogPath);
        foreach (string path in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(7));

        ContentPackagePublishResult second = publisher.Publish(Request());

        Assert.That(second.Packages[0].Package.Sha256, Is.EqualTo(first.Packages[0].Package.Sha256));
        Assert.That(second.Packages[0].ArchivePath, Is.EqualTo(first.Packages[0].ArchivePath));
        Assert.That(File.ReadAllBytes(second.Packages[0].ArchivePath), Is.EqualTo(firstArchive));
        Assert.That(File.ReadAllBytes(second.CatalogPath), Is.EqualTo(firstCatalog));
        Assert.That(Directory.GetDirectories(output, ".publishing-*"), Is.Empty);
    }

    [Test]
    public async Task Publish_OutputInstallsThroughRuntimePlannerAndInstaller()
    {
        ContentPackagePublishResult publication = Publisher().Publish(Request());
        PublishedContentPackage published = publication.Packages[0];
        string installRoot = Path.Combine(root, "installed");
        var planner = new ContentPackagePlanner(new EmptyRegistry(), new LargeStorage(), 0);
        ContentInstallPlan plan = planner.Plan(published.Package);
        var installer = new FileSystemContentPackageInstaller(installRoot);

        ContentPackageInstallResult result = await installer.InstallAsync(plan, published.ArchivePath);

        Assert.That(result.Succeeded, Is.True, result.ErrorMessage);
        Assert.That(File.ReadAllBytes(Path.Combine(installRoot, "en", "fixture", "images", "card.bin")),
            Is.EqualTo(Bytes(2048)));
        InstalledContentPackage receipt = new FileSystemInstalledContentPackageRegistry(installRoot)
            .Find("en.fixture");
        Assert.That(receipt, Is.Not.Null);
        Assert.That(receipt.Sha256, Is.EqualTo(published.Package.Sha256));
        Assert.That(receipt.InstalledBytes, Is.EqualTo(published.Package.InstalledBytes));
    }

    [Test]
    public void Publish_MultiplePackagesAreSortedAndCatalogReaderAcceptsOutput()
    {
        string secondSource = Path.Combine(root, "source", "en", "alpha");
        Directory.CreateDirectory(secondSource);
        File.WriteAllText(Path.Combine(secondSource, "manifest.json"), "{\"alpha\":true}");
        var request = new ContentPackagePublishRequest(
            output,
            4,
            new[]
            {
                Definition("en.fixture", source, "en/fixture"),
                Definition("en.alpha", secondSource, "en/alpha")
            });

        ContentPackagePublishResult result = Publisher().Publish(request);
        ContentPackageCatalogLoadResult catalog = new JsonContentPackageCatalogReader().Read(
            result.CatalogJson,
            new Uri("https://cdn.example.test/releases/catalog.json"));

        Assert.That(result.Packages.Select(item => item.Package.PackageId),
            Is.EqualTo(new[] { "en.alpha", "en.fixture" }));
        Assert.That(catalog.Succeeded, Is.True, catalog.ErrorMessage);
        Assert.That(catalog.Catalog.Revision, Is.EqualTo(4));
        Assert.That(catalog.Catalog.Packages.Select(item => item.Package.PackageId),
            Is.EqualTo(new[] { "en.alpha", "en.fixture" }));
        Assert.That(catalog.Catalog.Packages.All(item =>
            item.ArchiveUri.AbsolutePath.Contains(item.Package.Sha256)), Is.True);
    }

    [Test]
    public void Publish_RejectsNestedOutputAndDuplicateInstallPaths()
    {
        string nestedOutput = Path.Combine(source, "release");
        Assert.Throws<InvalidDataException>(() => Publisher().Publish(new ContentPackagePublishRequest(
            nestedOutput,
            1,
            new[] { Definition("en.fixture", source, "en/fixture") })));

        Assert.Throws<InvalidDataException>(() => Publisher().Publish(new ContentPackagePublishRequest(
            output,
            1,
            new[]
            {
                Definition("en.fixture", source, "en/fixture"),
                Definition("en.other", source, "EN/FIXTURE")
            })));
    }

    [Test]
    public void Publish_CancelledRequestLeavesNoCatalogOrTemporaryWorkspace()
    {
        var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => Publisher().Publish(Request(), cancellation.Token));
        Assert.That(File.Exists(Path.Combine(output, "catalog.json")), Is.False);
        Assert.That(Directory.Exists(output) ? Directory.GetDirectories(output, ".publishing-*") : Array.Empty<string>(),
            Is.Empty);
    }

    [Test]
    public void BatchPublish_VerifiesRuntimeInstallAndPrivateCatalogLoad()
    {
        File.WriteAllText(
            Path.Combine(source, "manifest.json"),
            "{\"SchemaVersion\":1,\"Source\":\"fixture\",\"Language\":\"en\"," +
            "\"Set\":{\"Id\":\"fixture\",\"Name\":\"Fixture Set\",\"SeriesId\":\"fixture\"," +
            "\"SeriesName\":\"Fixture\",\"ReleaseDate\":\"2000-01-01\"," +
            "\"OfficialCardCount\":0,\"TotalCardCount\":0},\"Cards\":[],\"Errors\":[]}");

        ContentPackagePublishResult result = ContentPackagePublisherBatch.Publish(
            output,
            1,
            1,
            "1.0.0",
            new[] { new ContentPackagePublisherBatch.ImportedSet("en", "fixture", source) });

        Assert.That(result.Packages.Count, Is.EqualTo(1));
        Assert.That(Directory.GetDirectories(output, ".verification-*"), Is.Empty);
    }

    private ContentPackagePublishRequest Request()
    {
        return new ContentPackagePublishRequest(
            output,
            1,
            new[] { Definition("en.fixture", source, "en/fixture") });
    }

    private static ContentPackagePublishDefinition Definition(string id, string path, string installPath)
    {
        return new ContentPackagePublishDefinition(id, path, installPath, 1, "1.0.0");
    }

    private static DeterministicContentPackagePublisher Publisher()
    {
        return new DeterministicContentPackagePublisher();
    }

    private static byte[] Bytes(int count)
    {
        var bytes = new byte[count];
        for (int index = 0; index < count; index++)
            bytes[index] = (byte)(index % 251);
        return bytes;
    }
}
