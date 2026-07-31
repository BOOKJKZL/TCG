using System;
using System.IO;
using System.Linq;
using System.Text;
using Gacha.Application;
using Gacha.Infrastructure.Content;
using NUnit.Framework;

public sealed class FileSystemContentPackageQueueStateStoreTests
{
    private string root;
    private string statePath;

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(
            Path.GetTempPath(),
            "gacha-queue-state-tests-" + Guid.NewGuid().ToString("N"));
        statePath = Path.Combine(root, "nested", "install-queue-v1.json");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }

    [Test]
    public void Store_RoundTripsVersionedStateAndClearsTransactionFiles()
    {
        var store = new FileSystemContentPackageQueueStateStore(statePath);
        var expected = new ContentPackageQueueResumeState(1, 42, new[] { "en.base1", "ja.sv1" });

        store.Save(expected);
        ContentPackageQueueResumeState loaded = store.Load();

        Assert.That(loaded.SchemaVersion, Is.EqualTo(1));
        Assert.That(loaded.CatalogRevision, Is.EqualTo(42));
        Assert.That(loaded.PackageIds, Is.EqualTo(expected.PackageIds));
        Assert.That(File.Exists(statePath + ".tmp"), Is.False);
        Assert.That(File.Exists(statePath + ".backup"), Is.False);
        store.Clear();
        Assert.That(File.Exists(statePath), Is.False);
    }

    [Test]
    public void Store_RecoversAtomicBackupWhenPrimaryIsMissing()
    {
        var store = new FileSystemContentPackageQueueStateStore(statePath);
        store.Save(new ContentPackageQueueResumeState(1, 7, new[] { "fixture" }));
        File.Move(statePath, statePath + ".backup");

        ContentPackageQueueResumeState loaded = store.Load();

        Assert.That(loaded.PackageIds.Single(), Is.EqualTo("fixture"));
        Assert.That(File.Exists(statePath), Is.True);
        Assert.That(File.Exists(statePath + ".backup"), Is.False);
    }

    [Test]
    public void Store_RejectsCorruptOversizedAndUnsafeState()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(statePath));
        var store = new FileSystemContentPackageQueueStateStore(statePath);
        File.WriteAllText(statePath, "not-json", new UTF8Encoding(false));
        Assert.Throws<Newtonsoft.Json.JsonReaderException>(() => store.Load());

        File.WriteAllBytes(
            statePath,
            new byte[FileSystemContentPackageQueueStateStore.MaximumStateBytes + 1]);
        Assert.Throws<InvalidDataException>(() => store.Load());

        Assert.Throws<InvalidDataException>(() => store.Save(
            new ContentPackageQueueResumeState(1, 1, new[] { "../unsafe" })));
    }
}
