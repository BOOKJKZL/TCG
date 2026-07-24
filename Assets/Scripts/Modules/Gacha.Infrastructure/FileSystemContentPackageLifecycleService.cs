using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Gacha.Application;

namespace Gacha.Infrastructure.Content
{
    /// <summary>
    /// Removes only receipt-owned content. Live data and its receipt are first
    /// moved into a same-volume transaction so a partial commit can be rolled back.
    /// Player save files live outside contentRoot and are never addressable here.
    /// </summary>
    public class FileSystemContentPackageLifecycleService : IContentPackageLifecycleService
    {
        private readonly object gate = new object();
        private readonly string contentRoot;
        private readonly string contentPrefix;
        private readonly string workspaceRoot;
        private readonly FileSystemInstalledContentPackageRegistry registry;

        public FileSystemContentPackageLifecycleService(string contentRoot)
        {
            if (string.IsNullOrWhiteSpace(contentRoot))
                throw new ArgumentException("Content root cannot be empty.", nameof(contentRoot));
            this.contentRoot = Path.GetFullPath(contentRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            DirectoryInfo root = new DirectoryInfo(this.contentRoot);
            if (root.Parent == null)
                throw new ArgumentException("Content root must have a writable parent directory.", nameof(contentRoot));
            contentPrefix = this.contentRoot + Path.DirectorySeparatorChar;
            workspaceRoot = Path.Combine(root.Parent.FullName, "." + root.Name + "-removing");
            registry = new FileSystemInstalledContentPackageRegistry(this.contentRoot);
        }

        public InstalledContentPackage FindInstalled(string packageId)
        {
            lock (gate)
            {
                string receiptPath = ReceiptPath(packageId);
                if (File.Exists(receiptPath) && IsLink(receiptPath))
                    throw new InvalidDataException("Installed package receipt cannot be a file link.");
                return registry.Find(packageId);
            }
        }

        public Task<ContentPackageRemovalResult> RemoveAsync(
            string packageId,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                lock (gate)
                    return Remove(packageId, cancellationToken);
            });
        }

        private ContentPackageRemovalResult Remove(string packageId, CancellationToken cancellationToken)
        {
            string receiptPath;
            InstalledContentPackage installed;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                receiptPath = ReceiptPath(packageId);
                if (!File.Exists(receiptPath))
                    return ContentPackageRemovalResult.NotInstalled();
                if (IsLink(receiptPath))
                    return Failure("Installed package receipt cannot be a file link.");
                installed = registry.Find(packageId);
                string validation = ValidateInstalled(installed);
                if (validation != null)
                    return Failure(validation);
            }
            catch (OperationCanceledException)
            {
                return Cancelled();
            }
            catch (Exception exception)
            {
                return Failure("Installed package state could not be read: " + exception.Message);
            }

            string destinationPath;
            try
            {
                destinationPath = ResolveInsideContentRoot(installed.InstallRelativePath);
                EnsurePathHasNoLinks(destinationPath);
                if (File.Exists(destinationPath))
                    return Failure("Installed content destination is a file and was not removed.");
            }
            catch (Exception exception)
            {
                return Failure("Installed content path is unsafe: " + exception.Message);
            }

            string transactionRoot = Path.Combine(
                workspaceRoot,
                installed.PackageId + "-" + Guid.NewGuid().ToString("N"));
            string contentBackup = Path.Combine(transactionRoot, "content");
            string receiptBackup = Path.Combine(transactionRoot, "receipt.json");
            bool contentMoved = false;
            bool receiptMoved = false;
            bool preserveForRecovery = false;

            try
            {
                Directory.CreateDirectory(transactionRoot);
                cancellationToken.ThrowIfCancellationRequested();
                if (Directory.Exists(destinationPath))
                {
                    Directory.Move(destinationPath, contentBackup);
                    contentMoved = true;
                }

                BeforeReceiptCommit();
                File.Move(receiptPath, receiptBackup);
                receiptMoved = true;
            }
            catch (OperationCanceledException)
            {
                ContentPackageRemovalResult rollback = Rollback(
                    installed,
                    destinationPath,
                    receiptPath,
                    contentBackup,
                    receiptBackup,
                    contentMoved,
                    receiptMoved,
                    "Content package removal was cancelled.");
                preserveForRecovery = rollback.Status == ContentPackageRemovalStatus.RollbackFailed;
                return rollback.Status == ContentPackageRemovalStatus.Failed ? Cancelled() : rollback;
            }
            catch (Exception exception)
            {
                ContentPackageRemovalResult rollback = Rollback(
                    installed,
                    destinationPath,
                    receiptPath,
                    contentBackup,
                    receiptBackup,
                    contentMoved,
                    receiptMoved,
                    "Content package removal failed: " + exception.Message);
                preserveForRecovery = rollback.Status == ContentPackageRemovalStatus.RollbackFailed;
                return rollback;
            }
            finally
            {
                if (!preserveForRecovery && !receiptMoved)
                {
                    DeleteDirectoryBestEffort(transactionRoot);
                    TryDeleteEmptyDirectory(workspaceRoot);
                }
            }

            string warning = null;
            if (!TryDeleteDirectory(transactionRoot, out string cleanupError))
                warning = "Removed content cleanup is pending in '" + transactionRoot + "': " + cleanupError;
            TryDeleteEmptyAncestors(Path.GetDirectoryName(destinationPath));
            TryDeleteEmptyDirectory(Path.GetDirectoryName(receiptPath));
            TryDeleteEmptyDirectory(workspaceRoot);
            return ContentPackageRemovalResult.Removed(installed, warning);
        }

        /// <summary>
        /// Transaction seam for deterministic failure verification. Production
        /// implementations leave it empty; tests may fail immediately before commit.
        /// </summary>
        protected virtual void BeforeReceiptCommit()
        {
        }

        private ContentPackageRemovalResult Rollback(
            InstalledContentPackage installed,
            string destinationPath,
            string receiptPath,
            string contentBackup,
            string receiptBackup,
            bool contentMoved,
            bool receiptMoved,
            string failureMessage)
        {
            try
            {
                if (receiptMoved && File.Exists(receiptBackup))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(receiptPath));
                    File.Move(receiptBackup, receiptPath);
                }
                if (contentMoved && Directory.Exists(contentBackup))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                    Directory.Move(contentBackup, destinationPath);
                }
                return ContentPackageRemovalResult.Failure(
                    ContentPackageRemovalStatus.Failed,
                    failureMessage + " Previous content was restored.");
            }
            catch (Exception rollbackException)
            {
                return ContentPackageRemovalResult.Failure(
                    ContentPackageRemovalStatus.RollbackFailed,
                    failureMessage + " Rollback also failed: " + rollbackException.Message +
                    " | Recovery workspace: " + Path.GetDirectoryName(contentBackup) +
                    " | Package: " + installed.PackageId);
            }
        }

        private string ReceiptPath(string packageId)
        {
            if (!IsSafePackageId(packageId))
                throw new ArgumentException("Package id contains unsupported path characters.", nameof(packageId));
            string path = Path.GetFullPath(Path.Combine(
                contentRoot,
                FileSystemInstalledContentPackageRegistry.ReceiptDirectoryName,
                packageId + ".json"));
            if (!path.StartsWith(contentPrefix, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Package id escapes the receipt directory.", nameof(packageId));
            return path;
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

        private static string ValidateInstalled(InstalledContentPackage installed)
        {
            if (installed == null)
                return "Installed package receipt is empty.";
            var descriptor = new ContentPackageDescriptor(
                installed.PackageId,
                installed.InstallRelativePath,
                installed.Revision,
                installed.Version,
                1,
                installed.InstalledBytes,
                installed.Sha256);
            string error = ContentPackagePlanner.ValidateDescriptor(descriptor);
            return error == null ? null : "Installed package receipt is invalid: " + error;
        }

        private string ResolveInsideContentRoot(string relativePath)
        {
            string candidate = Path.GetFullPath(Path.Combine(
                contentRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!candidate.StartsWith(contentPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Path escapes the content root.");
            return candidate;
        }

        private void EnsurePathHasNoLinks(string destinationPath)
        {
            string relative = destinationPath.Substring(contentPrefix.Length);
            string current = contentRoot;
            foreach (string segment in relative.Split(Path.DirectorySeparatorChar))
            {
                current = Path.Combine(current, segment);
                if ((Directory.Exists(current) || File.Exists(current)) && IsLink(current))
                    throw new InvalidDataException("Installed content path cannot contain links: " + current);
            }
        }

        private void TryDeleteEmptyAncestors(string path)
        {
            string current = path;
            while (!string.IsNullOrEmpty(current) &&
                   current.StartsWith(contentPrefix, StringComparison.OrdinalIgnoreCase))
            {
                if (!TryDeleteEmptyDirectory(current))
                    return;
                current = Path.GetDirectoryName(current);
            }
        }

        private static bool IsLink(string path)
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }

        private static bool TryDeleteDirectory(string path, out string error)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private static void DeleteDirectoryBestEffort(string path)
        {
            TryDeleteDirectory(path, out _);
        }

        private static bool TryDeleteEmptyDirectory(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                    return true;
                if (Directory.GetFileSystemEntries(path).Length != 0)
                    return false;
                Directory.Delete(path);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static ContentPackageRemovalResult Failure(string message)
        {
            return ContentPackageRemovalResult.Failure(ContentPackageRemovalStatus.Failed, message);
        }

        private static ContentPackageRemovalResult Cancelled()
        {
            return ContentPackageRemovalResult.Failure(
                ContentPackageRemovalStatus.Cancelled,
                "Content package removal was cancelled.");
        }
    }
}
