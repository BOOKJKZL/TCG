using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Gacha.Application;
using Gacha.Infrastructure.Content;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Gacha.EditorTools.Content
{
    public sealed class R2ReleasePublishRequest
    {
        public R2ReleasePublishRequest(
            string releaseDirectory,
            Uri publicBaseUri,
            string objectPrefix,
            string runtimeConfigPath,
            ContentCatalogTrustBundle trustBundle = null,
            string candidateAppVersion = null)
        {
            ReleaseDirectory = releaseDirectory;
            PublicBaseUri = publicBaseUri;
            ObjectPrefix = objectPrefix;
            RuntimeConfigPath = runtimeConfigPath;
            TrustBundle = trustBundle;
            CandidateAppVersion = candidateAppVersion;
        }

        public string ReleaseDirectory { get; }
        public Uri PublicBaseUri { get; }
        public string ObjectPrefix { get; }
        public string RuntimeConfigPath { get; }
        public ContentCatalogTrustBundle TrustBundle { get; }
        public string CandidateAppVersion { get; }
    }

    public sealed class R2ReleaseObject
    {
        public R2ReleaseObject(
            string objectKey,
            string localPath,
            Uri publicUri,
            long bytes,
            string sha256)
        {
            ObjectKey = objectKey;
            LocalPath = localPath;
            PublicUri = publicUri;
            Bytes = bytes;
            Sha256 = sha256;
        }

        public string ObjectKey { get; }
        public string LocalPath { get; }
        public Uri PublicUri { get; }
        public long Bytes { get; }
        public string Sha256 { get; }
    }

    public sealed class R2ReleaseUploadPlan
    {
        public R2ReleaseUploadPlan(
            string releaseDirectory,
            string catalogPath,
            string catalogObjectKey,
            Uri catalogUri,
            string runtimeConfigPath,
            IReadOnlyList<R2ReleaseObject> archives,
            long catalogBytes,
            string catalogSha256,
            int catalogSchemaVersion,
            ContentCatalogTrustBundle trustBundle)
        {
            ReleaseDirectory = releaseDirectory;
            CatalogPath = catalogPath;
            CatalogObjectKey = catalogObjectKey;
            CatalogUri = catalogUri;
            RuntimeConfigPath = runtimeConfigPath;
            Archives = archives;
            CatalogBytes = catalogBytes;
            CatalogSha256 = catalogSha256;
            CatalogSchemaVersion = catalogSchemaVersion;
            TrustBundle = trustBundle;
        }

        public string ReleaseDirectory { get; }
        public string CatalogPath { get; }
        public string CatalogObjectKey { get; }
        public Uri CatalogUri { get; }
        public string RuntimeConfigPath { get; }
        public IReadOnlyList<R2ReleaseObject> Archives { get; }
        public long CatalogBytes { get; }
        public string CatalogSha256 { get; }
        public int CatalogSchemaVersion { get; }
        public ContentCatalogTrustBundle TrustBundle { get; }
    }

    public sealed class R2RemoteObjectState
    {
        private R2RemoteObjectState(bool exists, long bytes, string sha256)
        {
            Exists = exists;
            Bytes = bytes;
            Sha256 = sha256;
        }

        public bool Exists { get; }
        public long Bytes { get; }
        public string Sha256 { get; }

        public static R2RemoteObjectState Missing()
        {
            return new R2RemoteObjectState(false, 0, null);
        }

        public static R2RemoteObjectState Present(long bytes, string sha256)
        {
            return new R2RemoteObjectState(true, bytes, sha256);
        }
    }

    public interface IR2ReleaseObjectStore
    {
        Task<R2RemoteObjectState> InspectAsync(string objectKey, CancellationToken cancellationToken);

        Task UploadFileAsync(
            string objectKey,
            string localPath,
            string sha256,
            string contentType,
            string cacheControl,
            CancellationToken cancellationToken);

        Task UploadBytesAsync(
            string objectKey,
            byte[] bytes,
            string sha256,
            string contentType,
            string cacheControl,
            CancellationToken cancellationToken);

        Task<R2RemoteObjectState> VerifyOriginAsync(
            string objectKey,
            long expectedBytes,
            CancellationToken cancellationToken);

        Task<R2RemoteObjectState> VerifyPublicAsync(
            Uri publicUri,
            long expectedBytes,
            string expectedSha256,
            CancellationToken cancellationToken);
    }

    public sealed class R2ReleasePublishResult
    {
        public R2ReleasePublishResult(int uploadedArchives, int reusedArchives, Uri catalogUri, string runtimeConfigPath)
        {
            UploadedArchives = uploadedArchives;
            ReusedArchives = reusedArchives;
            CatalogUri = catalogUri;
            RuntimeConfigPath = runtimeConfigPath;
        }

        public int UploadedArchives { get; }
        public int ReusedArchives { get; }
        public Uri CatalogUri { get; }
        public string RuntimeConfigPath { get; }
    }

    /// <summary>
    /// Publishes immutable archives before the mutable catalog. A remote archive
    /// conflict never gets overwritten, and runtime configuration is written only
    /// after the origin and public read path both return the published bytes.
    /// </summary>
    public sealed class R2ReleasePublisher
    {
        private const int RuntimeCatalogLimitBytes = 1048576;
        private readonly IR2ReleaseObjectStore objectStore;

        public R2ReleasePublisher(IR2ReleaseObjectStore objectStore)
        {
            this.objectStore = objectStore ?? throw new ArgumentNullException(nameof(objectStore));
        }

        public static R2ReleaseUploadPlan CreatePlan(R2ReleasePublishRequest request)
        {
            ValidateRequest(request);
            string releaseRoot = Path.GetFullPath(request.ReleaseDirectory);
            string releasePrefix = AppendSeparator(releaseRoot);
            string catalogPath = Path.Combine(releaseRoot, "catalog.json");
            if (!File.Exists(catalogPath))
                throw new FileNotFoundException("Published content catalog was not found.", catalogPath);

            string prefix = NormalizeObjectPrefix(request.ObjectPrefix);
            string catalogObjectKey = CombineObjectKey(prefix, "catalog.json");
            Uri catalogUri = CombinePublicUri(request.PublicBaseUri, catalogObjectKey);
            byte[] catalogBytes = File.ReadAllBytes(catalogPath);
            if (catalogBytes.LongLength > RuntimeCatalogLimitBytes)
                throw new InvalidDataException("Published content catalog exceeds the runtime 1 MiB limit.");
            string catalogJson = DecodeStrictUtf8(catalogBytes, catalogPath);
            JsonContentPackageCatalogReader reader = request.TrustBundle == null
                ? new JsonContentPackageCatalogReader()
                : request.TrustBundle.CreateCatalogReader();
            ContentPackageCatalogLoadResult parsed = reader.Read(catalogJson, catalogUri);
            if (!parsed.Succeeded)
                throw new InvalidDataException("Published content catalog failed runtime validation: " + parsed.ErrorMessage);
            if (parsed.Catalog.Packages.Count == 0)
                throw new InvalidDataException("Published content catalog contains no packages.");

            var archives = new List<R2ReleaseObject>();
            var objectKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (ContentPackageCatalogEntry entry in parsed.Catalog.Packages
                         .OrderBy(item => item.Package.PackageId, StringComparer.Ordinal))
            {
                string relativePath = GetCatalogRelativePath(catalogUri, entry.ArchiveUri);
                string localPath = Path.GetFullPath(Path.Combine(
                    releaseRoot,
                    relativePath.Replace('/', Path.DirectorySeparatorChar)));
                if (!localPath.StartsWith(releasePrefix, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Catalog archive escapes the release directory: " + relativePath);
                if (!File.Exists(localPath))
                    throw new FileNotFoundException("Catalog archive was not found.", localPath);

                long bytes = new FileInfo(localPath).Length;
                if (bytes != entry.Package.DownloadBytes)
                {
                    throw new InvalidDataException(
                        $"Archive '{entry.Package.PackageId}' size is {bytes}, expected {entry.Package.DownloadBytes}.");
                }
                string sha256 = ComputeSha256(localPath, CancellationToken.None);
                if (!string.Equals(sha256, entry.Package.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Archive hash does not match catalog: " + entry.Package.PackageId);

                string objectKey = CombineObjectKey(prefix, relativePath);
                if (!objectKeys.Add(objectKey))
                    throw new InvalidDataException("Catalog maps multiple packages to object key: " + objectKey);
                archives.Add(new R2ReleaseObject(
                    objectKey,
                    localPath,
                    entry.ArchiveUri,
                    bytes,
                    sha256));
            }

            return new R2ReleaseUploadPlan(
                releaseRoot,
                catalogPath,
                catalogObjectKey,
                catalogUri,
                Path.GetFullPath(request.RuntimeConfigPath),
                archives.AsReadOnly(),
                catalogBytes.LongLength,
                ComputeSha256(catalogBytes),
                parsed.Catalog.SchemaVersion,
                request.TrustBundle);
        }

        public async Task<R2ReleasePublishResult> PublishAsync(
            R2ReleaseUploadPlan plan,
            CancellationToken cancellationToken = default)
        {
            if (plan == null)
                throw new ArgumentNullException(nameof(plan));

            int uploaded = 0;
            int reused = 0;
            for (int index = 0; index < plan.Archives.Count; index++)
            {
                R2ReleaseObject archive = plan.Archives[index];
                cancellationToken.ThrowIfCancellationRequested();
                R2RemoteObjectState existing = await objectStore
                    .InspectAsync(archive.ObjectKey, cancellationToken);
                if (existing.Exists)
                {
                    AssertRemoteObject(archive.ObjectKey, existing, archive.Bytes, archive.Sha256, true);
                    reused++;
                }
                else
                {
                    await objectStore.UploadFileAsync(
                        archive.ObjectKey,
                        archive.LocalPath,
                        archive.Sha256,
                        "application/zip",
                        "public, max-age=31536000, immutable",
                        cancellationToken);
                    R2RemoteObjectState uploadedState = await objectStore
                        .InspectAsync(archive.ObjectKey, cancellationToken);
                    AssertRemoteObject(archive.ObjectKey, uploadedState, archive.Bytes, archive.Sha256, false);
                    uploaded++;
                }

                R2RemoteObjectState origin = await objectStore
                    .VerifyOriginAsync(archive.ObjectKey, archive.Bytes, cancellationToken);
                AssertRemoteObject(archive.ObjectKey, origin, archive.Bytes, archive.Sha256, false);
                R2RemoteObjectState publicRead = await objectStore
                    .VerifyPublicAsync(archive.PublicUri, archive.Bytes, archive.Sha256, cancellationToken);
                AssertRemoteObject(archive.ObjectKey, publicRead, archive.Bytes, archive.Sha256, false);
                int completed = index + 1;
                if (completed == 1 || completed % 25 == 0 || completed == plan.Archives.Count)
                {
                    Debug.Log(
                        $"Content publication progress: {completed}/{plan.Archives.Count}, " +
                        $"uploaded={uploaded}, reused={reused}, current='{archive.ObjectKey}'.");
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            byte[] catalogBytes = File.ReadAllBytes(plan.CatalogPath);
            if (catalogBytes.LongLength != plan.CatalogBytes ||
                !string.Equals(ComputeSha256(catalogBytes), plan.CatalogSha256, StringComparison.Ordinal))
                throw new InvalidDataException("Local catalog changed after the upload plan was created.");

            await objectStore.UploadBytesAsync(
                plan.CatalogObjectKey,
                catalogBytes,
                plan.CatalogSha256,
                "application/json; charset=utf-8",
                "no-cache, no-store, must-revalidate",
                cancellationToken);
            R2RemoteObjectState catalogOrigin = await objectStore
                .VerifyOriginAsync(plan.CatalogObjectKey, plan.CatalogBytes, cancellationToken);
            AssertRemoteObject(
                plan.CatalogObjectKey,
                catalogOrigin,
                plan.CatalogBytes,
                plan.CatalogSha256,
                false);
            R2RemoteObjectState catalogPublic = await objectStore
                .VerifyPublicAsync(plan.CatalogUri, plan.CatalogBytes, plan.CatalogSha256, cancellationToken);
            AssertRemoteObject(
                plan.CatalogObjectKey,
                catalogPublic,
                plan.CatalogBytes,
                plan.CatalogSha256,
                false);

            WriteRuntimeConfigAtomic(plan.RuntimeConfigPath, plan.CatalogUri, plan.TrustBundle);
            return new R2ReleasePublishResult(uploaded, reused, plan.CatalogUri, plan.RuntimeConfigPath);
        }

        private static void AssertRemoteObject(
            string objectKey,
            R2RemoteObjectState state,
            long expectedBytes,
            string expectedSha256,
            bool immutableConflict)
        {
            if (state == null || !state.Exists)
                throw new IOException("Remote object was not found after upload: " + objectKey);
            bool wrongBytes = state.Bytes != expectedBytes;
            bool wrongHash = !string.IsNullOrWhiteSpace(state.Sha256) &&
                             !string.Equals(state.Sha256, expectedSha256, StringComparison.OrdinalIgnoreCase);
            if (!wrongBytes && !wrongHash)
                return;

            string prefix = immutableConflict
                ? "Refusing to overwrite conflicting immutable object: "
                : "Remote object verification failed: ";
            throw new IOException(prefix + objectKey);
        }

        private static string GetCatalogRelativePath(Uri catalogUri, Uri archiveUri)
        {
            if (!string.Equals(catalogUri.Scheme, archiveUri.Scheme, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(catalogUri.Host, archiveUri.Host, StringComparison.OrdinalIgnoreCase) ||
                catalogUri.Port != archiveUri.Port ||
                !string.IsNullOrEmpty(archiveUri.Query))
                throw new InvalidDataException("Catalog archives must share the configured public origin and have no query.");

            string relative = Uri.UnescapeDataString(catalogUri.MakeRelativeUri(archiveUri).ToString())
                .Replace('\\', '/');
            if (relative.StartsWith("../", StringComparison.Ordinal) ||
                relative.StartsWith("/", StringComparison.Ordinal) ||
                relative.Contains(":") ||
                relative.Split('/').Any(segment => string.IsNullOrEmpty(segment) || segment == "." || segment == ".."))
                throw new InvalidDataException("Catalog archive is outside its release directory: " + archiveUri);
            return relative;
        }

        private static void ValidateRequest(R2ReleasePublishRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.ReleaseDirectory))
                throw new ArgumentException("Release directory cannot be empty.", nameof(request));
            if (!Directory.Exists(request.ReleaseDirectory))
                throw new DirectoryNotFoundException("Release directory was not found: " + request.ReleaseDirectory);
            if (request.PublicBaseUri == null || !request.PublicBaseUri.IsAbsoluteUri ||
                !string.Equals(request.PublicBaseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Public content base URL must be absolute HTTPS.", nameof(request));
            if (!string.IsNullOrEmpty(request.PublicBaseUri.UserInfo) ||
                !string.IsNullOrEmpty(request.PublicBaseUri.Query) ||
                !string.IsNullOrEmpty(request.PublicBaseUri.Fragment))
                throw new ArgumentException("Public content base URL cannot contain credentials, query, or fragment.", nameof(request));
            if (string.IsNullOrWhiteSpace(request.RuntimeConfigPath))
                throw new ArgumentException("Runtime configuration path cannot be empty.", nameof(request));
            if (request.TrustBundle != null)
            {
                if (string.IsNullOrWhiteSpace(request.CandidateAppVersion) ||
                    !string.Equals(
                        request.CandidateAppVersion,
                        request.CandidateAppVersion.Trim(),
                        StringComparison.Ordinal))
                    throw new ArgumentException(
                        "A trust bundle requires an exact candidate App version.", nameof(request));
                if (!string.Equals(
                        request.CandidateAppVersion,
                        request.TrustBundle.CurrentAppVersion,
                        StringComparison.Ordinal))
                    throw new InvalidDataException(
                        "Catalog trust bundle currentAppVersion does not match the candidate App version.");
            }
            else if (!string.IsNullOrEmpty(request.CandidateAppVersion))
            {
                throw new ArgumentException(
                    "Candidate App version is valid only when a Catalog trust bundle is supplied.", nameof(request));
            }
            NormalizeObjectPrefix(request.ObjectPrefix);
        }

        private static string NormalizeObjectPrefix(string value)
        {
            string prefix = (value ?? string.Empty).Trim().Replace('\\', '/').Trim('/');
            if (prefix.Length == 0)
                return string.Empty;
            foreach (string segment in prefix.Split('/'))
            {
                if (!IsPortableObjectSegment(segment))
                    throw new ArgumentException("R2 object prefix contains a non-portable segment: " + segment);
            }
            return prefix;
        }

        private static bool IsPortableObjectSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "." || value == "..")
                return false;
            return value.All(character =>
                char.IsLetterOrDigit(character) || character == '-' || character == '_' || character == '.');
        }

        private static string CombineObjectKey(string prefix, string relativePath)
        {
            string normalized = relativePath.Replace('\\', '/').TrimStart('/');
            foreach (string segment in normalized.Split('/'))
            {
                if (!IsPortableObjectSegment(segment))
                    throw new InvalidDataException("Release contains a non-portable object key: " + relativePath);
            }
            return string.IsNullOrEmpty(prefix) ? normalized : prefix + "/" + normalized;
        }

        private static Uri CombinePublicUri(Uri baseUri, string objectKey)
        {
            string root = baseUri.AbsoluteUri.TrimEnd('/') + "/";
            string escaped = string.Join("/", objectKey.Split('/').Select(Uri.EscapeDataString));
            return new Uri(new Uri(root), escaped);
        }

        private static string DecodeStrictUtf8(byte[] bytes, string path)
        {
            try
            {
                int offset = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf ? 3 : 0;
                return new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset);
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidDataException("Catalog is not valid UTF-8: " + path, exception);
            }
        }

        private static string ComputeSha256(string path, CancellationToken cancellationToken)
        {
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 sha = SHA256.Create())
            {
                var buffer = new byte[81920];
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    sha.TransformBlock(buffer, 0, read, buffer, 0);
                }
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return ToHex(sha.Hash);
            }
        }

        internal static string ComputeSha256(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
                return ToHex(sha.ComputeHash(bytes));
        }

        internal static string ToHex(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static void WriteRuntimeConfigAtomic(
            string path,
            Uri catalogUri,
            ContentCatalogTrustBundle trustBundle)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            var root = new JObject
            {
                ["catalogUrl"] = catalogUri.AbsoluteUri,
                ["timeoutSeconds"] = 15,
                ["maxCatalogBytes"] = RuntimeCatalogLimitBytes
            };
            if (trustBundle != null)
                root["trustedCatalogKeys"] = trustBundle.RuntimeTrustedKeys();
            string json = root.ToString(Formatting.Indented) + "\n";
            string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(temporary, json, new UTF8Encoding(false));
            try
            {
                if (File.Exists(path))
                    File.Replace(temporary, path, null);
                else
                    File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }

        private static string AppendSeparator(string value)
        {
            return value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        }
    }
}
