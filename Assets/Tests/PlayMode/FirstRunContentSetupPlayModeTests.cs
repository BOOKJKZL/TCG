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
                Assert.That(setup.Q<Button>("setup-manage"), Is.Not.Null);
                Assert.That(setup.Q<Button>("setup-retry"), Is.Not.Null);
                Assert.That(setup.Q<Label>("setup-content-language-detail").text,
                    Does.Contain("independent"));
                Assert.That(setup.Q<Button>("setup-content-language-en")
                    .ClassListContains("is-selected"), Is.True);
                Assert.That(remote.LoadCalls, Is.EqualTo(1));

                ApplicationServices.Languages.SelectUiLanguage("zh");
                yield return null;
                Assert.That(ApplicationServices.Languages.UiLanguageId, Is.EqualTo("zh"));
                Assert.That(setup.Q<Label>("setup-title").text, Does.Contain("语言"));

                controllerType.GetMethod("SelectContentLanguage", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(controller, new object[] { "zh-cn" });
                yield return null;
                Assert.That(ApplicationServices.Languages.UiLanguageId, Is.EqualTo("zh"));
                Assert.That(ApplicationServices.Languages.RequestedContentLanguageId, Is.EqualTo("zh-cn"));
                Assert.That(ApplicationServices.Languages.ContentLanguage.ResolvedLanguageId, Is.EqualTo("zh-cn"));
                Assert.That(setup.Q<Button>("setup-content-language-zh")
                    .ClassListContains("is-selected"), Is.True);

                controllerType.GetMethod("SelectContentLanguage", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(controller, new object[] { "ja" });
                yield return null;
                Assert.That(ApplicationServices.Languages.RequestedContentLanguageId, Is.EqualTo("ja"));
                Assert.That(setup.Q<Button>("setup-content-language-ja")
                    .ClassListContains("is-selected"), Is.True);
            }
            finally
            {
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
            try
            {
                var document = host.AddComponent<UIDocument>();
                document.panelSettings = Resources.Load<PanelSettings>("UI/Collection Panel Settings");
                var controller = host.AddComponent<PokemonPokedexController>();
                controller.Attach(document, manageDownloads: () => { });

                Assert.That(controller.Open(), Is.False);
                yield return null;

                Button manage = document.rootVisualElement.Q<Button>("pokedex-empty-manage-button");
                Assert.That(controller.MissingContent, Is.True);
                Assert.That(manage, Is.Not.Null);
                Assert.That(manage.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(manage.enabledInHierarchy, Is.True);
                Assert.That(manage.text, Is.Not.Empty);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                ApplicationServices.Reset();
            }
        }

        [Test]
        public void ContentReturnNavigation_ConsumesRememberedSceneOnce()
        {
            string currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            Assert.That(currentScene, Is.Not.Empty);

            ContentReturnNavigation.RememberCurrentScene();

            Assert.That(ContentReturnNavigation.ConsumeOrDefault("fallback"), Is.EqualTo(currentScene));
            Assert.That(ContentReturnNavigation.ConsumeOrDefault("fallback"), Is.EqualTo("fallback"));
        }
    }
}
