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
    public class GachaOpeningPlayModeTests
    {
        [UnityTest]
        public IEnumerator EmptyCatalog_KeepsMobileNavigationAndManageContentReachable()
        {
            Type controllerType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("GachaViewController"))
                .First(type => type != null);
            PropertyInfo catalogOverride = controllerType.GetProperty(
                "CatalogProviderOverride",
                BindingFlags.Static | BindingFlags.Public);
            PropertyInfo storeOverride = controllerType.GetProperty(
                "InventoryStoreOverride",
                BindingFlags.Static | BindingFlags.Public);
            PropertyInfo sceneOverride = controllerType.GetProperty(
                "SceneLoaderOverride",
                BindingFlags.Static | BindingFlags.Public);
            string routedScene = null;
            catalogOverride.SetValue(null, new EmptyCatalogProvider());
            storeOverride.SetValue(null, new MemoryProgressStore());
            sceneOverride.SetValue(null, (Action<string>)(scene => routedScene = scene));
            try
            {
                yield return SceneManager.LoadSceneAsync("003_GachaScene", LoadSceneMode.Single);
                Component controller = UnityEngine.Object.FindFirstObjectByType(controllerType) as Component;
                float deadline = Time.realtimeSinceStartup + 5f;
                while (!(bool)GetProperty(controller, "IsReady") && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That((bool)GetProperty(controller, "IsReady"), Is.True,
                    GetProperty(controller, "InitializationError") as string);
                yield return null;
                Assert.That((int)GetProperty(controller, "ProductCount"), Is.Zero);
                UIDocument document = controller.GetComponent<UIDocument>();
                VisualElement root = document.rootVisualElement;
                Assert.That(root.Q<VisualElement>("mobile-bottom-navigation").childCount, Is.EqualTo(5));
                VisualElement manage = root.Q<VisualElement>("gacha-manage-content-button");
                Assert.That(manage.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(manage.worldBound.height, Is.GreaterThanOrEqualTo(48f));
                Assert.That(root.Q<Label>("gacha-status").text, Is.Not.Empty);
                SendTap(manage);
                yield return null;
                Assert.That(routedScene, Is.EqualTo("006_ContentScene"));
            }
            finally
            {
                catalogOverride.SetValue(null, null);
                storeOverride.SetValue(null, null);
                sceneOverride.SetValue(null, null);
            }
        }

        [UnityTest]
        public IEnumerator GachaScene_OpensAndRevealsASimulatedPack()
        {
            Type controllerType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("GachaViewController"))
                .First(type => type != null);
            var store = new MemoryProgressStore();
            PropertyInfo storeOverride = controllerType.GetProperty(
                "InventoryStoreOverride",
                BindingFlags.Static | BindingFlags.Public);
            storeOverride.SetValue(null, store);
            PropertyInfo sceneLoaderOverride = controllerType.GetProperty(
                "SceneLoaderOverride",
                BindingFlags.Static | BindingFlags.Public);
            var cues = new List<FeedbackCue>();
            string originalUiLanguage = null;
            string originalContentLanguage = null;
            bool originalReduceMotion = UIFeedbackService.ReduceMotion;
            bool originalHaptics = UIFeedbackService.HapticsEnabled;
            bool originalSound = UIFeedbackService.SoundEnabled;
            float originalAnimationSpeed = UIFeedbackService.AnimationSpeed;
            UIFeedbackService.FeedbackPlayed += cues.Add;
            try
            {
                AsyncOperation load = SceneManager.LoadSceneAsync("003_GachaScene", LoadSceneMode.Single);
                yield return load;
                yield return null;

                MonoBehaviour controller = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
                        FindObjectsInactive.Include,
                        FindObjectsSortMode.None)
                    .Single(component => component.GetType() == controllerType);
                float deadline = Time.realtimeSinceStartup + 6f;
                while (!(bool)GetProperty(controller, "IsReady") && Time.realtimeSinceStartup < deadline)
                    yield return null;

                Assert.That((bool)GetProperty(controller, "IsReady"), Is.True,
                    GetProperty(controller, "InitializationError") as string);
                int productCount = (int)GetProperty(controller, "ProductCount");
                Assert.That(productCount, Is.GreaterThanOrEqualTo(5));

                UIDocument document = controller.GetComponent<UIDocument>();
                var confirmation = GetPrivateField(controller, "confirmationPresenter") as MobileConfirmationPresenter;
                Assert.That(confirmation, Is.Not.Null);
                Assert.That(document.rootVisualElement.Q<VisualElement>("safe-area")
                    .ClassListContains("safe-area-bound"), Is.True);
                Assert.That(document.rootVisualElement.Q<VisualElement>("mobile-bottom-navigation")
                    .childCount, Is.EqualTo(5));
                Assert.That(document.rootVisualElement.Q<VisualElement>("nav-gacha")
                    .Q<VisualElement>("action-selection-indicator").ClassListContains("is-selected"), Is.True);
                ListView productList = document.rootVisualElement.Q<ListView>("product-list");
                Assert.That(productList.itemsSource.Count, Is.EqualTo(productCount));
                Assert.That(productList.virtualizationMethod, Is.EqualTo(CollectionVirtualizationMethod.DynamicHeight));
                Assert.That(controller.gameObject.scene.GetRootGameObjects()
                    .SelectMany(sceneRoot => sceneRoot.GetComponentsInChildren<Canvas>(true))
                    .All(canvas => !canvas.gameObject.activeInHierarchy), Is.True);

                var viewport = new VisualElement { name = "gacha-contract-viewport" };
                viewport.style.position = Position.Relative;
                viewport.style.width = 720f;
                viewport.style.height = 1600f;
                VisualElement gachaHost = document.rootVisualElement.Q<VisualElement>("gacha-opening");
                VisualElement pageRoot = gachaHost.Q<VisualElement>("gacha-opening-page-shell");
                document.rootVisualElement.Clear();
                document.rootVisualElement.Add(viewport);
                viewport.Add(gachaHost);
                object pageShell = GetPrivateField(controller, "mobilePageShell");
                var safeAreaBinding = GetPrivateField(pageShell, "safeAreaBinding") as UiToolkitSafeAreaBinding;
                Assert.That(safeAreaBinding, Is.Not.Null);
                safeAreaBinding.Suspend();
                VisualElement safeArea = pageRoot.Q<VisualElement>("safe-area");
                safeArea.AddToClassList("mobile-layout--compact");
                safeArea.style.paddingLeft = 48f;
                safeArea.style.paddingTop = 60f;
                safeArea.style.paddingRight = 12f;
                safeArea.style.paddingBottom = 84f;
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
                ScrollView selectedScroll = pageRoot.Q<ScrollView>("selected-product-scroll");
                Assert.That(selectedScroll.worldBound.yMin,
                    Is.GreaterThanOrEqualTo(productList.worldBound.yMax - 1f));
                ApplicationServices.Languages.SelectUiLanguage("ja");
                yield return null;
                yield return null;
                VisualElement prepareTen = pageRoot.Q<VisualElement>("prepare-ten-button");
                selectedScroll.ScrollTo(prepareTen);
                yield return null;
                yield return null;
                Assert.That(selectedScroll.contentViewport.worldBound.Contains(prepareTen.worldBound.center), Is.True,
                    $"Selected viewport {selectedScroll.contentViewport.worldBound}; action {prepareTen.worldBound}; " +
                    $"scroll {selectedScroll.worldBound}.");
                Assert.That(prepareTen.worldBound.height, Is.GreaterThanOrEqualTo(48f));
                UiToolkitSafeAreaBinding sheetBinding = GetPrivateField(
                    confirmation.Sheet,
                    "safeAreaBinding") as UiToolkitSafeAreaBinding;
                Assert.That(sheetBinding, Is.Not.Null);
                sheetBinding.Suspend();
                VisualElement sheetSafeArea = pageRoot.Q<VisualElement>("sheet-safe-area");
                sheetSafeArea.AddToClassList("mobile-layout--compact");
                sheetSafeArea.style.paddingLeft = 48f;
                sheetSafeArea.style.paddingTop = 60f;
                sheetSafeArea.style.paddingRight = 12f;
                sheetSafeArea.style.paddingBottom = 84f;
                SendTap(prepareTen);
                yield return null;
                yield return null;
                Assert.That(confirmation.IsVisible, Is.True);
                Assert.That(pageRoot.Q<Label>("sheet-title").text, Is.Not.EqualTo("Confirm pack opening"));
                Rect sheetSafeContent = InsetRect(sheetSafeArea.worldBound, sheetSafeArea.resolvedStyle);
                VisualElement sheetPanel = pageRoot.Q<VisualElement>("sheet-panel");
                AssertContained(sheetSafeContent, sheetPanel.worldBound,
                    $"Japanese confirmation sheet; safe={sheetSafeContent}; panel={sheetPanel.worldBound}; " +
                    $"resolved width={sheetPanel.resolvedStyle.width}, margins=" +
                    $"{sheetPanel.resolvedStyle.marginLeft}/{sheetPanel.resolvedStyle.marginRight}");
                Assert.That(pageRoot.Q<VisualElement>("confirmation-confirm").worldBound.height,
                    Is.GreaterThanOrEqualTo(48f));
                Assert.That(pageRoot.Q<VisualElement>("confirmation-cancel").worldBound.height,
                    Is.GreaterThanOrEqualTo(48f));
                SendTap(confirmation.Cancel.Root);
                yield return null;
                Assert.That(confirmation.IsVisible, Is.False);
                ApplicationServices.Languages.SelectUiLanguage("en");
                yield return null;
                int initialBaseIndex = productList.itemsSource.Cast<ProductDefinition>()
                    .Select((product, index) => new { product, index })
                    .Single(pair => pair.product.SetId.EndsWith(":base1", StringComparison.Ordinal))
                    .index;
                productList.SetSelection(initialBaseIndex);
                yield return null;

                Assert.That(GetProperty(controller, "SelectedRuleTrust").ToString(), Is.EqualTo("HistoricallyVerified"));
                Assert.That(GetProperty(controller, "SelectedRuleConfidence").ToString(), Is.EqualTo("Corroborated"));
                Assert.That((string)GetProperty(controller, "SelectedRuleRegionId"),
                    Is.EqualTo("pokemon-international-en"));
                Assert.That((int)GetProperty(controller, "SelectedRuleEvidenceCount"), Is.EqualTo(2));

                Assert.That((string)GetProperty(controller, "SelectedThemeId"),
                    Is.EqualTo("pokemon-base1-vintage"));
                Assert.That((string)GetProperty(controller, "SelectedThemePackAudioKey"),
                    Is.EqualTo("pack.open.vintage"));
                Assert.That((string)GetProperty(controller, "SelectedThemeArtworkResourcePath"),
                    Is.EqualTo("Gacha/Themes/vintage-pack"));
                Assert.That((bool)GetProperty(controller, "HasSelectedThemeArtwork"), Is.True);
                Assert.That(document.rootVisualElement.Q<VisualElement>("gacha-opening")
                    .ClassListContains("gacha-theme--vintage"), Is.True);
                originalUiLanguage = ApplicationServices.Languages.UiLanguageId;
                originalContentLanguage = ApplicationServices.Languages.RequestedContentLanguageId;
                ApplicationServices.Languages.SelectUiLanguage("zh");
                yield return null;
                Assert.That(document.rootVisualElement.Q<Label>("gacha-title").text, Is.EqualTo("开启卡包"));
                Assert.That(ActionText(document.rootVisualElement, "prepare-pack-button"), Is.EqualTo("开 1 包"));
                Assert.That(ActionText(document.rootVisualElement, "prepare-ten-button"), Is.EqualTo("十连开包"));
                Assert.That(document.rootVisualElement.Q<Label>("rule-evidence-summary").text,
                    Does.Contain("已佐证").And.Contain("2026-07-23"));
                Assert.That(document.rootVisualElement.Q<VisualElement>("rule-source-list").childCount,
                    Is.EqualTo(2));
                Assert.That(document.rootVisualElement.Q<VisualElement>("rule-source-list")
                    .Children().First().Q<Label>().text, Does.StartWith("来源 1："));
                ApplicationServices.Languages.SelectUiLanguage("en");
                yield return null;
                Assert.That(document.rootVisualElement.Q<Label>("gacha-title").text, Is.EqualTo("Open a Pack"));
                Assert.That(ActionText(document.rootVisualElement, "prepare-pack-button"), Is.EqualTo("Open 1 pack"));
                Assert.That(ActionText(document.rootVisualElement, "prepare-ten-button"), Is.EqualTo("Open 10 packs"));
                Assert.That(document.rootVisualElement.Q<Label>("rule-evidence-summary").text,
                    Does.Contain("Corroborated").And.Contain("2026-07-23"));
                Assert.That(document.rootVisualElement.Q<VisualElement>("rule-source-list")
                    .Children().First().Q<Label>().text, Does.StartWith("Source 1:"));

                int exIndex = productList.itemsSource.Cast<ProductDefinition>()
                    .Select((product, index) => new { product, index })
                    .Single(pair => pair.product.SetId.EndsWith(":ex1", StringComparison.Ordinal))
                    .index;
                productList.SetSelection(exIndex);
                yield return null;
                Assert.That((string)GetProperty(controller, "SelectedRuleProfileId"),
                    Is.EqualTo("pokemon-ex1-psa-empirical-v1"));
                Assert.That((int)GetProperty(controller, "SelectedRuleEvidenceCount"), Is.EqualTo(1));
                Assert.That(document.rootVisualElement.Q<Label>("rule-notice").text,
                    Does.Contain("1 Reverse Holo"));
                Assert.That((string)GetProperty(controller, "SelectedThemeId"),
                    Is.EqualTo("pokemon-ex1-ruby"));
                Assert.That(document.rootVisualElement.Q<VisualElement>("gacha-opening")
                    .ClassListContains("gacha-theme--ruby"), Is.True);

                int sourcedIndex = productList.itemsSource.Cast<ProductDefinition>()
                    .Select((product, index) => new { product, index })
                    .Single(pair => pair.product.SetId.EndsWith(":swsh1", StringComparison.Ordinal))
                    .index;
                productList.SetSelection(sourcedIndex);
                yield return null;
                Assert.That(GetProperty(controller, "SelectedRuleTrust").ToString(),
                    Is.EqualTo("SourceInformedSimulation"));
                Assert.That(GetProperty(controller, "SelectedRuleConfidence").ToString(),
                    Is.EqualTo("Corroborated"));
                Assert.That(document.rootVisualElement.Q<Label>("rule-badge").text,
                    Is.EqualTo("SOURCED SIMULATION"));
                Assert.That(document.rootVisualElement.Q<Label>("rule-notice").text,
                    Does.Contain("5 Common").And.Contain("Basic Energy"));
                Assert.That(document.rootVisualElement.Q<Label>("rule-evidence-summary").text,
                    Does.Contain("Corroborated").And.Contain("2026-07-25"));
                Assert.That(document.rootVisualElement.Q<VisualElement>("rule-source-list").childCount,
                    Is.EqualTo(3));
                Assert.That((string)GetProperty(controller, "SelectedThemeId"),
                    Is.EqualTo("pokemon-swsh1-electric"));
                Assert.That((string)GetProperty(controller, "SelectedThemeArtworkResourcePath"),
                    Is.EqualTo("Gacha/Themes/electric-pack"));
                Assert.That((bool)GetProperty(controller, "HasSelectedThemeArtwork"), Is.True);
                Assert.That(document.rootVisualElement.Q<VisualElement>("gacha-opening")
                    .ClassListContains("gacha-theme--electric"), Is.True);

                int scarletVioletIndex = productList.itemsSource.Cast<ProductDefinition>()
                    .Select((product, index) => new { product, index })
                    .Single(pair => pair.product.SetId.EndsWith(":sv01", StringComparison.Ordinal))
                    .index;
                productList.SetSelection(scarletVioletIndex);
                yield return null;
                Assert.That((string)GetProperty(controller, "SelectedRuleProfileId"),
                    Is.EqualTo("pokemon-sv01-sourced-simulation-v1"));
                Assert.That(GetProperty(controller, "SelectedRuleTrust").ToString(),
                    Is.EqualTo("SourceInformedSimulation"));
                Assert.That(GetProperty(controller, "SelectedRuleConfidence").ToString(),
                    Is.EqualTo("Corroborated"));
                Assert.That(document.rootVisualElement.Q<Label>("rule-badge").text,
                    Is.EqualTo("SOURCED SIMULATION"));
                Assert.That(document.rootVisualElement.Q<Label>("rule-notice").text,
                    Does.Contain("4 Common").And.Contain("2 foil slots"));
                Assert.That(document.rootVisualElement.Q<Label>("rule-evidence-summary").text,
                    Does.Contain("Corroborated").And.Contain("2026-07-25"));
                Assert.That(document.rootVisualElement.Q<VisualElement>("rule-source-list").childCount,
                    Is.EqualTo(2));
                Assert.That((string)GetProperty(controller, "SelectedThemeId"),
                    Is.EqualTo("pokemon-sv01-gallery"));
                Assert.That(document.rootVisualElement.Q<VisualElement>("gacha-opening")
                    .ClassListContains("gacha-theme--gallery"), Is.True);

                int baseIndex = productList.itemsSource.Cast<ProductDefinition>()
                    .Select((product, index) => new { product, index })
                    .Single(pair => pair.product.SetId.EndsWith(":base1", StringComparison.Ordinal))
                    .index;
                productList.SetSelection(baseIndex);
                yield return null;
                Assert.That(GetProperty(controller, "SelectedRuleTrust").ToString(), Is.EqualTo("HistoricallyVerified"));
                Assert.That(document.rootVisualElement.Q<VisualElement>("rule-source-list").childCount,
                    Is.EqualTo(2));
                Assert.That((string)GetProperty(controller, "SelectedThemeId"),
                    Is.EqualTo("pokemon-base1-vintage"));
                Assert.That(document.rootVisualElement.Q<VisualElement>("gacha-opening")
                    .ClassListContains("gacha-theme--vintage"), Is.True);
                Assert.That(document.rootVisualElement.Q<VisualElement>("gacha-opening")
                    .ClassListContains("gacha-theme--gallery"), Is.False);

                VisualElement preparePack = document.rootVisualElement.Q<VisualElement>("prepare-pack-button");
                selectedScroll.ScrollTo(preparePack);
                yield return null;
                SendTap(preparePack);
                yield return null;
                Assert.That((bool)GetProperty(controller, "IsConfirmationVisible"), Is.True);
                Assert.That(document.rootVisualElement.Q<Label>("sheet-body").text,
                    Does.Contain("Card language:").And.Contain("Rule:"));
                SendTap(confirmation.Cancel.Root);
                yield return null;
                Assert.That((bool)GetProperty(controller, "IsConfirmationVisible"), Is.False);
                Assert.That((string)GetProperty(controller, "CurrentStage"), Is.EqualTo("Selection"));
                Assert.That(store.ProductsOpened, Is.Zero);
                deadline = Time.realtimeSinceStartup + 1f;
                while (confirmation.Root.resolvedStyle.display != DisplayStyle.None &&
                       Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That(confirmation.Root.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
                selectedScroll.ScrollTo(preparePack);
                yield return null;
                yield return null;
                Assert.That(selectedScroll.contentViewport.worldBound.Contains(preparePack.worldBound.center), Is.True);
                SendTap(preparePack);
                yield return null;
                yield return null;
                Assert.That((bool)GetProperty(controller, "IsConfirmationVisible"), Is.True,
                    "Reopening the confirmation after cancellation should succeed.");
                VisualElement confirmOpen = confirmation.Confirm.Root;
                deadline = Time.realtimeSinceStartup + 1f;
                while (confirmOpen.worldBound.width <= 0f && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That(confirmation.Confirm.IsEnabled, Is.True);
                Assert.That(confirmOpen.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(confirmOpen.worldBound.width, Is.GreaterThan(0f));
                Assert.That(confirmOpen.worldBound.height, Is.GreaterThanOrEqualTo(48f));
                SendTap(confirmOpen);
                Assert.That((bool)GetProperty(controller, "IsConfirmationVisible"), Is.False,
                    "The real pointer sequence should activate the confirmation action.");
                Assert.That((string)GetProperty(controller, "CurrentStage"), Is.EqualTo("Prepared"),
                    "The confirmation callback should advance the opening state exactly once.");
                SendTap(confirmOpen);
                yield return null;
                Assert.That((string)GetProperty(controller, "CurrentStage"), Is.EqualTo("Prepared"));
                Assert.That(store.ProductsOpened, Is.Zero);
                Assert.That(document.rootVisualElement.Q<VisualElement>("pack-stage").resolvedStyle.display,
                    Is.EqualTo(DisplayStyle.Flex));
                Assert.That(document.rootVisualElement.Q<VisualElement>("pack-theme-artwork")
                    .ClassListContains("is-visible"), Is.True);
                Assert.That((bool)GetProperty(controller, "ArePackParticlesRunning"), Is.True);
                Assert.That((int)GetProperty(controller, "PackParticleCount"), Is.EqualTo(6));
                Assert.That(document.rootVisualElement.Q<VisualElement>("pack-particle-layer").childCount,
                    Is.EqualTo(ThemeParticleField.MaximumParticleCount));
                Assert.That(InvokeBool(controller, "TearPack"), Is.True);

                deadline = Time.realtimeSinceStartup + 3f;
                VisualElement revealStage = document.rootVisualElement.Q<VisualElement>("reveal-stage");
                while (revealStage.resolvedStyle.display != DisplayStyle.Flex && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That((bool)GetProperty(controller, "ArePackParticlesRunning"), Is.False);

                int openedCardCount = (int)GetProperty(controller, "LastOpenedCardCount");
                Assert.That(openedCardCount, Is.EqualTo(11));
                ApplicationServices.Languages.SelectUiLanguage("zh");
                yield return null;
                Assert.That(document.rootVisualElement.Q<Label>("reveal-progress").text, Is.EqualTo("第 0 / 11 张"));
                Assert.That(ActionText(document.rootVisualElement, "reveal-next-button"), Is.EqualTo("翻开第一张"));
                ApplicationServices.Languages.SelectUiLanguage("en");
                yield return null;
                Assert.That(document.rootVisualElement.Q<Label>("reveal-progress").text, Is.EqualTo("0 of 11 cards"));
                Assert.That(ActionText(document.rootVisualElement, "reveal-next-button"), Is.EqualTo("Reveal first card"));
                Assert.That(store.ProductsOpened, Is.EqualTo(1));
                Assert.That(store.TotalCards, Is.EqualTo(openedCardCount));
                Assert.That(cues, Does.Contain(FeedbackCue.PackOpen));

                for (int index = 0; index < openedCardCount; index++)
                {
                    Assert.That(InvokeBool(controller, "RevealNextCard"), Is.True);
                    yield return new WaitForSecondsRealtime(0.28f);
                }

                deadline = Time.realtimeSinceStartup + 4f;
                while ((int)GetProperty(controller, "CachedTextureCount") == 0 && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That((int)GetProperty(controller, "RevealedCount"), Is.EqualTo(openedCardCount));
                Assert.That((int)GetProperty(controller, "CachedTextureCount"), Is.GreaterThan(0));
                long cachedBytes = (long)GetProperty(controller, "CachedTextureBytes");
                long budgetBytes = (long)GetProperty(controller, "CachedTextureBudgetBytes");
                Assert.That(cachedBytes, Is.GreaterThan(0L));
                Assert.That(cachedBytes, Is.LessThanOrEqualTo(budgetBytes));
                Assert.That((bool)GetProperty(controller, "IsCurrentRevealHighlighted"), Is.True);
                Assert.That((bool)GetProperty(controller, "AreRevealParticlesRunning"), Is.True);
                Assert.That((int)GetProperty(controller, "RevealParticleCount"), Is.EqualTo(10));
                Assert.That(document.rootVisualElement.Q<VisualElement>("reveal-aura")
                    .ClassListContains("is-highlighted"), Is.True);
                Assert.That(cues.Count(cue => cue == FeedbackCue.CardFlip), Is.EqualTo(openedCardCount));
                Assert.That(cues, Does.Contain(FeedbackCue.RareReveal));

                Assert.That(InvokeBool(controller, "RevealNextCard"), Is.True);
                yield return null;
                Assert.That((bool)GetProperty(controller, "IsSummaryVisible"), Is.True);
                Assert.That((bool)GetProperty(controller, "AreRevealParticlesRunning"), Is.False);
                Assert.That(cues, Does.Contain(FeedbackCue.CollectionNew));
                ApplicationServices.Languages.SelectUiLanguage("zh");
                yield return null;
                Assert.That(document.rootVisualElement.Q<Label>("summary-title").text, Is.EqualTo("开包完成"));
                Assert.That(document.rootVisualElement.Q<Label>("summary-metadata").text, Does.Contain("张卡牌"));
                ApplicationServices.Languages.SelectUiLanguage("en");
                yield return null;
                Assert.That(document.rootVisualElement.Q<Label>("summary-title").text, Is.EqualTo("Pack complete"));
                Assert.That(document.rootVisualElement.Q<Label>("summary-metadata").text, Does.Contain("cards"));

                string frozenLanguage = (string)GetProperty(controller, "FrozenContentLanguageId");
                string firstSummaryName = document.rootVisualElement
                    .Q<ScrollView>("summary-list").contentContainer
                    .Q<Label>(className: "gacha-summary-row__name").text;
                UniversalCatalog runtimeCatalog = GetPrivateField(controller, "catalog") as UniversalCatalog;
                ApplicationServices.Languages.SelectContentLanguage("ja", runtimeCatalog);
                yield return null;
                Assert.That((string)GetProperty(controller, "CurrentStage"), Is.EqualTo("Summary"));
                Assert.That((bool)GetProperty(controller, "IsSummaryVisible"), Is.True);
                Assert.That((string)GetProperty(controller, "FrozenContentLanguageId"), Is.EqualTo(frozenLanguage));
                Assert.That(document.rootVisualElement.Q<ScrollView>("summary-list").contentContainer
                    .Q<Label>(className: "gacha-summary-row__name").text, Is.EqualTo(firstSummaryName));
                UIFeedbackService.Configure(true, false, 1f, false);
                SendTap(document.rootVisualElement.Q<VisualElement>("open-again-button"));
                yield return null;
                Assert.That((string)GetProperty(controller, "CurrentStage"), Is.EqualTo("Selection"));
                Assert.That((string)GetProperty(controller, "FrozenContentLanguageId"), Is.Null);
                Assert.That((bool)GetProperty(controller, "IsConfirmationVisible"), Is.True);
                Assert.That(document.rootVisualElement.Q<Label>("sheet-body").text, Does.Contain("ja"));
                yield return null;
                SendTap(confirmation.Confirm.Root);
                yield return null;
                Assert.That((string)GetProperty(controller, "FrozenContentLanguageId"), Is.EqualTo("ja"));
                Assert.That(InvokeBool(controller, "TearPack"), Is.True);
                Assert.That((string)GetProperty(controller, "CurrentStage"), Is.EqualTo("Revealing"));
                Assert.That((bool)GetProperty(controller, "ArePackParticlesRunning"), Is.False);
                Assert.That((bool)GetProperty(controller, "AreRevealParticlesRunning"), Is.False);
                deadline = Time.realtimeSinceStartup + 3f;
                while (revealStage.resolvedStyle.display != DisplayStyle.Flex &&
                       Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That(InvokeBool(controller, "RevealAllCards"), Is.True);
                yield return null;
                Assert.That(store.GetOpeningHistory(1).Single().LanguageId, Is.EqualTo("ja"));
                UIFeedbackService.Configure(false, true, 1f, true);
                ApplicationServices.Languages.SelectContentLanguage(originalContentLanguage, runtimeCatalog);
                yield return null;
                InvokePrivate(controller, "ShowSelectionPage");
                yield return null;

                int neoIndex = productList.itemsSource.Cast<ProductDefinition>()
                    .Select((product, index) => new { product, index })
                    .Single(pair => pair.product.SetId.EndsWith(":neo1", StringComparison.Ordinal))
                    .index;
                productList.SetSelection(neoIndex);
                yield return null;
                Assert.That((string)GetProperty(controller, "SelectedRuleProfileId"),
                    Is.EqualTo("pokemon-neo1-first-edition-psa-v1"));
                Assert.That((int)GetProperty(controller, "SelectedRuleEvidenceCount"), Is.EqualTo(1));
                Assert.That(document.rootVisualElement.Q<VisualElement>("rule-source-list").childCount,
                    Is.EqualTo(1));
                string ruleNotice = document.rootVisualElement.Q<Label>("rule-notice").text;
                Assert.That(ruleNotice.Contains("First Edition") || ruleNotice.Contains("第一版"), Is.True);
                Assert.That((string)GetProperty(controller, "SelectedThemeId"),
                    Is.EqualTo("pokemon-neo1-forest"));
                Assert.That(document.rootVisualElement.Q<VisualElement>("gacha-opening")
                    .ClassListContains("gacha-theme--forest"), Is.True);

                Assert.That(InvokeBool(controller, "PrepareSelectedProduct"), Is.True);
                Assert.That(InvokeBool(controller, "TearPack"), Is.True);
                InvokePrivate(controller, "OnApplicationPause", true);
                Assert.That((string)GetProperty(controller, "CurrentStage"), Is.EqualTo("Revealing"));
                Assert.That((bool)GetProperty(controller, "ArePackParticlesRunning"), Is.False);
                yield return new WaitForSecondsRealtime(0.35f);
                Assert.That((string)GetProperty(controller, "CurrentStage"), Is.EqualTo("Revealing"));
                deadline = Time.realtimeSinceStartup + 3f;
                while (revealStage.resolvedStyle.display != DisplayStyle.Flex && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That((int)GetProperty(controller, "LastOpenedCardCount"), Is.EqualTo(11));
                Assert.That(store.LastCommittedIds.Count, Is.EqualTo(11));
                Assert.That(store.LastCommittedIds.All(id => id.Contains("first-edition")), Is.True);

                VisualElement revealAll = document.rootVisualElement.Q<VisualElement>("reveal-all-button");
                Assert.That(revealAll, Is.Not.Null);
                Assert.That(revealAll.ClassListContains("gacha-button--quiet"), Is.True);
                Assert.That(InvokeBool(controller, "RevealAllCards"), Is.True);
                yield return null;
                Assert.That((int)GetProperty(controller, "RevealedCount"), Is.EqualTo(11));
                Assert.That((bool)GetProperty(controller, "IsSummaryVisible"), Is.True);
                Assert.That(InvokeBool(controller, "RevealAllCards"), Is.False);

                VisualElement summaryProducts =
                    document.rootVisualElement.Q<VisualElement>("summary-products-button");
                deadline = Time.realtimeSinceStartup + 1f;
                while (summaryProducts.worldBound.height < 48f && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That(summaryProducts.worldBound.height, Is.GreaterThanOrEqualTo(48f));
                SendTap(summaryProducts);
                yield return null;
                Assert.That((string)GetProperty(controller, "CurrentStage"), Is.EqualTo("Selection"));
                deadline = Time.realtimeSinceStartup + 1f;
                do
                {
                    selectedScroll.ScrollTo(prepareTen);
                    yield return null;
                } while (!selectedScroll.contentViewport.worldBound.Contains(prepareTen.worldBound.center) &&
                         Time.realtimeSinceStartup < deadline);
                Assert.That(selectedScroll.contentViewport.worldBound.Contains(prepareTen.worldBound.center), Is.True);
                SendTap(prepareTen);
                yield return null;
                yield return null;
                Assert.That((bool)GetProperty(controller, "IsConfirmationVisible"), Is.True);
                VisualElement confirmTen = confirmation.Confirm.Root;
                SendTap(confirmTen);
                SendTap(confirmTen);
                yield return null;
                Assert.That((int)GetProperty(controller, "PreparedProductCount"), Is.EqualTo(10));
                Assert.That(document.rootVisualElement.Q<Label>("pack-hint").text,
                    Does.Contain("first of 10 packs"));
                Assert.That(InvokeBool(controller, "TearPack"), Is.True);
                deadline = Time.realtimeSinceStartup + 3f;
                while (revealStage.resolvedStyle.display != DisplayStyle.Flex && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That((int)GetProperty(controller, "LastOpenedProductCount"), Is.EqualTo(10));
                Assert.That((int)GetProperty(controller, "LastOpenedCardCount"), Is.EqualTo(110));
                Assert.That(store.ProductsOpened, Is.EqualTo(13));
                Assert.That(InvokeBool(controller, "RevealAllCards"), Is.True);
                yield return null;
                Assert.That(document.rootVisualElement.Q<Label>("summary-title").text,
                    Is.EqualTo("Batch complete"));
                Assert.That(document.rootVisualElement.Q<Label>("summary-metadata").text,
                    Does.Contain("10 packs").And.Contain("110 cards"));
                Assert.That((int)GetProperty(controller, "RecentHistoryCount"), Is.EqualTo(4));
                Assert.That(document.rootVisualElement.Q<Label>("opening-statistics").text,
                    Does.Contain("13 packs").And.Contain("137 cards"));
                Assert.That(document.rootVisualElement.Q<VisualElement>("opening-history").childCount,
                    Is.EqualTo(4));
                Assert.That(cues.Count(cue => cue == FeedbackCue.PackOpen), Is.EqualTo(4));

                int routeCalls = 0;
                string requestedScene = null;
                sceneLoaderOverride.SetValue(null, (Action<string>)(sceneName =>
                {
                    routeCalls++;
                    requestedScene = sceneName;
                }));
                SendTap(document.rootVisualElement.Q<VisualElement>("nav-gacha"));
                yield return null;
                Assert.That(routeCalls, Is.Zero);
                SendTap(document.rootVisualElement.Q<VisualElement>("nav-content"));
                SendTap(document.rootVisualElement.Q<VisualElement>("nav-settings"));
                yield return null;
                Assert.That(routeCalls, Is.EqualTo(1));
                Assert.That(requestedScene, Is.EqualTo("006_ContentScene"));
                Assert.That(document.rootVisualElement.Q<VisualElement>("nav-content")
                    .Q<VisualElement>("action-selection-indicator").ClassListContains("is-selected"), Is.True);
                Assert.That(document.rootVisualElement.Q<VisualElement>("nav-gacha")
                    .Q<VisualElement>("action-selection-indicator").ClassListContains("is-selected"), Is.False);
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(originalUiLanguage) && ApplicationServices.IsConfigured)
                    ApplicationServices.Languages.SelectUiLanguage(originalUiLanguage);
                if (!string.IsNullOrWhiteSpace(originalContentLanguage) && ApplicationServices.IsConfigured)
                {
                    UniversalCatalog installed = ApplicationServices.Catalog.Catalog;
                    if (installed != null)
                        ApplicationServices.Languages.SelectContentLanguage(originalContentLanguage, installed);
                }
                UIFeedbackService.FeedbackPlayed -= cues.Add;
                storeOverride.SetValue(null, null);
                sceneLoaderOverride.SetValue(null, null);
                UIFeedbackService.Configure(
                    originalReduceMotion,
                    originalHaptics,
                    originalAnimationSpeed,
                    originalSound);
            }
        }

        private static object GetProperty(object target, string name)
        {
            return target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(target);
        }

        private static string ActionText(VisualElement root, string name) =>
            root.Q<VisualElement>(name)?.Q<Label>()?.text;

        private static bool InvokeBool(object target, string name, params object[] arguments)
        {
            return (bool)target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public)
                ?.Invoke(target, arguments);
        }

        private static object GetPrivateField(object target, string name) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(target);

        private static void InvokePrivate(object target, string name, params object[] arguments) =>
            target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(target, arguments);

        private static Rect InsetRect(Rect bounds, IResolvedStyle style) => new Rect(
            bounds.xMin + style.paddingLeft,
            bounds.yMin + style.paddingTop,
            bounds.width - style.paddingLeft - style.paddingRight,
            bounds.height - style.paddingTop - style.paddingBottom);

        private static void AssertContained(Rect outer, Rect inner, string label)
        {
            Assert.That(inner.xMin, Is.GreaterThanOrEqualTo(outer.xMin - 1f), label);
            Assert.That(inner.yMin, Is.GreaterThanOrEqualTo(outer.yMin - 1f), label);
            Assert.That(inner.xMax, Is.LessThanOrEqualTo(outer.xMax + 1f), label);
            Assert.That(inner.yMax, Is.LessThanOrEqualTo(outer.yMax + 1f), label);
        }

        private static void SendTap(VisualElement control)
        {
            Assert.That(control, Is.Not.Null);
            using (PointerDownEvent down = PointerDownEvent.GetPooled(new Event
                   {
                       type = EventType.MouseDown,
                       button = 0,
                       mousePosition = control.worldBound.center
                   }))
                control.SendEvent(down);
            using (PointerUpEvent up = PointerUpEvent.GetPooled(new Event
                   {
                       type = EventType.MouseUp,
                       button = 0,
                       mousePosition = control.worldBound.center
                   }))
                control.SendEvent(up);
        }

        private sealed class MemoryProgressStore : IInventoryProgressStore
        {
            private readonly Dictionary<string, int> cards = new Dictionary<string, int>();
            private readonly List<ProductOpeningHistoryEntry> history = new List<ProductOpeningHistoryEntry>();
            private readonly Dictionary<string, int> productsByLanguage = new Dictionary<string, int>();
            private readonly Dictionary<string, int> productsBySet = new Dictionary<string, int>();
            private readonly Dictionary<string, int> cardsByRarity = new Dictionary<string, int>();
            public int ProductsOpened { get; private set; }
            public int TotalCards => cards.Values.Sum();
            public IReadOnlyList<string> LastCommittedIds { get; private set; } = Array.Empty<string>();

            public int GetProductsOpened(string productId)
            {
                return ProductsOpened;
            }

            public ProductInventoryBatchCommit CommitBatch(ProductOpeningBatchCommitRequest request)
            {
                var commits = new List<ProductInventoryCommit>();
                LastCommittedIds = request.Draws.SelectMany(draw => draw.Printings)
                    .Select(printing => printing.PrintingId).ToArray();
                foreach (ProductDrawResult result in request.Draws)
                {
                    var awards = new List<InventoryAward>();
                    foreach (DrawnPrinting printing in result.Printings)
                    {
                        int previous = cards.TryGetValue(printing.PrintingId, out int count) ? count : 0;
                        cards[printing.PrintingId] = previous + 1;
                        awards.Add(new InventoryAward(printing.PrintingId, previous, previous + 1));
                    }
                    ProductsOpened++;
                    commits.Add(new ProductInventoryCommit(result.ProductId, ProductsOpened, awards.AsReadOnly()));
                }
                Add(productsByLanguage, request.LanguageId, request.Draws.Count);
                Add(productsBySet, request.SetId, request.Draws.Count);
                var rarityCounts = new Dictionary<string, int>();
                foreach (DrawnPrinting drawn in request.Draws.SelectMany(draw => draw.Printings))
                {
                    string rarityId = request.RarityByPrintingId[drawn.PrintingId];
                    Add(cardsByRarity, rarityId, 1);
                    Add(rarityCounts, rarityId, 1);
                }
                history.Add(new ProductOpeningHistoryEntry(
                    request.TransactionId,
                    request.OpenedAtUtc,
                    request.ProductId,
                    request.SetId,
                    request.LanguageId,
                    request.ProfileId,
                    request.Draws.Count,
                    request.Draws.Sum(draw => draw.Printings.Count),
                    commits.Sum(commit => commit.NewPrintingCount),
                    rarityCounts));
                return new ProductInventoryBatchCommit(request.TransactionId, commits.AsReadOnly());
            }

            public IReadOnlyList<ProductOpeningHistoryEntry> GetOpeningHistory(int maximumCount) => history
                .AsEnumerable()
                .Reverse()
                .Take(maximumCount)
                .ToList()
                .AsReadOnly();

            public ProductOpeningStatistics GetOpeningStatistics() =>
                new ProductOpeningStatistics(productsByLanguage, productsBySet, cardsByRarity);

            private static void Add(Dictionary<string, int> counts, string id, int amount)
            {
                counts[id] = counts.TryGetValue(id, out int current) ? current + amount : amount;
            }
        }

        private sealed class EmptyCatalogProvider : ICatalogProvider
        {
            public CatalogLoadResult Load() => CatalogLoadResult.Success(
                new UniversalCatalog(
                    Array.Empty<LanguageDefinition>(),
                    Array.Empty<GameDefinition>(),
                    Array.Empty<SetDefinition>(),
                    Array.Empty<CollectibleItemDefinition>(),
                    Array.Empty<RarityDefinition>(),
                    Array.Empty<VariantDefinition>(),
                    Array.Empty<PrintingDefinition>(),
                    Array.Empty<ProductDefinition>()),
                0,
                0,
                0);
        }
    }
}
