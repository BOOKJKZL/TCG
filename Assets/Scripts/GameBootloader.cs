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

        // ✅ 初始化 Unity Services
        await UnityServices.InitializeAsync();

        // ✅ 匿名登录
        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();

        Debug.Log("Unity Services Initialized and Signed In");

        // ✅ 安全加载 Cloud Save
        CloudSaveServiceWrapper.LoadInventory((cards, gold) =>
        {
            Inventory.Instance.Data.Cards = cards;
            Inventory.Instance.Data.Gold = gold;
        });

        // ✅ 加载本地备份（可选）
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