using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Gacha.Application;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Gacha.Infrastructure.Content
{
    /// <summary>
    /// Persists only a successfully parsed remote catalog. The cache is bound to
    /// the configured source URI so changing servers cannot silently reuse an old
    /// package list. Installed content remains usable when the network is offline.
    /// </summary>
    public sealed class CachedContentPackageCatalogProvider : IContentPackageCatalogProvider, IDisposable
    {
        private const int CacheSchemaVersion = 1;
        private const int EnvelopeAllowanceBytes = 16 * 1024;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        private readonly object gate = new object();
        private readonly IContentPackageCatalogProvider upstream;
        private readonly string cachePath;
        private readonly Uri sourceCatalogUri;
        private readonly int maximumCatalogBytes;
        private readonly JsonContentPackageCatalogReader reader;
        private bool disposed;

        public CachedContentPackageCatalogProvider(
            IContentPackageCatalogProvider upstream,
            string cachePath,
            Uri sourceCatalogUri,
            int maximumCatalogBytes = HttpContentPackageCatalogProvider.DefaultMaximumCatalogBytes,
            JsonContentPackageCatalogReader reader = null)
        {
            this.upstream = upstream ?? throw new ArgumentNullException(nameof(upstream));
            if (string.IsNullOrWhiteSpace(cachePath))
                throw new ArgumentException("Catalog cache path cannot be empty.", nameof(cachePath));
            this.cachePath = Path.GetFullPath(cachePath);
            this.sourceCatalogUri = sourceCatalogUri ?? throw new ArgumentNullException(nameof(sourceCatalogUri));
            if (!sourceCatalogUri.IsAbsoluteUri)
                throw new ArgumentException("Catalog source URI must be absolute.", nameof(sourceCatalogUri));
            if (!string.IsNullOrEmpty(sourceCatalogUri.UserInfo))
                throw new ArgumentException("Catalog source URI cannot contain embedded credentials.", nameof(sourceCatalogUri));
            if (!string.IsNullOrEmpty(sourceCatalogUri.Fragment))
                throw new ArgumentException("Catalog source URI cannot contain a fragment.", nameof(sourceCatalogUri));
            if (maximumCatalogBytes < 1024 || maximumCatalogBytes > 4 * 1024 * 1024)
                throw new ArgumentOutOfRangeException(nameof(maximumCatalogBytes));
            this.maximumCatalogBytes = maximumCatalogBytes;
            this.reader = reader ?? new JsonContentPackageCatalogReader();
        }

        public async Task<ContentPackageCatalogLoadResult> LoadAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            ContentPackageCatalogLoadResult online;
            try
            {
                online = await upstream.LoadAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
                online = ContentPackageCatalogLoadResult.Failure(
                    "Remote content package catalog failed unexpectedly: " + exception.Message);
            }

            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            if (online != null && online.Succeeded)
            {
                string warning = online.WarningMessage;
                if (!online.UsedCachedCatalog)
                {
                    try
                    {
                        lock (gate)
                            WriteCache(online.Catalog);
                    }
                    catch (Exception exception) when (!(exception is OutOfMemoryException))
                    {
                        warning = CombineWarnings(
                            warning,
                            "The verified catalog loaded, but its offline cache could not be updated: " +
                            exception.Message);
                    }
                }
                return ContentPackageCatalogLoadResult.Success(
                    online.Catalog,
                    warning,
                    online.UsedCachedCatalog);
            }

            string onlineError = online?.ErrorMessage ?? "Remote content package catalog returned no result.";
            try
            {
                ContentPackageCatalog cached;
                lock (gate)
                    cached = ReadCache();
                if (cached != null)
                {
                    return ContentPackageCatalogLoadResult.Success(
                        cached,
                        onlineError + " The last verified catalog is being used.",
                        true);
                }
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
                return ContentPackageCatalogLoadResult.Failure(
                    onlineError + " The offline catalog cache is unavailable: " + exception.Message);
            }

            return ContentPackageCatalogLoadResult.Failure(onlineError);
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed)
                    return;
                disposed = true;
                if (upstream is IDisposable disposable)
                    disposable.Dispose();
            }
        }

        private void WriteCache(ContentPackageCatalog catalog)
        {
            ThrowIfDisposed();
            JObject catalogObject = SerializeCatalog(catalog);
            byte[] catalogBytes = StrictUtf8.GetBytes(catalogObject.ToString(Formatting.None));
            if (catalogBytes.Length > maximumCatalogBytes)
            {
                throw new InvalidDataException(
                    $"Serialized catalog uses {catalogBytes.Length} bytes; cache limit is {maximumCatalogBytes} bytes.");
            }

            var envelope = new JObject
            {
                ["cacheSchemaVersion"] = CacheSchemaVersion,
                ["sourceCatalogUrl"] = sourceCatalogUri.AbsoluteUri,
                ["catalog"] = catalogObject
            };
            byte[] bytes = StrictUtf8.GetBytes(envelope.ToString(Formatting.None));
            long maximumEnvelopeBytes = (long)maximumCatalogBytes + EnvelopeAllowanceBytes;
            if (bytes.Length > maximumEnvelopeBytes)
                throw new InvalidDataException("Catalog cache envelope exceeds the configured size limit.");
            string directory = Path.GetDirectoryName(cachePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            if (File.Exists(cachePath) && IsLink(cachePath))
                throw new InvalidDataException("Catalog cache cannot be a file link.");

            RecoverBackupIfNeeded();
            string temporaryPath = cachePath + ".tmp";
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
            File.WriteAllBytes(temporaryPath, bytes);
            try
            {
                CommitTemporary(temporaryPath);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }

        private ContentPackageCatalog ReadCache()
        {
            ThrowIfDisposed();
            RecoverBackupIfNeeded();
            if (!File.Exists(cachePath))
                return null;
            if (IsLink(cachePath))
                throw new InvalidDataException("Catalog cache cannot be a file link.");

            long maximumEnvelopeBytes = (long)maximumCatalogBytes + EnvelopeAllowanceBytes;
            long length = new FileInfo(cachePath).Length;
            if (length <= 0 || length > maximumEnvelopeBytes)
            {
                throw new InvalidDataException(
                    $"Catalog cache length {length} is outside the allowed range.");
            }

            string json = StrictUtf8.GetString(File.ReadAllBytes(cachePath));
            JObject envelope = JObject.Parse(json);
            if (envelope.Value<int?>("cacheSchemaVersion") != CacheSchemaVersion)
                throw new InvalidDataException("Catalog cache schema is not supported.");
            string cachedSource = envelope.Value<string>("sourceCatalogUrl");
            if (!string.Equals(cachedSource, sourceCatalogUri.AbsoluteUri, StringComparison.Ordinal))
                throw new InvalidDataException("Catalog cache belongs to a different configured source.");
            if (!(envelope["catalog"] is JObject catalogObject))
                throw new InvalidDataException("Catalog cache has no catalog object.");

            string catalogJson = catalogObject.ToString(Formatting.None);
            if (StrictUtf8.GetByteCount(catalogJson) > maximumCatalogBytes)
                throw new InvalidDataException("Cached catalog exceeds the configured size limit.");
            ContentPackageCatalogLoadResult result = reader.Read(catalogJson, sourceCatalogUri);
            if (!result.Succeeded)
                throw new InvalidDataException(result.ErrorMessage);
            return result.Catalog;
        }

        private void CommitTemporary(string temporaryPath)
        {
            if (!File.Exists(cachePath))
            {
                File.Move(temporaryPath, cachePath);
                return;
            }

            try
            {
                File.Replace(temporaryPath, cachePath, null);
                return;
            }
            catch (PlatformNotSupportedException)
            {
                // Some Android filesystems do not expose File.Replace.
            }
            catch (IOException)
            {
                // Fall back to a recoverable rename transaction on the same volume.
            }

            string backupPath = cachePath + ".backup";
            if (File.Exists(backupPath))
                File.Delete(backupPath);
            File.Move(cachePath, backupPath);
            try
            {
                File.Move(temporaryPath, cachePath);
                File.Delete(backupPath);
            }
            catch
            {
                if (!File.Exists(cachePath) && File.Exists(backupPath))
                    File.Move(backupPath, cachePath);
                throw;
            }
        }

        private void RecoverBackupIfNeeded()
        {
            string backupPath = cachePath + ".backup";
            if (!File.Exists(backupPath))
                return;
            if (File.Exists(cachePath))
                File.Delete(backupPath);
            else
                File.Move(backupPath, cachePath);
        }

        private static JObject SerializeCatalog(ContentPackageCatalog catalog)
        {
            var packages = new JArray();
            foreach (ContentPackageCatalogEntry entry in catalog.Packages)
            {
                ContentPackageDescriptor package = entry.Package;
                packages.Add(new JObject
                {
                    ["packageId"] = package.PackageId,
                    ["installRelativePath"] = package.InstallRelativePath,
                    ["revision"] = package.Revision,
                    ["version"] = package.Version,
                    ["downloadBytes"] = package.DownloadBytes,
                    ["installedBytes"] = package.InstalledBytes,
                    ["sha256"] = package.Sha256,
                    ["archiveUrl"] = entry.ArchiveUri.AbsoluteUri
                });
            }
            return new JObject
            {
                ["schemaVersion"] = catalog.SchemaVersion,
                ["revision"] = catalog.Revision,
                ["packages"] = packages
            };
        }

        private static bool IsLink(string path)
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }

        private static string CombineWarnings(string first, string second)
        {
            if (string.IsNullOrWhiteSpace(first))
                return string.IsNullOrWhiteSpace(second) ? null : second.Trim();
            if (string.IsNullOrWhiteSpace(second))
                return first.Trim();
            return first.Trim() + " | " + second.Trim();
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(CachedContentPackageCatalogProvider));
        }
    }
}
