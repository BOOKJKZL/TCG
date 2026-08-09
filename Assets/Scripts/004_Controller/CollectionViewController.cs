using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Gacha.Application;
using Gacha.Domain;
using Gacha.Pokemon.Presentation;
using Gacha.Presentation;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public sealed class CollectionViewController : MonoBehaviour
{
    private const int SearchDebounceMilliseconds = 120;
    private static readonly IComparer<string> CardNumberComparer =
        Comparer<string>.Create(CompareCardNumbers);

    private enum CardSortMode
    {
        Number,
        Name,
        Rarity
    }

    private sealed class SetRow
    {
        public AsyncCardImageView Image;
        public Label Name;
        public Label Metadata;
        public MobileActionControl Action;
        public SetDefinition Set;
    }

    private sealed class CardTile
    {
        public VisualElement Root;
        public AsyncCardImageView Image;
        public Label Name;
        public Label Number;
        public Label Rarity;
        public Label Owned;
        public Label NewBadge;
        public MobileActionControl Action;
        public PrintingDefinition Printing;
    }

    private sealed class CardGridRow
    {
        public CardTile[] Tiles;
    }

    private sealed class CardGridLine
    {
        public CardGridLine(PrintingDefinition first, PrintingDefinition second)
        {
            First = first;
            Second = second;
        }

        public PrintingDefinition First { get; }
        public PrintingDefinition Second { get; }
    }

    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private VisualTreeAsset viewAsset;
    [SerializeField, Range(8, 96)] private int textureCacheCapacity = 32;

    private readonly List<SetDefinition> sets = new List<SetDefinition>();
    private readonly List<PrintingDefinition> cards = new List<PrintingDefinition>();
    private readonly List<CardGridLine> cardGridLines = new List<CardGridLine>();
    private readonly Dictionary<string, List<PrintingDefinition>> cardsBySet =
        new Dictionary<string, List<PrintingDefinition>>(StringComparer.Ordinal);
    private readonly HashSet<AsyncCardImageView> imageViews = new HashSet<AsyncCardImageView>();
    private readonly List<string> rarityFilterIds = new List<string>();

    private UniversalCatalog catalog;
    private ICollectionProgressStore collectionProgress;
    private CardTextureCache textureCache;
    private VisualElement browserRoot;
    private VisualElement body;
    private VisualElement setPage;
    private VisualElement cardPage;
    private VisualElement filterPanel;
    private ScrollView detailContent;
    private VisualElement detailLanguageSwitcher;
    private ListView setList;
    private ListView cardList;
    private Label browserStatus;
    private Label cardPageTitle;
    private Label cardCount;
    private Label detailName;
    private Label detailMetadata;
    private Label detailProgress;
    private Label detailNewBadge;
    private Label filterEmpty;
    private AsyncCardImageView detailImage;
    private VisualElement zeroContentPanel;
    private Label zeroContentText;
    private TextField searchField;
    private DropdownField rarityFilter;
    private DropdownField sortField;
    private SetDefinition currentSet;
    private PrintingDefinition currentDetailPrinting;
    private IVisualElementScheduledItem languageSwapAnimation;
    private IVisualElementScheduledItem filterAnimation;
    private IVisualElementScheduledItem searchRefresh;
    private PokemonPokedexController pokedexController;
    private string searchQuery = string.Empty;
    private string selectedRarityId;
    private bool ownedOnly;
    private bool newOnly;
    private CardSortMode sortMode;
    private bool updatingFilterControls;
    private bool shellInitialized;
    private bool contentLanguageSubscribed;
    private bool navigationRequested;
    private bool destroyed;
    private PlayerUiErrorPresenter errorPresenter;
    private MobilePageShell mobilePageShell;
    private MobileTopBar mobileTopBar;
    private MobilePrimaryNavigation primaryNavigation;
    private MobileSheetPresenter detailSheet;
    private MobileSheetPresenter filterSheet;
    private MobileActionControl menuAction;
    private MobileActionControl pokedexAction;
    private MobileActionControl manageContentAction;
    private MobileActionControl backToSetsAction;
    private MobileActionControl closeDetailsAction;
    private MobileActionControl ownedOnlyAction;
    private MobileActionControl newOnlyAction;
    private MobileActionControl clearFiltersAction;
    private MobileActionControl openFiltersAction;
    private MobileActionControl closeFiltersAction;
    private MobileActionControl errorRetryAction;
    private MobileActionControl errorManageAction;
    private MobileActionControl errorHomeAction;
    private readonly List<MobileActionControl> detailLanguageActions = new List<MobileActionControl>();

    public static ICollectionProgressStore CollectionProgressStoreOverride { private get; set; }
    public static UniversalCatalog CatalogOverride { private get; set; }
    public static ICatalogProvider CatalogProviderOverride { private get; set; }
    public static Action<string> SceneLoaderOverride { private get; set; }

    public bool IsReady { get; private set; }
    public string InitializationError { get; private set; }
    public int InstalledSetCount => sets.Count;
    public int CurrentCardCount => cards.Count;
    public int CurrentSetTotalCount => CurrentSetCards.Count;
    public int OwnedCardCount => CurrentSetCards.Count(printing => Progress(printing).IsOwned);
    public int NewCardCount => CurrentSetCards.Count(printing => Progress(printing).IsNew);
    public int CachedTextureCount => textureCache?.Count ?? 0;
    public long CachedTextureBytes => textureCache?.DecodedBytes ?? 0L;
    public long CachedTextureBudgetBytes => textureCache?.MaximumDecodedBytes ?? 0L;
    public string DetailPrintingId => currentDetailPrinting?.Id;
    public int DetailLanguageCount => currentDetailPrinting == null
        ? 0
        : catalog?.PrintingLanguages.GetGroup(currentDetailPrinting.Id)?.AvailableLanguageIds.Count ?? 0;
    public bool HasDetailLanguageSwitcher => currentDetailPrinting != null &&
        catalog?.PrintingLanguages.GetGroup(currentDetailPrinting.Id)?.HasMultipleLanguages == true;
    public bool NavigationPending => navigationRequested;

    private IReadOnlyList<PrintingDefinition> CurrentSetCards =>
        currentSet != null && cardsBySet.TryGetValue(currentSet.Id, out List<PrintingDefinition> setCards)
            ? setCards
            : Array.Empty<PrintingDefinition>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        CollectionProgressStoreOverride = null;
        CatalogOverride = null;
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
        errorPresenter?.Dispose();
        errorPresenter = null;
        if (ApplicationServices.IsConfigured)
        {
            ApplicationServices.Languages.UiLanguageChanged -= OnUiLanguageChanged;
            ApplicationServices.Languages.ContentLanguageChanged -= OnContentLanguageChanged;
        }
        LocalizationSettings.SelectedLocaleChanged -= OnSelectedLocaleChanged;
        languageSwapAnimation?.Pause();
        languageSwapAnimation = null;
        filterAnimation?.Pause();
        filterAnimation = null;
        CancelSearchRefresh();
        foreach (AsyncCardImageView imageView in imageViews.ToArray())
            imageView.Dispose();
        imageViews.Clear();
        textureCache?.Dispose();
        textureCache = null;
        DisposeDetailLanguageActions();
        detailSheet?.Dispose();
        detailSheet = null;
        filterSheet?.Dispose();
        filterSheet = null;
        primaryNavigation?.Dispose();
        primaryNavigation = null;
        DisposeAction(ref menuAction);
        DisposeAction(ref pokedexAction);
        DisposeAction(ref manageContentAction);
        DisposeAction(ref backToSetsAction);
        DisposeAction(ref closeDetailsAction);
        DisposeAction(ref ownedOnlyAction);
        DisposeAction(ref newOnlyAction);
        DisposeAction(ref clearFiltersAction);
        DisposeAction(ref openFiltersAction);
        DisposeAction(ref closeFiltersAction);
        DisposeAction(ref errorRetryAction);
        DisposeAction(ref errorManageAction);
        DisposeAction(ref errorHomeAction);
        mobilePageShell?.Dispose();
        mobilePageShell = null;
    }

    private void OnApplicationPause(bool paused)
    {
        if (!paused)
            return;
        CompleteTransientVisuals();
    }

    public bool OpenSet(string setId)
    {
        SetDefinition set = sets.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, setId, StringComparison.Ordinal));
        if (set == null)
            return false;

        currentSet = set;
        cardPageTitle.text = DisplayName(set);
        SetVisible(setPage, false);
        SetVisible(cardPage, true);
        ResetFilters(false);
        RebuildRarityFilter();
        ApplyFilters(false);
        HideDetails(false);
        ApplyActionAvailability();
        return true;
    }

    public bool ShowPrintingDetails(string printingId)
    {
        PrintingDefinition printing = null;
        if (catalog != null)
            catalog.Printings.TryGetValue(printingId ?? string.Empty, out printing);
        if (printing == null)
            return false;

        currentDetailPrinting = printing;
        filterSheet?.HideImmediately();
        RefreshPrintingDetails(printing, true);
        RebuildDetailLanguageSwitcher();
        detailSheet.Show(CardUiText.Get("collection.title"));
        ApplyActionAvailability();
        return true;
    }

    public bool SwitchDetailCardLanguage(string cardLanguageId)
    {
        if (currentDetailPrinting == null || catalog == null)
            return false;
        PrintingDefinition next = catalog.PrintingLanguages.Select(currentDetailPrinting.Id, cardLanguageId);
        if (next == null || string.Equals(next.Id, currentDetailPrinting.Id, StringComparison.Ordinal))
            return false;

        currentDetailPrinting = next;
        RefreshPrintingDetails(next, true);
        RebuildDetailLanguageSwitcher();
        UIFeedbackService.Play(FeedbackCue.CardFlip, true);
        AnimateLanguageSwap();
        return true;
    }

    public void MenuBtnClick()
    {
        UIFeedbackService.Play(FeedbackCue.Back);
        NavigatePrimary(MobileDestination.Home);
    }

    private void NavigatePrimary(MobileDestination destination)
    {
        if (destination == MobileDestination.Collection || navigationRequested || destroyed)
            return;

        navigationRequested = true;
        filterSheet?.HideImmediately();
        detailSheet?.HideImmediately();
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
            primaryNavigation?.ClearPending(MobileDestination.Collection);
            ApplyActionAvailability();
            throw;
        }
    }

    private void Initialize()
    {
        try
        {
            EnsureShell();
            LoadCatalog(false);
        }
        catch (Exception exception)
        {
            ShowInitializationFailure(PlayerUiErrorMapper.FromException(exception), exception);
        }
    }

    public bool RetryInitialization()
    {
        if (!shellInitialized)
        {
            Initialize();
            return IsReady;
        }
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
            throw new InvalidOperationException("The collection browser UI document is not configured.");
        browserRoot = uiDocument.rootVisualElement.Q<VisualElement>("collection-browser");
        if (browserRoot == null)
            throw new InvalidOperationException("CollectionView.uxml is not attached to the UIDocument.");
        HideLegacyCanvas();
        body = browserRoot.Q<VisualElement>("collection-body");
        if (body == null)
            throw new InvalidOperationException("CollectionView.uxml is missing its mobile body.");
        body.RemoveFromHierarchy();
        mobilePageShell = new MobilePageShell("collection-page-shell");
        mobilePageShell.Root.AddToClassList("collection-browser");
        mobileTopBar = new MobileTopBar(string.Empty, string.Empty);
        mobileTopBar.Title.name = "collection-title";
        mobileTopBar.Subtitle.name = "collection-subtitle";
        mobilePageShell.HeaderSlot.Add(mobileTopBar.Root);
        mobilePageShell.ContentSlot.Add(body);
        browserRoot.Clear();
        browserRoot.Add(mobilePageShell.Root);
        QueryVisualElements();
        filterPanel.RemoveFromHierarchy();
        filterSheet = new MobileSheetPresenter("collection-filter-sheet");
        filterSheet.Root.AddToClassList("collection-filter-sheet");
        filterSheet.DismissRequested += () => filterSheet.Hide();
        filterSheet.Content.Add(filterPanel);
        mobilePageShell.ModalLayer.Add(filterSheet.Root);
        detailContent.RemoveFromHierarchy();
        detailSheet = new MobileSheetPresenter("collection-details-panel");
        detailSheet.Root.AddToClassList("collection-detail-sheet");
        detailSheet.DismissRequested += () => HideDetails(true);
        detailSheet.Content.Add(detailContent);
        mobilePageShell.ModalLayer.Add(detailSheet.Root);
        ConfigureActions();
        menuAction = new MobileActionControl(
            "collection-menu-button",
            string.Empty,
            () => NavigatePrimary(MobileDestination.Home),
            MobileActionTone.Quiet);
        pokedexAction = new MobileActionControl(
            "collection-pokedex-button",
            string.Empty,
            () => pokedexController?.Open(),
            MobileActionTone.Standard);
        mobileTopBar.AddAction(pokedexAction);
        mobileTopBar.AddAction(menuAction);
        primaryNavigation = new MobilePrimaryNavigation(
            MobileDestination.Collection,
            NavigatePrimary);
        mobilePageShell.BottomNavigationSlot.Add(primaryNavigation.BottomNavigation.Root);
        errorPresenter = new PlayerUiErrorPresenter(
            Required<VisualElement>("collection-error-panel"),
            Required<Label>("collection-error-title"),
            Required<Label>("collection-error-body"),
            errorRetryAction.Root,
            errorManageAction.Root,
            errorHomeAction.Root);
        ConfigureLists();
        pokedexController = GetComponent<PokemonPokedexController>();
        if (pokedexController == null)
            pokedexController = gameObject.AddComponent<PokemonPokedexController>();
        pokedexController.Attach(
            uiDocument,
            ShowPrintingDetails,
            OpenContentManagement);
        ApplicationServices.Languages.UiLanguageChanged += OnUiLanguageChanged;
        LocalizationSettings.SelectedLocaleChanged += OnSelectedLocaleChanged;
        shellInitialized = true;
        RefreshLocalizedChrome();
        ApplyActionAvailability();
        SetBrowserStatus(CardUiText.Get("common.status.loading"), false);
    }

    private bool LoadCatalog(bool forceReload)
    {
        IsReady = false;
        InitializationError = null;
        errorPresenter.Hide();
        detailSheet?.HideImmediately();
        filterSheet?.HideImmediately();
        ApplyActionAvailability();
        SetBrowserStatus(CardUiText.Get("common.status.loading"), false);
        try
        {
            CatalogLoadResult load = null;
            if (CatalogOverride == null)
            {
                load = CatalogProviderOverride?.Load() ??
                       ApplicationServices.Catalog.EnsureLoaded(forceReload);
                if (!load.Succeeded)
                {
                    ShowInitializationFailure(PlayerUiErrorMapper.FromCatalog(load), load.ErrorMessage);
                    return false;
                }
            }
            if (!ApplicationServices.HasContentImages)
            {
                ShowInitializationFailure(
                    PlayerUiErrorMapper.Create(PlayerUiErrorCode.ServiceUnavailable),
                    "The installed content image service is unavailable.");
                return false;
            }

            catalog = CatalogOverride ?? load.Catalog;
            ApplicationServices.Languages.RefreshContentLanguage(catalog);
            collectionProgress ??= CollectionProgressStoreOverride ?? new PlayerCollectionProgressStore();
            if (textureCache == null)
            {
                textureCache = new CardTextureCache(ApplicationServices.Images, textureCacheCapacity);
                detailImage = Track(new AsyncCardImageView(textureCache));
                browserRoot.Q<VisualElement>("detail-art-slot").Add(detailImage.Element);
            }
            IsReady = true;
            BuildBrowseData();
            RefreshLocalizedChrome();
            ShowSets();
            ApplyActionAvailability();
            if (!contentLanguageSubscribed)
            {
                ApplicationServices.Languages.ContentLanguageChanged += OnContentLanguageChanged;
                contentLanguageSubscribed = true;
            }
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
        InitializationError = developerDetail?.ToString() ?? "Collection initialization failed.";
        IsReady = false;
        SetVisible(setPage, false);
        SetVisible(cardPage, false);
        SetVisible(zeroContentPanel, false);
        detailSheet?.HideImmediately();
        SetBrowserStatus(string.Empty, false);
        errorPresenter?.Show(error);
        ApplyActionAvailability();
        Debug.LogWarning("Collection browser could not be initialized: " + InitializationError);
    }

    private void QueryVisualElements()
    {
        setPage = Required<VisualElement>("set-page");
        cardPage = Required<VisualElement>("card-page");
        filterPanel = Required<VisualElement>("collection-filters");
        detailContent = Required<ScrollView>("detail-content");
        detailLanguageSwitcher = Required<VisualElement>("detail-language-switcher");
        setList = Required<ListView>("set-list");
        cardList = Required<ListView>("card-list");
        browserStatus = Required<Label>("browser-status");
        zeroContentPanel = Required<VisualElement>("collection-zero-content");
        zeroContentText = Required<Label>("collection-zero-content-text");
        cardPageTitle = Required<Label>("card-page-title");
        cardCount = Required<Label>("card-count");
        detailName = Required<Label>("detail-name");
        detailMetadata = Required<Label>("detail-metadata");
        detailProgress = Required<Label>("detail-progress");
        detailNewBadge = Required<Label>("detail-new-badge");
        filterEmpty = Required<Label>("filter-empty");
        searchField = Required<TextField>("card-search");
        rarityFilter = Required<DropdownField>("rarity-filter");
        sortField = Required<DropdownField>("card-sort");
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
        T element = browserRoot.Q<T>(name);
        if (element == null)
            throw new InvalidOperationException($"Collection browser element '{name}' is missing.");
        return element;
    }

    private void ConfigureLists()
    {
        setList.virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight;
        setList.selectionType = SelectionType.None;
        setList.makeItem = MakeSetRow;
        setList.bindItem = BindSetRow;
        setList.unbindItem = UnbindSetRow;
        setList.destroyItem = DestroyRow;

        cardList.virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight;
        cardList.selectionType = SelectionType.None;
        cardList.makeItem = MakeCardGridRow;
        cardList.bindItem = BindCardGridRow;
        cardList.unbindItem = UnbindCardGridRow;
        cardList.destroyItem = DestroyRow;
    }

    private void ConfigureActions()
    {
        manageContentAction = new MobileActionControl(
            Required<VisualElement>("collection-manage-content-button"),
            OpenContentManagement);
        errorRetryAction = new MobileActionControl(
            Required<VisualElement>("collection-error-retry"),
            () => RetryInitialization());
        errorManageAction = new MobileActionControl(
            Required<VisualElement>("collection-error-manage"),
            OpenContentManagement);
        errorHomeAction = new MobileActionControl(
            Required<VisualElement>("collection-error-home"),
            MenuBtnClick);
        backToSetsAction = new MobileActionControl(
            Required<VisualElement>("back-to-sets-button"),
            () =>
        {
            UIFeedbackService.Play(FeedbackCue.Back);
            ShowSets();
        });
        closeDetailsAction = new MobileActionControl(
            "details-close-button",
            string.Empty,
            () =>
        {
            UIFeedbackService.Play(FeedbackCue.Back);
            HideDetails(true);
        }, MobileActionTone.Quiet);
        detailSheet.Actions.Add(closeDetailsAction.Root);
        openFiltersAction = new MobileActionControl(
            Required<VisualElement>("open-filters-button"),
            () => filterSheet.Show(CardUiText.Get("collection.filters.title")));
        closeFiltersAction = new MobileActionControl(
            "close-filters-button",
            string.Empty,
            () => filterSheet.Hide(),
            MobileActionTone.Quiet);
        filterSheet.Actions.Add(closeFiltersAction.Root);
        ownedOnlyAction = new MobileActionControl(
            Required<VisualElement>("owned-only-button"),
            () =>
        {
            ownedOnly = !ownedOnly;
            UIFeedbackService.Play(FeedbackCue.Confirm);
            RefreshFilterControls();
            ApplyFilters(true);
        });
        newOnlyAction = new MobileActionControl(
            Required<VisualElement>("new-only-button"),
            () =>
        {
            newOnly = !newOnly;
            UIFeedbackService.Play(FeedbackCue.Confirm);
            RefreshFilterControls();
            ApplyFilters(true);
        });
        clearFiltersAction = new MobileActionControl(
            Required<VisualElement>("clear-filters-button"),
            () =>
        {
            UIFeedbackService.Play(FeedbackCue.Back);
            ResetFilters(true);
        });
        searchField.RegisterValueChangedCallback(evt =>
        {
            if (updatingFilterControls)
                return;
            searchQuery = evt.newValue?.Trim() ?? string.Empty;
            ScheduleSearchRefresh();
        });
        rarityFilter.RegisterValueChangedCallback(_ =>
        {
            if (updatingFilterControls)
                return;
            selectedRarityId = rarityFilter.index > 0 && rarityFilter.index < rarityFilterIds.Count
                ? rarityFilterIds[rarityFilter.index]
                : null;
            UIFeedbackService.Play(FeedbackCue.Confirm);
            ApplyFilters(true);
        });
        sortField.RegisterValueChangedCallback(_ =>
        {
            if (updatingFilterControls)
                return;
            sortMode = sortField.index >= 0 && sortField.index <= (int)CardSortMode.Rarity
                ? (CardSortMode)sortField.index
                : CardSortMode.Number;
            UIFeedbackService.Play(FeedbackCue.Confirm);
            ApplyFilters(true);
        });
    }

    private void BuildBrowseData()
    {
        string languageId = ApplicationServices.Languages.ContentLanguage.ResolvedLanguageId;
        sets.Clear();
        cardsBySet.Clear();
        foreach (SetDefinition set in catalog.Sets.Values
                     .OrderBy(value => value, new SetDefinitionComparer(SetSortMode.Generation, languageId)))
        {
            List<PrintingDefinition> setCards = catalog.GetPrintings(set.Id, languageId)
                .OrderBy(printing => printing.Identity.CardNumber, CardNumberComparer)
                .ThenBy(printing => printing.Identity.VariantId, StringComparer.Ordinal)
                .ToList();
            if (setCards.Count == 0)
                continue;
            sets.Add(set);
            cardsBySet[set.Id] = setCards;
        }

        setList.itemsSource = sets;
        setList.Rebuild();
        SetVisible(zeroContentPanel, sets.Count == 0);
        SetVisible(setList, sets.Count > 0);
    }

    private VisualElement MakeSetRow()
    {
        var root = new VisualElement();
        root.AddToClassList("set-row");
        AsyncCardImageView image = Track(new AsyncCardImageView(textureCache));
        image.Element.AddToClassList("set-row__cover");
        var copy = new VisualElement();
        copy.AddToClassList("browser-row__copy");
        var name = new Label();
        name.AddToClassList("browser-row__title");
        var metadata = new Label();
        metadata.AddToClassList("browser-row__metadata");
        copy.Add(name);
        copy.Add(metadata);
        root.Add(image.Element);
        root.Add(copy);
        var row = new SetRow { Image = image, Name = name, Metadata = metadata };
        row.Action = new MobileActionControl(
            root,
            () =>
            {
                if (row.Set == null || navigationRequested)
                    return;
                UIFeedbackService.Play(FeedbackCue.Confirm);
                OpenSet(row.Set.Id);
            },
            playFeedback: false,
            feedbackLabel: name);
        root.userData = row;
        return root;
    }

    private void BindSetRow(VisualElement element, int index)
    {
        if (index < 0 || index >= sets.Count)
            return;
        SetDefinition set = sets[index];
        var row = (SetRow)element.userData;
        row.Set = set;
        row.Action.SetEnabled(!navigationRequested);
        row.Name.text = DisplayName(set);
        int count = cardsBySet.TryGetValue(set.Id, out List<PrintingDefinition> setCards) ? setCards.Count : 0;
        int owned = setCards?.Count(printing => Progress(printing).IsOwned) ?? 0;
        int unseen = setCards?.Count(printing => Progress(printing).IsNew) ?? 0;
        string year = set.ReleaseDate?.Year.ToString() ?? "—";
        row.Metadata.text = CardUiText.Format(
            "collection.set.metadata",
            year,
            owned,
            count,
            unseen,
            ApplicationServices.Languages.ContentLanguage.ResolvedLanguageId);
        PrintingDefinition cover = setCards?.FirstOrDefault(printing => !string.IsNullOrWhiteSpace(printing.ImageRelativePath));
        if (cover != null)
            row.Image.Bind(cover);
        else
            row.Image.Unbind();
        element.tooltip = row.Name.text;
    }

    private static void UnbindSetRow(VisualElement element, int index)
    {
        if (element.userData is SetRow row)
        {
            row.Set = null;
            row.Action.SetEnabled(false);
            row.Image.Unbind();
        }
    }

    private VisualElement MakeCardGridRow()
    {
        var root = new VisualElement();
        root.AddToClassList("card-grid-row");
        CardTile first = MakeCardTile(0);
        CardTile second = MakeCardTile(1);
        root.Add(first.Root);
        root.Add(second.Root);
        var row = new CardGridRow { Tiles = new[] { first, second } };
        root.userData = row;
        return root;
    }

    private CardTile MakeCardTile(int slot)
    {
        var root = new VisualElement { name = "card-tile-" + slot };
        root.AddToClassList("card-tile");
        AsyncCardImageView image = Track(new AsyncCardImageView(textureCache));
        image.Element.AddToClassList("card-tile__image");
        var copy = new VisualElement();
        copy.AddToClassList("card-tile__copy");
        var name = new Label();
        name.AddToClassList("card-tile__name");
        var number = new Label();
        number.AddToClassList("card-tile__number");
        var rarity = new Label();
        rarity.AddToClassList("card-tile__rarity");
        var progress = new VisualElement();
        progress.AddToClassList("card-tile__progress");
        var owned = new Label();
        owned.AddToClassList("card-tile__owned");
        var newBadge = new Label();
        newBadge.AddToClassList("card-tile__new");
        progress.Add(newBadge);
        progress.Add(owned);
        copy.Add(name);
        copy.Add(number);
        copy.Add(rarity);
        root.Add(image.Element);
        root.Add(copy);
        root.Add(progress);
        var tile = new CardTile
        {
            Root = root,
            Image = image,
            Name = name,
            Number = number,
            Rarity = rarity,
            Owned = owned,
            NewBadge = newBadge,
        };
        tile.Action = new MobileActionControl(
            root,
            () => ActivateCardTile(tile),
            playFeedback: false,
            feedbackLabel: name);
        return tile;
    }

    private void ActivateCardTile(CardTile tile)
    {
        if (tile?.Printing == null || navigationRequested)
            return;
        UIFeedbackService.Play(FeedbackCue.CardFlip, true);
        ShowPrintingDetails(tile.Printing.Id);
    }

    private void BindCardGridRow(VisualElement element, int index)
    {
        if (index < 0 || index >= cardGridLines.Count)
            return;
        CardGridLine line = cardGridLines[index];
        var row = (CardGridRow)element.userData;
        BindCardTile(row.Tiles[0], line.First);
        BindCardTile(row.Tiles[1], line.Second);
    }

    private void BindCardTile(CardTile row, PrintingDefinition printing)
    {
        row.Printing = printing;
        SetVisible(row.Root, printing != null);
        row.Action.SetEnabled(printing != null && !navigationRequested);
        if (printing == null)
        {
            row.Image.Unbind();
            return;
        }
        row.Name.text = DisplayName(printing);
        row.Number.text = $"#{printing.Identity.CardNumber}  ·  {printing.Identity.VariantId}";
        row.Rarity.text = catalog.Rarities.TryGetValue(printing.RarityId, out RarityDefinition rarity)
            ? DisplayName(rarity)
            : printing.RarityId;
        CollectionItemProgress progress = Progress(printing);
        row.Owned.text = FormatOwnedCount(progress.OwnedCount);
        row.Owned.EnableInClassList("is-owned", progress.IsOwned);
        row.NewBadge.text = CardUiText.Get("common.badge.new");
        SetVisible(row.NewBadge, progress.IsNew);
        row.Root.EnableInClassList("is-unowned", !progress.IsOwned);
        row.Root.EnableInClassList("is-new", progress.IsNew);
        row.Image.Bind(printing);
        row.Root.tooltip = row.Name.text;
    }

    private void UnbindCardGridRow(VisualElement element, int index)
    {
        if (!(element.userData is CardGridRow row))
            return;
        foreach (CardTile tile in row.Tiles)
        {
            tile.Printing = null;
            tile.Action.SetEnabled(false);
            tile.Image.Unbind();
        }
    }

    private void DestroyRow(VisualElement element)
    {
        AsyncCardImageView image = null;
        if (element.userData is SetRow setRow)
        {
            image = setRow.Image;
            setRow.Action.Dispose();
            setRow.Set = null;
        }
        else if (element.userData is CardGridRow cardRow)
        {
            foreach (CardTile tile in cardRow.Tiles)
            {
                tile.Action.Dispose();
                tile.Printing = null;
                tile.Image.Dispose();
                imageViews.Remove(tile.Image);
            }
            return;
        }

        if (image != null)
        {
            image.Dispose();
            imageViews.Remove(image);
        }
    }

    public void RefreshCollectionProgress()
    {
        setList?.RefreshItems();
        if (currentSet != null)
            ApplyFilters(false);
        else
            SetBrowserStatus(FormatCollectionSummary(), false);
    }

    public void SetOwnedOnlyFilter(bool value)
    {
        ownedOnly = value;
        RefreshFilterControls();
        ApplyFilters(true);
    }

    public void SetNewOnlyFilter(bool value)
    {
        newOnly = value;
        RefreshFilterControls();
        ApplyFilters(true);
    }

    private void ApplyFilters(bool animate, bool hideDetails = true)
    {
        CancelSearchRefresh();
        IEnumerable<PrintingDefinition> query = CurrentSetCards;
        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            query = query.Where(printing =>
                DisplayName(printing).IndexOf(searchQuery, StringComparison.OrdinalIgnoreCase) >= 0 ||
                printing.Identity.CardNumber.IndexOf(searchQuery, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        if (!string.IsNullOrWhiteSpace(selectedRarityId))
            query = query.Where(printing => string.Equals(printing.RarityId, selectedRarityId, StringComparison.Ordinal));
        if (ownedOnly)
            query = query.Where(printing => Progress(printing).IsOwned);
        if (newOnly)
            query = query.Where(printing => Progress(printing).IsNew);

        string cardLanguageId = ApplicationServices.Languages.ContentLanguage.ResolvedLanguageId;
        StringComparer cardNameComparer = CreateCardNameComparer(cardLanguageId);
        query = sortMode switch
        {
            CardSortMode.Name => query
                .OrderBy(printing => DisplayName(printing), cardNameComparer)
                .ThenBy(printing => printing.Identity.CardNumber, CardNumberComparer)
                .ThenBy(printing => printing.Id, StringComparer.Ordinal),
            CardSortMode.Rarity => query
                .OrderBy(RarityRank)
                .ThenBy(printing => printing.Identity.CardNumber, CardNumberComparer)
                .ThenBy(printing => printing.Id, StringComparer.Ordinal),
            _ => query
                .OrderBy(printing => printing.Identity.CardNumber, CardNumberComparer)
                .ThenBy(printing => printing.Identity.VariantId, StringComparer.Ordinal)
                .ThenBy(printing => printing.Id, StringComparer.Ordinal)
        };

        cards.Clear();
        cards.AddRange(query);
        cardGridLines.Clear();
        for (int index = 0; index < cards.Count; index += 2)
        {
            cardGridLines.Add(new CardGridLine(
                cards[index],
                index + 1 < cards.Count ? cards[index + 1] : null));
        }
        cardCount.text = FormatFilteredCardCount(cards.Count, CurrentSetTotalCount, OwnedCardCount, NewCardCount);
        filterEmpty.text = CardUiText.Get("collection.filter.empty");
        SetVisible(filterEmpty, cards.Count == 0);
        cardList.itemsSource = cardGridLines;
        cardList.Rebuild();
        if (hideDetails)
            HideDetails(false);
        if (animate)
            AnimateFilterResults();
    }

    private void ResetFilters(bool apply)
    {
        CancelSearchRefresh();
        searchQuery = string.Empty;
        selectedRarityId = null;
        ownedOnly = false;
        newOnly = false;
        sortMode = CardSortMode.Number;
        updatingFilterControls = true;
        searchField.SetValueWithoutNotify(string.Empty);
        if (rarityFilter.choices != null && rarityFilter.choices.Count > 0)
            rarityFilter.index = 0;
        if (sortField.choices != null && sortField.choices.Count > 0)
            sortField.index = 0;
        updatingFilterControls = false;
        RefreshFilterControls();
        if (apply)
            ApplyFilters(true);
    }

    private void ScheduleSearchRefresh()
    {
        CancelSearchRefresh();
        searchRefresh = cardPage.schedule.Execute(() =>
        {
            searchRefresh = null;
            ApplyFilters(true);
        });
        searchRefresh.ExecuteLater(SearchDebounceMilliseconds);
    }

    private void CancelSearchRefresh()
    {
        searchRefresh?.Pause();
        searchRefresh = null;
    }

    private void RebuildRarityFilter()
    {
        string previousRarityId = selectedRarityId;
        RarityDefinition[] rarities = CurrentSetCards
            .Select(printing => catalog.Rarities[printing.RarityId])
            .Distinct()
            .OrderBy(rarity => rarity.DisplayRank)
            .ThenBy(rarity => rarity.Id, StringComparer.Ordinal)
            .ToArray();

        rarityFilterIds.Clear();
        rarityFilterIds.Add(null);
        rarityFilterIds.AddRange(rarities.Select(rarity => rarity.Id));
        var choices = new List<string> { CardUiText.Get("collection.filter.all_rarities") };
        choices.AddRange(rarities.Select(DisplayName));
        int nextIndex = string.IsNullOrWhiteSpace(previousRarityId)
            ? 0
            : rarityFilterIds.IndexOf(previousRarityId);
        if (nextIndex < 0)
        {
            nextIndex = 0;
            selectedRarityId = null;
        }
        updatingFilterControls = true;
        rarityFilter.choices = choices;
        rarityFilter.index = nextIndex;
        updatingFilterControls = false;
    }

    private void RefreshFilterControls()
    {
        searchField.label = CardUiText.Get("collection.filter.search");
        rarityFilter.label = CardUiText.Get("collection.filter.rarity");
        sortField.label = CardUiText.Get("collection.sort.label");
        updatingFilterControls = true;
        sortField.choices = new List<string>
        {
            CardUiText.Get("collection.sort.number"),
            CardUiText.Get("collection.sort.name"),
            CardUiText.Get("collection.sort.rarity")
        };
        sortField.index = (int)sortMode;
        updatingFilterControls = false;
        ownedOnlyAction.SetLabel(ownedOnly
            ? CardUiText.Get("collection.filter.owned_on")
            : CardUiText.Get("collection.filter.owned_off"));
        newOnlyAction.SetLabel(newOnly
            ? CardUiText.Get("collection.filter.new_on")
            : CardUiText.Get("collection.filter.new_off"));
        clearFiltersAction.SetLabel(CardUiText.Get("common.action.clear"));
        ownedOnlyAction.SetSelected(ownedOnly);
        newOnlyAction.SetSelected(newOnly);
        ownedOnlyAction.Root.EnableInClassList("is-selected", ownedOnly);
        newOnlyAction.Root.EnableInClassList("is-selected", newOnly);
    }

    private void RefreshPrintingDetails(PrintingDefinition printing, bool markSeen)
    {
        string cardLanguageId = printing.Identity.LanguageId;
        detailName.text = CardDisplayName(printing, cardLanguageId);
        string setName = catalog.Sets.TryGetValue(printing.Identity.SetId, out SetDefinition set)
            ? CardDisplayName(set, cardLanguageId)
            : printing.Identity.SetId;
        string rarity = catalog.Rarities.TryGetValue(printing.RarityId, out RarityDefinition rarityDefinition)
            ? CardDisplayName(rarityDefinition, cardLanguageId)
            : printing.RarityId;
        string variant = catalog.Variants.TryGetValue(printing.Identity.VariantId, out VariantDefinition variantDefinition)
            ? CardDisplayName(variantDefinition, cardLanguageId)
            : printing.Identity.VariantId;
        detailMetadata.text = $"{setName}  ·  #{printing.Identity.CardNumber}\n" +
                              $"{rarity}  ·  {variant}  ·  {LanguageBadge(cardLanguageId)}";
        CollectionItemProgress progress = Progress(printing);
        detailProgress.text = FormatOwnedCount(progress.OwnedCount);
        detailNewBadge.text = CardUiText.Get("common.badge.new");
        SetVisible(detailNewBadge, progress.IsNew);
        detailImage.Bind(printing);
        if (markSeen && progress.IsNew)
            MarkPrintingSeen(printing);
    }

    private void RebuildDetailLanguageSwitcher()
    {
        DisposeDetailLanguageActions();
        detailLanguageSwitcher.Clear();
        PrintingLanguageGroup group = currentDetailPrinting == null
            ? null
            : catalog.PrintingLanguages.GetGroup(currentDetailPrinting.Id);
        if (group == null || !group.HasMultipleLanguages)
        {
            SetVisible(detailLanguageSwitcher, false);
            return;
        }

        foreach (string languageId in group.AvailableLanguageIds
                     .OrderBy(LanguageSortOrder)
                     .ThenBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            string selectedLanguage = languageId;
            var action = new MobileActionControl(
                "detail-language-" + selectedLanguage.ToLowerInvariant().Replace('_', '-'),
                LanguageBadge(selectedLanguage),
                () => SwitchDetailCardLanguage(selectedLanguage));
            action.Root.AddToClassList("collection-detail__language");
            bool selected = string.Equals(
                currentDetailPrinting.Identity.LanguageId,
                selectedLanguage,
                StringComparison.OrdinalIgnoreCase);
            action.SetSelected(selected);
            action.Root.EnableInClassList("is-selected", selected);
            detailLanguageActions.Add(action);
            detailLanguageSwitcher.Add(action.Root);
        }
        SetVisible(detailLanguageSwitcher, true);
    }

    private void AnimateLanguageSwap()
    {
        languageSwapAnimation?.Pause();
        languageSwapAnimation = null;
        if (UIFeedbackService.ReduceMotion)
        {
            detailImage.Element.style.opacity = 1f;
            detailLanguageSwitcher.style.opacity = 1f;
            return;
        }

        detailImage.Element.style.opacity = 0.35f;
        detailLanguageSwitcher.style.opacity = 0f;
        languageSwapAnimation = detailImage.Element.schedule.Execute(() =>
        {
            detailImage.Element.style.opacity = 1f;
            detailLanguageSwitcher.style.opacity = 1f;
            languageSwapAnimation = null;
        });
        languageSwapAnimation.ExecuteLater(Mathf.RoundToInt(120f / UIFeedbackService.AnimationSpeed));
    }

    private static int LanguageSortOrder(string languageId)
    {
        string normalized = languageId?.Trim().Replace('_', '-').ToLowerInvariant() ?? string.Empty;
        if (normalized.StartsWith("zh-", StringComparison.Ordinal) || normalized == "zh")
            return 0;
        if (normalized == "en" || normalized.StartsWith("en-", StringComparison.Ordinal))
            return 1;
        if (normalized == "ja" || normalized.StartsWith("ja-", StringComparison.Ordinal))
            return 2;
        return 3;
    }

    private static string LanguageBadge(string languageId)
    {
        string normalized = languageId?.Trim().Replace('_', '-').ToLowerInvariant() ?? string.Empty;
        if (normalized.StartsWith("zh-", StringComparison.Ordinal) || normalized == "zh")
            return "中";
        if (normalized == "en" || normalized.StartsWith("en-", StringComparison.Ordinal))
            return "EN";
        if (normalized == "ja" || normalized.StartsWith("ja-", StringComparison.Ordinal))
            return "日";
        return normalized.ToUpperInvariant();
    }

    private static string CardDisplayName(Definition definition, string cardLanguageId)
    {
        if (definition == null)
            return string.Empty;
        if (!string.IsNullOrWhiteSpace(cardLanguageId) &&
            definition.Names.TryGetValue(cardLanguageId, out string exact) &&
            !string.IsNullOrWhiteSpace(exact))
            return exact;
        return definition.Id;
    }

    private void MarkPrintingSeen(PrintingDefinition printing)
    {
        try
        {
            if (!collectionProgress.MarkSeen(printing.Id))
                return;

            SetVisible(detailNewBadge, false);
            setList.RefreshItems();
            if (newOnly)
                ApplyFilters(true, false);
            else
            {
                cardList.RefreshItems();
                cardCount.text = FormatFilteredCardCount(cards.Count, CurrentSetTotalCount, OwnedCardCount, NewCardCount);
            }
            SetBrowserStatus(FormatCollectionSummary(), false);
        }
        catch (Exception exception)
        {
            SetBrowserStatus(CardUiText.Get("collection.status.seen_save_failed"), true);
            Debug.LogWarning($"Collection viewed-card status could not be saved: {exception.Message}");
            UIFeedbackService.Play(FeedbackCue.Error);
        }
    }

    private void AnimateFilterResults()
    {
        filterAnimation?.Pause();
        if (UIFeedbackService.ReduceMotion)
        {
            cardList.style.opacity = 1f;
            filterEmpty.style.opacity = 1f;
            return;
        }

        float startedAt = Time.realtimeSinceStartup;
        float duration = 0.16f / UIFeedbackService.AnimationSpeed;
        cardList.style.opacity = 0.55f;
        filterEmpty.style.opacity = 0.55f;
        filterAnimation = cardPage.schedule.Execute(() =>
        {
            float progress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Time.realtimeSinceStartup - startedAt) / duration));
            cardList.style.opacity = Mathf.Lerp(0.55f, 1f, progress);
            filterEmpty.style.opacity = Mathf.Lerp(0.55f, 1f, progress);
            if (progress < 1f)
                return;
            filterAnimation?.Pause();
            filterAnimation = null;
        }).Every(16);
    }

    private void ShowSets()
    {
        filterSheet?.HideImmediately();
        currentSet = null;
        cards.Clear();
        cardGridLines.Clear();
        SetVisible(setPage, sets.Count > 0);
        SetVisible(cardPage, false);
        SetVisible(zeroContentPanel, sets.Count == 0);
        HideDetails(false);
        SetBrowserStatus(FormatCollectionSummary(), false);
        ApplyActionAvailability();
    }

    private void HideDetails(bool clearSelection)
    {
        languageSwapAnimation?.Pause();
        languageSwapAnimation = null;
        detailSheet?.Hide();
        detailImage?.Unbind();
        if (detailImage != null)
            detailImage.Element.style.opacity = 1f;
        currentDetailPrinting = null;
        DisposeDetailLanguageActions();
        detailLanguageSwitcher?.Clear();
        if (detailLanguageSwitcher != null)
        {
            SetVisible(detailLanguageSwitcher, false);
            detailLanguageSwitcher.style.opacity = 1f;
        }
        if (detailNewBadge != null)
            SetVisible(detailNewBadge, false);
        ApplyActionAvailability();
    }

    private void RefreshLocalizedChrome()
    {
        mobileTopBar.SetText(
            CardUiText.Get("collection.title"),
            CardUiText.Get("collection.subtitle"));
        menuAction.SetLabel(CardUiText.Get("common.action.main_menu"));
        manageContentAction.SetLabel(CardUiText.Get("common.action.manage_content"));
        errorPresenter?.RefreshLanguage();
        zeroContentText.text = CardUiText.Get("collection.status.no_content");
        pokedexAction.SetLabel(PokemonPokedexText.Get(
            "title",
            ApplicationServices.Languages.UiLanguageId));
        backToSetsAction.SetLabel(CardUiText.Get("collection.action.all_sets"));
        closeDetailsAction.SetLabel(CardUiText.Get("common.action.close"));
        openFiltersAction.SetLabel(CardUiText.Get("collection.action.filters"));
        closeFiltersAction.SetLabel(CardUiText.Get("common.action.close"));
        primaryNavigation.RefreshText();
        detailSheet.Title.text = CardUiText.Get("collection.title");
        filterSheet.Title.text = CardUiText.Get("collection.filters.title");
        if (currentDetailPrinting != null)
        {
            CollectionItemProgress detailState = Progress(currentDetailPrinting);
            detailProgress.text = FormatOwnedCount(detailState.OwnedCount);
            detailNewBadge.text = CardUiText.Get("common.badge.new");
        }
        if (IsReady)
            SetBrowserStatus(FormatCollectionSummary(), false);
        RefreshFilterControls();
        if (currentSet != null)
        {
            cardPageTitle.text = DisplayName(currentSet);
            RebuildRarityFilter();
            ApplyFilters(false);
        }

        setList?.RefreshItems();
        cardList?.RefreshItems();
        ApplyActionAvailability();
    }

    private void OpenContentManagement()
    {
        NavigatePrimary(MobileDestination.Content);
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
        string reopenSetId = currentSet?.Id;
        BuildBrowseData();
        if (!string.IsNullOrWhiteSpace(reopenSetId) && OpenSet(reopenSetId))
            return;
        ShowSets();
    }

    private string DisplayName(Definition definition)
    {
        return ApplicationServices.Languages.GetDisplayName(definition);
    }

    private int RarityRank(PrintingDefinition printing)
    {
        return catalog.Rarities.TryGetValue(printing.RarityId, out RarityDefinition rarity)
            ? rarity.DisplayRank
            : int.MaxValue;
    }

    private static StringComparer CreateCardNameComparer(string languageId)
    {
        string normalized = (languageId ?? string.Empty).Trim().ToLowerInvariant();
        string cultureName = normalized switch
        {
            "zh" => "zh-CN",
            "zh-cn" => "zh-CN",
            "ja" => "ja-JP",
            "en" => "en-US",
            _ => normalized
        };
        try
        {
            return string.IsNullOrWhiteSpace(cultureName)
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Create(CultureInfo.GetCultureInfo(cultureName), true);
        }
        catch (CultureNotFoundException)
        {
            return StringComparer.OrdinalIgnoreCase;
        }
    }

    private static int CompareCardNumbers(string left, string right)
    {
        left ??= string.Empty;
        right ??= string.Empty;
        int leftIndex = 0;
        int rightIndex = 0;
        while (leftIndex < left.Length && rightIndex < right.Length)
        {
            bool leftDigit = char.IsDigit(left[leftIndex]);
            bool rightDigit = char.IsDigit(right[rightIndex]);
            if (leftDigit && rightDigit)
            {
                int leftRunStart = leftIndex;
                int rightRunStart = rightIndex;
                while (leftIndex < left.Length && left[leftIndex] == '0')
                    leftIndex++;
                while (rightIndex < right.Length && right[rightIndex] == '0')
                    rightIndex++;
                int leftSignificantStart = leftIndex;
                int rightSignificantStart = rightIndex;
                while (leftIndex < left.Length && char.IsDigit(left[leftIndex]))
                    leftIndex++;
                while (rightIndex < right.Length && char.IsDigit(right[rightIndex]))
                    rightIndex++;
                int leftSignificantLength = leftIndex - leftSignificantStart;
                int rightSignificantLength = rightIndex - rightSignificantStart;
                if (leftSignificantLength != rightSignificantLength)
                    return leftSignificantLength.CompareTo(rightSignificantLength);
                for (int offset = 0; offset < leftSignificantLength; offset++)
                {
                    int comparison = left[leftSignificantStart + offset]
                        .CompareTo(right[rightSignificantStart + offset]);
                    if (comparison != 0)
                        return comparison;
                }
                int leftRunLength = leftIndex - leftRunStart;
                int rightRunLength = rightIndex - rightRunStart;
                if (leftRunLength != rightRunLength)
                    return leftRunLength.CompareTo(rightRunLength);
                continue;
            }

            if (leftDigit != rightDigit)
                return leftDigit ? -1 : 1;
            int characterComparison = char.ToUpperInvariant(left[leftIndex])
                .CompareTo(char.ToUpperInvariant(right[rightIndex]));
            if (characterComparison != 0)
                return characterComparison;
            leftIndex++;
            rightIndex++;
        }
        return (left.Length - leftIndex).CompareTo(right.Length - rightIndex);
    }

    private string FormatCollectionSummary()
    {
        int total = cardsBySet.Values.Sum(setCards => setCards.Count);
        int owned = cardsBySet.Values.Sum(setCards => setCards.Count(printing => Progress(printing).IsOwned));
        int unseen = cardsBySet.Values.Sum(setCards => setCards.Count(printing => Progress(printing).IsNew));
        return CardUiText.Format("collection.summary.all", sets.Count, owned, total, unseen);
    }

    private static string FormatFilteredCardCount(int shown, int total, int owned, int unseen)
    {
        return CardUiText.Format("collection.summary.filtered", shown, owned, total, unseen);
    }

    private static string FormatOwnedCount(int count)
    {
        return count > 0
            ? CardUiText.Format("collection.owned", count)
            : CardUiText.Get("collection.unowned");
    }

    private CollectionItemProgress Progress(PrintingDefinition printing)
    {
        return collectionProgress.GetProgress(printing.Id);
    }

    private void SetBrowserStatus(string message, bool isError)
    {
        if (browserStatus == null)
            return;
        browserStatus.text = message ?? string.Empty;
        SetVisible(browserStatus, !string.IsNullOrWhiteSpace(browserStatus.text));
        browserStatus.EnableInClassList("is-error", isError);
    }

    private void ApplyActionAvailability()
    {
        bool available = !destroyed && !navigationRequested;
        body?.EnableInClassList("is-pending", navigationRequested);
        if (body != null)
            body.pickingMode = available ? PickingMode.Position : PickingMode.Ignore;
        menuAction?.SetEnabled(available);
        pokedexAction?.SetEnabled(available);
        manageContentAction?.SetEnabled(available);
        errorRetryAction?.SetEnabled(available);
        errorManageAction?.SetEnabled(available);
        errorHomeAction?.SetEnabled(available);
        backToSetsAction?.SetEnabled(available && IsReady && currentSet != null);
        closeDetailsAction?.SetEnabled(available && currentDetailPrinting != null);
        ownedOnlyAction?.SetEnabled(available && IsReady && currentSet != null);
        newOnlyAction?.SetEnabled(available && IsReady && currentSet != null);
        clearFiltersAction?.SetEnabled(available && IsReady && currentSet != null);
        openFiltersAction?.SetEnabled(available && IsReady && currentSet != null);
        closeFiltersAction?.SetEnabled(available);
        foreach (MobileActionControl action in detailLanguageActions)
            action.SetEnabled(available);
        if (searchField != null)
            searchField.SetEnabled(available && IsReady && currentSet != null);
        if (rarityFilter != null)
            rarityFilter.SetEnabled(available && IsReady && currentSet != null);
        if (sortField != null)
            sortField.SetEnabled(available && IsReady && currentSet != null);
        if (setList != null)
        {
            foreach (VisualElement element in setList.Query<VisualElement>(className: "set-row").ToList())
                if (element.userData is SetRow row)
                    row.Action.SetEnabled(available && row.Set != null);
        }
        if (cardList != null)
        {
            foreach (VisualElement element in cardList.Query<VisualElement>(className: "card-grid-row").ToList())
            {
                if (!(element.userData is CardGridRow row))
                    continue;
                foreach (CardTile tile in row.Tiles)
                    tile.Action.SetEnabled(available && tile.Printing != null);
            }
        }
    }

    private void CompleteTransientVisuals()
    {
        CancelSearchRefresh();
        filterAnimation?.Pause();
        filterAnimation = null;
        languageSwapAnimation?.Pause();
        languageSwapAnimation = null;
        if (cardList != null)
            cardList.style.opacity = 1f;
        if (filterEmpty != null)
            filterEmpty.style.opacity = 1f;
        if (detailImage != null)
            detailImage.Element.style.opacity = 1f;
        if (detailLanguageSwitcher != null)
            detailLanguageSwitcher.style.opacity = 1f;
    }

    private void DisposeDetailLanguageActions()
    {
        foreach (MobileActionControl action in detailLanguageActions)
            action.Dispose();
        detailLanguageActions.Clear();
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

    private static void DisposeAction(ref MobileActionControl action)
    {
        action?.Dispose();
        action = null;
    }

    private static void SetVisible(VisualElement element, bool visible)
    {
        element?.EnableInClassList("is-hidden", !visible);
    }

    private AsyncCardImageView Track(AsyncCardImageView imageView)
    {
        imageViews.Add(imageView);
        return imageView;
    }
}
