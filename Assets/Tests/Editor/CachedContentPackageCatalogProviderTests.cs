using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Gacha.Application;
using Gacha.Infrastructure.Content;
using NUnit.Framework;

public class CachedContentPackageCatalogProviderTests
{
    private sealed class Provider : IContentPackageCatalogProvider, IDisposable
    {
        private readonly Func<CancellationToken, Task<ContentPackageCatalogLoadResult>> load;

        public Provider(Func<CancellationToken, Task<ContentPackageCatalogLoadResult>> load)
        {
            this.load = load;
        }

        public bool Disposed { get; private set; }

        public Task<ContentPackageCatalogLoadResult> LoadAsync(CancellationToken cancellationToken)
        {
            return load(cancellationToken);
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }

    private string root;
    private string cachePath;
    private Uri sourceUri;

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), "gacha-catalog-cache-" + Guid.NewGuid().ToString("N"));
        cachePath = Path.Combine(root, "catalog-cache-v1.json");
        sourceUri = new Uri("https://content.example.test/releases/android/catalog.json");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }

    [Test]
    public async Task OnlineSuccessThenOfflineRestart_UsesLastVerifiedCatalog()
    {
        ContentPackageCatalog onlineCatalog = Catalog(7, 'a');
        using (var online = Cached(Success(onlineCatalog), sourceUri))
        {
            ContentPackageCatalogLoadResult loaded = await online.LoadAsync(CancellationToken.None);

            Assert.That(loaded.Succeeded, Is.True, loaded.ErrorMessage);
            Assert.That(loaded.UsedCachedCatalog, Is.False);
            Assert.That(loaded.WarningMessage, Is.Null);
            Assert.That(File.Exists(cachePath), Is.True);
        }

        using (var restarted = Cached(Failure("fixture network offline"), sourceUri))
        {
            ContentPackageCatalogLoadResult fallback = await restarted.LoadAsync(CancellationToken.None);

            Assert.That(fallback.Succeeded, Is.True, fallback.ErrorMessage);
            Assert.That(fallback.UsedCachedCatalog, Is.True);
            Assert.That(fallback.WarningMessage, Does.Contain("fixture network offline"));
            Assert.That(fallback.Catalog.Revision, Is.EqualTo(7));
            Assert.That(fallback.Catalog.Packages[0].ArchiveUri.AbsoluteUri,
                Is.EqualTo(onlineCatalog.Packages[0].ArchiveUri.AbsoluteUri));
            Assert.That(fallback.Catalog.Packages[0].Metadata.Kind, Is.EqualTo("fixture"));
            Assert.That(fallback.Catalog.Packages[0].Metadata.ContentLanguageId, Is.EqualTo("en"));
            Assert.That(fallback.Catalog.Packages[0].Metadata.SetCode, Is.EqualTo("BASE1"));
        }
    }

    [Test]
    public async Task CacheFromDifferentConfiguredSource_IsRejected()
    {
        using (var online = Cached(Success(Catalog(1, 'a')), sourceUri))
            Assert.That((await online.LoadAsync(CancellationToken.None)).Succeeded, Is.True);

        var changedSource = new Uri("https://other.example.test/catalog.json");
        using (var offline = Cached(Failure("offline"), changedSource))
        {
            ContentPackageCatalogLoadResult result = await offline.LoadAsync(CancellationToken.None);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("different configured source"));
        }
    }

    [Test]
    public async Task CorruptCache_DoesNotBecomeSuccessfulCatalog()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(cachePath, "{broken-cache");
        using (var offline = Cached(Failure("offline"), sourceUri))
        {
            ContentPackageCatalogLoadResult result = await offline.LoadAsync(CancellationToken.None);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("offline catalog cache is unavailable"));
        }
    }

    [Test]
    public async Task NewVerifiedCatalog_ReplacesCacheWithoutTransactionDebris()
    {
        using (var first = Cached(Success(Catalog(1, 'a')), sourceUri))
            Assert.That((await first.LoadAsync(CancellationToken.None)).Succeeded, Is.True);
        using (var second = Cached(Success(Catalog(2, 'b')), sourceUri))
            Assert.That((await second.LoadAsync(CancellationToken.None)).Succeeded, Is.True);
        using (var offline = Cached(Failure("offline"), sourceUri))
        {
            ContentPackageCatalogLoadResult fallback = await offline.LoadAsync(CancellationToken.None);

            Assert.That(fallback.Succeeded, Is.True, fallback.ErrorMessage);
            Assert.That(fallback.Catalog.Revision, Is.EqualTo(2));
            Assert.That(fallback.Catalog.Packages[0].Package.Sha256, Is.EqualTo(new string('b', 64)));
        }
        Assert.That(File.Exists(cachePath + ".tmp"), Is.False);
        Assert.That(File.Exists(cachePath + ".backup"), Is.False);
    }

    [Test]
    public async Task ProtectedCache_RejectsLegacyDowngradeAndSameRevisionMutation()
    {
        using (var signer = new FixtureSigner())
        {
            JsonContentPackageCatalogReader reader = ProtectedReader(signer);
            ContentPackageCatalog protectedCatalog = ProtectedCatalog(7, 'a', signer);
            using (var first = Cached(Success(protectedCatalog), sourceUri, reader))
                Assert.That((await first.LoadAsync(CancellationToken.None)).Succeeded, Is.True);

            using (var downgrade = Cached(Success(Catalog(8, 'b')), sourceUri, reader))
            {
                ContentPackageCatalogLoadResult result = await downgrade.LoadAsync(CancellationToken.None);
                Assert.That(result.Succeeded, Is.True, result.ErrorMessage);
                Assert.That(result.UsedCachedCatalog, Is.True);
                Assert.That(result.Catalog.IsProtected, Is.True);
                Assert.That(result.Catalog.Revision, Is.EqualTo(7));
                Assert.That(result.WarningMessage, Does.Contain("cannot replace"));
            }

            using (var mutation = Cached(
                       Success(ProtectedCatalog(7, 'c', signer)), sourceUri, reader))
            {
                ContentPackageCatalogLoadResult result = await mutation.LoadAsync(CancellationToken.None);
                Assert.That(result.Succeeded, Is.True, result.ErrorMessage);
                Assert.That(result.UsedCachedCatalog, Is.True);
                Assert.That(result.Catalog.Packages[0].Package.Sha256, Is.EqualTo(new string('a', 64)));
                Assert.That(result.WarningMessage, Does.Contain("different signed content"));
            }
        }
    }

    [Test]
    public async Task InterruptedReplacement_BackupIsRecoveredOnOfflineRestart()
    {
        using (var online = Cached(Success(Catalog(1, 'a')), sourceUri))
            Assert.That((await online.LoadAsync(CancellationToken.None)).Succeeded, Is.True);
        File.Move(cachePath, cachePath + ".backup");

        using (var offline = Cached(Failure("offline"), sourceUri))
        {
            ContentPackageCatalogLoadResult result = await offline.LoadAsync(CancellationToken.None);

            Assert.That(result.Succeeded, Is.True, result.ErrorMessage);
            Assert.That(result.UsedCachedCatalog, Is.True);
            Assert.That(result.Catalog.Revision, Is.EqualTo(1));
        }
        Assert.That(File.Exists(cachePath), Is.True);
        Assert.That(File.Exists(cachePath + ".backup"), Is.False);
    }

    [Test]
    public async Task CacheWriteFailure_KeepsOnlineCatalogUsableWithWarning()
    {
        Directory.CreateDirectory(cachePath);
        using (var provider = Cached(Success(Catalog(1, 'a')), sourceUri))
        {
            ContentPackageCatalogLoadResult result = await provider.LoadAsync(CancellationToken.None);

            Assert.That(result.Succeeded, Is.True, result.ErrorMessage);
            Assert.That(result.UsedCachedCatalog, Is.False);
            Assert.That(result.WarningMessage, Does.Contain("offline cache could not be updated"));
        }
    }

    [Test]
    public void ExternalCancellation_DoesNotFallBackToStaleCache()
    {
        using (var online = Cached(Success(Catalog(1, 'a')), sourceUri))
            Assert.That(online.LoadAsync(CancellationToken.None).GetAwaiter().GetResult().Succeeded, Is.True);

        var upstream = new Provider(token =>
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(Failure("unreachable"));
        });
        using (var cached = new CachedContentPackageCatalogProvider(upstream, cachePath, sourceUri))
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () =>
                await cached.LoadAsync(cancellation.Token));
        }
        Assert.That(upstream.Disposed, Is.True);
    }

    private CachedContentPackageCatalogProvider Cached(
        ContentPackageCatalogLoadResult result,
        Uri configuredSource,
        JsonContentPackageCatalogReader reader = null)
    {
        return new CachedContentPackageCatalogProvider(
            new Provider(_ => Task.FromResult(result)),
            cachePath,
            configuredSource,
            reader: reader);
    }

    private static ContentPackageCatalogLoadResult Success(ContentPackageCatalog catalog)
    {
        return ContentPackageCatalogLoadResult.Success(catalog);
    }

    private static ContentPackageCatalogLoadResult Failure(string error)
    {
        return ContentPackageCatalogLoadResult.Failure(error);
    }

    private static ContentPackageCatalog Catalog(long revision, char hashCharacter)
    {
        string hash = new string(hashCharacter, 64);
        var package = new ContentPackageDescriptor(
            "en.base1",
            "en/base1",
            revision,
            revision + ".0.0",
            100,
            200,
            hash);
        return new ContentPackageCatalog(
            ContentPackageCatalog.SupportedSchemaVersion,
            revision,
            new[]
            {
                new ContentPackageCatalogEntry(
                    package,
                    new Uri("https://cdn.example.test/packages/en.base1/" + hash + ".zip"),
                    new ContentPackageMetadata(
                        "fixture",
                        new Dictionary<string, string> { ["en"] = "Base Set" },
                        "fixture-game",
                        "en",
                        "base1",
                        "BASE1",
                        new DateTime(1999, 1, 9),
                        1,
                        1,
                        new[] { "fixture" }))
            });
    }

    private static ContentPackageCatalog ProtectedCatalog(
        long revision,
        char hashCharacter,
        FixtureSigner signer)
    {
        ContentPackageCatalog legacy = Catalog(revision, hashCharacter);
        ContentPackageCatalogEntry[] entries = new[]
        {
            new ContentPackageCatalogEntry(
                legacy.Packages[0].Package,
                legacy.Packages[0].ArchiveUri,
                legacy.Packages[0].Metadata,
                "packages/en.base1/" + legacy.Packages[0].Package.Sha256 + ".zip")
        };
        byte[] canonical = ContentCatalogCanonicalizer.Canonicalize(
            ContentPackageCatalog.ProtectedSchemaVersion,
            revision,
            "1.0.0",
            ContentPackageCatalog.CurrentContentSchemaVersion,
            ContentPackageCatalog.CurrentRuleSchemaVersion,
            entries);
        var signature = new ContentCatalogSignature(
            signer.Algorithm,
            signer.KeyId,
            signer.Sign(canonical));
        return new ContentPackageCatalog(
            ContentPackageCatalog.ProtectedSchemaVersion,
            revision,
            entries,
            "1.0.0",
            ContentPackageCatalog.CurrentContentSchemaVersion,
            ContentPackageCatalog.CurrentRuleSchemaVersion,
            signature,
            ContentCatalogCanonicalizer.ComputeSha256(canonical));
    }

    private static JsonContentPackageCatalogReader ProtectedReader(FixtureSigner signer)
    {
        var verifier = new RsaContentCatalogSignatureVerifier(
            new Dictionary<string, string> { [signer.KeyId] = signer.PublicKey });
        return new JsonContentPackageCatalogReader(new ContentCatalogCompatibilityPolicy(
            "1.0.0",
            ContentPackageCatalog.CurrentContentSchemaVersion,
            ContentPackageCatalog.CurrentRuleSchemaVersion,
            verifier));
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
        public string KeyId => "fixture-cache-2026";
        public string PublicKey { get; }

        public string Sign(byte[] canonicalPayload) => Convert.ToBase64String(rsa.SignData(
            canonicalPayload,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1));

        public bool Verify(
            ContentCatalogSignature signature,
            byte[] canonicalPayload,
            out string errorMessage) =>
            new RsaContentCatalogSignatureVerifier(
                    new Dictionary<string, string> { [KeyId] = PublicKey })
                .Verify(signature, canonicalPayload, out errorMessage);

        public void Dispose() => rsa.Dispose();
    }
}
