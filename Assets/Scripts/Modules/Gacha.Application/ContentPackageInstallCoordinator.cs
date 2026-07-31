using System;
using System.Threading;
using System.Threading.Tasks;

namespace Gacha.Application
{
    public enum ContentPackageOperationState
    {
        Idle,
        Planning,
        Blocked,
        Downloading,
        Paused,
        Installing,
        Succeeded,
        AlreadyCurrent,
        Cancelled,
        Failed
    }

    public enum ContentPackageOperationFailureStage
    {
        None,
        Download,
        Install,
        Cleanup
    }

    public sealed class ContentPackageOperationSnapshot
    {
        internal ContentPackageOperationSnapshot(
            string packageId,
            ContentPackageOperationState state,
            int attempt,
            ContentInstallPlan plan,
            ContentDownloadSnapshot download,
            ContentPackageInstallResult installResult,
            ContentPackageOperationFailureStage failureStage,
            string errorMessage,
            string warningMessage)
        {
            PackageId = packageId;
            State = state;
            Attempt = attempt;
            Plan = plan;
            Download = download;
            InstallResult = installResult;
            FailureStage = failureStage;
            ErrorMessage = errorMessage;
            WarningMessage = warningMessage;
        }

        public string PackageId { get; }
        public ContentPackageOperationState State { get; }
        public int Attempt { get; }
        public ContentInstallPlan Plan { get; }
        public ContentDownloadSnapshot Download { get; }
        public ContentPackageInstallResult InstallResult { get; }
        public ContentPackageOperationFailureStage FailureStage { get; }
        public string ErrorMessage { get; }
        public string WarningMessage { get; }
        public float Progress01 => State == ContentPackageOperationState.Succeeded ||
                                   State == ContentPackageOperationState.AlreadyCurrent ||
                                   State == ContentPackageOperationState.Installing
            ? 1f
            : Download?.Progress01 ?? 0f;
        public bool CanPause => State == ContentPackageOperationState.Downloading;
        public bool CanCancel => State != ContentPackageOperationState.Succeeded &&
                                 State != ContentPackageOperationState.AlreadyCurrent &&
                                 State != ContentPackageOperationState.Cancelled;
        public bool CanRetry => State == ContentPackageOperationState.Blocked ||
                                State == ContentPackageOperationState.Paused ||
                                State == ContentPackageOperationState.Cancelled ||
                                State == ContentPackageOperationState.Failed;
    }

    public sealed class ContentPackageOperationFailure
    {
        internal ContentPackageOperationFailure(
            string packageId,
            int attempt,
            ContentPackageOperationFailureStage stage,
            string errorMessage)
        {
            PackageId = packageId;
            Attempt = attempt;
            Stage = stage;
            ErrorMessage = errorMessage;
        }

        public string PackageId { get; }
        public int Attempt { get; }
        public ContentPackageOperationFailureStage Stage { get; }
        public string ErrorMessage { get; }
    }

    public interface IContentPackageInstallCoordinatorFactory
    {
        ContentPackageInstallCoordinator Create(ContentPackageCatalog catalog, string packageId);
    }

    /// <summary>
    /// Coordinates one immutable package through planning, resumable download,
    /// verified installation and archive cleanup. Presentation code observes one
    /// state model instead of sequencing three infrastructure services itself.
    /// </summary>
    public sealed class ContentPackageInstallCoordinator
    {
        private readonly object gate = new object();
        private readonly ContentPackageDescriptor package;
        private readonly ContentPackagePlanner planner;
        private readonly IContentPackageTransfer transfer;
        private readonly IContentPackageInstaller installer;
        private readonly SemaphoreSlim installationGate;
        private readonly ContentPackageDownloadTask downloadTask;

        private ContentPackageOperationState state = ContentPackageOperationState.Idle;
        private int attempt;
        private int failurePublishedAttempt = -1;
        private ContentInstallPlan plan;
        private ContentDownloadSnapshot download;
        private ContentPackageInstallResult installResult;
        private ContentPackageOperationFailureStage failureStage;
        private string errorMessage;
        private string warningMessage;
        private CancellationTokenSource cancellation;
        private Task<ContentPackageOperationSnapshot> activeTask;

        public ContentPackageInstallCoordinator(
            ContentPackageDescriptor package,
            ContentPackagePlanner planner,
            IContentPackageTransfer transfer,
            IContentPackageInstaller installer,
            SemaphoreSlim installationGate = null)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            this.planner = planner ?? throw new ArgumentNullException(nameof(planner));
            this.transfer = transfer ?? throw new ArgumentNullException(nameof(transfer));
            this.installer = installer ?? throw new ArgumentNullException(nameof(installer));
            this.installationGate = installationGate;
            downloadTask = new ContentPackageDownloadTask(package, transfer);
            download = downloadTask.Current;
            downloadTask.Changed += OnDownloadChanged;
        }

        public event Action<ContentPackageOperationSnapshot> Changed;
        public event Action<ContentPackageOperationFailure> FailureReported;

        public ContentPackageOperationSnapshot Current
        {
            get
            {
                lock (gate)
                    return SnapshotLocked();
            }
        }

        public Task<ContentPackageOperationSnapshot> StartAsync()
        {
            ContentPackageOperationSnapshot changed;
            Task<ContentPackageOperationSnapshot> result;
            lock (gate)
            {
                if (IsActive(state))
                    return activeTask;
                if (state == ContentPackageOperationState.Succeeded ||
                    state == ContentPackageOperationState.AlreadyCurrent)
                    return Task.FromResult(SnapshotLocked());

                attempt++;
                state = ContentPackageOperationState.Planning;
                plan = null;
                installResult = null;
                failureStage = ContentPackageOperationFailureStage.None;
                errorMessage = null;
                warningMessage = null;
                cancellation = new CancellationTokenSource();
                int runAttempt = attempt;
                activeTask = RunAsync(runAttempt, cancellation);
                result = activeTask;
                changed = SnapshotLocked();
            }

            PublishChanged(changed);
            return result;
        }

        public Task<ContentPackageOperationSnapshot> RetryAsync()
        {
            lock (gate)
            {
                if (!SnapshotLocked().CanRetry)
                    return Task.FromResult(SnapshotLocked());
            }
            return StartAsync();
        }

        public async Task<ContentPackageOperationSnapshot> PauseAsync()
        {
            Task<ContentPackageOperationSnapshot> operation;
            lock (gate)
            {
                if (state != ContentPackageOperationState.Downloading)
                    return SnapshotLocked();
                operation = activeTask;
            }

            await downloadTask.PauseAsync();
            return operation == null ? Current : await operation;
        }

        public async Task<ContentPackageOperationSnapshot> CancelAsync()
        {
            ContentPackageOperationState currentState;
            Task<ContentPackageOperationSnapshot> operation;
            CancellationTokenSource source;
            int currentAttempt;
            lock (gate)
            {
                if (state == ContentPackageOperationState.Succeeded ||
                    state == ContentPackageOperationState.AlreadyCurrent ||
                    state == ContentPackageOperationState.Cancelled)
                    return SnapshotLocked();
                currentState = state;
                operation = activeTask;
                source = cancellation;
                currentAttempt = attempt;
            }

            TryCancel(source);
            if (currentState == ContentPackageOperationState.Downloading)
                await downloadTask.CancelAsync();

            if (operation != null)
                return await operation;

            ContentDownloadSnapshot discarded = await downloadTask.DiscardAsync();
            if (discarded.State == ContentDownloadState.Failed)
            {
                return Fail(
                    currentAttempt,
                    ContentPackageOperationFailureStage.Cleanup,
                    discarded.ErrorMessage);
            }
            return Transition(currentAttempt, ContentPackageOperationState.Cancelled, null, null);
        }

        /// <summary>
        /// Returns a terminal coordinator to a fresh install state after an
        /// external lifecycle service removes its installed receipt and files.
        /// Any stale archive is discarded so reinstall cannot reuse deleted state.
        /// </summary>
        public async Task<ContentPackageOperationSnapshot> ResetAfterRemovalAsync()
        {
            lock (gate)
            {
                if (IsActive(state))
                    throw new InvalidOperationException("An active content operation cannot be reset for reinstall.");
            }

            ContentDownloadSnapshot discarded = await downloadTask.DiscardAsync();
            ContentPackageOperationSnapshot snapshot;
            lock (gate)
            {
                state = ContentPackageOperationState.Idle;
                plan = null;
                download = discarded;
                installResult = null;
                failureStage = ContentPackageOperationFailureStage.None;
                errorMessage = null;
                warningMessage = discarded.State == ContentDownloadState.Failed
                    ? "Downloaded package cleanup failed: " + discarded.ErrorMessage
                    : null;
                snapshot = SnapshotLocked();
            }
            PublishChanged(snapshot);
            return snapshot;
        }

        private async Task<ContentPackageOperationSnapshot> RunAsync(
            int runAttempt,
            CancellationTokenSource runCancellation)
        {
            CancellationToken token = runCancellation.Token;
            await Task.Yield();
            try
            {
                token.ThrowIfCancellationRequested();
                ContentInstallPlan nextPlan = planner.Plan(package);
                SetPlan(runAttempt, nextPlan);

                if (nextPlan.Status == ContentInstallPlanStatus.AlreadyCurrent)
                {
                    TryDeleteDownload(out string cleanupWarning);
                    return Transition(
                        runAttempt,
                        ContentPackageOperationState.AlreadyCurrent,
                        null,
                        cleanupWarning);
                }
                if (!nextPlan.CanStart)
                {
                    return Transition(
                        runAttempt,
                        ContentPackageOperationState.Blocked,
                        PlanError(nextPlan),
                        null);
                }

                token.ThrowIfCancellationRequested();
                Transition(runAttempt, ContentPackageOperationState.Downloading, null, null);
                ContentDownloadSnapshot downloaded = await downloadTask.StartAsync();

                if (downloaded.State == ContentDownloadState.Paused)
                    return Transition(runAttempt, ContentPackageOperationState.Paused, null, null);
                if (downloaded.State == ContentDownloadState.Cancelled)
                    return Transition(runAttempt, ContentPackageOperationState.Cancelled, null, null);
                if (downloaded.State == ContentDownloadState.Failed)
                {
                    return Fail(
                        runAttempt,
                        ContentPackageOperationFailureStage.Download,
                        downloaded.ErrorMessage);
                }
                if (downloaded.State != ContentDownloadState.Completed ||
                    string.IsNullOrWhiteSpace(downloaded.ArchivePath))
                {
                    return Fail(
                        runAttempt,
                        ContentPackageOperationFailureStage.Download,
                        "Content download did not produce a complete archive.");
                }

                token.ThrowIfCancellationRequested();
                Transition(runAttempt, ContentPackageOperationState.Installing, null, null);
                ContentPackageInstallResult installed;
                if (installationGate == null)
                {
                    installed = await installer.InstallAsync(
                        nextPlan,
                        downloaded.ArchivePath,
                        token);
                }
                else
                {
                    await installationGate.WaitAsync(token);
                    try
                    {
                        installed = await installer.InstallAsync(
                            nextPlan,
                            downloaded.ArchivePath,
                            token);
                    }
                    finally
                    {
                        installationGate.Release();
                    }
                }
                SetInstallResult(runAttempt, installed);

                if (installed == null)
                {
                    return Fail(
                        runAttempt,
                        ContentPackageOperationFailureStage.Install,
                        "Content package installer returned no result.");
                }
                if (installed.Succeeded)
                {
                    TryDeleteDownload(out string cleanupWarning);
                    return Transition(
                        runAttempt,
                        ContentPackageOperationState.Succeeded,
                        null,
                        cleanupWarning);
                }
                if (installed.Status == ContentPackageInstallStatus.Cancelled)
                    return await CancelAfterDownloadAsync(runAttempt);
                if (RequiresFreshDownload(installed.Status))
                {
                    ContentDownloadSnapshot discarded = await downloadTask.DiscardAsync();
                    if (discarded.State == ContentDownloadState.Failed)
                    {
                        return Fail(
                            runAttempt,
                            ContentPackageOperationFailureStage.Cleanup,
                            discarded.ErrorMessage);
                    }
                }
                return Fail(
                    runAttempt,
                    ContentPackageOperationFailureStage.Install,
                    installed.ErrorMessage);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return await CancelAfterDownloadAsync(runAttempt);
            }
            catch (Exception exception)
            {
                ContentPackageOperationFailureStage stage;
                lock (gate)
                {
                    stage = state == ContentPackageOperationState.Installing
                        ? ContentPackageOperationFailureStage.Install
                        : ContentPackageOperationFailureStage.Download;
                }
                return Fail(runAttempt, stage, exception.Message);
            }
            finally
            {
                runCancellation.Dispose();
                lock (gate)
                {
                    if (ReferenceEquals(cancellation, runCancellation))
                        cancellation = null;
                    if (attempt == runAttempt)
                        activeTask = null;
                }
            }
        }

        private async Task<ContentPackageOperationSnapshot> CancelAfterDownloadAsync(int runAttempt)
        {
            ContentDownloadSnapshot current = downloadTask.Current;
            if (current.State != ContentDownloadState.Cancelled)
            {
                ContentDownloadSnapshot discarded = await downloadTask.DiscardAsync();
                if (discarded.State == ContentDownloadState.Failed)
                {
                    return Fail(
                        runAttempt,
                        ContentPackageOperationFailureStage.Cleanup,
                        discarded.ErrorMessage);
                }
            }
            return Transition(runAttempt, ContentPackageOperationState.Cancelled, null, null);
        }

        private static bool RequiresFreshDownload(ContentPackageInstallStatus status)
        {
            return status == ContentPackageInstallStatus.ArchiveNotFound ||
                   status == ContentPackageInstallStatus.IntegrityMismatch ||
                   status == ContentPackageInstallStatus.InvalidArchive;
        }

        private bool TryDeleteDownload(out string error)
        {
            try
            {
                transfer.DeletePartial(package);
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                error = "Downloaded package cleanup failed: " + exception.Message;
                return false;
            }
        }

        private void SetPlan(int runAttempt, ContentInstallPlan value)
        {
            lock (gate)
            {
                if (attempt == runAttempt)
                    plan = value;
            }
        }

        private void SetInstallResult(int runAttempt, ContentPackageInstallResult value)
        {
            lock (gate)
            {
                if (attempt == runAttempt)
                    installResult = value;
            }
        }

        private void OnDownloadChanged(ContentDownloadSnapshot value)
        {
            ContentPackageOperationSnapshot snapshot = null;
            lock (gate)
            {
                download = value;
                if (state == ContentPackageOperationState.Downloading)
                    snapshot = SnapshotLocked();
            }
            if (snapshot != null)
                PublishChanged(snapshot);
        }

        private ContentPackageOperationSnapshot Fail(
            int runAttempt,
            ContentPackageOperationFailureStage stage,
            string message)
        {
            ContentPackageOperationSnapshot snapshot;
            ContentPackageOperationFailure failure = null;
            lock (gate)
            {
                if (attempt != runAttempt)
                    return SnapshotLocked();
                state = ContentPackageOperationState.Failed;
                failureStage = stage;
                errorMessage = string.IsNullOrWhiteSpace(message)
                    ? "Content package operation failed."
                    : message.Trim();
                warningMessage = null;
                snapshot = SnapshotLocked();
                if (failurePublishedAttempt != runAttempt)
                {
                    failurePublishedAttempt = runAttempt;
                    failure = new ContentPackageOperationFailure(
                        package.PackageId,
                        runAttempt,
                        stage,
                        errorMessage);
                }
            }

            PublishChanged(snapshot);
            if (failure != null)
                PublishFailure(failure);
            return snapshot;
        }

        private ContentPackageOperationSnapshot Transition(
            int runAttempt,
            ContentPackageOperationState nextState,
            string error,
            string warning)
        {
            ContentPackageOperationSnapshot snapshot;
            lock (gate)
            {
                if (attempt != runAttempt)
                    return SnapshotLocked();
                state = nextState;
                failureStage = ContentPackageOperationFailureStage.None;
                errorMessage = error;
                warningMessage = warning;
                snapshot = SnapshotLocked();
            }
            PublishChanged(snapshot);
            return snapshot;
        }

        private ContentPackageOperationSnapshot SnapshotLocked()
        {
            return new ContentPackageOperationSnapshot(
                package.PackageId,
                state,
                attempt,
                plan,
                download,
                installResult,
                failureStage,
                errorMessage,
                warningMessage);
        }

        private static bool IsActive(ContentPackageOperationState value)
        {
            return value == ContentPackageOperationState.Planning ||
                   value == ContentPackageOperationState.Downloading ||
                   value == ContentPackageOperationState.Installing;
        }

        private static string PlanError(ContentInstallPlan value)
        {
            if (!string.IsNullOrWhiteSpace(value.ErrorMessage))
                return value.ErrorMessage;
            if (value.Status == ContentInstallPlanStatus.InsufficientSpace)
            {
                return $"Content package requires {value.RequiredBytes} available bytes; " +
                       $"only {value.AvailableBytes} bytes are available.";
            }
            return "Content package installation cannot start: " + value.Status;
        }

        private void PublishChanged(ContentPackageOperationSnapshot snapshot)
        {
            InvokeSafely(Changed, snapshot);
        }

        private void PublishFailure(ContentPackageOperationFailure failure)
        {
            InvokeSafely(FailureReported, failure);
        }

        private static void InvokeSafely<T>(Action<T> handlers, T value)
        {
            if (handlers == null)
                return;
            foreach (Action<T> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(value);
                }
                catch
                {
                    // Presentation observers cannot corrupt package state.
                }
            }
        }

        private static void TryCancel(CancellationTokenSource source)
        {
            try
            {
                source?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The operation completed between state inspection and cancel.
            }
        }
    }
}
