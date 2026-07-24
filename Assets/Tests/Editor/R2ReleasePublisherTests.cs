using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Gacha.EditorTools.Content;
using NUnit.Framework;

public class R2ReleasePublisherTests
{
    private sealed class FakeStore : IR2ReleaseObjectStore
    {
        private readonly Dictionary<string, byte[]> objects = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        public List<string> Operations { get; } = new List<string>();

        public void Seed(string key, byte[] bytes)
        {
            objects[key] = bytes;
        }

        public Task<R2RemoteObjectState> InspectAsync(string objectKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Operations.Add("inspect:" + objectKey);
            return Task.FromResult(objects.TryGetValue(objectKey, out byte[] bytes)
                ? Present(bytes)
                : R2RemoteObjectState.Missing());
        }

        public Task UploadFileAsync(
            string objectKey,
            string localPath,
            string sha256,
            string contentType,
            string cacheControl,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Operations.Add("upload-file:" + objectKey + ":" + cacheControl);
            objects[objectKey] = File.ReadAllBytes(localPath);
            return Task.CompletedTask;
        }

        public Task UploadBytesAsync(
            string objectKey,
            byte[] bytes,
            string sha256,
            string contentType,
            string cacheControl,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Operations.Add("upload-bytes:" + objectKey + ":" + cacheControl);
            objects[objectKey] = bytes.ToArray();
            return Task.CompletedTask;
        }

        public Task<R2RemoteObjectState> VerifyOriginAsync(
            string objectKey,
            long expectedBytes,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Operations.Add("origin:" + objectKey);
            return Task.FromResult(objects.TryGetValue(objectKey, out byte[] bytes)
                ? Present(bytes)
                : R2RemoteObjectState.Missing());
        }

        public Task<R2RemoteObjectState> VerifyPublicAsync(
            Uri publicUri,
            long expectedBytes,
            string expectedSha256,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Operations.Add("public:" + publicUri.AbsolutePath);
            byte[] bytes = objects.Values.FirstOrDefault(value =>
                string.Equals(R2ReleasePublisher.ComputeSha256(value), expectedSha256, StringComparison.Ordinal));
            return Task.FromResult(bytes == null ? R2RemoteObjectState.Missing() : Present(bytes));
        }

        private static R2RemoteObjectState Present(byte[] bytes)
        {
            return R2RemoteObjectState.Present(bytes.LongLength, R2ReleasePublisher.ComputeSha256(bytes));
        }
    }

    private string root;
    private string source;
    private string release;
    private string config;

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), "gacha-r2-publisher-" + Guid.NewGuid().ToString("N"));
        source = Path.Combine(root, "source");
        release = Path.Combine(root, "release");
        config = Path.Combine(root, "private", "remote-content.json");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "manifest.json"), "{\"fixture\":true}");
        File.WriteAllBytes(Path.Combine(source, "card.bin"), Bytes(4096));
        new DeterministicContentPackagePublisher().Publish(new ContentPackagePublishRequest(
            release,
            7,
            new[]
            {
                new ContentPackagePublishDefinition("en.fixture", source, "en/fixture", 3, "3.0.0")
            }));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }

    [Test]
    public void CreatePlan_ValidatesCatalogArchivesAndObjectMappingOffline()
    {
        R2ReleaseUploadPlan plan = CreatePlan();

        Assert.That(plan.Archives.Count, Is.EqualTo(1));
        Assert.That(plan.Archives[0].ObjectKey, Does.StartWith("releases/android/packages/en.fixture/"));
        Assert.That(plan.Archives[0].ObjectKey, Does.Contain(plan.Archives[0].Sha256));
        Assert.That(plan.CatalogObjectKey, Is.EqualTo("releases/android/catalog.json"));
        Assert.That(plan.CatalogUri.AbsoluteUri, Is.EqualTo("https://cards.example.test/releases/android/catalog.json"));
        Assert.That(File.Exists(config), Is.False);
    }

    [Test]
    public async Task Publish_UploadsAndVerifiesArchivesBeforeCatalogThenWritesConfig()
    {
        R2ReleaseUploadPlan plan = CreatePlan();
        var store = new FakeStore();

        R2ReleasePublishResult result = await new R2ReleasePublisher(store).PublishAsync(plan);

        int archiveUpload = store.Operations.FindIndex(item => item.StartsWith("upload-file:", StringComparison.Ordinal));
        int archiveOrigin = store.Operations.FindIndex(item => item.StartsWith("origin:releases/android/packages/", StringComparison.Ordinal));
        int archivePublic = store.Operations.FindIndex(item => item.StartsWith("public:/releases/android/packages/", StringComparison.Ordinal));
        int catalogUpload = store.Operations.FindIndex(item => item.StartsWith("upload-bytes:releases/android/catalog.json", StringComparison.Ordinal));
        int catalogPublic = store.Operations.FindIndex(item => item == "public:/releases/android/catalog.json");
        Assert.That(archiveUpload, Is.GreaterThanOrEqualTo(0));
        Assert.That(archiveOrigin, Is.GreaterThan(archiveUpload));
        Assert.That(archivePublic, Is.GreaterThan(archiveOrigin));
        Assert.That(catalogUpload, Is.GreaterThan(archivePublic));
        Assert.That(catalogPublic, Is.GreaterThan(catalogUpload));
        Assert.That(result.UploadedArchives, Is.EqualTo(1));
        Assert.That(result.ReusedArchives, Is.Zero);
        Assert.That(File.ReadAllText(config), Does.Contain(plan.CatalogUri.AbsoluteUri));
        Assert.That(File.ReadAllText(config), Does.Not.Contain("ACCESS"));
    }

    [Test]
    public void Publish_RefusesConflictingImmutableArchiveBeforeCatalog()
    {
        R2ReleaseUploadPlan plan = CreatePlan();
        var store = new FakeStore();
        store.Seed(plan.Archives[0].ObjectKey, Bytes(17));

        IOException exception = Assert.ThrowsAsync<IOException>(async () =>
            await new R2ReleasePublisher(store).PublishAsync(plan));

        Assert.That(exception.Message, Does.Contain("Refusing to overwrite"));
        Assert.That(store.Operations.Any(item => item.StartsWith("upload-file:", StringComparison.Ordinal)), Is.False);
        Assert.That(store.Operations.Any(item => item.StartsWith("upload-bytes:", StringComparison.Ordinal)), Is.False);
        Assert.That(File.Exists(config), Is.False);
    }

    [Test]
    public async Task Publish_ReusesMatchingArchiveButStillDownloadsBothReadPaths()
    {
        R2ReleaseUploadPlan plan = CreatePlan();
        var store = new FakeStore();
        store.Seed(plan.Archives[0].ObjectKey, File.ReadAllBytes(plan.Archives[0].LocalPath));

        R2ReleasePublishResult result = await new R2ReleasePublisher(store).PublishAsync(plan);

        Assert.That(result.UploadedArchives, Is.Zero);
        Assert.That(result.ReusedArchives, Is.EqualTo(1));
        Assert.That(store.Operations.Any(item => item.StartsWith("upload-file:", StringComparison.Ordinal)), Is.False);
        Assert.That(store.Operations, Does.Contain("origin:" + plan.Archives[0].ObjectKey));
        Assert.That(store.Operations, Does.Contain("public:" + plan.Archives[0].PublicUri.AbsolutePath));
    }

    [Test]
    public void Publish_LocalCatalogMutationStopsFinalPointerAndConfig()
    {
        R2ReleaseUploadPlan plan = CreatePlan();
        File.AppendAllText(plan.CatalogPath, " \n");
        var store = new FakeStore();

        Assert.ThrowsAsync<InvalidDataException>(async () =>
            await new R2ReleasePublisher(store).PublishAsync(plan));

        Assert.That(store.Operations.Any(item => item.StartsWith("upload-bytes:", StringComparison.Ordinal)), Is.False);
        Assert.That(File.Exists(config), Is.False);
    }

    [Test]
    public void SignatureV4_MatchesIndependentFixedVector()
    {
        const string emptySha = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

        string authorization = CloudflareR2ObjectStore.CreateAuthorization(
            HttpMethod.Put,
            new Uri("https://abc.r2.cloudflarestorage.com/bucket/releases/android/catalog.json"),
            emptySha,
            emptySha,
            "ACCESS",
            "SECRET",
            new DateTimeOffset(2026, 7, 24, 1, 2, 3, TimeSpan.Zero));

        Assert.That(authorization, Is.EqualTo(
            "AWS4-HMAC-SHA256 Credential=ACCESS/20260724/auto/s3/aws4_request, " +
            "SignedHeaders=host;x-amz-content-sha256;x-amz-date;x-amz-meta-sha256, " +
            "Signature=036cd4eb307af702c75977b2bb81d7417af5ec26dac62894bc4c072edb839616"));
    }

    [Test]
    public void Credentials_RejectEndpointThatCouldReceiveR2Secrets()
    {
        Assert.Throws<ArgumentException>(() => new CloudflareR2Credentials(
            new Uri("https://example.test"),
            "bucket",
            "ACCESS",
            "SECRET"));
        Assert.DoesNotThrow(() => new CloudflareR2Credentials(
            new Uri("https://abc.eu.r2.cloudflarestorage.com"),
            "bucket",
            "ACCESS",
            "SECRET"));
    }

    [Test]
    public void CreatePlan_RejectsNonHttpsPublicBaseAndEscapingPrefix()
    {
        Assert.Throws<ArgumentException>(() => R2ReleasePublisher.CreatePlan(new R2ReleasePublishRequest(
            release,
            new Uri("http://cards.example.test"),
            "releases/android",
            config)));
        Assert.Throws<ArgumentException>(() => R2ReleasePublisher.CreatePlan(new R2ReleasePublishRequest(
            release,
            new Uri("https://cards.example.test"),
            "../android",
            config)));
    }

    private R2ReleaseUploadPlan CreatePlan()
    {
        return R2ReleasePublisher.CreatePlan(new R2ReleasePublishRequest(
            release,
            new Uri("https://cards.example.test"),
            "releases/android",
            config));
    }

    private static byte[] Bytes(int count)
    {
        var bytes = new byte[count];
        for (int index = 0; index < bytes.Length; index++)
            bytes[index] = (byte)(index % 251);
        return bytes;
    }
}
