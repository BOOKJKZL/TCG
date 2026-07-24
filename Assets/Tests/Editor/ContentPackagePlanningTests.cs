using System;
using Gacha.Application;
using NUnit.Framework;

public class ContentPackagePlanningTests
{
    private const string HashA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string HashB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private sealed class Registry : IInstalledContentPackageRegistry
    {
        public InstalledContentPackage Installed { get; set; }
        public Exception Error { get; set; }
        public int Calls { get; private set; }

        public InstalledContentPackage Find(string packageId)
        {
            Calls++;
            if (Error != null)
                throw Error;
            return Installed;
        }
    }

    private sealed class Storage : IContentStorageProbe
    {
        public long AvailableBytes { get; set; }
        public Exception Error { get; set; }
        public int Calls { get; private set; }

        public long GetAvailableBytes()
        {
            Calls++;
            if (Error != null)
                throw Error;
            return AvailableBytes;
        }
    }

    [Test]
    public void Plan_NewPackageWithExactAtomicSpace_IsReadyToInstall()
    {
        var registry = new Registry();
        var storage = new Storage { AvailableBytes = 350 };
        var planner = new ContentPackagePlanner(registry, storage, 50);

        ContentInstallPlan plan = planner.Plan(Package(downloadBytes: 100, installedBytes: 200));

        Assert.That(plan.Status, Is.EqualTo(ContentInstallPlanStatus.Ready));
        Assert.That(plan.Action, Is.EqualTo(ContentInstallAction.Install));
        Assert.That(plan.RequiredBytes, Is.EqualTo(350));
        Assert.That(plan.AvailableBytes, Is.EqualTo(350));
        Assert.That(plan.CanStart, Is.True);
    }

    [Test]
    public void Plan_InsufficientSpace_ReportsRequiredAndAvailableBytes()
    {
        var planner = new ContentPackagePlanner(
            new Registry(),
            new Storage { AvailableBytes = 349 },
            50);

        ContentInstallPlan plan = planner.Plan(Package(downloadBytes: 100, installedBytes: 200));

        Assert.That(plan.Status, Is.EqualTo(ContentInstallPlanStatus.InsufficientSpace));
        Assert.That(plan.Action, Is.EqualTo(ContentInstallAction.Install));
        Assert.That(plan.RequiredBytes, Is.EqualTo(350));
        Assert.That(plan.AvailableBytes, Is.EqualTo(349));
        Assert.That(plan.CanStart, Is.False);
    }

    [Test]
    public void Plan_CurrentPackage_DoesNotProbeStorage()
    {
        var registry = new Registry
        {
            Installed = Installed(revision: 4, sha256: HashA)
        };
        var storage = new Storage { Error = new InvalidOperationException("must not run") };
        var planner = new ContentPackagePlanner(registry, storage);

        ContentInstallPlan plan = planner.Plan(Package(revision: 4, sha256: HashA));

        Assert.That(plan.Status, Is.EqualTo(ContentInstallPlanStatus.AlreadyCurrent));
        Assert.That(plan.Action, Is.EqualTo(ContentInstallAction.None));
        Assert.That(plan.RequiredBytes, Is.Zero);
        Assert.That(storage.Calls, Is.Zero);
    }

    [Test]
    public void Plan_HigherRevision_IsAnAtomicUpdate()
    {
        var registry = new Registry
        {
            Installed = Installed(revision: 3, sha256: HashA)
        };
        var planner = new ContentPackagePlanner(registry, new Storage { AvailableBytes = 1000 }, 0);

        ContentInstallPlan plan = planner.Plan(Package(revision: 4, sha256: HashB));

        Assert.That(plan.Status, Is.EqualTo(ContentInstallPlanStatus.Ready));
        Assert.That(plan.Action, Is.EqualTo(ContentInstallAction.Update));
        Assert.That(plan.InstalledPackage, Is.SameAs(registry.Installed));
    }

    [Test]
    public void Plan_SameRevisionWithDifferentHash_RepairsInsteadOfTreatingAsCurrent()
    {
        var registry = new Registry
        {
            Installed = Installed(revision: 4, sha256: HashA)
        };
        var planner = new ContentPackagePlanner(registry, new Storage { AvailableBytes = 1000 }, 0);

        ContentInstallPlan plan = planner.Plan(Package(revision: 4, sha256: HashB));

        Assert.That(plan.Status, Is.EqualTo(ContentInstallPlanStatus.Ready));
        Assert.That(plan.Action, Is.EqualTo(ContentInstallAction.Repair));
    }

    [Test]
    public void Plan_OlderCatalog_DoesNotDowngradeValidInstalledPackage()
    {
        var registry = new Registry
        {
            Installed = Installed(revision: 5, sha256: HashB)
        };
        var storage = new Storage();
        var planner = new ContentPackagePlanner(registry, storage);

        ContentInstallPlan plan = planner.Plan(Package(revision: 4, sha256: HashA));

        Assert.That(plan.Status, Is.EqualTo(ContentInstallPlanStatus.AlreadyCurrent));
        Assert.That(plan.Action, Is.EqualTo(ContentInstallAction.None));
        Assert.That(storage.Calls, Is.Zero);
    }

    [Test]
    public void Plan_InvalidHash_IsRejectedBeforeReadingLocalState()
    {
        var registry = new Registry { Error = new InvalidOperationException("must not run") };
        var storage = new Storage { Error = new InvalidOperationException("must not run") };
        var planner = new ContentPackagePlanner(registry, storage);

        ContentInstallPlan plan = planner.Plan(Package(sha256: "not-a-sha256"));

        Assert.That(plan.Status, Is.EqualTo(ContentInstallPlanStatus.InvalidPackage));
        Assert.That(plan.ErrorMessage, Does.Contain("SHA-256"));
        Assert.That(registry.Calls, Is.Zero);
        Assert.That(storage.Calls, Is.Zero);
    }

    [Test]
    public void Plan_StorageFailure_IsReturnedAsStateInsteadOfEscaping()
    {
        var planner = new ContentPackagePlanner(
            new Registry(),
            new Storage { Error = new InvalidOperationException("volume unavailable") });

        ContentInstallPlan plan = planner.Plan(Package());

        Assert.That(plan.Status, Is.EqualTo(ContentInstallPlanStatus.StorageUnavailable));
        Assert.That(plan.Action, Is.EqualTo(ContentInstallAction.Install));
        Assert.That(plan.AvailableBytes, Is.EqualTo(-1));
        Assert.That(plan.ErrorMessage, Does.Contain("volume unavailable"));
    }

    [Test]
    public void Plan_HugePackage_SaturatesRequiredBytesWithoutOverflow()
    {
        var planner = new ContentPackagePlanner(
            new Registry(),
            new Storage { AvailableBytes = long.MaxValue },
            1);

        ContentInstallPlan plan = planner.Plan(Package(
            downloadBytes: long.MaxValue - 10,
            installedBytes: 20));

        Assert.That(plan.RequiredBytes, Is.EqualTo(long.MaxValue));
        Assert.That(plan.Status, Is.EqualTo(ContentInstallPlanStatus.Ready));
    }

    private static ContentPackageDescriptor Package(
        long revision = 4,
        long downloadBytes = 100,
        long installedBytes = 200,
        string sha256 = HashA)
    {
        return new ContentPackageDescriptor(
            "en.base1",
            revision,
            "1.0.0",
            downloadBytes,
            installedBytes,
            sha256);
    }

    private static InstalledContentPackage Installed(long revision, string sha256)
    {
        return new InstalledContentPackage("en.base1", revision, "1.0.0", 200, sha256);
    }
}
