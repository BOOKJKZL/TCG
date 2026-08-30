using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Gacha.Application;
using Gacha.Infrastructure.Content;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
        Assert.That(entry.Metadata.IsLegacy, Is.True);
        Assert.That(result.Catalog.Resolve(entry.Package), Is.EqualTo(entry.ArchiveUri));
    }

    [Test]
    public void Reader_ParsesSchemaV2PlayerMetadataWithoutChangingPackageIdentity()
    {
        string metadata = "{\"kind\":\"card-set\",\"gameId\":\"pokemon-tcg\"," +
                          "\"contentLanguageId\":\"ja\",\"localizedNames\":{" +
                          "\"en\":\"Japanese Set\",\"ja\":\"日本語セット\"}," +
                          "\"setId\":\"sv10\",\"setCode\":\"SV10\"," +
                          "\"releaseDate\":\"2025-06-06\",\"generationOrder\":9," +
                          "\"sortOrdinal\":42,\"tags\":[\"pokemon\",\"booster\"]," +
                          "\"dependencies\":[]}";
        ContentPackageCatalogLoadResult result = Reader().Read(
            Json(PackageJson("ja.sv10", HashA, "packages/ja.sv10/" + HashA + ".zip", metadata), 2),
            new Uri("https://content.example.test/catalog.json"));

        Assert.That(result.Succeeded, Is.True, result.ErrorMessage);
        ContentPackageCatalogEntry entry = result.Catalog.Find("ja.sv10");
        Assert.That(entry.Package.Sha256, Is.EqualTo(HashA));
        Assert.That(entry.Metadata.Kind, Is.EqualTo("card-set"));
        Assert.That(entry.Metadata.ContentLanguageId, Is.EqualTo("ja"));
        Assert.That(entry.Metadata.GetDisplayName("ja", null), Is.EqualTo("日本語セット"));
        Assert.That(entry.Metadata.ReleaseDate, Is.EqualTo(new DateTime(2025, 6, 6)));
        Assert.That(entry.Metadata.GenerationOrder, Is.EqualTo(9));
        Assert.That(entry.Metadata.Tags, Is.EqualTo(new[] { "booster", "pokemon" }));
    }

    [Test]
    public void Reader_RejectsSchemaV2MissingMetadata()
    {
        ContentPackageCatalogLoadResult result = Reader().Read(
            Json(PackageJson("en.base1", HashA, "packages/" + HashA + ".zip"), 2),
            new Uri("https://content.example.test/catalog.json"));

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("no schema v2 metadata"));
    }

    [Test]
    public void Reader_RejectsMissingAndCyclicDependencies()
    {
        string missingMetadata = Metadata("Missing", "missing.package");
        ContentPackageCatalogLoadResult missing = Reader().Read(
            Json(PackageJson("en.base1", HashA, "packages/" + HashA + ".zip", missingMetadata), 2),
            new Uri("https://content.example.test/catalog.json"));
        Assert.That(missing.Succeeded, Is.False);
        Assert.That(missing.ErrorMessage, Does.Contain("depends on missing"));

        string first = PackageJson("en.base1", HashA, "packages/" + HashA + ".zip",
            Metadata("Base", "en.other"));
        string second = PackageJson("en.other", HashB, "packages/" + HashB + ".zip",
            Metadata("Other", "en.base1"));
        ContentPackageCatalogLoadResult cyclic = Reader().Read(
            Json(first + "," + second, 2),
            new Uri("https://content.example.test/catalog.json"));
        Assert.That(cyclic.Succeeded, Is.False);
        Assert.That(cyclic.ErrorMessage, Does.Contain("dependency cycle"));
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
    [TestCase(4)]
    public void Reader_RejectsUnsupportedSchema(int schemaVersion)
    {
        ContentPackageCatalogLoadResult result = Reader().Read(
            Json(PackageJson("en.base1", HashA, "packages/" + HashA + ".zip"), schemaVersion),
            new Uri("https://content.example.test/catalog.json"));

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("not supported"));
    }

    [Test]
    public void Reader_VerifiesCompatibleCatalogV3AndRejectsTampering()
    {
        using (var signer = new FixtureSigner())
        {
            string json = ProtectedJson(signer, "1.0.0");
            JsonContentPackageCatalogReader reader = ProtectedReader("1.2.0", signer);

            ContentPackageCatalogLoadResult valid = reader.Read(
                json,
                new Uri("https://content.example.test/catalog.json"));
            JObject tamperedRoot = JObject.Parse(json);
            tamperedRoot["revision"] = 8;
            ContentPackageCatalogLoadResult tampered = reader.Read(
                tamperedRoot.ToString(Formatting.None),
                new Uri("https://content.example.test/catalog.json"));

            Assert.That(valid.Succeeded, Is.True, valid.ErrorMessage);
            Assert.That(valid.Catalog.IsProtected, Is.True);
            Assert.That(valid.Catalog.CanonicalSha256, Has.Length.EqualTo(64));
            Assert.That(tampered.Succeeded, Is.False);
            Assert.That(tampered.ErrorMessage, Does.Contain("signature verification failed"));
        }
    }

    [Test]
    public void Reader_CatalogV3FailsClosedWithoutTrustOrCompatibleApp()
    {
        using (var signer = new FixtureSigner())
        {
            string json = ProtectedJson(signer, "2.0.0");
            Uri uri = new Uri("https://content.example.test/catalog.json");

            ContentPackageCatalogLoadResult noPolicy = Reader().Read(json, uri);
            ContentPackageCatalogLoadResult oldApp = ProtectedReader("1.9.9", signer).Read(json, uri);
            var unknownVerifier = new RsaContentCatalogSignatureVerifier(
                new Dictionary<string, string>());
            var unknownPolicy = new ContentCatalogCompatibilityPolicy(
                "2.0.0",
                ContentPackageCatalog.CurrentContentSchemaVersion,
                ContentPackageCatalog.CurrentRuleSchemaVersion,
                unknownVerifier);
            ContentPackageCatalogLoadResult unknownKey =
                new JsonContentPackageCatalogReader(unknownPolicy).Read(json, uri);

            Assert.That(noPolicy.Succeeded, Is.False);
            Assert.That(noPolicy.ErrorMessage, Does.Contain("requires a runtime compatibility and trust policy"));
            Assert.That(oldApp.Succeeded, Is.False);
            Assert.That(oldApp.ErrorMessage, Does.Contain("requires app 2.0.0 or later"));
            Assert.That(unknownKey.Succeeded, Is.False);
            Assert.That(unknownKey.ErrorMessage, Does.Contain("is not trusted"));
        }
    }

    [Test]
    public void Reader_CatalogV3RejectsSchemaMismatchAndMissingSignature()
    {
        using (var signer = new FixtureSigner())
        {
            string json = ProtectedJson(signer, "1.0.0");
            Uri uri = new Uri("https://content.example.test/catalog.json");
            var verifier = new RsaContentCatalogSignatureVerifier(
                new Dictionary<string, string> { [signer.KeyId] = signer.PublicKey });
            var incompatiblePolicy = new ContentCatalogCompatibilityPolicy(
                "1.0.0",
                ContentPackageCatalog.CurrentContentSchemaVersion + 1,
                ContentPackageCatalog.CurrentRuleSchemaVersion,
                verifier);
            ContentPackageCatalogLoadResult schemaMismatch =
                new JsonContentPackageCatalogReader(incompatiblePolicy).Read(json, uri);
            JObject missingRoot = JObject.Parse(json);
            missingRoot.Remove("signature");
            ContentPackageCatalogLoadResult missingSignature = ProtectedReader("1.0.0", signer).Read(
                missingRoot.ToString(Formatting.None),
                uri);

            Assert.That(schemaMismatch.Succeeded, Is.False);
            Assert.That(schemaMismatch.ErrorMessage, Does.Contain("content schema"));
            Assert.That(missingSignature.Succeeded, Is.False);
            Assert.That(missingSignature.ErrorMessage, Does.Contain("has no signature"));
        }
    }

    [Test]
    public void RsaVerifier_RejectsMalformedSubjectPublicKeyInfo()
    {
        var verifier = new RsaContentCatalogSignatureVerifier(
            new Dictionary<string, string>
            {
                ["malformed"] = Convert.ToBase64String(new byte[] { 0x30, 0x00 })
            });
        var signature = new ContentCatalogSignature(
            "RS256",
            "malformed",
            Convert.ToBase64String(new byte[256]));

        bool verified = verifier.Verify(signature, new byte[] { 1, 2, 3 }, out string error);

        Assert.That(verified, Is.False);
        Assert.That(error, Does.Contain("invalid"));
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

    private static JsonContentPackageCatalogReader ProtectedReader(
        string currentAppVersion,
        FixtureSigner signer)
    {
        var verifier = new RsaContentCatalogSignatureVerifier(
            new Dictionary<string, string> { [signer.KeyId] = signer.PublicKey });
        return new JsonContentPackageCatalogReader(new ContentCatalogCompatibilityPolicy(
            currentAppVersion,
            ContentPackageCatalog.CurrentContentSchemaVersion,
            ContentPackageCatalog.CurrentRuleSchemaVersion,
            verifier));
    }

    private static string ProtectedJson(FixtureSigner signer, string minimumAppVersion)
    {
        const string archiveUrl = "packages/en.base1/" + HashA + ".zip";
        var metadata = new ContentPackageMetadata(
            "fixture",
            new Dictionary<string, string> { ["en"] = "Base" });
        var package = new ContentPackageDescriptor(
            "en.base1", "en/base1", 3, "3.0.0", 100, 200, HashA);
        var entry = new ContentPackageCatalogEntry(
            package,
            new Uri("https://content.example.test/" + archiveUrl),
            metadata,
            archiveUrl);
        byte[] canonical = ContentCatalogCanonicalizer.Canonicalize(
            ContentPackageCatalog.ProtectedSchemaVersion,
            7,
            minimumAppVersion,
            ContentPackageCatalog.CurrentContentSchemaVersion,
            ContentPackageCatalog.CurrentRuleSchemaVersion,
            new[] { entry });
        var root = new JObject
        {
            ["schemaVersion"] = ContentPackageCatalog.ProtectedSchemaVersion,
            ["revision"] = 7,
            ["minAppVersion"] = minimumAppVersion,
            ["contentSchemaVersion"] = ContentPackageCatalog.CurrentContentSchemaVersion,
            ["ruleSchemaVersion"] = ContentPackageCatalog.CurrentRuleSchemaVersion,
            ["packages"] = new JArray(JObject.Parse(PackageJson(
                "en.base1", HashA, archiveUrl, Metadata("Base")))),
            ["signature"] = new JObject
            {
                ["algorithm"] = signer.Algorithm,
                ["keyId"] = signer.KeyId,
                ["value"] = signer.Sign(canonical)
            }
        };
        return root.ToString(Formatting.None);
    }

    private sealed class FixtureSigner : IContentCatalogSigner, IDisposable
    {
        private readonly RSA rsa;

        public FixtureSigner()
        {
            rsa = new RSACryptoServiceProvider(2048);
            PublicKey = Convert.ToBase64String(
                RsaSubjectPublicKeyInfo.Encode(rsa.ExportParameters(false)));
        }

        public string Algorithm => RsaContentCatalogSignatureVerifier.SupportedAlgorithm;
        public string KeyId => "fixture-2026";
        public string PublicKey { get; }

        public string Sign(byte[] canonicalPayload) => Convert.ToBase64String(rsa.SignData(
            canonicalPayload,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1));

        public bool Verify(
            ContentCatalogSignature signature,
            byte[] canonicalPayload,
            out string errorMessage)
        {
            return new RsaContentCatalogSignatureVerifier(
                    new Dictionary<string, string> { [KeyId] = PublicKey })
                .Verify(signature, canonicalPayload, out errorMessage);
        }

        public void Dispose() => rsa.Dispose();
    }

    private static string Json(string packages, int schemaVersion = 1)
    {
        return "{\"schemaVersion\":" + schemaVersion +
               ",\"revision\":7,\"packages\":[" + packages + "]}";
    }

    private static string PackageJson(
        string packageId,
        string hash,
        string archiveUrl,
        string metadata = null)
    {
        return "{\"packageId\":\"" + packageId +
               "\",\"installRelativePath\":\"en/base1\"" +
               ",\"revision\":3,\"version\":\"3.0.0\"" +
               ",\"downloadBytes\":100,\"installedBytes\":200" +
               ",\"sha256\":\"" + hash +
               "\",\"archiveUrl\":\"" + archiveUrl + "\"" +
               (metadata == null ? string.Empty : ",\"metadata\":" + metadata) + "}";
    }

    private static string Metadata(string name, params string[] dependencies)
    {
        return "{\"kind\":\"fixture\",\"localizedNames\":{\"en\":\"" + name +
               "\"},\"tags\":[],\"dependencies\":[" +
               string.Join(",", Array.ConvertAll(dependencies, value => "\"" + value + "\"")) + "]}";
    }
}
