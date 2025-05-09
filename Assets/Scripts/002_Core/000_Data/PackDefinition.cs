using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "TCG/Pack Definition")]
public class PackDefinition : ScriptableObject
{
    public string PackId;
    public string DisplayName;
    [Range(1, 20)] 
    public int CardsPerPack = 5;
    [Tooltip("Probability 0..1 per rarity in order C,R,SR,UR, must sum to 1")] 
    public List<float> RarityRates = new List<float> { 0.75f, 0.2f, 0.04f, 0.01f };
    [Tooltip("Guarantee at least SR after opening N packs")] 
    public int PityN = 10;
}