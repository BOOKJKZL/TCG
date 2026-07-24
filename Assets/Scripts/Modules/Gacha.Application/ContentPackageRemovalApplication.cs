using System;
using System.Threading;
using System.Threading.Tasks;

namespace Gacha.Application
{
    public enum ContentPackageRemovalStatus
    {
        Removed,
        NotInstalled,
        Cancelled,
        Failed,
        RollbackFailed
    }

    public sealed class ContentPackageRemovalResult
    {
        private ContentPackageRemovalResult(
            ContentPackageRemovalStatus status,
            InstalledContentPackage removedPackage,
            string errorMessage,
            string warningMessage)
        {
            Status = status;
            RemovedPackage = removedPackage;
            ErrorMessage = errorMessage;
            WarningMessage = warningMessage;
        }

        public ContentPackageRemovalStatus Status { get; }
        public InstalledContentPackage RemovedPackage { get; }
        public string ErrorMessage { get; }
        public string WarningMessage { get; }
        public bool Succeeded => Status == ContentPackageRemovalStatus.Removed ||
                                 Status == ContentPackageRemovalStatus.NotInstalled;

        public static ContentPackageRemovalResult Removed(
            InstalledContentPackage package,
            string warningMessage = null)
        {
            return new ContentPackageRemovalResult(
                ContentPackageRemovalStatus.Removed,
                package ?? throw new ArgumentNullException(nameof(package)),
                null,
                string.IsNullOrWhiteSpace(warningMessage) ? null : warningMessage.Trim());
        }

        public static ContentPackageRemovalResult NotInstalled()
        {
            return new ContentPackageRemovalResult(ContentPackageRemovalStatus.NotInstalled, null, null, null);
        }

        public static ContentPackageRemovalResult Failure(
            ContentPackageRemovalStatus status,
            string errorMessage)
        {
            if (status != ContentPackageRemovalStatus.Cancelled &&
                status != ContentPackageRemovalStatus.Failed &&
                status != ContentPackageRemovalStatus.RollbackFailed)
                throw new ArgumentOutOfRangeException(nameof(status));
            return new ContentPackageRemovalResult(
                status,
                null,
                string.IsNullOrWhiteSpace(errorMessage)
                    ? "Content package removal failed."
                    : errorMessage.Trim(),
                null);
        }
    }

    /// <summary>
    /// Owns installed package state and removal. Implementations must never
    /// mutate player inventory, settings, or other save data.
    /// </summary>
    public interface IContentPackageLifecycleService
    {
        InstalledContentPackage FindInstalled(string packageId);

        Task<ContentPackageRemovalResult> RemoveAsync(
            string packageId,
            CancellationToken cancellationToken = default);
    }
}
