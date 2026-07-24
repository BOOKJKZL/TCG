using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Gacha.Application;
using NUnit.Framework;

public class ContentPackageDownloadTaskTests
{
    private sealed class Transfer : IContentPackageTransfer
    {
        public long PersistedBytes;
        public int DownloadCalls;
        public int DeleteCalls;
        public bool BlockNext;
        public bool ReturnBeforeComplete;
        public long FailNextAfterBytes = -1;
        public Exception DeleteError;
        public readonly List<long> Offsets = new List<long>();
        public TaskCompletionSource<bool> Started = NewSignal();

        public long GetDownloadedBytes(ContentPackageDescriptor package) => PersistedBytes;

        public async Task DownloadAsync(
            ContentPackageDescriptor package,
            long offset,
            IProgress<long> persistedBytesProgress,
            CancellationToken cancellationToken)
        {
            DownloadCalls++;
            Offsets.Add(offset);
            Started.TrySetResult(true);

            if (FailNextAfterBytes >= 0)
            {
                PersistedBytes = Math.Min(package.DownloadBytes, offset + FailNextAfterBytes);
                persistedBytesProgress.Report(PersistedBytes);
                FailNextAfterBytes = -1;
                throw new IOException("network interrupted");
            }

            if (BlockNext)
            {
                PersistedBytes = Math.Min(package.DownloadBytes, offset + 30);
                persistedBytesProgress.Report(PersistedBytes);
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            if (ReturnBeforeComplete)
            {
                PersistedBytes = Math.Min(package.DownloadBytes, offset + 20);
                persistedBytesProgress.Report(PersistedBytes);
                return;
            }

            PersistedBytes = package.DownloadBytes;
            persistedBytesProgress.Report(PersistedBytes);
        }

        public void DeletePartial(ContentPackageDescriptor package)
        {
            DeleteCalls++;
            if (DeleteError != null)
                throw DeleteError;
            PersistedBytes = 0;
        }

        public string GetArchivePath(ContentPackageDescriptor package)
        {
            return "/downloads/" + package.PackageId + ".zip";
        }

        public void ResetSignal()
        {
            Started = NewSignal();
        }

        private static TaskCompletionSource<bool> NewSignal()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    [Test]
    public async Task Start_NewDownload_ReportsCompletionAndArchivePath()
    {
        var transfer = new Transfer();
        var task = new ContentPackageDownloadTask(Package(), transfer);
        var states = new List<ContentDownloadState>();
        task.Changed += snapshot => states.Add(snapshot.State);

        ContentDownloadSnapshot result = await task.StartAsync();

        Assert.That(result.State, Is.EqualTo(ContentDownloadState.Completed));
        Assert.That(result.DownloadedBytes, Is.EqualTo(100));
        Assert.That(result.Progress01, Is.EqualTo(1f));
        Assert.That(result.ArchivePath, Is.EqualTo("/downloads/en.base1.zip"));
        Assert.That(result.Attempt, Is.EqualTo(1));
        Assert.That(states, Does.Contain(ContentDownloadState.Downloading));
        Assert.That(states[states.Count - 1], Is.EqualTo(ContentDownloadState.Completed));
    }

    [Test]
    public async Task Start_ExistingPartialFile_ResumesFromPersistedOffset()
    {
        var transfer = new Transfer { PersistedBytes = 45 };
        var task = new ContentPackageDownloadTask(Package(), transfer);

        ContentDownloadSnapshot result = await task.StartAsync();

        Assert.That(result.State, Is.EqualTo(ContentDownloadState.Completed));
        Assert.That(transfer.Offsets, Is.EqualTo(new[] { 45L }));
        Assert.That(transfer.DeleteCalls, Is.Zero);
    }

    [Test]
    public async Task PauseThenResume_PreservesPartialBytesAndUsesNewOffset()
    {
        var transfer = new Transfer { BlockNext = true };
        var task = new ContentPackageDownloadTask(Package(), transfer);

        Task<ContentDownloadSnapshot> firstRun = task.StartAsync();
        await transfer.Started.Task;
        ContentDownloadSnapshot paused = await task.PauseAsync();

        Assert.That(paused.State, Is.EqualTo(ContentDownloadState.Paused));
        Assert.That(paused.DownloadedBytes, Is.EqualTo(30));
        Assert.That(transfer.DeleteCalls, Is.Zero);

        transfer.BlockNext = false;
        transfer.ResetSignal();
        ContentDownloadSnapshot completed = await task.StartAsync();

        Assert.That(completed.State, Is.EqualTo(ContentDownloadState.Completed));
        Assert.That(completed.Attempt, Is.EqualTo(2));
        Assert.That(transfer.Offsets, Is.EqualTo(new[] { 0L, 30L }));
        Assert.That(await firstRun, Is.SameAs(paused));
    }

    [Test]
    public async Task Retry_FailedTransferKeepsPartialBytesAndReportsFailureOnce()
    {
        var transfer = new Transfer { FailNextAfterBytes = 25 };
        var task = new ContentPackageDownloadTask(Package(), transfer);
        var failures = new List<ContentDownloadFailure>();
        task.FailureReported += failures.Add;

        ContentDownloadSnapshot failed = await task.StartAsync();

        Assert.That(failed.State, Is.EqualTo(ContentDownloadState.Failed));
        Assert.That(failed.DownloadedBytes, Is.EqualTo(25));
        Assert.That(failed.ErrorMessage, Does.Contain("network interrupted"));
        Assert.That(failures, Has.Count.EqualTo(1));
        Assert.That(failures[0].Attempt, Is.EqualTo(1));

        ContentDownloadSnapshot completed = await task.RetryAsync();

        Assert.That(completed.State, Is.EqualTo(ContentDownloadState.Completed));
        Assert.That(transfer.Offsets, Is.EqualTo(new[] { 0L, 25L }));
        Assert.That(failures, Has.Count.EqualTo(1));
        Assert.That((await task.RetryAsync()).State, Is.EqualTo(ContentDownloadState.Completed));
        Assert.That(failures, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Cancel_ActiveTransferDeletesPartialFile()
    {
        var transfer = new Transfer { BlockNext = true };
        var task = new ContentPackageDownloadTask(Package(), transfer);

        task.StartAsync();
        await transfer.Started.Task;
        ContentDownloadSnapshot cancelled = await task.CancelAsync();

        Assert.That(cancelled.State, Is.EqualTo(ContentDownloadState.Cancelled));
        Assert.That(cancelled.DownloadedBytes, Is.Zero);
        Assert.That(transfer.PersistedBytes, Is.Zero);
        Assert.That(transfer.DeleteCalls, Is.EqualTo(1));
        Assert.That(cancelled.CanResume, Is.True);
        Assert.That(cancelled.CanCancel, Is.False);
    }

    [Test]
    public async Task StartWhileDownloading_ReturnsSameOperationWithoutDuplicateTransfer()
    {
        var transfer = new Transfer { BlockNext = true };
        var task = new ContentPackageDownloadTask(Package(), transfer);

        Task<ContentDownloadSnapshot> first = task.StartAsync();
        await transfer.Started.Task;
        Task<ContentDownloadSnapshot> duplicate = task.StartAsync();

        Assert.That(duplicate, Is.SameAs(first));
        Assert.That(transfer.DownloadCalls, Is.EqualTo(1));
        await task.CancelAsync();
    }

    [Test]
    public async Task Start_OversizedPartialFile_IsDeletedBeforeRestart()
    {
        var transfer = new Transfer { PersistedBytes = 101 };
        var task = new ContentPackageDownloadTask(Package(), transfer);

        ContentDownloadSnapshot result = await task.StartAsync();

        Assert.That(result.State, Is.EqualTo(ContentDownloadState.Completed));
        Assert.That(transfer.DeleteCalls, Is.EqualTo(1));
        Assert.That(transfer.Offsets, Is.EqualTo(new[] { 0L }));
    }

    [Test]
    public async Task Start_TransportReturningEarly_ProducesOneStructuredFailure()
    {
        var transfer = new Transfer { ReturnBeforeComplete = true };
        var task = new ContentPackageDownloadTask(Package(), transfer);
        int failures = 0;
        task.FailureReported += _ => failures++;

        ContentDownloadSnapshot result = await task.StartAsync();

        Assert.That(result.State, Is.EqualTo(ContentDownloadState.Failed));
        Assert.That(result.ErrorMessage, Does.Contain("expected 100 bytes"));
        Assert.That(result.DownloadedBytes, Is.EqualTo(20));
        Assert.That(failures, Is.EqualTo(1));
    }

    [Test]
    public async Task SubscriberException_DoesNotTurnSuccessfulDownloadIntoFailure()
    {
        var transfer = new Transfer();
        var task = new ContentPackageDownloadTask(Package(), transfer);
        task.Changed += _ => throw new InvalidOperationException("broken view");
        int failures = 0;
        task.FailureReported += _ => failures++;

        ContentDownloadSnapshot result = await task.StartAsync();

        Assert.That(result.State, Is.EqualTo(ContentDownloadState.Completed));
        Assert.That(failures, Is.Zero);
    }

    private static ContentPackageDescriptor Package()
    {
        return new ContentPackageDescriptor(
            "en.base1",
            "en/base1",
            1,
            "1.0.0",
            100,
            200,
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
    }
}
