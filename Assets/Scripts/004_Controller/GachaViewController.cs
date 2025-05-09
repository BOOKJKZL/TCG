using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;

public class GachaViewController : MonoBehaviour
{
    [SerializeField] private PackDefinition packDefinition;

    private GachaService _gacha;

    private void Start()
    {
        _gacha = new GachaService(CardDatabase.Instance, Inventory.Instance);
    }

    private void OnOpenPack()
    {
        List<Card> results = _gacha.DrawPack(packDefinition);
        // TODO: display results UI
        foreach (Card c in results)
            Debug.Log($"Obtained {c.Id}");
    }

    public void MenuBtnClick()
    {
        GameManager.Instance.loadManager.LoadScene(1);
    }
}