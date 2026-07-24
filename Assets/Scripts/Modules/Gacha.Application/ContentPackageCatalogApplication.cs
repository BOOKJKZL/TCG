using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace Gacha.Application
{
    public sealed class ContentPackageCatalogEntry
    {
        public ContentPackageCatalogEntry(ContentPackageDescriptor package, Uri archiveUri)
        {
            Package = package ?? throw new ArgumentNullException(nameof(package));
            ArchiveUri = archiveUri ?? throw new ArgumentNullException(nameof(archiveUri));
            if (!archiveUri.IsAbsoluteUri)
                throw new ArgumentException("Content package archive URI must be absolute.", nameof(archiveUri));
            if (!string.IsNullOrEmpty(archiveUri.UserInfo))
                throw new ArgumentException("Content package archive URI cannot contain embedded credentials.", nameof(archiveUri));
            if (!string.IsNullOrEmpty(archiveUri.Fragment))
                throw new ArgumentException("Content package archive URI cannot contain a fragment.", nameof(archiveUri));
        }

        public ContentPackageDescriptor Package { get; }
        public Uri ArchiveUri { get; }
    }

    /// <summary>
    /// Immutable remote package snapshot. The same instance is also the URI
    /// resolver used by the HTTP byte source, preventing a descriptor from being
    /// resolved against a different catalog revision by package id alone.
    /// </summary>
    public sealed class ContentPackageCatalog : IContentPackageUriResolver
    {
        public const int SupportedSchemaVersion = 1;

        private readonly ReadOnlyCollection<ContentPackageCatalogEntry> packages;
        private readonly Dictionary<string, ContentPackageCatalogEntry> packagesById;

        public ContentPackageCatalog(
            int schemaVersion,
            long revision,
            IEnumerable<ContentPackageCatalogEntry> packages)
        {
            if (schemaVersion != SupportedSchemaVersion)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(schemaVersion),
                    $"Content package catalog schema {schemaVersion} is not supported.");
            }
            if (revision <= 0)
                throw new ArgumentOutOfRangeException(nameof(revision), "Catalog revision must be greater than zero.");
            if (packages == null)
                throw new ArgumentNullException(nameof(packages));

            var copy = new List<ContentPackageCatalogEntry>();
            packagesById = new Dictionary<string, ContentPackageCatalogEntry>(StringComparer.Ordinal);
            foreach (ContentPackageCatalogEntry entry in packages)
            {
                if (entry == null)
                    throw new ArgumentException("Content package catalog cannot contain an empty entry.", nameof(packages));
                string error = ContentPackagePlanner.ValidateDescriptor(entry.Package);
                if (error != null)
                    throw new ArgumentException($"Package '{entry.Package.PackageId}' is invalid: {error}", nameof(packages));
                if (packagesById.ContainsKey(entry.Package.PackageId))
                    throw new ArgumentException($"Content package catalog contains duplicate id '{entry.Package.PackageId}'.", nameof(packages));
                packagesById.Add(entry.Package.PackageId, entry);
                copy.Add(entry);
            }

            SchemaVersion = schemaVersion;
            Revision = revision;
            this.packages = copy.AsReadOnly();
        }

        public int SchemaVersion { get; }
        public long Revision { get; }
        public IReadOnlyList<ContentPackageCatalogEntry> Packages => packages;

        public ContentPackageCatalogEntry Find(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId))
                return null;
            packagesById.TryGetValue(packageId.Trim(), out ContentPackageCatalogEntry entry);
            return entry;
        }

        public Uri Resolve(ContentPackageDescriptor package)
        {
            if (package == null)
                throw new ArgumentNullException(nameof(package));
            if (!packagesById.TryGetValue(package.PackageId, out ContentPackageCatalogEntry entry))
                throw new KeyNotFoundException($"Package '{package.PackageId}' is not present in catalog revision {Revision}.");
            if (!Matches(entry.Package, package))
            {
                throw new InvalidOperationException(
                    $"Package '{package.PackageId}' does not match catalog revision {Revision}.");
            }
            return entry.ArchiveUri;
        }

        private static bool Matches(ContentPackageDescriptor expected, ContentPackageDescriptor actual)
        {
            return expected.Revision == actual.Revision &&
                   expected.DownloadBytes == actual.DownloadBytes &&
                   expected.InstalledBytes == actual.InstalledBytes &&
                   string.Equals(expected.PackageId, actual.PackageId, StringComparison.Ordinal) &&
                   string.Equals(expected.InstallRelativePath, actual.InstallRelativePath, StringComparison.Ordinal) &&
                   string.Equals(expected.Version, actual.Version, StringComparison.Ordinal) &&
                   string.Equals(expected.Sha256, actual.Sha256, StringComparison.OrdinalIgnoreCase);
        }
    }

    public sealed class ContentPackageCatalogLoadResult
    {
        private ContentPackageCatalogLoadResult(
            ContentPackageCatalog catalog,
            string errorMessage,
            string warningMessage,
            bool usedCachedCatalog)
        {
            Catalog = catalog;
            ErrorMessage = errorMessage;
            WarningMessage = warningMessage;
            UsedCachedCatalog = usedCachedCatalog;
        }

        public ContentPackageCatalog Catalog { get; }
        public string ErrorMessage { get; }
        public string WarningMessage { get; }
        public bool UsedCachedCatalog { get; }
        public bool Succeeded => Catalog != null && string.IsNullOrEmpty(ErrorMessage);

        public static ContentPackageCatalogLoadResult Success(
            ContentPackageCatalog catalog,
            string warningMessage = null,
            bool usedCachedCatalog = false)
        {
            return new ContentPackageCatalogLoadResult(
                catalog ?? throw new ArgumentNullException(nameof(catalog)),
                null,
                string.IsNullOrWhiteSpace(warningMessage) ? null : warningMessage.Trim(),
                usedCachedCatalog);
        }

        public static ContentPackageCatalogLoadResult Failure(string errorMessage)
        {
            return new ContentPackageCatalogLoadResult(
                null,
                string.IsNullOrWhiteSpace(errorMessage)
                    ? "Content package catalog could not be loaded."
                    : errorMessage.Trim(),
                null,
                false);
        }
    }

    public interface IContentPackageCatalogProvider
    {
        Task<ContentPackageCatalogLoadResult> LoadAsync(CancellationToken cancellationToken);
    }
}
