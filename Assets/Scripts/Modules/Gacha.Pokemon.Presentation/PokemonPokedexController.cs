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

        private sealed class SpeciesRow
        {
            public Label Number;
            public Label Name;
            public Label Genus;
        }

        private sealed class IntroducedFormRow
        {
            public Label Number;
            public Label Name;
            public Label Metadata;
        }

        private sealed class RelatedCardItem
        {
            public PokemonCardSubjectLink Link;
            public PrintingDefinition Printing;
        }

        private sealed class RelatedCardRow
        {
            public AsyncCardImageView Image;
            public Label Name;
            public Label Metadata;
            public Label Status;
        }

        private readonly List<PokemonSpeciesDefinition> visibleSpecies = new List<PokemonSpeciesDefinition>();
        private readonly List<PokemonFormDefinition> visibleIntroducedForms = new List<PokemonFormDefinition>();
        private readonly List<RelatedCardItem> visibleCards = new List<RelatedCardItem>();
        private readonly HashSet<AsyncCardImageView> cardImageViews = new HashSet<AsyncCardImageView>();
        private readonly List<string> generationIds = new List<string>();
        private readonly Dictionary<string, PokemonArtworkCatalog> artworkCatalogs =
            new Dictionary<string, PokemonArtworkCatalog>(StringComparer.Ordinal);
        private readonly HashSet<string> missingArtworkCatalogs = new HashSet<string>(StringComparer.Ordinal);
        private PokemonPokedexBrowser browser;
        private string taxonomySourceSha256;
        private UiToolkitSafeAreaBinding safeAreaBinding;
        private VisualElement root;
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
        private Button closeButton;
        private Button detailBackButton;
        private Button formCardsButton;
        private Button speciesCardsButton;
        private Button manageDownloadsButton;
        private Button emptyManageDownloadsButton;
        private IVisualElementScheduledItem transitionAnimation;
        private IVisualElementScheduledItem speciesSearchRefresh;
        private IVisualElementScheduledItem cardSearchRefresh;
        private CardTextureCache artworkCache;
        private AsyncCardImageView artworkView;
        private CardTextureCache cardTextureCache;
        private UniversalCatalog runtimeCatalog;
        private Func<string, bool> openPrintingDetails;
        private Action manageDownloads;
        private bool showAllSpeciesCards;
        private string cardSearch = string.Empty;
        private int cardSortMode;
        private bool attached;
        private bool updatingControls;
        private int returnListIndex = -1;

        public static PokemonPokedexSnapshotBundle SnapshotOverride { private get; set; }
        public bool IsReady { get; private set; }
        public bool IsOpen => root != null && root.resolvedStyle.display == DisplayStyle.Flex;
        public string InitializationError { get; private set; }
        public bool MissingContent { get; private set; }
        public string LoadedCardLanguageId { get; private set; }
        public int VisibleSpeciesCount => visibleSpecies.Count;
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
        public int InstalledVisibleCardCount => visibleCards.Count(value => value.Printing != null);
        public bool ShowingAllSpeciesCards => showAllSpeciesCards;
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
            Action manageDownloads = null)
        {
            if (attached)
                return;
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            this.openPrintingDetails = openPrintingDetails;
            this.manageDownloads = manageDownloads;
            VisualTreeAsset view = Resources.Load<VisualTreeAsset>("UI/PokedexView");
            if (view == null)
                throw new InvalidOperationException("PokedexView.uxml is missing from Resources/UI.");
            TemplateContainer content = view.Instantiate();
            root = content.Q<VisualElement>("pokedex-overlay");
            if (root == null)
                throw new InvalidOperationException("PokedexView.uxml has no pokedex-overlay root.");
            content.Remove(root);
            document.rootVisualElement.Add(root);
            safeAreaBinding = UiToolkitSafeArea.Attach(root);
            QueryElements();
            ConfigureControls();
            RefreshLocalizedContent();
            root.style.display = DisplayStyle.None;
            safeAreaBinding.Suspend();
            attached = true;
        }

        public bool Open()
        {
            if (!attached)
                return false;
            root.style.display = DisplayStyle.Flex;
            safeAreaBinding.Resume();
            root.BringToFront();
            if (!EnsureReady())
            {
                status.text = MissingContent
                    ? PokemonPokedexText.Get("content_missing", UiLanguage)
                    : PokemonPokedexText.Format("unavailable", UiLanguage, InitializationError);
                status.AddToClassList("is-error");
                emptyManageDownloadsButton.style.display = manageDownloads == null
                    ? DisplayStyle.None
                    : DisplayStyle.Flex;
                UIFeedbackService.Play(FeedbackCue.Error);
                AnimateOpen();
                return false;
            }
            status.RemoveFromClassList("is-error");
            ShowList();
            RefreshLocalizedContent();
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
            root.style.display = DisplayStyle.None;
            safeAreaBinding?.Suspend();
            UIFeedbackService.Play(FeedbackCue.Back);
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
                    throw new InvalidOperationException(catalogLoad.ErrorMessage);
                if (!catalogLoad.HasInstalledContent)
                {
                    MissingContent = true;
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
                if (ApplicationServices.IsConfigured)
                {
                    ApplicationServices.Languages.UiLanguageChanged += OnUiLanguageChanged;
                    ApplicationServices.Languages.ContentLanguageChanged += OnCardLanguageChanged;
                }
                IsReady = true;
                return true;
            }
            catch (Exception exception)
            {
                InitializationError = exception.Message;
                Debug.LogWarning("Pokédex could not be initialized: " + exception.Message);
                return false;
            }
        }

        private void OnDestroy()
        {
            safeAreaBinding?.Dispose();
            safeAreaBinding = null;
            if (ApplicationServices.IsConfigured)
            {
                ApplicationServices.Languages.UiLanguageChanged -= OnUiLanguageChanged;
                ApplicationServices.Languages.ContentLanguageChanged -= OnCardLanguageChanged;
            }
            transitionAnimation?.Pause();
            CancelSpeciesSearchRefresh();
            CancelCardSearchRefresh();
            artworkView?.Dispose();
            artworkView = null;
            artworkCache?.Dispose();
            artworkCache = null;
            foreach (AsyncCardImageView image in cardImageViews.ToArray())
                image.Dispose();
            cardImageViews.Clear();
            cardTextureCache?.Dispose();
            cardTextureCache = null;
        }

        private void QueryElements()
        {
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
            title = Required<Label>("pokedex-title");
            subtitle = Required<Label>("pokedex-subtitle");
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
            closeButton = Required<Button>("pokedex-close-button");
            detailBackButton = Required<Button>("pokedex-detail-back");
            formCardsButton = Required<Button>("pokedex-form-cards-button");
            speciesCardsButton = Required<Button>("pokedex-species-cards-button");
            manageDownloadsButton = Required<Button>("pokedex-manage-downloads-button");
            emptyManageDownloadsButton = Required<Button>("pokedex-empty-manage-button");
        }

        private T Required<T>(string name) where T : VisualElement =>
            root.Q<T>(name) ?? throw new InvalidOperationException("Missing Pokédex UI element: " + name);

        private void ConfigureControls()
        {
            speciesList.virtualizationMethod = CollectionVirtualizationMethod.FixedHeight;
            speciesList.fixedItemHeight = 92f;
            speciesList.selectionType = SelectionType.Single;
            speciesList.makeItem = MakeSpeciesRow;
            speciesList.bindItem = BindSpeciesRow;
            speciesList.selectionChanged += selection =>
            {
                PokemonSpeciesDefinition species = selection.OfType<PokemonSpeciesDefinition>().FirstOrDefault();
                if (species != null)
                    OpenSpecies(species.Id);
            };
            introducedFormsList.virtualizationMethod = CollectionVirtualizationMethod.FixedHeight;
            introducedFormsList.fixedItemHeight = 78f;
            introducedFormsList.selectionType = SelectionType.Single;
            introducedFormsList.makeItem = MakeIntroducedFormRow;
            introducedFormsList.bindItem = BindIntroducedFormRow;
            introducedFormsList.selectionChanged += selection =>
            {
                PokemonFormDefinition form = selection.OfType<PokemonFormDefinition>().FirstOrDefault();
                if (form != null)
                    OpenSpeciesForm(form.SpeciesId, form.Id);
            };
            cardList.virtualizationMethod = CollectionVirtualizationMethod.FixedHeight;
            cardList.fixedItemHeight = 150f;
            cardList.selectionType = SelectionType.Single;
            cardList.makeItem = MakeRelatedCardRow;
            cardList.bindItem = BindRelatedCardRow;
            cardList.unbindItem = UnbindRelatedCardRow;
            cardList.destroyItem = DestroyRelatedCardRow;
            cardList.selectionChanged += selection =>
            {
                RelatedCardItem item = selection.OfType<RelatedCardItem>().FirstOrDefault();
                if (item != null)
                    OpenRelatedCard(item);
            };
            closeButton.clicked += Close;
            detailBackButton.clicked += () => NavigateBack();
            formCardsButton.clicked += () => ShowAllSpeciesCards(false);
            speciesCardsButton.clicked += () => ShowAllSpeciesCards(true);
            manageDownloadsButton.clicked += () =>
            {
                UIFeedbackService.Play(FeedbackCue.Confirm);
                manageDownloads?.Invoke();
            };
            emptyManageDownloadsButton.clicked += () =>
            {
                UIFeedbackService.Play(FeedbackCue.Confirm);
                manageDownloads?.Invoke();
            };
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

        private VisualElement MakeSpeciesRow()
        {
            var row = new VisualElement();
            row.AddToClassList("pokedex-row");
            var number = new Label();
            number.AddToClassList("pokedex-row__number");
            var copy = new VisualElement();
            copy.AddToClassList("pokedex-row__copy");
            var name = new Label();
            name.AddToClassList("pokedex-row__name");
            var genus = new Label();
            genus.AddToClassList("pokedex-row__genus");
            copy.Add(name);
            copy.Add(genus);
            row.Add(number);
            row.Add(copy);
            row.userData = new SpeciesRow { Number = number, Name = name, Genus = genus };
            return row;
        }

        private void BindSpeciesRow(VisualElement element, int index)
        {
            if (index < 0 || index >= visibleSpecies.Count)
                return;
            PokemonSpeciesDefinition species = visibleSpecies[index];
            var row = (SpeciesRow)element.userData;
            row.Number.text = "#" + species.NationalDexNumber.ToString("000");
            row.Name.text = PokemonPokedexBrowser.Localized(species.Names, UiLanguage);
            row.Genus.text = PokemonPokedexBrowser.Localized(species.Genera, UiLanguage);
            element.tooltip = row.Name.text;
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
            row.userData = new IntroducedFormRow { Number = number, Name = name, Metadata = metadata };
            return row;
        }

        private void BindIntroducedFormRow(VisualElement element, int index)
        {
            if (index < 0 || index >= visibleIntroducedForms.Count)
                return;
            PokemonFormDefinition form = visibleIntroducedForms[index];
            PokemonSpeciesDefinition species = browser.GetSpecies(form.SpeciesId);
            var row = (IntroducedFormRow)element.userData;
            row.Number.text = "#" + species.NationalDexNumber.ToString("000");
            row.Name.text = PokemonPokedexBrowser.Localized(form.Names, UiLanguage);
            row.Metadata.text = string.IsNullOrWhiteSpace(form.RegionId)
                ? form.FormKind
                : form.RegionId;
            element.tooltip = form.Id;
        }

        private VisualElement MakeRelatedCardRow()
        {
            var row = new VisualElement();
            row.AddToClassList("pokedex-card-row");
            AsyncCardImageView image = null;
            if (cardTextureCache != null)
            {
                image = new AsyncCardImageView(cardTextureCache);
                cardImageViews.Add(image);
                row.Add(image.Element);
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
            row.Add(copy);
            row.Add(installStatus);
            row.userData = new RelatedCardRow
            {
                Image = image,
                Name = name,
                Metadata = metadata,
                Status = installStatus
            };
            return row;
        }

        private void BindRelatedCardRow(VisualElement element, int index)
        {
            if (index < 0 || index >= visibleCards.Count)
                return;
            RelatedCardItem item = visibleCards[index];
            var row = (RelatedCardRow)element.userData;
            row.Name.text = item.Link.CardName;
            row.Metadata.text = item.Link.SetId + " · #" + item.Link.LocalId;
            bool installed = item.Printing != null;
            row.Status.text = PokemonPokedexText.Get(
                installed ? "card_installed" : "card_not_installed", UiLanguage);
            element.EnableInClassList("is-not-installed", !installed);
            if (installed && !string.IsNullOrWhiteSpace(item.Printing.ImageRelativePath))
                row.Image?.Bind(item.Printing);
            else
                row.Image?.Unbind();
            element.tooltip = item.Link.CardId;
        }

        private static void UnbindRelatedCardRow(VisualElement element, int index)
        {
            if (element.userData is RelatedCardRow row)
                row.Image?.Unbind();
        }

        private void DestroyRelatedCardRow(VisualElement element)
        {
            if (!(element.userData is RelatedCardRow row) || row.Image == null)
                return;
            row.Image.Dispose();
            cardImageViews.Remove(row.Image);
        }

        private void RefreshSpeciesList(bool animate)
        {
            if (browser == null)
                return;
            visibleSpecies.Clear();
            visibleSpecies.AddRange(browser.VisibleSpecies);
            visibleIntroducedForms.Clear();
            visibleIntroducedForms.AddRange(browser.VisibleIntroducedForms);
            speciesList.itemsSource = visibleSpecies;
            speciesList.ClearSelection();
            speciesList.Rebuild();
            introducedFormsList.itemsSource = visibleIntroducedForms;
            introducedFormsList.ClearSelection();
            introducedFormsList.Rebuild();
            introducedFormsHeading.text = PokemonPokedexText.Format(
                "new_forms", UiLanguage, visibleIntroducedForms.Count);
            introducedFormsSection.style.display = visibleIntroducedForms.Count > 0
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            status.text = PokemonPokedexText.Format("count", UiLanguage, visibleSpecies.Count);
            empty.text = PokemonPokedexText.Get("empty", UiLanguage);
            empty.style.display = visibleSpecies.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (animate && !UIFeedbackService.ReduceMotion)
            {
                speciesList.style.opacity = 0.55f;
                speciesList.schedule.Execute(() => speciesList.style.opacity = 1f).ExecuteLater(
                    Mathf.RoundToInt(100f / UIFeedbackService.AnimationSpeed));
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
            detailTypes.text = PokemonPokedexText.Format("types", language, string.Join(" / ", form.TypeIds));
            detailRegion.text = string.IsNullOrWhiteSpace(form.RegionId)
                ? string.Empty
                : PokemonPokedexText.Format("region", language, form.RegionId);
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
            detailBackButton.text = PokemonPokedexText.Get("back", language);
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
            cardList.itemsSource = visibleCards;
            cardList.ClearSelection();
            cardList.Rebuild();
            formCardsButton.EnableInClassList("is-selected", !showAllSpeciesCards);
            speciesCardsButton.EnableInClassList("is-selected", showAllSpeciesCards);
            string emptyKey = showAllSpeciesCards ? "card_empty_species" : "card_empty_form";
            cardEmpty.text = PokemonPokedexText.Get(emptyKey, UiLanguage);
            cardEmpty.style.display = visibleCards.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (animate && !UIFeedbackService.ReduceMotion)
            {
                cardList.style.opacity = 0.45f;
                cardList.schedule.Execute(() => cardList.style.opacity = 1f).ExecuteLater(
                    Mathf.RoundToInt(120f / UIFeedbackService.AnimationSpeed));
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
            UIFeedbackService.Play(FeedbackCue.CardFlip, true);
            AnimateDetails();
            return true;
        }

        private void RebuildFormButtons()
        {
            formStrip.Clear();
            foreach (PokemonFormDefinition form in browser.SelectableForms)
            {
                var button = new Button(() => OpenForm(form.Id))
                {
                    text = PokemonPokedexBrowser.Localized(form.Names, UiLanguage),
                    tooltip = form.Id
                };
                button.AddToClassList("pokedex-form-button");
                button.EnableInClassList("is-selected", form.Id == browser.SelectedForm.Id);
                formStrip.Add(button);
            }
            formStrip.parent.style.display = browser.SelectableForms.Count > 1
                ? DisplayStyle.Flex
                : DisplayStyle.None;
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
            {
                speciesList.SetSelectionWithoutNotify(new[] { returnListIndex });
                speciesList.ScrollToItem(returnListIndex);
            }
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
            title.text = PokemonPokedexText.Get("title", UiLanguage);
            subtitle.text = PokemonPokedexText.Get("subtitle", UiLanguage);
            closeButton.text = PokemonPokedexText.Get("close", UiLanguage);
            searchField.label = PokemonPokedexText.Get("search", UiLanguage);
            formCardsButton.text = PokemonPokedexText.Get("card_scope_form", UiLanguage);
            speciesCardsButton.text = PokemonPokedexText.Get("card_scope_species", UiLanguage);
            manageDownloadsButton.text = PokemonPokedexText.Get("manage_downloads", UiLanguage);
            manageDownloadsButton.style.display = manageDownloads == null ? DisplayStyle.None : DisplayStyle.Flex;
            emptyManageDownloadsButton.text = PokemonPokedexText.Get("manage_downloads", UiLanguage);
            if (IsReady)
                emptyManageDownloadsButton.style.display = DisplayStyle.None;
            cardSearchField.label = PokemonPokedexText.Get("card_search", UiLanguage);
            updatingControls = true;
            cardSortField.label = PokemonPokedexText.Get("card_sort", UiLanguage);
            cardSortField.choices = new List<string>
            {
                PokemonPokedexText.Get("card_sort_set", UiLanguage),
                PokemonPokedexText.Get("card_sort_name", UiLanguage)
            };
            cardSortField.index = cardSortMode;
            updatingControls = false;
            RefreshGenerationChoices();
            RefreshSpeciesList(false);
            if (browser?.SelectedSpecies != null && detailPage.resolvedStyle.display == DisplayStyle.Flex)
                RefreshDetails();
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
                status.text = PokemonPokedexText.Format("unavailable", UiLanguage, exception.Message);
                status.AddToClassList("is-error");
                UIFeedbackService.Play(FeedbackCue.Error);
            }
        }

        private void AnimateOpen()
        {
            transitionAnimation?.Pause();
            if (UIFeedbackService.ReduceMotion)
            {
                root.style.opacity = 1f;
                root.style.scale = new Scale(Vector2.one);
                return;
            }
            float start = Time.realtimeSinceStartup;
            float duration = 0.22f / UIFeedbackService.AnimationSpeed;
            root.style.opacity = 0f;
            root.style.scale = new Scale(new Vector2(0.985f, 0.985f));
            transitionAnimation = root.schedule.Execute(() =>
            {
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Time.realtimeSinceStartup - start) / duration));
                root.style.opacity = t;
                root.style.scale = new Scale(Vector2.one * Mathf.Lerp(0.985f, 1f, t));
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
            detailPage.schedule.Execute(() => detailPage.style.opacity = 1f).ExecuteLater(
                Mathf.RoundToInt(140f / UIFeedbackService.AnimationSpeed));
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
