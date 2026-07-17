using System;
using System.IO;
using System.Linq;
using Gacha.Domain;
using Gacha.Infrastructure.Content;
using Gacha.Presentation;
using UnityEngine;

public class GachaViewController : MonoBehaviour
{
    [Header("Installed content")]
    [SerializeField] private string contentRootOverride;
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
            string contentRoot = ResolveContentRoot();
            var documents = new PrivateContentManifestReader().LoadDirectory(contentRoot);
            PrivateCatalogImportResult import = new PrivateManifestCatalogAdapter().Build(documents);
            catalog = import.Catalog;

            ProductDefinition product = string.IsNullOrWhiteSpace(productId)
                ? catalog.Products.Values.OrderBy(value => value.Id, StringComparer.Ordinal).First()
                : catalog.Products[productId];
            productId = product.Id;
            rules = SimulatedProductRuleFactory.CreateUniform(catalog, product.Id, cardsPerPack);
            Debug.Log($"Gacha content ready: {import.SourceSetCount} sets, {import.SourceCardCount} collectibles, {import.PrintingCount} printings.");
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

    private string ResolveContentRoot()
    {
        if (!string.IsNullOrWhiteSpace(contentRootOverride))
            return Path.GetFullPath(contentRootOverride);

#if UNITY_EDITOR
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
        return Path.Combine(projectRoot, "LocalContent", "Imports");
#else
        return Path.Combine(Application.persistentDataPath, "Content");
#endif
    }
}
