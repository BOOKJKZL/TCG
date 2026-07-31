using System;
using Gacha.Application;
using NUnit.Framework;

public sealed class ContentDownloadPolicyTests
{
    private sealed class Storage : IContentStorageProbe
    {
        public long AvailableBytes = long.MaxValue;
        public bool Throw;

        public long GetAvailableBytes()
        {
            if (Throw)
                throw new InvalidOperationException("fixture storage unavailable");
            return AvailableBytes;
        }
    }

    private sealed class Network : IContentNetworkProbe
    {
        public ContentNetworkType Type = ContentNetworkType.WifiOrEthernet;
        public bool Throw;

        public ContentNetworkType GetNetworkType()
        {
            if (Throw)
                throw new InvalidOperationException("fixture network unavailable");
            return Type;
        }
    }

    private sealed class Store : IContentDownloadPreferenceStore
    {
        public ContentDownloadPreferences Value = new ContentDownloadPreferences(true);
        public int SaveCalls;
        public bool ThrowOnLoad;

        public ContentDownloadPreferences Load()
        {
            if (ThrowOnLoad)
                throw new InvalidOperationException("fixture preferences unavailable");
            return Value;
        }

        public void Save(ContentDownloadPreferences preferences)
        {
            Value = preferences;
            SaveCalls++;
        }
    }

    [Test]
    public void Evaluate_WifiAndEnoughStorageIsReadyWithOneReserve()
    {
        var storage = new Storage { AvailableBytes = 1000 };
        var service = Service(storage: storage, reserve: 100);

        ContentDownloadPreflightResult result = service.Evaluate(Selection(200, 300));

        Assert.That(result.Status, Is.EqualTo(ContentDownloadPreflightStatus.Ready));
        Assert.That(result.NetworkType, Is.EqualTo(ContentNetworkType.WifiOrEthernet));
        Assert.That(result.RequiredBytes, Is.EqualTo(600));
        Assert.That(result.AvailableBytes, Is.EqualTo(1000));
        Assert.That(result.CanStart, Is.True);
    }

    [Test]
    public void Evaluate_RejectsInsufficientOrUnreadableStorageBeforeNetworkStart()
    {
        var storage = new Storage { AvailableBytes = 599 };
        ContentDownloadPolicyService service = Service(storage: storage, reserve: 100);

        Assert.That(service.Evaluate(Selection(200, 300)).Status,
            Is.EqualTo(ContentDownloadPreflightStatus.InsufficientSpace));
        storage.Throw = true;
        ContentDownloadPreflightResult unavailable = service.Evaluate(Selection(200, 300));
        Assert.That(unavailable.Status, Is.EqualTo(ContentDownloadPreflightStatus.StorageUnavailable));
        Assert.That(unavailable.ErrorMessage, Does.Contain("fixture storage unavailable"));
    }

    [Test]
    public void Evaluate_OfflineAndUnknownNetworkFailClosed()
    {
        var network = new Network { Type = ContentNetworkType.Offline };
        ContentDownloadPolicyService service = Service(network: network);

        Assert.That(service.Evaluate(Selection(200, 300)).Status,
            Is.EqualTo(ContentDownloadPreflightStatus.Offline));
        network.Throw = true;
        Assert.That(service.Evaluate(Selection(200, 300)).Status,
            Is.EqualTo(ContentDownloadPreflightStatus.NetworkUnavailable));
    }

    [Test]
    public void Evaluate_LargeMobileDownloadWaitsForWifiByDefault()
    {
        var network = new Network { Type = ContentNetworkType.MobileData };
        ContentDownloadPolicyService service = Service(network: network, threshold: 100);

        ContentDownloadPreflightResult result = service.Evaluate(Selection(100, 1));

        Assert.That(result.Status, Is.EqualTo(ContentDownloadPreflightStatus.WaitingForWifi));
        Assert.That(result.NetworkType, Is.EqualTo(ContentNetworkType.MobileData));
    }

    [Test]
    public void Evaluate_MobileDownloadRequiresExplicitConfirmationWhenWifiOnlyIsOff()
    {
        var network = new Network { Type = ContentNetworkType.MobileData };
        var store = new Store { Value = new ContentDownloadPreferences(false) };
        ContentDownloadPolicyService service = Service(network: network, store: store, threshold: 100);

        Assert.That(service.Evaluate(Selection(500, 1)).Status,
            Is.EqualTo(ContentDownloadPreflightStatus.CellularConfirmationRequired));
        Assert.That(service.Evaluate(Selection(500, 1), true).Status,
            Is.EqualTo(ContentDownloadPreflightStatus.Ready));
    }

    [Test]
    public void Preferences_DefaultToWifiOnlyAndPersistChanges()
    {
        var store = new Store { ThrowOnLoad = true };
        ContentDownloadPolicyService service = Service(store: store);
        int changes = 0;
        service.Changed += _ => changes++;

        Assert.That(service.Current.WifiOnlyForLargeDownloads, Is.True);
        service.SetWifiOnlyForLargeDownloads(false);

        Assert.That(service.Current.WifiOnlyForLargeDownloads, Is.False);
        Assert.That(store.SaveCalls, Is.EqualTo(1));
        Assert.That(changes, Is.EqualTo(1));
    }

    [Test]
    public void Evaluate_NoSelectionAndCurrentSelectionDoNotProbeStorage()
    {
        var storage = new Storage { Throw = true };
        ContentDownloadPolicyService service = Service(storage: storage);

        Assert.That(service.Evaluate(null).Status,
            Is.EqualTo(ContentDownloadPreflightStatus.NoSelection));
        Assert.That(service.Evaluate(Selection(0, 0)).Status,
            Is.EqualTo(ContentDownloadPreflightStatus.AlreadyCurrent));
    }

    private static ContentDownloadPolicyService Service(
        Storage storage = null,
        Network network = null,
        Store store = null,
        long threshold = 1000,
        long reserve = 0) =>
        new ContentDownloadPolicyService(
            storage ?? new Storage(),
            network ?? new Network(),
            store ?? new Store(),
            threshold,
            reserve);

    private static ContentPackageSelectionSummary Selection(long downloadBytes, long installedBytes)
    {
        ContentPackageCatalog catalog = new ContentPackageCatalog(
            2,
            1,
            new[]
            {
                new ContentPackageCatalogEntry(
                    new ContentPackageDescriptor(
                        "fixture",
                        "fixture",
                        1,
                        "1.0.0",
                        Math.Max(1, downloadBytes),
                        Math.Max(1, installedBytes),
                        new string('a', 64)),
                    new Uri("https://content.example.test/fixture.zip"))
            });
        if (downloadBytes <= 0)
        {
            var installed = new InstalledContentPackage(
                "fixture", "fixture", 1, "1.0.0", Math.Max(1, installedBytes), new string('a', 64));
            return ContentPackageLibrary.SummarizeSelection(
                catalog,
                new[] { "fixture" },
                _ => installed);
        }
        return ContentPackageLibrary.SummarizeSelection(catalog, new[] { "fixture" });
    }
}
