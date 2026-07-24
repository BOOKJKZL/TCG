using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Gacha.Application;
using Gacha.Infrastructure.Content;
using NUnit.Framework;

public class ContentPackageCatalogTests
{
    private const string HashA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string HashB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Test]
    public void Reader_ResolvesContentAddressedRelativeArchiveUrl()
    {
        ContentPackageCatalogLoadResult result = Reader().Read(
            Json(PackageJson("en.base1", HashA, "packages/en.base1/" + HashA + ".zip")),
            new Uri("https://content.example.test/catalog/v7/catalog.json"));

        Assert.That(result.Succeeded, Is.True, result.ErrorMessage);
        Assert.That(result.Catalog.SchemaVersion, Is.EqualTo(1));
        Assert.That(result.Catalog.Revision, Is.EqualTo(7));
        Assert.That(result.Catalog.Packages.Count, Is.EqualTo(1));
        ContentPackageCatalogEntry entry = result.Catalog.Find("en.base1");
        Assert.That(entry, Is.Not.Null);
        Assert.That(entry.ArchiveUri.AbsoluteUri, Is.EqualTo(
            "https://content.example.test/catalog/v7/packages/en.base1/" + HashA + ".zip"));
        Assert.That(result.Catalog.Resolve(entry.Package), Is.EqualTo(entry.ArchiveUri));
    }

    [Test]
    public void Reader_RejectsArchiveUrlThatIsNotContentAddressed()
    {
        ContentPackageCatalogLoadResult result = Reader().Read(
            Json(PackageJson("en.base1", HashA, "packages/en.base1/latest.zip")),
            new Uri("https://content.example.test/catalog.json"));

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("must contain its SHA-256"));
    }

    [Test]
    public void Reader_RejectsPublicPlainHttpArchive()
    {
        ContentPackageCatalogLoadResult result = Reader().Read(
            Json(PackageJson(
                "en.base1",
                HashA,
                "http://content.example.test/packages/" + HashA + ".zip")),
            new Uri("https://content.example.test/catalog.json"));

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("must use HTTPS"));
    }

    [Test]
    public void Reader_RejectsDuplicatePackageIds()
    {
        string first = PackageJson("en.base1", HashA, "packages/" + HashA + ".zip");
        string second = PackageJson("en.base1", HashB, "packages/" + HashB + ".zip");

        ContentPackageCatalogLoadResult result = Reader().Read(
            Json(first + "," + second),
            new Uri("https://content.example.test/catalog.json"));

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("duplicate id"));
    }

    [TestCase(0)]
    [TestCase(2)]
    public void Reader_RejectsUnsupportedSchema(int schemaVersion)
    {
        ContentPackageCatalogLoadResult result = Reader().Read(
            Json(PackageJson("en.base1", HashA, "packages/" + HashA + ".zip"), schemaVersion),
            new Uri("https://content.example.test/catalog.json"));

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("not supported"));
    }

    [Test]
    public void Reader_RejectsInvalidPackageBeforeNetworkUse()
    {
        string invalid = PackageJson("../escape", HashA, "packages/" + HashA + ".zip");

        ContentPackageCatalogLoadResult result = Reader().Read(
            Json(invalid),
            new Uri("https://content.example.test/catalog.json"));

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("Package id"));
    }

    [Test]
    public void Catalog_RefusesDescriptorFromDifferentRevision()
    {
        ContentPackageCatalogLoadResult result = Reader().Read(
            Json(PackageJson("en.base1", HashA, "packages/" + HashA + ".zip")),
            new Uri("https://content.example.test/catalog.json"));
        var stale = new ContentPackageDescriptor(
            "en.base1",
            "en/base1",
            2,
            "2.0.0",
            100,
            200,
            HashA);

        Assert.Throws<InvalidOperationException>(() => result.Catalog.Resolve(stale));
    }

    [Test]
    public void Reader_InvalidJsonReturnsStructuredFailure()
    {
        ContentPackageCatalogLoadResult result = Reader().Read(
            "{not-json",
            new Uri("https://content.example.test/catalog.json"));

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Catalog, Is.Null);
        Assert.That(result.ErrorMessage, Does.StartWith("Content package catalog is invalid:"));
    }

    [Test]
    public async Task FileProvider_ReadsCatalogWithoutChangingArchiveOrigin()
    {
        string temporaryRoot = Path.Combine(Path.GetTempPath(), "gacha-catalog-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(temporaryRoot, "catalog.json");
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            File.WriteAllText(path, Json(PackageJson(
                "en.base1",
                HashA,
                "packages/" + HashA + ".zip")));
            var provider = new FileSystemContentPackageCatalogProvider(
                path,
                new Uri("https://cdn.example.test/releases/v7/catalog.json"));

            ContentPackageCatalogLoadResult result = await provider.LoadAsync(CancellationToken.None);

            Assert.That(result.Succeeded, Is.True, result.ErrorMessage);
            Assert.That(result.Catalog.Packages[0].ArchiveUri.Host, Is.EqualTo("cdn.example.test"));
        }
        finally
        {
            if (Directory.Exists(temporaryRoot))
                Directory.Delete(temporaryRoot, true);
        }
    }

    private static JsonContentPackageCatalogReader Reader()
    {
        return new JsonContentPackageCatalogReader();
    }

    private static string Json(string packages, int schemaVersion = 1)
    {
        return "{\"schemaVersion\":" + schemaVersion +
               ",\"revision\":7,\"packages\":[" + packages + "]}";
    }

    private static string PackageJson(string packageId, string hash, string archiveUrl)
    {
        return "{\"packageId\":\"" + packageId +
               "\",\"installRelativePath\":\"en/base1\"" +
               ",\"revision\":3,\"version\":\"3.0.0\"" +
               ",\"downloadBytes\":100,\"installedBytes\":200" +
               ",\"sha256\":\"" + hash +
               "\",\"archiveUrl\":\"" + archiveUrl + "\"}";
    }
}
