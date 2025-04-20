using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "TCG/Card")]
public class Card : ScriptableObject
{
    [Header("Identity")] public string Id; 
    public string LocalizationKeyName; 
    public string LocalizationKeyDescription; 
    public CardType Type; 
    public Rarity Rarity;
    public List<string> PackIds = new List<string>();

    [Header("Artwork")]
    public Sprite Artwork;

    [Header("Stats (Optional)")]
    public int Attack;
    public int Health;
    public int Damage;
    public int Heal;
    public int Armor;
    public int ManaCost;

}

public enum CardType { Character, Spell, Equipment, Other }
public enum Rarity { C, R, SR, UR }