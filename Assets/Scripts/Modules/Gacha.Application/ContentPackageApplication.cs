using System;

namespace Gacha.Application
{
    public enum ContentInstallAction
    {
        None,
        Install,
        Update,
        Repair
    }

    public enum ContentInstallPlanStatus
    {
        Ready,
        AlreadyCurrent,
        InsufficientSpace,
        InvalidPackage,
        StorageUnavailable
    }

    public sealed class ContentPackageDescriptor
    {
        public ContentPackageDescriptor(
            string packageId,
            long revision,
            string version,
            long downloadBytes,
            long installedBytes,
            string sha256)
        {
            PackageId = packageId?.Trim();
            Revision = revision;
            Version = version?.Trim();
            DownloadBytes = downloadBytes;
            InstalledBytes = installedBytes;
            Sha256 = sha256?.Trim().ToLowerInvariant();
        }

        public string PackageId { get; }
        public long Revision { get; }
        public string Version { get; }
        public long DownloadBytes { get; }
        public long InstalledBytes { get; }
        public string Sha256 { get; }
    }

    public sealed class InstalledContentPackage
    {
        public InstalledContentPackage(
            string packageId,
            long revision,
            string version,
            long installedBytes,
            string sha256)
        {
            PackageId = packageId?.Trim();
            Revision = revision;
            Version = version?.Trim();
            InstalledBytes = installedBytes;
            Sha256 = sha256?.Trim().ToLowerInvariant();
        }

        public string PackageId { get; }
        public long Revision { get; }
        public string Version { get; }
        public long InstalledBytes { get; }
        public string Sha256 { get; }
    }

    public interface IInstalledContentPackageRegistry
    {
        InstalledContentPackage Find(string packageId);
    }

    public interface IContentStorageProbe
    {
        long GetAvailableBytes();
    }

    public sealed class ContentInstallPlan
    {
        internal ContentInstallPlan(
            ContentInstallPlanStatus status,
            ContentInstallAction action,
            ContentPackageDescriptor package,
            InstalledContentPackage installedPackage,
            long requiredBytes,
            long availableBytes,
            string errorMessage)
        {
            Status = status;
            Action = action;
            Package = package;
            InstalledPackage = installedPackage;
            RequiredBytes = requiredBytes;
            AvailableBytes = availableBytes;
            ErrorMessage = errorMessage;
        }

        public ContentInstallPlanStatus Status { get; }
        public ContentInstallAction Action { get; }
        public ContentPackageDescriptor Package { get; }
        public InstalledContentPackage InstalledPackage { get; }
        public long RequiredBytes { get; }
        public long AvailableBytes { get; }
        public string ErrorMessage { get; }
        public bool CanStart => Status == ContentInstallPlanStatus.Ready;
    }

    /// <summary>
    /// Produces a side-effect-free install decision before a package is downloaded.
    /// Required space includes the archive, a fully extracted staging copy and a
    /// reserve so the currently installed package can stay untouched until commit.
    /// </summary>
    public sealed class ContentPackagePlanner
    {
        public const long DefaultSafetyReserveBytes = 32L * 1024L * 1024L;

        private readonly IInstalledContentPackageRegistry registry;
        private readonly IContentStorageProbe storage;
        private readonly long safetyReserveBytes;

        public ContentPackagePlanner(
            IInstalledContentPackageRegistry registry,
            IContentStorageProbe storage,
            long safetyReserveBytes = DefaultSafetyReserveBytes)
        {
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
            this.storage = storage ?? throw new ArgumentNullException(nameof(storage));
            if (safetyReserveBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(safetyReserveBytes));
            this.safetyReserveBytes = safetyReserveBytes;
        }

        public ContentInstallPlan Plan(ContentPackageDescriptor package)
        {
            string validationError = Validate(package);
            if (validationError != null)
                return Result(ContentInstallPlanStatus.InvalidPackage, ContentInstallAction.None, package, null, 0, -1, validationError);

            InstalledContentPackage installed;
            try
            {
                installed = registry.Find(package.PackageId);
            }
            catch (Exception exception)
            {
                return Result(
                    ContentInstallPlanStatus.StorageUnavailable,
                    ContentInstallAction.None,
                    package,
                    null,
                    0,
                    -1,
                    "Installed package state could not be read: " + exception.Message);
            }

            if (installed != null && !string.Equals(installed.PackageId, package.PackageId, StringComparison.Ordinal))
            {
                return Result(
                    ContentInstallPlanStatus.StorageUnavailable,
                    ContentInstallAction.None,
                    package,
                    installed,
                    0,
                    -1,
                    "The installed package registry returned a mismatched package id.");
            }

            ContentInstallAction action = ResolveAction(package, installed);
            if (action == ContentInstallAction.None)
            {
                return Result(
                    ContentInstallPlanStatus.AlreadyCurrent,
                    action,
                    package,
                    installed,
                    0,
                    -1,
                    null);
            }

            long requiredBytes = SaturatingAdd(package.DownloadBytes, package.InstalledBytes, safetyReserveBytes);
            long availableBytes;
            try
            {
                availableBytes = storage.GetAvailableBytes();
                if (availableBytes < 0)
                    throw new InvalidOperationException("Available storage cannot be negative.");
            }
            catch (Exception exception)
            {
                return Result(
                    ContentInstallPlanStatus.StorageUnavailable,
                    action,
                    package,
                    installed,
                    requiredBytes,
                    -1,
                    "Available storage could not be read: " + exception.Message);
            }

            ContentInstallPlanStatus status = availableBytes >= requiredBytes
                ? ContentInstallPlanStatus.Ready
                : ContentInstallPlanStatus.InsufficientSpace;
            return Result(status, action, package, installed, requiredBytes, availableBytes, null);
        }

        private static ContentInstallAction ResolveAction(
            ContentPackageDescriptor package,
            InstalledContentPackage installed)
        {
            if (installed == null)
                return ContentInstallAction.Install;

            if (installed.Revision > package.Revision && ValidateInstalled(installed) == null)
                return ContentInstallAction.None;
            if (installed.Revision < package.Revision)
                return ContentInstallAction.Update;

            return ValidateInstalled(installed) == null &&
                   string.Equals(installed.Sha256, package.Sha256, StringComparison.OrdinalIgnoreCase)
                ? ContentInstallAction.None
                : ContentInstallAction.Repair;
        }

        private static string Validate(ContentPackageDescriptor package)
        {
            if (package == null)
                return "Package metadata is missing.";
            if (!IsSafePackageId(package.PackageId))
                return "Package id must contain only letters, digits, period, dash or underscore.";
            if (package.Revision <= 0)
                return "Package revision must be greater than zero.";
            if (string.IsNullOrWhiteSpace(package.Version))
                return "Package version is missing.";
            if (package.DownloadBytes <= 0)
                return "Package download size must be greater than zero.";
            if (package.InstalledBytes <= 0)
                return "Package installed size must be greater than zero.";
            if (!IsSha256(package.Sha256))
                return "Package SHA-256 must contain exactly 64 hexadecimal characters.";
            return null;
        }

        private static string ValidateInstalled(InstalledContentPackage package)
        {
            if (package == null)
                return "Installed package is missing.";
            if (!IsSafePackageId(package.PackageId) || package.Revision <= 0 || package.InstalledBytes <= 0)
                return "Installed package metadata is invalid.";
            if (string.IsNullOrWhiteSpace(package.Version) || !IsSha256(package.Sha256))
                return "Installed package version or SHA-256 is invalid.";
            return null;
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

        private static bool IsSha256(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 64)
                return false;

            foreach (char character in value)
            {
                bool isHex = character >= '0' && character <= '9' ||
                             character >= 'a' && character <= 'f' ||
                             character >= 'A' && character <= 'F';
                if (!isHex)
                    return false;
            }
            return true;
        }

        private static long SaturatingAdd(long first, long second, long third)
        {
            if (long.MaxValue - first < second)
                return long.MaxValue;
            long sum = first + second;
            return long.MaxValue - sum < third ? long.MaxValue : sum + third;
        }

        private static ContentInstallPlan Result(
            ContentInstallPlanStatus status,
            ContentInstallAction action,
            ContentPackageDescriptor package,
            InstalledContentPackage installed,
            long requiredBytes,
            long availableBytes,
            string errorMessage)
        {
            return new ContentInstallPlan(
                status,
                action,
                package,
                installed,
                requiredBytes,
                availableBytes,
                errorMessage);
        }
    }
}
