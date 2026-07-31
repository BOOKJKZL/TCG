using System;
using Gacha.Application;
using UnityEngine;

namespace Gacha.Infrastructure.Content
{
    public sealed class UnityContentNetworkProbe : IContentNetworkProbe
    {
        public ContentNetworkType GetNetworkType()
        {
            switch (UnityEngine.Application.internetReachability)
            {
                case NetworkReachability.NotReachable:
                    return ContentNetworkType.Offline;
                case NetworkReachability.ReachableViaLocalAreaNetwork:
                    return ContentNetworkType.WifiOrEthernet;
                case NetworkReachability.ReachableViaCarrierDataNetwork:
                    return ContentNetworkType.MobileData;
                default:
                    return ContentNetworkType.Unknown;
            }
        }
    }

    public sealed class PlayerPrefsContentDownloadPreferenceStore : IContentDownloadPreferenceStore
    {
        public const string DefaultWifiOnlyKey = "settings.downloads.wifi-only-large";

        private readonly string wifiOnlyKey;

        public PlayerPrefsContentDownloadPreferenceStore(string wifiOnlyKey = DefaultWifiOnlyKey)
        {
            if (string.IsNullOrWhiteSpace(wifiOnlyKey))
                throw new ArgumentException("Preference key cannot be empty.", nameof(wifiOnlyKey));
            this.wifiOnlyKey = wifiOnlyKey.Trim();
        }

        public ContentDownloadPreferences Load() =>
            new ContentDownloadPreferences(PlayerPrefs.GetInt(wifiOnlyKey, 1) != 0);

        public void Save(ContentDownloadPreferences preferences)
        {
            if (preferences == null)
                throw new ArgumentNullException(nameof(preferences));
            PlayerPrefs.SetInt(wifiOnlyKey, preferences.WifiOnlyForLargeDownloads ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}
