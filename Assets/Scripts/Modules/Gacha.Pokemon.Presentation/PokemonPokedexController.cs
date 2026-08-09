using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Gacha.Application;
using Gacha.Domain;
using Gacha.Infrastructure.Content;
using Gacha.Pokemon.Application;
using Gacha.Pokemon.Domain;
using Gacha.Pokemon.Infrastructure;
using Gacha.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gacha.Pokemon.Presentation
{
    public sealed class PokemonPokedexController : MonoBehaviour
    {
        private const int SearchDebounceMilliseconds = 120;

        private sealed class SpeciesGridLine
        {
            public PokemonSpeciesDefinition First;
            public PokemonSpeciesDefinition Second;
        }

        private sealed class SpeciesTile
        {
            public VisualElement Root;
            public Label Number;
            public Label Name;
            public Label Genus;
            public PokemonSpeciesDefinition Species;
            public MobileActionControl Action;
        }

        private sealed class SpeciesGridRow
        {
            public SpeciesTile[] Tiles;
        }

        private sealed class IntroducedFormRow
        {
            public Label Number;
            public Label Name;
            public Label Metadata;
            public PokemonFormDefinition Form;
            public MobileActionControl Action;
        }

        private sealed class RelatedCardItem
        {
            public PokemonCardSubjectLink Link;
            public PrintingDefinition Printing;
        }

        private sealed class RelatedCardGridLine
        {
            public RelatedCardItem First;
            public RelatedCardItem Second;
        }

        private sealed class RelatedCardTile
        {
            public VisualElement Root;
            public AsyncCardImageView Image;
            public Label Name;
            public Label Metadata;
            public Label Status;
            public RelatedCardItem Item;
            public MobileActionControl Action;
        }

        private sealed class RelatedCardGridRow
        {
            public RelatedCardTile[] Tiles;
        }

        private readonly List<PokemonSpeciesDefinition> visibleSpecies = new List<PokemonSpeciesDefinition>();
        private readonly List<SpeciesGridLine> speciesGridLines = new List<SpeciesGridLine>();
        private readonly List<PokemonFormDefinition> visibleIntroducedForms = new List<PokemonFormDefinition>();
        private readonly List<RelatedCardItem> visibleCards = new List<RelatedCardItem>();
        private readonly List<RelatedCardGridLine> relatedCardGridLines = new List<RelatedCardGridLine>();
        private readonly HashSet<AsyncCardImageView> cardImageViews = new HashSet<AsyncCardImageView>();
        private readonly List<string> generationIds = new List<string>();
        private readonly Dictionary<string, PokemonArtworkCatalog> artworkCatalogs =
            new Dictionary<string, PokemonArtworkCatalog>(StringComparer.Ordinal);
        private readonly HashSet<string> missingArtworkCatalogs = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<MobileActionControl> formActions = new List<MobileActionControl>();
        private PokemonPokedexBrowser browser;
        private string taxonomySourceSha256;
        private PlayerUiError initializationPlayerError;
        private MobilePageShell mobilePageShell;
        private MobileTopBar mobileTopBar;
        private MobilePrimaryNavigation primaryNavigation;
        private VisualElement root;
        private VisualElement body;
        private VisualElement listPage;
        private VisualElement detailPage;
        private VisualElement formStrip;
        private VisualElement introducedFormsSection;
        private ListView speciesList;
        private ListView introducedFormsList;
        private ListView cardList;
        private DropdownField generationField;
        private TextField searchField;
        private TextField cardSearchField;
        private DropdownField cardSortField;
        private Label title;
        private Label subtitle;
        private Label status;
        private Label empty;
        private Label introducedFormsHeading;
        private Label detailNumber;
        private Label detailName;
        private Label detailGenus;
        private Label detailDebut;
        private Label detailTypes;
        private Label detailRegion;
        private Label detailDescription;
        private Label detailArtNumber;
        private Label detailArtStatus;
        private Label formsHeading;
        private Label cardsHeading;
        private Label cardsCount;
        private Label cardEmpty;
        private MobileActionControl closeAction;
        private MobileActionControl detailBackAction;
        private MobileActionControl formCardsAction;
        private MobileActionControl speciesCardsAction;
        private MobileActionControl manageDownloadsAction;
        private MobileActionControl emptyManageDownloadsAction;
        private MobileActionControl errorRetryAction;
        private MobileActionControl errorManageAction;
        private MobileActionControl errorCloseAction;
        private IVisualElementScheduledItem transitionAnimation;
        private IVisualElementScheduledItem speciesSearchRefresh;
        private IVisualElementScheduledItem cardSearchRefresh;
        private IVisualElementScheduledItem speciesListAnimation;
        private IVisualElementScheduledItem cardListAnimation;
        private IVisualElementScheduledItem detailAnimation;
        private IVisualElementScheduledItem speciesLayoutRefresh;
        private IVisualElementScheduledItem cardLayoutRefresh;
        private CardTextureCache artworkCache;
        private AsyncCardImageView artworkView;
        private CardTextureCache cardTextureCache;
        private UniversalCatalog runtimeCatalog;
        private Func<string, bool> openPrintingDetails;
        private Action manageDownloads;
        private Action<MobileDestination> navigatePrimary;
        private bool showAllSpeciesCards;
        private string cardSearch = string.Empty;
        private int cardSortMode;
        private bool attached;
        private bool updatingControls;
        private int returnListIndex = -1;
        private PlayerUiErrorPresenter errorPresenter;
        private bool uiLanguageSubscribed;
        private bool contentLanguageSubscribed;
        private bool navigationRequested;

        public static PokemonPokedexSnapshotBundle SnapshotOverride { private get; set; }
        public bool IsReady { get; private set; }
        public bool IsOpen => root != null && root.resolvedStyle.display == DisplayStyle.Flex;
        public string InitializationError { get; private set; }
        public PlayerUiErrorCode? InitializationErrorCode => initializationPlayerError?.Code;
        public bool MissingContent { get; private set; }
        public string LoadedCardLanguageId { get; private set; }
        public int VisibleSpeciesCount => visibleSpecies.Count;
        public int SpeciesGridLineCount => speciesGridLines.Count;
        public int VisibleIntroducedFormCount => visibleIntroducedForms.Count;
        public int GenerationCount => browser?.Generations.Count ?? 0;
        public string CurrentGenerationId => browser?.GenerationId;
        public string SelectedSpeciesId => browser?.SelectedSpecies?.Id;
        public string SelectedFormId => browser?.SelectedForm?.Id;
        public int SelectableFormCount => browser?.SelectableForms.Count ?? 0;
        public AsyncCardImageState ArtworkState => artworkView?.State ?? AsyncCardImageState.Empty;
        public Task ArtworkLoadTask => artworkView?.CurrentLoadTask ?? Task.CompletedTask;
        public int CachedArtworkCount => artworkCache?.Count ?? 0;
        public long CachedArtworkBytes => artworkCache?.DecodedBytes ?? 0L;
        public long CachedArtworkBudgetBytes => artworkCache?.MaximumDecodedBytes ?? 0L;
        public int VisibleCardCount => visibleCards.Count;
        public int RelatedCardGridLineCount => relatedCardGridLines.Count;
        public int InstalledVisibleCardCount => visibleCards.Count(value => value.Printing != null);
        public bool ShowingAllSpeciesCards => showAllSpeciesCards;
        public int PrimaryNavigationCount => primaryNavigation?.Count ?? 0;
        public int CachedCardTextureCount => cardTextureCache?.Count ?? 0;
        public long CachedCardTextureBytes => cardTextureCache?.DecodedBytes ?? 0L;
        public long CachedCardTextureBudgetBytes => cardTextureCache?.MaximumDecodedBytes ?? 0L;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            SnapshotOverride = null;
        }

        public void Attach(
            UIDocument document,
            Func<string, bool> openPrintingDetails = null,
            Action manageDownloads = null,
            Action<MobileDestination> navigatePrimary = null)
        {
            if (attached)
                return;
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            this.openPrintingDetails = openPrintingDetails;
            this.manageDownloads = manageDownloads;
            this.navigatePrimary = navigatePrimary;
            VisualTreeAsset view = Resources.Load<VisualTreeAsset>("UI/PokedexView");
            if (view == null)
                throw new InvalidOperationException("PokedexView.uxml is missing from Resources/UI.");
            TemplateContainer content = view.Instantiate();
            root = content.Q<VisualElement>("pokedex-overlay");
            if (root == null)
                throw new InvalidOperationException("PokedexView.uxml has no pokedex-overlay root.");
            content.Remove(root);
            document.rootVisualElement.Add(root);
            VisualElement body = root.Q<VisualElement>("pokedex-body") ??
                                 throw new InvalidOperationException("PokedexView.uxml has no pokedex-body.");
            body.RemoveFromHierarchy();
            mobilePageShell = new MobilePageShell("pokedex-page-shell");
            mobilePageShell.Root.AddToClassList("pokedex-mobile-page");
            mobileTopBar = new MobileTopBar(string.Empty, string.Empty);
            mobileTopBar.Title.name = "pokedex-title";
            mobileTopBar.Subtitle.name = "pokedex-subtitle";
            mobilePageShell.HeaderSlot.Add(mobileTopBar.Root);
            mobilePageShell.ContentSlot.Add(body);
            root.Clear();
            root.Add(mobilePageShell.Root);
            QueryElements();
            ConfigureControls();
            ConfigureActions();
            closeAction = new MobileActionControl(
                "pokedex-close-button",
                string.Empty,
                Close,
                MobileActionTone.Quiet);
            mobileTopBar.AddAction(closeAction);
            primaryNavigation = new MobilePrimaryNavigation(
                MobileDestination.Collection,
                NavigatePrimary);
            mobilePageShell.BottomNavigationSlot.Add(primaryNavigation.BottomNavigation.Root);
            errorPresenter = new PlayerUiErrorPresenter(
                Required<VisualElement>("pokedex-error-panel"),
                Required<Label>("pokedex-error-title"),
                Required<Label>("pokedex-error-body"),
                errorRetryAction.Root,
                errorManageAction.Root,
                close: errorCloseAction.Root);
            if (ApplicationServices.IsConfigured)
            {
                ApplicationServices.Languages.UiLanguageChanged += OnUiLanguageChanged;
                uiLanguageSubscribed = true;
            }
            RefreshLocalizedContent();
            root.style.display = DisplayStyle.None;
            attached = true;
        }

        public bool Open()
        {
            if (!attached)
                return false;
            navigationRequested = false;
            primaryNavigation?.ClearPending(MobileDestination.Collection);
            root.style.display = DisplayStyle.Flex;
            root.BringToFront();
            if (!EnsureReady())
            {
                PlayerUiError error = MissingContent
                    ? PlayerUiErrorMapper.Create(PlayerUiErrorCode.NotInstalled)
                    : initializationPlayerError ?? PlayerUiErrorMapper.FromDetail(InitializationError);
                status.text = string.Empty;
                status.RemoveFromClassList("is-error");
                SetVisible(emptyManageDownloadsAction?.Root, false);
                errorPresenter.Show(error);
                AnimateOpen();
                return false;
            }
            errorPresenter.Hide();
            status.RemoveFromClassList("is-error");
            ShowList();
            RefreshLocalizedContent();
            ScheduleSpeciesLayoutRefresh();
            UIFeedbackService.Play(FeedbackCue.Confirm);
            AnimateOpen();
            return true;
        }

        public void Close()
        {
            if (root == null)
                return;
            transitionAnimation?.Pause();
            transitionAnimation = null;
            CancelSpeciesSearchRefresh();
            CancelCardSearchRefresh();
            CancelPresentationAnimations();
            CancelLayoutRefreshes();
            errorPresenter?.HideImmediately();
            root.style.display = DisplayStyle.None;
            UIFeedbackService.Play(FeedbackCue.Back);
        }

        private void NavigatePrimary(MobileDestination destination)
        {
            if (destination == MobileDestination.Collection)
            {
                Close();
                return;
            }
            if (navigationRequested)
                return;
            navigationRequested = true;
            primaryNavigation?.SetPending(destination);
            ApplyActionAvailability();
            try
            {
                navigatePrimary?.Invoke(destination);
            }
            catch
            {
                navigationRequested = false;
                primaryNavigation?.ClearPending(MobileDestination.Collection);
                ApplyActionAvailability();
                throw;
            }
        }

        public bool RetryOpen()
        {
            UIFeedbackService.Play(FeedbackCue.ButtonClick);
            ResetInitializationState();
            return Open();
        }

        public void SetSearch(string value)
        {
            if (!EnsureReady())
                return;
            CancelSpeciesSearchRefresh();
            browser.Search(value);
            updatingControls = true;
            searchField.SetValueWithoutNotify(browser.Query);
            updatingControls = false;
            RefreshSpeciesList(true);
        }

        public bool SelectGeneration(string generationId)
        {
            if (!EnsureReady() || !generationIds.Contains(generationId))
                return false;
            CancelSpeciesSearchRefresh();
            browser.SelectGeneration(generationId);
            updatingControls = true;
            generationField.index = generationIds.IndexOf(generationId);
            searchField.SetValueWithoutNotify(string.Empty);
            updatingControls = false;
            ShowList();
            RefreshSpeciesList(true);
            UIFeedbackService.Play(FeedbackCue.Confirm);
            return true;
        }

        public bool OpenSpecies(string speciesId)
        {
            if (!EnsureReady())
                return false;
            returnListIndex = visibleSpecies.FindIndex(value => value.Id == speciesId);
            if (!browser.OpenSpecies(speciesId))
                return false;
            ResetCardGallery();
            RefreshDetails();
            listPage.style.display = DisplayStyle.None;
            detailPage.style.display = DisplayStyle.Flex;
            ScheduleCardLayoutRefresh();
            UIFeedbackService.Play(FeedbackCue.CardFlip, true);
            AnimateDetails();
            return true;
        }

        public bool OpenForm(string formId)
        {
            if (!EnsureReady() || !browser.OpenForm(formId))
                return false;
            ResetCardGallery();
            RefreshDetails();
            ScheduleCardLayoutRefresh();
            UIFeedbackService.Play(FeedbackCue.Confirm);
            AnimateDetails();
            return true;
        }

        private bool EnsureReady()
        {
            if (IsReady)
                return true;
            if (!string.IsNullOrWhiteSpace(InitializationError))
                return false;
            try
            {
                CatalogLoadResult catalogLoad = ApplicationServices.Catalog.EnsureLoaded();
                if (!catalogLoad.Succeeded)
                {
                    initializationPlayerError = PlayerUiErrorMapper.FromCatalog(catalogLoad);
                    InitializationError = catalogLoad.ErrorMessage;
                    return false;
                }
                if (!catalogLoad.HasInstalledContent)
                {
                    MissingContent = true;
                    initializationPlayerError = PlayerUiErrorMapper.Create(PlayerUiErrorCode.NotInstalled);
                    InitializationError = PokemonPokedexText.Get("content_missing", UiLanguage);
                    return false;
                }
                PokemonPokedexSnapshotBundle snapshot = SnapshotOverride ??
                    new PokemonPokedexSnapshotRepository().Load(TaxonomyPath, CardSubjectPath);
                browser = new PokemonPokedexBrowser(snapshot.Catalog, snapshot.SubjectCatalog);
                taxonomySourceSha256 = snapshot.Taxonomy.SourceSha256;
                LoadedCardLanguageId = snapshot.CardSubjects.Language;
                runtimeCatalog = catalogLoad.Catalog;
                artworkCache = new CardTextureCache(new PrivateContentImageSource(ArtworkRoot), 8);
                artworkView = new AsyncCardImageView(artworkCache);
                artworkView.Element.AddToClassList("pokedex-artwork-image");
                Required<VisualElement>("pokedex-art-slot").Add(artworkView.Element);
                if (ApplicationServices.HasContentImages)
                    cardTextureCache = new CardTextureCache(ApplicationServices.Images, 24);
                BuildGenerationChoices();
                RefreshSpeciesList(false);
                if (ApplicationServices.IsConfigured && !contentLanguageSubscribed)
                {
                    ApplicationServices.Languages.ContentLanguageChanged += OnCardLanguageChanged;
                    contentLanguageSubscribed = true;
                }
                IsReady = true;
                initializationPlayerError = null;
                return true;
            }
            catch (Exception exception)
            {
                InitializationError = exception.Message;
                initializationPlayerError = PlayerUiErrorMapper.FromException(exception);
                Debug.LogWarning("Pokédex could not be initialized: " + exception.Message);
                return false;
            }
        }

        private void ResetInitializationState()
        {
            IsReady = false;
            MissingContent = false;
            InitializationError = null;
            initializationPlayerError = null;
            browser = null;
            runtimeCatalog = null;
            taxonomySourceSha256 = null;
            LoadedCardLanguageId = null;
            generationIds.Clear();
            visibleSpecies.Clear();
            speciesGridLines.Clear();
            visibleIntroducedForms.Clear();
            visibleCards.Clear();
            relatedCardGridLines.Clear();
            artworkCatalogs.Clear();
            missingArtworkCatalogs.Clear();
            artworkView?.Dispose();
            artworkView = null;
            artworkCache?.Dispose();
            artworkCache = null;
            foreach (AsyncCardImageView image in cardImageViews.ToArray())
                image.Dispose();
            cardImageViews.Clear();
            cardTextureCache?.Dispose();
            cardTextureCache = null;
            Required<VisualElement>("pokedex-art-slot").Clear();
            speciesList.itemsSource = speciesGridLines;
            introducedFormsList.itemsSource = visibleIntroducedForms;
            cardList.itemsSource = relatedCardGridLines;
            speciesList.Rebuild();
            introducedFormsList.Rebuild();
            cardList.Rebuild();
        }

        private void OnDestroy()
        {
            navigationRequested = true;
            speciesList?.UnregisterCallback<GeometryChangedEvent>(OnSpeciesListGeometryChanged);
            cardList?.UnregisterCallback<GeometryChangedEvent>(OnCardListGeometryChanged);
            errorPresenter?.Dispose();
            errorPresenter = null;
            if (ApplicationServices.IsConfigured)
            {
                if (uiLanguageSubscribed)
                    ApplicationServices.Languages.UiLanguageChanged -= OnUiLanguageChanged;
                if (contentLanguageSubscribed)
                    ApplicationServices.Languages.ContentLanguageChanged -= OnCardLanguageChanged;
            }
            transitionAnimation?.Pause();
            CancelSpeciesSearchRefresh();
            CancelCardSearchRefresh();
            CancelPresentationAnimations();
            CancelLayoutRefreshes();
            artworkView?.Dispose();
            artworkView = null;
            artworkCache?.Dispose();
            artworkCache = null;
            foreach (AsyncCardImageView image in cardImageViews.ToArray())
                image.Dispose();
            cardImageViews.Clear();
            cardTextureCache?.Dispose();
            cardTextureCache = null;
            DisposeFormActions();
            DisposeAction(ref detailBackAction);
            DisposeAction(ref formCardsAction);
            DisposeAction(ref speciesCardsAction);
            DisposeAction(ref manageDownloadsAction);
            DisposeAction(ref emptyManageDownloadsAction);
            DisposeAction(ref errorRetryAction);
            DisposeAction(ref errorManageAction);
            DisposeAction(ref errorCloseAction);
            DisposeAction(ref closeAction);
            primaryNavigation?.Dispose();
            primaryNavigation = null;
            mobilePageShell?.Dispose();
            mobilePageShell = null;
            root?.RemoveFromHierarchy();
            root = null;
        }

        private void QueryElements()
        {
            body = Required<VisualElement>("pokedex-body");
            listPage = Required<VisualElement>("pokedex-list-page");
            detailPage = Required<VisualElement>("pokedex-detail-page");
            formStrip = Required<VisualElement>("pokedex-form-strip");
            introducedFormsSection = Required<VisualElement>("pokedex-introduced-forms-section");
            speciesList = Required<ListView>("pokedex-species-list");
            introducedFormsList = Required<ListView>("pokedex-introduced-forms-list");
            cardList = Required<ListView>("pokedex-card-list");
            generationField = Required<DropdownField>("pokedex-generation");
            searchField = Required<TextField>("pokedex-search");
            cardSearchField = Required<TextField>("pokedex-card-search");
            cardSortField = Required<DropdownField>("pokedex-card-sort");
            title = mobileTopBar.Title;
            subtitle = mobileTopBar.Subtitle;
            status = Required<Label>("pokedex-status");
            empty = Required<Label>("pokedex-empty");
            introducedFormsHeading = Required<Label>("pokedex-introduced-forms-heading");
            detailNumber = Required<Label>("pokedex-detail-number");
            detailName = Required<Label>("pokedex-detail-name");
            detailGenus = Required<Label>("pokedex-detail-genus");
            detailDebut = Required<Label>("pokedex-detail-debut");
            detailTypes = Required<Label>("pokedex-detail-types");
            detailRegion = Required<Label>("pokedex-detail-region");
            detailDescription = Required<Label>("pokedex-detail-description");
            detailArtNumber = Required<Label>("pokedex-art-number");
            detailArtStatus = Required<Label>("pokedex-art-status");
            formsHeading = Required<Label>("pokedex-forms-heading");
            cardsHeading = Required<Label>("pokedex-cards-heading");
            cardsCount = Required<Label>("pokedex-card-count");
            cardEmpty = Required<Label>("pokedex-card-empty");
        }

        private T Required<T>(string name) where T : VisualElement =>
            root.Q<T>(name) ?? throw new InvalidOperationException("Missing Pokédex UI element: " + name);

        private void ConfigureControls()
        {
            speciesList.virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight;
            speciesList.fixedItemHeight = 112f;
            speciesList.selectionType = SelectionType.None;
            speciesList.makeItem = MakeSpeciesGridRow;
            speciesList.bindItem = BindSpeciesGridRow;
            speciesList.unbindItem = UnbindSpeciesGridRow;
            speciesList.destroyItem = DestroySpeciesGridRow;
            speciesList.RegisterCallback<GeometryChangedEvent>(OnSpeciesListGeometryChanged);
            introducedFormsList.virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight;
            introducedFormsList.fixedItemHeight = 72f;
            introducedFormsList.selectionType = SelectionType.None;
            introducedFormsList.makeItem = MakeIntroducedFormRow;
            introducedFormsList.bindItem = BindIntroducedFormRow;
            introducedFormsList.unbindItem = UnbindIntroducedFormRow;
            introducedFormsList.destroyItem = DestroyIntroducedFormRow;
            cardList.virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight;
            cardList.fixedItemHeight = 148f;
            cardList.selectionType = SelectionType.None;
            cardList.makeItem = MakeRelatedCardGridRow;
            cardList.bindItem = BindRelatedCardGridRow;
            cardList.unbindItem = UnbindRelatedCardGridRow;
            cardList.destroyItem = DestroyRelatedCardGridRow;
            cardList.RegisterCallback<GeometryChangedEvent>(OnCardListGeometryChanged);
            searchField.RegisterValueChangedCallback(evt =>
            {
                if (!updatingControls && browser != null)
                    ScheduleSpeciesSearchRefresh(evt.newValue);
            });
            generationField.RegisterValueChangedCallback(_ =>
            {
                if (!updatingControls && browser != null &&
                    generationField.index >= 0 && generationField.index < generationIds.Count)
                    SelectGeneration(generationIds[generationField.index]);
            });
            cardSearchField.RegisterValueChangedCallback(evt =>
            {
                if (updatingControls)
                    return;
                cardSearch = (evt.newValue ?? string.Empty).Trim();
                ScheduleCardSearchRefresh();
            });
            cardSortField.RegisterValueChangedCallback(_ =>
            {
                if (updatingControls)
                    return;
                cardSortMode = Mathf.Max(0, cardSortField.index);
                RefreshCardGallery(true);
            });
        }

        private void ConfigureActions()
        {
            detailBackAction = BindAction("pokedex-detail-back", () => NavigateBack());
            formCardsAction = BindAction("pokedex-form-cards-button", () => ShowAllSpeciesCards(false));
            speciesCardsAction = BindAction("pokedex-species-cards-button", () => ShowAllSpeciesCards(true));
            manageDownloadsAction = BindAction("pokedex-manage-downloads-button", OpenContentManagement);
            emptyManageDownloadsAction = BindAction("pokedex-empty-manage-button", OpenContentManagement);
            errorRetryAction = BindAction("pokedex-error-retry", () => RetryOpen(), false);
            errorManageAction = BindAction("pokedex-error-manage", OpenContentManagement);
            errorCloseAction = BindAction("pokedex-error-close", Close);
        }

        private MobileActionControl BindAction(string name, Action callback, bool playFeedback = true)
        {
            VisualElement actionRoot = Required<VisualElement>(name);
            return new MobileActionControl(
                actionRoot,
                callback,
                playFeedback,
                feedbackLabel: actionRoot.Q<Label>());
        }

        private void OpenContentManagement()
        {
            if (manageDownloads == null || navigationRequested)
                return;
            navigationRequested = true;
            primaryNavigation?.SetPending(MobileDestination.Content);
            ApplyActionAvailability();
            try
            {
                manageDownloads();
            }
            catch
            {
                navigationRequested = false;
                primaryNavigation?.ClearPending(MobileDestination.Collection);
                ApplyActionAvailability();
                throw;
            }
        }

        private void ApplyActionAvailability()
        {
            bool available = !navigationRequested;
            body?.EnableInClassList("is-pending", navigationRequested);
            if (body != null)
                body.pickingMode = available ? PickingMode.Position : PickingMode.Ignore;
            closeAction?.SetEnabled(available);
            detailBackAction?.SetEnabled(available && IsReady);
            formCardsAction?.SetEnabled(available && IsReady);
            speciesCardsAction?.SetEnabled(available && IsReady);
            manageDownloadsAction?.SetEnabled(available && manageDownloads != null);
            emptyManageDownloadsAction?.SetEnabled(available && manageDownloads != null);
            errorRetryAction?.SetEnabled(available);
            errorManageAction?.SetEnabled(available && manageDownloads != null);
            errorCloseAction?.SetEnabled(available);
            foreach (MobileActionControl action in formActions)
                action.SetEnabled(available && IsReady);
            if (root != null)
            {
                foreach (VisualElement element in root.Query<VisualElement>(className: "pokedex-species-tile").ToList())
                    if (element.userData is SpeciesTile tile)
                        tile.Action.SetEnabled(available && tile.Species != null);
                foreach (VisualElement element in root.Query<VisualElement>(className: "pokedex-form-row").ToList())
                    if (element.userData is IntroducedFormRow row)
                        row.Action.SetEnabled(available && row.Form != null);
                foreach (VisualElement element in root.Query<VisualElement>(className: "pokedex-card-tile").ToList())
                    if (element.userData is RelatedCardTile tile)
                        tile.Action.SetEnabled(available && tile.Item != null);
            }
            searchField?.SetEnabled(available && IsReady);
            generationField?.SetEnabled(available && IsReady);
            cardSearchField?.SetEnabled(available && IsReady);
            cardSortField?.SetEnabled(available && IsReady);
        }

        private VisualElement MakeSpeciesGridRow()
        {
            var root = new VisualElement();
            root.AddToClassList("pokedex-species-grid-row");
            SpeciesTile first = MakeSpeciesTile(0);
            SpeciesTile second = MakeSpeciesTile(1);
            root.Add(first.Root);
            root.Add(second.Root);
            root.userData = new SpeciesGridRow { Tiles = new[] { first, second } };
            return root;
        }

        private SpeciesTile MakeSpeciesTile(int slot)
        {
            var tile = new SpeciesTile();
            tile.Root = new VisualElement { name = "pokedex-species-tile-" + slot };
            tile.Root.AddToClassList("pokedex-species-tile");
            tile.Number = new Label();
            tile.Number.AddToClassList("pokedex-row__number");
            tile.Name = new Label();
            tile.Name.AddToClassList("pokedex-row__name");
            tile.Genus = new Label();
            tile.Genus.AddToClassList("pokedex-row__genus");
            tile.Root.Add(tile.Number);
            tile.Root.Add(tile.Name);
            tile.Root.Add(tile.Genus);
            tile.Action = new MobileActionControl(
                tile.Root,
                () => ActivateSpeciesTile(tile),
                feedbackLabel: tile.Name);
            tile.Root.userData = tile;
            return tile;
        }

        private void ActivateSpeciesTile(SpeciesTile tile)
        {
            if (tile?.Species == null || navigationRequested)
                return;
            OpenSpecies(tile.Species.Id);
        }

        private void BindSpeciesGridRow(VisualElement element, int index)
        {
            if (index < 0 || index >= speciesGridLines.Count)
                return;
            SpeciesGridLine line = speciesGridLines[index];
            var row = (SpeciesGridRow)element.userData;
            BindSpeciesTile(row.Tiles[0], line.First);
            BindSpeciesTile(row.Tiles[1], line.Second);
        }

        private void BindSpeciesTile(SpeciesTile tile, PokemonSpeciesDefinition species)
        {
            tile.Species = species;
            bool visible = species != null;
            tile.Root.EnableInClassList("is-hidden", !visible);
            tile.Action.SetEnabled(visible && !navigationRequested);
            if (!visible)
                return;
            tile.Number.text = "#" + species.NationalDexNumber.ToString("000");
            tile.Name.text = PokemonPokedexBrowser.Localized(species.Names, UiLanguage);
            tile.Genus.text = PokemonPokedexBrowser.Localized(species.Genera, UiLanguage);
            tile.Root.tooltip = tile.Name.text;
        }

        private static void UnbindSpeciesGridRow(VisualElement element, int index)
        {
            if (!(element.userData is SpeciesGridRow row))
                return;
            foreach (SpeciesTile tile in row.Tiles)
            {
                tile.Species = null;
                tile.Action.SetEnabled(false);
            }
        }

        private static void DestroySpeciesGridRow(VisualElement element)
        {
            if (!(element.userData is SpeciesGridRow row))
                return;
            foreach (SpeciesTile tile in row.Tiles)
                tile.Action.Dispose();
        }

        private VisualElement MakeIntroducedFormRow()
        {
            var row = new VisualElement();
            row.AddToClassList("pokedex-form-row");
            var number = new Label();
            number.AddToClassList("pokedex-row__number");
            var copy = new VisualElement();
            copy.AddToClassList("pokedex-row__copy");
            var name = new Label();
            name.AddToClassList("pokedex-row__name");
            var metadata = new Label();
            metadata.AddToClassList("pokedex-row__genus");
            copy.Add(name);
            copy.Add(metadata);
            row.Add(number);
            row.Add(copy);
            var view = new IntroducedFormRow { Number = number, Name = name, Metadata = metadata };
            view.Action = new MobileActionControl(
                row,
                () => ActivateIntroducedForm(view),
                feedbackLabel: name);
            row.userData = view;
            return row;
        }

        private void ActivateIntroducedForm(IntroducedFormRow row)
        {
            if (row?.Form == null || navigationRequested)
                return;
            OpenSpeciesForm(row.Form.SpeciesId, row.Form.Id);
        }

        private void BindIntroducedFormRow(VisualElement element, int index)
        {
            if (index < 0 || index >= visibleIntroducedForms.Count)
                return;
            PokemonFormDefinition form = visibleIntroducedForms[index];
            PokemonSpeciesDefinition species = browser.GetSpecies(form.SpeciesId);
            var row = (IntroducedFormRow)element.userData;
            row.Form = form;
            row.Action.SetEnabled(!navigationRequested);
            row.Number.text = "#" + species.NationalDexNumber.ToString("000");
            row.Name.text = PokemonPokedexBrowser.Localized(form.Names, UiLanguage);
            row.Metadata.text = string.IsNullOrWhiteSpace(form.RegionId)
                ? PokemonPokedexText.FormKindName(form.FormKind, UiLanguage)
                : PokemonPokedexText.RegionName(form.RegionId, UiLanguage);
            element.tooltip = row.Name.text;
        }

        private static void UnbindIntroducedFormRow(VisualElement element, int index)
        {
            if (!(element.userData is IntroducedFormRow row))
                return;
            row.Form = null;
            row.Action.SetEnabled(false);
        }

        private static void DestroyIntroducedFormRow(VisualElement element)
        {
            if (element.userData is IntroducedFormRow row)
                row.Action.Dispose();
        }

        private VisualElement MakeRelatedCardGridRow()
        {
            var row = new VisualElement();
            row.AddToClassList("pokedex-card-grid-row");
            RelatedCardTile first = MakeRelatedCardTile(0);
            RelatedCardTile second = MakeRelatedCardTile(1);
            row.Add(first.Root);
            row.Add(second.Root);
            row.userData = new RelatedCardGridRow { Tiles = new[] { first, second } };
            return row;
        }

        private RelatedCardTile MakeRelatedCardTile(int slot)
        {
            var tile = new RelatedCardTile();
            tile.Root = new VisualElement { name = "pokedex-card-tile-" + slot };
            tile.Root.AddToClassList("pokedex-card-tile");
            AsyncCardImageView image = null;
            if (cardTextureCache != null)
            {
                image = new AsyncCardImageView(cardTextureCache);
                cardImageViews.Add(image);
                tile.Root.Add(image.Element);
            }
            var copy = new VisualElement();
            copy.AddToClassList("pokedex-card-row__copy");
            var name = new Label();
            name.AddToClassList("pokedex-card-row__name");
            var metadata = new Label();
            metadata.AddToClassList("pokedex-card-row__metadata");
            copy.Add(name);
            copy.Add(metadata);
            var installStatus = new Label();
            installStatus.AddToClassList("pokedex-card-row__status");
            tile.Root.Add(copy);
            tile.Root.Add(installStatus);
            tile.Image = image;
            tile.Name = name;
            tile.Metadata = metadata;
            tile.Status = installStatus;
            tile.Action = new MobileActionControl(
                tile.Root,
                () => ActivateRelatedCard(tile),
                feedbackLabel: name);
            tile.Root.userData = tile;
            return tile;
        }

        private void ActivateRelatedCard(RelatedCardTile tile)
        {
            if (tile?.Item == null || navigationRequested)
                return;
            OpenRelatedCard(tile.Item);
        }

        private void BindRelatedCardGridRow(VisualElement element, int index)
        {
            if (index < 0 || index >= relatedCardGridLines.Count)
                return;
            RelatedCardGridLine line = relatedCardGridLines[index];
            var row = (RelatedCardGridRow)element.userData;
            BindRelatedCardTile(row.Tiles[0], line.First);
            BindRelatedCardTile(row.Tiles[1], line.Second);
        }

        private void BindRelatedCardTile(RelatedCardTile tile, RelatedCardItem item)
        {
            tile.Item = item;
            bool visible = item != null;
            tile.Root.EnableInClassList("is-hidden", !visible);
            tile.Action.SetEnabled(visible && !navigationRequested);
            if (!visible)
            {
                tile.Image?.Unbind();
                return;
            }
            tile.Name.text = item.Link.CardName;
            tile.Metadata.text = item.Link.SetId + " · #" + item.Link.LocalId;
            bool installed = item.Printing != null;
            tile.Status.text = PokemonPokedexText.Get(
                installed ? "card_installed" : "card_not_installed", UiLanguage);
            tile.Root.EnableInClassList("is-not-installed", !installed);
            if (installed && !string.IsNullOrWhiteSpace(item.Printing.ImageRelativePath))
                tile.Image?.Bind(item.Printing);
            else
                tile.Image?.Unbind();
            tile.Root.tooltip = item.Link.CardId;
        }

        private static void UnbindRelatedCardGridRow(VisualElement element, int index)
        {
            if (!(element.userData is RelatedCardGridRow row))
                return;
            foreach (RelatedCardTile tile in row.Tiles)
            {
                tile.Item = null;
                tile.Action.SetEnabled(false);
                tile.Image?.Unbind();
            }
        }

        private void DestroyRelatedCardGridRow(VisualElement element)
        {
            if (!(element.userData is RelatedCardGridRow row))
                return;
            foreach (RelatedCardTile tile in row.Tiles)
            {
                if (tile.Image != null)
                {
                    tile.Image.Dispose();
                    cardImageViews.Remove(tile.Image);
                }
                tile.Action.Dispose();
                tile.Item = null;
            }
        }

        private void RefreshSpeciesList(bool animate)
        {
            if (browser == null)
                return;
            visibleSpecies.Clear();
            visibleSpecies.AddRange(browser.VisibleSpecies);
            speciesGridLines.Clear();
            for (int index = 0; index < visibleSpecies.Count; index += 2)
            {
                speciesGridLines.Add(new SpeciesGridLine
                {
                    First = visibleSpecies[index],
                    Second = index + 1 < visibleSpecies.Count ? visibleSpecies[index + 1] : null
                });
            }
            visibleIntroducedForms.Clear();
            visibleIntroducedForms.AddRange(browser.VisibleIntroducedForms);
            speciesList.itemsSource = speciesGridLines;
            speciesList.ClearSelection();
            speciesList.RefreshItems();
            introducedFormsList.itemsSource = visibleIntroducedForms;
            introducedFormsList.ClearSelection();
            introducedFormsList.RefreshItems();
            introducedFormsHeading.text = PokemonPokedexText.Format(
                "new_forms", UiLanguage, visibleIntroducedForms.Count);
            SetVisible(introducedFormsSection, visibleIntroducedForms.Count > 0);
            status.text = PokemonPokedexText.Format("count", UiLanguage, visibleSpecies.Count);
            empty.text = PokemonPokedexText.Get("empty", UiLanguage);
            SetVisible(Required<VisualElement>("pokedex-empty-state"), visibleSpecies.Count == 0);
            if (animate && !UIFeedbackService.ReduceMotion)
            {
                speciesListAnimation?.Pause();
                speciesList.style.opacity = 0.55f;
                speciesListAnimation = speciesList.schedule.Execute(() =>
                {
                    speciesList.style.opacity = 1f;
                    speciesListAnimation = null;
                });
                speciesListAnimation.ExecuteLater(Mathf.RoundToInt(100f / UIFeedbackService.AnimationSpeed));
            }
        }

        private void RefreshDetails()
        {
            PokemonSpeciesDefinition species = browser.SelectedSpecies;
            PokemonFormDefinition form = browser.SelectedForm;
            string language = UiLanguage;
            detailNumber.text = PokemonPokedexText.Format("number", language, species.NationalDexNumber);
            detailName.text = PokemonPokedexBrowser.Localized(
                form?.Names?.Count > 0 ? form.Names : species.Names, language);
            detailGenus.text = PokemonPokedexBrowser.Localized(species.Genera, language);
            detailDebut.text = PokemonPokedexText.Format(
                "debut", language,
                PokemonPokedexBrowser.Localized(
                    browser.Generations.First(value => value.Id == species.DebutGenerationId).Names,
                    language));
            detailTypes.text = PokemonPokedexText.Format(
                "types", language,
                string.Join(" / ", form.TypeIds.Select(value => PokemonPokedexText.TypeName(value, language))));
            detailRegion.text = string.IsNullOrWhiteSpace(form.RegionId)
                ? string.Empty
                : PokemonPokedexText.Format(
                    "region", language, PokemonPokedexText.RegionName(form.RegionId, language));
            detailRegion.style.display = string.IsNullOrWhiteSpace(form.RegionId)
                ? DisplayStyle.None
                : DisplayStyle.Flex;
            detailDescription.text = PokemonPokedexBrowser.Localized(species.Descriptions, language);
            detailArtNumber.text = "#" + species.NationalDexNumber.ToString("000");
            detailArtStatus.text = PokemonPokedexText.Get("art_pending", language) + "\n" +
                                   PokemonPokedexText.Get("art_hint", language);
            BindArtwork(form);
            formsHeading.text = PokemonPokedexText.Get("forms", language);
            cardsHeading.text = PokemonPokedexText.Get("cards", language);
            cardsCount.text = PokemonPokedexText.Format(
                "card_count", language,
                browser.GetSpeciesCards(species.Id).Count,
                browser.GetFormCards(form.Id).Count);
            detailBackAction.SetLabel(PokemonPokedexText.Get("back", language));
            RebuildFormButtons();
            RefreshCardGallery(false);
        }

        private void BindArtwork(PokemonFormDefinition form)
        {
            if (artworkView == null)
                return;
            PokemonArtworkEntry entry = FindArtwork(form);
            string relativePath = entry == null
                ? null
                : form.IntroducedGenerationId + "/" + entry.RelativePath;
            var printing = new PrintingDefinition(
                "pokemon-artwork:" + form.Id,
                "pokemon-artwork-item:" + form.Id,
                new PrintingIdentity(
                    "pokemon",
                    "pokedex",
                    form.PokemonId.ToString(),
                    "und",
                    "artwork"),
                "artwork",
                form.Names,
                relativePath,
                entry?.Sha256);
            artworkView.Bind(printing);
            detailArtNumber.style.display = entry == null ? DisplayStyle.Flex : DisplayStyle.None;
            detailArtStatus.style.display = DisplayStyle.None;
        }

        public void ShowAllSpeciesCards(bool value)
        {
            if (browser?.SelectedSpecies == null)
                return;
            CancelCardSearchRefresh();
            showAllSpeciesCards = value;
            UIFeedbackService.Play(FeedbackCue.Confirm);
            RefreshCardGallery(true);
        }

        public void SetCardSearch(string value)
        {
            CancelCardSearchRefresh();
            cardSearch = (value ?? string.Empty).Trim();
            updatingControls = true;
            cardSearchField.SetValueWithoutNotify(cardSearch);
            updatingControls = false;
            RefreshCardGallery(true);
        }

        private void ScheduleSpeciesSearchRefresh(string value)
        {
            CancelSpeciesSearchRefresh();
            string query = value ?? string.Empty;
            speciesSearchRefresh = speciesList.schedule.Execute(() =>
            {
                speciesSearchRefresh = null;
                if (browser == null)
                    return;
                browser.Search(query);
                RefreshSpeciesList(true);
            });
            speciesSearchRefresh.ExecuteLater(SearchDebounceMilliseconds);
        }

        private void CancelSpeciesSearchRefresh()
        {
            speciesSearchRefresh?.Pause();
            speciesSearchRefresh = null;
        }

        private void ScheduleCardSearchRefresh()
        {
            CancelCardSearchRefresh();
            cardSearchRefresh = cardList.schedule.Execute(() =>
            {
                cardSearchRefresh = null;
                RefreshCardGallery(true);
            });
            cardSearchRefresh.ExecuteLater(SearchDebounceMilliseconds);
        }

        private void CancelCardSearchRefresh()
        {
            cardSearchRefresh?.Pause();
            cardSearchRefresh = null;
        }

        public bool OpenRelatedCard(int index)
        {
            if (index < 0 || index >= visibleCards.Count)
                return false;
            return OpenRelatedCard(visibleCards[index]);
        }

        private bool OpenRelatedCard(RelatedCardItem item)
        {
            cardList.ClearSelection();
            if (item?.Printing == null)
            {
                status.text = PokemonPokedexText.Get("card_not_installed", UiLanguage);
                status.AddToClassList("is-error");
                UIFeedbackService.Play(FeedbackCue.Error);
                return false;
            }
            if (openPrintingDetails == null || !openPrintingDetails(item.Printing.Id))
            {
                status.text = PokemonPokedexText.Get("card_not_installed", UiLanguage);
                status.AddToClassList("is-error");
                UIFeedbackService.Play(FeedbackCue.Error);
                return false;
            }
            UIFeedbackService.Play(FeedbackCue.CardFlip, true);
            Close();
            return true;
        }

        private void ResetCardGallery()
        {
            showAllSpeciesCards = false;
            cardSearch = string.Empty;
            cardSortMode = 0;
            updatingControls = true;
            cardSearchField.SetValueWithoutNotify(string.Empty);
            cardSortField.index = 0;
            updatingControls = false;
        }

        private void RefreshCardGallery(bool animate)
        {
            if (browser?.SelectedSpecies == null)
                return;
            IEnumerable<PokemonCardSubjectLink> links = showAllSpeciesCards
                ? browser.GetSpeciesCards(browser.SelectedSpecies.Id)
                : browser.GetFormCards(browser.SelectedForm.Id);
            if (!string.IsNullOrWhiteSpace(cardSearch))
            {
                links = links.Where(value =>
                    value.CardName.IndexOf(cardSearch, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    value.SetId.IndexOf(cardSearch, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    value.LocalId.IndexOf(cardSearch, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            links = cardSortMode == 1
                ? links.OrderBy(value => value.CardName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(value => value.SetId, StringComparer.Ordinal)
                    .ThenBy(value => value.LocalId, StringComparer.Ordinal)
                : links.OrderBy(value => value.SetId, StringComparer.Ordinal)
                    .ThenBy(value => value.LocalId, StringComparer.Ordinal)
                    .ThenBy(value => value.CardName, StringComparer.OrdinalIgnoreCase);
            visibleCards.Clear();
            foreach (PokemonCardSubjectLink link in links)
            {
                PrintingDefinition printing = link.PrintingIds
                    .Select(id => runtimeCatalog.Printings.TryGetValue(id, out PrintingDefinition value) ? value : null)
                    .FirstOrDefault(value => value != null);
                visibleCards.Add(new RelatedCardItem { Link = link, Printing = printing });
            }
            relatedCardGridLines.Clear();
            for (int index = 0; index < visibleCards.Count; index += 2)
            {
                relatedCardGridLines.Add(new RelatedCardGridLine
                {
                    First = visibleCards[index],
                    Second = index + 1 < visibleCards.Count ? visibleCards[index + 1] : null
                });
            }
            cardList.itemsSource = relatedCardGridLines;
            cardList.ClearSelection();
            cardList.RefreshItems();
            formCardsAction.Root.EnableInClassList("is-selected", !showAllSpeciesCards);
            speciesCardsAction.Root.EnableInClassList("is-selected", showAllSpeciesCards);
            formCardsAction.SetSelected(!showAllSpeciesCards);
            speciesCardsAction.SetSelected(showAllSpeciesCards);
            string emptyKey = showAllSpeciesCards ? "card_empty_species" : "card_empty_form";
            cardEmpty.text = PokemonPokedexText.Get(emptyKey, UiLanguage);
            SetVisible(Required<VisualElement>("pokedex-card-empty-state"), visibleCards.Count == 0);
            if (animate && !UIFeedbackService.ReduceMotion)
            {
                cardListAnimation?.Pause();
                cardList.style.opacity = 0.45f;
                cardListAnimation = cardList.schedule.Execute(() =>
                {
                    cardList.style.opacity = 1f;
                    cardListAnimation = null;
                });
                cardListAnimation.ExecuteLater(Mathf.RoundToInt(120f / UIFeedbackService.AnimationSpeed));
            }
        }

        private PokemonArtworkEntry FindArtwork(PokemonFormDefinition form)
        {
            string generationId = form.IntroducedGenerationId;
            if (!artworkCatalogs.TryGetValue(generationId, out PokemonArtworkCatalog catalog) &&
                !missingArtworkCatalogs.Contains(generationId))
            {
                string manifestPath = Path.Combine(ArtworkRoot, generationId, "manifest.json");
                try
                {
                    catalog = new PokemonArtworkManifestReader().LoadFile(manifestPath);
                    if (!string.Equals(catalog.TaxonomySourceSha256, taxonomySourceSha256, StringComparison.Ordinal))
                        throw new InvalidDataException("Installed artwork targets a different taxonomy snapshot.");
                    artworkCatalogs[generationId] = catalog;
                }
                catch (Exception exception) when (
                    exception is IOException || exception is UnauthorizedAccessException ||
                    exception is InvalidDataException || exception is ArgumentException)
                {
                    missingArtworkCatalogs.Add(generationId);
                    Debug.LogWarning($"Pokédex artwork package is unavailable for {generationId}: {exception.Message}");
                }
            }
            return catalog?.Find(form.Id);
        }

        public bool OpenSpeciesForm(string speciesId, string formId)
        {
            if (!EnsureReady() || !browser.OpenSpecies(speciesId, formId))
                return false;
            returnListIndex = visibleSpecies.FindIndex(value => value.Id == speciesId);
            ResetCardGallery();
            RefreshDetails();
            listPage.style.display = DisplayStyle.None;
            detailPage.style.display = DisplayStyle.Flex;
            ScheduleCardLayoutRefresh();
            UIFeedbackService.Play(FeedbackCue.CardFlip, true);
            AnimateDetails();
            return true;
        }

        private void RebuildFormButtons()
        {
            DisposeFormActions();
            formStrip.Clear();
            foreach (PokemonFormDefinition form in browser.SelectableForms)
            {
                var action = new MobileActionControl(
                    "pokedex-form-" + form.Id.Replace(':', '-'),
                    PokemonPokedexBrowser.Localized(form.Names, UiLanguage),
                    () => OpenForm(form.Id),
                    MobileActionTone.Quiet);
                action.Root.tooltip = PokemonPokedexBrowser.Localized(form.Names, UiLanguage);
                action.Root.userData = action;
                action.Root.AddToClassList("pokedex-form-action");
                action.Root.EnableInClassList("is-selected", form.Id == browser.SelectedForm.Id);
                action.SetSelected(form.Id == browser.SelectedForm.Id);
                action.SetEnabled(!navigationRequested);
                formActions.Add(action);
                formStrip.Add(action.Root);
            }
            SetVisible(Required<VisualElement>("pokedex-forms-section"), browser.SelectableForms.Count > 1);
        }

        private void DisposeFormActions()
        {
            foreach (MobileActionControl action in formActions)
                action.Dispose();
            formActions.Clear();
        }

        public bool NavigateBack()
        {
            if (browser.CanNavigateBack && browser.NavigateBack())
            {
                RefreshDetails();
                UIFeedbackService.Play(FeedbackCue.Back);
                AnimateDetails();
                return true;
            }
            ShowList();
            if (returnListIndex >= 0 && returnListIndex < visibleSpecies.Count)
                speciesList.ScrollToItem(returnListIndex / 2);
            UIFeedbackService.Play(FeedbackCue.Back);
            return false;
        }

        private void ShowList()
        {
            listPage.style.display = DisplayStyle.Flex;
            detailPage.style.display = DisplayStyle.None;
        }

        private void BuildGenerationChoices()
        {
            generationIds.Clear();
            generationIds.AddRange(browser.Generations.Select(value => value.Id));
            RefreshGenerationChoices();
        }

        private void RefreshGenerationChoices()
        {
            if (browser == null)
                return;
            updatingControls = true;
            generationField.choices = browser.Generations
                .Select(value => PokemonPokedexBrowser.Localized(value.Names, UiLanguage))
                .ToList();
            generationField.index = generationIds.IndexOf(browser.GenerationId);
            generationField.label = PokemonPokedexText.Get("generation", UiLanguage);
            updatingControls = false;
        }

        private void RefreshLocalizedContent()
        {
            updatingControls = true;
            mobileTopBar.SetText(
                PokemonPokedexText.Get("title", UiLanguage),
                PokemonPokedexText.Get("subtitle", UiLanguage));
            closeAction?.SetLabel(PokemonPokedexText.Get("close", UiLanguage));
            searchField.label = PokemonPokedexText.Get("search", UiLanguage);
            detailBackAction?.SetLabel(PokemonPokedexText.Get("back", UiLanguage));
            formCardsAction?.SetLabel(PokemonPokedexText.Get("card_scope_form", UiLanguage));
            speciesCardsAction?.SetLabel(PokemonPokedexText.Get("card_scope_species", UiLanguage));
            manageDownloadsAction?.SetLabel(PokemonPokedexText.Get("manage_downloads", UiLanguage));
            SetVisible(manageDownloadsAction?.Root, manageDownloads != null);
            emptyManageDownloadsAction?.SetLabel(PokemonPokedexText.Get("manage_downloads", UiLanguage));
            if (IsReady)
                SetVisible(emptyManageDownloadsAction?.Root, false);
            errorPresenter?.RefreshLanguage();
            cardSearchField.label = PokemonPokedexText.Get("card_search", UiLanguage);
            cardSearchField.SetValueWithoutNotify(cardSearch);
            cardSortField.label = PokemonPokedexText.Get("card_sort", UiLanguage);
            cardSortField.choices = new List<string>
            {
                PokemonPokedexText.Get("card_sort_set", UiLanguage),
                PokemonPokedexText.Get("card_sort_name", UiLanguage)
            };
            cardSortField.index = cardSortMode;
            updatingControls = false;
            RefreshGenerationChoices();
            primaryNavigation?.RefreshText();
            RefreshSpeciesList(false);
            if (browser?.SelectedSpecies != null && detailPage.resolvedStyle.display == DisplayStyle.Flex)
                RefreshDetails();
            ApplyActionAvailability();
        }


        private void OnUiLanguageChanged(string _)
        {
            RefreshLocalizedContent();
        }

        private void OnCardLanguageChanged(ContentLanguageSelection selection)
        {
            if (!IsReady || SnapshotOverride != null)
                return;
            try
            {
                string generationId = browser.GenerationId;
                string speciesId = browser.SelectedSpecies?.Id;
                string formId = browser.SelectedForm?.Id;
                PokemonPokedexSnapshotBundle snapshot = new PokemonPokedexSnapshotRepository().Load(
                    TaxonomyPath, CardSubjectPathForLanguage(selection.ResolvedLanguageId));
                var replacement = new PokemonPokedexBrowser(snapshot.Catalog, snapshot.SubjectCatalog);
                if (replacement.Generations.Any(value => value.Id == generationId))
                    replacement.SelectGeneration(generationId);
                if (speciesId != null)
                    replacement.OpenSpecies(speciesId, formId);
                browser = replacement;
                taxonomySourceSha256 = snapshot.Taxonomy.SourceSha256;
                LoadedCardLanguageId = snapshot.CardSubjects.Language;
                BuildGenerationChoices();
                RefreshSpeciesList(false);
                if (browser.SelectedSpecies != null &&
                    detailPage.resolvedStyle.display == DisplayStyle.Flex)
                    RefreshDetails();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Pokédex card language could not be changed: " + exception.Message);
                status.text = string.Empty;
                status.RemoveFromClassList("is-error");
                errorPresenter?.Show(PlayerUiErrorMapper.FromException(exception));
            }
        }

        private void AnimateOpen()
        {
            transitionAnimation?.Pause();
            if (UIFeedbackService.ReduceMotion)
            {
                root.style.opacity = 1f;
                return;
            }
            float start = Time.realtimeSinceStartup;
            float duration = 0.22f / UIFeedbackService.AnimationSpeed;
            root.style.opacity = 0f;
            transitionAnimation = root.schedule.Execute(() =>
            {
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Time.realtimeSinceStartup - start) / duration));
                root.style.opacity = t;
                if (t >= 1f)
                {
                    transitionAnimation?.Pause();
                    transitionAnimation = null;
                }
            }).Every(16);
        }

        private void AnimateDetails()
        {
            if (UIFeedbackService.ReduceMotion)
            {
                detailPage.style.opacity = 1f;
                return;
            }
            detailPage.style.opacity = 0.35f;
            detailAnimation?.Pause();
            detailAnimation = detailPage.schedule.Execute(() =>
            {
                detailPage.style.opacity = 1f;
                detailAnimation = null;
            });
            detailAnimation.ExecuteLater(Mathf.RoundToInt(140f / UIFeedbackService.AnimationSpeed));
        }

        private void CancelPresentationAnimations()
        {
            speciesListAnimation?.Pause();
            speciesListAnimation = null;
            cardListAnimation?.Pause();
            cardListAnimation = null;
            detailAnimation?.Pause();
            detailAnimation = null;
            if (speciesList != null)
                speciesList.style.opacity = 1f;
            if (cardList != null)
                cardList.style.opacity = 1f;
            if (detailPage != null)
                detailPage.style.opacity = 1f;
        }

        private void ScheduleSpeciesLayoutRefresh()
        {
            speciesLayoutRefresh?.Pause();
            speciesLayoutRefresh = root.schedule.Execute(() =>
            {
                speciesLayoutRefresh = null;
                speciesList.Rebuild();
                introducedFormsList.Rebuild();
            });
        }

        private void OnSpeciesListGeometryChanged(GeometryChangedEvent evt)
        {
            if (evt.newRect.height > 0f &&
                Mathf.Abs(evt.newRect.height - evt.oldRect.height) > 0.5f &&
                speciesGridLines.Count > 0)
                ScheduleSpeciesLayoutRefresh();
        }

        private void ScheduleCardLayoutRefresh()
        {
            cardLayoutRefresh?.Pause();
            cardLayoutRefresh = root.schedule.Execute(() =>
            {
                cardLayoutRefresh = null;
                cardList.Rebuild();
            });
        }

        private void OnCardListGeometryChanged(GeometryChangedEvent evt)
        {
            if (evt.newRect.height > 0f &&
                Mathf.Abs(evt.newRect.height - evt.oldRect.height) > 0.5f &&
                visibleCards.Count > 0)
                ScheduleCardLayoutRefresh();
        }

        private void CancelLayoutRefreshes()
        {
            speciesLayoutRefresh?.Pause();
            speciesLayoutRefresh = null;
            cardLayoutRefresh?.Pause();
            cardLayoutRefresh = null;
        }

        private void OnApplicationPause(bool paused)
        {
            if (!paused)
                return;
            transitionAnimation?.Pause();
            transitionAnimation = null;
            CancelSpeciesSearchRefresh();
            CancelCardSearchRefresh();
            CancelPresentationAnimations();
            CancelLayoutRefreshes();
        }

        private static void SetVisible(VisualElement element, bool visible)
        {
            if (element == null)
                return;
            element.EnableInClassList("is-hidden", !visible);
        }

        private static void DisposeAction(ref MobileActionControl action)
        {
            action?.Dispose();
            action = null;
        }

        private string UiLanguage =>
            ApplicationServices.IsConfigured ? ApplicationServices.Languages.UiLanguageId : "en";

        private static string ProjectRoot => Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
        private static string TaxonomyPath
        {
            get
            {
#if UNITY_EDITOR
                return Path.Combine(ProjectRoot, "LocalContent", "Pokedex", "snapshot", "pokemon-taxonomy.json");
#else
                return Path.Combine(UnityEngine.Application.persistentDataPath, "Content", "pokedex", "taxonomy", "pokemon-taxonomy.json");
#endif
            }
        }

        public static string CardSubjectSnapshotFileName(string cardLanguageId)
        {
            string normalized = NormalizeCardLanguageId(cardLanguageId);
            return $"pokemon-card-subject-links.{normalized}.json";
        }

        public static string NormalizeCardLanguageId(string cardLanguageId)
        {
            string normalized = (cardLanguageId ?? string.Empty).Trim().ToLowerInvariant();
            return normalized switch
            {
                "ja" => "ja",
                "zh" => "zh-cn",
                "zh-cn" => "zh-cn",
                "zh-hans" => "zh-cn",
                _ => "en"
            };
        }

        private static string CardSubjectPath => CardSubjectPathForLanguage(
            ApplicationServices.IsConfigured
                ? ApplicationServices.Languages.ContentLanguage.ResolvedLanguageId
                : "en");

        private static string CardSubjectPathForLanguage(string cardLanguageId)
        {
            string language = NormalizeCardLanguageId(cardLanguageId);
            string fileName = CardSubjectSnapshotFileName(language);
#if UNITY_EDITOR
            return Path.Combine(ProjectRoot, "LocalContent", "Pokedex", "links", fileName);
#else
            return Path.Combine(UnityEngine.Application.persistentDataPath, "Content", "pokedex", "links", language, fileName);
#endif
        }

        private static string ArtworkRoot
        {
            get
            {
#if UNITY_EDITOR
                return Path.Combine(ProjectRoot, "LocalContent", "Pokedex", "artwork");
#else
                return Path.Combine(UnityEngine.Application.persistentDataPath, "Content", "pokedex", "artwork");
#endif
            }
        }
    }
}
