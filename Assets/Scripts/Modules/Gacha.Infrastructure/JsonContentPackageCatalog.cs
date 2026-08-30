using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Gacha.Application;
using Newtonsoft.Json;

namespace Gacha.Infrastructure.Content
{
    /// <summary>
    /// Reads schema-versioned package metadata. Archive URLs must be content
    /// addressed by the package SHA-256 so a resume never crosses object versions.
    /// </summary>
    public sealed class JsonContentPackageCatalogReader
    {
        private readonly ContentCatalogCompatibilityPolicy compatibilityPolicy;

        public JsonContentPackageCatalogReader(
            ContentCatalogCompatibilityPolicy compatibilityPolicy = null)
        {
            this.compatibilityPolicy = compatibilityPolicy;
        }

        public ContentPackageCatalogLoadResult Read(string json, Uri catalogUri)
        {
            if (string.IsNullOrWhiteSpace(json))
                return ContentPackageCatalogLoadResult.Failure("Content package catalog JSON is empty.");
            if (catalogUri == null || !catalogUri.IsAbsoluteUri)
                return ContentPackageCatalogLoadResult.Failure("Content package catalog URI must be absolute.");

            try
            {
                CatalogDto dto = JsonConvert.DeserializeObject<CatalogDto>(
                    json,
                    new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error });
                if (dto == null)
                    return ContentPackageCatalogLoadResult.Failure("Content package catalog JSON has no root object.");
                if (dto.SchemaVersion < ContentPackageCatalog.MinimumSupportedSchemaVersion ||
                    dto.SchemaVersion > ContentPackageCatalog.MaximumSupportedSchemaVersion)
                    throw new InvalidDataException(
                        $"Content package catalog schema {dto.SchemaVersion} is not supported.");

                var entries = new List<ContentPackageCatalogEntry>();
                if (dto.Packages != null)
                {
                    foreach (PackageDto item in dto.Packages)
                    {
                        if (item == null)
                            throw new InvalidDataException("Content package catalog contains an empty package entry.");

                        var package = new ContentPackageDescriptor(
                            item.PackageId,
                            item.InstallRelativePath,
                            item.Revision,
                            item.Version,
                            item.DownloadBytes,
                            item.InstalledBytes,
                            item.Sha256);
                        string validationError = ContentPackagePlanner.ValidateDescriptor(package);
                        if (validationError != null)
                            throw new InvalidDataException($"Package '{package.PackageId}' is invalid: {validationError}");
                        if (string.IsNullOrWhiteSpace(item.ArchiveUrl))
                            throw new InvalidDataException($"Package '{package.PackageId}' has no archive URL.");
                        if (!Uri.TryCreate(catalogUri, item.ArchiveUrl.Trim(), out Uri archiveUri))
                            throw new InvalidDataException($"Package '{package.PackageId}' has an invalid archive URL.");
                        bool isHttps = string.Equals(
                            archiveUri.Scheme,
                            Uri.UriSchemeHttps,
                            StringComparison.OrdinalIgnoreCase);
                        bool isLoopbackHttp = string.Equals(
                            archiveUri.Scheme,
                            Uri.UriSchemeHttp,
                            StringComparison.OrdinalIgnoreCase) && archiveUri.IsLoopback;
                        if (!isHttps && !isLoopbackHttp)
                        {
                            throw new InvalidDataException(
                                $"Package '{package.PackageId}' archive URL must use HTTPS; HTTP is allowed only for loopback fixtures.");
                        }
                        if (archiveUri.AbsolutePath.IndexOf(package.Sha256, StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            throw new InvalidDataException(
                                $"Package '{package.PackageId}' archive URL must contain its SHA-256 for immutable resume.");
                        }

                        ContentPackageMetadata metadata = dto.SchemaVersion >= 2
                            ? ParseMetadata(item, package.PackageId)
                            : null;
                        if (dto.SchemaVersion < 2 && item.Metadata != null)
                            throw new InvalidDataException(
                                $"Package '{package.PackageId}' cannot contain metadata before schema v2.");
                        entries.Add(new ContentPackageCatalogEntry(
                            package,
                            archiveUri,
                            metadata,
                            item.ArchiveUrl.Trim()));
                    }
                }

                if (dto.SchemaVersion < ContentPackageCatalog.ProtectedSchemaVersion)
                {
                    if (dto.MinAppVersion != null || dto.ContentSchemaVersion != 0 ||
                        dto.RuleSchemaVersion != 0 || dto.Signature != null)
                        throw new InvalidDataException(
                            "Protected catalog fields require schema v3.");
                    return ContentPackageCatalogLoadResult.Success(
                        new ContentPackageCatalog(dto.SchemaVersion, dto.Revision, entries));
                }

                if (compatibilityPolicy == null)
                    throw new InvalidDataException(
                        "Catalog schema v3 requires a runtime compatibility and trust policy.");
                SignatureDto signatureDto = dto.Signature ?? throw new InvalidDataException(
                    "Catalog schema v3 has no signature.");
                var signature = new ContentCatalogSignature(
                    signatureDto.Algorithm,
                    signatureDto.KeyId,
                    signatureDto.Value);
                byte[] canonicalPayload = ContentCatalogCanonicalizer.Canonicalize(
                    dto.SchemaVersion,
                    dto.Revision,
                    dto.MinAppVersion,
                    dto.ContentSchemaVersion,
                    dto.RuleSchemaVersion,
                    entries);
                string compatibilityError = compatibilityPolicy.Validate(
                    dto.MinAppVersion,
                    dto.ContentSchemaVersion,
                    dto.RuleSchemaVersion,
                    signature,
                    canonicalPayload);
                if (compatibilityError != null)
                    throw new InvalidDataException(compatibilityError);

                return ContentPackageCatalogLoadResult.Success(new ContentPackageCatalog(
                    dto.SchemaVersion,
                    dto.Revision,
                    entries,
                    dto.MinAppVersion,
                    dto.ContentSchemaVersion,
                    dto.RuleSchemaVersion,
                    signature,
                    ContentCatalogCanonicalizer.ComputeSha256(canonicalPayload)));
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
                return ContentPackageCatalogLoadResult.Failure(
                    "Content package catalog is invalid: " + exception.Message);
            }
        }

        private sealed class CatalogDto
        {
            public int SchemaVersion;
            public long Revision;
            public string MinAppVersion;
            public int ContentSchemaVersion;
            public int RuleSchemaVersion;
            public List<PackageDto> Packages;
            public SignatureDto Signature;
        }

        private sealed class SignatureDto
        {
            public string Algorithm;
            public string KeyId;
            public string Value;
        }

        private sealed class PackageDto
        {
            public string PackageId;
            public string InstallRelativePath;
            public long Revision;
            public string Version;
            public long DownloadBytes;
            public long InstalledBytes;
            public string Sha256;
            public string ArchiveUrl;
            public MetadataDto Metadata;
        }

        private sealed class MetadataDto
        {
            public string Kind;
            public string GameId;
            public string ContentLanguageId;
            public Dictionary<string, string> LocalizedNames;
            public string SetId;
            public string SetCode;
            public string ReleaseDate;
            public int? GenerationOrder;
            public int? SortOrdinal;
            public List<string> Tags;
            public List<string> Dependencies;
        }

        private static ContentPackageMetadata ParseMetadata(PackageDto item, string packageId)
        {
            MetadataDto source = item.Metadata ?? throw new InvalidDataException(
                $"Package '{packageId}' has no schema v2 metadata.");
            DateTime? releaseDate = null;
            if (!string.IsNullOrWhiteSpace(source.ReleaseDate))
            {
                if (!DateTime.TryParseExact(
                        source.ReleaseDate.Trim(),
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out DateTime parsed))
                    throw new InvalidDataException(
                        $"Package '{packageId}' releaseDate must use yyyy-MM-dd.");
                releaseDate = parsed;
            }
            return new ContentPackageMetadata(
                source.Kind,
                source.LocalizedNames,
                source.GameId,
                source.ContentLanguageId,
                source.SetId,
                source.SetCode,
                releaseDate,
                source.GenerationOrder,
                source.SortOrdinal,
                source.Tags,
                source.Dependencies);
        }
    }

    public sealed class FileSystemContentPackageCatalogProvider : IContentPackageCatalogProvider
    {
        private readonly string catalogPath;
        private readonly Uri catalogUri;
        private readonly JsonContentPackageCatalogReader reader;

        public FileSystemContentPackageCatalogProvider(
            string catalogPath,
            Uri catalogUri,
            JsonContentPackageCatalogReader reader = null)
        {
            if (string.IsNullOrWhiteSpace(catalogPath))
                throw new ArgumentException("Catalog path cannot be empty.", nameof(catalogPath));
            this.catalogPath = Path.GetFullPath(catalogPath);
            this.catalogUri = catalogUri ?? throw new ArgumentNullException(nameof(catalogUri));
            this.reader = reader ?? new JsonContentPackageCatalogReader();
        }

        public Task<ContentPackageCatalogLoadResult> LoadAsync(CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string json = File.ReadAllText(catalogPath);
                    cancellationToken.ThrowIfCancellationRequested();
                    return reader.Read(json, catalogUri);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    return ContentPackageCatalogLoadResult.Failure(
                        "Content package catalog could not be read: " + exception.Message);
                }
            }, cancellationToken);
        }
    }
}
