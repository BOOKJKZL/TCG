using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Authentication;

public class GameBootloader : MonoBehaviour
{
    [SerializeField] private CardDatabase cardDatabasePrefab;
    [SerializeField] private Inventory inventoryPrefab;

    private async void Awake()
    {
        if (CardDatabase.Instance == null)
            Instantiate(cardDatabasePrefab);

        if (Inventory.Instance == null)
            Instantiate(inventoryPrefab);

        await UnityServices.InitializeAsync();

        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();

        Debug.Log("Unity Services Initialized and Signed In");

        CloudSaveServiceWrapper.LoadInventory((cards, gold) =>
        {
            Inventory.Instance.Data.Cards = cards;
            Inventory.Instance.Data.Gold = gold;
        });

        InventoryData local = LocalSaveService.Load();
        Inventory.Instance.Data = local;
    }

    private void OnApplicationPause(bool pause)
    {
        if (pause) Save();
    }

    private void OnApplicationQuit()
    {
        Save();
    }

    private void Save()
    {
        LocalSaveService.Save(Inventory.Instance.Data);
        CloudSaveServiceWrapper.SaveInventory(Inventory.Instance.Data.Cards, Inventory.Instance.Data.Gold);
    }

}