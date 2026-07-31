using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using Gacha.Application;

namespace Gacha.Infrastructure.Content
{
    /// <summary>
    /// Creates and caches one operation per package id. All operations share an
    /// HTTP client while each immutable catalog remains its own URI resolver.
    /// </summary>
    public sealed class HttpContentPackageInstallCoordinatorFactory :
        IContentPackageInstallCoordinatorFactory,
        IDisposable
    {
        private readonly object gate = new object();
        private readonly string downloadRoot;
        private readonly ContentPackagePlanner planner;
        private readonly IContentPackageInstaller installer;
        private readonly HttpClient client;
        private readonly bool ownsClient;
        private readonly Dictionary<string, CachedOperation> operations =
            new Dictionary<string, CachedOperation>(StringComparer.Ordinal);
        private readonly SemaphoreSlim installationGate = new SemaphoreSlim(1, 1);
        private bool disposed;

        public HttpContentPackageInstallCoordinatorFactory(
            string downloadRoot,
            ContentPackagePlanner planner,
            IContentPackageInstaller installer,
            HttpClient client = null)
        {
            if (string.IsNullOrWhiteSpace(downloadRoot))
                throw new ArgumentException("Download root cannot be empty.", nameof(downloadRoot));
            this.downloadRoot = Path.GetFullPath(downloadRoot);
            this.planner = planner ?? throw new ArgumentNullException(nameof(planner));
            this.installer = installer ?? throw new ArgumentNullException(nameof(installer));
            this.client = client ?? new HttpClient();
            ownsClient = client == null;
        }

        public ContentPackageInstallCoordinator Create(ContentPackageCatalog catalog, string packageId)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            if (string.IsNullOrWhiteSpace(packageId))
                throw new ArgumentException("Package id cannot be empty.", nameof(packageId));

            ContentPackageCatalogEntry entry = catalog.Find(packageId);
            if (entry == null)
                throw new KeyNotFoundException($"Package '{packageId}' is not present in catalog revision {catalog.Revision}.");

            lock (gate)
            {
                if (disposed)
                    throw new ObjectDisposedException(nameof(HttpContentPackageInstallCoordinatorFactory));
                if (operations.TryGetValue(entry.Package.PackageId, out CachedOperation cached))
                {
                    if (Matches(cached.Package, entry.Package))
                        return cached.Coordinator;
                    if (!CanReplace(cached.Coordinator.Current.State))
                    {
                        throw new InvalidOperationException(
                            $"Package '{entry.Package.PackageId}' revision {cached.Package.Revision} must be cancelled before catalog revision {entry.Package.Revision} can start.");
                    }
                    operations.Remove(entry.Package.PackageId);
                }

                var source = new HttpContentPackageByteSource(catalog, client);
                var transfer = new FileSystemContentPackageTransfer(downloadRoot, source);
                var coordinator = new ContentPackageInstallCoordinator(
                    entry.Package,
                    planner,
                    transfer,
                    installer,
                    installationGate);
                operations.Add(
                    entry.Package.PackageId,
                    new CachedOperation(entry.Package, coordinator));
                return coordinator;
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed)
                    return;
                disposed = true;
                operations.Clear();
                if (ownsClient)
                    client.Dispose();
                installationGate.Dispose();
            }
        }

        private static bool Matches(ContentPackageDescriptor first, ContentPackageDescriptor second)
        {
            return first.Revision == second.Revision &&
                   first.DownloadBytes == second.DownloadBytes &&
                   first.InstalledBytes == second.InstalledBytes &&
                   string.Equals(first.PackageId, second.PackageId, StringComparison.Ordinal) &&
                   string.Equals(first.InstallRelativePath, second.InstallRelativePath, StringComparison.Ordinal) &&
                   string.Equals(first.Version, second.Version, StringComparison.Ordinal) &&
                   string.Equals(first.Sha256, second.Sha256, StringComparison.OrdinalIgnoreCase);
        }

        private static bool CanReplace(ContentPackageOperationState state)
        {
            return state == ContentPackageOperationState.Succeeded ||
                   state == ContentPackageOperationState.AlreadyCurrent ||
                   state == ContentPackageOperationState.Cancelled;
        }

        private sealed class CachedOperation
        {
            public CachedOperation(
                ContentPackageDescriptor package,
                ContentPackageInstallCoordinator coordinator)
            {
                Package = package;
                Coordinator = coordinator;
            }

            public ContentPackageDescriptor Package { get; }
            public ContentPackageInstallCoordinator Coordinator { get; }
        }
    }
}
