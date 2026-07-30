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
        public IEnumerator CollectionScene_OpensLocalizedVirtualizedGenerationOnePokedex()
        {
            string originalLanguage = null;
            var cues = new List<FeedbackCue>();
            Type collectionControllerType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("CollectionViewController"))
                .First(type => type != null);
            PropertyInfo progressOverride = collectionControllerType.GetProperty(
                "CollectionProgressStoreOverride",
                BindingFlags.Static | BindingFlags.Public);
            progressOverride.SetValue(null, new EmptyCollectionProgressStore());
            UIFeedbackService.FeedbackPlayed += cues.Add;
            try
            {
                AsyncOperation load = SceneManager.LoadSceneAsync("004_CollectionScene", LoadSceneMode.Single);
                yield return load;
                yield return null;

                PokemonPokedexController controller = UnityEngine.Object.FindFirstObjectByType<PokemonPokedexController>();
                Assert.That(controller, Is.Not.Null);
                originalLanguage = ApplicationServices.Languages.UiLanguageId;
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
                Assert.That(root.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(root.resolvedStyle.opacity, Is.EqualTo(1f).Within(0.05f));
                Assert.That(controller.GenerationCount, Is.EqualTo(9));
                Assert.That(controller.CurrentGenerationId, Is.EqualTo("generation-1"));
                Assert.That(controller.VisibleSpeciesCount, Is.EqualTo(151));
                Assert.That(list.virtualizationMethod, Is.EqualTo(CollectionVirtualizationMethod.FixedHeight));
                Assert.That(list.itemsSource.Cast<PokemonSpeciesDefinition>()
                    .Select(value => value.NationalDexNumber), Is.EqualTo(Enumerable.Range(1, 151)));

                ApplicationServices.Languages.SelectUiLanguage("zh");
                yield return null;
                Assert.That(document.rootVisualElement.Q<Label>("pokedex-title").text, Is.EqualTo("宝可梦图鉴"));
                Assert.That(document.rootVisualElement.Q<TextField>("pokedex-search").label,
                    Is.EqualTo("搜索名称或全国编号"));
                ApplicationServices.Languages.SelectUiLanguage("en");
                yield return null;
                Assert.That(document.rootVisualElement.Q<Label>("pokedex-title").text, Is.EqualTo("Pokédex"));

                controller.SetSearch("#025");
                yield return null;
                Assert.That(controller.VisibleSpeciesCount, Is.EqualTo(1));
                PokemonSpeciesDefinition pikachu = list.itemsSource.Cast<PokemonSpeciesDefinition>().Single();
                Assert.That(pikachu.NationalDexNumber, Is.EqualTo(25));
                Assert.That(controller.OpenSpecies(pikachu.Id), Is.True);
                yield return new WaitForSecondsRealtime(0.18f);
                Assert.That(document.rootVisualElement.Q<VisualElement>("pokedex-detail-page").resolvedStyle.display,
                    Is.EqualTo(DisplayStyle.Flex));
                Assert.That(document.rootVisualElement.Q<Label>("pokedex-detail-name").text, Is.EqualTo("Pikachu"));
                Assert.That(document.rootVisualElement.Q<Label>("pokedex-card-count").text, Does.Contain("cards"));
                Assert.That(cues, Does.Contain(FeedbackCue.CardFlip));

                controller.NavigateBack();
                controller.SetSearch(string.Empty);
                Assert.That(controller.OpenSpecies("pokemon-species:19"), Is.True);
                yield return null;
                Assert.That(controller.SelectableFormCount, Is.GreaterThan(1));
                string defaultForm = controller.SelectedFormId;
                Button regional = document.rootVisualElement.Query<Button>(className: "pokedex-form-button")
                    .ToList()
                    .First(button => button.tooltip != defaultForm);
                Assert.That(controller.OpenForm(regional.tooltip), Is.True);
                Assert.That(controller.SelectedFormId, Is.Not.EqualTo(defaultForm));
                Assert.That(controller.NavigateBack(), Is.True);
                Assert.That(controller.SelectedFormId, Is.EqualTo(defaultForm));

                controller.Close();
                UIFeedbackService.Configure(true, false, 1f, true);
                Assert.That(controller.Open(), Is.True);
                yield return null;
                Assert.That(root.resolvedStyle.opacity, Is.EqualTo(1f).Within(0.001f));
                Assert.That(cues, Does.Contain(FeedbackCue.Confirm));
                Assert.That(cues, Does.Contain(FeedbackCue.Back));
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(originalLanguage) && ApplicationServices.IsConfigured)
                    ApplicationServices.Languages.SelectUiLanguage(originalLanguage);
                UIFeedbackService.FeedbackPlayed -= cues.Add;
                UIFeedbackService.Configure(false, true, 1f, true);
                progressOverride.SetValue(null, null);
            }
        }

        private sealed class EmptyCollectionProgressStore : ICollectionProgressStore
        {
            public CollectionItemProgress GetProgress(string printingId) =>
                new CollectionItemProgress(printingId, 0, false);

            public bool MarkSeen(string printingId) => false;
        }
    }
}
