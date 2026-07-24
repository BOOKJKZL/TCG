using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Gacha.Application;
using Gacha.Infrastructure.Content;
using NUnit.Framework;

public class ContentPackageInstallIntegrationTests
{
    private sealed class Storage : IContentStorageProbe
    {
        public long GetAvailableBytes() => long.MaxValue;
    }

    private sealed class QueueHandler : HttpMessageHandler
    {
        private readonly Queue<byte[]> responses;

        public QueueHandler(params byte[][] responses)
        {
            this.responses = new Queue<byte[]>(responses);
        }

        public int Calls { get; private set; }
        public readonly List<string> Ranges = new List<string>();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            Ranges.Add(request.Headers.Range?.ToString());
            if (responses.Count == 0)
                throw new InvalidOperationException("HTTP fixture has no queued response.");
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(responses.Dequeue()),
                RequestMessage = request
            };
            return Task.FromResult(response);
        }
    }

    private sealed class ResponseHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> response;

        public ResponseHandler(Func<HttpRequestMessage, HttpResponseMessage> response)
        {
            this.response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HttpResponseMessage result = response(request);
            result.RequestMessage = request;
            return Task.FromResult(result);
        }
    }

    private string temporaryRoot;
    private string contentRoot;
    private string downloadRoot;

    [SetUp]
    public void SetUp()
    {
        temporaryRoot = Path.Combine(Path.GetTempPath(), "gacha-install-flow-" + Guid.NewGuid().ToString("N"));
        contentRoot = Path.Combine(temporaryRoot, "Content");
        downloadRoot = Path.Combine(temporaryRoot, "Downloads");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(temporaryRoot))
            Directory.Delete(temporaryRoot, true);
    }

    [Test]
    public async Task CatalogToHttpDownloadToAtomicInstall_CompletesAndPublishesReceipt()
    {
        PackageArchive archive = Archive("version-one", "card-image-one");
        ContentPackageCatalog catalog = Catalog(1, "1.0.0", archive);
        ContentPackageCatalogEntry entry = catalog.Packages[0];
        var handler = new QueueHandler(archive.Bytes);

        using (var client = new HttpClient(handler))
        using (var source = new HttpContentPackageByteSource(catalog, client))
        {
            ContentPackageInstallCoordinator coordinator = Coordinator(entry.Package, source);

            ContentPackageOperationSnapshot result = await coordinator.StartAsync();

            Assert.That(result.State, Is.EqualTo(ContentPackageOperationState.Succeeded));
            Assert.That(result.InstallResult.Succeeded, Is.True);
            Assert.That(File.ReadAllText(Path.Combine(contentRoot, "en", "base1", "manifest.json")),
                Is.EqualTo("version-one"));
            Assert.That(File.ReadAllText(Path.Combine(contentRoot, "en", "base1", "images", "card.bin")),
                Is.EqualTo("card-image-one"));
            InstalledContentPackage receipt = new FileSystemInstalledContentPackageRegistry(contentRoot)
                .Find("en.base1");
            Assert.That(receipt.Revision, Is.EqualTo(1));
            Assert.That(receipt.Sha256, Is.EqualTo(archive.Sha256));
            Assert.That(handler.Calls, Is.EqualTo(1));
            Assert.That(handler.Ranges, Is.EqualTo(new string[] { null }));
            Assert.That(Directory.Exists(downloadRoot), Is.False);
        }
    }

    [Test]
    public async Task CorruptUpdate_IsDiscardedWithoutTouchingInstalledContent_ThenRetrySucceeds()
    {
        PackageArchive oldArchive = Archive("version-one", "card-image-one");
        ContentPackageDescriptor oldPackage = Descriptor(1, "1.0.0", oldArchive);
        await InstallInitialPackage(oldPackage, oldArchive.Bytes);

        PackageArchive updateArchive = Archive("version-two", "card-image-two-expanded");
        byte[] corruptBytes = (byte[])updateArchive.Bytes.Clone();
        corruptBytes[corruptBytes.Length / 2] ^= 0x5a;
        ContentPackageCatalog catalog = Catalog(2, "2.0.0", updateArchive);
        ContentPackageCatalogEntry entry = catalog.Packages[0];
        var handler = new QueueHandler(corruptBytes, updateArchive.Bytes);

        using (var client = new HttpClient(handler))
        using (var source = new HttpContentPackageByteSource(catalog, client))
        {
            ContentPackageInstallCoordinator coordinator = Coordinator(entry.Package, source);
            int failures = 0;
            coordinator.FailureReported += _ => failures++;

            ContentPackageOperationSnapshot failed = await coordinator.StartAsync();

            Assert.That(failed.State, Is.EqualTo(ContentPackageOperationState.Failed));
            Assert.That(failed.FailureStage, Is.EqualTo(ContentPackageOperationFailureStage.Install));
            Assert.That(failed.InstallResult.Status, Is.EqualTo(ContentPackageInstallStatus.IntegrityMismatch));
            Assert.That(File.ReadAllText(Path.Combine(contentRoot, "en", "base1", "manifest.json")),
                Is.EqualTo("version-one"));
            Assert.That(new FileSystemInstalledContentPackageRegistry(contentRoot).Find("en.base1").Revision,
                Is.EqualTo(1));
            Assert.That(File.Exists(Path.Combine(downloadRoot, "en.base1.zip")), Is.False);
            Assert.That(File.Exists(Path.Combine(downloadRoot, "en.base1.part")), Is.False);

            ContentPackageOperationSnapshot completed = await coordinator.RetryAsync();

            Assert.That(completed.State, Is.EqualTo(ContentPackageOperationState.Succeeded));
            Assert.That(File.ReadAllText(Path.Combine(contentRoot, "en", "base1", "manifest.json")),
                Is.EqualTo("version-two"));
            Assert.That(new FileSystemInstalledContentPackageRegistry(contentRoot).Find("en.base1").Revision,
                Is.EqualTo(2));
            Assert.That(handler.Calls, Is.EqualTo(2));
            Assert.That(handler.Ranges, Is.EqualTo(new string[] { null, null }));
            Assert.That(failures, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task InstalledPackage_RemoveResetAndReinstall_CompletesInSameCoordinator()
    {
        PackageArchive archive = Archive("version-one", "card-image-one");
        ContentPackageCatalog catalog = Catalog(1, "1.0.0", archive);
        ContentPackageCatalogEntry entry = catalog.Packages[0];
        var handler = new QueueHandler(archive.Bytes, archive.Bytes);

        using (var client = new HttpClient(handler))
        using (var source = new HttpContentPackageByteSource(catalog, client))
        {
            ContentPackageInstallCoordinator coordinator = Coordinator(entry.Package, source);
            ContentPackageOperationSnapshot installed = await coordinator.StartAsync();
            var lifecycle = new FileSystemContentPackageLifecycleService(contentRoot);

            ContentPackageRemovalResult removed = await lifecycle.RemoveAsync(entry.Package.PackageId);
            ContentPackageOperationSnapshot reset = await coordinator.ResetAfterRemovalAsync();
            ContentPackageOperationSnapshot reinstalled = await coordinator.StartAsync();

            Assert.That(installed.State, Is.EqualTo(ContentPackageOperationState.Succeeded));
            Assert.That(removed.Succeeded, Is.True, removed.ErrorMessage);
            Assert.That(reset.State, Is.EqualTo(ContentPackageOperationState.Idle));
            Assert.That(reinstalled.State, Is.EqualTo(ContentPackageOperationState.Succeeded));
            Assert.That(handler.Calls, Is.EqualTo(2));
            Assert.That(lifecycle.FindInstalled(entry.Package.PackageId), Is.Not.Null);
            Assert.That(File.ReadAllText(Path.Combine(contentRoot, "en", "base1", "manifest.json")),
                Is.EqualTo("version-one"));
        }
    }

    [Test]
    public async Task InterruptedDownload_NewCoordinatorResumesPersistedBytesAfterRestart()
    {
        PackageArchive archive = Archive("restart-version", "restart-card-image");
        ContentPackageCatalog catalog = Catalog(1, "1.0.0", archive);
        ContentPackageCatalogEntry entry = catalog.Packages[0];
        int persistedBytes = Math.Max(1, archive.Bytes.Length / 2);

        var firstHandler = new ResponseHandler(request =>
        {
            Assert.That(request.Headers.Range, Is.Null);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Slice(archive.Bytes, 0, persistedBytes))
            };
            response.Content.Headers.ContentLength = archive.Bytes.Length;
            return response;
        });
        using (var firstClient = new HttpClient(firstHandler))
        using (var firstSource = new HttpContentPackageByteSource(catalog, firstClient))
        {
            ContentPackageOperationSnapshot interrupted = await Coordinator(entry.Package, firstSource).StartAsync();

            Assert.That(interrupted.State, Is.EqualTo(ContentPackageOperationState.Failed));
            Assert.That(interrupted.FailureStage, Is.EqualTo(ContentPackageOperationFailureStage.Download));
            Assert.That(new FileInfo(Path.Combine(downloadRoot, "en.base1.part")).Length,
                Is.EqualTo(persistedBytes));
        }

        var resumedHandler = new ResponseHandler(request =>
        {
            Assert.That(request.Headers.Range?.ToString(), Is.EqualTo("bytes=" + persistedBytes + "-"));
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(Slice(
                    archive.Bytes,
                    persistedBytes,
                    archive.Bytes.Length - persistedBytes))
            };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(
                persistedBytes,
                archive.Bytes.Length - 1,
                archive.Bytes.Length);
            return response;
        });
        using (var resumedClient = new HttpClient(resumedHandler))
        using (var resumedSource = new HttpContentPackageByteSource(catalog, resumedClient))
        {
            ContentPackageOperationSnapshot completed = await Coordinator(entry.Package, resumedSource).StartAsync();

            Assert.That(completed.State, Is.EqualTo(ContentPackageOperationState.Succeeded));
            Assert.That(File.ReadAllText(Path.Combine(contentRoot, "en", "base1", "manifest.json")),
                Is.EqualTo("restart-version"));
            Assert.That(new FileSystemInstalledContentPackageRegistry(contentRoot).Find("en.base1"), Is.Not.Null);
            Assert.That(Directory.Exists(downloadRoot), Is.False);
        }
    }

    private ContentPackageInstallCoordinator Coordinator(
        ContentPackageDescriptor package,
        IContentPackageByteSource source)
    {
        var registry = new FileSystemInstalledContentPackageRegistry(contentRoot);
        var planner = new ContentPackagePlanner(registry, new Storage(), 0);
        var transfer = new FileSystemContentPackageTransfer(downloadRoot, source);
        var installer = new FileSystemContentPackageInstaller(contentRoot);
        return new ContentPackageInstallCoordinator(package, planner, transfer, installer);
    }

    private async Task InstallInitialPackage(ContentPackageDescriptor package, byte[] archiveBytes)
    {
        Directory.CreateDirectory(temporaryRoot);
        string path = Path.Combine(temporaryRoot, "initial.zip");
        File.WriteAllBytes(path, archiveBytes);
        var planner = new ContentPackagePlanner(
            new FileSystemInstalledContentPackageRegistry(contentRoot),
            new Storage(),
            0);
        ContentPackageInstallResult result = await new FileSystemContentPackageInstaller(contentRoot)
            .InstallAsync(planner.Plan(package), path);
        Assert.That(result.Succeeded, Is.True, result.ErrorMessage);
    }

    private static ContentPackageCatalog Catalog(long revision, string version, PackageArchive archive)
    {
        string sha = archive.Sha256;
        string json = "{\"schemaVersion\":1,\"revision\":" + revision +
                      ",\"packages\":[{\"packageId\":\"en.base1\"" +
                      ",\"installRelativePath\":\"en/base1\"" +
                      ",\"revision\":" + revision +
                      ",\"version\":\"" + version + "\"" +
                      ",\"downloadBytes\":" + archive.Bytes.Length +
                      ",\"installedBytes\":" + archive.InstalledBytes +
                      ",\"sha256\":\"" + sha + "\"" +
                      ",\"archiveUrl\":\"packages/en.base1/" + sha + ".zip\"}]}";
        ContentPackageCatalogLoadResult result = new JsonContentPackageCatalogReader().Read(
            json,
            new Uri("https://content.example.test/releases/catalog.json"));
        Assert.That(result.Succeeded, Is.True, result.ErrorMessage);
        return result.Catalog;
    }

    private static ContentPackageDescriptor Descriptor(
        long revision,
        string version,
        PackageArchive archive)
    {
        return new ContentPackageDescriptor(
            "en.base1",
            "en/base1",
            revision,
            version,
            archive.Bytes.Length,
            archive.InstalledBytes,
            archive.Sha256);
    }

    private static PackageArchive Archive(string manifest, string image)
    {
        byte[] manifestBytes = Encoding.UTF8.GetBytes(manifest);
        byte[] imageBytes = Encoding.UTF8.GetBytes(image);
        using (var output = new MemoryStream())
        {
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            {
                Write(zip, "manifest.json", manifestBytes);
                Write(zip, "images/card.bin", imageBytes);
            }
            byte[] bytes = output.ToArray();
            using (SHA256 sha = SHA256.Create())
            {
                string hash = BitConverter.ToString(sha.ComputeHash(bytes))
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
                return new PackageArchive(bytes, manifestBytes.Length + imageBytes.Length, hash);
            }
        }
    }

    private static void Write(ZipArchive archive, string path, byte[] bytes)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
        using (Stream stream = entry.Open())
            stream.Write(bytes, 0, bytes.Length);
    }

    private static byte[] Slice(byte[] source, int offset, int count)
    {
        var result = new byte[count];
        Buffer.BlockCopy(source, offset, result, 0, count);
        return result;
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
