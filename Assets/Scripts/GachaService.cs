using System.Collections.Generic;
using UnityEngine;

public class GachaService
{
    private readonly CardDatabase _db; private readonly Inventory _inv;

    public GachaService(CardDatabase db, Inventory inv)
    {
        _db = db;
        _inv = inv;
    }

    public List<Card> DrawPack(PackDefinition def)
    {
        var results = new List<Card>();
        for (int i = 0; i < def.CardsPerPack; i++)
        {
            Rarity rarity = RollRarity(def);
            Card card = _db.GetRandomCard(rarity, def.PackId);
            _inv.AddCard(card);
            results.Add(card);
        }

        _inv.IncrementPackCounter(def.PackId);
        return results;
    }

    private Rarity RollRarity(PackDefinition def)
    {
        int opened = _inv.GetPacksOpened(def.PackId);
        if (def.PityN > 0 && (opened + 1) % def.PityN == 0)
            return Rarity.SR;

        float rand = Random.value;
        float cumulative = 0f;
        for (int i = 0; i < def.RarityRates.Count; i++)
        {
            cumulative += def.RarityRates[i];
            if (rand <= cumulative)
                return (Rarity)i;
        }
        return Rarity.C;
    }

}