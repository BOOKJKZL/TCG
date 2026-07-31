using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Gacha.Application;
using Gacha.Domain;
using Gacha.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Gacha.Tests.PlayMode
{
    public class CollectionBrowserPlayModeTests
    {
        [UnityTest]
        public IEnumerator CollectionScene_VirtualizesInstalledCardsAndOpensDetails()
        {
            Type controllerType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("CollectionViewController"))
                .First(type => type != null);
            var progressStore = new MemoryCollectionProgressStore();
            PropertyInfo storeOverride = controllerType.GetProperty(
                "CollectionProgressStoreOverride",
                BindingFlags.Static | BindingFlags.Public);
            storeOverride.SetValue(null, progressStore);
            var cues = new List<FeedbackCue>();
            string originalUiLanguage = null;
            UIFeedbackService.FeedbackPlayed += cues.Add;
            try
            {
                AsyncOperation load = SceneManager.LoadSceneAsync("004_CollectionScene", LoadSceneMode.Single);
                yield return load;
                yield return null;

                MonoBehaviour controller = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
                        FindObjectsInactive.Include,
                        FindObjectsSortMode.None)
                    .FirstOrDefault(component => component.GetType() == controllerType);
                Assert.That(controller, Is.Not.Null);

                float deadline = Time.realtimeSinceStartup + 5f;
                while (!(bool)GetProperty(controller, "IsReady") && Time.realtimeSinceStartup < deadline)
                    yield return null;

                Assert.That((bool)GetProperty(controller, "IsReady"), Is.True, GetProperty(controller, "InitializationError") as string);
                int installedSetCount = (int)GetProperty(controller, "InstalledSetCount");
                Assert.That(installedSetCount, Is.GreaterThanOrEqualTo(5));

                UIDocument document = controller.GetComponent<UIDocument>();
                Assert.That(document, Is.Not.Null);
                originalUiLanguage = ApplicationServices.Languages.UiLanguageId;
                ApplicationServices.Languages.SelectUiLanguage("zh");
                yield return null;
                Assert.That(document.rootVisualElement.Q<Label>("collection-title").text, Is.EqualTo("卡牌收藏"));
                Assert.That(document.rootVisualElement.Q<TextField>("card-search").label, Is.EqualTo("搜索名称或卡号"));
                ApplicationServices.Languages.SelectUiLanguage("en");
                yield return null;
                Assert.That(document.rootVisualElement.Q<Label>("collection-title").text, Is.EqualTo("Card Collection"));
                Assert.That(document.rootVisualElement.Q<TextField>("card-search").label, Is.EqualTo("Search name or number"));

                ListView setList = document.rootVisualElement.Q<ListView>("set-list");
                ListView cardList = document.rootVisualElement.Q<ListView>("card-list");
                Assert.That(setList.itemsSource.Count, Is.EqualTo(installedSetCount));
                Assert.That(setList.virtualizationMethod, Is.EqualTo(CollectionVirtualizationMethod.FixedHeight));
                Assert.That(cardList.virtualizationMethod, Is.EqualTo(CollectionVirtualizationMethod.FixedHeight));

                int baseSetIndex = setList.itemsSource.Cast<SetDefinition>()
                    .Select((set, index) => new { set, index })
                    .Single(pair => pair.set.Id.EndsWith(":base1", StringComparison.Ordinal))
                    .index;
                setList.SetSelection(baseSetIndex);
                yield return null;
                Assert.That((int)GetProperty(controller, "CurrentCardCount"), Is.GreaterThan(0));
                Assert.That(cues, Does.Contain(FeedbackCue.Confirm));

                PrintingDefinition[] availableCards = cardList.itemsSource.Cast<PrintingDefinition>().ToArray();
                progressStore.Set(availableCards[0].Id, 2, true);
                progressStore.Set(availableCards[1].Id, 1, false);
                Invoke(controller, "RefreshCollectionProgress");
                yield return null;
                Assert.That((int)GetProperty(controller, "OwnedCardCount"), Is.EqualTo(2));
                Assert.That((int)GetProperty(controller, "NewCardCount"), Is.EqualTo(1));

                Invoke(controller, "SetOwnedOnlyFilter", true);
                yield return null;
                Assert.That((int)GetProperty(controller, "CurrentCardCount"), Is.EqualTo(2));
                Invoke(controller, "SetNewOnlyFilter", true);
                yield return null;
                Assert.That((int)GetProperty(controller, "CurrentCardCount"), Is.EqualTo(1));

                cardList.SetSelection(0);
                yield return new WaitForSecondsRealtime(0.35f);
                VisualElement details = document.rootVisualElement.Q<VisualElement>("details-panel");
                Assert.That(details.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(details.resolvedStyle.opacity, Is.EqualTo(1f).Within(0.05f));
                Assert.That((bool)GetProperty(controller, "HasDetailLanguageSwitcher"), Is.False);
                Assert.That(document.rootVisualElement.Q<VisualElement>("detail-language-switcher").resolvedStyle.display,
                    Is.EqualTo(DisplayStyle.None));
                Assert.That(progressStore.GetProgress(availableCards[0].Id).IsNew, Is.False);
                Assert.That((int)GetProperty(controller, "NewCardCount"), Is.EqualTo(0));
                Assert.That(cues, Does.Contain(FeedbackCue.CardFlip));

                Invoke(controller, "SetNewOnlyFilter", false);
                Invoke(controller, "SetOwnedOnlyFilter", false);
                TextField search = document.rootVisualElement.Q<TextField>("card-search");
                search.value = "definitely-no-matching-card";
                Assert.That((int)GetProperty(controller, "CurrentCardCount"), Is.GreaterThan(0),
                    "Typing should not rebuild the virtualized list in the same frame.");
                yield return new WaitForSecondsRealtime(0.18f);
                Assert.That((int)GetProperty(controller, "CurrentCardCount"), Is.EqualTo(0));
                Assert.That(document.rootVisualElement.Q<Label>("filter-empty").resolvedStyle.display,
                    Is.EqualTo(DisplayStyle.Flex));
                search.value = string.Empty;
                yield return new WaitForSecondsRealtime(0.18f);

                DropdownField rarityFilter = document.rootVisualElement.Q<DropdownField>("rarity-filter");
                Assert.That(rarityFilter.choices.Count, Is.GreaterThan(1));
                rarityFilter.index = 1;
                yield return null;
                Assert.That((int)GetProperty(controller, "CurrentCardCount"), Is.GreaterThan(0));
                Assert.That((int)GetProperty(controller, "CurrentCardCount"),
                    Is.LessThan((int)GetProperty(controller, "CurrentSetTotalCount")));
                rarityFilter.index = 0;
                yield return null;

                deadline = Time.realtimeSinceStartup + 5f;
                while ((int)GetProperty(controller, "CachedTextureCount") == 0 && Time.realtimeSinceStartup < deadline)
                    yield return null;

                int cached = (int)GetProperty(controller, "CachedTextureCount");
                int available = (int)GetProperty(controller, "CurrentCardCount");
                Assert.That(cached, Is.GreaterThan(0));
                Assert.That(cached, Is.LessThanOrEqualTo(32));
                Assert.That(cached, Is.LessThan(available));

                progressStore.Set(availableCards[1].Id, 1, true);
                progressStore.ThrowOnMarkSeen = true;
                Invoke(controller, "RefreshCollectionProgress");
                LogAssert.Expect(LogType.Warning, "Collection viewed-card status could not be saved: disk full");
                int failingIndex = cardList.itemsSource.Cast<PrintingDefinition>()
                    .Select((printing, index) => new { printing, index })
                    .Single(pair => pair.printing.Id == availableCards[1].Id)
                    .index;
                cardList.SetSelection(failingIndex);
                yield return null;
                Assert.That(progressStore.GetProgress(availableCards[1].Id).IsNew, Is.True);
                Assert.That(document.rootVisualElement.Q<Label>("browser-status").ClassListContains("is-error"), Is.True);
                Assert.That(cues, Does.Contain(FeedbackCue.Error));
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(originalUiLanguage) && ApplicationServices.IsConfigured)
                    ApplicationServices.Languages.SelectUiLanguage(originalUiLanguage);
                UIFeedbackService.FeedbackPlayed -= cues.Add;
                storeOverride.SetValue(null, null);
            }
        }

        [UnityTest]
        public IEnumerator CollectionDetails_SwitchOnlyInstalledCardVersionWithoutChangingGlobalLanguages()
        {
            Type controllerType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("CollectionViewController"))
                .First(type => type != null);
            PropertyInfo catalogOverride = controllerType.GetProperty(
                "CatalogOverride",
                BindingFlags.Static | BindingFlags.Public);
            PropertyInfo storeOverride = controllerType.GetProperty(
                "CollectionProgressStoreOverride",
                BindingFlags.Static | BindingFlags.Public);
            UniversalCatalog testCatalog = BuildCardLanguageCatalog();
            catalogOverride.SetValue(null, testCatalog);
            var progressStore = new MemoryCollectionProgressStore();
            progressStore.Set("card-025-en", 2, false);
            progressStore.Set("card-025-ja", 5, false);
            storeOverride.SetValue(null, progressStore);
            Assert.That(ApplicationServices.IsConfigured, Is.True);
            string originalUiLanguage = ApplicationServices.Languages.UiLanguageId;
            string originalCardLanguage = ApplicationServices.Languages.RequestedContentLanguageId;
            ApplicationServices.Languages.SelectContentLanguage("en", testCatalog);
            var cues = new List<FeedbackCue>();
            UIFeedbackService.FeedbackPlayed += cues.Add;
            try
            {
                AsyncOperation load = SceneManager.LoadSceneAsync("004_CollectionScene", LoadSceneMode.Single);
                yield return load;
                yield return null;

                MonoBehaviour controller = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
                        FindObjectsInactive.Include,
                        FindObjectsSortMode.None)
                    .FirstOrDefault(component => component.GetType() == controllerType);
                Assert.That(controller, Is.Not.Null);
                float deadline = Time.realtimeSinceStartup + 5f;
                while (!(bool)GetProperty(controller, "IsReady") && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That((bool)GetProperty(controller, "IsReady"), Is.True,
                    GetProperty(controller, "InitializationError") as string);

                bool opened = (bool)controllerType.GetMethod("ShowPrintingDetails")
                    .Invoke(controller, new object[] { "card-025-en" });
                Assert.That(opened, Is.True);
                yield return new WaitForSecondsRealtime(0.25f);

                UIDocument document = controller.GetComponent<UIDocument>();
                VisualElement switcher = document.rootVisualElement.Q<VisualElement>("detail-language-switcher");
                Button[] languageButtons = switcher.Children().OfType<Button>().ToArray();
                Assert.That((bool)GetProperty(controller, "HasDetailLanguageSwitcher"), Is.True);
                Assert.That((int)GetProperty(controller, "DetailLanguageCount"), Is.EqualTo(3));
                Assert.That(switcher.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(languageButtons.Select(button => button.text), Is.EqualTo(new[] { "中", "EN", "日" }));
                Assert.That(document.rootVisualElement.Q<Label>("detail-progress").text, Does.Contain("2"));

                bool switched = (bool)controllerType.GetMethod("SwitchDetailCardLanguage")
                    .Invoke(controller, new object[] { "ja" });
                Assert.That(switched, Is.True);
                yield return new WaitForSecondsRealtime(0.2f);

                Assert.That(GetProperty(controller, "DetailPrintingId"), Is.EqualTo("card-025-ja"));
                Assert.That(document.rootVisualElement.Q<Label>("detail-name").text, Is.EqualTo("ピカチュウ"));
                Assert.That(document.rootVisualElement.Q<Label>("detail-metadata").text, Does.Contain("日本語セット"));
                Assert.That(document.rootVisualElement.Q<Label>("detail-progress").text, Does.Contain("5"),
                    "Owned counts remain attached to the selected printing, not the language group.");
                Assert.That(document.rootVisualElement.Q<Button>("detail-language-ja").ClassListContains("is-selected"), Is.True);
                Assert.That(switcher.resolvedStyle.opacity, Is.EqualTo(1f).Within(0.01f));
                Assert.That(ApplicationServices.Languages.UiLanguageId, Is.EqualTo(originalUiLanguage));
                Assert.That(ApplicationServices.Languages.RequestedContentLanguageId, Is.EqualTo("en"));
                Assert.That(ApplicationServices.Languages.ContentLanguage.ResolvedLanguageId, Is.EqualTo("en"));
                Assert.That(cues, Does.Contain(FeedbackCue.CardFlip));

                opened = (bool)controllerType.GetMethod("ShowPrintingDetails")
                    .Invoke(controller, new object[] { "trainer-001-en" });
                Assert.That(opened, Is.True);
                yield return null;
                Assert.That((bool)GetProperty(controller, "HasDetailLanguageSwitcher"), Is.False);
                Assert.That(switcher.childCount, Is.Zero);
                Assert.That(switcher.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
            }
            finally
            {
                UIFeedbackService.FeedbackPlayed -= cues.Add;
                catalogOverride.SetValue(null, null);
                storeOverride.SetValue(null, null);
                if (ApplicationServices.IsConfigured)
                {
                    ApplicationServices.Languages.SelectUiLanguage(originalUiLanguage);
                    UniversalCatalog installed = ApplicationServices.Catalog.EnsureLoaded().Catalog;
                    ApplicationServices.Languages.SelectContentLanguage(originalCardLanguage, installed);
                }
            }
        }

        private static object GetProperty(object target, string name)
        {
            return target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(target);
        }

        private static void Invoke(object target, string name, params object[] arguments)
        {
            target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public)?.Invoke(target, arguments);
        }

        private static UniversalCatalog BuildCardLanguageCatalog()
        {
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = "Pokemon TCG",
                ["ja"] = "ポケモンカード",
                ["zh-cn"] = "宝可梦卡牌"
            };
            var languages = new[]
            {
                new LanguageDefinition("en", new Dictionary<string, string> { ["en"] = "English" }),
                new LanguageDefinition("ja", new Dictionary<string, string> { ["ja"] = "日本語" }),
                new LanguageDefinition("zh-cn", new Dictionary<string, string> { ["zh-cn"] = "简体中文" })
            };
            var game = new GameDefinition("pokemon", names, languages.Select(value => value.Id));
            var set = new SetDefinition("language-set", game.Id, new Dictionary<string, string>
            {
                ["en"] = "English Set",
                ["ja"] = "日本語セット",
                ["zh-cn"] = "简体中文系列"
            });
            var pokemon = new CollectibleItemDefinition("pikachu", game.Id, new Dictionary<string, string>
            {
                ["en"] = "Pikachu",
                ["ja"] = "ピカチュウ",
                ["zh-cn"] = "皮卡丘"
            }, "pokemon");
            var trainer = new CollectibleItemDefinition("trainer", game.Id,
                new Dictionary<string, string> { ["en"] = "Trainer" }, "trainer");
            var rarity = new RarityDefinition("rare", game.Id, new Dictionary<string, string>
            {
                ["en"] = "Rare",
                ["ja"] = "レア",
                ["zh-cn"] = "稀有"
            }, 1);
            var variant = new VariantDefinition("normal", game.Id, new Dictionary<string, string>
            {
                ["en"] = "Normal",
                ["ja"] = "通常",
                ["zh-cn"] = "普通"
            });
            var printings = new[]
            {
                new PrintingDefinition("card-025-en", pokemon.Id,
                    new PrintingIdentity(game.Id, set.Id, "025", "en", variant.Id), rarity.Id,
                    new Dictionary<string, string> { ["en"] = "Pikachu" }),
                new PrintingDefinition("card-025-ja", pokemon.Id,
                    new PrintingIdentity(game.Id, set.Id, "025", "ja", variant.Id), rarity.Id,
                    new Dictionary<string, string> { ["ja"] = "ピカチュウ" }),
                new PrintingDefinition("card-025-zh-cn", pokemon.Id,
                    new PrintingIdentity(game.Id, set.Id, "025", "zh-cn", variant.Id), rarity.Id,
                    new Dictionary<string, string> { ["zh-cn"] = "皮卡丘" }),
                new PrintingDefinition("trainer-001-en", trainer.Id,
                    new PrintingIdentity(game.Id, set.Id, "001", "en", variant.Id), rarity.Id,
                    new Dictionary<string, string> { ["en"] = "Trainer" })
            };
            return new UniversalCatalog(
                languages,
                new[] { game },
                new[] { set },
                new[] { pokemon, trainer },
                new[] { rarity },
                new[] { variant },
                printings,
                Array.Empty<ProductDefinition>());
        }

        private sealed class MemoryCollectionProgressStore : ICollectionProgressStore
        {
            private readonly Dictionary<string, CollectionItemProgress> progress =
                new Dictionary<string, CollectionItemProgress>(StringComparer.Ordinal);

            public bool ThrowOnMarkSeen { get; set; }

            public void Set(string printingId, int count, bool isNew)
            {
                progress[printingId] = new CollectionItemProgress(printingId, count, isNew);
            }

            public CollectionItemProgress GetProgress(string printingId)
            {
                return progress.TryGetValue(printingId, out CollectionItemProgress value)
                    ? value
                    : new CollectionItemProgress(printingId, 0, false);
            }

            public bool MarkSeen(string printingId)
            {
                if (ThrowOnMarkSeen)
                    throw new InvalidOperationException("disk full");
                CollectionItemProgress current = GetProgress(printingId);
                if (!current.IsNew)
                    return false;
                Set(printingId, current.OwnedCount, false);
                return true;
            }
        }
    }
}
