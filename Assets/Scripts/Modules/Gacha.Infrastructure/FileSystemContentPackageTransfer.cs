using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Gacha.Application;

namespace Gacha.Infrastructure.Content
{
    /// <summary>
    /// Persists resumable package bytes as .part files and publishes a .zip only
    /// after the expected byte count is present. Hash verification remains the
    /// installer's responsibility.
    /// </summary>
    public sealed class FileSystemContentPackageTransfer : IContentPackageTransfer
    {
        private const int CopyBufferBytes = 81920;

        private readonly string downloadRoot;
        private readonly IContentPackageByteSource source;

        public FileSystemContentPackageTransfer(
            string downloadRoot,
            IContentPackageByteSource source)
        {
            if (string.IsNullOrWhiteSpace(downloadRoot))
                throw new ArgumentException("Download root cannot be empty.", nameof(downloadRoot));

            this.downloadRoot = Path.GetFullPath(downloadRoot);
            this.source = source ?? throw new ArgumentNullException(nameof(source));
        }

        public long GetDownloadedBytes(ContentPackageDescriptor package)
        {
            Paths paths = ResolvePaths(package);
            if (File.Exists(paths.Archive))
                return new FileInfo(paths.Archive).Length;
            return File.Exists(paths.Partial) ? new FileInfo(paths.Partial).Length : 0;
        }

        public async Task DownloadAsync(
            ContentPackageDescriptor package,
            long offset,
            IProgress<long> persistedBytesProgress,
            CancellationToken cancellationToken)
        {
            Paths paths = ResolvePaths(package);
            if (offset < 0 || offset > package.DownloadBytes)
                throw new ArgumentOutOfRangeException(nameof(offset));

            Directory.CreateDirectory(downloadRoot);
            if (File.Exists(paths.Archive))
            {
                long archiveBytes = new FileInfo(paths.Archive).Length;
                if (archiveBytes == package.DownloadBytes)
                {
                    DeleteFileIfExists(paths.Partial);
                    persistedBytesProgress?.Report(package.DownloadBytes);
                    return;
                }
                if (File.Exists(paths.Partial))
                    throw new IOException("Both an incomplete archive and a partial package file exist.");
                File.Move(paths.Archive, paths.Partial);
            }

            long partialBytes = File.Exists(paths.Partial) ? new FileInfo(paths.Partial).Length : 0;
            if (partialBytes != offset)
            {
                throw new IOException(
                    $"Partial package contains {partialBytes} bytes but resume offset is {offset} bytes.");
            }

            if (partialBytes < package.DownloadBytes)
            {
                using (Stream input = await source.OpenReadAsync(package, offset, cancellationToken))
                {
                    if (input == null)
                        throw new IOException("Package byte source returned no stream.");

                    using (var output = new FileStream(
                        paths.Partial,
                        FileMode.OpenOrCreate,
                        FileAccess.Write,
                        FileShare.None,
                        CopyBufferBytes,
                        FileOptions.Asynchronous | FileOptions.SequentialScan))
                    {
                        output.Position = partialBytes;
                        byte[] buffer = new byte[CopyBufferBytes];
                        while (partialBytes < package.DownloadBytes)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            int read = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                            if (read <= 0)
                                break;
                            if (partialBytes > package.DownloadBytes - read)
                                throw new IOException("Package source returned more bytes than declared.");

                            await output.WriteAsync(buffer, 0, read, cancellationToken);
                            partialBytes += read;
                            persistedBytesProgress?.Report(partialBytes);
                        }
                        await output.FlushAsync(cancellationToken);
                    }
                }
            }

            if (partialBytes == package.DownloadBytes)
            {
                if (File.Exists(paths.Archive))
                    throw new IOException("Completed package archive already exists.");
                File.Move(paths.Partial, paths.Archive);
                persistedBytesProgress?.Report(package.DownloadBytes);
            }
        }

        public void DeletePartial(ContentPackageDescriptor package)
        {
            Paths paths = ResolvePaths(package);
            DeleteFileIfExists(paths.Partial);
            DeleteFileIfExists(paths.Archive);
            DeleteDirectoryIfEmpty(downloadRoot);
        }

        public string GetArchivePath(ContentPackageDescriptor package)
        {
            Paths paths = ResolvePaths(package);
            return File.Exists(paths.Archive) && new FileInfo(paths.Archive).Length == package.DownloadBytes
                ? paths.Archive
                : null;
        }

        private Paths ResolvePaths(ContentPackageDescriptor package)
        {
            if (package == null)
                throw new ArgumentNullException(nameof(package));
            if (!IsSafePackageId(package.PackageId))
                throw new ArgumentException("Package id contains unsupported path characters.", nameof(package));

            string basePath = Path.Combine(downloadRoot, package.PackageId);
            return new Paths(basePath + ".part", basePath + ".zip");
        }

        private static bool IsSafePackageId(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
                return false;
            foreach (char character in value)
            {
                if (!char.IsLetterOrDigit(character) && character != '.' && character != '-' && character != '_')
                    return false;
            }
            return true;
        }

        private static void DeleteFileIfExists(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }

        private static void DeleteDirectoryIfEmpty(string path)
        {
            if (Directory.Exists(path) && Directory.GetFileSystemEntries(path).Length == 0)
                Directory.Delete(path);
        }

        private sealed class Paths
        {
            public Paths(string partial, string archive)
            {
                Partial = partial;
                Archive = archive;
            }

            public string Partial { get; }
            public string Archive { get; }
        }
    }

    /// <summary>
    /// Development/sideload byte source. A package id maps to one ZIP under the
    /// configured source directory and supports the same offset contract as HTTP.
    /// </summary>
    public sealed class LocalFileContentPackageByteSource : IContentPackageByteSource
    {
        private const int BufferBytes = 81920;

        private readonly string sourceRoot;

        public LocalFileContentPackageByteSource(string sourceRoot)
        {
            if (string.IsNullOrWhiteSpace(sourceRoot))
                throw new ArgumentException("Source root cannot be empty.", nameof(sourceRoot));
            this.sourceRoot = Path.GetFullPath(sourceRoot);
        }

        public Task<Stream> OpenReadAsync(
            ContentPackageDescriptor package,
            long offset,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (package == null)
                throw new ArgumentNullException(nameof(package));
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (string.IsNullOrWhiteSpace(package.PackageId) ||
                package.PackageId.IndexOfAny(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }) >= 0)
                throw new ArgumentException("Package id contains unsupported path characters.", nameof(package));

            string sourcePath = Path.Combine(sourceRoot, package.PackageId + ".zip");
            var stream = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                BufferBytes,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            long sourceLength = stream.Length;
            if (offset > sourceLength)
            {
                stream.Dispose();
                throw new IOException($"Resume offset {offset} is beyond source length {sourceLength}.");
            }
            stream.Position = offset;
            return Task.FromResult<Stream>(stream);
        }
    }
}
