using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class InventoryData
{
    public Dictionary<string, int> Cards = new Dictionary<string, int>();
    public Dictionary<string, int> PacksOpened = new Dictionary<string, int>();
    public int Gold = 0;
}

public class Inventory : MonoBehaviour
{
    public static Inventory Instance { get; private set; }
    public InventoryData Data = new InventoryData();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void AddCard(Card card)
    {
        if (card == null) return;
        if (!Data.Cards.ContainsKey(card.Id))
            Data.Cards[card.Id] = 0;
        Data.Cards[card.Id]++;
    }

    public void IncrementPackCounter(string packId)
    {
        if (!Data.PacksOpened.ContainsKey(packId))
            Data.PacksOpened[packId] = 0;
        Data.PacksOpened[packId]++;
    }

    public int GetPacksOpened(string packId) =>
        Data.PacksOpened.ContainsKey(packId) ? Data.PacksOpened[packId] : 0;

}