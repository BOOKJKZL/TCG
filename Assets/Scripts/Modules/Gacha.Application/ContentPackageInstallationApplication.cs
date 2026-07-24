using System;
using System.Threading;
using System.Threading.Tasks;

namespace Gacha.Application
{
    public enum ContentPackageInstallStatus
    {
        Succeeded,
        InvalidPlan,
        ArchiveNotFound,
        IntegrityMismatch,
        InvalidArchive,
        Cancelled,
        Failed,
        RollbackFailed
    }

    public sealed class ContentPackageInstallResult
    {
        private ContentPackageInstallResult(
            ContentPackageInstallStatus status,
            InstalledContentPackage installedPackage,
            string errorMessage)
        {
            Status = status;
            InstalledPackage = installedPackage;
            ErrorMessage = errorMessage;
        }

        public ContentPackageInstallStatus Status { get; }
        public InstalledContentPackage InstalledPackage { get; }
        public string ErrorMessage { get; }
        public bool Succeeded => Status == ContentPackageInstallStatus.Succeeded && InstalledPackage != null;

        public static ContentPackageInstallResult Success(InstalledContentPackage installedPackage)
        {
            if (installedPackage == null)
                throw new ArgumentNullException(nameof(installedPackage));
            return new ContentPackageInstallResult(ContentPackageInstallStatus.Succeeded, installedPackage, null);
        }

        public static ContentPackageInstallResult Failure(
            ContentPackageInstallStatus status,
            string errorMessage)
        {
            if (status == ContentPackageInstallStatus.Succeeded)
                throw new ArgumentException("Use Success for a successful install result.", nameof(status));

            return new ContentPackageInstallResult(
                status,
                null,
                string.IsNullOrWhiteSpace(errorMessage) ? "Content package installation failed." : errorMessage.Trim());
        }
    }

    public interface IContentPackageInstaller
    {
        Task<ContentPackageInstallResult> InstallAsync(
            ContentInstallPlan plan,
            string archivePath,
            CancellationToken cancellationToken = default);
    }
}
