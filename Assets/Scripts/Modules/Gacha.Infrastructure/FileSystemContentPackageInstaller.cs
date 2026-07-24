using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Gacha.Application;

namespace Gacha.Infrastructure.Content
{
    /// <summary>
    /// Installs a verified ZIP on the same volume as the content root. Extraction
    /// happens outside the live catalog; commit uses directory renames and rolls
    /// back the previous directory if receipt publication fails.
    /// </summary>
    public sealed class FileSystemContentPackageInstaller : IContentPackageInstaller
    {
        private const int CopyBufferBytes = 81920;

        private readonly string contentRoot;
        private readonly string workspaceRoot;

        public FileSystemContentPackageInstaller(string contentRoot)
        {
            if (string.IsNullOrWhiteSpace(contentRoot))
                throw new ArgumentException("Content root cannot be empty.", nameof(contentRoot));

            this.contentRoot = Path.GetFullPath(contentRoot);
            DirectoryInfo root = new DirectoryInfo(this.contentRoot);
            if (root.Parent == null)
                throw new ArgumentException("Content root must have a writable parent directory.", nameof(contentRoot));
            workspaceRoot = Path.Combine(root.Parent.FullName, "." + root.Name + "-installing");
        }

        public Task<ContentPackageInstallResult> InstallAsync(
            ContentInstallPlan plan,
            string archivePath,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() => Install(plan, archivePath, cancellationToken));
        }

        private ContentPackageInstallResult Install(
            ContentInstallPlan plan,
            string archivePath,
            CancellationToken cancellationToken)
        {
            if (plan == null || !plan.CanStart || plan.Package == null)
            {
                return Failure(
                    ContentPackageInstallStatus.InvalidPlan,
                    "Only a ready content install plan can be committed.");
            }
            if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
            {
                return Failure(
                    ContentPackageInstallStatus.ArchiveNotFound,
                    "The downloaded content archive was not found.");
            }

            ContentPackageDescriptor package = plan.Package;
            string transactionRoot = Path.Combine(
                workspaceRoot,
                package.PackageId + "-" + Guid.NewGuid().ToString("N"));
            string stagingPath = Path.Combine(transactionRoot, "staging");
            string rollbackPath = Path.Combine(transactionRoot, "rollback");
            string receiptTemporaryPath = Path.Combine(transactionRoot, "receipt.json");
            string receiptBackupPath = Path.Combine(transactionRoot, "receipt.backup.json");
            bool preserveTransactionForRecovery = false;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileInfo archive = new FileInfo(archivePath);
                if (archive.Length != package.DownloadBytes)
                {
                    return Failure(
                        ContentPackageInstallStatus.IntegrityMismatch,
                        $"Archive size was {archive.Length} bytes; expected {package.DownloadBytes} bytes.");
                }

                string archiveSha256 = ComputeSha256(archive.FullName, cancellationToken);
                if (!string.Equals(archiveSha256, package.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    return Failure(
                        ContentPackageInstallStatus.IntegrityMismatch,
                        "Archive SHA-256 did not match the package catalog.");
                }

                Directory.CreateDirectory(stagingPath);
                long extractedBytes = ExtractArchive(
                    archive.FullName,
                    stagingPath,
                    package.InstalledBytes,
                    cancellationToken);
                if (extractedBytes != package.InstalledBytes)
                {
                    return Failure(
                        ContentPackageInstallStatus.IntegrityMismatch,
                        $"Extracted content was {extractedBytes} bytes; expected {package.InstalledBytes} bytes.");
                }

                cancellationToken.ThrowIfCancellationRequested();
                var installed = new InstalledContentPackage(
                    package.PackageId,
                    package.InstallRelativePath,
                    package.Revision,
                    package.Version,
                    package.InstalledBytes,
                    package.Sha256);
                File.WriteAllText(
                    receiptTemporaryPath,
                    FileSystemInstalledContentPackageRegistry.SerializeReceipt(installed),
                    new UTF8Encoding(false));

                ContentPackageInstallResult result = Commit(
                    plan.Action,
                    installed,
                    stagingPath,
                    rollbackPath,
                    receiptTemporaryPath,
                    receiptBackupPath);
                preserveTransactionForRecovery = result.Status == ContentPackageInstallStatus.RollbackFailed;
                return result;
            }
            catch (OperationCanceledException)
            {
                return Failure(ContentPackageInstallStatus.Cancelled, "Content package installation was cancelled.");
            }
            catch (ContentPackageArchiveException exception)
            {
                return Failure(ContentPackageInstallStatus.InvalidArchive, exception.Message);
            }
            catch (InvalidDataException exception)
            {
                return Failure(ContentPackageInstallStatus.InvalidArchive, "Content archive is invalid: " + exception.Message);
            }
            catch (Exception exception)
            {
                return Failure(ContentPackageInstallStatus.Failed, "Content package installation failed: " + exception.Message);
            }
            finally
            {
                if (!preserveTransactionForRecovery)
                    DeleteDirectoryBestEffort(transactionRoot);
                DeleteDirectoryIfEmptyBestEffort(workspaceRoot);
            }
        }

        private ContentPackageInstallResult Commit(
            ContentInstallAction action,
            InstalledContentPackage installed,
            string stagingPath,
            string rollbackPath,
            string receiptTemporaryPath,
            string receiptBackupPath)
        {
            string destinationPath = ResolveInsideRoot(contentRoot, installed.InstallRelativePath);
            string destinationParent = Path.GetDirectoryName(destinationPath);
            string receiptDirectory = Path.Combine(
                contentRoot,
                FileSystemInstalledContentPackageRegistry.ReceiptDirectoryName);
            string receiptPath = Path.Combine(receiptDirectory, installed.PackageId + ".json");
            bool oldContentMoved = false;
            bool newContentMoved = false;
            bool oldReceiptMoved = false;

            try
            {
                Directory.CreateDirectory(destinationParent);
                Directory.CreateDirectory(receiptDirectory);
                if (File.Exists(destinationPath))
                    throw new IOException($"Content destination is a file: {destinationPath}");
                if (action == ContentInstallAction.Install &&
                    (Directory.Exists(destinationPath) || File.Exists(receiptPath)))
                    throw new IOException("A new package cannot replace unregistered content or an existing receipt.");

                if (Directory.Exists(destinationPath))
                {
                    Directory.Move(destinationPath, rollbackPath);
                    oldContentMoved = true;
                }

                Directory.Move(stagingPath, destinationPath);
                newContentMoved = true;

                if (File.Exists(receiptPath))
                {
                    File.Move(receiptPath, receiptBackupPath);
                    oldReceiptMoved = true;
                }

                File.Move(receiptTemporaryPath, receiptPath);
                DeleteDirectoryBestEffort(rollbackPath);
                DeleteFileBestEffort(receiptBackupPath);
                return ContentPackageInstallResult.Success(installed);
            }
            catch (Exception commitException)
            {
                try
                {
                    if (File.Exists(receiptPath))
                        File.Delete(receiptPath);
                    if (oldReceiptMoved && File.Exists(receiptBackupPath))
                        File.Move(receiptBackupPath, receiptPath);

                    if (newContentMoved && Directory.Exists(destinationPath))
                        Directory.Delete(destinationPath, true);
                    if (oldContentMoved && Directory.Exists(rollbackPath))
                        Directory.Move(rollbackPath, destinationPath);
                }
                catch (Exception rollbackException)
                {
                    return Failure(
                        ContentPackageInstallStatus.RollbackFailed,
                        "Content commit failed and rollback also failed: " +
                        commitException.Message + " | " + rollbackException.Message +
                        " | Recovery workspace: " + Path.GetDirectoryName(rollbackPath));
                }

                return Failure(
                    ContentPackageInstallStatus.Failed,
                    "Content commit failed; the previous package was restored: " + commitException.Message);
            }
        }

        private static long ExtractArchive(
            string archivePath,
            string stagingPath,
            long expectedBytes,
            CancellationToken cancellationToken)
        {
            long declaredBytes = 0;
            long extractedBytes = 0;
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (FileStream archiveStream = File.OpenRead(archivePath))
            using (var zip = new ZipArchive(archiveStream, ZipArchiveMode.Read, false))
            {
                if (zip.Entries.Count == 0)
                    throw new ContentPackageArchiveException("Content archive contains no entries.");

                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string relativePath = NormalizeArchivePath(entry.FullName);
                    bool isDirectory = entry.FullName.EndsWith("/", StringComparison.Ordinal) ||
                                       entry.FullName.EndsWith("\\", StringComparison.Ordinal);
                    if (!paths.Add(relativePath))
                        throw new ContentPackageArchiveException($"Archive contains a duplicate path: {relativePath}");

                    string destination = ResolveInsideRoot(stagingPath, relativePath);
                    if (isDirectory)
                    {
                        Directory.CreateDirectory(destination);
                        continue;
                    }

                    if (entry.Length < 0 || declaredBytes > expectedBytes - entry.Length)
                        throw new ContentPackageArchiveException("Archive expands beyond the declared installed size.");
                    declaredBytes += entry.Length;

                    string parent = Path.GetDirectoryName(destination);
                    Directory.CreateDirectory(parent);
                    using (Stream input = entry.Open())
                    using (FileStream output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        long copied = CopyEntry(
                            input,
                            output,
                            expectedBytes - extractedBytes,
                            cancellationToken);
                        if (copied != entry.Length)
                            throw new ContentPackageArchiveException($"Archive entry size is inconsistent: {relativePath}");
                        extractedBytes += copied;
                    }
                }
            }

            return extractedBytes;
        }

        private static long CopyEntry(
            Stream input,
            Stream output,
            long remainingPackageBytes,
            CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[CopyBufferBytes];
            long copied = 0;
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (copied > remainingPackageBytes - read)
                    throw new ContentPackageArchiveException("Archive entry exceeds the declared installed size.");
                output.Write(buffer, 0, read);
                copied += read;
            }
            return copied;
        }

        private static string NormalizeArchivePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ContentPackageArchiveException("Archive contains an empty path.");

            string normalized = value.Replace('\\', '/');
            if (normalized.StartsWith("/", StringComparison.Ordinal) ||
                normalized.Length >= 2 && char.IsLetter(normalized[0]) && normalized[1] == ':')
                throw new ContentPackageArchiveException($"Archive contains a rooted path: {value}");

            normalized = normalized.TrimEnd('/');
            string[] segments = normalized.Split('/');
            foreach (string segment in segments)
            {
                if (string.IsNullOrWhiteSpace(segment) || segment == "." || segment == "..")
                    throw new ContentPackageArchiveException($"Archive contains an unsafe path: {value}");
                if (segment.EndsWith(".", StringComparison.Ordinal) || segment.EndsWith(" ", StringComparison.Ordinal))
                    throw new ContentPackageArchiveException($"Archive path is not portable: {value}");
                foreach (char character in segment)
                {
                    if (character < 32 || character == '<' || character == '>' || character == ':' ||
                        character == '"' || character == '|' || character == '?' || character == '*')
                        throw new ContentPackageArchiveException($"Archive path is not portable: {value}");
                }
            }
            return string.Join("/", segments);
        }

        private static string ResolveInsideRoot(string root, string relativePath)
        {
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string candidate = Path.GetFullPath(Path.Combine(
                fullRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            string prefix = fullRoot + Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new ContentPackageArchiveException($"Path escapes its allowed root: {relativePath}");
            return candidate;
        }

        private static string ComputeSha256(string path, CancellationToken cancellationToken)
        {
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] buffer = new byte[CopyBufferBytes];
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    sha256.TransformBlock(buffer, 0, read, buffer, 0);
                }
                sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return BitConverter.ToString(sha256.Hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static ContentPackageInstallResult Failure(
            ContentPackageInstallStatus status,
            string errorMessage)
        {
            return ContentPackageInstallResult.Failure(status, errorMessage);
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
                // A later maintenance pass can remove an unused transaction directory.
            }
        }

        private static void DeleteDirectoryIfEmptyBestEffort(string path)
        {
            try
            {
                if (Directory.Exists(path) && Directory.GetFileSystemEntries(path).Length == 0)
                    Directory.Delete(path);
            }
            catch
            {
                // The directory is outside the live catalog and is safe to leave behind.
            }
        }

        private static void DeleteFileBestEffort(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // The backup is harmless after a successful receipt publication.
            }
        }

        private sealed class ContentPackageArchiveException : Exception
        {
            public ContentPackageArchiveException(string message) : base(message) { }
        }
    }
}
