using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Gacha.Application;
using Gacha.Domain;
using Gacha.Presentation;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public sealed class GachaViewController : MonoBehaviour
{
    private enum GachaPageState
    {
        Loading,
        Selection,
        Prepared,
        Opening,
        CommittedFailure,
        Revealing,
        Summary,
        Error
    }

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
    private string frozenContentLanguageId;
    private Texture2D selectedThemeArtwork;
    private GachaPageState pageState = GachaPageState.Loading;
    private bool pendingContentLanguageRefresh;
    private bool navigationRequested;
    private bool destroyed;
    private int pendingConfirmationCount;

    private VisualElement root;
    private VisualElement body;
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
    private VisualElement openingHistory;
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
    private MobilePageShell mobilePageShell;
    private MobileTopBar mobileTopBar;
    private MobilePrimaryNavigation primaryNavigation;
    private MobileConfirmationPresenter confirmationPresenter;
    private MobileActionControl menuAction;
    private MobileActionControl manageContentAction;
    private MobileActionControl prepareAction;
    private MobileActionControl prepareTenAction;
    private MobileActionControl tearAction;
    private MobileActionControl revealAction;
    private MobileActionControl revealAllAction;
    private MobileActionControl backToProductsAction;
    private MobileActionControl openAgainAction;
    private MobileActionControl summaryProductsAction;
    private MobileActionControl errorRetryAction;
    private MobileActionControl errorManageAction;
    private MobileActionControl errorHomeAction;
    private readonly List<MobileActionControl> ruleSourceActions = new List<MobileActionControl>();

    private AsyncCardImageView selectedImage;
    private AsyncCardImageView packImage;
    private AsyncCardImageView revealImage;
    private IVisualElementScheduledItem packAnimation;
    private IVisualElementScheduledItem revealAnimation;
    private ThemeParticleField packParticles;
    private ThemeParticleField revealParticles;
    private PlayerUiErrorPresenter errorPresenter;
    private bool shellInitialized;
    private bool contentLanguageSubscribed;

    public static IInventoryProgressStore InventoryStoreOverride { private get; set; }
    public static ICatalogProvider CatalogProviderOverride { private get; set; }
    public static Action<string> SceneLoaderOverride { private get; set; }

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
    public bool IsConfirmationVisible => confirmationPresenter?.IsVisible ?? false;
    public string CurrentStage => pageState.ToString();
    public string FrozenContentLanguageId => frozenContentLanguageId;

    public event Action<ProductDrawResult> PackOpened;
    public event Action<string> InitializationFailed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        InventoryStoreOverride = null;
        CatalogProviderOverride = null;
        SceneLoaderOverride = null;
    }

    private void Awake()
    {
        EnsureDocumentAssets();
    }

    private IEnumerator Start()
    {
        try
        {
            EnsureShell();
        }
        catch (Exception exception)
        {
            ShowInitializationFailure(PlayerUiErrorMapper.FromException(exception), exception);
            yield break;
        }
        while (LocalizationSettings.SelectedLocale == null)
            yield return null;
        RefreshLocalizedChrome();
        LoadCatalog(false);
    }

    private void OnDestroy()
    {
        destroyed = true;
        if (productList != null)
            productList.selectionChanged -= OnProductSelectionChanged;
        DisposeRuleSourceActions();
        confirmationPresenter?.Dispose();
        confirmationPresenter = null;
        primaryNavigation?.Dispose();
        primaryNavigation = null;
        DisposeAction(ref menuAction);
        DisposeAction(ref manageContentAction);
        DisposeAction(ref prepareAction);
        DisposeAction(ref prepareTenAction);
        DisposeAction(ref tearAction);
        DisposeAction(ref revealAction);
        DisposeAction(ref revealAllAction);
        DisposeAction(ref backToProductsAction);
        DisposeAction(ref openAgainAction);
        DisposeAction(ref summaryProductsAction);
        DisposeAction(ref errorRetryAction);
        DisposeAction(ref errorManageAction);
        DisposeAction(ref errorHomeAction);
        mobilePageShell?.Dispose();
        mobilePageShell = null;
        errorPresenter?.Dispose();
        errorPresenter = null;
        if (ApplicationServices.IsConfigured)
        {
            ApplicationServices.Languages.UiLanguageChanged -= OnUiLanguageChanged;
            ApplicationServices.Languages.ContentLanguageChanged -= OnContentLanguageChanged;
        }
        LocalizationSettings.SelectedLocaleChanged -= OnSelectedLocaleChanged;

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

    private void OnApplicationPause(bool paused)
    {
        if (paused)
            CompleteActiveAnimationsForPause();
    }

    public bool TryInitialize()
    {
        if (IsReady)
            return true;

        try
        {
            EnsureShell();
            return LoadCatalog(false);
        }
        catch (Exception exception)
        {
            ShowInitializationFailure(PlayerUiErrorMapper.FromException(exception), exception);
            return false;
        }
    }

    public bool RetryInitialization()
    {
        if (!shellInitialized)
            return TryInitialize();
        UIFeedbackService.Play(FeedbackCue.ButtonClick);
        return LoadCatalog(true);
    }

    private void EnsureShell()
    {
        if (shellInitialized)
            return;
        GameApplicationBootstrap.EnsureConfigured();
        EnsureDocumentAssets();
        if (uiDocument == null || uiDocument.panelSettings == null)
            throw new InvalidOperationException("The pack opening UI document is not configured.");
        root = uiDocument.rootVisualElement.Q<VisualElement>("gacha-opening");
        if (root == null)
            throw new InvalidOperationException("GachaView.uxml is not attached to the UIDocument.");
        HideLegacyCanvas();
        body = root.Q<VisualElement>("gacha-body");
        if (body == null)
            throw new InvalidOperationException("GachaView.uxml is missing its mobile body.");
        body.RemoveFromHierarchy();
        mobilePageShell = new MobilePageShell("gacha-opening-page-shell");
        mobilePageShell.Root.AddToClassList("gacha-opening");
        mobileTopBar = new MobileTopBar(string.Empty, string.Empty);
        mobileTopBar.Title.name = "gacha-title";
        mobileTopBar.Subtitle.name = "gacha-subtitle";
        mobilePageShell.HeaderSlot.Add(mobileTopBar.Root);
        mobilePageShell.ContentSlot.Add(body);
        root.Clear();
        root.Add(mobilePageShell.Root);
        QueryVisualElements();
        ConfigureActions();
        menuAction = new MobileActionControl(
            "gacha-menu-button",
            string.Empty,
            () => NavigatePrimary(MobileDestination.Home),
            MobileActionTone.Quiet);
        mobileTopBar.AddAction(menuAction);
        primaryNavigation = new MobilePrimaryNavigation(
            MobileDestination.Gacha,
            NavigatePrimary);
        mobilePageShell.BottomNavigationSlot.Add(primaryNavigation.BottomNavigation.Root);
        confirmationPresenter = new MobileConfirmationPresenter();
        mobilePageShell.ModalLayer.Add(confirmationPresenter.Root);
        errorPresenter = new PlayerUiErrorPresenter(
            Required<VisualElement>("gacha-error-panel"),
            Required<Label>("gacha-error-title"),
            Required<Label>("gacha-error-body"),
            errorRetryAction.Root,
            errorManageAction.Root,
            errorHomeAction.Root);
        ConfigureProductList();
        ApplicationServices.Languages.UiLanguageChanged += OnUiLanguageChanged;
        LocalizationSettings.SelectedLocaleChanged += OnSelectedLocaleChanged;
        shellInitialized = true;
        RefreshLocalizedChrome();
        pageState = GachaPageState.Loading;
        ApplyActionAvailability();
        SetStatus(CardUiText.Get("common.status.loading"), false);
    }

    private bool LoadCatalog(bool forceReload)
    {
        IsReady = false;
        InitializationError = null;
        pageState = GachaPageState.Loading;
        pendingConfirmationCount = 0;
        confirmationPresenter?.Hide();
        errorPresenter.Hide();
        ApplyActionAvailability();
        SetStatus(CardUiText.Get("common.status.loading"), false);
        try
        {
            CatalogLoadResult load = CatalogProviderOverride?.Load() ??
                                     ApplicationServices.Catalog.EnsureLoaded(forceReload);
            if (!load.Succeeded)
            {
                ShowInitializationFailure(PlayerUiErrorMapper.FromCatalog(load), load.ErrorMessage);
                return false;
            }
            if (!ApplicationServices.HasContentImages)
            {
                ShowInitializationFailure(
                    PlayerUiErrorMapper.Create(PlayerUiErrorCode.ServiceUnavailable),
                    "The installed content image service is unavailable.");
                return false;
            }

            catalog = load.Catalog;
            ApplicationServices.Languages.RefreshContentLanguage(catalog);
            if (textureCache == null)
            {
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
            }

            IsReady = true;
            RebuildProducts();
            RefreshLocalizedChrome();
            ShowSelectionPage();
            if (!contentLanguageSubscribed)
            {
                ApplicationServices.Languages.ContentLanguageChanged += OnContentLanguageChanged;
                contentLanguageSubscribed = true;
            }
            InitializationError = null;
            Debug.Log($"Pack opening ready: {products.Count} installed products.");
            return true;
        }
        catch (Exception exception)
        {
            ShowInitializationFailure(PlayerUiErrorMapper.FromException(exception), exception);
            return false;
        }
    }

    private void ShowInitializationFailure(PlayerUiError error, object developerDetail)
    {
        InitializationError = developerDetail?.ToString() ?? "Gacha initialization failed.";
        IsReady = false;
        pageState = GachaPageState.Error;
        pendingConfirmationCount = 0;
        confirmationPresenter?.Hide();
        if (selectionPage != null) selectionPage.style.display = DisplayStyle.None;
        if (openingPage != null) openingPage.style.display = DisplayStyle.None;
        if (manageContentAction != null) manageContentAction.Root.style.display = DisplayStyle.None;
        SetStatus(string.Empty, false);
        errorPresenter?.Show(error);
        ApplyActionAvailability();
        Debug.LogWarning("Gacha content could not be initialized: " + InitializationError);
        InitializationFailed?.Invoke(InitializationError);
    }

    public void OnOpenPack()
    {
        RequestOpenConfirmation(1);
    }

    public bool PrepareSelectedProduct()
    {
        return PrepareSelectedBatch(1);
    }

    public bool PrepareTenProducts()
    {
        return PrepareSelectedBatch(10);
    }

    public bool RequestOpenConfirmation(int productCount)
    {
        if (destroyed || !IsReady || selectedProduct == null || selectedProfile == null ||
            (pageState != GachaPageState.Selection && pageState != GachaPageState.Summary) ||
            (productCount != 1 && productCount != 10))
            return false;

        pendingConfirmationCount = productCount;
        string selectedId = selectedProduct.Id;
        string contentLanguageId = ApplicationServices.Languages.ContentLanguage.ResolvedLanguageId;
        GachaPageState requestedFrom = pageState;
        string productName = selectedProduct.GetDisplayName(contentLanguageId);
        confirmationPresenter.Show(
            CardUiText.Get("gacha.confirm.title"),
            CardUiText.Format(
                "gacha.confirm.body",
                productCount,
                productName,
                contentLanguageId,
                RuleTrustLabel(selectedProfile.Trust)),
            CardUiText.Get("gacha.action.confirm_open"),
            CardUiText.Get("common.action.cancel"),
            () =>
            {
                pendingConfirmationCount = 0;
                if (destroyed || pageState != requestedFrom ||
                    selectedProduct == null || !string.Equals(selectedProduct.Id, selectedId, StringComparison.Ordinal) ||
                    !string.Equals(
                        ApplicationServices.Languages.ContentLanguage.ResolvedLanguageId,
                        contentLanguageId,
                        StringComparison.OrdinalIgnoreCase))
                    return;
                PrepareSelectedBatch(productCount);
            },
            () => pendingConfirmationCount = 0,
            false);
        return true;
    }

    private bool PrepareSelectedBatch(int productCount)
    {
        if (!IsReady || selectedProduct == null || selectedProfile == null || packAnimating ||
            (pageState != GachaPageState.Selection && pageState != GachaPageState.Summary))
            return false;

        pendingConfirmationCount = 0;
        preparedProductCount = productCount;
        frozenContentLanguageId = ApplicationServices.Languages.ContentLanguage.ResolvedLanguageId;
        pageState = GachaPageState.Prepared;
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
        tearAction.SetEnabled(true);
        packTitle.text = productCount == 1
            ? DisplayName(selectedProduct, frozenContentLanguageId)
            : CardUiText.Format(
                "gacha.pack.batch_title",
                productCount,
                DisplayName(selectedProduct, frozenContentLanguageId));
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
        ApplyActionAvailability();
        AnimatePackReady();
        return true;
    }

    public bool TearPack()
    {
        if (!IsReady || selectedProduct == null || pageState != GachaPageState.Prepared ||
            packAnimating || currentBatchOutcome != null)
            return false;

        ProductOpeningBatchOutcome committedOutcome;
        try
        {
            pageState = GachaPageState.Opening;
            ApplyActionAvailability();
            committedOutcome = openingService.OpenBatch(selectedProduct.Id, preparedProductCount);
        }
        catch (Exception exception)
        {
            currentBatchOutcome = null;
            pageState = GachaPageState.Prepared;
            ApplyActionAvailability();
            SetStatus(PlayerUiErrorText.Body(PlayerUiErrorMapper.FromException(exception)), true);
            Debug.LogWarning($"Pack opening failed: {exception.Message}");
            InitializationFailed?.Invoke(exception.Message);
            UIFeedbackService.Play(FeedbackCue.Error);
            return false;
        }

        // OpenBatch is the irreversible inventory boundary. Once it returns, never clear the
        // outcome or return to Prepared: presentation/event failures must not allow a second draw.
        currentBatchOutcome = committedOutcome;
        tearAction.SetEnabled(false);
        try
        {
            BuildRevealEntries();
        }
        catch (Exception exception)
        {
            ShowCommittedPresentationFailure(exception);
            return true;
        }

        NotifyPackOpenedSafely();
        try
        {
            RefreshOpeningJournal();
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Pack journal refresh failed after inventory commit: {exception}");
        }

        packAnimating = true;
        UIFeedbackService.Play(FeedbackCue.PackOpen, selectedTheme.PackOpenAudioKey, true);
        AnimatePackTear(BeginRevealStage);
        return true;
    }

    private void NotifyPackOpenedSafely()
    {
        Delegate[] subscribers = PackOpened?.GetInvocationList();
        if (subscribers == null)
            return;

        foreach (ProductDrawResult draw in currentBatchOutcome.Draws)
        {
            foreach (Delegate subscriber in subscribers)
            {
                try
                {
                    ((Action<ProductDrawResult>)subscriber).Invoke(draw);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"Pack opened subscriber failed after inventory commit: {exception}");
                }
            }
        }
    }

    private void ShowCommittedPresentationFailure(Exception exception)
    {
        packAnimation?.Pause();
        packAnimation = null;
        packParticles?.Stop();
        packAnimating = false;
        pageState = GachaPageState.CommittedFailure;
        SetStatus(PlayerUiErrorText.Body(PlayerUiErrorMapper.FromException(exception)), true);
        ApplyActionAvailability();
        Debug.LogWarning($"Pack presentation failed after inventory commit: {exception}");
        InitializationFailed?.Invoke(exception.Message);
        UIFeedbackService.Play(FeedbackCue.Error);
    }

    public bool RevealNextCard()
    {
        if (pageState != GachaPageState.Revealing || currentBatchOutcome == null ||
            packAnimating || revealAnimating)
            return false;
        if (revealIndex >= revealEntries.Count - 1)
        {
            ShowSummary();
            return true;
        }

        revealIndex++;
        RevealEntry entry = revealEntries[revealIndex];
        revealImage.Bind(entry.Printing);
        revealName.text = DisplayName(entry.Printing, entry.Printing.Identity.LanguageId);
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
        revealAction.SetLabel(revealIndex == revealEntries.Count - 1
            ? CardUiText.Get("gacha.action.view_results")
            : CardUiText.Get("gacha.action.reveal_next"));

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
        if (pageState != GachaPageState.Revealing || currentBatchOutcome == null ||
            packAnimating || revealEntries.Count == 0 || IsSummaryVisible)
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
        NavigatePrimary(MobileDestination.Home);
    }

    private void NavigatePrimary(MobileDestination destination)
    {
        if (destination == MobileDestination.Gacha || navigationRequested || destroyed)
            return;

        navigationRequested = true;
        primaryNavigation?.SetPending(destination);
        ApplyActionAvailability();
        string sceneName = MobilePrimaryNavigation.SceneName(destination);
        if (destination == MobileDestination.Content)
            ContentReturnNavigation.RememberCurrentScene();
        else
            ContentReturnNavigation.Clear();

        try
        {
            if (SceneLoaderOverride != null)
                SceneLoaderOverride(sceneName);
            else if (GameManager.Instance != null && GameManager.Instance.loadManager != null)
                GameManager.Instance.loadManager.LoadScene(sceneName);
            else
                SceneManager.LoadScene(sceneName);
        }
        catch
        {
            navigationRequested = false;
            primaryNavigation?.ClearPending(MobileDestination.Gacha);
            ApplyActionAvailability();
            throw;
        }
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
        openingHistory = Required<VisualElement>("opening-history");
        title = mobileTopBar.Title;
        subtitle = mobileTopBar.Subtitle;
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
        packParticles?.Dispose();
        revealParticles?.Dispose();
        packParticles = new ThemeParticleField(packParticleLayer);
        revealParticles = new ThemeParticleField(revealParticleLayer);
    }

    private void ConfigureProductList()
    {
        productList.virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight;
        productList.selectionType = SelectionType.Single;
        productList.makeItem = MakeProductRow;
        productList.bindItem = BindProductRow;
        productList.unbindItem = UnbindProductRow;
        productList.destroyItem = DestroyProductRow;
        productList.selectionChanged += OnProductSelectionChanged;
    }

    private void ConfigureActions()
    {
        manageContentAction = new MobileActionControl(
            Required<VisualElement>("gacha-manage-content-button"),
            OpenContentManagement);
        errorRetryAction = new MobileActionControl(
            Required<VisualElement>("gacha-error-retry"),
            () => RetryInitialization());
        errorManageAction = new MobileActionControl(
            Required<VisualElement>("gacha-error-manage"),
            OpenContentManagement);
        errorHomeAction = new MobileActionControl(
            Required<VisualElement>("gacha-error-home"),
            MenuBtnClick);
        prepareAction = new MobileActionControl(
            Required<VisualElement>("prepare-pack-button"),
            () => RequestOpenConfirmation(1));
        prepareTenAction = new MobileActionControl(
            Required<VisualElement>("prepare-ten-button"),
            () => RequestOpenConfirmation(10));
        tearAction = new MobileActionControl(
            Required<VisualElement>("tear-pack-button"),
            () => TearPack());
        revealAction = new MobileActionControl(
            Required<VisualElement>("reveal-next-button"),
            () => RevealNextCard());
        revealAllAction = new MobileActionControl(
            Required<VisualElement>("reveal-all-button"),
            () => RevealAllCards());
        backToProductsAction = new MobileActionControl(
            Required<VisualElement>("back-to-products-button"),
            () =>
        {
            UIFeedbackService.Play(FeedbackCue.Back);
            ShowSelectionPage();
        });
        openAgainAction = new MobileActionControl(
            Required<VisualElement>("open-again-button"),
            RequestRepeatOpening);
        summaryProductsAction = new MobileActionControl(
            Required<VisualElement>("summary-products-button"),
            () =>
        {
            UIFeedbackService.Play(FeedbackCue.Back);
            ShowSelectionPage();
        });
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
            prepareAction.SetEnabled(false);
            prepareTenAction.SetEnabled(false);
            SetStatus(CardUiText.Get("gacha.status.no_products"), true);
            manageContentAction.Root.style.display = DisplayStyle.Flex;
            RefreshOpeningJournal();
            ApplyActionAvailability();
            return;
        }

        manageContentAction.Root.style.display = DisplayStyle.None;
        SelectProduct(next);
        int index = products.IndexOf(next);
        productList.SetSelectionWithoutNotify(new[] { index });
        RefreshOpeningJournal();
        ApplyActionAvailability();
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
        prepareAction.SetEnabled(true);
        prepareTenAction.SetEnabled(true);
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
        pageState = GachaPageState.Revealing;
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
        revealAction.SetLabel(CardUiText.Get("gacha.action.reveal_first"));
        revealAction.SetEnabled(true);
        revealAllAction.SetLabel(CardUiText.Get("gacha.action.reveal_all"));
        revealAllAction.SetEnabled(true);
        ApplyActionAvailability();
    }

    private void ShowSummary()
    {
        packParticles.Stop();
        revealParticles.Stop();
        revealAnimation?.Pause();
        revealAnimation = null;
        revealAnimating = false;
        pageState = GachaPageState.Summary;
        revealStage.style.display = DisplayStyle.None;
        summaryStage.style.display = DisplayStyle.Flex;
        ApplySummaryText();
        BuildSummaryList();
        ApplyActionAvailability();
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
            var name = new Label(DisplayName(entry.Printing, entry.Printing.Identity.LanguageId));
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
        pendingConfirmationCount = 0;
        confirmationPresenter?.Hide();
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
        pageState = GachaPageState.Selection;
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
        frozenContentLanguageId = null;
        if (pendingContentLanguageRefresh && IsReady && catalog != null)
        {
            pendingContentLanguageRefresh = false;
            RebuildProducts();
            RefreshLocalizedChrome();
        }
        ApplyActionAvailability();
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
            ApplyActionAvailability();
            return;
        }

        revealAnimating = true;
        ApplyActionAvailability();
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
            ApplyActionAvailability();
        }).Every(16);
    }

    private void RefreshLocalizedChrome()
    {
        if (root == null)
            return;
        root.EnableInClassList("reduce-motion", UIFeedbackService.ReduceMotion);
        title.text = CardUiText.Get("gacha.title");
        subtitle.text = CardUiText.Get("gacha.subtitle");
        menuAction?.SetLabel(CardUiText.Get("common.action.main_menu"));
        manageContentAction?.SetLabel(CardUiText.Get("common.action.manage_content"));
        errorPresenter?.RefreshLanguage();
        prepareAction?.SetLabel(CardUiText.Get("gacha.action.open_one"));
        prepareTenAction?.SetLabel(CardUiText.Get("gacha.action.open_ten"));
        tearAction?.SetLabel(CardUiText.Get("gacha.action.tear"));
        revealAllAction?.SetLabel(CardUiText.Get("gacha.action.reveal_all"));
        backToProductsAction?.SetLabel(CardUiText.Get("gacha.action.all_products"));
        openAgainAction?.SetLabel(preparedProductCount == 1
            ? CardUiText.Get("gacha.action.open_another")
            : CardUiText.Get("gacha.action.open_ten_again"));
        summaryProductsAction?.SetLabel(CardUiText.Get("gacha.action.choose_another"));
        primaryNavigation?.RefreshText();
        oddsHeading.text = CardUiText.Get("gacha.odds.heading");
        if (selectedProduct != null)
            SelectProduct(selectedProduct);
        else if (IsReady && products.Count == 0)
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
        if (openingService != null)
            RefreshOpeningJournal();
        productList?.RefreshItems();
        if (confirmationPresenter?.IsVisible == true && pendingConfirmationCount > 0)
            RequestOpenConfirmation(pendingConfirmationCount);
        ApplyActionAvailability();
    }

    private void OpenContentManagement()
    {
        NavigatePrimary(MobileDestination.Content);
    }

    private void RequestRepeatOpening()
    {
        int count = preparedProductCount;
        if (pendingContentLanguageRefresh)
            ShowSelectionPage();
        RequestOpenConfirmation(count);
    }

    private void OnUiLanguageChanged(string languageId)
    {
        RefreshLocalizedChrome();
    }

    private void OnSelectedLocaleChanged(Locale locale)
    {
        RefreshLocalizedChrome();
    }

    private void OnContentLanguageChanged(ContentLanguageSelection selection)
    {
        if (!IsReady || catalog == null)
            return;
        pendingConfirmationCount = 0;
        confirmationPresenter?.Hide();
        if (pageState == GachaPageState.Opening ||
            pageState == GachaPageState.Revealing ||
            pageState == GachaPageState.Summary)
        {
            pendingContentLanguageRefresh = true;
            return;
        }
        string previousProductId = selectedProduct?.Id;
        productId = previousProductId;
        ShowSelectionPage();
        RebuildProducts();
        RefreshLocalizedChrome();
    }

    private void BuildRuleEvidence()
    {
        DisposeRuleSourceActions();
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
            var actionRoot = new VisualElement { name = $"gacha-rule-source-{sourceIndex}" };
            actionRoot.AddToClassList("gacha-source-button");
            var label = new Label
            {
                text = CardUiText.Format("gacha.action.rule_source_number", sourceIndex, evidence.Title),
                pickingMode = PickingMode.Ignore
            };
            label.AddToClassList("gacha-source-button__label");
            actionRoot.Add(label);
            var action = new MobileActionControl(
                actionRoot,
                () => OpenRuleSource(source),
                fallbackLabelClass: "gacha-source-button__label");
            ruleSourceActions.Add(action);
            ruleSourceList.Add(action.Root);
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
            ? DisplayName(definition, printing.Identity.LanguageId)
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

    private static string DisplayName(Definition definition, string contentLanguageId)
    {
        return definition.GetDisplayName(contentLanguageId);
    }

    private static string RuleTrustLabel(ProductRuleTrust trust)
    {
        switch (trust)
        {
            case ProductRuleTrust.HistoricallyVerified:
                return CardUiText.Get("gacha.rule.verified");
            case ProductRuleTrust.SourceInformedSimulation:
                return CardUiText.Get("gacha.rule.sourced_simulation");
            default:
                return CardUiText.Get("gacha.rule.simulation");
        }
    }

    private void ApplyActionAvailability()
    {
        bool available = !destroyed && !navigationRequested;
        menuAction?.SetEnabled(available);
        manageContentAction?.SetEnabled(available);
        errorRetryAction?.SetEnabled(available && pageState == GachaPageState.Error);
        errorManageAction?.SetEnabled(available && pageState == GachaPageState.Error);
        errorHomeAction?.SetEnabled(available && pageState == GachaPageState.Error);
        prepareAction?.SetEnabled(
            available && pageState == GachaPageState.Selection && selectedProduct != null);
        prepareTenAction?.SetEnabled(
            available && pageState == GachaPageState.Selection && selectedProduct != null);
        tearAction?.SetEnabled(available && pageState == GachaPageState.Prepared && !packAnimating);
        revealAction?.SetEnabled(available && pageState == GachaPageState.Revealing && !revealAnimating);
        revealAllAction?.SetEnabled(available && pageState == GachaPageState.Revealing && !revealAnimating);
        backToProductsAction?.SetEnabled(
            available && (pageState == GachaPageState.Prepared ||
                          pageState == GachaPageState.CommittedFailure ||
                          pageState == GachaPageState.Revealing));
        openAgainAction?.SetEnabled(available && pageState == GachaPageState.Summary);
        summaryProductsAction?.SetEnabled(available && pageState == GachaPageState.Summary);
    }

    private void CompleteActiveAnimationsForPause()
    {
        packAnimation?.Pause();
        packAnimation = null;
        revealAnimation?.Pause();
        revealAnimation = null;
        packParticles?.Stop();
        revealParticles?.Stop();

        if (pageState == GachaPageState.Opening && currentBatchOutcome != null)
        {
            packAnimating = false;
            BeginRevealStage();
            return;
        }

        packAnimating = false;
        revealAnimating = false;
        if (packShell != null)
        {
            packShell.style.opacity = 1f;
            packShell.style.scale = new Scale(Vector3.one);
        }
        if (revealCard != null)
        {
            revealCard.style.opacity = 1f;
            revealCard.style.scale = new Scale(Vector3.one);
        }
        ApplyActionAvailability();
    }

    private void HideLegacyCanvas()
    {
        foreach (GameObject sceneRoot in gameObject.scene.GetRootGameObjects())
        {
            foreach (Canvas canvas in sceneRoot.GetComponentsInChildren<Canvas>(true))
            {
                if (canvas != null && canvas.gameObject.scene == gameObject.scene)
                    canvas.gameObject.SetActive(false);
            }
        }
    }

    private void DisposeRuleSourceActions()
    {
        foreach (MobileActionControl action in ruleSourceActions)
            action.Dispose();
        ruleSourceActions.Clear();
    }

    private static void DisposeAction(ref MobileActionControl action)
    {
        action?.Dispose();
        action = null;
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
        revealAllAction.SetLabel(CardUiText.Get("gacha.action.reveal_all"));
        if (revealIndex < 0 || revealIndex >= revealEntries.Count)
        {
            revealName.text = CardUiText.Get("gacha.reveal.ready");
            revealMetadata.text = CardUiText.Get("gacha.reveal.one_at_time");
            revealNewBadge.text = string.Empty;
            revealProgress.text = CardUiText.Format("gacha.reveal.pending_progress", revealEntries.Count);
            revealAction.SetLabel(CardUiText.Get("gacha.action.reveal_first"));
            return;
        }

        RevealEntry entry = revealEntries[revealIndex];
        revealName.text = DisplayName(entry.Printing, entry.Printing.Identity.LanguageId);
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
        revealAction.SetLabel(revealIndex == revealEntries.Count - 1
            ? CardUiText.Get("gacha.action.view_results")
            : CardUiText.Get("gacha.action.reveal_next"));
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
