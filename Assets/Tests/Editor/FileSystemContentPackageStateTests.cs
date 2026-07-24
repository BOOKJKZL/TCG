using System;
using System.IO;
using Gacha.Application;
using Gacha.Infrastructure.Content;
using NUnit.Framework;

public class FileSystemContentPackageStateTests
{
    private string temporaryRoot;

    [SetUp]
    public void SetUp()
    {
        temporaryRoot = Path.Combine(Path.GetTempPath(), "gacha-content-state-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(temporaryRoot))
            Directory.Delete(temporaryRoot, true);
    }

    [Test]
    public void Registry_MissingReceipt_ReturnsNotInstalledWithoutCreatingState()
    {
        var registry = new FileSystemInstalledContentPackageRegistry(temporaryRoot);

        InstalledContentPackage installed = registry.Find("en.base1");

        Assert.That(installed, Is.Null);
        Assert.That(Directory.Exists(Path.Combine(
            temporaryRoot,
            FileSystemInstalledContentPackageRegistry.ReceiptDirectoryName)), Is.False);
    }

    [Test]
    public void Registry_ValidReceipt_LoadsInstalledPackageState()
    {
        WriteReceipt(
            "en.base1",
            "{\"PackageId\":\"en.base1\",\"Revision\":7,\"Version\":\"2.1.0\",\"InstalledBytes\":456,\"Sha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"}");
        var registry = new FileSystemInstalledContentPackageRegistry(temporaryRoot);

        InstalledContentPackage installed = registry.Find("en.base1");

        Assert.That(installed, Is.Not.Null);
        Assert.That(installed.PackageId, Is.EqualTo("en.base1"));
        Assert.That(installed.Revision, Is.EqualTo(7));
        Assert.That(installed.Version, Is.EqualTo("2.1.0"));
        Assert.That(installed.InstalledBytes, Is.EqualTo(456));
        Assert.That(installed.Sha256, Has.Length.EqualTo(64));
    }

    [Test]
    public void Registry_MismatchedReceiptId_IsRejected()
    {
        WriteReceipt(
            "en.base1",
            "{\"PackageId\":\"en.other\",\"Revision\":7,\"Version\":\"2.1.0\",\"InstalledBytes\":456,\"Sha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"}");
        var registry = new FileSystemInstalledContentPackageRegistry(temporaryRoot);

        Assert.Throws<InvalidDataException>(() => registry.Find("en.base1"));
    }

    [TestCase("../escape")]
    [TestCase("en/base1")]
    [TestCase("en\\base1")]
    public void Registry_PathLikePackageId_IsRejected(string packageId)
    {
        var registry = new FileSystemInstalledContentPackageRegistry(temporaryRoot);

        Assert.Throws<ArgumentException>(() => registry.Find(packageId));
    }

    [Test]
    public void StorageProbe_UsesNearestExistingParentWithoutCreatingContentRoot()
    {
        string missingContentRoot = Path.Combine(temporaryRoot, "missing", "Content");
        var storage = new FileSystemContentStorageProbe(missingContentRoot);

        long availableBytes = storage.GetAvailableBytes();

        Assert.That(availableBytes, Is.GreaterThan(0));
        Assert.That(Directory.Exists(missingContentRoot), Is.False);
    }

    [Test]
    public void Planner_WithCorruptReceipt_ReturnsStorageUnavailable()
    {
        WriteReceipt("en.base1", "{not-json}");
        var planner = new ContentPackagePlanner(
            new FileSystemInstalledContentPackageRegistry(temporaryRoot),
            new FileSystemContentStorageProbe(temporaryRoot),
            0);
        var package = new ContentPackageDescriptor(
            "en.base1",
            1,
            "1.0.0",
            100,
            200,
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

        ContentInstallPlan plan = planner.Plan(package);

        Assert.That(plan.Status, Is.EqualTo(ContentInstallPlanStatus.StorageUnavailable));
        Assert.That(plan.ErrorMessage, Does.Contain("Installed package state"));
    }

    private void WriteReceipt(string packageId, string json)
    {
        string receiptDirectory = Path.Combine(
            temporaryRoot,
            FileSystemInstalledContentPackageRegistry.ReceiptDirectoryName);
        Directory.CreateDirectory(receiptDirectory);
        File.WriteAllText(Path.Combine(receiptDirectory, packageId + ".json"), json);
    }
}
