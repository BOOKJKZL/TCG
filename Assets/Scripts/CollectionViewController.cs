using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Localization.Settings;
using System.Linq;
using System.Collections.Generic;

public class CollectionViewController : MonoBehaviour
{
    [SerializeField] private UIDocument uiDocument; 
    [SerializeField] private VisualTreeAsset cardItemTemplate;

    private void Start()
    {
        var root = uiDocument.rootVisualElement;
        var list = root.Q<ListView>("collection_list");

        List<Card> allCards = CardDatabase.Instance.GetAllCards();
        list.itemsSource = allCards;
        list.makeItem = () => cardItemTemplate.Instantiate();
        list.bindItem = (e, i) =>
        {
            Card c = allCards[i];
            string locName = LocalizationSettings.StringDatabase.GetLocalizedString("Card_UI", $"card_name_{c.Id}");
            e.Q<Label>("card_name").text = locName;
            e.Q<Label>("card_rarity").text = c.Rarity.ToString();
            e.Q<VisualElement>("card_art").style.backgroundImage = new StyleBackground(c.Artwork);
        };
        list.selectionType = SelectionType.Single;
        list.onSelectionChange += OnSelectCard;
    }

    private void OnSelectCard(IEnumerable<object> objs)
    {
        Card c = objs.FirstOrDefault() as Card;
        if (c == null) return;
        // TODO: show details panel with flip animation
        Debug.Log($"Selected {c.Id}");
    }

}