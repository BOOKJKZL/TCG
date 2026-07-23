using System;
using System.Collections.Generic;
using System.Linq;
using Gacha.Application;
using Gacha.Domain;
using Gacha.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public sealed class CollectionViewController : MonoBehaviour
{
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
    }

    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private VisualTreeAsset viewAsset;
    [SerializeField, Range(8, 96)] private int textureCacheCapacity = 32;

    private readonly List<SetDefinition> sets = new List<SetDefinition>();
    private readonly List<PrintingDefinition> cards = new List<PrintingDefinition>();
    private readonly Dictionary<string, List<PrintingDefinition>> cardsBySet =
        new Dictionary<string, List<PrintingDefinition>>(StringComparer.Ordinal);
    private readonly HashSet<AsyncCardImageView> imageViews = new HashSet<AsyncCardImageView>();

    private UniversalCatalog catalog;
    private CardTextureCache textureCache;
    private VisualElement browserRoot;
    private VisualElement setPage;
    private VisualElement cardPage;
    private VisualElement detailsPanel;
    private ListView setList;
    private ListView cardList;
    private Label pageTitle;
    private Label pageSubtitle;
    private Label browserStatus;
    private Label cardPageTitle;
    private Label cardCount;
    private Label detailName;
    private Label detailMetadata;
    private AsyncCardImageView detailImage;
    private Button menuButton;
    private Button backToSetsButton;
    private Button closeDetailsButton;
    private SetDefinition currentSet;
    private IVisualElementScheduledItem detailsAnimation;

    public bool IsReady { get; private set; }
    public string InitializationError { get; private set; }
    public int InstalledSetCount => sets.Count;
    public int CurrentCardCount => cards.Count;
    public int CachedTextureCount => textureCache?.Count ?? 0;

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

        detailsAnimation?.Pause();
        detailsAnimation = null;
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
        cards.Clear();
        if (cardsBySet.TryGetValue(set.Id, out List<PrintingDefinition> setCards))
            cards.AddRange(setCards);

        cardPageTitle.text = DisplayName(set);
        cardCount.text = FormatCardCount(cards.Count);
        cardList.itemsSource = cards;
        cardList.ClearSelection();
        cardList.Rebuild();
        setPage.style.display = DisplayStyle.None;
        cardPage.style.display = DisplayStyle.Flex;
        HideDetails(false);
        return true;
    }

    public bool ShowPrintingDetails(string printingId)
    {
        PrintingDefinition printing = cards.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, printingId, StringComparison.Ordinal));
        if (printing == null)
            return false;

        detailName.text = DisplayName(printing);
        string rarity = catalog.Rarities.TryGetValue(printing.RarityId, out RarityDefinition definition)
            ? DisplayName(definition)
            : printing.RarityId;
        detailMetadata.text = $"#{printing.Identity.CardNumber}  ·  {rarity}  ·  {printing.Identity.LanguageId}";
        detailImage.Bind(printing);
        detailsPanel.style.display = DisplayStyle.Flex;
        detailsPanel.BringToFront();
        AnimateDetailsIn();
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
            CatalogLoadResult load = ApplicationServices.Catalog.EnsureLoaded();
            if (!load.Succeeded)
                throw new InvalidOperationException(load.ErrorMessage);
            if (!ApplicationServices.HasContentImages)
                throw new InvalidOperationException("The installed content image service is unavailable.");

            catalog = load.Catalog;
            ApplicationServices.Languages.RefreshContentLanguage(catalog);
            textureCache = new CardTextureCache(ApplicationServices.Images, textureCacheCapacity);
            detailImage = Track(new AsyncCardImageView(textureCache));
            browserRoot.Q<VisualElement>("detail-art-slot").Add(detailImage.Element);

            ConfigureLists();
            ConfigureButtons();
            BuildBrowseData();
            RefreshLocalizedChrome();
            ShowSets();

            ApplicationServices.Languages.UiLanguageChanged += OnUiLanguageChanged;
            ApplicationServices.Languages.ContentLanguageChanged += OnContentLanguageChanged;
            IsReady = true;
        }
        catch (Exception exception)
        {
            InitializationError = exception.Message;
            if (browserStatus != null)
                browserStatus.text = Localized("Collection unavailable", "收藏浏览暂不可用") + $": {exception.Message}";
            Debug.LogWarning($"Collection browser could not be initialized: {exception.Message}");
            UIFeedbackService.Play(FeedbackCue.Error);
        }
    }

    private void QueryVisualElements()
    {
        setPage = Required<VisualElement>("set-page");
        cardPage = Required<VisualElement>("card-page");
        detailsPanel = Required<VisualElement>("details-panel");
        setList = Required<ListView>("set-list");
        cardList = Required<ListView>("card-list");
        pageTitle = Required<Label>("collection-title");
        pageSubtitle = Required<Label>("collection-subtitle");
        browserStatus = Required<Label>("browser-status");
        cardPageTitle = Required<Label>("card-page-title");
        cardCount = Required<Label>("card-count");
        detailName = Required<Label>("detail-name");
        detailMetadata = Required<Label>("detail-metadata");
        menuButton = Required<Button>("menu-button");
        backToSetsButton = Required<Button>("back-to-sets-button");
        closeDetailsButton = Required<Button>("details-close-button");
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
    }

    private void BuildBrowseData()
    {
        sets.Clear();
        sets.AddRange(catalog.Sets.Values
            .OrderBy(set => set.ReleaseDate ?? DateTime.MaxValue)
            .ThenBy(set => set.Id, StringComparer.Ordinal));

        cardsBySet.Clear();
        string languageId = ApplicationServices.Languages.ContentLanguage.ResolvedLanguageId;
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
        string year = set.ReleaseDate?.Year.ToString() ?? "—";
        row.Metadata.text = $"{year}  ·  {FormatCardCount(count)}  ·  {ApplicationServices.Languages.ContentLanguage.ResolvedLanguageId}";
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
        copy.Add(name);
        copy.Add(number);
        copy.Add(rarity);
        root.Add(image.Element);
        root.Add(copy);
        root.userData = new CardRow { Image = image, Name = name, Number = number, Rarity = rarity };
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

    private void ShowSets()
    {
        currentSet = null;
        cards.Clear();
        setList.ClearSelection();
        cardList.ClearSelection();
        setPage.style.display = DisplayStyle.Flex;
        cardPage.style.display = DisplayStyle.None;
        HideDetails(false);
        browserStatus.text = FormatSetCount(sets.Count);
    }

    private void HideDetails(bool clearSelection)
    {
        detailsAnimation?.Pause();
        detailsAnimation = null;
        detailsPanel.style.display = DisplayStyle.None;
        detailsPanel.style.opacity = 0f;
        detailImage?.Unbind();
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
        pageTitle.text = Localized("Card Collection", "卡牌收藏");
        pageSubtitle.text = Localized(
            "Installed private sets · images load only when visible",
            "已安装的私人系列 · 仅加载屏幕可见卡图");
        menuButton.text = Localized("Main menu", "主菜单");
        backToSetsButton.text = Localized("All sets", "全部系列");
        closeDetailsButton.text = Localized("Close", "关闭");
        browserStatus.text = FormatSetCount(sets.Count);
        if (currentSet != null)
        {
            cardPageTitle.text = DisplayName(currentSet);
            cardCount.text = FormatCardCount(cards.Count);
        }

        setList?.RefreshItems();
        cardList?.RefreshItems();
    }

    private void OnUiLanguageChanged(string languageId)
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

    private static string FormatSetCount(int count)
    {
        return Localized($"{count} installed sets", $"已安装 {count} 个系列");
    }

    private static string FormatCardCount(int count)
    {
        return Localized($"{count} cards", $"{count} 张卡牌");
    }

    private static string Localized(string english, string chinese)
    {
        return ApplicationServices.IsConfigured &&
               ApplicationServices.Languages.UiLanguageId.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? chinese
            : english;
    }

    private AsyncCardImageView Track(AsyncCardImageView imageView)
    {
        imageViews.Add(imageView);
        return imageView;
    }
}
