using UnityEngine;
using UnityEngine.UIElements;
using System.Linq;
using System.Collections.Generic;
using Gacha.Domain;

public class CollectionViewController : MonoBehaviour
{
    [SerializeField] private UIDocument uiDocument; 
    [SerializeField] private VisualTreeAsset cardItemTemplate;

    private void OnSelectPrinting(IEnumerable<object> objects)
    {
        PrintingDefinition printing = objects.OfType<PrintingDefinition>().FirstOrDefault();
        if (printing == null) return;
        // TODO: show details panel with flip animation
        Debug.Log($"Selected {printing.Id}");
    }

    public void MenuBtnClick()
    {
        GameManager.Instance.loadManager.LoadScene(1);
    }
}
