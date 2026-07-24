using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Gacha.Application;
using NUnit.Framework;

public class ContentPackageInstallCoordinatorTests
{
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private sealed class Registry : IInstalledContentPackageRegistry
    {
        public InstalledContentPackage Installed;
        public InstalledContentPackage Find(string packageId) => Installed;
    }

    private sealed class Storage : IContentStorageProbe
    {
        public long AvailableBytes = 1000;
        public long GetAvailableBytes() => AvailableBytes;
    }

    private sealed class Transfer : IContentPackageTransfer
    {
        public long Bytes;
        public int DownloadCalls;
        public int DeleteCalls;
        public bool FailNext;
        public bool BlockNext;
        public bool DeleteThrows;
        public TaskCompletionSource<bool> Started = NewSignal();

        public long GetDownloadedBytes(ContentPackageDescriptor package) => Bytes;

        public async Task DownloadAsync(
            ContentPackageDescriptor package,
            long offset,
            IProgress<long> persistedBytesProgress,
            CancellationToken cancellationToken)
        {
            DownloadCalls++;
            Started.TrySetResult(true);
            if (BlockNext)
                await Task.Delay(Timeout.Infinite, cancellationToken);
            if (FailNext)
            {
                FailNext = false;
                throw new IOException("fixture download failed");
            }
            Bytes = package.DownloadBytes;
            persistedBytesProgress?.Report(Bytes);
        }

        public void DeletePartial(ContentPackageDescriptor package)
        {
            DeleteCalls++;
            if (DeleteThrows)
                throw new IOException("fixture cleanup failed");
            Bytes = 0;
        }

        public string GetArchivePath(ContentPackageDescriptor package)
        {
            return Bytes == package.DownloadBytes ? "fixture-package.zip" : null;
        }
    }

    private sealed class Installer : IContentPackageInstaller
    {
        public readonly Queue<ContentPackageInstallResult> Results =
            new Queue<ContentPackageInstallResult>();
        public int Calls;
        public bool BlockNext;
        public TaskCompletionSource<bool> Started = NewSignal();

        public async Task<ContentPackageInstallResult> InstallAsync(
            ContentInstallPlan plan,
            string archivePath,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Started.TrySetResult(true);
            if (BlockNext)
                await Task.Delay(Timeout.Infinite, cancellationToken);
            if (Results.Count > 0)
                return Results.Dequeue();
            return ContentPackageInstallResult.Success(new InstalledContentPackage(
                plan.Package.PackageId,
                plan.Package.InstallRelativePath,
                plan.Package.Revision,
                plan.Package.Version,
                plan.Package.InstalledBytes,
                plan.Package.Sha256));
        }
    }

    [Test]
    public async Task Start_RunsPlanDownloadInstallAndCleanupInOrder()
    {
        var transfer = new Transfer();
        var installer = new Installer();
        var coordinator = Create(transfer, installer);
        var states = new List<ContentPackageOperationState>();
        coordinator.Changed += value => states.Add(value.State);

        ContentPackageOperationSnapshot result = await coordinator.StartAsync();

        Assert.That(result.State, Is.EqualTo(ContentPackageOperationState.Succeeded));
        Assert.That(result.Progress01, Is.EqualTo(1f));
        Assert.That(result.InstallResult.Succeeded, Is.True);
        Assert.That(transfer.DownloadCalls, Is.EqualTo(1));
        Assert.That(installer.Calls, Is.EqualTo(1));
        Assert.That(transfer.DeleteCalls, Is.EqualTo(1));
        Assert.That(states, Does.Contain(ContentPackageOperationState.Planning));
        Assert.That(states, Does.Contain(ContentPackageOperationState.Downloading));
        Assert.That(states, Does.Contain(ContentPackageOperationState.Installing));
        Assert.That(states[states.Count - 1], Is.EqualTo(ContentPackageOperationState.Succeeded));
    }

    [Test]
    public async Task Start_WhenAlreadyInstalledSkipsDownloadAndRemovesStaleArchive()
    {
        ContentPackageDescriptor package = Package();
        var registry = new Registry
        {
            Installed = new InstalledContentPackage(
                package.PackageId,
                package.InstallRelativePath,
                package.Revision,
                package.Version,
                package.InstalledBytes,
                package.Sha256)
        };
        var transfer = new Transfer { Bytes = package.DownloadBytes };
        var installer = new Installer();
        var coordinator = Create(transfer, installer, registry: registry);

        ContentPackageOperationSnapshot result = await coordinator.StartAsync();

        Assert.That(result.State, Is.EqualTo(ContentPackageOperationState.AlreadyCurrent));
        Assert.That(transfer.DownloadCalls, Is.Zero);
        Assert.That(installer.Calls, Is.Zero);
        Assert.That(transfer.DeleteCalls, Is.EqualTo(1));
    }

    [Test]
    public async Task Start_WhenSpaceIsInsufficientReturnsBlockedWithoutFailureEvent()
    {
        var transfer = new Transfer();
        var installer = new Installer();
        var coordinator = Create(transfer, installer, availableBytes: 10);
        int failures = 0;
        coordinator.FailureReported += _ => failures++;

        ContentPackageOperationSnapshot result = await coordinator.StartAsync();

        Assert.That(result.State, Is.EqualTo(ContentPackageOperationState.Blocked));
        Assert.That(result.Plan.Status, Is.EqualTo(ContentInstallPlanStatus.InsufficientSpace));
        Assert.That(result.ErrorMessage, Does.Contain("available bytes"));
        Assert.That(transfer.DownloadCalls, Is.Zero);
        Assert.That(installer.Calls, Is.Zero);
        Assert.That(failures, Is.Zero);
    }

    [Test]
    public async Task DownloadFailure_RetryCompletesAndReportsOneFailurePerAttempt()
    {
        var transfer = new Transfer { FailNext = true };
        var coordinator = Create(transfer, new Installer());
        var failures = new List<ContentPackageOperationFailure>();
        coordinator.FailureReported += failures.Add;

        ContentPackageOperationSnapshot failed = await coordinator.StartAsync();
        ContentPackageOperationSnapshot completed = await coordinator.RetryAsync();

        Assert.That(failed.State, Is.EqualTo(ContentPackageOperationState.Failed));
        Assert.That(failed.FailureStage, Is.EqualTo(ContentPackageOperationFailureStage.Download));
        Assert.That(completed.State, Is.EqualTo(ContentPackageOperationState.Succeeded));
        Assert.That(transfer.DownloadCalls, Is.EqualTo(2));
        Assert.That(failures.Count, Is.EqualTo(1));
        Assert.That(failures[0].Attempt, Is.EqualTo(1));
    }

    [Test]
    public async Task IntegrityFailure_RetryRedownloadsDiscardedArchive()
    {
        var transfer = new Transfer();
        var installer = new Installer();
        installer.Results.Enqueue(ContentPackageInstallResult.Failure(
            ContentPackageInstallStatus.IntegrityMismatch,
            "fixture hash mismatch"));
        var coordinator = Create(transfer, installer);

        ContentPackageOperationSnapshot failed = await coordinator.StartAsync();
        ContentPackageOperationSnapshot completed = await coordinator.RetryAsync();

        Assert.That(failed.State, Is.EqualTo(ContentPackageOperationState.Failed));
        Assert.That(failed.FailureStage, Is.EqualTo(ContentPackageOperationFailureStage.Install));
        Assert.That(completed.State, Is.EqualTo(ContentPackageOperationState.Succeeded));
        Assert.That(transfer.DownloadCalls, Is.EqualTo(2));
        Assert.That(installer.Calls, Is.EqualTo(2));
        Assert.That(transfer.DeleteCalls, Is.EqualTo(2));
    }

    [Test]
    public async Task TransientInstallFailure_RetryReusesCompletedArchive()
    {
        var transfer = new Transfer();
        var installer = new Installer();
        installer.Results.Enqueue(ContentPackageInstallResult.Failure(
            ContentPackageInstallStatus.Failed,
            "fixture disk temporarily busy"));
        var coordinator = Create(transfer, installer);

        ContentPackageOperationSnapshot failed = await coordinator.StartAsync();
        ContentPackageOperationSnapshot completed = await coordinator.RetryAsync();

        Assert.That(failed.State, Is.EqualTo(ContentPackageOperationState.Failed));
        Assert.That(completed.State, Is.EqualTo(ContentPackageOperationState.Succeeded));
        Assert.That(transfer.DownloadCalls, Is.EqualTo(1));
        Assert.That(installer.Calls, Is.EqualTo(2));
        Assert.That(transfer.DeleteCalls, Is.EqualTo(1));
    }

    [Test]
    public async Task PauseThenResumeKeepsDownloadAndCompletes()
    {
        var transfer = new Transfer { BlockNext = true };
        var coordinator = Create(transfer, new Installer());

        Task<ContentPackageOperationSnapshot> active = coordinator.StartAsync();
        await transfer.Started.Task;
        ContentPackageOperationSnapshot paused = await coordinator.PauseAsync();

        Assert.That(await active, Is.SameAs(paused));
        Assert.That(paused.State, Is.EqualTo(ContentPackageOperationState.Paused));
        Assert.That(transfer.DeleteCalls, Is.Zero);

        transfer.BlockNext = false;
        ContentPackageOperationSnapshot completed = await coordinator.RetryAsync();

        Assert.That(completed.State, Is.EqualTo(ContentPackageOperationState.Succeeded));
        Assert.That(transfer.DownloadCalls, Is.EqualTo(2));
    }

    [Test]
    public async Task CancelDuringDownloadDeletesTemporaryArchive()
    {
        var transfer = new Transfer { BlockNext = true };
        var coordinator = Create(transfer, new Installer());

        Task<ContentPackageOperationSnapshot> active = coordinator.StartAsync();
        await transfer.Started.Task;
        ContentPackageOperationSnapshot cancelled = await coordinator.CancelAsync();

        Assert.That(await active, Is.SameAs(cancelled));
        Assert.That(cancelled.State, Is.EqualTo(ContentPackageOperationState.Cancelled));
        Assert.That(transfer.DeleteCalls, Is.EqualTo(1));
    }

    [Test]
    public async Task CancelDuringInstallCancelsInstallerAndCleansArchive()
    {
        var transfer = new Transfer();
        var installer = new Installer { BlockNext = true };
        var coordinator = Create(transfer, installer);

        Task<ContentPackageOperationSnapshot> active = coordinator.StartAsync();
        await installer.Started.Task;
        ContentPackageOperationSnapshot cancelled = await coordinator.CancelAsync();

        Assert.That(await active, Is.SameAs(cancelled));
        Assert.That(cancelled.State, Is.EqualTo(ContentPackageOperationState.Cancelled));
        Assert.That(transfer.DeleteCalls, Is.EqualTo(1));
    }

    [Test]
    public async Task CleanupFailureAfterInstallIsWarningNotFalseInstallFailure()
    {
        var transfer = new Transfer { DeleteThrows = true };
        var coordinator = Create(transfer, new Installer());
        int failures = 0;
        coordinator.FailureReported += _ => failures++;

        ContentPackageOperationSnapshot result = await coordinator.StartAsync();

        Assert.That(result.State, Is.EqualTo(ContentPackageOperationState.Succeeded));
        Assert.That(result.WarningMessage, Does.Contain("cleanup failed"));
        Assert.That(result.ErrorMessage, Is.Null);
        Assert.That(failures, Is.Zero);
    }

    [Test]
    public async Task DuplicateStartReturnsSameOperationAndSubscriberErrorsAreIsolated()
    {
        var transfer = new Transfer { BlockNext = true };
        var coordinator = Create(transfer, new Installer());
        coordinator.Changed += _ => throw new InvalidOperationException("broken view");

        Task<ContentPackageOperationSnapshot> first = coordinator.StartAsync();
        await transfer.Started.Task;
        Task<ContentPackageOperationSnapshot> duplicate = coordinator.StartAsync();

        Assert.That(duplicate, Is.SameAs(first));
        Assert.That(transfer.DownloadCalls, Is.EqualTo(1));
        await coordinator.CancelAsync();
    }

    private static ContentPackageInstallCoordinator Create(
        Transfer transfer,
        Installer installer,
        long availableBytes = 1000,
        Registry registry = null)
    {
        var storage = new Storage { AvailableBytes = availableBytes };
        var planner = new ContentPackagePlanner(registry ?? new Registry(), storage, 0);
        return new ContentPackageInstallCoordinator(Package(), planner, transfer, installer);
    }

    private static ContentPackageDescriptor Package()
    {
        return new ContentPackageDescriptor(
            "en.base1",
            "en/base1",
            3,
            "3.0.0",
            100,
            200,
            Hash);
    }

    private static TaskCompletionSource<bool> NewSignal()
    {
        return new TaskCompletionSource<bool>();
    }
}
