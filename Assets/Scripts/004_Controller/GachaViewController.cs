using System;
using System.Collections.Generic;
using System.Linq;
using Gacha.Application;
using Gacha.Domain;
using Gacha.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public sealed class GachaViewController : MonoBehaviour
{
    private sealed class ProductRow
    {
        public AsyncCardImageView Image;
        public Label Name;
        public Label Metadata;
    }

    private sealed class RevealEntry
    {
        public PrintingDefinition Printing;
        public InventoryAward Award;
    }

    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private VisualTreeAsset viewAsset;
    [SerializeField] private string productId;
    [SerializeField, Range(1, 20)] private int cardsPerPack = 5;
    [SerializeField, Range(8, 64)] private int textureCacheCapacity = 24;

    private readonly List<ProductDefinition> products = new List<ProductDefinition>();
    private readonly List<RevealEntry> revealEntries = new List<RevealEntry>();
    private readonly HashSet<AsyncCardImageView> imageViews = new HashSet<AsyncCardImageView>();

    private UniversalCatalog catalog;
    private ProductOpeningService openingService;
    private ProductDefinition selectedProduct;
    private ProductRuleProfile selectedProfile;
    private ProductOddsSummary selectedOdds;
    private ProductOpeningOutcome currentOutcome;
    private CardTextureCache textureCache;
    private int revealIndex = -1;
    private bool packAnimating;
    private bool revealAnimating;

    private VisualElement root;
    private VisualElement selectionPage;
    private VisualElement openingPage;
    private VisualElement selectedArtSlot;
    private VisualElement oddsList;
    private VisualElement packStage;
    private VisualElement packShell;
    private VisualElement packTearLine;
    private VisualElement packArtSlot;
    private VisualElement revealStage;
    private VisualElement revealCard;
    private VisualElement revealArtSlot;
    private VisualElement summaryStage;
    private ListView productList;
    private ScrollView summaryList;
    private Label title;
    private Label subtitle;
    private Label status;
    private Label selectedName;
    private Label selectedMetadata;
    private Label ruleBadge;
    private Label ruleNotice;
    private Label oddsHeading;
    private Label packTitle;
    private Label packHint;
    private Label revealProgress;
    private Label revealName;
    private Label revealMetadata;
    private Label revealNewBadge;
    private Label summaryTitle;
    private Label summaryMetadata;
    private Button menuButton;
    private Button prepareButton;
    private Button ruleSourceButton;
    private Button tearButton;
    private Button revealButton;
    private Button revealAllButton;
    private Button backToProductsButton;
    private Button openAgainButton;
    private Button summaryProductsButton;

    private AsyncCardImageView selectedImage;
    private AsyncCardImageView packImage;
    private AsyncCardImageView revealImage;
    private IVisualElementScheduledItem packAnimation;
    private IVisualElementScheduledItem revealAnimation;

    public static IInventoryProgressStore InventoryStoreOverride { private get; set; }

    public bool IsReady { get; private set; }
    public string InitializationError { get; private set; }
    public int ProductCount => products.Count;
    public string SelectedProductId => selectedProduct?.Id;
    public ProductRuleTrust SelectedRuleTrust => selectedProfile?.Trust ?? ProductRuleTrust.Simulated;
    public string SelectedRuleProfileId => selectedProfile?.Id;
    public int LastOpenedCardCount => currentOutcome?.Draw.Printings.Count ?? 0;
    public int RevealedCount => revealIndex + 1;
    public int CachedTextureCount => textureCache?.Count ?? 0;
    public bool IsSummaryVisible => summaryStage != null && summaryStage.resolvedStyle.display == DisplayStyle.Flex;

    public event Action<ProductDrawResult> PackOpened;
    public event Action<string> InitializationFailed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        InventoryStoreOverride = null;
    }

    private void Awake()
    {
        EnsureDocumentAssets();
    }

    private void Start()
    {
        TryInitialize();
    }

    private void OnDestroy()
    {
        if (ApplicationServices.IsConfigured)
        {
            ApplicationServices.Languages.UiLanguageChanged -= OnUiLanguageChanged;
            ApplicationServices.Languages.ContentLanguageChanged -= OnContentLanguageChanged;
        }

        packAnimation?.Pause();
        revealAnimation?.Pause();
        foreach (AsyncCardImageView imageView in imageViews.ToArray())
            imageView.Dispose();
        imageViews.Clear();
        textureCache?.Dispose();
        textureCache = null;
    }

    public bool TryInitialize()
    {
        if (IsReady)
            return true;

        try
        {
            GameApplicationBootstrap.EnsureConfigured();
            EnsureDocumentAssets();
            if (uiDocument == null || uiDocument.panelSettings == null)
                throw new InvalidOperationException("The pack opening UI document is not configured.");

            root = uiDocument.rootVisualElement.Q<VisualElement>("gacha-opening");
            if (root == null)
                throw new InvalidOperationException("GachaView.uxml is not attached to the UIDocument.");

            QueryVisualElements();
            CatalogLoadResult load = ApplicationServices.Catalog.EnsureLoaded();
            if (!load.Succeeded)
                throw new InvalidOperationException(load.ErrorMessage);
            if (!ApplicationServices.HasContentImages)
                throw new InvalidOperationException("The installed content image service is unavailable.");

            catalog = load.Catalog;
            ApplicationServices.Languages.RefreshContentLanguage(catalog);
            textureCache = new CardTextureCache(ApplicationServices.Images, textureCacheCapacity);
            selectedImage = Track(new AsyncCardImageView(textureCache));
            packImage = Track(new AsyncCardImageView(textureCache));
            revealImage = Track(new AsyncCardImageView(textureCache));
            selectedImage.Element.AddToClassList("gacha-selected-art");
            packImage.Element.AddToClassList("gacha-pack-art");
            revealImage.Element.AddToClassList("gacha-reveal-art");
            selectedArtSlot.Add(selectedImage.Element);
            packArtSlot.Add(packImage.Element);
            revealArtSlot.Add(revealImage.Element);

            ConfigureProductList();
            ConfigureButtons();
            RebuildProducts();
            RefreshLocalizedChrome();
            ShowSelectionPage();

            ApplicationServices.Languages.UiLanguageChanged += OnUiLanguageChanged;
            ApplicationServices.Languages.ContentLanguageChanged += OnContentLanguageChanged;
            IsReady = true;
            InitializationError = null;
            Debug.Log($"Pack opening ready: {products.Count} installed products.");
            return true;
        }
        catch (Exception exception)
        {
            InitializationError = exception.Message;
            SetStatus(CardUiText.Format("gacha.status.unavailable", exception.Message), true);
            Debug.LogWarning($"Gacha content could not be initialized: {exception.Message}");
            InitializationFailed?.Invoke(exception.Message);
            UIFeedbackService.Play(FeedbackCue.Error);
            return false;
        }
    }

    public void OnOpenPack()
    {
        PrepareSelectedProduct();
    }

    public bool PrepareSelectedProduct()
    {
        if (!IsReady || selectedProduct == null || selectedProfile == null || packAnimating)
            return false;

        UIFeedbackService.Play(FeedbackCue.Confirm);
        SetStatus(string.Empty, false);
        currentOutcome = null;
        revealEntries.Clear();
        revealIndex = -1;
        selectionPage.style.display = DisplayStyle.None;
        openingPage.style.display = DisplayStyle.Flex;
        packStage.style.display = DisplayStyle.Flex;
        revealStage.style.display = DisplayStyle.None;
        summaryStage.style.display = DisplayStyle.None;
        packShell.style.opacity = 1f;
        packShell.style.scale = new Scale(Vector3.one);
        packTearLine.style.width = Length.Percent(0f);
        tearButton.SetEnabled(true);
        packTitle.text = DisplayName(selectedProduct);
        packHint.text = CardUiText.Get("gacha.pack.hint");
        PrintingDefinition cover = CoverFor(selectedProduct);
        if (cover != null)
            packImage.Bind(cover);
        else
            packImage.Unbind();
        return true;
    }

    public bool TearPack()
    {
        if (!IsReady || selectedProduct == null || packAnimating || currentOutcome != null)
            return false;

        try
        {
            currentOutcome = openingService.Open(selectedProduct.Id);
            BuildRevealEntries();
            PackOpened?.Invoke(currentOutcome.Draw);
            tearButton.SetEnabled(false);
            packAnimating = true;
            UIFeedbackService.Play(FeedbackCue.PackOpen, true);
            AnimatePackTear(BeginRevealStage);
            return true;
        }
        catch (Exception exception)
        {
            currentOutcome = null;
            SetStatus(CardUiText.Format("gacha.status.open_failed", exception.Message), true);
            Debug.LogWarning($"Pack opening failed: {exception.Message}");
            InitializationFailed?.Invoke(exception.Message);
            UIFeedbackService.Play(FeedbackCue.Error);
            return false;
        }
    }

    public bool RevealNextCard()
    {
        if (currentOutcome == null || packAnimating || revealAnimating)
            return false;
        if (revealIndex >= revealEntries.Count - 1)
        {
            ShowSummary();
            return true;
        }

        revealIndex++;
        RevealEntry entry = revealEntries[revealIndex];
        revealImage.Bind(entry.Printing);
        revealName.text = DisplayName(entry.Printing);
        revealMetadata.text = RevealMetadata(entry.Printing, entry.Award.CurrentCount);
        revealNewBadge.text = entry.Award.IsNew
            ? CardUiText.Get("common.badge.new")
            : CardUiText.Get("gacha.badge.owned");
        revealNewBadge.EnableInClassList("is-new", entry.Award.IsNew);
        revealProgress.text = CardUiText.Format(
            "gacha.reveal.progress",
            revealIndex + 1,
            revealEntries.Count);
        revealButton.text = revealIndex == revealEntries.Count - 1
            ? CardUiText.Get("gacha.action.view_results")
            : CardUiText.Get("gacha.action.reveal_next");

        UIFeedbackService.Play(FeedbackCue.CardFlip, true);
        if (catalog.Rarities.TryGetValue(entry.Printing.RarityId, out RarityDefinition rarity) &&
            !string.IsNullOrWhiteSpace(rarity.PresentationKey))
        {
            UIFeedbackService.Play(FeedbackCue.RareReveal, true);
        }
        AnimateRevealCard();
        return true;
    }

    public bool RevealAllCards()
    {
        if (currentOutcome == null || packAnimating || revealEntries.Count == 0 || IsSummaryVisible)
            return false;

        revealAnimation?.Pause();
        revealAnimation = null;
        revealAnimating = false;
        revealIndex = revealEntries.Count - 1;
        ShowSummary();
        return true;
    }

    public void MenuBtnClick()
    {
        UIFeedbackService.Play(FeedbackCue.Back);
        if (GameManager.Instance != null && GameManager.Instance.loadManager != null)
            GameManager.Instance.loadManager.LoadScene(1);
        else
            SceneManager.LoadScene("002_MainMenuScene");
    }

    private void QueryVisualElements()
    {
        selectionPage = Required<VisualElement>("gacha-selection-page");
        openingPage = Required<VisualElement>("gacha-opening-page");
        selectedArtSlot = Required<VisualElement>("selected-art-slot");
        oddsList = Required<VisualElement>("odds-list");
        packStage = Required<VisualElement>("pack-stage");
        packShell = Required<VisualElement>("pack-shell");
        packTearLine = Required<VisualElement>("pack-tear-line");
        packArtSlot = Required<VisualElement>("pack-art-slot");
        revealStage = Required<VisualElement>("reveal-stage");
        revealCard = Required<VisualElement>("reveal-card");
        revealArtSlot = Required<VisualElement>("reveal-art-slot");
        summaryStage = Required<VisualElement>("summary-stage");
        productList = Required<ListView>("product-list");
        summaryList = Required<ScrollView>("summary-list");
        title = Required<Label>("gacha-title");
        subtitle = Required<Label>("gacha-subtitle");
        status = Required<Label>("gacha-status");
        selectedName = Required<Label>("selected-product-name");
        selectedMetadata = Required<Label>("selected-product-metadata");
        ruleBadge = Required<Label>("rule-badge");
        ruleNotice = Required<Label>("rule-notice");
        oddsHeading = Required<Label>("odds-heading");
        packTitle = Required<Label>("pack-title");
        packHint = Required<Label>("pack-hint");
        revealProgress = Required<Label>("reveal-progress");
        revealName = Required<Label>("reveal-name");
        revealMetadata = Required<Label>("reveal-metadata");
        revealNewBadge = Required<Label>("reveal-new-badge");
        summaryTitle = Required<Label>("summary-title");
        summaryMetadata = Required<Label>("summary-metadata");
        menuButton = Required<Button>("gacha-menu-button");
        prepareButton = Required<Button>("prepare-pack-button");
        ruleSourceButton = Required<Button>("rule-source-button");
        tearButton = Required<Button>("tear-pack-button");
        revealButton = Required<Button>("reveal-next-button");
        revealAllButton = Required<Button>("reveal-all-button");
        backToProductsButton = Required<Button>("back-to-products-button");
        openAgainButton = Required<Button>("open-again-button");
        summaryProductsButton = Required<Button>("summary-products-button");
    }

    private void ConfigureProductList()
    {
        productList.virtualizationMethod = CollectionVirtualizationMethod.FixedHeight;
        productList.fixedItemHeight = 132f;
        productList.selectionType = SelectionType.Single;
        productList.makeItem = MakeProductRow;
        productList.bindItem = BindProductRow;
        productList.unbindItem = UnbindProductRow;
        productList.destroyItem = DestroyProductRow;
        productList.selectionChanged += OnProductSelectionChanged;
    }

    private void ConfigureButtons()
    {
        menuButton.clicked += MenuBtnClick;
        prepareButton.clicked += () => PrepareSelectedProduct();
        ruleSourceButton.clicked += OpenSelectedRuleSource;
        tearButton.clicked += () => TearPack();
        revealButton.clicked += () => RevealNextCard();
        revealAllButton.clicked += () => RevealAllCards();
        backToProductsButton.clicked += () =>
        {
            UIFeedbackService.Play(FeedbackCue.Back);
            ShowSelectionPage();
        };
        openAgainButton.clicked += () => PrepareSelectedProduct();
        summaryProductsButton.clicked += () =>
        {
            UIFeedbackService.Play(FeedbackCue.Back);
            ShowSelectionPage();
        };
    }

    private void RebuildProducts()
    {
        string languageId = ApplicationServices.Languages.ContentLanguage.ResolvedLanguageId;
        IProductRuleProvider simulatedRules = new UniformSimulationRuleProvider(cardsPerPack, languageId);
        IProductRuleProvider productRules = ApplicationServices.ProductRules == null
            ? simulatedRules
            : new FallbackProductRuleProvider(ApplicationServices.ProductRules, simulatedRules);
        openingService = new ProductOpeningService(
            catalog,
            productRules,
            InventoryStoreOverride ?? new PlayerInventoryProgressStore(),
            contentLanguageId: languageId);

        products.Clear();
        products.AddRange(catalog.Products.Values
            .Where(product => product.EligiblePrintingIds.Any(printingId =>
                string.Equals(
                    catalog.Printings[printingId].Identity.LanguageId,
                    languageId,
                    StringComparison.OrdinalIgnoreCase)))
            .OrderBy(product => catalog.Sets[product.SetId].ReleaseDate ?? DateTime.MaxValue)
            .ThenBy(product => product.Id, StringComparer.Ordinal));
        productList.itemsSource = products;
        productList.Rebuild();

        ProductDefinition next = products.FirstOrDefault(product => product.Id == productId) ?? products.FirstOrDefault();
        if (next == null)
        {
            selectedProduct = null;
            selectedProfile = null;
            prepareButton.SetEnabled(false);
            SetStatus(CardUiText.Get("gacha.status.no_products"), true);
            return;
        }

        SelectProduct(next);
        int index = products.IndexOf(next);
        productList.SetSelectionWithoutNotify(new[] { index });
    }

    private VisualElement MakeProductRow()
    {
        var row = new VisualElement();
        row.AddToClassList("gacha-product-row");
        AsyncCardImageView image = Track(new AsyncCardImageView(textureCache));
        image.Element.AddToClassList("gacha-product-row__cover");
        var copy = new VisualElement();
        copy.AddToClassList("gacha-product-row__copy");
        var name = new Label();
        name.AddToClassList("gacha-product-row__name");
        var metadata = new Label();
        metadata.AddToClassList("gacha-product-row__metadata");
        copy.Add(name);
        copy.Add(metadata);
        row.Add(image.Element);
        row.Add(copy);
        row.userData = new ProductRow { Image = image, Name = name, Metadata = metadata };
        return row;
    }

    private void BindProductRow(VisualElement element, int index)
    {
        if (index < 0 || index >= products.Count)
            return;
        ProductDefinition product = products[index];
        SetDefinition set = catalog.Sets[product.SetId];
        var row = (ProductRow)element.userData;
        row.Name.text = DisplayName(product);
        row.Metadata.text = ProductMetadata(product, set);
        PrintingDefinition cover = CoverFor(product);
        if (cover != null)
            row.Image.Bind(cover);
        else
            row.Image.Unbind();
        element.tooltip = row.Name.text;
    }

    private static void UnbindProductRow(VisualElement element, int index)
    {
        if (element.userData is ProductRow row)
            row.Image.Unbind();
    }

    private void DestroyProductRow(VisualElement element)
    {
        if (!(element.userData is ProductRow row))
            return;
        row.Image.Dispose();
        imageViews.Remove(row.Image);
    }

    private void OnProductSelectionChanged(IEnumerable<object> selection)
    {
        ProductDefinition product = selection.OfType<ProductDefinition>().FirstOrDefault();
        if (product == null || product == selectedProduct)
            return;
        UIFeedbackService.Play(FeedbackCue.Confirm);
        SelectProduct(product);
    }

    private void SelectProduct(ProductDefinition product)
    {
        selectedProduct = product;
        productId = product.Id;
        selectedProfile = openingService.GetProfile(product.Id);
        selectedOdds = ProductOddsAnalyzer.Analyze(catalog, selectedProfile.Rules);
        SetDefinition set = catalog.Sets[product.SetId];
        selectedName.text = DisplayName(product);
        selectedMetadata.text = ProductMetadata(product, set);
        ruleBadge.text = selectedProfile.IsHistoricallyVerified
            ? CardUiText.Get("gacha.rule.verified")
            : CardUiText.Get("gacha.rule.simulation");
        ruleBadge.EnableInClassList("is-verified", selectedProfile.IsHistoricallyVerified);
        ruleNotice.text = selectedProfile.IsHistoricallyVerified
            ? selectedProfile.GetDescription(ApplicationServices.Languages.UiLanguageId)
            : CardUiText.Get("gacha.rule.simulation_notice");
        ruleSourceButton.style.display = selectedProfile.SourceReferences.Count > 0 && selectedProfile.IsHistoricallyVerified
            ? DisplayStyle.Flex
            : DisplayStyle.None;
        prepareButton.SetEnabled(true);
        PrintingDefinition cover = CoverFor(product);
        if (cover != null)
            selectedImage.Bind(cover);
        else
            selectedImage.Unbind();
        BuildOddsList();
        SetStatus(string.Empty, false);
    }

    private void BuildOddsList()
    {
        oddsList.Clear();
        if (selectedOdds == null)
            return;
        foreach (RarityOdds odds in selectedOdds.Rarities)
        {
            var row = new VisualElement();
            row.AddToClassList("gacha-odds-row");
            string name = catalog.Rarities.TryGetValue(odds.RarityId, out RarityDefinition rarity)
                ? DisplayName(rarity)
                : odds.RarityId;
            var nameLabel = new Label(name);
            nameLabel.AddToClassList("gacha-odds-row__name");
            var valueLabel = new Label(odds.AverageSlotProbability.ToString("P1"));
            valueLabel.AddToClassList("gacha-odds-row__value");
            row.Add(nameLabel);
            row.Add(valueLabel);
            oddsList.Add(row);
        }
    }

    private void BuildRevealEntries()
    {
        revealEntries.Clear();
        if (currentOutcome.Inventory.Awards.Count != currentOutcome.Draw.Printings.Count)
            throw new InvalidOperationException("Inventory awards do not match the drawn cards.");
        for (int index = 0; index < currentOutcome.Draw.Printings.Count; index++)
        {
            DrawnPrinting drawn = currentOutcome.Draw.Printings[index];
            InventoryAward award = currentOutcome.Inventory.Awards[index];
            if (!string.Equals(drawn.PrintingId, award.PrintingId, StringComparison.Ordinal))
                throw new InvalidOperationException("Inventory award order does not match the draw result.");
            revealEntries.Add(new RevealEntry
            {
                Printing = catalog.Printings[drawn.PrintingId],
                Award = award
            });
        }
    }

    private void BeginRevealStage()
    {
        packAnimating = false;
        packStage.style.display = DisplayStyle.None;
        revealStage.style.display = DisplayStyle.Flex;
        summaryStage.style.display = DisplayStyle.None;
        revealIndex = -1;
        revealImage.Unbind();
        revealName.text = CardUiText.Get("gacha.reveal.ready");
        revealMetadata.text = CardUiText.Get("gacha.reveal.one_at_time");
        revealNewBadge.text = string.Empty;
        revealProgress.text = CardUiText.Format("gacha.reveal.pending_progress", revealEntries.Count);
        revealButton.text = CardUiText.Get("gacha.action.reveal_first");
        revealButton.SetEnabled(true);
        revealAllButton.text = CardUiText.Get("gacha.action.reveal_all");
        revealAllButton.SetEnabled(true);
    }

    private void ShowSummary()
    {
        revealAnimation?.Pause();
        revealAnimation = null;
        revealAnimating = false;
        revealStage.style.display = DisplayStyle.None;
        summaryStage.style.display = DisplayStyle.Flex;
        ApplySummaryText();
        BuildSummaryList();
        if (currentOutcome.Inventory.NewPrintingCount > 0)
            UIFeedbackService.Play(FeedbackCue.CollectionNew, true);
        else
            UIFeedbackService.Play(FeedbackCue.Confirm);
    }

    private void BuildSummaryList()
    {
        summaryList.Clear();
        foreach (RevealEntry entry in revealEntries)
        {
            var row = new VisualElement();
            row.AddToClassList("gacha-summary-row");
            var copy = new VisualElement();
            copy.AddToClassList("gacha-summary-row__copy");
            var name = new Label(DisplayName(entry.Printing));
            name.AddToClassList("gacha-summary-row__name");
            var metadata = new Label(RevealMetadata(entry.Printing, entry.Award.CurrentCount));
            metadata.AddToClassList("gacha-summary-row__metadata");
            copy.Add(name);
            copy.Add(metadata);
            var badge = new Label(entry.Award.IsNew
                ? CardUiText.Get("common.badge.new")
                : $"×{entry.Award.CurrentCount}");
            badge.AddToClassList("gacha-summary-row__badge");
            badge.EnableInClassList("is-new", entry.Award.IsNew);
            row.Add(copy);
            row.Add(badge);
            summaryList.Add(row);
        }
    }

    private void ShowSelectionPage()
    {
        packAnimation?.Pause();
        revealAnimation?.Pause();
        packAnimating = false;
        revealAnimating = false;
        currentOutcome = null;
        revealEntries.Clear();
        revealIndex = -1;
        openingPage.style.display = DisplayStyle.None;
        selectionPage.style.display = DisplayStyle.Flex;
        packStage.style.display = DisplayStyle.None;
        revealStage.style.display = DisplayStyle.None;
        summaryStage.style.display = DisplayStyle.None;
        packImage?.Unbind();
        revealImage?.Unbind();
    }

    private void AnimatePackTear(Action completed)
    {
        packAnimation?.Pause();
        if (UIFeedbackService.ReduceMotion)
        {
            packTearLine.style.width = Length.Percent(100f);
            completed();
            return;
        }

        float startedAt = Time.realtimeSinceStartup;
        float duration = 0.46f / UIFeedbackService.AnimationSpeed;
        packAnimation = packShell.schedule.Execute(() =>
        {
            float progress = Mathf.Clamp01((Time.realtimeSinceStartup - startedAt) / duration);
            float eased = Mathf.SmoothStep(0f, 1f, progress);
            packTearLine.style.width = Length.Percent(eased * 100f);
            float pulse = 1f + Mathf.Sin(progress * Mathf.PI * 3f) * 0.035f * (1f - progress);
            packShell.style.scale = new Scale(new Vector3(pulse, pulse, 1f));
            packShell.style.opacity = 1f - Mathf.Max(0f, (progress - 0.72f) / 0.28f);
            if (progress < 1f)
                return;
            packAnimation?.Pause();
            packAnimation = null;
            completed();
        }).Every(16);
    }

    private void AnimateRevealCard()
    {
        revealAnimation?.Pause();
        if (UIFeedbackService.ReduceMotion)
        {
            revealCard.style.opacity = 1f;
            revealCard.style.scale = new Scale(Vector3.one);
            revealAnimating = false;
            revealButton.SetEnabled(true);
            return;
        }

        revealAnimating = true;
        revealButton.SetEnabled(false);
        revealCard.style.opacity = 0f;
        revealCard.style.scale = new Scale(new Vector3(0.72f, 0.72f, 1f));
        float startedAt = Time.realtimeSinceStartup;
        float duration = 0.22f / UIFeedbackService.AnimationSpeed;
        revealAnimation = revealCard.schedule.Execute(() =>
        {
            float progress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Time.realtimeSinceStartup - startedAt) / duration));
            revealCard.style.opacity = progress;
            float scale = Mathf.Lerp(0.72f, 1f, progress);
            revealCard.style.scale = new Scale(new Vector3(scale, scale, 1f));
            if (progress < 1f)
                return;
            revealAnimation?.Pause();
            revealAnimation = null;
            revealAnimating = false;
            revealButton.SetEnabled(true);
        }).Every(16);
    }

    private void RefreshLocalizedChrome()
    {
        if (root == null)
            return;
        title.text = CardUiText.Get("gacha.title");
        subtitle.text = CardUiText.Get("gacha.subtitle");
        menuButton.text = CardUiText.Get("common.action.main_menu");
        prepareButton.text = CardUiText.Get("gacha.action.prepare");
        ruleSourceButton.text = CardUiText.Get("gacha.action.rule_source");
        tearButton.text = CardUiText.Get("gacha.action.tear");
        revealAllButton.text = CardUiText.Get("gacha.action.reveal_all");
        backToProductsButton.text = CardUiText.Get("gacha.action.all_products");
        openAgainButton.text = CardUiText.Get("gacha.action.open_another");
        summaryProductsButton.text = CardUiText.Get("gacha.action.choose_another");
        oddsHeading.text = CardUiText.Get("gacha.odds.heading");
        if (selectedProduct != null)
            SelectProduct(selectedProduct);
        else if (products.Count == 0)
            SetStatus(CardUiText.Get("gacha.status.no_products"), true);
        if (currentOutcome != null)
            ApplyRevealText();
        if (IsSummaryVisible)
        {
            ApplySummaryText();
            BuildSummaryList();
        }
        if (packStage.resolvedStyle.display == DisplayStyle.Flex)
            packHint.text = CardUiText.Get("gacha.pack.hint");
        productList?.RefreshItems();
    }

    private void OnUiLanguageChanged(string languageId)
    {
        RefreshLocalizedChrome();
    }

    private void OnContentLanguageChanged(ContentLanguageSelection selection)
    {
        string previousProductId = selectedProduct?.Id;
        productId = previousProductId;
        RebuildProducts();
        RefreshLocalizedChrome();
        ShowSelectionPage();
    }

    private void OpenSelectedRuleSource()
    {
        string source = selectedProfile?.SourceReference;
        if (string.IsNullOrWhiteSpace(source))
            return;
        UIFeedbackService.Play(FeedbackCue.Confirm);
        UnityEngine.Application.OpenURL(source);
    }

    private PrintingDefinition CoverFor(ProductDefinition product)
    {
        string languageId = ApplicationServices.Languages.ContentLanguage.ResolvedLanguageId;
        return product.EligiblePrintingIds
            .Select(id => catalog.Printings[id])
            .FirstOrDefault(printing =>
                string.Equals(printing.Identity.LanguageId, languageId, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(printing.ImageRelativePath));
    }

    private string ProductMetadata(ProductDefinition product, SetDefinition set)
    {
        string year = set.ReleaseDate?.Year.ToString() ?? "—";
        string languageId = ApplicationServices.Languages.ContentLanguage.ResolvedLanguageId;
        int cardCount = product.EligiblePrintingIds.Count(id =>
            string.Equals(catalog.Printings[id].Identity.LanguageId, languageId, StringComparison.OrdinalIgnoreCase));
        return CardUiText.Format("gacha.product.metadata", year, cardCount, languageId);
    }

    private string RevealMetadata(PrintingDefinition printing, int ownedCount)
    {
        string rarity = catalog.Rarities.TryGetValue(printing.RarityId, out RarityDefinition definition)
            ? DisplayName(definition)
            : printing.RarityId;
        return CardUiText.Format(
            "gacha.reveal.metadata",
            printing.Identity.CardNumber,
            rarity,
            printing.Identity.VariantId,
            ownedCount);
    }

    private string DisplayName(Definition definition)
    {
        return ApplicationServices.Languages.GetDisplayName(definition);
    }

    private void SetStatus(string message, bool isError)
    {
        if (status == null)
            return;
        status.text = message ?? string.Empty;
        status.style.display = string.IsNullOrWhiteSpace(message) ? DisplayStyle.None : DisplayStyle.Flex;
        status.EnableInClassList("is-error", isError);
    }

    private void EnsureDocumentAssets()
    {
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();
        if (uiDocument == null && UnityEngine.Application.isPlaying)
            uiDocument = gameObject.AddComponent<UIDocument>();
        if (uiDocument == null)
            return;
        if (uiDocument.panelSettings == null)
            uiDocument.panelSettings = Resources.Load<PanelSettings>("UI/Collection Panel Settings");
        if (uiDocument.visualTreeAsset == null && viewAsset != null)
            uiDocument.visualTreeAsset = viewAsset;
    }

    private T Required<T>(string name) where T : VisualElement
    {
        T element = root.Q<T>(name);
        if (element == null)
            throw new InvalidOperationException($"Pack opening element '{name}' is missing.");
        return element;
    }

    private AsyncCardImageView Track(AsyncCardImageView imageView)
    {
        imageViews.Add(imageView);
        return imageView;
    }

    private void ApplyRevealText()
    {
        revealAllButton.text = CardUiText.Get("gacha.action.reveal_all");
        if (revealIndex < 0 || revealIndex >= revealEntries.Count)
        {
            revealName.text = CardUiText.Get("gacha.reveal.ready");
            revealMetadata.text = CardUiText.Get("gacha.reveal.one_at_time");
            revealNewBadge.text = string.Empty;
            revealProgress.text = CardUiText.Format("gacha.reveal.pending_progress", revealEntries.Count);
            revealButton.text = CardUiText.Get("gacha.action.reveal_first");
            return;
        }

        RevealEntry entry = revealEntries[revealIndex];
        revealName.text = DisplayName(entry.Printing);
        revealMetadata.text = RevealMetadata(entry.Printing, entry.Award.CurrentCount);
        revealNewBadge.text = entry.Award.IsNew
            ? CardUiText.Get("common.badge.new")
            : CardUiText.Get("gacha.badge.owned");
        revealProgress.text = CardUiText.Format("gacha.reveal.progress", revealIndex + 1, revealEntries.Count);
        revealButton.text = revealIndex == revealEntries.Count - 1
            ? CardUiText.Get("gacha.action.view_results")
            : CardUiText.Get("gacha.action.reveal_next");
    }

    private void ApplySummaryText()
    {
        if (currentOutcome == null)
            return;
        summaryTitle.text = CardUiText.Get("gacha.summary.title");
        summaryMetadata.text = CardUiText.Format(
            "gacha.summary.metadata",
            revealEntries.Count,
            currentOutcome.Inventory.NewPrintingCount,
            currentOutcome.Inventory.ProductsOpened);
    }
}
