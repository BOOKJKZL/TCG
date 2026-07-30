using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Gacha.Application;
using Gacha.Infrastructure.Content;
using Newtonsoft.Json;

namespace Gacha.EditorTools.Content
{
    public sealed class ContentPackagePublishDefinition
    {
        public ContentPackagePublishDefinition(
            string packageId,
            string sourceDirectory,
            string installRelativePath,
            long revision,
            string version,
            IEnumerable<string> includedRelativePaths = null)
        {
            PackageId = packageId;
            SourceDirectory = sourceDirectory;
            InstallRelativePath = installRelativePath;
            Revision = revision;
            Version = version;
            IncludedRelativePaths = includedRelativePaths?.ToArray();
        }

        public string PackageId { get; }
        public string SourceDirectory { get; }
        public string InstallRelativePath { get; }
        public long Revision { get; }
        public string Version { get; }
        public IReadOnlyList<string> IncludedRelativePaths { get; }
    }

    public sealed class ContentPackagePublishRequest
    {
        public ContentPackagePublishRequest(
            string outputDirectory,
            long catalogRevision,
            IEnumerable<ContentPackagePublishDefinition> packages)
        {
            OutputDirectory = outputDirectory;
            CatalogRevision = catalogRevision;
            Packages = (packages ?? throw new ArgumentNullException(nameof(packages))).ToArray();
        }

        public string OutputDirectory { get; }
        public long CatalogRevision { get; }
        public IReadOnlyList<ContentPackagePublishDefinition> Packages { get; }
    }

    public sealed class PublishedContentPackage
    {
        public PublishedContentPackage(ContentPackageDescriptor package, string archivePath, string archiveUrl)
        {
            Package = package;
            ArchivePath = archivePath;
            ArchiveUrl = archiveUrl;
        }

        public ContentPackageDescriptor Package { get; }
        public string ArchivePath { get; }
        public string ArchiveUrl { get; }
    }

    public sealed class ContentPackagePublishResult
    {
        public ContentPackagePublishResult(
            string catalogPath,
            string catalogJson,
            IReadOnlyList<PublishedContentPackage> packages)
        {
            CatalogPath = catalogPath;
            CatalogJson = catalogJson;
            Packages = packages;
        }

        public string CatalogPath { get; }
        public string CatalogJson { get; }
        public IReadOnlyList<PublishedContentPackage> Packages { get; }
    }

    /// <summary>
    /// Builds stable archives from imported files. File timestamps and source
    /// enumeration order never enter the ZIP, so unchanged bytes keep one hash.
    /// </summary>
    public sealed class DeterministicContentPackagePublisher
    {
        private static readonly DateTimeOffset StableTimestamp =
            new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public ContentPackagePublishResult Publish(
            ContentPackagePublishRequest request,
            CancellationToken cancellationToken = default)
        {
            ValidateRequest(request);
            string outputRoot = Path.GetFullPath(request.OutputDirectory);
            Directory.CreateDirectory(outputRoot);
            string temporaryRoot = Path.Combine(outputRoot, ".publishing-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryRoot);

            try
            {
                var published = new List<PublishedContentPackage>();
                foreach (ContentPackagePublishDefinition definition in request.Packages
                             .OrderBy(item => item.PackageId, StringComparer.Ordinal))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    published.Add(PublishPackage(outputRoot, temporaryRoot, definition, cancellationToken));
                }

                string catalogJson = SerializeCatalog(request.CatalogRevision, published);
                ContentPackageCatalogLoadResult validation = new JsonContentPackageCatalogReader().Read(
                    catalogJson,
                    new Uri("https://publisher.invalid/releases/catalog.json"));
                if (!validation.Succeeded)
                    throw new InvalidDataException("Generated package catalog failed validation: " + validation.ErrorMessage);

                string catalogPath = Path.Combine(outputRoot, "catalog.json");
                WriteTextAtomic(catalogPath, catalogJson);
                return new ContentPackagePublishResult(catalogPath, catalogJson, published.AsReadOnly());
            }
            finally
            {
                DeleteDirectoryBestEffort(temporaryRoot);
            }
        }

        private static PublishedContentPackage PublishPackage(
            string outputRoot,
            string temporaryRoot,
            ContentPackagePublishDefinition definition,
            CancellationToken cancellationToken)
        {
            string sourceRoot = Path.GetFullPath(definition.SourceDirectory);
            IReadOnlyList<SourceFile> files = EnumerateSourceFiles(
                sourceRoot,
                outputRoot,
                definition.IncludedRelativePaths);
            long installedBytes = SumInstalledBytes(files);
            string temporaryArchive = Path.Combine(temporaryRoot, definition.PackageId + ".zip");
            WriteArchive(temporaryArchive, files, cancellationToken);
            long downloadBytes = new FileInfo(temporaryArchive).Length;
            string sha256 = ComputeSha256(temporaryArchive, cancellationToken);
            var descriptor = new ContentPackageDescriptor(
                definition.PackageId,
                definition.InstallRelativePath,
                definition.Revision,
                definition.Version,
                downloadBytes,
                installedBytes,
                sha256);
            string descriptorError = ContentPackagePlanner.ValidateDescriptor(descriptor);
            if (descriptorError != null)
                throw new InvalidDataException($"Package '{definition.PackageId}' is invalid: {descriptorError}");

            string archiveUrl = "packages/" + definition.PackageId + "/" + sha256 + ".zip";
            string archivePath = Path.Combine(
                outputRoot,
                "packages",
                definition.PackageId,
                sha256 + ".zip");
            Directory.CreateDirectory(Path.GetDirectoryName(archivePath));
            if (File.Exists(archivePath))
            {
                string existingHash = ComputeSha256(archivePath, cancellationToken);
                if (!string.Equals(existingHash, sha256, StringComparison.Ordinal))
                    throw new IOException("Existing content-addressed archive does not match its file name: " + archivePath);
                File.Delete(temporaryArchive);
            }
            else
            {
                File.Move(temporaryArchive, archivePath);
            }

            ValidateArchive(archivePath, files.Count, installedBytes, cancellationToken);
            return new PublishedContentPackage(descriptor, archivePath, archiveUrl);
        }

        private static IReadOnlyList<SourceFile> EnumerateSourceFiles(
            string sourceRoot,
            string outputRoot,
            IReadOnlyList<string> includedRelativePaths)
        {
            if (!Directory.Exists(sourceRoot))
                throw new DirectoryNotFoundException("Content package source directory was not found: " + sourceRoot);
            string sourcePrefix = AppendSeparator(sourceRoot);
            string outputPrefix = AppendSeparator(outputRoot);
            if (outputRoot.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase) ||
                sourceRoot.StartsWith(outputPrefix, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(sourceRoot, outputRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Content package source and output directories must not contain each other.");

            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var files = new List<SourceFile>();
            foreach (string directory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Content package source cannot contain directory links: " + directory);
            }
            IEnumerable<string> sourcePaths = includedRelativePaths == null
                ? Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
                : ResolveIncludedPaths(sourceRoot, sourcePrefix, includedRelativePaths);
            foreach (string path in sourcePaths)
            {
                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Content package source cannot contain file links: " + path);
                string relativePath = path.Substring(sourcePrefix.Length).Replace('\\', '/');
                ValidatePortablePath(relativePath);
                if (!paths.Add(relativePath))
                    throw new InvalidDataException("Content package contains a duplicate portable path: " + relativePath);
                files.Add(new SourceFile(Path.GetFullPath(path), relativePath, new FileInfo(path).Length));
            }
            files.Sort((left, right) => StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath));
            if (files.Count == 0)
                throw new InvalidDataException("Content package source contains no files.");
            return files;
        }

        private static IEnumerable<string> ResolveIncludedPaths(
            string sourceRoot,
            string sourcePrefix,
            IReadOnlyList<string> includedRelativePaths)
        {
            foreach (string requestedPath in includedRelativePaths)
            {
                string portablePath = (requestedPath ?? string.Empty).Replace('\\', '/');
                ValidatePortablePath(portablePath);
                string fullPath = Path.GetFullPath(Path.Combine(
                    sourceRoot,
                    portablePath.Replace('/', Path.DirectorySeparatorChar)));
                if (!fullPath.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Included content path escapes its source directory: " + requestedPath);
                if (!File.Exists(fullPath))
                    throw new FileNotFoundException("Included content file was not found.", fullPath);
                yield return fullPath;
            }
        }

        private static void WriteArchive(
            string archivePath,
            IReadOnlyList<SourceFile> files,
            CancellationToken cancellationToken)
        {
            using (FileStream stream = new FileStream(archivePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, false, Encoding.UTF8))
            {
                var buffer = new byte[81920];
                foreach (SourceFile file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ZipArchiveEntry entry = archive.CreateEntry(file.RelativePath, CompressionLevel.Optimal);
                    entry.LastWriteTime = StableTimestamp;
                    entry.ExternalAttributes = 0;
                    using (FileStream input = File.OpenRead(file.FullPath))
                    using (Stream output = entry.Open())
                    {
                        int read;
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            output.Write(buffer, 0, read);
                        }
                    }
                }
            }
        }

        private static void ValidateArchive(
            string archivePath,
            int expectedFiles,
            long expectedBytes,
            CancellationToken cancellationToken)
        {
            long bytes = 0;
            int files = 0;
            using (FileStream stream = File.OpenRead(archivePath))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, false, Encoding.UTF8))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
                        continue;
                    files++;
                    checked { bytes += entry.Length; }
                }
            }
            if (files != expectedFiles || bytes != expectedBytes)
            {
                throw new InvalidDataException(
                    $"Published archive verification failed: files {files}/{expectedFiles}, bytes {bytes}/{expectedBytes}.");
            }
        }

        private static long SumInstalledBytes(IReadOnlyList<SourceFile> files)
        {
            long total = 0;
            foreach (SourceFile file in files)
                checked { total += file.Bytes; }
            if (total <= 0)
                throw new InvalidDataException("Content package installed size must be greater than zero.");
            return total;
        }

        private static string SerializeCatalog(
            long revision,
            IReadOnlyList<PublishedContentPackage> packages)
        {
            var dto = new CatalogDto
            {
                schemaVersion = ContentPackageCatalog.SupportedSchemaVersion,
                revision = revision,
                packages = packages.Select(item => new PackageDto
                {
                    packageId = item.Package.PackageId,
                    installRelativePath = item.Package.InstallRelativePath,
                    revision = item.Package.Revision,
                    version = item.Package.Version,
                    downloadBytes = item.Package.DownloadBytes,
                    installedBytes = item.Package.InstalledBytes,
                    sha256 = item.Package.Sha256,
                    archiveUrl = item.ArchiveUrl
                }).ToArray()
            };

            var builder = new StringBuilder();
            using (var text = new StringWriter(builder, CultureInfo.InvariantCulture) { NewLine = "\n" })
            using (var json = new JsonTextWriter(text)
            {
                Formatting = Formatting.Indented,
                Indentation = 2,
                IndentChar = ' '
            })
            {
                JsonSerializer.CreateDefault().Serialize(json, dto);
            }
            return builder.ToString().TrimEnd('\r', '\n') + "\n";
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
                return BitConverter.ToString(sha.Hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static void WriteTextAtomic(string path, string value)
        {
            string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(temporary, value, new UTF8Encoding(false));
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

        private static void ValidateRequest(ContentPackagePublishRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.OutputDirectory))
                throw new ArgumentException("Content publication output directory cannot be empty.", nameof(request));
            if (request.CatalogRevision <= 0)
                throw new ArgumentOutOfRangeException(nameof(request), "Catalog revision must be greater than zero.");
            if (request.Packages == null || request.Packages.Count == 0)
                throw new ArgumentException("At least one content package is required.", nameof(request));

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var installPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ContentPackagePublishDefinition definition in request.Packages)
            {
                if (definition == null)
                    throw new ArgumentException("Content publication contains an empty package definition.", nameof(request));
                if (!ids.Add(definition.PackageId ?? string.Empty))
                    throw new InvalidDataException("Content publication contains duplicate package id: " + definition.PackageId);
                if (!installPaths.Add(definition.InstallRelativePath ?? string.Empty))
                    throw new InvalidDataException("Content publication contains duplicate install path: " + definition.InstallRelativePath);
            }
        }

        private static void ValidatePortablePath(string value)
        {
            string[] segments = value.Split('/');
            foreach (string segment in segments)
            {
                if (string.IsNullOrWhiteSpace(segment) || segment == "." || segment == ".." ||
                    segment.EndsWith(".", StringComparison.Ordinal) || segment.EndsWith(" ", StringComparison.Ordinal))
                    throw new InvalidDataException("Content source has a non-portable path: " + value);
                foreach (char character in segment)
                {
                    if (character < 32 || character == '<' || character == '>' || character == ':' ||
                        character == '"' || character == '|' || character == '?' || character == '*')
                        throw new InvalidDataException("Content source has a non-portable path: " + value);
                }
            }
        }

        private static string AppendSeparator(string value)
        {
            return value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        }

        private static void DeleteDirectoryBestEffort(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
            }
            catch
            {
                // A failed cleanup must not hide the publication result or error.
            }
        }

        private sealed class SourceFile
        {
            public SourceFile(string fullPath, string relativePath, long bytes)
            {
                FullPath = fullPath;
                RelativePath = relativePath;
                Bytes = bytes;
            }

            public string FullPath { get; }
            public string RelativePath { get; }
            public long Bytes { get; }
        }

        private sealed class CatalogDto
        {
            public int schemaVersion;
            public long revision;
            public PackageDto[] packages;
        }

        private sealed class PackageDto
        {
            public string packageId;
            public string installRelativePath;
            public long revision;
            public string version;
            public long downloadBytes;
            public long installedBytes;
            public string sha256;
            public string archiveUrl;
        }
    }
}
