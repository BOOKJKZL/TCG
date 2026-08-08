using System;
using System.Collections.Generic;
using System.Globalization;
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
        public int ProductIndex;
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
    private ProductOpeningTheme selectedTheme = ProductOpeningThemeService.DefaultTheme;
    private ProductOpeningBatchOutcome currentBatchOutcome;
    private CardTextureCache textureCache;
    private int revealIndex = -1;
    private int preparedProductCount = 1;
    private bool packAnimating;
    private bool revealAnimating;
    private bool currentRevealHighlighted;
    private string appliedThemeClass;
    private Texture2D selectedThemeArtwork;
    private UiToolkitSafeAreaBinding safeAreaBinding;

    private VisualElement root;
    private VisualElement selectionPage;
    private VisualElement openingPage;
    private VisualElement selectedArtSlot;
    private VisualElement oddsList;
    private VisualElement ruleSourceList;
    private VisualElement packStage;
    private VisualElement packParticleLayer;
    private VisualElement packShell;
    private VisualElement packThemeArtwork;
    private VisualElement packThemeBand;
    private VisualElement packTearLine;
    private VisualElement packArtSlot;
    private VisualElement revealStage;
    private VisualElement revealParticleLayer;
    private VisualElement revealCard;
    private VisualElement revealAura;
    private VisualElement revealArtSlot;
    private VisualElement summaryStage;
    private ListView productList;
    private ScrollView summaryList;
    private ScrollView openingHistory;
    private Label title;
    private Label subtitle;
    private Label status;
    private Label selectedName;
    private Label selectedMetadata;
    private Label ruleBadge;
    private Label ruleNotice;
    private Label ruleEvidenceSummary;
    private Label oddsHeading;
    private Label packTitle;
    private Label packHint;
    private Label revealProgress;
    private Label revealName;
    private Label revealMetadata;
    private Label revealNewBadge;
    private Label summaryTitle;
    private Label summaryMetadata;
    private Label openingStatistics;
    private Button menuButton;
    private Button manageContentButton;
    private Button prepareButton;
    private Button prepareTenButton;
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
    private ThemeParticleField packParticles;
    private ThemeParticleField revealParticles;

    public static IInventoryProgressStore InventoryStoreOverride { private get; set; }

    public bool IsReady { get; private set; }
    public string InitializationError { get; private set; }
    public int ProductCount => products.Count;
    public string SelectedProductId => selectedProduct?.Id;
    public ProductRuleTrust SelectedRuleTrust => selectedProfile?.Trust ?? ProductRuleTrust.Simulated;
    public ProductRuleConfidence SelectedRuleConfidence =>
        selectedProfile?.Confidence ?? ProductRuleConfidence.Unverified;
    public string SelectedRuleProfileId => selectedProfile?.Id;
    public string SelectedRuleRegionId => selectedProfile?.RegionId;
    public int SelectedRuleEvidenceCount => selectedProfile?.Evidence.Count ?? 0;
    public string SelectedThemeId => selectedTheme?.Id;
    public string SelectedThemeStyleClass => selectedTheme?.StyleClass;
    public string SelectedThemePackAudioKey => selectedTheme?.PackOpenAudioKey;
    public string SelectedThemeRareAudioKey => selectedTheme?.RareRevealAudioKey;
    public string SelectedThemeArtworkResourcePath => selectedTheme?.PackArtworkResourcePath;
    public bool HasSelectedThemeArtwork => selectedThemeArtwork != null;
    public bool IsCurrentRevealHighlighted => currentRevealHighlighted;
    public int PackParticleCount => packParticles?.ActiveParticleCount ?? 0;
    public int RevealParticleCount => revealParticles?.ActiveParticleCount ?? 0;
    public bool ArePackParticlesRunning => packParticles?.IsRunning ?? false;
    public bool AreRevealParticlesRunning => revealParticles?.IsRunning ?? false;
    public int LastOpenedCardCount => currentBatchOutcome?.Draws.Sum(draw => draw.Printings.Count) ?? 0;
    public int LastOpenedProductCount => currentBatchOutcome?.Draws.Count ?? 0;
    public int PreparedProductCount => preparedProductCount;
    public int RecentHistoryCount => openingService?.GetOpeningHistory(10).Count ?? 0;
    public int RevealedCount => revealIndex + 1;
    public int CachedTextureCount => textureCache?.Count ?? 0;
    public long CachedTextureBytes => textureCache?.DecodedBytes ?? 0L;
    public long CachedTextureBudgetBytes => textureCache?.MaximumDecodedBytes ?? 0L;
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
        safeAreaBinding?.Dispose();
        safeAreaBinding = null;
        if (ApplicationServices.IsConfigured)
        {
            ApplicationServices.Languages.UiLanguageChanged -= OnUiLanguageChanged;
            ApplicationServices.Languages.ContentLanguageChanged -= OnContentLanguageChanged;
        }

        packAnimation?.Pause();
        revealAnimation?.Pause();
        packParticles?.Dispose();
        revealParticles?.Dispose();
        packParticles = null;
        revealParticles = null;
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
            safeAreaBinding = UiToolkitSafeArea.Attach(root);

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
        return PrepareSelectedBatch(1);
    }

    public bool PrepareTenProducts()
    {
        return PrepareSelectedBatch(10);
    }

    private bool PrepareSelectedBatch(int productCount)
    {
        if (!IsReady || selectedProduct == null || selectedProfile == null || packAnimating)
            return false;

        preparedProductCount = productCount;
        UIFeedbackService.Play(FeedbackCue.Confirm);
        SetStatus(string.Empty, false);
        currentBatchOutcome = null;
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
        revealAura.RemoveFromClassList("is-highlighted");
        revealAura.style.opacity = 0f;
        currentRevealHighlighted = false;
        revealParticles.Stop();
        packParticles.PlayAmbient(selectedTheme.ParticleTheme);
        tearButton.SetEnabled(true);
        packTitle.text = productCount == 1
            ? DisplayName(selectedProduct)
            : CardUiText.Format("gacha.pack.batch_title", productCount, DisplayName(selectedProduct));
        packHint.text = productCount == 1
            ? CardUiText.Get("gacha.pack.hint")
            : CardUiText.Format("gacha.pack.batch_hint", productCount);
        if (selectedThemeArtwork != null)
            packImage.Unbind();
        else
        {
            PrintingDefinition cover = CoverFor(selectedProduct);
            if (cover != null)
                packImage.Bind(cover);
            else
                packImage.Unbind();
        }
        AnimatePackReady();
        return true;
    }

    public bool TearPack()
    {
        if (!IsReady || selectedProduct == null || packAnimating || currentBatchOutcome != null)
            return false;

        try
        {
            currentBatchOutcome = openingService.OpenBatch(selectedProduct.Id, preparedProductCount);
            BuildRevealEntries();
            foreach (ProductDrawResult draw in currentBatchOutcome.Draws)
                PackOpened?.Invoke(draw);
            RefreshOpeningJournal();
            tearButton.SetEnabled(false);
            packAnimating = true;
            UIFeedbackService.Play(FeedbackCue.PackOpen, selectedTheme.PackOpenAudioKey, true);
            AnimatePackTear(BeginRevealStage);
            return true;
        }
        catch (Exception exception)
        {
            currentBatchOutcome = null;
            SetStatus(CardUiText.Format("gacha.status.open_failed", exception.Message), true);
            Debug.LogWarning($"Pack opening failed: {exception.Message}");
            InitializationFailed?.Invoke(exception.Message);
            UIFeedbackService.Play(FeedbackCue.Error);
            return false;
        }
    }

    public bool RevealNextCard()
    {
        if (currentBatchOutcome == null || packAnimating || revealAnimating)
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
        revealProgress.text = preparedProductCount > 1
            ? CardUiText.Format(
                "gacha.reveal.batch_progress",
                entry.ProductIndex + 1,
                preparedProductCount,
                revealIndex + 1,
                revealEntries.Count)
            : CardUiText.Format("gacha.reveal.progress", revealIndex + 1, revealEntries.Count);
        revealButton.text = revealIndex == revealEntries.Count - 1
            ? CardUiText.Get("gacha.action.view_results")
            : CardUiText.Get("gacha.action.reveal_next");

        currentRevealHighlighted = catalog.Rarities.TryGetValue(
            entry.Printing.RarityId,
            out RarityDefinition rarity) && selectedTheme.Highlights(rarity);
        revealAura.EnableInClassList("is-highlighted", currentRevealHighlighted);
        if (currentRevealHighlighted)
            revealParticles.PlayBurst(selectedTheme.ParticleTheme);
        else
            revealParticles.Stop();
        UIFeedbackService.Play(FeedbackCue.CardFlip, true);
        if (currentRevealHighlighted)
        {
            UIFeedbackService.Play(FeedbackCue.RareReveal, selectedTheme.RareRevealAudioKey, true);
        }
        AnimateRevealCard(currentRevealHighlighted, entry.ProductIndex > 0);
        return true;
    }

    public bool RevealAllCards()
    {
        if (currentBatchOutcome == null || packAnimating || revealEntries.Count == 0 || IsSummaryVisible)
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
        ruleSourceList = Required<VisualElement>("rule-source-list");
        packStage = Required<VisualElement>("pack-stage");
        packParticleLayer = Required<VisualElement>("pack-particle-layer");
        packShell = Required<VisualElement>("pack-shell");
        packThemeArtwork = Required<VisualElement>("pack-theme-artwork");
        packThemeBand = Required<VisualElement>("pack-theme-band");
        packTearLine = Required<VisualElement>("pack-tear-line");
        packArtSlot = Required<VisualElement>("pack-art-slot");
        revealStage = Required<VisualElement>("reveal-stage");
        revealParticleLayer = Required<VisualElement>("reveal-particle-layer");
        revealCard = Required<VisualElement>("reveal-card");
        revealAura = Required<VisualElement>("reveal-aura");
        revealArtSlot = Required<VisualElement>("reveal-art-slot");
        summaryStage = Required<VisualElement>("summary-stage");
        productList = Required<ListView>("product-list");
        summaryList = Required<ScrollView>("summary-list");
        openingHistory = Required<ScrollView>("opening-history");
        title = Required<Label>("gacha-title");
        subtitle = Required<Label>("gacha-subtitle");
        status = Required<Label>("gacha-status");
        selectedName = Required<Label>("selected-product-name");
        selectedMetadata = Required<Label>("selected-product-metadata");
        ruleBadge = Required<Label>("rule-badge");
        ruleNotice = Required<Label>("rule-notice");
        ruleEvidenceSummary = Required<Label>("rule-evidence-summary");
        oddsHeading = Required<Label>("odds-heading");
        packTitle = Required<Label>("pack-title");
        packHint = Required<Label>("pack-hint");
        revealProgress = Required<Label>("reveal-progress");
        revealName = Required<Label>("reveal-name");
        revealMetadata = Required<Label>("reveal-metadata");
        revealNewBadge = Required<Label>("reveal-new-badge");
        summaryTitle = Required<Label>("summary-title");
        summaryMetadata = Required<Label>("summary-metadata");
        openingStatistics = Required<Label>("opening-statistics");
        menuButton = Required<Button>("gacha-menu-button");
        manageContentButton = Required<Button>("gacha-manage-content-button");
        prepareButton = Required<Button>("prepare-pack-button");
        prepareTenButton = Required<Button>("prepare-ten-button");
        tearButton = Required<Button>("tear-pack-button");
        revealButton = Required<Button>("reveal-next-button");
        revealAllButton = Required<Button>("reveal-all-button");
        backToProductsButton = Required<Button>("back-to-products-button");
        openAgainButton = Required<Button>("open-again-button");
        summaryProductsButton = Required<Button>("summary-products-button");
        packParticles?.Dispose();
        revealParticles?.Dispose();
        packParticles = new ThemeParticleField(packParticleLayer);
        revealParticles = new ThemeParticleField(revealParticleLayer);
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
        manageContentButton.clicked += OpenContentManagement;
        prepareButton.clicked += () => PrepareSelectedProduct();
        prepareTenButton.clicked += () => PrepareTenProducts();
        tearButton.clicked += () => TearPack();
        revealButton.clicked += () => RevealNextCard();
        revealAllButton.clicked += () => RevealAllCards();
        backToProductsButton.clicked += () =>
        {
            UIFeedbackService.Play(FeedbackCue.Back);
            ShowSelectionPage();
        };
        openAgainButton.clicked += () => PrepareSelectedBatch(preparedProductCount);
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
            .OrderBy(
                product => catalog.Sets[product.SetId],
                new SetDefinitionComparer(SetSortMode.Generation, languageId))
            .ThenBy(product => product.Id, StringComparer.Ordinal));
        productList.itemsSource = products;
        productList.Rebuild();

        ProductDefinition next = products.FirstOrDefault(product => product.Id == productId) ?? products.FirstOrDefault();
        if (next == null)
        {
            selectedProduct = null;
            selectedProfile = null;
            prepareButton.SetEnabled(false);
            prepareTenButton.SetEnabled(false);
            SetStatus(CardUiText.Get("gacha.status.no_products"), true);
            manageContentButton.style.display = DisplayStyle.Flex;
            RefreshOpeningJournal();
            return;
        }

        manageContentButton.style.display = DisplayStyle.None;
        SelectProduct(next);
        int index = products.IndexOf(next);
        productList.SetSelectionWithoutNotify(new[] { index });
        RefreshOpeningJournal();
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
        ApplyTheme(ProductOpeningThemeService.Resolve(product));
        selectedProfile = openingService.GetProfile(product.Id);
        selectedOdds = ProductOddsAnalyzer.Analyze(catalog, selectedProfile.Rules);
        SetDefinition set = catalog.Sets[product.SetId];
        selectedName.text = DisplayName(product);
        selectedMetadata.text = ProductMetadata(product, set);
        bool isVerified = selectedProfile.Trust == ProductRuleTrust.HistoricallyVerified;
        bool isSourcedSimulation = selectedProfile.Trust == ProductRuleTrust.SourceInformedSimulation;
        string badgeKey = isVerified
            ? "gacha.rule.verified"
            : isSourcedSimulation
                ? "gacha.rule.sourced_simulation"
                : "gacha.rule.simulation";
        ruleBadge.text = CardUiText.Get(badgeKey);
        ruleBadge.EnableInClassList("is-verified", isVerified);
        ruleBadge.EnableInClassList("is-sourced", isSourcedSimulation);
        ruleNotice.text = selectedProfile.Trust == ProductRuleTrust.Simulated
            ? CardUiText.Get("gacha.rule.simulation_notice")
            : selectedProfile.GetDescription(ApplicationServices.Languages.UiLanguageId);
        BuildRuleEvidence();
        prepareButton.SetEnabled(true);
        prepareTenButton.SetEnabled(true);
        PrintingDefinition cover = CoverFor(product);
        if (cover != null)
            selectedImage.Bind(cover);
        else
            selectedImage.Unbind();
        BuildOddsList();
        SetStatus(string.Empty, false);
    }

    private void ApplyTheme(ProductOpeningTheme theme)
    {
        selectedTheme = theme ?? ProductOpeningThemeService.DefaultTheme;
        if (root == null)
            return;
        if (!string.IsNullOrWhiteSpace(appliedThemeClass))
            root.RemoveFromClassList(appliedThemeClass);
        appliedThemeClass = selectedTheme.StyleClass;
        root.AddToClassList(appliedThemeClass);
        selectedThemeArtwork = string.IsNullOrWhiteSpace(selectedTheme.PackArtworkResourcePath)
            ? null
            : Resources.Load<Texture2D>(selectedTheme.PackArtworkResourcePath);
        packThemeArtwork.style.backgroundImage = selectedThemeArtwork == null
            ? new StyleBackground(StyleKeyword.None)
            : new StyleBackground(selectedThemeArtwork);
        packThemeArtwork.EnableInClassList("is-visible", selectedThemeArtwork != null);
        if (packThemeBand != null)
            packThemeBand.userData = selectedTheme.Id;
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

    private void RefreshOpeningJournal()
    {
        if (openingService == null || openingStatistics == null || openingHistory == null)
            return;

        ProductOpeningStatistics statistics = openingService.GetOpeningStatistics();
        openingStatistics.text = statistics.TotalProductsOpened == 0
            ? CardUiText.Get("gacha.statistics.empty")
            : CardUiText.Format(
                "gacha.statistics.summary",
                statistics.TotalProductsOpened,
                statistics.TotalCardsDrawn,
                FormatCounts(statistics.ProductsByLanguage, id => id),
                FormatCounts(statistics.ProductsBySet, id =>
                    catalog.Sets.TryGetValue(id, out SetDefinition set) ? DisplayName(set) : id),
                FormatCounts(statistics.CardsByRarity, id =>
                    catalog.Rarities.TryGetValue(id, out RarityDefinition rarity) ? DisplayName(rarity) : id));

        openingHistory.Clear();
        IReadOnlyList<ProductOpeningHistoryEntry> history = openingService.GetOpeningHistory(8);
        if (history.Count == 0)
        {
            var empty = new Label(CardUiText.Get("gacha.history.empty"));
            empty.AddToClassList("gacha-history-row");
            openingHistory.Add(empty);
            return;
        }

        foreach (ProductOpeningHistoryEntry entry in history)
        {
            string productName = catalog.Products.TryGetValue(entry.ProductId, out ProductDefinition product)
                ? DisplayName(product)
                : entry.ProductId;
            var row = new Label(CardUiText.Format(
                "gacha.history.row",
                entry.OpenedAtUtc.ToLocalTime().ToString("MM-dd HH:mm", CultureInfo.CurrentCulture),
                productName,
                entry.ProductCount,
                entry.CardCount,
                entry.NewPrintingCount,
                entry.LanguageId));
            row.AddToClassList("gacha-history-row");
            openingHistory.Add(row);
        }
    }

    private static string FormatCounts(
        IReadOnlyDictionary<string, int> counts,
        Func<string, string> getName)
    {
        if (counts == null || counts.Count == 0)
            return "—";
        return string.Join(", ", counts
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Take(4)
            .Select(pair => $"{getName(pair.Key)} ×{pair.Value}"));
    }

    private void BuildRevealEntries()
    {
        revealEntries.Clear();
        if (currentBatchOutcome.Inventory.Products.Count != currentBatchOutcome.Draws.Count)
            throw new InvalidOperationException("Inventory product commits do not match the batch draws.");
        for (int productIndex = 0; productIndex < currentBatchOutcome.Draws.Count; productIndex++)
        {
            ProductDrawResult draw = currentBatchOutcome.Draws[productIndex];
            ProductInventoryCommit commit = currentBatchOutcome.Inventory.Products[productIndex];
            if (commit.Awards.Count != draw.Printings.Count)
                throw new InvalidOperationException("Inventory awards do not match the drawn cards.");
            for (int index = 0; index < draw.Printings.Count; index++)
            {
                DrawnPrinting drawn = draw.Printings[index];
                InventoryAward award = commit.Awards[index];
                if (!string.Equals(drawn.PrintingId, award.PrintingId, StringComparison.Ordinal))
                    throw new InvalidOperationException("Inventory award order does not match the draw result.");
                revealEntries.Add(new RevealEntry
                {
                    Printing = catalog.Printings[drawn.PrintingId],
                    Award = award,
                    ProductIndex = productIndex
                });
            }
        }
    }

    private void BeginRevealStage()
    {
        packParticles.Stop();
        revealParticles.Stop();
        packAnimating = false;
        packStage.style.display = DisplayStyle.None;
        revealStage.style.display = DisplayStyle.Flex;
        summaryStage.style.display = DisplayStyle.None;
        revealIndex = -1;
        revealImage.Unbind();
        revealAura.RemoveFromClassList("is-highlighted");
        revealAura.style.opacity = 0f;
        revealAura.style.scale = new Scale(Vector3.one);
        currentRevealHighlighted = false;
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
        packParticles.Stop();
        revealParticles.Stop();
        revealAnimation?.Pause();
        revealAnimation = null;
        revealAnimating = false;
        revealStage.style.display = DisplayStyle.None;
        summaryStage.style.display = DisplayStyle.Flex;
        ApplySummaryText();
        BuildSummaryList();
        if (currentBatchOutcome.Inventory.NewPrintingCount > 0)
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
        packParticles?.Stop();
        revealParticles?.Stop();
        packAnimation?.Pause();
        revealAnimation?.Pause();
        packAnimating = false;
        revealAnimating = false;
        currentBatchOutcome = null;
        revealEntries.Clear();
        revealIndex = -1;
        currentRevealHighlighted = false;
        openingPage.style.display = DisplayStyle.None;
        selectionPage.style.display = DisplayStyle.Flex;
        packStage.style.display = DisplayStyle.None;
        revealStage.style.display = DisplayStyle.None;
        summaryStage.style.display = DisplayStyle.None;
        packImage?.Unbind();
        revealImage?.Unbind();
        if (revealAura != null)
        {
            revealAura.RemoveFromClassList("is-highlighted");
            revealAura.style.opacity = 0f;
            revealAura.style.scale = new Scale(Vector3.one);
        }
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
        float duration = selectedTheme.PackTearDurationSeconds / UIFeedbackService.AnimationSpeed;
        packAnimation = packShell.schedule.Execute(() =>
        {
            float progress = Mathf.Clamp01((Time.realtimeSinceStartup - startedAt) / duration);
            float eased = Mathf.SmoothStep(0f, 1f, progress);
            packTearLine.style.width = Length.Percent(eased * 100f);
            float pulse = 1f + Mathf.Sin(progress * Mathf.PI * selectedTheme.PackPulseCycles) *
                (selectedTheme.PackPulseScale - 1f) * (1f - progress);
            packShell.style.scale = new Scale(new Vector3(pulse, pulse, 1f));
            packShell.style.opacity = 1f - Mathf.Max(0f, (progress - 0.72f) / 0.28f);
            if (progress < 1f)
                return;
            packAnimation?.Pause();
            packAnimation = null;
            completed();
        }).Every(16);
    }

    private void AnimatePackReady()
    {
        packAnimation?.Pause();
        if (UIFeedbackService.ReduceMotion)
        {
            packShell.style.opacity = 1f;
            packShell.style.scale = new Scale(Vector3.one);
            packThemeArtwork.style.opacity = 1f;
            return;
        }

        packShell.style.opacity = 0f;
        packShell.style.scale = new Scale(new Vector3(0.94f, 0.94f, 1f));
        packThemeArtwork.style.opacity = selectedThemeArtwork == null ? 0f : 0.35f;
        float startedAt = Time.realtimeSinceStartup;
        float duration = 0.24f / UIFeedbackService.AnimationSpeed;
        packAnimation = packShell.schedule.Execute(() =>
        {
            float progress = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Clamp01((Time.realtimeSinceStartup - startedAt) / duration));
            packShell.style.opacity = progress;
            float scale = Mathf.Lerp(0.94f, 1f, progress);
            packShell.style.scale = new Scale(new Vector3(scale, scale, 1f));
            packThemeArtwork.style.opacity = selectedThemeArtwork == null
                ? 0f
                : Mathf.Lerp(0.35f, 1f, progress);
            if (progress < 1f)
                return;
            packAnimation?.Pause();
            packAnimation = null;
        }).Every(16);
    }

    private void AnimateRevealCard(bool highlighted, bool shortTransition)
    {
        revealAnimation?.Pause();
        if (UIFeedbackService.ReduceMotion)
        {
            revealCard.style.opacity = 1f;
            revealCard.style.scale = new Scale(Vector3.one);
            revealAura.style.opacity = highlighted ? 0.42f : 0f;
            revealAura.style.scale = new Scale(Vector3.one);
            revealAnimating = false;
            revealButton.SetEnabled(true);
            return;
        }

        revealAnimating = true;
        revealButton.SetEnabled(false);
        revealCard.style.opacity = 0f;
        revealCard.style.scale = new Scale(new Vector3(
            selectedTheme.RevealStartScale,
            selectedTheme.RevealStartScale,
            1f));
        revealAura.style.opacity = 0f;
        revealAura.style.scale = new Scale(new Vector3(0.72f, 0.72f, 1f));
        float startedAt = Time.realtimeSinceStartup;
        float duration = (shortTransition
            ? Math.Min(selectedTheme.RevealDurationSeconds, 0.14f)
            : selectedTheme.RevealDurationSeconds) / UIFeedbackService.AnimationSpeed;
        revealAnimation = revealCard.schedule.Execute(() =>
        {
            float progress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Time.realtimeSinceStartup - startedAt) / duration));
            revealCard.style.opacity = progress;
            float rareWave = highlighted ? Mathf.Sin(progress * Mathf.PI) : 0f;
            float scale = Mathf.Lerp(selectedTheme.RevealStartScale, 1f, progress) +
                rareWave * (selectedTheme.RarePulseScale - 1f);
            revealCard.style.scale = new Scale(new Vector3(scale, scale, 1f));
            if (highlighted)
            {
                float auraScale = Mathf.Lerp(0.72f, 1f, progress) +
                    rareWave * (selectedTheme.RarePulseScale - 1f);
                revealAura.style.opacity = Mathf.Clamp01(0.42f * progress + 0.38f * rareWave);
                revealAura.style.scale = new Scale(new Vector3(auraScale, auraScale, 1f));
            }
            if (progress < 1f)
                return;
            revealAnimation?.Pause();
            revealAnimation = null;
            revealAnimating = false;
            revealAura.style.opacity = highlighted ? 0.42f : 0f;
            revealAura.style.scale = new Scale(Vector3.one);
            revealButton.SetEnabled(true);
        }).Every(16);
    }

    private void RefreshLocalizedChrome()
    {
        if (root == null)
            return;
        root.EnableInClassList("reduce-motion", UIFeedbackService.ReduceMotion);
        title.text = CardUiText.Get("gacha.title");
        subtitle.text = CardUiText.Get("gacha.subtitle");
        menuButton.text = CardUiText.Get("common.action.main_menu");
        manageContentButton.text = CardUiText.Get("common.action.manage_content");
        prepareButton.text = CardUiText.Get("gacha.action.open_one");
        prepareTenButton.text = CardUiText.Get("gacha.action.open_ten");
        tearButton.text = CardUiText.Get("gacha.action.tear");
        revealAllButton.text = CardUiText.Get("gacha.action.reveal_all");
        backToProductsButton.text = CardUiText.Get("gacha.action.all_products");
        openAgainButton.text = preparedProductCount == 1
            ? CardUiText.Get("gacha.action.open_another")
            : CardUiText.Get("gacha.action.open_ten_again");
        summaryProductsButton.text = CardUiText.Get("gacha.action.choose_another");
        oddsHeading.text = CardUiText.Get("gacha.odds.heading");
        if (selectedProduct != null)
            SelectProduct(selectedProduct);
        else if (products.Count == 0)
            SetStatus(CardUiText.Get("gacha.status.no_products"), true);
        if (currentBatchOutcome != null)
            ApplyRevealText();
        if (IsSummaryVisible)
        {
            ApplySummaryText();
            BuildSummaryList();
        }
        if (packStage.resolvedStyle.display == DisplayStyle.Flex)
        {
            packHint.text = preparedProductCount == 1
                ? CardUiText.Get("gacha.pack.hint")
                : CardUiText.Format("gacha.pack.batch_hint", preparedProductCount);
        }
        RefreshOpeningJournal();
        productList?.RefreshItems();
    }

    private static void OpenContentManagement()
    {
        ContentReturnNavigation.RememberCurrentScene();
        SceneManager.LoadScene("006_ContentScene");
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

    private void BuildRuleEvidence()
    {
        ruleSourceList.Clear();
        if (selectedProfile == null)
        {
            ruleEvidenceSummary.text = string.Empty;
            ruleSourceList.style.display = DisplayStyle.None;
            return;
        }

        string uiLanguage = ApplicationServices.Languages.UiLanguageId;
        if (selectedProfile.LastCheckedOn.HasValue)
        {
            ruleEvidenceSummary.text = CardUiText.Format(
                "gacha.rule.evidence.checked",
                selectedProfile.GetRegionName(uiLanguage),
                ConfidenceLabel(selectedProfile.Confidence),
                selectedProfile.LastCheckedOn.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }
        else
        {
            ruleEvidenceSummary.text = CardUiText.Get("gacha.rule.evidence.unverified");
        }

        int sourceIndex = 0;
        foreach (ProductRuleEvidence evidence in selectedProfile.Evidence)
        {
            sourceIndex++;
            string source = evidence.SourceReference;
            var button = new Button(() => OpenRuleSource(source))
            {
                text = CardUiText.Format("gacha.action.rule_source_number", sourceIndex, evidence.Title),
                tooltip = source
            };
            button.AddToClassList("gacha-source-button");
            ruleSourceList.Add(button);
        }
        ruleSourceList.style.display = ruleSourceList.childCount > 0
            ? DisplayStyle.Flex
            : DisplayStyle.None;
    }

    private static string ConfidenceLabel(ProductRuleConfidence confidence)
    {
        switch (confidence)
        {
            case ProductRuleConfidence.Authoritative:
                return CardUiText.Get("gacha.rule.confidence.authoritative");
            case ProductRuleConfidence.Corroborated:
                return CardUiText.Get("gacha.rule.confidence.corroborated");
            default:
                return CardUiText.Get("gacha.rule.confidence.unverified");
        }
    }

    private static void OpenRuleSource(string source)
    {
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
        revealProgress.text = preparedProductCount > 1
            ? CardUiText.Format(
                "gacha.reveal.batch_progress",
                entry.ProductIndex + 1,
                preparedProductCount,
                revealIndex + 1,
                revealEntries.Count)
            : CardUiText.Format("gacha.reveal.progress", revealIndex + 1, revealEntries.Count);
        revealButton.text = revealIndex == revealEntries.Count - 1
            ? CardUiText.Get("gacha.action.view_results")
            : CardUiText.Get("gacha.action.reveal_next");
    }

    private void ApplySummaryText()
    {
        if (currentBatchOutcome == null)
            return;
        summaryTitle.text = preparedProductCount == 1
            ? CardUiText.Get("gacha.summary.title")
            : CardUiText.Get("gacha.summary.batch_title");
        summaryMetadata.text = preparedProductCount == 1
            ? CardUiText.Format(
                "gacha.summary.metadata",
                revealEntries.Count,
                currentBatchOutcome.Inventory.NewPrintingCount,
                currentBatchOutcome.Inventory.ProductsOpened)
            : CardUiText.Format(
                "gacha.summary.batch_metadata",
                preparedProductCount,
                revealEntries.Count,
                currentBatchOutcome.Inventory.NewPrintingCount,
                currentBatchOutcome.Inventory.ProductsOpened);
    }
}
