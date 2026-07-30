using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;

public class ContentImportCheckpointTests
{
    private string temporaryDirectory;

    [SetUp]
    public void SetUp()
    {
        temporaryDirectory = Path.Combine(
            Path.GetTempPath(), "gacha-import-checkpoint-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(temporaryDirectory))
            Directory.Delete(temporaryDirectory, true);
    }

    [Test]
    public void Store_PersistsProgressFailuresAndReloadsSameConfiguration()
    {
        var store = new ContentImportCheckpointStore(temporaryDirectory, "en", "low", "webp");
        store.Begin(new[] { "set-b", "set-a", "set-a" });
        store.StartSet("set-a", 2);
        store.RecordCard("set-a", "card-1", null);
        store.RecordCard("set-a", "card-2", "missing image");
        store.CompleteSet("set-a");
        store.WriteFailureReport();

        var reloaded = new ContentImportCheckpointStore(temporaryDirectory, "en", "low", "webp");
        ContentImportCheckpoint snapshot = reloaded.Snapshot();
        ContentImportFailureReport report = JsonConvert.DeserializeObject<ContentImportFailureReport>(
            File.ReadAllText(reloaded.FailureReportPath));

        Assert.That(snapshot.Sets.Select(item => item.SetId), Is.EqualTo(new[] { "set-a", "set-b" }));
        ContentImportSetCheckpoint set = snapshot.Sets[0];
        Assert.That(set.State, Is.EqualTo("completed-with-errors"));
        Assert.That(set.ExpectedCards, Is.EqualTo(2));
        Assert.That(set.ProcessedCards, Is.EqualTo(2));
        Assert.That(set.FailedCards, Is.EqualTo(1));
        Assert.That(report.Failures, Has.Count.EqualTo(1));
        Assert.That(report.Failures[0].ItemId, Is.EqualTo("card-2"));
        Assert.That(Directory.GetFiles(temporaryDirectory, "*.download", SearchOption.AllDirectories),
            Is.Empty);
    }

    [Test]
    public void Store_UsesSeparateCheckpointForImageConfiguration()
    {
        var webp = new ContentImportCheckpointStore(temporaryDirectory, "en", "low", "webp");
        var jpg = new ContentImportCheckpointStore(temporaryDirectory, "en", "low", "jpg");

        Assert.That(webp.CheckpointPath, Is.Not.EqualTo(jpg.CheckpointPath));
        Assert.That(webp.FailureReportPath, Is.Not.EqualTo(jpg.FailureReportPath));
    }

    [Test]
    public async Task Store_RetriesAtomicReplaceWhileReaderBrieflyBlocksDeletion()
    {
        var store = new ContentImportCheckpointStore(temporaryDirectory, "en", "low", "webp");
        store.Begin(new[] { "set-a" });
        FileStream blocker = File.Open(store.CheckpointPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        Task release = Task.Run(async () =>
        {
            await Task.Delay(100);
            blocker.Dispose();
        });

        Assert.DoesNotThrow(() => store.StartSet("set-a", 1));
        await release;

        ContentImportCheckpoint checkpoint = store.Snapshot();
        Assert.That(checkpoint.Sets.Single().State, Is.EqualTo("running"));
        Assert.That(File.Exists(store.CheckpointPath), Is.True);
    }
}
