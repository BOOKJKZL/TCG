using System;

namespace Gacha.Application
{
    public enum ContentNetworkType
    {
        Offline,
        WifiOrEthernet,
        MobileData,
        Unknown
    }

    public enum ContentDownloadPreflightStatus
    {
        NoSelection,
        AlreadyCurrent,
        Ready,
        Offline,
        WaitingForWifi,
        CellularConfirmationRequired,
        InsufficientSpace,
        StorageUnavailable,
        NetworkUnavailable
    }

    public sealed class ContentDownloadPreferences
    {
        public ContentDownloadPreferences(bool wifiOnlyForLargeDownloads)
        {
            WifiOnlyForLargeDownloads = wifiOnlyForLargeDownloads;
        }

        public bool WifiOnlyForLargeDownloads { get; }
    }

    public interface IContentDownloadPreferenceStore
    {
        ContentDownloadPreferences Load();
        void Save(ContentDownloadPreferences preferences);
    }

    public interface IContentNetworkProbe
    {
        ContentNetworkType GetNetworkType();
    }

    public sealed class ContentDownloadPreflightResult
    {
        internal ContentDownloadPreflightResult(
            ContentDownloadPreflightStatus status,
            ContentNetworkType networkType,
            long downloadBytes,
            long requiredBytes,
            long availableBytes,
            string errorMessage)
        {
            Status = status;
            NetworkType = networkType;
            DownloadBytes = downloadBytes;
            RequiredBytes = requiredBytes;
            AvailableBytes = availableBytes;
            ErrorMessage = errorMessage;
        }

        public ContentDownloadPreflightStatus Status { get; }
        public ContentNetworkType NetworkType { get; }
        public long DownloadBytes { get; }
        public long RequiredBytes { get; }
        public long AvailableBytes { get; }
        public string ErrorMessage { get; }
        public bool CanStart => Status == ContentDownloadPreflightStatus.Ready;
    }

    /// <summary>
    /// Evaluates a whole selection before any coordinator starts. The storage
    /// estimate is deliberately conservative: all pending archives, all pending
    /// installed bytes and one rollback reserve must fit on the content volume.
    /// </summary>
    public sealed class ContentDownloadPolicyService
    {
        public const long DefaultLargeDownloadThresholdBytes = 100L * 1024L * 1024L;

        private readonly IContentStorageProbe storage;
        private readonly IContentNetworkProbe network;
        private readonly IContentDownloadPreferenceStore store;
        private readonly long largeDownloadThresholdBytes;
        private readonly long safetyReserveBytes;
        private ContentDownloadPreferences current;

        public ContentDownloadPolicyService(
            IContentStorageProbe storage,
            IContentNetworkProbe network,
            IContentDownloadPreferenceStore store,
            long largeDownloadThresholdBytes = DefaultLargeDownloadThresholdBytes,
            long safetyReserveBytes = ContentPackagePlanner.DefaultSafetyReserveBytes)
        {
            this.storage = storage ?? throw new ArgumentNullException(nameof(storage));
            this.network = network ?? throw new ArgumentNullException(nameof(network));
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            if (largeDownloadThresholdBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(largeDownloadThresholdBytes));
            if (safetyReserveBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(safetyReserveBytes));
            this.largeDownloadThresholdBytes = largeDownloadThresholdBytes;
            this.safetyReserveBytes = safetyReserveBytes;
            current = LoadPreferences();
        }

        public event Action<ContentDownloadPreferences> Changed;

        public ContentDownloadPreferences Current => current;
        public long LargeDownloadThresholdBytes => largeDownloadThresholdBytes;

        public void SetWifiOnlyForLargeDownloads(bool value)
        {
            if (current.WifiOnlyForLargeDownloads == value)
                return;
            var next = new ContentDownloadPreferences(value);
            store.Save(next);
            current = next;
            Changed?.Invoke(current);
        }

        public ContentNetworkType GetNetworkType()
        {
            try
            {
                return network.GetNetworkType();
            }
            catch
            {
                return ContentNetworkType.Unknown;
            }
        }

        public ContentDownloadPreflightResult Evaluate(
            ContentPackageSelectionSummary selection,
            bool cellularConfirmed = false)
        {
            if (selection == null || selection.SelectedCount <= 0)
            {
                return Result(
                    ContentDownloadPreflightStatus.NoSelection,
                    ContentNetworkType.Unknown,
                    0,
                    0,
                    -1);
            }
            if (selection.DownloadBytes <= 0)
            {
                return Result(
                    ContentDownloadPreflightStatus.AlreadyCurrent,
                    GetNetworkType(),
                    0,
                    0,
                    -1);
            }

            long requiredBytes = SaturatingAdd(
                selection.DownloadBytes,
                selection.InstalledBytes,
                safetyReserveBytes);
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
                    ContentDownloadPreflightStatus.StorageUnavailable,
                    ContentNetworkType.Unknown,
                    selection.DownloadBytes,
                    requiredBytes,
                    -1,
                    exception.Message);
            }

            ContentNetworkType networkType = GetNetworkType();
            if (availableBytes < requiredBytes)
            {
                return Result(
                    ContentDownloadPreflightStatus.InsufficientSpace,
                    networkType,
                    selection.DownloadBytes,
                    requiredBytes,
                    availableBytes);
            }
            if (networkType == ContentNetworkType.Offline)
            {
                return Result(
                    ContentDownloadPreflightStatus.Offline,
                    networkType,
                    selection.DownloadBytes,
                    requiredBytes,
                    availableBytes);
            }
            if (networkType == ContentNetworkType.Unknown)
            {
                return Result(
                    ContentDownloadPreflightStatus.NetworkUnavailable,
                    networkType,
                    selection.DownloadBytes,
                    requiredBytes,
                    availableBytes);
            }
            if (networkType == ContentNetworkType.MobileData)
            {
                if (current.WifiOnlyForLargeDownloads &&
                    selection.DownloadBytes >= largeDownloadThresholdBytes)
                {
                    return Result(
                        ContentDownloadPreflightStatus.WaitingForWifi,
                        networkType,
                        selection.DownloadBytes,
                        requiredBytes,
                        availableBytes);
                }
                if (!cellularConfirmed)
                {
                    return Result(
                        ContentDownloadPreflightStatus.CellularConfirmationRequired,
                        networkType,
                        selection.DownloadBytes,
                        requiredBytes,
                        availableBytes);
                }
            }

            return Result(
                ContentDownloadPreflightStatus.Ready,
                networkType,
                selection.DownloadBytes,
                requiredBytes,
                availableBytes);
        }

        private ContentDownloadPreferences LoadPreferences()
        {
            try
            {
                return store.Load() ?? new ContentDownloadPreferences(true);
            }
            catch
            {
                return new ContentDownloadPreferences(true);
            }
        }

        private static ContentDownloadPreflightResult Result(
            ContentDownloadPreflightStatus status,
            ContentNetworkType networkType,
            long downloadBytes,
            long requiredBytes,
            long availableBytes,
            string errorMessage = null) =>
            new ContentDownloadPreflightResult(
                status,
                networkType,
                downloadBytes,
                requiredBytes,
                availableBytes,
                errorMessage);

        private static long SaturatingAdd(params long[] values)
        {
            long result = 0;
            foreach (long value in values)
            {
                if (value < 0)
                    return long.MaxValue;
                if (long.MaxValue - result < value)
                    return long.MaxValue;
                result += value;
            }
            return result;
        }
    }
}
