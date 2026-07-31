using System;
using System.Collections.Generic;
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

    private sealed class SetRow
    {
        public AsyncCardImageView Image;
        public Label Name;
        public Label Metadata;
    }

    private sealed class CardRow
    {
        public AsyncCardImageView Image;
        public Label Name;
        public Label Number;
        public Label Rarity;
        public Label Owned;
        public Label NewBadge;
    }

    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private VisualTreeAsset viewAsset;
    [SerializeField, Range(8, 96)] private int textureCacheCapacity = 32;

    private readonly List<SetDefinition> sets = new List<SetDefinition>();
    private readonly List<PrintingDefinition> cards = new List<PrintingDefinition>();
    private readonly Dictionary<string, List<PrintingDefinition>> cardsBySet =
        new Dictionary<string, List<PrintingDefinition>>(StringComparer.Ordinal);
    private readonly HashSet<AsyncCardImageView> imageViews = new HashSet<AsyncCardImageView>();
    private readonly List<string> rarityFilterIds = new List<string>();

    private UniversalCatalog catalog;
    private ICollectionProgressStore collectionProgress;
    private CardTextureCache textureCache;
    private VisualElement browserRoot;
    private VisualElement setPage;
    private VisualElement cardPage;
    private VisualElement detailsPanel;
    private VisualElement detailLanguageSwitcher;
    private ListView setList;
    private ListView cardList;
    private Label pageTitle;
    private Label pageSubtitle;
    private Label browserStatus;
    private Label cardPageTitle;
    private Label cardCount;
    private Label detailName;
    private Label detailMetadata;
    private Label detailProgress;
    private Label detailNewBadge;
    private Label filterEmpty;
    private AsyncCardImageView detailImage;
    private Button menuButton;
    private Button pokedexButton;
    private Button backToSetsButton;
    private Button closeDetailsButton;
    private Button ownedOnlyButton;
    private Button newOnlyButton;
    private Button clearFiltersButton;
    private TextField searchField;
    private DropdownField rarityFilter;
    private SetDefinition currentSet;
    private PrintingDefinition currentDetailPrinting;
    private IVisualElementScheduledItem detailsAnimation;
    private IVisualElementScheduledItem languageSwapAnimation;
    private IVisualElementScheduledItem filterAnimation;
    private IVisualElementScheduledItem searchRefresh;
    private PokemonPokedexController pokedexController;
    private string searchQuery = string.Empty;
    private string selectedRarityId;
    private bool ownedOnly;
    private bool newOnly;
    private bool updatingFilterControls;

    public static ICollectionProgressStore CollectionProgressStoreOverride { private get; set; }
    public static UniversalCatalog CatalogOverride { private get; set; }

    public bool IsReady { get; private set; }
    public string InitializationError { get; private set; }
    public int InstalledSetCount => sets.Count;
    public int CurrentCardCount => cards.Count;
    public int CurrentSetTotalCount => CurrentSetCards.Count;
    public int OwnedCardCount => CurrentSetCards.Count(printing => Progress(printing).IsOwned);
    public int NewCardCount => CurrentSetCards.Count(printing => Progress(printing).IsNew);
    public int CachedTextureCount => textureCache?.Count ?? 0;
    public string DetailPrintingId => currentDetailPrinting?.Id;
    public int DetailLanguageCount => currentDetailPrinting == null
        ? 0
        : catalog?.PrintingLanguages.GetGroup(currentDetailPrinting.Id)?.AvailableLanguageIds.Count ?? 0;
    public bool HasDetailLanguageSwitcher => currentDetailPrinting != null &&
        catalog?.PrintingLanguages.GetGroup(currentDetailPrinting.Id)?.HasMultipleLanguages == true;

    private IReadOnlyList<PrintingDefinition> CurrentSetCards =>
        currentSet != null && cardsBySet.TryGetValue(currentSet.Id, out List<PrintingDefinition> setCards)
            ? setCards
            : Array.Empty<PrintingDefinition>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        CollectionProgressStoreOverride = null;
        CatalogOverride = null;
    }

    private void Awake()
    {
        EnsureDocumentAssets();
    }

    private void Start()
    {
        Initialize();
    }

    private void OnDestroy()
    {
        if (ApplicationServices.IsConfigured)
        {
            ApplicationServices.Languages.UiLanguageChanged -= OnUiLanguageChanged;
            ApplicationServices.Languages.ContentLanguageChanged -= OnContentLanguageChanged;
        }
        LocalizationSettings.SelectedLocaleChanged -= OnSelectedLocaleChanged;

        detailsAnimation?.Pause();
        detailsAnimation = null;
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
    }

    public bool OpenSet(string setId)
    {
        SetDefinition set = sets.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, setId, StringComparison.Ordinal));
        if (set == null)
            return false;

        currentSet = set;
        cardPageTitle.text = DisplayName(set);
        cardList.itemsSource = cards;
        setPage.style.display = DisplayStyle.None;
        cardPage.style.display = DisplayStyle.Flex;
        ResetFilters(false);
        RebuildRarityFilter();
        ApplyFilters(false);
        HideDetails(false);
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
        RefreshPrintingDetails(printing, true);
        RebuildDetailLanguageSwitcher();
        detailsPanel.style.display = DisplayStyle.Flex;
        detailsPanel.BringToFront();
        AnimateDetailsIn();
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
        if (GameManager.Instance != null && GameManager.Instance.loadManager != null)
            GameManager.Instance.loadManager.LoadScene(1);
        else
            SceneManager.LoadScene("002_MainMenuScene");
    }

    private void Initialize()
    {
        try
        {
            GameApplicationBootstrap.EnsureConfigured();
            if (uiDocument == null)
                uiDocument = GetComponent<UIDocument>();
            if (uiDocument == null)
                throw new InvalidOperationException("The collection browser has no UIDocument.");
            EnsureDocumentAssets();
            if (uiDocument.panelSettings == null)
                throw new InvalidOperationException("The collection browser has no PanelSettings.");

            browserRoot = uiDocument.rootVisualElement.Q<VisualElement>("collection-browser");
            if (browserRoot == null)
                throw new InvalidOperationException("CollectionView.uxml is not attached to the UIDocument.");

            QueryVisualElements();
            CatalogLoadResult load = null;
            if (CatalogOverride == null)
            {
                load = ApplicationServices.Catalog.EnsureLoaded();
                if (!load.Succeeded)
                    throw new InvalidOperationException(load.ErrorMessage);
            }
            if (!ApplicationServices.HasContentImages)
                throw new InvalidOperationException("The installed content image service is unavailable.");

            catalog = CatalogOverride ?? load.Catalog;
            ApplicationServices.Languages.RefreshContentLanguage(catalog);
            collectionProgress = CollectionProgressStoreOverride ?? new PlayerCollectionProgressStore();
            textureCache = new CardTextureCache(ApplicationServices.Images, textureCacheCapacity);
            detailImage = Track(new AsyncCardImageView(textureCache));
            browserRoot.Q<VisualElement>("detail-art-slot").Add(detailImage.Element);

            ConfigureLists();
            ConfigureButtons();
            pokedexController = GetComponent<PokemonPokedexController>();
            if (pokedexController == null)
                pokedexController = gameObject.AddComponent<PokemonPokedexController>();
            pokedexController.Attach(
                uiDocument,
                ShowPrintingDetails,
                () =>
                {
                    if (GameManager.Instance != null && GameManager.Instance.loadManager != null)
                        GameManager.Instance.loadManager.LoadScene(5);
                    else
                        SceneManager.LoadScene("006_ContentScene");
                });
            BuildBrowseData();
            RefreshLocalizedChrome();
            ShowSets();

            ApplicationServices.Languages.UiLanguageChanged += OnUiLanguageChanged;
            ApplicationServices.Languages.ContentLanguageChanged += OnContentLanguageChanged;
            LocalizationSettings.SelectedLocaleChanged += OnSelectedLocaleChanged;
            IsReady = true;
        }
        catch (Exception exception)
        {
            InitializationError = exception.Message;
            if (browserStatus != null)
                browserStatus.text = CardUiText.Format("collection.status.unavailable", exception.Message);
            Debug.LogWarning($"Collection browser could not be initialized: {exception.Message}");
            UIFeedbackService.Play(FeedbackCue.Error);
        }
    }

    private void QueryVisualElements()
    {
        setPage = Required<VisualElement>("set-page");
        cardPage = Required<VisualElement>("card-page");
        detailsPanel = Required<VisualElement>("details-panel");
        detailLanguageSwitcher = Required<VisualElement>("detail-language-switcher");
        setList = Required<ListView>("set-list");
        cardList = Required<ListView>("card-list");
        pageTitle = Required<Label>("collection-title");
        pageSubtitle = Required<Label>("collection-subtitle");
        browserStatus = Required<Label>("browser-status");
        cardPageTitle = Required<Label>("card-page-title");
        cardCount = Required<Label>("card-count");
        detailName = Required<Label>("detail-name");
        detailMetadata = Required<Label>("detail-metadata");
        detailProgress = Required<Label>("detail-progress");
        detailNewBadge = Required<Label>("detail-new-badge");
        filterEmpty = Required<Label>("filter-empty");
        menuButton = Required<Button>("menu-button");
        pokedexButton = Required<Button>("pokedex-button");
        backToSetsButton = Required<Button>("back-to-sets-button");
        closeDetailsButton = Required<Button>("details-close-button");
        ownedOnlyButton = Required<Button>("owned-only-button");
        newOnlyButton = Required<Button>("new-only-button");
        clearFiltersButton = Required<Button>("clear-filters-button");
        searchField = Required<TextField>("card-search");
        rarityFilter = Required<DropdownField>("rarity-filter");
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
        setList.virtualizationMethod = CollectionVirtualizationMethod.FixedHeight;
        setList.fixedItemHeight = 132f;
        setList.selectionType = SelectionType.Single;
        setList.makeItem = MakeSetRow;
        setList.bindItem = BindSetRow;
        setList.unbindItem = UnbindSetRow;
        setList.destroyItem = DestroyRow;
        setList.selectionChanged += OnSetSelectionChanged;

        cardList.virtualizationMethod = CollectionVirtualizationMethod.FixedHeight;
        cardList.fixedItemHeight = 184f;
        cardList.selectionType = SelectionType.Single;
        cardList.makeItem = MakeCardRow;
        cardList.bindItem = BindCardRow;
        cardList.unbindItem = UnbindCardRow;
        cardList.destroyItem = DestroyRow;
        cardList.selectionChanged += OnCardSelectionChanged;
    }

    private void ConfigureButtons()
    {
        menuButton.clicked += MenuBtnClick;
        pokedexButton.clicked += () => pokedexController?.Open();
        backToSetsButton.clicked += () =>
        {
            UIFeedbackService.Play(FeedbackCue.Back);
            ShowSets();
        };
        closeDetailsButton.clicked += () =>
        {
            UIFeedbackService.Play(FeedbackCue.Back);
            HideDetails(true);
        };
        ownedOnlyButton.clicked += () =>
        {
            ownedOnly = !ownedOnly;
            UIFeedbackService.Play(FeedbackCue.Confirm);
            RefreshFilterControls();
            ApplyFilters(true);
        };
        newOnlyButton.clicked += () =>
        {
            newOnly = !newOnly;
            UIFeedbackService.Play(FeedbackCue.Confirm);
            RefreshFilterControls();
            ApplyFilters(true);
        };
        clearFiltersButton.clicked += () =>
        {
            UIFeedbackService.Play(FeedbackCue.Back);
            ResetFilters(true);
        };
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
    }

    private void BuildBrowseData()
    {
        string languageId = ApplicationServices.Languages.ContentLanguage.ResolvedLanguageId;
        sets.Clear();
        sets.AddRange(catalog.Sets.Values
            .OrderBy(set => set, new SetDefinitionComparer(SetSortMode.Generation, languageId)));

        cardsBySet.Clear();
        foreach (SetDefinition set in sets)
        {
            List<PrintingDefinition> setCards = catalog.GetPrintings(set.Id, languageId)
                .OrderBy(printing => printing.Identity.CardNumber, StringComparer.OrdinalIgnoreCase)
                .ThenBy(printing => printing.Identity.VariantId, StringComparer.Ordinal)
                .ToList();
            cardsBySet[set.Id] = setCards;
        }

        setList.itemsSource = sets;
        setList.Rebuild();
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
        root.userData = new SetRow { Image = image, Name = name, Metadata = metadata };
        return root;
    }

    private void BindSetRow(VisualElement element, int index)
    {
        if (index < 0 || index >= sets.Count)
            return;
        SetDefinition set = sets[index];
        var row = (SetRow)element.userData;
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
            row.Image.Unbind();
    }

    private VisualElement MakeCardRow()
    {
        var root = new VisualElement();
        root.AddToClassList("card-row");
        AsyncCardImageView image = Track(new AsyncCardImageView(textureCache));
        image.Element.AddToClassList("card-row__image");
        var copy = new VisualElement();
        copy.AddToClassList("browser-row__copy");
        var name = new Label();
        name.AddToClassList("browser-row__title");
        var number = new Label();
        number.AddToClassList("browser-row__metadata");
        var rarity = new Label();
        rarity.AddToClassList("card-row__rarity");
        var progress = new VisualElement();
        progress.AddToClassList("card-row__progress");
        var owned = new Label();
        owned.AddToClassList("card-row__owned");
        var newBadge = new Label();
        newBadge.AddToClassList("card-row__new");
        progress.Add(newBadge);
        progress.Add(owned);
        copy.Add(name);
        copy.Add(number);
        copy.Add(rarity);
        root.Add(image.Element);
        root.Add(copy);
        root.Add(progress);
        root.userData = new CardRow
        {
            Image = image,
            Name = name,
            Number = number,
            Rarity = rarity,
            Owned = owned,
            NewBadge = newBadge
        };
        return root;
    }

    private void BindCardRow(VisualElement element, int index)
    {
        if (index < 0 || index >= cards.Count)
            return;
        PrintingDefinition printing = cards[index];
        var row = (CardRow)element.userData;
        row.Name.text = DisplayName(printing);
        row.Number.text = $"#{printing.Identity.CardNumber}  ·  {printing.Identity.VariantId}";
        row.Rarity.text = catalog.Rarities.TryGetValue(printing.RarityId, out RarityDefinition rarity)
            ? DisplayName(rarity)
            : printing.RarityId;
        CollectionItemProgress progress = Progress(printing);
        row.Owned.text = FormatOwnedCount(progress.OwnedCount);
        row.Owned.EnableInClassList("is-owned", progress.IsOwned);
        row.NewBadge.text = CardUiText.Get("common.badge.new");
        row.NewBadge.style.display = progress.IsNew ? DisplayStyle.Flex : DisplayStyle.None;
        element.EnableInClassList("is-unowned", !progress.IsOwned);
        element.EnableInClassList("is-new", progress.IsNew);
        row.Image.Bind(printing);
        element.tooltip = row.Name.text;
    }

    private static void UnbindCardRow(VisualElement element, int index)
    {
        if (element.userData is CardRow row)
            row.Image.Unbind();
    }

    private void DestroyRow(VisualElement element)
    {
        AsyncCardImageView image = null;
        if (element.userData is SetRow setRow)
            image = setRow.Image;
        else if (element.userData is CardRow cardRow)
            image = cardRow.Image;

        if (image == null)
            return;
        image.Dispose();
        imageViews.Remove(image);
    }

    private void OnSetSelectionChanged(IEnumerable<object> selection)
    {
        SetDefinition set = selection.OfType<SetDefinition>().FirstOrDefault();
        if (set == null)
            return;
        UIFeedbackService.Play(FeedbackCue.Confirm);
        OpenSet(set.Id);
    }

    private void OnCardSelectionChanged(IEnumerable<object> selection)
    {
        PrintingDefinition printing = selection.OfType<PrintingDefinition>().FirstOrDefault();
        if (printing == null)
            return;
        UIFeedbackService.Play(FeedbackCue.CardFlip, true);
        ShowPrintingDetails(printing.Id);
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

        cards.Clear();
        cards.AddRange(query);
        cardCount.text = FormatFilteredCardCount(cards.Count, CurrentSetTotalCount, OwnedCardCount, NewCardCount);
        filterEmpty.text = CardUiText.Get("collection.filter.empty");
        filterEmpty.style.display = cards.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
        cardList.itemsSource = cards;
        cardList.ClearSelection();
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
        updatingFilterControls = true;
        searchField.SetValueWithoutNotify(string.Empty);
        if (rarityFilter.choices != null && rarityFilter.choices.Count > 0)
            rarityFilter.index = 0;
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
        ownedOnlyButton.text = ownedOnly
            ? CardUiText.Get("collection.filter.owned_on")
            : CardUiText.Get("collection.filter.owned_off");
        newOnlyButton.text = newOnly
            ? CardUiText.Get("collection.filter.new_on")
            : CardUiText.Get("collection.filter.new_off");
        clearFiltersButton.text = CardUiText.Get("common.action.clear");
        ownedOnlyButton.EnableInClassList("is-selected", ownedOnly);
        newOnlyButton.EnableInClassList("is-selected", newOnly);
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
        detailNewBadge.style.display = progress.IsNew ? DisplayStyle.Flex : DisplayStyle.None;
        detailImage.Bind(printing);
        if (markSeen && progress.IsNew)
            MarkPrintingSeen(printing);
    }

    private void RebuildDetailLanguageSwitcher()
    {
        detailLanguageSwitcher.Clear();
        PrintingLanguageGroup group = currentDetailPrinting == null
            ? null
            : catalog.PrintingLanguages.GetGroup(currentDetailPrinting.Id);
        if (group == null || !group.HasMultipleLanguages)
        {
            detailLanguageSwitcher.style.display = DisplayStyle.None;
            return;
        }

        foreach (string languageId in group.AvailableLanguageIds
                     .OrderBy(LanguageSortOrder)
                     .ThenBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            string selectedLanguage = languageId;
            var button = new Button(() => SwitchDetailCardLanguage(selectedLanguage))
            {
                text = LanguageBadge(selectedLanguage),
                name = "detail-language-" + selectedLanguage.ToLowerInvariant().Replace('_', '-')
            };
            button.AddToClassList("details-panel__language");
            button.EnableInClassList("is-selected", string.Equals(
                currentDetailPrinting.Identity.LanguageId,
                selectedLanguage,
                StringComparison.OrdinalIgnoreCase));
            detailLanguageSwitcher.Add(button);
        }
        detailLanguageSwitcher.style.display = DisplayStyle.Flex;
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
            definition.Names.TryGetValue(cardLanguageId, out string exact))
            return exact;
        return definition.Names.Values.First();
    }

    private void MarkPrintingSeen(PrintingDefinition printing)
    {
        try
        {
            if (!collectionProgress.MarkSeen(printing.Id))
                return;

            detailNewBadge.style.display = DisplayStyle.None;
            setList.RefreshItems();
            if (newOnly)
                ApplyFilters(true, false);
            else
            {
                int index = cards.IndexOf(printing);
                if (index >= 0)
                    cardList.RefreshItem(index);
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
        currentSet = null;
        cards.Clear();
        setList.ClearSelection();
        cardList.ClearSelection();
        setPage.style.display = DisplayStyle.Flex;
        cardPage.style.display = DisplayStyle.None;
        HideDetails(false);
        SetBrowserStatus(FormatCollectionSummary(), false);
    }

    private void HideDetails(bool clearSelection)
    {
        detailsAnimation?.Pause();
        detailsAnimation = null;
        languageSwapAnimation?.Pause();
        languageSwapAnimation = null;
        detailsPanel.style.display = DisplayStyle.None;
        detailsPanel.style.opacity = 0f;
        detailImage?.Unbind();
        if (detailImage != null)
            detailImage.Element.style.opacity = 1f;
        currentDetailPrinting = null;
        detailLanguageSwitcher?.Clear();
        if (detailLanguageSwitcher != null)
        {
            detailLanguageSwitcher.style.display = DisplayStyle.None;
            detailLanguageSwitcher.style.opacity = 1f;
        }
        if (detailNewBadge != null)
            detailNewBadge.style.display = DisplayStyle.None;
        if (clearSelection)
            cardList?.ClearSelection();
    }

    private void AnimateDetailsIn()
    {
        detailsAnimation?.Pause();
        if (UIFeedbackService.ReduceMotion)
        {
            detailsPanel.style.opacity = 1f;
            detailsPanel.style.scale = new Scale(Vector3.one);
            return;
        }

        float startedAt = Time.realtimeSinceStartup;
        float duration = 0.22f / UIFeedbackService.AnimationSpeed;
        detailsPanel.style.opacity = 0f;
        detailsPanel.style.scale = new Scale(new Vector3(0.94f, 0.94f, 1f));
        detailsAnimation = detailsPanel.schedule.Execute(() =>
        {
            float progress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Time.realtimeSinceStartup - startedAt) / duration));
            detailsPanel.style.opacity = progress;
            float scale = Mathf.Lerp(0.94f, 1f, progress);
            detailsPanel.style.scale = new Scale(new Vector3(scale, scale, 1f));
            if (progress >= 1f)
            {
                detailsAnimation?.Pause();
                detailsAnimation = null;
            }
        }).Every(16);
    }

    private void RefreshLocalizedChrome()
    {
        pageTitle.text = CardUiText.Get("collection.title");
        pageSubtitle.text = CardUiText.Get("collection.subtitle");
        menuButton.text = CardUiText.Get("common.action.main_menu");
        pokedexButton.text = PokemonPokedexText.Get(
            "title",
            ApplicationServices.Languages.UiLanguageId);
        backToSetsButton.text = CardUiText.Get("collection.action.all_sets");
        closeDetailsButton.text = CardUiText.Get("common.action.close");
        if (currentDetailPrinting != null)
        {
            CollectionItemProgress detailState = Progress(currentDetailPrinting);
            detailProgress.text = FormatOwnedCount(detailState.OwnedCount);
            detailNewBadge.text = CardUiText.Get("common.badge.new");
        }
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
        browserStatus.text = message ?? string.Empty;
        browserStatus.EnableInClassList("is-error", isError);
    }

    private AsyncCardImageView Track(AsyncCardImageView imageView)
    {
        imageViews.Add(imageView);
        return imageView;
    }
}
