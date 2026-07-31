using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Gacha.Application;
using NUnit.Framework;

public sealed class ContentPackageInstallQueueTests
{
    private sealed class ConcurrencyProbe
    {
        private int current;
        private int maximum;

        public int Maximum => maximum;

        public void Enter()
        {
            int value = Interlocked.Increment(ref current);
            int observed;
            while (value > (observed = maximum))
                Interlocked.CompareExchange(ref maximum, value, observed);
        }

        public void Exit() => Interlocked.Decrement(ref current);
    }

    private sealed class Registry : IInstalledContentPackageRegistry
    {
        private readonly ConcurrentDictionary<string, InstalledContentPackage> values =
            new ConcurrentDictionary<string, InstalledContentPackage>(StringComparer.Ordinal);

        public InstalledContentPackage Find(string packageId)
        {
            values.TryGetValue(packageId, out InstalledContentPackage value);
            return value;
        }

        public void Add(InstalledContentPackage value) => values[value.PackageId] = value;
    }

    private sealed class Storage : IContentStorageProbe
    {
        public long GetAvailableBytes() => long.MaxValue;
    }

    private sealed class Transfer : IContentPackageTransfer
    {
        private readonly ConcurrencyProbe probe;
        private readonly IList<string> events;
        private readonly bool failOnce;
        private long bytes;
        private int attempts;

        public Transfer(ConcurrencyProbe probe, IList<string> events, bool failOnce)
        {
            this.probe = probe;
            this.events = events;
            this.failOnce = failOnce;
        }

        public long GetDownloadedBytes(ContentPackageDescriptor package) => bytes;

        public async Task DownloadAsync(
            ContentPackageDescriptor package,
            long offset,
            IProgress<long> persistedBytesProgress,
            CancellationToken cancellationToken)
        {
            probe.Enter();
            lock (events) events.Add("download-start:" + package.PackageId);
            try
            {
                bytes = Math.Max(offset, package.DownloadBytes / 2);
                persistedBytesProgress?.Report(bytes);
                await Task.Delay(60, cancellationToken);
                if (failOnce && Interlocked.Increment(ref attempts) == 1)
                    throw new InvalidOperationException("fixture download failed");
                bytes = package.DownloadBytes;
                persistedBytesProgress?.Report(bytes);
                lock (events) events.Add("download-end:" + package.PackageId);
            }
            finally
            {
                probe.Exit();
            }
        }

        public void DeletePartial(ContentPackageDescriptor package) => bytes = 0;
        public string GetArchivePath(ContentPackageDescriptor package) =>
            bytes == package.DownloadBytes ? package.PackageId + ".zip" : null;
    }

    private sealed class Installer : IContentPackageInstaller
    {
        private readonly Registry registry;
        private readonly ConcurrencyProbe probe;
        private readonly IList<string> events;

        public Installer(Registry registry, ConcurrencyProbe probe, IList<string> events)
        {
            this.registry = registry;
            this.probe = probe;
            this.events = events;
        }

        public async Task<ContentPackageInstallResult> InstallAsync(
            ContentInstallPlan plan,
            string archivePath,
            CancellationToken cancellationToken = default)
        {
            probe.Enter();
            lock (events) events.Add("install-start:" + plan.Package.PackageId);
            try
            {
                await Task.Delay(25, cancellationToken);
                var installed = new InstalledContentPackage(
                    plan.Package.PackageId,
                    plan.Package.InstallRelativePath,
                    plan.Package.Revision,
                    plan.Package.Version,
                    plan.Package.InstalledBytes,
                    plan.Package.Sha256);
                registry.Add(installed);
                lock (events) events.Add("install-end:" + plan.Package.PackageId);
                return ContentPackageInstallResult.Success(installed);
            }
            finally
            {
                probe.Exit();
            }
        }
    }

    private sealed class Factory : IContentPackageInstallCoordinatorFactory, IDisposable
    {
        private readonly Registry registry = new Registry();
        private readonly ConcurrencyProbe downloads;
        private readonly ConcurrencyProbe installs;
        private readonly IList<string> events;
        private readonly string failOncePackageId;
        private readonly SemaphoreSlim installGate = new SemaphoreSlim(1, 1);

        public Factory(
            ConcurrencyProbe downloads,
            ConcurrencyProbe installs,
            IList<string> events,
            string failOncePackageId = null)
        {
            this.downloads = downloads;
            this.installs = installs;
            this.events = events;
            this.failOncePackageId = failOncePackageId;
        }

        public int CreateCalls { get; private set; }

        public ContentPackageInstallCoordinator Create(ContentPackageCatalog catalog, string packageId)
        {
            CreateCalls++;
            ContentPackageDescriptor package = catalog.Find(packageId).Package;
            return new ContentPackageInstallCoordinator(
                package,
                new ContentPackagePlanner(registry, new Storage(), 0),
                new Transfer(downloads, events, packageId == failOncePackageId),
                new Installer(registry, installs, events),
                installGate);
        }

        public void Dispose() => installGate.Dispose();
    }

    [Test]
    public async Task Queue_LimitsDownloadsToTwoAndSerializesInstallation()
    {
        ContentPackageCatalog catalog = Catalog(Enumerable.Range(0, 6)
            .Select(index => Entry("package." + index)).ToArray());
        var downloads = new ConcurrencyProbe();
        var installs = new ConcurrencyProbe();
        var events = new List<string>();
        using var factory = new Factory(downloads, installs, events);
        var queue = new ContentPackageInstallQueue(catalog, factory, 2);

        queue.EnqueueSelection(catalog.Packages.Select(value => value.Package.PackageId));
        ContentPackageQueueSnapshot result = await queue.StartAsync();

        Assert.That(result.SucceededCount, Is.EqualTo(6));
        Assert.That(result.FailedCount, Is.Zero);
        Assert.That(downloads.Maximum, Is.EqualTo(2));
        Assert.That(installs.Maximum, Is.EqualTo(1));
        Assert.That(factory.CreateCalls, Is.EqualTo(6));
    }

    [Test]
    public async Task Queue_InstallsDependenciesBeforeStartingDependents()
    {
        ContentPackageCatalogEntry taxonomy = Entry("taxonomy");
        ContentPackageCatalogEntry links = Entry("links", "taxonomy");
        ContentPackageCatalogEntry artwork = Entry("artwork", "taxonomy");
        ContentPackageCatalog catalog = Catalog(taxonomy, links, artwork);
        var events = new List<string>();
        using var factory = new Factory(new ConcurrencyProbe(), new ConcurrencyProbe(), events);
        var queue = new ContentPackageInstallQueue(catalog, factory, 2);

        ContentPackageQueueSnapshot enqueued = queue.EnqueueSelection(new[] { "links", "artwork" });
        ContentPackageQueueSnapshot result = await queue.StartAsync();

        Assert.That(enqueued.Items.Select(value => value.PackageId),
            Is.EqualTo(new[] { "taxonomy", "artwork", "links" }));
        Assert.That(result.SucceededCount, Is.EqualTo(3));
        int taxonomyInstalled = events.IndexOf("install-end:taxonomy");
        Assert.That(events.IndexOf("download-start:links"), Is.GreaterThan(taxonomyInstalled));
        Assert.That(events.IndexOf("download-start:artwork"), Is.GreaterThan(taxonomyInstalled));
    }

    [Test]
    public async Task Queue_PauseThenResumePreservesWorkAndCompletes()
    {
        ContentPackageCatalog catalog = Catalog(Entry("first"), Entry("second"));
        using var factory = new Factory(
            new ConcurrencyProbe(), new ConcurrencyProbe(), new List<string>());
        var queue = new ContentPackageInstallQueue(catalog, factory, 2);
        queue.EnqueueSelection(new[] { "first", "second" });

        Task<ContentPackageQueueSnapshot> running = queue.StartAsync();
        await Task.Delay(10);
        ContentPackageQueueSnapshot paused = await queue.PauseAsync();
        ContentPackageQueueSnapshot completed = await queue.ResumeAsync();

        Assert.That(paused.Paused, Is.True);
        Assert.That(paused.Items.Count(value => value.State == ContentPackageQueueItemState.Paused),
            Is.GreaterThanOrEqualTo(1));
        Assert.That(completed.SucceededCount, Is.EqualTo(2));
        Assert.That((await running).Paused, Is.True);
    }

    [Test]
    public async Task Queue_RetryFailedRestartsOnlyFailedPackage()
    {
        ContentPackageCatalog catalog = Catalog(Entry("stable"), Entry("retry"));
        using var factory = new Factory(
            new ConcurrencyProbe(), new ConcurrencyProbe(), new List<string>(), "retry");
        var queue = new ContentPackageInstallQueue(catalog, factory, 2);
        queue.EnqueueSelection(new[] { "stable", "retry" });

        ContentPackageQueueSnapshot failed = await queue.StartAsync();
        ContentPackageQueueSnapshot completed = await queue.RetryFailedAsync();

        Assert.That(failed.SucceededCount, Is.EqualTo(1));
        Assert.That(failed.FailedCount, Is.EqualTo(1));
        Assert.That(completed.SucceededCount, Is.EqualTo(2));
        Assert.That(factory.CreateCalls, Is.EqualTo(2));
    }

    [Test]
    public async Task Queue_PublishesTerminalSnapshotWhenDependencyFails()
    {
        ContentPackageCatalog catalog = Catalog(
            Entry("required"),
            Entry("dependent", "required"));
        using var factory = new Factory(
            new ConcurrencyProbe(), new ConcurrencyProbe(), new List<string>(), "required");
        var queue = new ContentPackageInstallQueue(catalog, factory, 2);
        var snapshots = new List<ContentPackageQueueSnapshot>();
        queue.Changed += snapshots.Add;

        queue.EnqueueSelection(new[] { "dependent" });
        ContentPackageQueueSnapshot result = await queue.StartAsync();

        Assert.That(result.IsComplete, Is.True);
        Assert.That(result.FailedCount, Is.EqualTo(2));
        Assert.That(snapshots.Last().IsComplete, Is.True);
        Assert.That(snapshots.Last().FailedCount, Is.EqualTo(2));
    }

    [TestCase(0)]
    [TestCase(4)]
    public void Queue_RejectsConcurrencyOutsideOneToThree(int value)
    {
        ContentPackageCatalog catalog = Catalog(Entry("fixture"));
        using var factory = new Factory(
            new ConcurrencyProbe(), new ConcurrencyProbe(), new List<string>());

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ContentPackageInstallQueue(catalog, factory, value));
    }

    private static ContentPackageCatalog Catalog(params ContentPackageCatalogEntry[] entries) =>
        new ContentPackageCatalog(2, 1, entries);

    private static ContentPackageCatalogEntry Entry(string id, params string[] dependencies)
    {
        string hash = new string('a', 64);
        return new ContentPackageCatalogEntry(
            new ContentPackageDescriptor(id, id, 1, "1.0.0", 100, 200, hash),
            new Uri("https://content.example.test/packages/" + id + "/" + hash + ".zip"),
            new ContentPackageMetadata(
                "fixture",
                new Dictionary<string, string> { ["en"] = id },
                dependencies: dependencies));
    }
}
