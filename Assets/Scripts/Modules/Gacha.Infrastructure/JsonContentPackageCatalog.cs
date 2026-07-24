using System;
using System.Collections.Generic;
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
        public ContentPackageCatalogLoadResult Read(string json, Uri catalogUri)
        {
            if (string.IsNullOrWhiteSpace(json))
                return ContentPackageCatalogLoadResult.Failure("Content package catalog JSON is empty.");
            if (catalogUri == null || !catalogUri.IsAbsoluteUri)
                return ContentPackageCatalogLoadResult.Failure("Content package catalog URI must be absolute.");

            try
            {
                CatalogDto dto = JsonConvert.DeserializeObject<CatalogDto>(json);
                if (dto == null)
                    return ContentPackageCatalogLoadResult.Failure("Content package catalog JSON has no root object.");

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

                        entries.Add(new ContentPackageCatalogEntry(package, archiveUri));
                    }
                }

                return ContentPackageCatalogLoadResult.Success(new ContentPackageCatalog(
                    dto.SchemaVersion,
                    dto.Revision,
                    entries));
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
            public List<PackageDto> Packages;
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
