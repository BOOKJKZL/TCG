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
                Assert.That(englishCatalogStatus, Does.Contain("Catalog ready"));

                ApplicationServices.Languages.SelectUiLanguage("zh");
                yield return null;
                Assert.That(ApplicationServices.Languages.UiLanguageId, Is.EqualTo("zh"));
                Assert.That(setup.Q<Label>("setup-catalog").text,
                    Is.Not.EqualTo(englishCatalogStatus));
                Assert.That(setup.Q<Label>("setup-catalog").text,
                    Does.Not.Contain("Catalog ready"));
                Assert.That(setup.Q<Label>("setup-catalog").text,
                    Does.Contain("目录更新完成：0 个内容包"));
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
            try
            {
                var document = host.AddComponent<UIDocument>();
                document.panelSettings = Resources.Load<PanelSettings>("UI/Collection Panel Settings");
                var controller = host.AddComponent<PokemonPokedexController>();
                controller.Attach(document, manageDownloads: () => manageInvoked = true);

                Assert.That(controller.Open(), Is.False);
                yield return null;

                VisualElement errorPanel = document.rootVisualElement.Q<VisualElement>("pokedex-error-panel");
                Button manage = document.rootVisualElement.Q<Button>("pokedex-error-manage");
                Assert.That(controller.MissingContent, Is.True);
                Assert.That(errorPanel, Is.Not.Null);
                Assert.That(errorPanel.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(manage, Is.Not.Null);
                Assert.That(manage.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(manage.enabledInHierarchy, Is.True);
                Assert.That(manage.text, Is.Not.Empty);
                MethodInfo invokeClick = typeof(Clickable).GetMethod(
                    "Invoke",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(invokeClick, Is.Not.Null);
                invokeClick.Invoke(manage.clickable, new object[] { null });
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
                UnityEngine.Object.DestroyImmediate(host);
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
            try
            {
                var document = host.AddComponent<UIDocument>();
                document.panelSettings = Resources.Load<PanelSettings>("UI/Collection Panel Settings");
                var controller = host.AddComponent<PokemonPokedexController>();
                controller.Attach(document);

                Assert.That(controller.Open(), Is.False);
                yield return null;

                Assert.That(controller.InitializationErrorCode,
                    Is.EqualTo(PlayerUiErrorCode.CatalogCorrupt));
                string copy = string.Join("\n", document.rootVisualElement.Query<Label>()
                    .ToList().Select(label => label.text));
                Assert.That(copy, Does.Not.Contain("/storage/"));
                Assert.That(copy, Does.Not.Contain("sentinel-stack"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
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
    }
}
