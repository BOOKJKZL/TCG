using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Gacha.Application
{
    public enum ContentPackageQueueItemState
    {
        Queued,
        Running,
        Paused,
        Succeeded,
        Failed,
        Cancelled
    }

    public sealed class ContentPackageQueueItemSnapshot
    {
        internal ContentPackageQueueItemSnapshot(
            string packageId,
            ContentPackageQueueItemState state,
            ContentPackageOperationState operationState,
            string errorMessage)
        {
            PackageId = packageId;
            State = state;
            OperationState = operationState;
            ErrorMessage = errorMessage;
        }

        public string PackageId { get; }
        public ContentPackageQueueItemState State { get; }
        public ContentPackageOperationState OperationState { get; }
        public string ErrorMessage { get; }
    }

    public sealed class ContentPackageQueueSnapshot
    {
        internal ContentPackageQueueSnapshot(
            IReadOnlyList<ContentPackageQueueItemSnapshot> items,
            bool paused,
            int maximumConcurrentDownloads)
        {
            Items = items;
            Paused = paused;
            MaximumConcurrentDownloads = maximumConcurrentDownloads;
        }

        public IReadOnlyList<ContentPackageQueueItemSnapshot> Items { get; }
        public bool Paused { get; }
        public int MaximumConcurrentDownloads { get; }
        public int QueuedCount => Items.Count(value => value.State == ContentPackageQueueItemState.Queued);
        public int RunningCount => Items.Count(value => value.State == ContentPackageQueueItemState.Running);
        public int SucceededCount => Items.Count(value => value.State == ContentPackageQueueItemState.Succeeded);
        public int FailedCount => Items.Count(value => value.State == ContentPackageQueueItemState.Failed);
        public bool IsComplete => Items.Count > 0 && Items.All(value =>
            value.State == ContentPackageQueueItemState.Succeeded ||
            value.State == ContentPackageQueueItemState.Cancelled ||
            value.State == ContentPackageQueueItemState.Failed);
    }

    public sealed class ContentPackageInstallQueue
    {
        public const int DefaultMaximumConcurrentDownloads = 2;
        public const int MinimumConcurrentDownloads = 1;
        public const int MaximumConcurrentDownloadsLimit = 3;

        private sealed class Item
        {
            public Item(string packageId)
            {
                PackageId = packageId;
            }

            public string PackageId { get; }
            public ContentPackageQueueItemState State = ContentPackageQueueItemState.Queued;
            public ContentPackageOperationState OperationState = ContentPackageOperationState.Idle;
            public string ErrorMessage;
            public ContentPackageInstallCoordinator Operation;
        }

        private readonly object gate = new object();
        private readonly ContentPackageCatalog catalog;
        private readonly IContentPackageInstallCoordinatorFactory factory;
        private readonly List<Item> items = new List<Item>();
        private readonly Dictionary<string, Item> byId =
            new Dictionary<string, Item>(StringComparer.Ordinal);
        private readonly HashSet<string> active = new HashSet<string>(StringComparer.Ordinal);
        private readonly int maximumConcurrentDownloads;
        private bool paused;
        private Task<ContentPackageQueueSnapshot> activeRun;

        public ContentPackageInstallQueue(
            ContentPackageCatalog catalog,
            IContentPackageInstallCoordinatorFactory factory,
            int maximumConcurrentDownloads = DefaultMaximumConcurrentDownloads)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
            if (maximumConcurrentDownloads < MinimumConcurrentDownloads ||
                maximumConcurrentDownloads > MaximumConcurrentDownloadsLimit)
                throw new ArgumentOutOfRangeException(
                    nameof(maximumConcurrentDownloads),
                    $"Concurrent downloads must be {MinimumConcurrentDownloads}-{MaximumConcurrentDownloadsLimit}.");
            this.maximumConcurrentDownloads = maximumConcurrentDownloads;
        }

        public event Action<ContentPackageQueueSnapshot> Changed;

        public ContentPackageQueueSnapshot Current
        {
            get
            {
                lock (gate)
                    return SnapshotLocked();
            }
        }

        public ContentPackageQueueSnapshot EnqueueSelection(IEnumerable<string> selectedPackageIds)
        {
            string[] ordered = DependencyOrder(selectedPackageIds).ToArray();
            ContentPackageQueueSnapshot snapshot;
            lock (gate)
            {
                foreach (string packageId in ordered)
                {
                    if (byId.ContainsKey(packageId))
                        continue;
                    var item = new Item(packageId);
                    items.Add(item);
                    byId.Add(packageId, item);
                }
                snapshot = SnapshotLocked();
            }
            Publish(snapshot);
            return snapshot;
        }

        public Task<ContentPackageQueueSnapshot> StartAsync()
        {
            lock (gate)
            {
                paused = false;
                if (activeRun != null && !activeRun.IsCompleted)
                    return activeRun;
                activeRun = RunAsync();
                return activeRun;
            }
        }

        public async Task<ContentPackageQueueSnapshot> PauseAsync()
        {
            ContentPackageInstallCoordinator[] operations;
            lock (gate)
            {
                paused = true;
                operations = active
                    .Select(id => byId[id].Operation)
                    .Where(value => value != null)
                    .ToArray();
            }
            await Task.WhenAll(operations.Select(value => value.PauseAsync()));
            ContentPackageQueueSnapshot snapshot = Current;
            Publish(snapshot);
            return snapshot;
        }

        public Task<ContentPackageQueueSnapshot> ResumeAsync()
        {
            lock (gate)
            {
                paused = false;
                foreach (Item item in items.Where(value =>
                             value.State == ContentPackageQueueItemState.Paused))
                    item.State = ContentPackageQueueItemState.Queued;
            }
            return StartAsync();
        }

        public Task<ContentPackageQueueSnapshot> RetryFailedAsync()
        {
            lock (gate)
            {
                foreach (Item item in items.Where(value =>
                             value.State == ContentPackageQueueItemState.Failed))
                {
                    item.State = ContentPackageQueueItemState.Queued;
                    item.ErrorMessage = null;
                }
            }
            return StartAsync();
        }

        public async Task<ContentPackageQueueSnapshot> CancelAsync()
        {
            ContentPackageInstallCoordinator[] operations;
            lock (gate)
            {
                paused = true;
                foreach (Item item in items.Where(value =>
                             value.State == ContentPackageQueueItemState.Queued ||
                             value.State == ContentPackageQueueItemState.Paused))
                    item.State = ContentPackageQueueItemState.Cancelled;
                operations = active
                    .Select(id => byId[id].Operation)
                    .Where(value => value != null)
                    .ToArray();
            }
            await Task.WhenAll(operations.Select(value => value.CancelAsync()));
            ContentPackageQueueSnapshot snapshot = Current;
            Publish(snapshot);
            return snapshot;
        }

        private async Task<ContentPackageQueueSnapshot> RunAsync()
        {
            var running = new List<Task>();
            while (true)
            {
                ContentPackageQueueSnapshot changed = null;
                lock (gate)
                {
                    running.RemoveAll(task => task.IsCompleted);
                    if (!paused)
                    {
                        while (active.Count < maximumConcurrentDownloads)
                        {
                            Item next = NextEligibleLocked();
                            if (next == null)
                                break;
                            next.State = ContentPackageQueueItemState.Running;
                            next.ErrorMessage = null;
                            active.Add(next.PackageId);
                            Task task = RunItemAsync(next);
                            running.Add(task);
                            changed = SnapshotLocked();
                        }
                    }
                    if (running.Count == 0)
                    {
                        FailBlockedDependentsLocked();
                        ContentPackageQueueSnapshot complete = SnapshotLocked();
                        activeRun = null;
                        return complete;
                    }
                }
                if (changed != null)
                    Publish(changed);
                await Task.WhenAny(running);
            }
        }

        private async Task RunItemAsync(Item item)
        {
            try
            {
                ContentPackageInstallCoordinator operation;
                lock (gate)
                {
                    item.Operation ??= factory.Create(catalog, item.PackageId);
                    operation = item.Operation;
                }
                ContentPackageOperationSnapshot result = operation.Current.CanRetry
                    ? await operation.RetryAsync()
                    : await operation.StartAsync();
                lock (gate)
                {
                    item.OperationState = result.State;
                    item.ErrorMessage = result.ErrorMessage;
                    item.State = QueueState(result.State);
                    active.Remove(item.PackageId);
                }
            }
            catch (Exception exception)
            {
                lock (gate)
                {
                    item.State = ContentPackageQueueItemState.Failed;
                    item.ErrorMessage = exception.Message;
                    active.Remove(item.PackageId);
                }
            }
            Publish(Current);
        }

        private Item NextEligibleLocked()
        {
            return items.FirstOrDefault(item =>
                item.State == ContentPackageQueueItemState.Queued &&
                catalog.Find(item.PackageId).Metadata.Dependencies.All(dependency =>
                    byId.TryGetValue(dependency, out Item required) &&
                    required.State == ContentPackageQueueItemState.Succeeded));
        }

        private void FailBlockedDependentsLocked()
        {
            foreach (Item item in items.Where(value =>
                         value.State == ContentPackageQueueItemState.Queued))
            {
                string failed = catalog.Find(item.PackageId).Metadata.Dependencies.FirstOrDefault(
                    dependency => byId.TryGetValue(dependency, out Item required) &&
                                  (required.State == ContentPackageQueueItemState.Failed ||
                                   required.State == ContentPackageQueueItemState.Cancelled));
                if (failed == null)
                    continue;
                item.State = ContentPackageQueueItemState.Failed;
                item.ErrorMessage = "Required package did not complete: " + failed;
            }
        }

        private IEnumerable<string> DependencyOrder(IEnumerable<string> selectedPackageIds)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var ordered = new List<string>();
            foreach (string packageId in (selectedPackageIds ?? Array.Empty<string>())
                         .Where(value => !string.IsNullOrWhiteSpace(value))
                         .Select(value => value.Trim())
                         .Distinct(StringComparer.Ordinal)
                         .OrderBy(value => value, StringComparer.Ordinal))
                Visit(packageId, visited, ordered);
            return ordered;
        }

        private void Visit(string packageId, ISet<string> visited, ICollection<string> ordered)
        {
            ContentPackageCatalogEntry entry = catalog.Find(packageId) ??
                                               throw new ArgumentException(
                                                   "Selected package is not in the catalog: " + packageId,
                                                   nameof(packageId));
            if (!visited.Add(packageId))
                return;
            foreach (string dependency in entry.Metadata.Dependencies)
                Visit(dependency, visited, ordered);
            ordered.Add(packageId);
        }

        private ContentPackageQueueSnapshot SnapshotLocked()
        {
            ContentPackageQueueItemSnapshot[] copy = items.Select(item =>
                new ContentPackageQueueItemSnapshot(
                    item.PackageId,
                    item.State,
                    item.OperationState,
                    item.ErrorMessage)).ToArray();
            return new ContentPackageQueueSnapshot(
                new ReadOnlyCollection<ContentPackageQueueItemSnapshot>(copy),
                paused,
                maximumConcurrentDownloads);
        }

        private static ContentPackageQueueItemState QueueState(ContentPackageOperationState state)
        {
            switch (state)
            {
                case ContentPackageOperationState.Succeeded:
                case ContentPackageOperationState.AlreadyCurrent:
                    return ContentPackageQueueItemState.Succeeded;
                case ContentPackageOperationState.Paused:
                    return ContentPackageQueueItemState.Paused;
                case ContentPackageOperationState.Cancelled:
                    return ContentPackageQueueItemState.Cancelled;
                case ContentPackageOperationState.Failed:
                case ContentPackageOperationState.Blocked:
                    return ContentPackageQueueItemState.Failed;
                default:
                    return ContentPackageQueueItemState.Running;
            }
        }

        private void Publish(ContentPackageQueueSnapshot snapshot)
        {
            Action<ContentPackageQueueSnapshot> handlers = Changed;
            if (handlers == null)
                return;
            foreach (Action<ContentPackageQueueSnapshot> handler in handlers.GetInvocationList())
            {
                try { handler(snapshot); }
                catch { }
            }
        }
    }
}
