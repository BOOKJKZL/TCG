using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.CloudSave;
using Unity.Services.Core;
using UnityEngine;

/// <summary>
/// Unity Gaming Services adapter. The rest of the game only exchanges an
/// InventoryData object and does not depend on Cloud Save response types.
/// </summary>
public static class CloudSaveServiceWrapper
{
    private const string SnapshotKey = "inventory_v4";
    private const string PreviousSnapshotKey = "inventory_v3";
    private const string OlderSnapshotKey = "inventory_v2";
    private const string LegacyInventoryKey = "inventory";
    private const string LegacyGoldKey = "gold";

    public static bool IsReady =>
        UnityServices.State == ServicesInitializationState.Initialized &&
        AuthenticationService.Instance.IsSignedIn;

    public static async Task<bool> InitializeAsync()
    {
        try
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
                await UnityServices.InitializeAsync();

            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();

            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Unity Services is unavailable; continuing offline. {exception.Message}");
            return false;
        }
    }

    public static async Task<bool> SaveInventoryAsync(InventoryData inventory)
    {
        if (!IsReady || inventory == null)
            return false;

        try
        {
            string json = JsonUtility.ToJson(inventory.ToSnapshot());
            var data = new Dictionary<string, object> { { SnapshotKey, json } };
            await CloudSaveService.Instance.Data.Player.SaveAsync(data);
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Cloud save failed; the local save is still available. {exception.Message}");
            return false;
        }
    }

    public static async Task<CloudInventoryLoadResult> LoadInventoryAsync()
    {
        if (!IsReady)
            return CloudInventoryLoadResult.Failed();

        try
        {
            var keys = new HashSet<string>
            {
                SnapshotKey,
                PreviousSnapshotKey,
                OlderSnapshotKey,
                LegacyInventoryKey,
                LegacyGoldKey
            };
            var results = await CloudSaveService.Instance.Data.Player.LoadAsync(keys);

            if (results.TryGetValue(SnapshotKey, out var snapshotItem))
            {
                string json = snapshotItem.Value.GetAs<string>();
                InventorySnapshot snapshot = JsonUtility.FromJson<InventorySnapshot>(json);
                return CloudInventoryLoadResult.Success(InventoryData.FromSnapshot(snapshot));
            }

            if (results.TryGetValue(PreviousSnapshotKey, out var previousSnapshotItem))
            {
                string json = previousSnapshotItem.Value.GetAs<string>();
                InventorySnapshot snapshot = JsonUtility.FromJson<InventorySnapshot>(json);
                return CloudInventoryLoadResult.Success(InventoryData.FromSnapshot(snapshot));
            }

            if (results.TryGetValue(OlderSnapshotKey, out var olderSnapshotItem))
            {
                string json = olderSnapshotItem.Value.GetAs<string>();
                InventorySnapshot snapshot = JsonUtility.FromJson<InventorySnapshot>(json);
                return CloudInventoryLoadResult.Success(InventoryData.FromSnapshot(snapshot));
            }

            // One-time migration from the old two-key cloud format.
            bool hasLegacyData = results.ContainsKey(LegacyInventoryKey) || results.ContainsKey(LegacyGoldKey);
            if (!hasLegacyData)
                return CloudInventoryLoadResult.Empty();

            var legacy = new InventoryData();
            if (results.TryGetValue(LegacyInventoryKey, out var inventoryItem))
            {
                try
                {
                    legacy.Cards = inventoryItem.Value.GetAs<Dictionary<string, int>>() ??
                                   new Dictionary<string, int>();
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"Legacy cloud card data could not be migrated. {exception.Message}");
                }
            }
            if (results.TryGetValue(LegacyGoldKey, out var goldItem))
                legacy.Gold = goldItem.Value.GetAs<int>();
            return CloudInventoryLoadResult.Success(legacy);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Cloud load failed; continuing with the local save. {exception.Message}");
            return CloudInventoryLoadResult.Failed();
        }
    }
}

public sealed class CloudInventoryLoadResult
{
    public bool Succeeded { get; private set; }
    public bool Found { get; private set; }
    public InventoryData Data { get; private set; }

    private CloudInventoryLoadResult() { }

    public static CloudInventoryLoadResult Success(InventoryData data)
    {
        return new CloudInventoryLoadResult { Succeeded = true, Found = true, Data = data };
    }

    public static CloudInventoryLoadResult Empty()
    {
        return new CloudInventoryLoadResult { Succeeded = true, Found = false };
    }

    public static CloudInventoryLoadResult Failed()
    {
        return new CloudInventoryLoadResult { Succeeded = false, Found = false };
    }
}
