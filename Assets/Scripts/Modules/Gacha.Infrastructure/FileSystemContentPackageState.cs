using System;
using System.IO;
using Gacha.Application;
using Newtonsoft.Json;

namespace Gacha.Infrastructure.Content
{
    public sealed class FileSystemInstalledContentPackageRegistry : IInstalledContentPackageRegistry
    {
        public const string ReceiptDirectoryName = ".packages";

        private readonly string contentRoot;

        public FileSystemInstalledContentPackageRegistry(string contentRoot)
        {
            if (string.IsNullOrWhiteSpace(contentRoot))
                throw new ArgumentException("Content root cannot be empty.", nameof(contentRoot));

            this.contentRoot = Path.GetFullPath(contentRoot);
        }

        public InstalledContentPackage Find(string packageId)
        {
            if (!IsSafePackageId(packageId))
                throw new ArgumentException("Package id contains unsupported path characters.", nameof(packageId));

            string receiptPath = Path.Combine(contentRoot, ReceiptDirectoryName, packageId + ".json");
            if (!File.Exists(receiptPath))
                return null;

            string json = File.ReadAllText(receiptPath);
            ContentPackageReceiptDto receipt = JsonConvert.DeserializeObject<ContentPackageReceiptDto>(json);
            if (receipt == null)
                throw new InvalidDataException($"Content package receipt is empty: {receiptPath}");
            if (!string.Equals(receipt.PackageId, packageId, StringComparison.Ordinal))
                throw new InvalidDataException($"Content package receipt id does not match '{packageId}'.");

            return new InstalledContentPackage(
                receipt.PackageId,
                receipt.InstallRelativePath,
                receipt.Revision,
                receipt.Version,
                receipt.InstalledBytes,
                receipt.Sha256);
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

        private sealed class ContentPackageReceiptDto
        {
            public string PackageId;
            public string InstallRelativePath;
            public long Revision;
            public string Version;
            public long InstalledBytes;
            public string Sha256;
        }
    }

    /// <summary>
    /// Reads free space from the volume that contains the content root without
    /// creating the root as a side effect. Android uses StatFs for the app volume.
    /// </summary>
    public sealed class FileSystemContentStorageProbe : IContentStorageProbe
    {
        private readonly string contentRoot;

        public FileSystemContentStorageProbe(string contentRoot)
        {
            if (string.IsNullOrWhiteSpace(contentRoot))
                throw new ArgumentException("Content root cannot be empty.", nameof(contentRoot));

            this.contentRoot = Path.GetFullPath(contentRoot);
        }

        public long GetAvailableBytes()
        {
            string existingPath = FindNearestExistingDirectory(contentRoot);
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var statFs = new UnityEngine.AndroidJavaObject("android.os.StatFs", existingPath))
                return statFs.Call<long>("getAvailableBytes");
#else
            string volumeRoot = Path.GetPathRoot(existingPath);
            if (string.IsNullOrWhiteSpace(volumeRoot))
                throw new IOException($"No storage volume could be resolved for: {contentRoot}");
            return new DriveInfo(volumeRoot).AvailableFreeSpace;
#endif
        }

        private static string FindNearestExistingDirectory(string path)
        {
            DirectoryInfo current = new DirectoryInfo(path);
            while (current != null && !current.Exists)
                current = current.Parent;

            if (current == null)
                throw new DirectoryNotFoundException($"No existing parent directory was found for: {path}");
            return current.FullName;
        }
    }
}
