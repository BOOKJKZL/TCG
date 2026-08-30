using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace Gacha.Application
{
    public sealed class ContentPackageCatalogEntry
    {
        public ContentPackageCatalogEntry(
            ContentPackageDescriptor package,
            Uri archiveUri,
            ContentPackageMetadata metadata = null,
            string catalogArchiveUrl = null)
        {
            Package = package ?? throw new ArgumentNullException(nameof(package));
            ArchiveUri = archiveUri ?? throw new ArgumentNullException(nameof(archiveUri));
            Metadata = metadata ?? ContentPackageMetadata.Legacy(package.PackageId);
            if (!archiveUri.IsAbsoluteUri)
                throw new ArgumentException("Content package archive URI must be absolute.", nameof(archiveUri));
            if (!string.IsNullOrEmpty(archiveUri.UserInfo))
                throw new ArgumentException("Content package archive URI cannot contain embedded credentials.", nameof(archiveUri));
            if (!string.IsNullOrEmpty(archiveUri.Fragment))
                throw new ArgumentException("Content package archive URI cannot contain a fragment.", nameof(archiveUri));
            if (catalogArchiveUrl != null && string.IsNullOrWhiteSpace(catalogArchiveUrl))
                throw new ArgumentException("Catalog archive URL cannot be empty.", nameof(catalogArchiveUrl));
            CatalogArchiveUrl = string.IsNullOrWhiteSpace(catalogArchiveUrl)
                ? archiveUri.AbsoluteUri
                : catalogArchiveUrl.Trim();
        }

        public ContentPackageDescriptor Package { get; }
        public Uri ArchiveUri { get; }
        public ContentPackageMetadata Metadata { get; }
        public string CatalogArchiveUrl { get; }
    }

    public sealed class ContentCatalogSignature
    {
        public ContentCatalogSignature(string algorithm, string keyId, string value)
        {
            Algorithm = Required(algorithm, nameof(algorithm));
            KeyId = Required(keyId, nameof(keyId));
            Value = Required(value, nameof(value));
        }

        public string Algorithm { get; }
        public string KeyId { get; }
        public string Value { get; }

        private static string Required(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException(name + " cannot be empty.", name);
            if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
                throw new ArgumentException(name + " cannot contain surrounding whitespace.", name);
            return value;
        }
    }

    /// <summary>
    /// Immutable remote package snapshot. The same instance is also the URI
    /// resolver used by the HTTP byte source, preventing a descriptor from being
    /// resolved against a different catalog revision by package id alone.
    /// </summary>
    public sealed class ContentPackageCatalog : IContentPackageUriResolver
    {
        public const int MinimumSupportedSchemaVersion = 1;
        public const int LegacyPublishSchemaVersion = 2;
        public const int ProtectedSchemaVersion = 3;
        public const int SupportedSchemaVersion = LegacyPublishSchemaVersion;
        public const int MaximumSupportedSchemaVersion = ProtectedSchemaVersion;
        public const int CurrentContentSchemaVersion = 1;
        public const int CurrentRuleSchemaVersion = 1;

        private readonly ReadOnlyCollection<ContentPackageCatalogEntry> packages;
        private readonly Dictionary<string, ContentPackageCatalogEntry> packagesById;

        public ContentPackageCatalog(
            int schemaVersion,
            long revision,
            IEnumerable<ContentPackageCatalogEntry> packages)
            : this(schemaVersion, revision, packages, null, 0, 0, null, null)
        {
        }

        public ContentPackageCatalog(
            int schemaVersion,
            long revision,
            IEnumerable<ContentPackageCatalogEntry> packages,
            string minimumAppVersion,
            int contentSchemaVersion,
            int ruleSchemaVersion,
            ContentCatalogSignature signature,
            string canonicalSha256)
        {
            if (schemaVersion < MinimumSupportedSchemaVersion ||
                schemaVersion > MaximumSupportedSchemaVersion)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(schemaVersion),
                    $"Content package catalog schema {schemaVersion} is not supported; " +
                    $"expected {MinimumSupportedSchemaVersion}-{MaximumSupportedSchemaVersion}.");
            }
            if (revision <= 0)
                throw new ArgumentOutOfRangeException(nameof(revision), "Catalog revision must be greater than zero.");
            if (packages == null)
                throw new ArgumentNullException(nameof(packages));
            bool isProtected = schemaVersion >= ProtectedSchemaVersion;
            if (isProtected)
            {
                if (string.IsNullOrWhiteSpace(minimumAppVersion))
                    throw new ArgumentException(
                        "Protected catalogs require a minimum app version.", nameof(minimumAppVersion));
                if (contentSchemaVersion <= 0)
                    throw new ArgumentOutOfRangeException(nameof(contentSchemaVersion));
                if (ruleSchemaVersion <= 0)
                    throw new ArgumentOutOfRangeException(nameof(ruleSchemaVersion));
                if (signature == null)
                    throw new ArgumentNullException(nameof(signature));
                if (!IsSha256(canonicalSha256))
                    throw new ArgumentException(
                        "Protected catalog canonical SHA-256 must contain 64 hexadecimal characters.",
                        nameof(canonicalSha256));
            }
            else if (minimumAppVersion != null || contentSchemaVersion != 0 ||
                     ruleSchemaVersion != 0 || signature != null || canonicalSha256 != null)
            {
                throw new ArgumentException(
                    "Legacy catalogs cannot contain protected schema fields.", nameof(schemaVersion));
            }

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
            ValidateDependencies(copy, packagesById);

            SchemaVersion = schemaVersion;
            Revision = revision;
            MinimumAppVersion = minimumAppVersion?.Trim();
            ContentSchemaVersion = contentSchemaVersion;
            RuleSchemaVersion = ruleSchemaVersion;
            Signature = signature;
            CanonicalSha256 = canonicalSha256?.ToLowerInvariant();
            this.packages = copy.AsReadOnly();
        }

        public int SchemaVersion { get; }
        public long Revision { get; }
        public string MinimumAppVersion { get; }
        public int ContentSchemaVersion { get; }
        public int RuleSchemaVersion { get; }
        public ContentCatalogSignature Signature { get; }
        public string CanonicalSha256 { get; }
        public bool IsProtected => SchemaVersion >= ProtectedSchemaVersion;
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

        private static bool IsSha256(string value)
        {
            if (value == null || value.Length != 64)
                return false;
            foreach (char character in value)
            {
                bool digit = character >= '0' && character <= '9';
                bool lower = character >= 'a' && character <= 'f';
                bool upper = character >= 'A' && character <= 'F';
                if (!digit && !lower && !upper)
                    return false;
            }
            return true;
        }

        private static void ValidateDependencies(
            IReadOnlyCollection<ContentPackageCatalogEntry> entries,
            IReadOnlyDictionary<string, ContentPackageCatalogEntry> byId)
        {
            foreach (ContentPackageCatalogEntry entry in entries)
            foreach (string dependency in entry.Metadata.Dependencies)
            {
                if (string.Equals(dependency, entry.Package.PackageId, StringComparison.Ordinal))
                    throw new ArgumentException(
                        $"Package '{entry.Package.PackageId}' cannot depend on itself.", nameof(entries));
                if (!byId.ContainsKey(dependency))
                    throw new ArgumentException(
                        $"Package '{entry.Package.PackageId}' depends on missing '{dependency}'.",
                        nameof(entries));
            }

            var visiting = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            foreach (ContentPackageCatalogEntry entry in entries)
                Visit(entry.Package.PackageId, byId, visiting, visited);
        }

        private static void Visit(
            string packageId,
            IReadOnlyDictionary<string, ContentPackageCatalogEntry> byId,
            ISet<string> visiting,
            ISet<string> visited)
        {
            if (visited.Contains(packageId))
                return;
            if (!visiting.Add(packageId))
                throw new ArgumentException(
                    $"Content package dependency cycle contains '{packageId}'.", nameof(byId));
            foreach (string dependency in byId[packageId].Metadata.Dependencies)
                Visit(dependency, byId, visiting, visited);
            visiting.Remove(packageId);
            visited.Add(packageId);
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
