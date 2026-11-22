using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Gacha.Application;
using Gacha.Infrastructure.Content;
using NUnit.Framework;

public class HttpContentPackageInstallCoordinatorFactoryTests
{
    private const string HashA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string HashB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private string downloads;

    [SetUp]
    public void SetUp() => downloads = Path.Combine(Path.GetTempPath(), "factory-" + Guid.NewGuid().ToString("N"));

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(downloads)) Directory.Delete(downloads, true);
    }

    private sealed class BlockingDownload : HttpMessageHandler
    {
        public TaskCompletionSource<Uri> Started { get; } = new TaskCompletionSource<Uri>();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Started.TrySetResult(request.RequestUri);
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("Unexpected completion of a blocked download.");
        }
    }

    private sealed class Registry : IInstalledContentPackageRegistry
    {
        public InstalledContentPackage Find(string packageId) => null;
    }

    private sealed class Storage : IContentStorageProbe
    {
        public long GetAvailableBytes() => long.MaxValue;
    }

    private sealed class Installer : IContentPackageInstaller
    {
        public Task<ContentPackageInstallResult> InstallAsync(
            ContentInstallPlan plan,
            string archivePath,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ContentPackageInstallResult.Failure(
                ContentPackageInstallStatus.Failed,
                "fixture"));
        }
    }

    [Test]
    public void Create_SameCatalogPackageReturnsSameOperation()
    {
        using (var client = new HttpClient())
        using (var factory = Factory(client))
        {
            ContentPackageCatalog catalog = Catalog(1, HashA);

            ContentPackageInstallCoordinator first = factory.Create(catalog, "en.base1");
            ContentPackageInstallCoordinator second = factory.Create(catalog, "en.base1");

            Assert.That(second, Is.SameAs(first));
        }
    }

    [Test]
    public async Task Create_NewRevisionRequiresOldOperationToBeCancelled()
    {
        var transport = new BlockingDownload();
        using (var client = new HttpClient(transport))
        using (var factory = Factory(client))
        {
            ContentPackageInstallCoordinator first = factory.Create(Catalog(1, HashA), "en.base1");
            Task started = first.StartAsync();
            try
            {
                Assert.That(await Task.WhenAny(transport.Started.Task, Task.Delay(5000)), Is.SameAs(transport.Started.Task));
                Assert.Throws<InvalidOperationException>(() => factory.Create(Catalog(2, HashB), "en.base1"));
            }
            finally
            {
                await first.CancelAsync();
                await started;
            }
            ContentPackageInstallCoordinator second = factory.Create(Catalog(2, HashB), "en.base1");

            Assert.That(second, Is.Not.SameAs(first));
        }
    }

    [Test]
    public async Task Create_IdleOperationUsesRefreshedRevisionAndMirror()
    {
        var transport = new BlockingDownload();
        using (var client = new HttpClient(transport))
        using (var factory = Factory(client))
        {
            var initial = factory.Create(Catalog(1, HashA), "en.base1");
            var updated = factory.Create(Catalog(2, HashB), "en.base1");
            Assert.That(updated, Is.Not.SameAs(initial));
            var mirror = factory.Create(Catalog(2, HashB, "mirror.example.test"), "en.base1");
            Task started = mirror.StartAsync();
            try
            {
                Assert.That(await Task.WhenAny(transport.Started.Task, Task.Delay(5000)), Is.SameAs(transport.Started.Task));
                Assert.That((await transport.Started.Task).Host, Is.EqualTo("mirror.example.test"));
            }
            finally
            {
                await mirror.CancelAsync();
                await started;
            }
        }
    }

    [Test]
    public void Create_UnknownPackageAndDisposedFactoryAreRejected()
    {
        using (var client = new HttpClient())
        {
            var factory = Factory(client);
            ContentPackageCatalog catalog = Catalog(1, HashA);

            Assert.Throws<KeyNotFoundException>(() => factory.Create(catalog, "missing"));
            factory.Dispose();
            Assert.Throws<ObjectDisposedException>(() => factory.Create(catalog, "en.base1"));
        }
    }

    private HttpContentPackageInstallCoordinatorFactory Factory(HttpClient client)
    {
        return new HttpContentPackageInstallCoordinatorFactory(
            downloads,
            new ContentPackagePlanner(new Registry(), new Storage(), 0),
            new Installer(),
            client);
    }

    private static ContentPackageCatalog Catalog(long revision, string sha256, string host = "content.example.test")
    {
        var package = new ContentPackageDescriptor(
            "en.base1",
            "en/base1",
            revision,
            revision + ".0.0",
            100,
            200,
            sha256);
        return new ContentPackageCatalog(
            1,
            revision,
            new[]
            {
                new ContentPackageCatalogEntry(
                    package,
                    new Uri("https://" + host + "/packages/" + sha256 + ".zip"))
            });
    }
}
