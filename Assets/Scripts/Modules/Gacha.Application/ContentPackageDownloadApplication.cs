using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Gacha.Application
{
    public enum ContentDownloadState
    {
        Idle,
        Downloading,
        Paused,
        Completed,
        Cancelled,
        Failed
    }

    public sealed class ContentDownloadSnapshot
    {
        internal ContentDownloadSnapshot(
            string packageId,
            ContentDownloadState state,
            long downloadedBytes,
            long totalBytes,
            int attempt,
            string archivePath,
            string errorMessage)
        {
            PackageId = packageId;
            State = state;
            DownloadedBytes = downloadedBytes;
            TotalBytes = totalBytes;
            Attempt = attempt;
            ArchivePath = archivePath;
            ErrorMessage = errorMessage;
        }

        public string PackageId { get; }
        public ContentDownloadState State { get; }
        public long DownloadedBytes { get; }
        public long TotalBytes { get; }
        public int Attempt { get; }
        public string ArchivePath { get; }
        public string ErrorMessage { get; }
        public float Progress01 => TotalBytes <= 0 ? 0f : (float)DownloadedBytes / TotalBytes;
        public bool CanPause => State == ContentDownloadState.Downloading;
        public bool CanResume => State == ContentDownloadState.Idle ||
                                 State == ContentDownloadState.Paused ||
                                 State == ContentDownloadState.Cancelled ||
                                 State == ContentDownloadState.Failed;
        public bool CanCancel => State != ContentDownloadState.Completed && State != ContentDownloadState.Cancelled;
    }

    public sealed class ContentDownloadFailure
    {
        internal ContentDownloadFailure(string packageId, int attempt, string errorMessage)
        {
            PackageId = packageId;
            Attempt = attempt;
            ErrorMessage = errorMessage;
        }

        public string PackageId { get; }
        public int Attempt { get; }
        public string ErrorMessage { get; }
    }

    /// <summary>
    /// Infrastructure owns the partial file and remote protocol. Progress values
    /// are absolute bytes already persisted, not bytes from the current request.
    /// </summary>
    public interface IContentPackageTransfer
    {
        long GetDownloadedBytes(ContentPackageDescriptor package);

        Task DownloadAsync(
            ContentPackageDescriptor package,
            long offset,
            IProgress<long> persistedBytesProgress,
            CancellationToken cancellationToken);

        void DeletePartial(ContentPackageDescriptor package);
        string GetArchivePath(ContentPackageDescriptor package);
    }

    public interface IContentPackageByteSource
    {
        Task<Stream> OpenReadAsync(
            ContentPackageDescriptor package,
            long offset,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Owns one package's resumable download lifecycle. It intentionally does not
    /// install the archive; a completed snapshot can be passed to the installer.
    /// </summary>
    public sealed class ContentPackageDownloadTask
    {
        private enum StopRequest
        {
            None,
            Pause,
            Cancel
        }

        private readonly object gate = new object();
        private readonly ContentPackageDescriptor package;
        private readonly IContentPackageTransfer transfer;

        private ContentDownloadState state = ContentDownloadState.Idle;
        private long downloadedBytes;
        private int attempt;
        private string archivePath;
        private string errorMessage;
        private StopRequest stopRequest;
        private CancellationTokenSource cancellation;
        private Task<ContentDownloadSnapshot> activeTask;

        public ContentPackageDownloadTask(
            ContentPackageDescriptor package,
            IContentPackageTransfer transfer)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            this.transfer = transfer ?? throw new ArgumentNullException(nameof(transfer));
            if (string.IsNullOrWhiteSpace(package.PackageId))
                throw new ArgumentException("Package id cannot be empty.", nameof(package));
            if (package.DownloadBytes <= 0)
                throw new ArgumentException("Package download size must be greater than zero.", nameof(package));
        }

        public event Action<ContentDownloadSnapshot> Changed;
        public event Action<ContentDownloadFailure> FailureReported;

        public ContentDownloadSnapshot Current
        {
            get
            {
                lock (gate)
                    return SnapshotLocked();
            }
        }

        public Task<ContentDownloadSnapshot> StartAsync()
        {
            ContentDownloadSnapshot changed;
            Task<ContentDownloadSnapshot> result;
            lock (gate)
            {
                if (state == ContentDownloadState.Downloading)
                    return activeTask;
                if (state == ContentDownloadState.Completed)
                    return Task.FromResult(SnapshotLocked());

                attempt++;
                stopRequest = StopRequest.None;
                archivePath = null;
                errorMessage = null;
                state = ContentDownloadState.Downloading;
                cancellation = new CancellationTokenSource();
                int runAttempt = attempt;
                activeTask = RunAsync(runAttempt, cancellation);
                result = activeTask;
                changed = SnapshotLocked();
            }

            PublishChanged(changed);
            return result;
        }

        public Task<ContentDownloadSnapshot> RetryAsync()
        {
            lock (gate)
            {
                if (state != ContentDownloadState.Failed)
                    return Task.FromResult(SnapshotLocked());
            }
            return StartAsync();
        }

        public Task<ContentDownloadSnapshot> PauseAsync()
        {
            CancellationTokenSource source;
            Task<ContentDownloadSnapshot> task;
            lock (gate)
            {
                if (state != ContentDownloadState.Downloading)
                    return Task.FromResult(SnapshotLocked());
                if (stopRequest == StopRequest.None)
                    stopRequest = StopRequest.Pause;
                source = cancellation;
                task = activeTask;
            }

            TryCancel(source);
            return task;
        }

        public Task<ContentDownloadSnapshot> CancelAsync()
        {
            CancellationTokenSource source = null;
            Task<ContentDownloadSnapshot> task = null;
            lock (gate)
            {
                if (state == ContentDownloadState.Completed || state == ContentDownloadState.Cancelled)
                    return Task.FromResult(SnapshotLocked());
                if (state == ContentDownloadState.Downloading)
                {
                    stopRequest = StopRequest.Cancel;
                    source = cancellation;
                    task = activeTask;
                }
            }

            if (source != null)
            {
                TryCancel(source);
                return task;
            }
            return Task.FromResult(CancelStoppedTask());
        }

        private async Task<ContentDownloadSnapshot> RunAsync(
            int runAttempt,
            CancellationTokenSource runCancellation)
        {
            CancellationToken token = runCancellation.Token;
            await Task.Yield();
            try
            {
                token.ThrowIfCancellationRequested();
                long existingBytes = transfer.GetDownloadedBytes(package);
                if (existingBytes < 0)
                    throw new InvalidOperationException("Downloaded byte count cannot be negative.");
                if (existingBytes > package.DownloadBytes)
                {
                    transfer.DeletePartial(package);
                    existingBytes = 0;
                }

                ReportProgress(runAttempt, existingBytes);
                await transfer.DownloadAsync(
                    package,
                    existingBytes,
                    new InlineProgress(bytes => ReportProgress(runAttempt, bytes)),
                    token);

                if (HasStopRequest(runAttempt))
                    return HandleStop(runAttempt);
                token.ThrowIfCancellationRequested();

                long finalBytes = transfer.GetDownloadedBytes(package);
                if (finalBytes != package.DownloadBytes)
                {
                    throw new InvalidOperationException(
                        $"Download ended with {finalBytes} persisted bytes; expected {package.DownloadBytes} bytes.");
                }

                string completedArchivePath = transfer.GetArchivePath(package);
                if (string.IsNullOrWhiteSpace(completedArchivePath))
                    throw new InvalidOperationException("Completed download has no archive path.");
                return Complete(runAttempt, completedArchivePath);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return HandleStop(runAttempt);
            }
            catch (Exception exception)
            {
                if (HasStopRequest(runAttempt))
                    return HandleStop(runAttempt);
                return Fail(runAttempt, exception.Message);
            }
            finally
            {
                runCancellation.Dispose();
                lock (gate)
                {
                    if (ReferenceEquals(cancellation, runCancellation))
                        cancellation = null;
                }
            }
        }

        private ContentDownloadSnapshot HandleStop(int runAttempt)
        {
            StopRequest request;
            lock (gate)
            {
                if (attempt != runAttempt)
                    return SnapshotLocked();
                request = stopRequest;
            }

            if (request == StopRequest.Cancel)
            {
                try
                {
                    transfer.DeletePartial(package);
                }
                catch (Exception exception)
                {
                    return Fail(runAttempt, "Partial download could not be deleted: " + exception.Message);
                }
                return Transition(runAttempt, ContentDownloadState.Cancelled, 0, null, null);
            }

            long persisted;
            lock (gate)
                persisted = downloadedBytes;
            try
            {
                persisted = transfer.GetDownloadedBytes(package);
            }
            catch
            {
                // The most recent persisted progress is still safe for display.
            }
            return Transition(runAttempt, ContentDownloadState.Paused, persisted, null, null);
        }

        private ContentDownloadSnapshot CancelStoppedTask()
        {
            int currentAttempt;
            lock (gate)
                currentAttempt = attempt;

            try
            {
                transfer.DeletePartial(package);
            }
            catch (Exception exception)
            {
                return Fail(currentAttempt, "Partial download could not be deleted: " + exception.Message);
            }
            return Transition(currentAttempt, ContentDownloadState.Cancelled, 0, null, null);
        }

        private ContentDownloadSnapshot Complete(int runAttempt, string completedArchivePath)
        {
            return Transition(
                runAttempt,
                ContentDownloadState.Completed,
                package.DownloadBytes,
                completedArchivePath,
                null);
        }

        private ContentDownloadSnapshot Fail(int runAttempt, string message)
        {
            ContentDownloadSnapshot snapshot;
            ContentDownloadFailure failure;
            lock (gate)
            {
                if (attempt != runAttempt)
                    return SnapshotLocked();
                if (state == ContentDownloadState.Failed && string.Equals(errorMessage, message, StringComparison.Ordinal))
                    return SnapshotLocked();

                state = ContentDownloadState.Failed;
                archivePath = null;
                errorMessage = string.IsNullOrWhiteSpace(message) ? "Content download failed." : message.Trim();
                snapshot = SnapshotLocked();
                failure = new ContentDownloadFailure(package.PackageId, attempt, errorMessage);
            }

            PublishChanged(snapshot);
            PublishFailure(failure);
            return snapshot;
        }

        private ContentDownloadSnapshot Transition(
            int runAttempt,
            ContentDownloadState nextState,
            long persistedBytes,
            string completedArchivePath,
            string error)
        {
            ContentDownloadSnapshot snapshot;
            lock (gate)
            {
                if (attempt != runAttempt)
                    return SnapshotLocked();
                state = nextState;
                downloadedBytes = ClampBytes(persistedBytes);
                archivePath = completedArchivePath;
                errorMessage = error;
                stopRequest = StopRequest.None;
                snapshot = SnapshotLocked();
            }

            PublishChanged(snapshot);
            return snapshot;
        }

        private void ReportProgress(int runAttempt, long persistedBytes)
        {
            ContentDownloadSnapshot snapshot;
            lock (gate)
            {
                if (attempt != runAttempt || state != ContentDownloadState.Downloading)
                    return;
                long clamped = ClampBytes(persistedBytes);
                if (clamped <= downloadedBytes)
                    return;
                downloadedBytes = clamped;
                snapshot = SnapshotLocked();
            }

            PublishChanged(snapshot);
        }

        private bool HasStopRequest(int runAttempt)
        {
            lock (gate)
                return attempt == runAttempt && stopRequest != StopRequest.None;
        }

        private long ClampBytes(long value)
        {
            return Math.Max(0, Math.Min(value, package.DownloadBytes));
        }

        private ContentDownloadSnapshot SnapshotLocked()
        {
            return new ContentDownloadSnapshot(
                package.PackageId,
                state,
                downloadedBytes,
                package.DownloadBytes,
                attempt,
                archivePath,
                errorMessage);
        }

        private void PublishChanged(ContentDownloadSnapshot snapshot)
        {
            InvokeSafely(Changed, snapshot);
        }

        private void PublishFailure(ContentDownloadFailure failure)
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
                    // A presentation subscriber must not corrupt the transfer state.
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
                // The operation completed between the state check and cancellation.
            }
        }

        private sealed class InlineProgress : IProgress<long>
        {
            private readonly Action<long> report;

            public InlineProgress(Action<long> report)
            {
                this.report = report;
            }

            public void Report(long value)
            {
                report(value);
            }
        }
    }
}
