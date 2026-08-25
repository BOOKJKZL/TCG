using System;
using System.Threading.Tasks;
using UnityEngine;

public class GameBootloader : MonoBehaviour
{
    [SerializeField] private Inventory inventoryPrefab;

    private bool _cloudReady;

    public static bool IsReady { get; private set; }
    public static event Action Ready;

    private async void Awake()
    {
        GameCloudConflictSession.Current.Changed += OnCloudConflictChanged;
        EnsureCoreObjectsExist();

        // Local data is available immediately and is always the offline fallback.
        LocalSaveLoadResult localLoad = LocalSaveService.LoadDetailed();
        InventoryData local = localLoad.Data;
        Inventory.Instance.ReplaceData(local);

        _cloudReady = await CloudSaveServiceWrapper.InitializeAsync();
        if (_cloudReady)
        {
            CloudInventoryLoadResult cloudLoad = await CloudSaveServiceWrapper.LoadInventoryAsync();
            if (cloudLoad.Succeeded)
            {
                InventoryConflictPreparation preparation =
                    GameCloudConflictSession.Current.Prepare(local, cloudLoad.Data, cloudLoad.Found);
                Inventory.Instance.ReplaceData(preparation.Selected);
                if (preparation.RequiresChoice)
                {
                    // Keep the verified local save available for play, but do not
                    // overwrite either side until the player reviews the conflict.
                    _cloudReady = false;
                    LocalSaveService.Save(Inventory.Instance.Data);
                }
                else
                {
                    // Persist migrations and automatic no-conflict decisions.
                    await SaveAsync();
                }
            }
            else
            {
                // Never overwrite remote data after a failed read. Cloud sync can
                // be retried safely on the next application session.
                _cloudReady = false;
            }
        }

        IsReady = true;
        Ready?.Invoke();
        Debug.Log(_cloudReady
            ? "Game data initialized with cloud synchronization."
            : "Game data initialized in offline mode.");
    }

    private void EnsureCoreObjectsExist()
    {
        if (Inventory.Instance == null)
            Instantiate(inventoryPrefab);
    }

    private void OnApplicationPause(bool pause)
    {
        if (pause)
            _ = SaveAsync();
    }

    private void OnDestroy()
    {
        GameCloudConflictSession.Current.Changed -= OnCloudConflictChanged;
    }

    private void OnCloudConflictChanged()
    {
        if (!GameCloudConflictSession.Current.HasPending)
            _cloudReady = CloudSaveServiceWrapper.IsReady;
    }

    private void OnApplicationQuit()
    {
        // Unity does not guarantee enough time for a network request while
        // quitting, so local save is synchronous and cloud save is best effort.
        SaveLocal();
        if (_cloudReady)
            _ = CloudSaveServiceWrapper.SaveInventoryAsync(Inventory.Instance.Data);
    }

    private async Task SaveAsync()
    {
        SaveLocal();
        if (_cloudReady)
            await CloudSaveServiceWrapper.SaveInventoryAsync(Inventory.Instance.Data);
    }

    private static void SaveLocal()
    {
        if (Inventory.Instance == null)
            return;

        Inventory.Instance.Data.Touch();
        LocalSaveService.Save(Inventory.Instance.Data);
    }
}
