using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class CardDatabase : MonoBehaviour
{
    public static CardDatabase Instance { get; private set; }

    // PackId -> Rarity -> List<Card>
    private readonly Dictionary<string, Dictionary<Rarity, List<Card>>> _cardsByPack = new();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        LoadCards();
    }

    private void LoadCards()
    {
        Card[] allCards = Resources.LoadAll<Card>("Data/Cards");

        foreach (Card c in allCards)
        {
            foreach (string packId in c.PackIds)
            {
                if (!_cardsByPack.ContainsKey(packId))
                    _cardsByPack[packId] = new();

                if (!_cardsByPack[packId].ContainsKey(c.Rarity))
                    _cardsByPack[packId][c.Rarity] = new();

                _cardsByPack[packId][c.Rarity].Add(c);
            }
        }
    }


    public Card GetRandomCard(Rarity rarity, string packId)
    {
        if (!_cardsByPack.ContainsKey(packId)) return null;
        if (!_cardsByPack[packId].ContainsKey(rarity)) return null;

        var list = _cardsByPack[packId][rarity];
        if (list.Count == 0) return null;

        return list[Random.Range(0, list.Count)];
    }

    public List<Card> GetAllCards()
    {
        return _cardsByPack.Values
            .SelectMany(dict => dict.Values)
            .SelectMany(list => list)
            .Distinct()
            .ToList();
    }

}