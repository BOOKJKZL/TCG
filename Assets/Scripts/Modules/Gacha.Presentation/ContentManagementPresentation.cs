using System;
using System.Threading;
using Gacha.Application;

namespace Gacha.Presentation
{
    public enum ContentPackagePrimaryAction
    {
        None,
        Install,
        Update,
        Repair,
        Resume,
        Retry
    }

    public sealed class ContentPackageUiState
    {
        internal ContentPackageUiState(
            string statusKey,
            ContentPackagePrimaryAction primaryAction,
            string primaryActionKey,
            bool showProgress,
            bool isBusy,
            bool isError)
        {
            StatusKey = statusKey;
            PrimaryAction = primaryAction;
            PrimaryActionKey = primaryActionKey;
            ShowProgress = showProgress;
            IsBusy = isBusy;
            IsError = isError;
        }

        public string StatusKey { get; }
        public ContentPackagePrimaryAction PrimaryAction { get; }
        public string PrimaryActionKey { get; }
        public bool ShowProgress { get; }
        public bool IsBusy { get; }
        public bool IsError { get; }
    }

    public sealed class ContentPackageItemPresentation
    {
        private ContentPackageItemPresentation(
            ContentPackageCatalogEntry entry,
            ContentPackageOperationSnapshot operation,
            ContentPackageUiState uiState)
        {
            Entry = entry;
            Operation = operation;
            UiState = uiState;
        }

        public ContentPackageCatalogEntry Entry { get; }
        public ContentPackageOperationSnapshot Operation { get; }
        public ContentPackageUiState UiState { get; }
        public string PackageId => Entry.Package.PackageId;
        public string Version => Entry.Package.Version;
        public long DownloadBytes => Entry.Package.DownloadBytes;
        public float Progress01 => Operation?.Progress01 ?? 0f;
        public bool CanPause => Operation?.CanPause == true;
        public bool CanCancel => Operation?.CanCancel == true;
        public string ErrorMessage => Operation?.ErrorMessage;
        public string WarningMessage => Operation?.WarningMessage;

        public static ContentPackageItemPresentation Create(
            ContentPackageCatalogEntry entry,
            ContentPackageOperationSnapshot operation)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));
            if (operation == null)
                throw new ArgumentNullException(nameof(operation));
            if (!string.Equals(
                    entry.Package.PackageId,
                    operation.PackageId,
                    StringComparison.Ordinal))
                throw new ArgumentException("Package entry and operation snapshot ids do not match.", nameof(operation));

            ContentInstallPlanStatus? planStatus = operation.Plan?.Status;
            ContentInstallAction action = operation.Plan?.Action ?? ContentInstallAction.Install;
            return new ContentPackageItemPresentation(
                entry,
                operation,
                Resolve(operation.State, planStatus, action));
        }

        public static ContentPackageUiState Resolve(
            ContentPackageOperationState state,
            ContentInstallPlanStatus? planStatus = null,
            ContentInstallAction action = ContentInstallAction.Install)
        {
            switch (state)
            {
                case ContentPackageOperationState.Planning:
                    return State("content.status.checking", ContentPackagePrimaryAction.None, null, false, true, false);
                case ContentPackageOperationState.Blocked:
                    return State(
                        BlockedStatusKey(planStatus),
                        ContentPackagePrimaryAction.Retry,
                        "content.action.retry",
                        false,
                        false,
                        true);
                case ContentPackageOperationState.Downloading:
                    return State("content.status.downloading", ContentPackagePrimaryAction.None, null, true, true, false);
                case ContentPackageOperationState.Paused:
                    return State("content.status.paused", ContentPackagePrimaryAction.Resume, "content.action.resume", true, false, false);
                case ContentPackageOperationState.Installing:
                    return State("content.status.installing", ContentPackagePrimaryAction.None, null, true, true, false);
                case ContentPackageOperationState.Succeeded:
                    return State("content.status.installed", ContentPackagePrimaryAction.None, null, true, false, false);
                case ContentPackageOperationState.AlreadyCurrent:
                    return State("content.status.current", ContentPackagePrimaryAction.None, null, true, false, false);
                case ContentPackageOperationState.Cancelled:
                    return State("content.status.cancelled", Action(action), ActionKey(action), false, false, false);
                case ContentPackageOperationState.Failed:
                    return State("content.status.failed", ContentPackagePrimaryAction.Retry, "content.action.retry", true, false, true);
                default:
                    return State("content.status.ready", Action(action), ActionKey(action), false, false, false);
            }
        }

        private static ContentPackageUiState State(
            string statusKey,
            ContentPackagePrimaryAction action,
            string actionKey,
            bool showProgress,
            bool isBusy,
            bool isError)
        {
            return new ContentPackageUiState(statusKey, action, actionKey, showProgress, isBusy, isError);
        }

        private static string BlockedStatusKey(ContentInstallPlanStatus? status)
        {
            switch (status)
            {
                case ContentInstallPlanStatus.InsufficientSpace:
                    return "content.status.insufficient_space";
                case ContentInstallPlanStatus.InvalidPackage:
                    return "content.status.invalid_package";
                case ContentInstallPlanStatus.StorageUnavailable:
                    return "content.status.storage_unavailable";
                default:
                    return "content.status.blocked";
            }
        }

        private static ContentPackagePrimaryAction Action(ContentInstallAction action)
        {
            switch (action)
            {
                case ContentInstallAction.Update: return ContentPackagePrimaryAction.Update;
                case ContentInstallAction.Repair: return ContentPackagePrimaryAction.Repair;
                default: return ContentPackagePrimaryAction.Install;
            }
        }

        private static string ActionKey(ContentInstallAction action)
        {
            switch (action)
            {
                case ContentInstallAction.Update: return "content.action.update";
                case ContentInstallAction.Repair: return "content.action.repair";
                default: return "content.action.install";
            }
        }
    }

    public interface IUiThreadDispatcher
    {
        bool IsDispatchThread { get; }
        void Post(Action action);
    }

    public sealed class SynchronizationContextUiThreadDispatcher : IUiThreadDispatcher
    {
        private readonly SynchronizationContext context;
        private readonly int threadId;

        public SynchronizationContextUiThreadDispatcher(SynchronizationContext context)
        {
            this.context = context ?? throw new ArgumentNullException(nameof(context));
            threadId = Environment.CurrentManagedThreadId;
        }

        public bool IsDispatchThread => Environment.CurrentManagedThreadId == threadId;

        public void Post(Action action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            if (IsDispatchThread)
            {
                action();
                return;
            }
            context.Post(_ => action(), null);
        }
    }

    /// <summary>
    /// Converts coordinator callbacks from HTTP/file worker threads into UI-thread
    /// callbacks. Queued work is ignored after the page unbinds.
    /// </summary>
    public sealed class ContentPackageOperationUiBridge : IDisposable
    {
        private readonly ContentPackageInstallCoordinator coordinator;
        private readonly IUiThreadDispatcher dispatcher;
        private volatile bool disposed;

        public ContentPackageOperationUiBridge(
            ContentPackageInstallCoordinator coordinator,
            IUiThreadDispatcher dispatcher)
        {
            this.coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            coordinator.Changed += OnChanged;
            coordinator.FailureReported += OnFailureReported;
        }

        public event Action<ContentPackageOperationSnapshot> Changed;
        public event Action<ContentPackageOperationFailure> FailureReported;

        public ContentPackageOperationSnapshot Current => coordinator.Current;

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            coordinator.Changed -= OnChanged;
            coordinator.FailureReported -= OnFailureReported;
            Changed = null;
            FailureReported = null;
        }

        private void OnChanged(ContentPackageOperationSnapshot value)
        {
            dispatcher.Post(() =>
            {
                if (!disposed)
                    InvokeSafely(Changed, value);
            });
        }

        private void OnFailureReported(ContentPackageOperationFailure value)
        {
            dispatcher.Post(() =>
            {
                if (!disposed)
                    InvokeSafely(FailureReported, value);
            });
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
                    // A broken visual subscriber cannot break another row.
                }
            }
        }
    }
}
