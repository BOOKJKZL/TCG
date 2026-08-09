using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Gacha.Application;
using Gacha.Pokemon.Domain;
using Gacha.Pokemon.Presentation;
using Gacha.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Gacha.Tests.PlayMode
{
    public sealed class PokemonPokedexPlayModeTests
    {
        [UnityTest]
        public IEnumerator Pokedex_720By1600InsetsKeepGridDetailsAndFiveNavigationActionsReachable()
        {
            Type collectionControllerType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("CollectionViewController"))
                .First(type => type != null);
            PropertyInfo progressOverride = collectionControllerType.GetProperty(
                "CollectionProgressStoreOverride", BindingFlags.Static | BindingFlags.Public);
            progressOverride.SetValue(null, new EmptyCollectionProgressStore());
            string originalLanguage = null;
            UIFeedbackService.Configure(true, false, 1f, false);
            try
            {
                yield return SceneManager.LoadSceneAsync("004_CollectionScene", LoadSceneMode.Single);
                yield return null;
                PokemonPokedexController controller =
                    UnityEngine.Object.FindFirstObjectByType<PokemonPokedexController>();
                Assert.That(controller, Is.Not.Null);
                originalLanguage = ApplicationServices.Languages.UiLanguageId;
                UIDocument document = controller.GetComponent<UIDocument>();
                VisualElement documentRoot = document.rootVisualElement;
                const float physicalWidth = 720f;
                const float physicalHeight = 1600f;
                const float panelReferenceWidth = 1000f;
                float physicalToPanel = panelReferenceWidth / physicalWidth;
                documentRoot.style.width = panelReferenceWidth;
                documentRoot.style.height = physicalHeight * physicalToPanel;
                yield return null;
                VisualElement preOpenHost = documentRoot.Q<VisualElement>("pokedex-overlay");
                VisualElement preOpenPageRoot = preOpenHost.Q<VisualElement>("pokedex-page-shell");
                var preOpenPageShell = GetPrivateField(controller, "mobilePageShell");
                var preOpenSafeBinding = GetPrivateField(preOpenPageShell, "safeAreaBinding") as UiToolkitSafeAreaBinding;
                Assert.That(preOpenSafeBinding, Is.Not.Null);
                preOpenSafeBinding.Suspend();
                VisualElement preOpenSafeArea = preOpenPageRoot.Q<VisualElement>("safe-area");
                preOpenSafeArea.AddToClassList("mobile-layout--compact");
                preOpenSafeArea.style.paddingLeft = 12f + 36f * physicalToPanel;
                preOpenSafeArea.style.paddingTop = 12f + 48f * physicalToPanel;
                preOpenSafeArea.style.paddingRight = 12f;
                preOpenSafeArea.style.paddingBottom = 12f + 72f * physicalToPanel;
                yield return null;
                Assert.That(controller.Open(), Is.True, controller.InitializationError);
                yield return null;
                VisualElement host = documentRoot.Q<VisualElement>("pokedex-overlay");
                VisualElement pageRoot = host.Q<VisualElement>("pokedex-page-shell");
                float initialDeadline = Time.realtimeSinceStartup + 3f;
                while (host.Q<VisualElement>(className: "pokedex-species-tile") == null &&
                       Time.realtimeSinceStartup < initialDeadline)
                    yield return null;
                Assert.That(host.Q<VisualElement>(className: "pokedex-species-tile"), Is.Not.Null,
                    "The initially visible overlay must realize its first Pokédex grid row.");
                var pageShell = GetPrivateField(controller, "mobilePageShell");
                var safeBinding = GetPrivateField(pageShell, "safeAreaBinding") as UiToolkitSafeAreaBinding;
                Assert.That(safeBinding, Is.Not.Null);
                safeBinding.Suspend();
                VisualElement safeArea = pageRoot.Q<VisualElement>("safe-area");
                safeArea.AddToClassList("mobile-layout--compact");
                safeArea.style.paddingLeft = 12f + 36f * physicalToPanel;
                safeArea.style.paddingTop = 12f + 48f * physicalToPanel;
                safeArea.style.paddingRight = 12f;
                safeArea.style.paddingBottom = 12f + 72f * physicalToPanel;
                yield return null;
                yield return null;

                Rect safeContent = InsetRect(safeArea.worldBound, safeArea.resolvedStyle);
                AssertContained(safeContent, pageRoot.Q<VisualElement>("mobile-top-bar").worldBound, "top bar");
                AssertContained(safeContent,
                    pageRoot.Q<VisualElement>("mobile-bottom-navigation").worldBound,
                    "bottom navigation");
                foreach (string destination in new[] { "home", "gacha", "collection", "content", "settings" })
                {
                    VisualElement nav = pageRoot.Q<VisualElement>("nav-" + destination);
                    Assert.That(nav.worldBound.height, Is.GreaterThanOrEqualTo(48f), destination);
                    AssertContained(safeContent, nav.worldBound, destination);
                }

                ApplicationServices.Languages.SelectUiLanguage("ja");
                float deadline = Time.realtimeSinceStartup + 3f;
                string japaneseTitle = PokemonPokedexText.Get("title", "ja");
                while (pageRoot.Q<Label>("pokedex-title").text != japaneseTitle &&
                       Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That(pageRoot.Q<Label>("pokedex-title").text, Is.EqualTo(japaneseTitle));

                ListView speciesList = pageRoot.Q<ListView>("pokedex-species-list");
                deadline = Time.realtimeSinceStartup + 3f;
                while (speciesList.Q<VisualElement>(className: "pokedex-species-tile") == null &&
                       Time.realtimeSinceStartup < deadline)
                    yield return null;
                VisualElement[] tiles = speciesList.Query<VisualElement>(className: "pokedex-species-tile")
                    .ToList()
                    .Where(tile => !tile.ClassListContains("is-hidden") && tile.worldBound.height > 0f)
                    .Take(2)
                    .ToArray();
                Assert.That(tiles.Length, Is.EqualTo(2),
                    $"list={speciesList.worldBound} layout={speciesList.layout} display={speciesList.resolvedStyle.display} " +
                    $"items={speciesList.itemsSource?.Count ?? -1} contentChildren={speciesList.contentContainer?.childCount ?? -1}");
                Assert.That(tiles[0].worldBound.xMax, Is.LessThanOrEqualTo(tiles[1].worldBound.xMin + 2f));
                foreach (VisualElement tile in tiles)
                {
                    Assert.That(tile.worldBound.height, Is.GreaterThanOrEqualTo(48f));
                    Assert.That(tile.worldBound.xMin, Is.GreaterThanOrEqualTo(safeContent.xMin - 1f));
                    Assert.That(tile.worldBound.xMax, Is.LessThanOrEqualTo(safeContent.xMax + 1f));
                }
                Color stableBorder = tiles[0].resolvedStyle.borderLeftColor;
                tiles[0].Focus();
                yield return null;
                Assert.That(tiles[0].resolvedStyle.borderLeftColor, Is.EqualTo(stableBorder),
                    "Focus feedback must not mutate a stable Android action root border.");

                SendTap(tiles[0]);
                yield return null;
                VisualElement detailPage = pageRoot.Q<VisualElement>("pokedex-detail-page");
                Assert.That(detailPage.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
                AssertContained(safeContent, detailPage.worldBound, "detail page");
                Assert.That(pageRoot.Q<VisualElement>("pokedex-detail-back").worldBound.height,
                    Is.GreaterThanOrEqualTo(48f));
                controller.ShowAllSpeciesCards(true);
                ListView relatedList = pageRoot.Q<ListView>("pokedex-card-list");
                Assert.That(controller.RelatedCardGridLineCount,
                    Is.EqualTo((controller.VisibleCardCount + 1) / 2));
                relatedList.ScrollToItem(0);
                deadline = Time.realtimeSinceStartup + 3f;
                VisualElement relatedCard = null;
                while (Time.realtimeSinceStartup < deadline)
                {
                    relatedCard = relatedList.Query<VisualElement>(className: "pokedex-card-tile")
                        .ToList()
                        .FirstOrDefault(row => !row.ClassListContains("is-hidden") &&
                                               !row.ClassListContains("is-not-installed") &&
                                               !float.IsNaN(row.worldBound.height) &&
                                               row.worldBound.height > 0f);
                    if (relatedCard != null)
                        break;
                    yield return null;
                }
                Assert.That(relatedCard, Is.Not.Null);
                Assert.That(relatedCard.worldBound.height, Is.GreaterThanOrEqualTo(48f));
                ScrollView relatedScroll = relatedList.Q<ScrollView>();
                Assert.That(relatedScroll.contentViewport.worldBound.Contains(relatedCard.worldBound.center), Is.True,
                    $"card={relatedCard.worldBound} viewport={relatedScroll.contentViewport.worldBound}");
                VisualElement[] cardTiles = relatedList.Query<VisualElement>(className: "pokedex-card-tile")
                    .ToList()
                    .Where(tile => !tile.ClassListContains("is-hidden") && tile.worldBound.height > 0f)
                    .Take(2)
                    .ToArray();
                Assert.That(cardTiles.Length, Is.EqualTo(2));
                Assert.That(cardTiles[0].worldBound.xMax, Is.LessThanOrEqualTo(cardTiles[1].worldBound.xMin + 2f));
                foreach (VisualElement cardTile in cardTiles)
                    AssertContained(safeContent, cardTile.worldBound, "related card tile");
                var relatedAction = GetPrivateField(relatedCard.userData, "Action") as MobileActionControl;
                Assert.That(relatedAction, Is.Not.Null);
                Assert.That(relatedAction.IsEnabled, Is.True);
                Assert.That(relatedAction.Root, Is.SameAs(relatedCard));
                Assert.That(GetPrivateField(relatedAction, "disposed"), Is.False);
                Assert.That(relatedCard.pickingMode, Is.EqualTo(PickingMode.Position));
                SendKeyboardActivate(relatedCard, () => Assert.That(
                    relatedCard.Q<Label>(className: "pokedex-card-row__name").ClassListContains("is-pressed"),
                    Is.True,
                    "The related-card label must receive stable pressed feedback."));
                yield return null;
                Assert.That(host.resolvedStyle.display, Is.EqualTo(DisplayStyle.None),
                    $"card={relatedCard.tooltip} status={pageRoot.Q<Label>("pokedex-status").text}");
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(originalLanguage) && ApplicationServices.IsConfigured)
                    ApplicationServices.Languages.SelectUiLanguage(originalLanguage);
                UIFeedbackService.Configure(false, true, 1f, true);
                progressOverride.SetValue(null, null);
            }
        }

        [UnityTest]
        public IEnumerator CollectionScene_OpensLocalizedVirtualizedGenerationOnePokedex()
        {
            string originalLanguage = null;
            string originalCardLanguage = null;
            var cues = new List<FeedbackCue>();
            Type collectionControllerType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("CollectionViewController"))
                .First(type => type != null);
            PropertyInfo progressOverride = collectionControllerType.GetProperty(
                "CollectionProgressStoreOverride",
                BindingFlags.Static | BindingFlags.Public);
            PropertyInfo sceneOverride = collectionControllerType.GetProperty(
                "SceneLoaderOverride",
                BindingFlags.Static | BindingFlags.Public);
            progressOverride.SetValue(null, new EmptyCollectionProgressStore());
            var routes = new List<string>();
            sceneOverride.SetValue(null, new Action<string>(routes.Add));
            UIFeedbackService.FeedbackPlayed += cues.Add;
            try
            {
                AsyncOperation load = SceneManager.LoadSceneAsync("004_CollectionScene", LoadSceneMode.Single);
                yield return load;
                yield return null;

                PokemonPokedexController controller = UnityEngine.Object.FindFirstObjectByType<PokemonPokedexController>();
                Assert.That(controller, Is.Not.Null);
                originalLanguage = ApplicationServices.Languages.UiLanguageId;
                originalCardLanguage = ApplicationServices.Languages.RequestedContentLanguageId;
                UIFeedbackService.Configure(false, false, 1f, true);
                Assert.That(controller.Open(), Is.True, controller.InitializationError);

                float deadline = Time.realtimeSinceStartup + 8f;
                while (!controller.IsReady && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That(controller.IsReady, Is.True, controller.InitializationError);
                yield return new WaitForSecondsRealtime(0.3f);

                UIDocument document = controller.GetComponent<UIDocument>();
                Assert.That(document, Is.Not.Null);
                VisualElement root = document.rootVisualElement.Q<VisualElement>("pokedex-overlay");
                ListView list = document.rootVisualElement.Q<ListView>("pokedex-species-list");
                VisualElement safeArea = root.Q<VisualElement>("safe-area");
                Assert.That(safeArea.ClassListContains("safe-area-bound"), Is.True);
                Assert.That(root.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(root.resolvedStyle.opacity, Is.EqualTo(1f).Within(0.05f));
                Assert.That(controller.GenerationCount, Is.EqualTo(9));
                Assert.That(controller.CurrentGenerationId, Is.EqualTo("generation-1"));
                Assert.That(controller.VisibleSpeciesCount, Is.EqualTo(151));
                Assert.That(controller.SpeciesGridLineCount, Is.EqualTo(76));
                Assert.That(controller.PrimaryNavigationCount, Is.EqualTo(5));
                Assert.That(list.virtualizationMethod, Is.EqualTo(CollectionVirtualizationMethod.DynamicHeight));
                Assert.That(root.Query<VisualElement>(className: "mobile-bottom-navigation__item").ToList(),
                    Has.Count.EqualTo(5));

                string uiLanguageBeforeCardSwitch = ApplicationServices.Languages.UiLanguageId;
                ApplicationServices.Languages.SelectContentLanguage(
                    "ja", ApplicationServices.Catalog.Catalog);
                yield return null;
                Assert.That(controller.LoadedCardLanguageId, Is.EqualTo("ja"));
                Assert.That(ApplicationServices.Languages.UiLanguageId, Is.EqualTo(uiLanguageBeforeCardSwitch));
                ApplicationServices.Languages.SelectContentLanguage(
                    "en", ApplicationServices.Catalog.Catalog);
                yield return null;
                Assert.That(controller.LoadedCardLanguageId, Is.EqualTo("en"));
                Assert.That(ApplicationServices.Languages.UiLanguageId, Is.EqualTo(uiLanguageBeforeCardSwitch));

                Assert.That(controller.SelectGeneration("generation-7"), Is.True);
                yield return null;
                Assert.That(controller.VisibleSpeciesCount, Is.EqualTo(88));
                Assert.That(controller.VisibleIntroducedFormCount, Is.GreaterThanOrEqualTo(20));
                ListView introducedForms = document.rootVisualElement.Q<ListView>("pokedex-introduced-forms-list");
                PokemonFormDefinition[] alolaForms = introducedForms.itemsSource
                    .Cast<PokemonFormDefinition>()
                    .Where(form => form.RegionId == "alola")
                    .ToArray();
                Assert.That(alolaForms.Length, Is.EqualTo(20));
                Assert.That(controller.OpenSpeciesForm(alolaForms[0].SpeciesId, alolaForms[0].Id), Is.True);
                Assert.That(controller.SelectedFormId, Is.EqualTo(alolaForms[0].Id));
                controller.NavigateBack();
                Assert.That(controller.SelectGeneration("generation-1"), Is.True);
                yield return null;
                Assert.That(controller.VisibleSpeciesCount, Is.EqualTo(151));
                Assert.That(controller.VisibleIntroducedFormCount, Is.EqualTo(0));

                ApplicationServices.Languages.SelectUiLanguage("zh");
                yield return null;
                Assert.That(document.rootVisualElement.Q<Label>("pokedex-title").text, Is.EqualTo("宝可梦图鉴"));
                Assert.That(document.rootVisualElement.Q<TextField>("pokedex-search").label,
                    Is.EqualTo("搜索名称或全国编号"));
                string cardLanguageBeforeJapaneseUi =
                    ApplicationServices.Languages.RequestedContentLanguageId;
                ApplicationServices.Languages.SelectUiLanguage("ja");
                yield return null;
                Assert.That(document.rootVisualElement.Q<Label>("pokedex-title").text,
                    Is.EqualTo("ポケモン図鑑"));
                Assert.That(document.rootVisualElement.Q<TextField>("pokedex-search").label,
                    Is.EqualTo("名前または全国図鑑番号を検索"));
                Assert.That(ApplicationServices.Languages.RequestedContentLanguageId,
                    Is.EqualTo(cardLanguageBeforeJapaneseUi));
                ApplicationServices.Languages.SelectUiLanguage("en");
                yield return null;
                Assert.That(document.rootVisualElement.Q<Label>("pokedex-title").text, Is.EqualTo("Pokédex"));

                TextField speciesSearch = document.rootVisualElement.Q<TextField>("pokedex-search");
                speciesSearch.value = "Bulbasaur";
                speciesSearch.value = "Pikachu";
                Assert.That(controller.VisibleSpeciesCount, Is.EqualTo(151),
                    "Rapid typing should not rebuild the virtualized list in the same frame.");
                yield return new WaitForSecondsRealtime(0.18f);
                Assert.That(controller.VisibleSpeciesCount, Is.EqualTo(1));
                Assert.That(controller.SpeciesGridLineCount, Is.EqualTo(1));

                controller.SetSearch("#025");
                yield return null;
                Assert.That(controller.VisibleSpeciesCount, Is.EqualTo(1));
                yield return null;
                VisualElement pikachuTile = root.Query<VisualElement>(className: "pokedex-species-tile")
                    .ToList().First(tile => !tile.ClassListContains("is-hidden"));
                Assert.That(pikachuTile.worldBound.height, Is.GreaterThanOrEqualTo(48f));
                SendTap(pikachuTile);
                yield return new WaitForSecondsRealtime(0.18f);
                Assert.That(document.rootVisualElement.Q<VisualElement>("pokedex-detail-page").resolvedStyle.display,
                    Is.EqualTo(DisplayStyle.Flex));
                Assert.That(document.rootVisualElement.Q<Label>("pokedex-detail-name").text, Is.EqualTo("Pikachu"));
                Assert.That(document.rootVisualElement.Q<Label>("pokedex-card-count").text, Does.Contain("cards"));
                Label detailTypes = document.rootVisualElement.Q<Label>("pokedex-detail-types");
                Assert.That(detailTypes.text, Does.Contain("Electric"));
                ApplicationServices.Languages.SelectUiLanguage("zh");
                yield return null;
                Assert.That(detailTypes.text, Does.Contain("电"));
                Assert.That(detailTypes.text, Does.Not.Contain("electric").IgnoreCase);
                ApplicationServices.Languages.SelectUiLanguage("ja");
                yield return null;
                Assert.That(detailTypes.text, Does.Contain("でんき"));
                Assert.That(detailTypes.text, Does.Not.Contain("electric").IgnoreCase);
                ApplicationServices.Languages.SelectUiLanguage("en");
                yield return null;
                Assert.That(cues, Does.Contain(FeedbackCue.CardFlip));
                deadline = Time.realtimeSinceStartup + 5f;
                while (controller.ArtworkState == AsyncCardImageState.Loading &&
                       Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That(controller.ArtworkState, Is.EqualTo(AsyncCardImageState.Ready));
                Assert.That(controller.CachedArtworkCount, Is.InRange(1, 8));
                Assert.That(controller.CachedArtworkBytes, Is.GreaterThan(0L));
                Assert.That(controller.CachedArtworkBytes,
                    Is.LessThanOrEqualTo(controller.CachedArtworkBudgetBytes));
                Assert.That(controller.ShowingAllSpeciesCards, Is.False);
                Assert.That(controller.VisibleCardCount, Is.EqualTo(0));
                controller.ShowAllSpeciesCards(true);
                yield return null;
                var activeBrowser = GetPrivateField(controller, "browser") as
                    Gacha.Pokemon.Application.PokemonPokedexBrowser;
                Assert.That(controller.VisibleCardCount, Is.GreaterThan(0),
                    $"selected={controller.SelectedSpeciesId} language={controller.LoadedCardLanguageId} " +
                    $"search={GetPrivateField(controller, "cardSearch")} " +
                    $"sourceCards={activeBrowser?.GetSpeciesCards(controller.SelectedSpeciesId).Count ?? -1}");
                Assert.That(controller.InstalledVisibleCardCount, Is.EqualTo(controller.VisibleCardCount));
                Assert.That(controller.RelatedCardGridLineCount,
                    Is.EqualTo((controller.VisibleCardCount + 1) / 2));
                ListView relatedCardList = document.rootVisualElement.Q<ListView>("pokedex-card-list");
                Assert.That(relatedCardList.virtualizationMethod,
                    Is.EqualTo(CollectionVirtualizationMethod.DynamicHeight));
                TextField relatedCardSearch = document.rootVisualElement.Q<TextField>("pokedex-card-search");
                relatedCardSearch.value = "definitely-no-matching-card";
                Assert.That(controller.VisibleCardCount, Is.GreaterThan(0));
                yield return new WaitForSecondsRealtime(0.18f);
                Assert.That(controller.VisibleCardCount, Is.EqualTo(0));
                controller.SetCardSearch("Pikachu");
                yield return null;
                Assert.That(controller.VisibleCardCount, Is.GreaterThan(0));
                Assert.That(controller.OpenRelatedCard(0), Is.True);
                yield return null;
                Assert.That(root.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
                Assert.That(document.rootVisualElement.Q<VisualElement>("collection-details-panel").resolvedStyle.display,
                    Is.EqualTo(DisplayStyle.Flex));
                Assert.That(controller.Open(), Is.True);
                yield return null;

                controller.NavigateBack();
                controller.SetSearch(string.Empty);
                Assert.That(controller.OpenSpecies("pokemon-species:19"), Is.True);
                yield return null;
                Assert.That(controller.SelectableFormCount, Is.GreaterThan(1));
                string defaultForm = controller.SelectedFormId;
                VisualElement regional = document.rootVisualElement.Query<VisualElement>(className: "pokedex-form-action")
                    .ToList()
                    .First(action => !action.ClassListContains("is-selected"));
                ScrollView formScroll = document.rootVisualElement.Q<ScrollView>(className: "pokedex-form-scroll");
                deadline = Time.realtimeSinceStartup + 3f;
                while (!formScroll.contentViewport.worldBound.Contains(regional.worldBound.center) &&
                       Time.realtimeSinceStartup < deadline)
                {
                    formScroll.ScrollTo(regional);
                    yield return null;
                }
                Assert.That(regional.userData, Is.TypeOf<MobileActionControl>());
                Assert.That(((MobileActionControl)regional.userData).IsEnabled, Is.True);
                Assert.That(regional.tooltip, Is.Not.Null.And.Not.Empty);
                Assert.That(regional.tooltip, Does.Not.Contain("pokemon-form:").IgnoreCase);
                SendKeyboardActivate(regional);
                yield return null;
                Assert.That(controller.SelectedFormId, Is.Not.EqualTo(defaultForm));
                ApplicationServices.Languages.SelectUiLanguage("zh");
                yield return null;
                Label detailRegion = document.rootVisualElement.Q<Label>("pokedex-detail-region");
                Assert.That(detailRegion.text, Does.Not.Contain("alola").IgnoreCase);
                Assert.That(detailRegion.text, Does.Not.Contain("galar").IgnoreCase);
                Assert.That(detailRegion.text, Does.Not.Contain("hisui").IgnoreCase);
                Assert.That(detailRegion.text, Does.Not.Contain("paldea").IgnoreCase);
                ApplicationServices.Languages.SelectUiLanguage("en");
                yield return null;
                Assert.That(controller.NavigateBack(), Is.True);
                Assert.That(controller.SelectedFormId, Is.EqualTo(defaultForm));
                while (controller.NavigateBack())
                    yield return null;

                Assert.That(controller.OpenSpecies("pokemon-species:100"), Is.True);
                Assert.That(controller.NavigateBack(), Is.True);
                Assert.That(controller.NavigateBack(), Is.False);
                deadline = Time.realtimeSinceStartup + 3f;
                VisualElement returnedSecondColumnTile = null;
                while (Time.realtimeSinceStartup < deadline)
                {
                    returnedSecondColumnTile = list.Query<VisualElement>(className: "pokedex-species-tile")
                        .ToList()
                        .FirstOrDefault(tile => tile.Q<Label>(className: "pokedex-row__number")?.text == "#100" &&
                                                tile.worldBound.height > 0f);
                    if (returnedSecondColumnTile != null)
                        break;
                    yield return null;
                }
                Assert.That(returnedSecondColumnTile, Is.Not.Null,
                    "Returning from a second-column species must scroll to its containing grid line.");

                controller.Close();
                UIFeedbackService.Configure(true, false, 1f, true);
                Assert.That(controller.Open(), Is.True);
                yield return null;
                Assert.That(root.resolvedStyle.opacity, Is.EqualTo(1f).Within(0.001f));
                Assert.That(cues, Does.Contain(FeedbackCue.Confirm));
                Assert.That(cues, Does.Contain(FeedbackCue.Back));

                SendTap(root.Q<VisualElement>("nav-collection"));
                Assert.That(root.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
                Assert.That(controller.Open(), Is.True);
                yield return null;
                SendTap(root.Q<VisualElement>("nav-content"));
                SendTap(root.Q<VisualElement>("nav-settings"));
                Assert.That(routes, Is.EqualTo(new[] { "006_ContentScene" }));
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(originalLanguage) && ApplicationServices.IsConfigured)
                    ApplicationServices.Languages.SelectUiLanguage(originalLanguage);
                if (!string.IsNullOrWhiteSpace(originalCardLanguage) &&
                    ApplicationServices.IsConfigured && ApplicationServices.Catalog.IsReady)
                    ApplicationServices.Languages.SelectContentLanguage(
                        originalCardLanguage, ApplicationServices.Catalog.Catalog);
                UIFeedbackService.FeedbackPlayed -= cues.Add;
                UIFeedbackService.Configure(false, true, 1f, true);
                progressOverride.SetValue(null, null);
                sceneOverride.SetValue(null, null);
            }
        }

        private static void SendTap(VisualElement control, Action afterPointerDown = null)
        {
            Assert.That(control, Is.Not.Null);
            Vector2 position = control.worldBound.center;
            using (PointerDownEvent down = PointerDownEvent.GetPooled(new Event
                   {
                       type = EventType.MouseDown,
                       button = 0,
                       mousePosition = position
                   }))
            {
                control.SendEvent(down);
            }
            afterPointerDown?.Invoke();
            using (PointerUpEvent up = PointerUpEvent.GetPooled(new Event
                   {
                       type = EventType.MouseUp,
                       button = 0,
                       mousePosition = position
                   }))
            {
                control.SendEvent(up);
            }
        }

        private static void SendKeyboardActivate(VisualElement control, Action afterKeyDown = null)
        {
            Assert.That(control, Is.Not.Null);
            control.Focus();
            using (KeyDownEvent down = KeyDownEvent.GetPooled(new Event
                   {
                       type = EventType.KeyDown,
                       keyCode = KeyCode.Return
                   }))
            {
                control.SendEvent(down);
            }
            afterKeyDown?.Invoke();
            using (KeyUpEvent up = KeyUpEvent.GetPooled(new Event
                   {
                       type = EventType.KeyUp,
                       keyCode = KeyCode.Return
                   }))
            {
                control.SendEvent(up);
            }
        }

        private static object GetPrivateField(object target, string name) =>
            target.GetType().GetField(
                name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(target);

        private static Rect InsetRect(Rect outer, IResolvedStyle style) => new Rect(
            outer.xMin + style.paddingLeft,
            outer.yMin + style.paddingTop,
            outer.width - style.paddingLeft - style.paddingRight,
            outer.height - style.paddingTop - style.paddingBottom);

        private static void AssertContained(Rect outer, Rect inner, string label)
        {
            Assert.That(inner.xMin, Is.GreaterThanOrEqualTo(outer.xMin - 1f), label);
            Assert.That(inner.xMax, Is.LessThanOrEqualTo(outer.xMax + 1f), label);
            Assert.That(inner.yMin, Is.GreaterThanOrEqualTo(outer.yMin - 1f), label);
            Assert.That(inner.yMax, Is.LessThanOrEqualTo(outer.yMax + 1f), label);
        }

        private sealed class EmptyCollectionProgressStore : ICollectionProgressStore
        {
            public CollectionItemProgress GetProgress(string printingId) =>
                new CollectionItemProgress(printingId, 0, false);

            public bool MarkSeen(string printingId) => false;
        }
    }
}
