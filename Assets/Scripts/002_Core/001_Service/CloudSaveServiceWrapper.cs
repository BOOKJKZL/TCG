using System.Collections.Generic;
using UnityEngine;
using Unity.Services.CloudSave;
using Unity.Services.Authentication;
using Unity.Services.Core;

public static class CloudSaveServiceWrapper
{
    private const string InvKey = "inventory"; 
    private const string GoldKey = "gold";

    public static async void InitUGS()
    {
        await UnityServices.InitializeAsync();
        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    public static async void SaveInventory(Dictionary<string, int> inv, int gold)
    {
        var data = new Dictionary<string, object>
    {
        { InvKey, inv },
        { GoldKey, gold }
    };
        await CloudSaveService.Instance.Data.Player.SaveAsync(data);
    }

    public static async void LoadInventory(System.Action<Dictionary<string, int>, int> onLoaded)
    {
        var keys = new HashSet<string> { InvKey, GoldKey };
        var results = await CloudSaveService.Instance.Data.Player.LoadAsync(keys);

        var inv = results.ContainsKey(InvKey)
            ? results[InvKey].Value.GetAs<Dictionary<string, int>>()
            : new Dictionary<string, int>();
        int gold = results.ContainsKey(GoldKey) ? results[GoldKey].Value.GetAs<int>() : 0;
        onLoaded?.Invoke(inv, gold);
    }


}