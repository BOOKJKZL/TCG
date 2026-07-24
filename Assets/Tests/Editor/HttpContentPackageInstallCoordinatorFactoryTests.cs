using System;
using System.Collections.Generic;
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
        using (var client = new HttpClient())
        using (var factory = Factory(client))
        {
            ContentPackageInstallCoordinator first = factory.Create(Catalog(1, HashA), "en.base1");

            Assert.Throws<InvalidOperationException>(() =>
                factory.Create(Catalog(2, HashB), "en.base1"));
            await first.CancelAsync();
            ContentPackageInstallCoordinator second = factory.Create(Catalog(2, HashB), "en.base1");

            Assert.That(second, Is.Not.SameAs(first));
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

    private static HttpContentPackageInstallCoordinatorFactory Factory(HttpClient client)
    {
        return new HttpContentPackageInstallCoordinatorFactory(
            "fixture-downloads",
            new ContentPackagePlanner(new Registry(), new Storage(), 0),
            new Installer(),
            client);
    }

    private static ContentPackageCatalog Catalog(long revision, string sha256)
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
                    new Uri("https://content.example.test/packages/" + sha256 + ".zip"))
            });
    }
}
