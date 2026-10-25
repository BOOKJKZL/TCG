using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Gacha.Application;
using Gacha.Domain;
using Gacha.Pokemon.Presentation;
using Gacha.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Gacha.Tests.PlayMode
{
    public sealed class FirstRunContentSetupPlayModeTests
    {
        private sealed class EmptyCatalogProvider : ICatalogProvider
        {
            public CatalogLoadResult Load()
            {
                return CatalogLoadResult.Success(new UniversalCatalog(
                    Array.Empty<LanguageDefinition>(),
                    Array.Empty<GameDefinition>(),
                    Array.Empty<SetDefinition>(),
                    Array.Empty<CollectibleItemDefinition>(),
                    Array.Empty<RarityDefinition>(),
                    Array.Empty<VariantDefinition>(),
                    Array.Empty<PrintingDefinition>(),
                    Array.Empty<ProductDefinition>()), 0, 0, 0);
            }
        }

        private sealed class CorruptCatalogProvider : ICatalogProvider
        {
            public CatalogLoadResult Load() => CatalogLoadResult.Failure(
                "/storage/private/Content sentinel-stack",
                CatalogFailureReason.CatalogCorrupt);
        }

        private sealed class EmptyRemoteCatalogProvider : IContentPackageCatalogProvider
        {
            public int LoadCalls { get; private set; }

            public Task<ContentPackageCatalogLoadResult> LoadAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LoadCalls++;
                return Task.FromResult(ContentPackageCatalogLoadResult.Success(
                    new ContentPackageCatalog(
                        ContentPackageCatalog.SupportedSchemaVersion,
                        1,
                        Array.Empty<ContentPackageCatalogEntry>())));
            }
        }

        private sealed class FixedRemoteCatalogProvider : IContentPackageCatalogProvider
        {
            private readonly ContentPackageCatalog catalog;

            public FixedRemoteCatalogProvider(ContentPackageCatalog catalog)
            {
                this.catalog = catalog;
            }

            public int LoadCalls { get; private set; }

            public Task<ContentPackageCatalogLoadResult> LoadAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LoadCalls++;
                return Task.FromResult(ContentPackageCatalogLoadResult.Success(catalog));
            }
        }

        private sealed class RecoverableRemoteCatalogProvider : IContentPackageCatalogProvider
        {
            public bool Fail { get; set; } = true;
            public int LoadCalls { get; private set; }

            public Task<ContentPackageCatalogLoadResult> LoadAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LoadCalls++;
                return Task.FromResult(Fail
                    ? ContentPackageCatalogLoadResult.Failure("/private/catalog sentinel-offline")
                    : ContentPackageCatalogLoadResult.Success(new ContentPackageCatalog(
                        ContentPackageCatalog.SupportedSchemaVersion, 1, Array.Empty<ContentPackageCatalogEntry>())));
            }
        }

        private sealed class BlockingRemoteCatalogProvider : IContentPackageCatalogProvider
        {
            private readonly TaskCompletionSource<ContentPackageCatalogLoadResult> completion =
                new TaskCompletionSource<ContentPackageCatalogLoadResult>();

            public int LoadCalls { get; private set; }
            public CancellationToken Token { get; private set; }

            public Task<ContentPackageCatalogLoadResult> LoadAsync(CancellationToken cancellationToken)
            {
                LoadCalls++;
                Token = cancellationToken;
                cancellationToken.Register(() => completion.TrySetCanceled());
                return completion.Task;
            }
        }

        private sealed class MemoryLanguageStore : ILanguagePreferenceStore
        {
            public LanguagePreferences Load() => new LanguagePreferences("en", "en");
            public void Save(LanguagePreferences preferences) { }
        }

        [UnityTest]
        public IEnumerator CleanInstall_ShowsLocalizedSetupAndRefreshesOnlyCatalogMetadata()
        {
            var remote = new EmptyRemoteCatalogProvider();
            ApplicationServices.Configure(
                new CatalogSession(new EmptyCatalogProvider()),
                new LanguageSelectionService(new MemoryLanguageStore(), new[] { "en", "zh", "ja" }),
                contentPackageCatalogs: remote);
            GameObject host = new GameObject("First Run Setup Host");
            try
            {
                Type controllerType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(assembly =>
                    {
                        try { return assembly.GetTypes(); }
                        catch { return Array.Empty<Type>(); }
                    })
                    .First(type => type.FullName == "Gacha.Presentation.FirstRunContentSetupController");
                Component controller = host.AddComponent(controllerType);

                float deadline = Time.realtimeSinceStartup + 5f;
                VisualElement setup = null;
                while (setup == null && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                    UIDocument document = host.GetComponent<UIDocument>();
                    setup = document?.rootVisualElement.Q<VisualElement>("first-run-content");
                }

                Assert.That(setup, Is.Not.Null);
                Assert.That(setup.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
                yield return null;
                AssertColor(setup.Q<ScrollView>("setup-scroll").resolvedStyle.backgroundColor, "#FFFFFF");
                AssertColor(setup.Q<Label>("setup-title").resolvedStyle.color, "#14171A");
                AssertColor(setup.Q<Label>("setup-body").resolvedStyle.color, "#556170");
                AssertColor(setup.Q<VisualElement>("setup-manage").resolvedStyle.backgroundColor, "#2563EB");
                AssertColor(setup.Q<VisualElement>("setup-manage").Q<Label>().resolvedStyle.color, "#FFFFFF");
                Assert.That(setup.Q<Label>("setup-title").text, Is.Not.Empty);
                Assert.That(setup.Q<Label>("setup-body").text, Does.Contain("no ZIP"));
                Assert.That(setup.Q<Label>("setup-storage").text, Does.Contain("app-managed"));
                Assert.That(setup.Query<Button>().ToList(), Is.Empty);
                Assert.That(setup.Q<VisualElement>("setup-manage"), Is.Not.Null);
                Assert.That(setup.Q<VisualElement>("setup-retry"), Is.Not.Null);
                Assert.That(setup.Q<Label>("setup-content-language-detail").text,
                    Does.Contain("independent"));
                Assert.That(setup.Q<VisualElement>("setup-content-language-en").Q<Label>()
                    .ClassListContains("is-selected"), Is.True);
                Assert.That(remote.LoadCalls, Is.EqualTo(1));
                string englishCatalogStatus = setup.Q<Label>("setup-catalog").text;
                Assert.That(englishCatalogStatus, Does.Contain("No downloadable packs"));

                ApplicationServices.Languages.SelectUiLanguage("zh");
                yield return null;
                Assert.That(ApplicationServices.Languages.UiLanguageId, Is.EqualTo("zh"));
                Assert.That(setup.Q<Label>("setup-catalog").text,
                    Is.Not.EqualTo(englishCatalogStatus));
                Assert.That(setup.Q<Label>("setup-catalog").text,
                    Does.Not.Contain("Catalog ready"));
                Assert.That(setup.Q<Label>("setup-catalog").text,
                    Does.Contain("目录中没有可下载内容"));
                Assert.That(setup.Q<Label>("setup-title").text, Does.Contain("语言"));

                controllerType.GetMethod("SelectContentLanguage", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(controller, new object[] { "zh-cn" });
                yield return null;
                Assert.That(ApplicationServices.Languages.UiLanguageId, Is.EqualTo("zh"));
                Assert.That(ApplicationServices.Languages.RequestedContentLanguageId, Is.EqualTo("zh-cn"));
                Assert.That(ApplicationServices.Languages.ContentLanguage.ResolvedLanguageId, Is.EqualTo("zh-cn"));
                Assert.That(setup.Q<VisualElement>("setup-content-language-zh").Q<Label>()
                    .ClassListContains("is-selected"), Is.True);

                controllerType.GetMethod("SelectContentLanguage", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(controller, new object[] { "ja" });
                yield return null;
                Assert.That(ApplicationServices.Languages.RequestedContentLanguageId, Is.EqualTo("ja"));
                Assert.That(setup.Q<VisualElement>("setup-content-language-ja").Q<Label>()
                    .ClassListContains("is-selected"), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                ApplicationServices.Reset();
            }
        }

        [UnityTest]
        public IEnumerator RecommendedFirstPack_OnlyCarriesIntentToContentLibrary()
        {
            var remote = new FixedRemoteCatalogProvider(RecommendationCatalog());
            ApplicationServices.Configure(
                new CatalogSession(new EmptyCatalogProvider()),
                new LanguageSelectionService(new MemoryLanguageStore(), new[] { "en", "zh-cn", "ja" }),
                contentPackageCatalogs: remote);
            string requestedScene = null;
            Type controllerType = FindFirstRunControllerType();
            PropertyInfo sceneLoader = controllerType.GetProperty(
                "SceneLoaderOverride", BindingFlags.Static | BindingFlags.Public);
            sceneLoader?.SetValue(null, new Action<string>(scene => requestedScene = scene));
            GameObject host = new GameObject("Recommended First Run Setup Host");
            try
            {
                Component controller = host.AddComponent(controllerType);
                VisualElement setup = null;
                float deadline = Time.realtimeSinceStartup + 5f;
                while (setup == null && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                    setup = host.GetComponent<UIDocument>()?.rootVisualElement
                        .Q<VisualElement>("first-run-content");
                }
                VisualElement recommendation = setup?.Q<VisualElement>("setup-recommendation");
                while (recommendation != null &&
                       recommendation.resolvedStyle.display != DisplayStyle.Flex &&
                       Time.realtimeSinceStartup < deadline)
                    yield return null;

                Assert.That(recommendation, Is.Not.Null);
                Assert.That(recommendation.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(setup.Q<Label>("setup-recommendation-name").text, Is.EqualTo("Starter"));
                Assert.That(remote.LoadCalls, Is.EqualTo(1));
                Assert.That(ApplicationServices.Languages.RequestedContentLanguageId, Is.EqualTo("en"));
                Assert.That(ContentLaunchRequest.ConsumeRecommendation(), Is.Null,
                    "Metadata refresh must not create a launch request before player confirmation.");

                VisualElement recommendedAction = setup.Q<VisualElement>("setup-recommended");
                var viewport = new VisualElement { name = "first-run-contract-viewport" };
                viewport.style.position = Position.Relative;
                viewport.style.width = 720f;
                viewport.style.height = 1600f;
                UIDocument document = host.GetComponent<UIDocument>();
                document.rootVisualElement.Clear();
                document.rootVisualElement.Add(viewport);
                viewport.Add(setup);
                FieldInfo safeAreaField = controllerType.GetField(
                    "safeArea", BindingFlags.Instance | BindingFlags.NonPublic);
                var safeArea = safeAreaField?.GetValue(controller) as UiToolkitSafeAreaBinding;
                Assert.That(safeArea, Is.Not.Null);
                safeArea.Suspend();
                setup.AddToClassList("mobile-layout--compact");
                setup.style.paddingLeft = 48f;
                setup.style.paddingTop = 60f;
                setup.style.paddingRight = 12f;
                setup.style.paddingBottom = 84f;
                yield return null;
                yield return null;

                Rect safeContent = InsetRect(setup.worldBound, setup.resolvedStyle);
                ScrollView setupScroll = setup.Q<ScrollView>("setup-scroll");
                AssertContained(safeContent, setupScroll.worldBound, "first-run scroll panel");
                VisualElement laterAction = setup.Q<VisualElement>("setup-later");
                foreach (string localeId in new[] { "en", "zh", "ja" })
                {
                    ApplicationServices.Languages.SelectUiLanguage(localeId);
                    yield return null;
                    setupScroll.ScrollTo(laterAction);
                    yield return null;
                    yield return null;
                    Assert.That(setupScroll.contentViewport.worldBound.Contains(laterAction.worldBound.center),
                        Is.True, localeId);
                    Assert.That(laterAction.resolvedStyle.height, Is.GreaterThanOrEqualTo(48f), localeId);
                    setupScroll.ScrollTo(recommendedAction);
                    yield return null;
                    yield return null;
                    Assert.That(setupScroll.contentViewport.worldBound.Contains(recommendedAction.worldBound.center),
                        Is.True, localeId);
                    Assert.That(recommendedAction.resolvedStyle.height, Is.GreaterThanOrEqualTo(48f), localeId);
                }
                yield return null;
                SendTap(recommendedAction);
                yield return null;

                Assert.That(requestedScene, Is.EqualTo("006_ContentScene"));
                Assert.That(ContentLaunchRequest.ConsumeRecommendation(), Is.EqualTo("en.starter"));
                Assert.That(ApplicationServices.Languages.RequestedContentLanguageId, Is.EqualTo("en"));
            }
            finally
            {
                sceneLoader?.SetValue(null, null);
                ContentLaunchRequest.Clear();
                UnityEngine.Object.DestroyImmediate(host);
                ApplicationServices.Reset();
            }
        }

        [UnityTest]
        public IEnumerator FirstRun_RuntimeFixtureCapturesEmptyAndRecoverableCatalogWithLongTextAfterResize()
        {
            var remote = new RecoverableRemoteCatalogProvider();
            ApplicationServices.Configure(
                new CatalogSession(new EmptyCatalogProvider()),
                new LanguageSelectionService(new MemoryLanguageStore(), new[] { "en", "zh", "ja" }),
                contentPackageCatalogs: remote);
            Type controllerType = FindFirstRunControllerType();
            FieldInfo dismissed = controllerType.GetField("dismissedForSession", BindingFlags.Static | BindingFlags.NonPublic);
            object originalDismissed = dismissed.GetValue(null);
            dismissed.SetValue(null, false);
            GameObject host = new GameObject("First Run Runtime Capture Fixture");
            var originalLocale = LocalizationSettings.SelectedLocale;
            CoreNeutralSceneCapture capture = null;
            MobilePageShell backdrop = null;
            try
            {
                Component controller = host.AddComponent(controllerType);
                VisualElement setup = null;
                float deadline = Time.realtimeSinceStartup + 5f;
                while (setup == null && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                    setup = host.GetComponent<UIDocument>()?.rootVisualElement.Q<VisualElement>("first-run-content");
                }
                Assert.That(setup, Is.Not.Null);
                Assert.That(remote.LoadCalls, Is.EqualTo(1));
                UIDocument document = host.GetComponent<UIDocument>();
                var viewport = new VisualElement { name = "first-run-capture-contract-viewport" };
                viewport.style.position = Position.Relative;
                viewport.style.width = 720f;
                viewport.style.height = 1600f;
                document.rootVisualElement.Clear();
                document.rootVisualElement.Add(viewport);
                // The real setup controller owns a translucent overlay in another document at
                // runtime. Reuse the actual shared shell as this isolated fixture's opaque base.
                backdrop = new MobilePageShell("first-run-fixture-backdrop");
                viewport.Add(backdrop.Root);
                viewport.Add(setup);
                var safeBinding = controllerType.GetField("safeArea", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(controller) as UiToolkitSafeAreaBinding;
                Assert.That(safeBinding, Is.Not.Null);
                safeBinding.Suspend();
                setup.AddToClassList("mobile-layout--compact");
                setup.style.paddingLeft = 48f;
                setup.style.paddingTop = 60f;
                setup.style.paddingRight = 12f;
                setup.style.paddingBottom = 84f;
                capture = CoreNeutralSceneCapture.BeginIfRequested(document, viewport,
                    "FirstRun_RuntimeFixtureCapturesEmptyAndRecoverableCatalogWithLongTextAfterResize; real FirstRunContentSetupController/UXML; " +
                    "actual MobilePageShell as isolated opaque fixture backdrop; in-memory empty installed catalog and recoverable remote stub; " +
                    "no network or persistent writes; real managed-storage free-space read");
                ScrollView scroll = setup.Q<ScrollView>("setup-scroll");
                VisualElement retry = setup.Q<VisualElement>("setup-retry");
                Label body = setup.Q<Label>("setup-body");
                for (int frame = 0; frame < 4; frame++)
                    yield return null;
                float normalBodySize = body.resolvedStyle.fontSize;

                foreach (string localeId in new[] { "en", "zh", "ja" })
                {
                    ApplicationServices.Languages.SelectUiLanguage(localeId);
                    var locale = LocalizationSettings.AvailableLocales.Locales
                        .First(value => value.Identifier.Code == localeId);
                    LocalizationSettings.SelectedLocale = locale;
                    body.style.fontSize = StyleKeyword.Null;
                    remote.Fail = true;
                    scroll.ScrollTo(retry);
                    yield return null;
                    yield return null;
                    int previousCalls = remote.LoadCalls;
                    SendTap(retry);
                    yield return null;
                    Assert.That(remote.LoadCalls, Is.EqualTo(previousCalls + 1), "Retry must use the real action callback.");
                    string unavailable = setup.Q<Label>("setup-catalog").text;
                    Assert.That(unavailable, Is.Not.Empty);
                    Assert.That(unavailable, Does.Not.Contain("/private/"));
                    Assert.That(unavailable, Does.Not.Contain("sentinel-offline"));
                    Assert.That(retry.Q<Label>().ClassListContains("is-disabled"), Is.False);
                    if (capture != null)
                        yield return capture.Capture("first-run-unavailable",
                            "Catalog unavailable; real retry/manage/offline actions; in-memory failed provider; locale=" + localeId, scroll);

                    remote.Fail = false;
                    previousCalls = remote.LoadCalls;
                    SendTap(retry);
                    yield return null;
                    Assert.That(remote.LoadCalls, Is.EqualTo(previousCalls + 1));
                    Assert.That(setup.Q<Label>("setup-catalog").text, Is.Not.EqualTo(unavailable));
                    Assert.That(setup.Q<VisualElement>("setup-recommendation").resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
                    Assert.That(ApplicationServices.Languages.RequestedContentLanguageId, Is.EqualTo("en"));
                    scroll.scrollOffset = Vector2.zero;
                    if (capture != null)
                        yield return capture.Capture("first-run-empty",
                            "Retry recovered to an empty downloadable catalog; installed catalog empty; locale=" + localeId, scroll);

                    // A controlled 140% text fixture exercises real localized copy. It does not
                    // claim operating-system text scaling or screen-reader/device acceptance.
                    body.style.fontSize = normalBodySize * 1.4f;
                    foreach (Vector2 size in new[] { new Vector2(720f, 1600f), new Vector2(1600f, 720f), new Vector2(720f, 1600f) })
                    {
                        viewport.style.width = size.x;
                        viewport.style.height = size.y;
                        for (int frame = 0; frame < 4; frame++)
                            yield return null;
                        AssertContained(InsetRect(setup.worldBound, setup.resolvedStyle), scroll.worldBound,
                            "first-run scroll " + localeId + " " + size);
                        Assert.That(body.resolvedStyle.whiteSpace, Is.EqualTo(WhiteSpace.Normal));
                        Assert.That(body.worldBound.width, Is.LessThanOrEqualTo(scroll.contentViewport.worldBound.width + 1f));
                        Assert.That(body.worldBound.height, Is.GreaterThan(normalBodySize * 1.4f));
                        foreach (string actionName in new[] { "setup-manage", "setup-retry", "setup-later" })
                        {
                            VisualElement action = setup.Q<VisualElement>(actionName);
                            scroll.ScrollTo(action);
                            yield return null;
                            yield return null;
                            Assert.That(action.worldBound.width, Is.GreaterThanOrEqualTo(48f), actionName);
                            Assert.That(action.worldBound.height, Is.GreaterThanOrEqualTo(48f), actionName);
                            Assert.That(scroll.contentViewport.worldBound.Contains(action.worldBound.center), Is.True,
                                localeId + " " + size + " " + actionName);
                            VisualElement picked = action.panel.Pick(action.worldBound.center);
                            Assert.That(picked == action || action.Contains(picked), Is.True,
                                "Long localized text must leave the real action pickable: " + actionName);
                        }
                    }
                }
            }
            finally
            {
                capture?.Dispose();
                backdrop?.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
                dismissed.SetValue(null, originalDismissed);
                LocalizationSettings.SelectedLocale = originalLocale;
                ApplicationServices.Reset();
            }
        }

        private static Rect InsetRect(Rect outer, IResolvedStyle style)
        {
            return new Rect(
                outer.xMin + style.paddingLeft,
                outer.yMin + style.paddingTop,
                outer.width - style.paddingLeft - style.paddingRight,
                outer.height - style.paddingTop - style.paddingBottom);
        }

        private static void AssertColor(Color actual, string hex)
        {
            Assert.That(ColorUtility.TryParseHtmlString(hex, out Color expected), Is.True);
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.005f), hex + " red");
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.005f), hex + " green");
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.005f), hex + " blue");
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.005f), hex + " alpha");
        }

        private static void AssertContained(Rect outer, Rect inner, string label)
        {
            Assert.That(inner.xMin, Is.GreaterThanOrEqualTo(outer.xMin - 1f), label + " left");
            Assert.That(inner.yMin, Is.GreaterThanOrEqualTo(outer.yMin - 1f), label + " top");
            Assert.That(inner.xMax, Is.LessThanOrEqualTo(outer.xMax + 1f), label + " right");
            Assert.That(inner.yMax, Is.LessThanOrEqualTo(outer.yMax + 1f), label + " bottom");
        }

        [UnityTest]
        public IEnumerator NotNow_CancelsRefreshAndDestroysStableOverlayHost()
        {
            var remote = new BlockingRemoteCatalogProvider();
            ApplicationServices.Configure(
                new CatalogSession(new EmptyCatalogProvider()),
                new LanguageSelectionService(new MemoryLanguageStore(), new[] { "en", "zh", "ja" }),
                contentPackageCatalogs: remote);
            GameObject host = new GameObject("Dismissible First Run Setup Host");
            Type controllerType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(assembly =>
                {
                    try { return assembly.GetTypes(); }
                    catch { return Array.Empty<Type>(); }
                })
                .First(type => type.FullName == "Gacha.Presentation.FirstRunContentSetupController");
            try
            {
                host.AddComponent(controllerType);
                VisualElement setup = null;
                float deadline = Time.realtimeSinceStartup + 5f;
                while (setup == null && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                    setup = host.GetComponent<UIDocument>()?.rootVisualElement
                        .Q<VisualElement>("first-run-content");
                }

                Assert.That(setup, Is.Not.Null);
                Assert.That(setup.Query<Button>().ToList(), Is.Empty);
                Assert.That(remote.LoadCalls, Is.EqualTo(1));
                VisualElement later = setup.Q<VisualElement>("setup-later");
                setup.Q<ScrollView>("setup-scroll").ScrollTo(later);
                yield return null;
                SendTap(later);
                Assert.That(setup.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
                yield return null;
                Assert.That(remote.Token.IsCancellationRequested, Is.True);
                Assert.That(host == null, Is.True);
                ApplicationServices.Languages.SelectUiLanguage("ja");
                yield return null;
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                FieldInfo dismissed = controllerType.GetField(
                    "dismissedForSession",
                    BindingFlags.Static | BindingFlags.NonPublic);
                dismissed?.SetValue(null, false);
                if (host != null)
                    UnityEngine.Object.DestroyImmediate(host);
                ApplicationServices.Reset();
            }
        }

        [UnityTest]
        public IEnumerator CleanInstall_PokedexKeepsManageContentActionReachable()
        {
            ApplicationServices.Configure(
                new CatalogSession(new EmptyCatalogProvider()),
                new LanguageSelectionService(new MemoryLanguageStore(), new[] { "en", "zh", "ja" }));
            GameObject host = new GameObject("Empty Pokédex Host");
            bool manageInvoked = false;
            CoreNeutralSceneCapture capture = null;
            var originalLocale = LocalizationSettings.SelectedLocale;
            bool originalReduceMotion = UIFeedbackService.ReduceMotion;
            bool originalHaptics = UIFeedbackService.HapticsEnabled;
            bool originalSound = UIFeedbackService.SoundEnabled;
            float originalAnimationSpeed = UIFeedbackService.AnimationSpeed;
            UIFeedbackService.Configure(true, false, 1f, false);
            try
            {
                var document = host.AddComponent<UIDocument>();
                document.panelSettings = Resources.Load<PanelSettings>("UI/Collection Panel Settings");
                var controller = host.AddComponent<PokemonPokedexController>();
                controller.Attach(document, manageDownloads: () => manageInvoked = true);
                capture = PreparePokedexCapture(document, controller, "EmptyCatalogProvider; memory-only languages/progress; no network");
                yield return null;

                Assert.That(controller.Open(), Is.False);
                yield return null;

                VisualElement errorPanel = document.rootVisualElement.Q<VisualElement>("pokedex-error-panel");
                VisualElement manage = document.rootVisualElement.Q<VisualElement>("pokedex-error-manage");
                Assert.That(controller.MissingContent, Is.True);
                Assert.That(errorPanel, Is.Not.Null);
                Assert.That(errorPanel.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(manage, Is.Not.Null);
                Assert.That(manage.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(manage.Q<Label>().text, Is.Not.Empty);
                Assert.That(manage.worldBound.height, Is.GreaterThanOrEqualTo(48f));
                AssertPokedexOpaqueAndAttached(document);
                if (capture != null)
                    yield return capture.Capture("pokedex-empty", "No installed catalog; visible manage-content recovery action", errorPanel);
                SendKeyboardActivate(manage);
                Assert.That(manageInvoked, Is.True);

                Assert.That(controller.RetryOpen(), Is.False);
                Assert.That(errorPanel.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
                controller.Close();
                Assert.That(errorPanel.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
                Assert.That(document.rootVisualElement.Q<VisualElement>("pokedex-overlay")
                    .resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
            }
            finally
            {
                capture?.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
                LocalizationSettings.SelectedLocale = originalLocale;
                UIFeedbackService.Configure(originalReduceMotion, originalHaptics, originalAnimationSpeed, originalSound);
                ApplicationServices.Reset();
            }
        }

        [UnityTest]
        public IEnumerator Pokedex_PreservesStructuredCatalogFailureWithoutLeakingDetail()
        {
            ApplicationServices.Configure(
                new CatalogSession(new CorruptCatalogProvider()),
                new LanguageSelectionService(new MemoryLanguageStore(), new[] { "en", "zh", "ja" }));
            GameObject host = new GameObject("Corrupt Pokédex Host");
            CoreNeutralSceneCapture capture = null;
            var originalLocale = LocalizationSettings.SelectedLocale;
            bool originalReduceMotion = UIFeedbackService.ReduceMotion;
            bool originalHaptics = UIFeedbackService.HapticsEnabled;
            bool originalSound = UIFeedbackService.SoundEnabled;
            float originalAnimationSpeed = UIFeedbackService.AnimationSpeed;
            UIFeedbackService.Configure(true, false, 1f, false);
            try
            {
                var document = host.AddComponent<UIDocument>();
                document.panelSettings = Resources.Load<PanelSettings>("UI/Collection Panel Settings");
                var controller = host.AddComponent<PokemonPokedexController>();
                controller.Attach(document);
                capture = PreparePokedexCapture(document, controller, "CorruptCatalogProvider; synthetic error sentinel; memory-only languages; no network");

                Assert.That(controller.Open(), Is.False);
                yield return null;

                Assert.That(controller.InitializationErrorCode,
                    Is.EqualTo(PlayerUiErrorCode.CatalogCorrupt));
                string copy = string.Join("\n", document.rootVisualElement.Query<Label>()
                    .ToList().Select(label => label.text));
                Assert.That(copy, Does.Not.Contain("/storage/"));
                Assert.That(copy, Does.Not.Contain("sentinel-stack"));
                VisualElement errorPanel = document.rootVisualElement.Q<VisualElement>("pokedex-error-panel");
                Assert.That(errorPanel.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
                AssertPokedexOpaqueAndAttached(document);
                if (capture != null)
                    yield return capture.Capture("pokedex-recoverable-error", "Structured CatalogCorrupt; localized recoverable error; internal sentinel hidden", errorPanel);
            }
            finally
            {
                capture?.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
                LocalizationSettings.SelectedLocale = originalLocale;
                UIFeedbackService.Configure(originalReduceMotion, originalHaptics, originalAnimationSpeed, originalSound);
                ApplicationServices.Reset();
            }
        }

        [UnityTest]
        public IEnumerator ContentReturnNavigation_ConsumesRememberedSceneOnce()
        {
            UnityEngine.SceneManagement.Scene original =
                UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            UnityEngine.SceneManagement.Scene source =
                UnityEngine.SceneManagement.SceneManager.CreateScene("ContentReturnNavigationTestScene");
            Assert.That(UnityEngine.SceneManagement.SceneManager.SetActiveScene(source), Is.True);

            ContentReturnNavigation.RememberCurrentScene();

            Assert.That(ContentReturnNavigation.ConsumeOrDefault("fallback"), Is.EqualTo(source.name));
            Assert.That(ContentReturnNavigation.ConsumeOrDefault("fallback"), Is.EqualTo("fallback"));
            Assert.That(UnityEngine.SceneManagement.SceneManager.SetActiveScene(original), Is.True);
            yield return UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(source);
        }

        private static void AssertPokedexOpaqueAndAttached(UIDocument document)
        {
            VisualElement overlay = document.rootVisualElement.Q<VisualElement>("pokedex-overlay");
            Assert.That(overlay.panel, Is.SameAs(document.rootVisualElement.panel));
            Assert.That(overlay.panel, Is.Not.Null);
            Assert.That(overlay.resolvedStyle.opacity, Is.EqualTo(1f).Within(0.001f),
                "Capture fixture must finish the real Open transition before reading the framebuffer.");
        }

        private static CoreNeutralSceneCapture PreparePokedexCapture(
            UIDocument document, PokemonPokedexController controller, string fixture)
        {
            LocalizationSettings.SelectedLocale = LocalizationSettings.AvailableLocales.Locales
                .First(locale => locale.Identifier.Code == "en");
            VisualElement root = document.rootVisualElement;
            root.style.width = 720f;
            root.style.height = 1600f;
            object pageShell = typeof(PokemonPokedexController).GetField(
                "mobilePageShell", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
            var safeBinding = typeof(MobilePageShell).GetField(
                "safeAreaBinding", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(pageShell) as UiToolkitSafeAreaBinding;
            Assert.That(safeBinding, Is.Not.Null);
            safeBinding.Suspend();
            VisualElement safeArea = root.Q<VisualElement>("safe-area");
            safeArea.AddToClassList("mobile-layout--compact");
            safeArea.style.paddingLeft = 48f;
            safeArea.style.paddingTop = 60f;
            safeArea.style.paddingRight = 12f;
            safeArea.style.paddingBottom = 84f;
            return CoreNeutralSceneCapture.BeginIfRequested(document, root,
                "Real PokemonPokedexController/UXML on a temporary host; " + fixture);
        }

        private static ContentPackageCatalog RecommendationCatalog()
        {
            string hash = new string('a', 64);
            var entry = new ContentPackageCatalogEntry(
                new ContentPackageDescriptor(
                    "en.starter", "en/starter", 1, "1.0.0", 1024, 2048, hash),
                new Uri("https://content.example.test/en/starter.zip"),
                new ContentPackageMetadata(
                    "card-set",
                    new Dictionary<string, string> { ["en"] = "Starter" },
                    contentLanguageId: "en",
                    generationOrder: 1,
                    sortOrdinal: 1,
                    tags: new[] { "starter" }));
            return new ContentPackageCatalog(
                ContentPackageCatalog.SupportedSchemaVersion,
                1,
                new[] { entry });
        }

        private static Type FindFirstRunControllerType() =>
            AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(assembly =>
                {
                    try { return assembly.GetTypes(); }
                    catch { return Array.Empty<Type>(); }
                })
                .First(type => type.FullName == "Gacha.Presentation.FirstRunContentSetupController");

        private static void SendTap(VisualElement control)
        {
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

        private static void SendKeyboardActivate(VisualElement control)
        {
            control.Focus();
            using (KeyDownEvent down = KeyDownEvent.GetPooled(new Event
                   {
                       type = EventType.KeyDown,
                       keyCode = KeyCode.Return
                   }))
                control.SendEvent(down);
            using (KeyUpEvent up = KeyUpEvent.GetPooled(new Event
                   {
                       type = EventType.KeyUp,
                       keyCode = KeyCode.Return
                   }))
                control.SendEvent(up);
        }
    }
}
