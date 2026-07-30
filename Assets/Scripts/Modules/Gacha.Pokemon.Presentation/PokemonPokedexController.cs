using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gacha.Application;
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

        private readonly List<PokemonSpeciesDefinition> visibleSpecies = new List<PokemonSpeciesDefinition>();
        private readonly List<PokemonFormDefinition> visibleIntroducedForms = new List<PokemonFormDefinition>();
        private readonly List<string> generationIds = new List<string>();
        private PokemonPokedexBrowser browser;
        private VisualElement root;
        private VisualElement listPage;
        private VisualElement detailPage;
        private VisualElement formStrip;
        private VisualElement introducedFormsSection;
        private ListView speciesList;
        private ListView introducedFormsList;
        private DropdownField generationField;
        private TextField searchField;
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
        private Button closeButton;
        private Button detailBackButton;
        private IVisualElementScheduledItem transitionAnimation;
        private bool attached;
        private bool updatingControls;
        private int returnListIndex = -1;

        public static PokemonPokedexSnapshotBundle SnapshotOverride { private get; set; }
        public bool IsReady { get; private set; }
        public bool IsOpen => root != null && root.resolvedStyle.display == DisplayStyle.Flex;
        public string InitializationError { get; private set; }
        public int VisibleSpeciesCount => visibleSpecies.Count;
        public int VisibleIntroducedFormCount => visibleIntroducedForms.Count;
        public int GenerationCount => browser?.Generations.Count ?? 0;
        public string CurrentGenerationId => browser?.GenerationId;
        public string SelectedSpeciesId => browser?.SelectedSpecies?.Id;
        public string SelectedFormId => browser?.SelectedForm?.Id;
        public int SelectableFormCount => browser?.SelectableForms.Count ?? 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            SnapshotOverride = null;
        }

        public void Attach(UIDocument document)
        {
            if (attached)
                return;
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            VisualTreeAsset view = Resources.Load<VisualTreeAsset>("UI/PokedexView");
            if (view == null)
                throw new InvalidOperationException("PokedexView.uxml is missing from Resources/UI.");
            TemplateContainer content = view.Instantiate();
            root = content.Q<VisualElement>("pokedex-overlay");
            if (root == null)
                throw new InvalidOperationException("PokedexView.uxml has no pokedex-overlay root.");
            content.Remove(root);
            document.rootVisualElement.Add(root);
            QueryElements();
            ConfigureControls();
            root.style.display = DisplayStyle.None;
            attached = true;
        }

        public bool Open()
        {
            if (!attached)
                return false;
            root.style.display = DisplayStyle.Flex;
            root.BringToFront();
            if (!EnsureReady())
            {
                status.text = PokemonPokedexText.Format("unavailable", UiLanguage, InitializationError);
                status.AddToClassList("is-error");
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
            UIFeedbackService.Play(FeedbackCue.Back);
        }

        public void SetSearch(string value)
        {
            if (!EnsureReady())
                return;
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
                PokemonPokedexSnapshotBundle snapshot = SnapshotOverride ??
                    new PokemonPokedexSnapshotRepository().Load(TaxonomyPath, CardSubjectPath);
                browser = new PokemonPokedexBrowser(snapshot.Catalog, snapshot.SubjectCatalog);
                BuildGenerationChoices();
                RefreshSpeciesList(false);
                if (ApplicationServices.IsConfigured)
                    ApplicationServices.Languages.UiLanguageChanged += OnUiLanguageChanged;
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
            if (ApplicationServices.IsConfigured)
                ApplicationServices.Languages.UiLanguageChanged -= OnUiLanguageChanged;
            transitionAnimation?.Pause();
        }

        private void QueryElements()
        {
            listPage = Required<VisualElement>("pokedex-list-page");
            detailPage = Required<VisualElement>("pokedex-detail-page");
            formStrip = Required<VisualElement>("pokedex-form-strip");
            introducedFormsSection = Required<VisualElement>("pokedex-introduced-forms-section");
            speciesList = Required<ListView>("pokedex-species-list");
            introducedFormsList = Required<ListView>("pokedex-introduced-forms-list");
            generationField = Required<DropdownField>("pokedex-generation");
            searchField = Required<TextField>("pokedex-search");
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
            closeButton = Required<Button>("pokedex-close-button");
            detailBackButton = Required<Button>("pokedex-detail-back");
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
            closeButton.clicked += Close;
            detailBackButton.clicked += () => NavigateBack();
            searchField.RegisterValueChangedCallback(evt =>
            {
                if (!updatingControls && browser != null)
                {
                    browser.Search(evt.newValue);
                    RefreshSpeciesList(true);
                }
            });
            generationField.RegisterValueChangedCallback(_ =>
            {
                if (!updatingControls && browser != null &&
                    generationField.index >= 0 && generationField.index < generationIds.Count)
                    SelectGeneration(generationIds[generationField.index]);
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
            formsHeading.text = PokemonPokedexText.Get("forms", language);
            cardsHeading.text = PokemonPokedexText.Get("cards", language);
            cardsCount.text = PokemonPokedexText.Format(
                "card_count", language,
                browser.GetSpeciesCards(species.Id).Count,
                browser.GetFormCards(form.Id).Count);
            detailBackButton.text = PokemonPokedexText.Get("back", language);
            RebuildFormButtons();
        }

        public bool OpenSpeciesForm(string speciesId, string formId)
        {
            if (!EnsureReady() || !browser.OpenSpecies(speciesId, formId))
                return false;
            returnListIndex = visibleSpecies.FindIndex(value => value.Id == speciesId);
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
            RefreshGenerationChoices();
            RefreshSpeciesList(false);
            if (browser?.SelectedSpecies != null && detailPage.resolvedStyle.display == DisplayStyle.Flex)
                RefreshDetails();
        }


        private void OnUiLanguageChanged(string _)
        {
            RefreshLocalizedContent();
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

        private static string CardSubjectPath
        {
            get
            {
#if UNITY_EDITOR
                return Path.Combine(ProjectRoot, "LocalContent", "Pokedex", "links", "pokemon-card-subject-links.en.json");
#else
                return Path.Combine(UnityEngine.Application.persistentDataPath, "Content", "pokedex", "links", "en", "pokemon-card-subject-links.en.json");
#endif
            }
        }
    }
}
