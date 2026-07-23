using System;
using System.Linq;
using Gacha.Application;
using Gacha.Domain;
using Gacha.Presentation;
using UnityEngine;

public class GachaViewController : MonoBehaviour
{
    [Header("Installed content")]
    [SerializeField] private string productId;

    [Header("Temporary simulated product")]
    [SerializeField, Range(1, 20)] private int cardsPerPack = 5;

    private UniversalCatalog catalog;
    private ProductDrawRules rules;
    private readonly GachaEngine engine = new GachaEngine();

    public bool IsReady => catalog != null && rules != null;
    public event Action<ProductDrawResult> PackOpened;
    public event Action<string> InitializationFailed;

    private void Start()
    {
        TryInitialize();
    }

    public bool TryInitialize()
    {
        try
        {
            GameApplicationBootstrap.EnsureConfigured();
            CatalogLoadResult load = ApplicationServices.Catalog.EnsureLoaded();
            if (!load.Succeeded)
                throw new InvalidOperationException(load.ErrorMessage);

            catalog = load.Catalog;
            ApplicationServices.Languages.RefreshContentLanguage(catalog);

            ProductDefinition product = string.IsNullOrWhiteSpace(productId)
                ? catalog.Products.Values.OrderBy(value => value.Id, StringComparer.Ordinal).First()
                : catalog.Products[productId];
            productId = product.Id;
            rules = SimulatedProductRuleFactory.CreateUniform(catalog, product.Id, cardsPerPack);
            Debug.Log($"Gacha content ready: {load.SourceSetCount} sets, {load.SourceItemCount} collectibles, {load.PrintingCount} printings.");
            return true;
        }
        catch (Exception exception)
        {
            catalog = null;
            rules = null;
            string message = $"Gacha content could not be initialized: {exception.Message}";
            Debug.LogWarning(message);
            InitializationFailed?.Invoke(message);
            UIFeedbackService.Play(FeedbackCue.Error);
            return false;
        }
    }

    public void OnOpenPack()
    {
        if (!IsReady && !TryInitialize())
            return;
        if (Inventory.Instance == null)
        {
            string message = "Inventory is not initialized.";
            InitializationFailed?.Invoke(message);
            Debug.LogWarning(message);
            UIFeedbackService.Play(FeedbackCue.Error);
            return;
        }

        int productsOpened = Inventory.Instance.GetProductsOpened(rules.ProductId);
        ProductDrawResult result = engine.Draw(catalog, rules, productsOpened);
        foreach (DrawnPrinting printing in result.Printings)
            Inventory.Instance.AddPrinting(printing.PrintingId);
        Inventory.Instance.IncrementProductCounter(result.ProductId);

        UIFeedbackService.Play(FeedbackCue.PackOpen, true);
        PackOpened?.Invoke(result);
        Debug.Log($"Opened '{result.ProductId}' and obtained {result.Printings.Count} printings.");
    }

    public void MenuBtnClick()
    {
        GameManager.Instance.loadManager.LoadScene(1);
    }

}
