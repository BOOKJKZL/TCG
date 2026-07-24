using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Gacha.Application;
using Gacha.Domain;
using NUnit.Framework;

public class ApplicationServicesTests
{
    private sealed class CatalogProvider : ICatalogProvider
    {
        private readonly UniversalCatalog catalog;

        public CatalogProvider(UniversalCatalog catalog)
        {
            this.catalog = catalog;
        }

        public int Calls { get; private set; }
        public bool FailNext { get; set; }

        public CatalogLoadResult Load()
        {
            Calls++;
            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException("fixture unavailable");
            }

            return CatalogLoadResult.Success(catalog, 1, 2, 3);
        }
    }

    private sealed class PreferenceStore : ILanguagePreferenceStore
    {
        public PreferenceStore(string uiLanguageId, string contentLanguageId)
        {
            Saved = new LanguagePreferences(uiLanguageId, contentLanguageId);
        }

        public LanguagePreferences Saved { get; private set; }
        public int SaveCalls { get; private set; }

        public LanguagePreferences Load() => Saved;

        public void Save(LanguagePreferences preferences)
        {
            Saved = preferences;
            SaveCalls++;
        }
    }

    private sealed class EmptyPackageRegistry : IInstalledContentPackageRegistry
    {
        public InstalledContentPackage Find(string packageId) => null;
    }

    private sealed class FixedStorageProbe : IContentStorageProbe
    {
        public long GetAvailableBytes() => 1024;
    }

    private sealed class PackageInstaller : IContentPackageInstaller
    {
        public Task<ContentPackageInstallResult> InstallAsync(
            ContentInstallPlan plan,
            string archivePath,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ContentPackageInstallResult.Failure(
                ContentPackageInstallStatus.InvalidPlan,
                "fixture"));
        }
    }

    private sealed class PackageOperationFactory : IContentPackageInstallCoordinatorFactory, IDisposable
    {
        public bool Disposed { get; private set; }

        public ContentPackageInstallCoordinator Create(ContentPackageCatalog catalog, string packageId)
        {
            throw new NotSupportedException("fixture");
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }

    [TearDown]
    public void TearDown()
    {
        ApplicationServices.Reset();
    }

    [Test]
    public void CatalogSession_CachesSuccessfulLoad()
    {
        var provider = new CatalogProvider(CreateCatalog("en"));
        var session = new CatalogSession(provider);

        CatalogLoadResult first = session.EnsureLoaded();
        CatalogLoadResult second = session.EnsureLoaded();

        Assert.That(first.Succeeded, Is.True);
        Assert.That(second, Is.SameAs(first));
        Assert.That(provider.Calls, Is.EqualTo(1));
    }

    [Test]
    public void CatalogSession_RetriesAfterFailureWithoutCachingBrokenState()
    {
        var provider = new CatalogProvider(CreateCatalog("en")) { FailNext = true };
        var session = new CatalogSession(provider);

        CatalogLoadResult failed = session.EnsureLoaded();
        CatalogLoadResult recovered = session.EnsureLoaded();

        Assert.That(failed.Succeeded, Is.False);
        Assert.That(failed.ErrorMessage, Does.Contain("fixture unavailable"));
        Assert.That(recovered.Succeeded, Is.True);
        Assert.That(provider.Calls, Is.EqualTo(2));
    }

    [Test]
    public void ApplicationServices_ExposesPackagePlannerAndResetClearsIt()
    {
        var catalog = new CatalogSession(new CatalogProvider(CreateCatalog("en")));
        var languages = new LanguageSelectionService(new PreferenceStore("en", "en"), new[] { "en" });
        var planner = new ContentPackagePlanner(new EmptyPackageRegistry(), new FixedStorageProbe(), 0);
        var installer = new PackageInstaller();
        var operations = new PackageOperationFactory();

        ApplicationServices.Configure(
            catalog,
            languages,
            contentPackages: planner,
            contentPackageInstaller: installer,
            contentPackageOperations: operations);

        Assert.That(ApplicationServices.ContentPackages, Is.SameAs(planner));
        Assert.That(ApplicationServices.ContentPackageInstaller, Is.SameAs(installer));
        Assert.That(ApplicationServices.ContentPackageOperations, Is.SameAs(operations));
        ApplicationServices.Reset();
        Assert.That(ApplicationServices.ContentPackages, Is.Null);
        Assert.That(ApplicationServices.ContentPackageInstaller, Is.Null);
        Assert.That(ApplicationServices.ContentPackageOperations, Is.Null);
        Assert.That(operations.Disposed, Is.True);
    }

    [Test]
    public void LanguageSelection_KeepsUiAndContentPreferencesIndependent()
    {
        var store = new PreferenceStore("zh", "ja");
        var service = new LanguageSelectionService(store, new[] { "en", "zh" });

        ContentLanguageSelection content = service.RefreshContentLanguage(CreateCatalog("en"));
        bool uiChanged = service.SelectUiLanguage("en");

        Assert.That(content.RequestedLanguageId, Is.EqualTo("ja"));
        Assert.That(content.ResolvedLanguageId, Is.EqualTo("en"));
        Assert.That(content.UsedFallback, Is.True);
        Assert.That(uiChanged, Is.True);
        Assert.That(service.UiLanguageId, Is.EqualTo("en"));
        Assert.That(service.RequestedContentLanguageId, Is.EqualTo("ja"));
        Assert.That(store.Saved.UiLanguageId, Is.EqualTo("en"));
        Assert.That(store.Saved.ContentLanguageId, Is.EqualTo("ja"));
    }

    [Test]
    public void LanguageSelection_UsesParentUiLocaleAndEnglishContentFallback()
    {
        var store = new PreferenceStore("zh-CN", "zh-CN");
        var service = new LanguageSelectionService(store, new[] { "en", "zh" });

        ContentLanguageSelection content = service.RefreshContentLanguage(CreateCatalog("en"));

        Assert.That(service.UiLanguageId, Is.EqualTo("zh"));
        Assert.That(content.ResolvedLanguageId, Is.EqualTo("en"));
        Assert.That(content.UsedFallback, Is.True);
    }

    [Test]
    public void LanguageSelection_UsesConfiguredRegionalAndDefinitionFallbacks()
    {
        var store = new PreferenceStore("zh", "zh-CN");
        var service = new LanguageSelectionService(store, new[] { "en", "zh" });
        UniversalCatalog regionalCatalog = CreateCatalog("zh-TW", "en");

        ContentLanguageSelection regional = service.RefreshContentLanguage(regionalCatalog);
        Assert.That(regional.ResolvedLanguageId, Is.EqualTo("zh-TW"));

        UniversalCatalog definitionCatalog = CreateCatalog(
            new LanguageDefinition("zh-CN", new Dictionary<string, string> { ["zh-CN"] = "简体中文" }, "zh-TW"),
            new LanguageDefinition("zh-TW", new Dictionary<string, string> { ["zh-TW"] = "繁體中文" }),
            new LanguageDefinition("en", new Dictionary<string, string> { ["en"] = "English" }));
        service.SelectContentLanguage("zh-CN", definitionCatalog);
        var displayDefinition = new GameDefinition(
            "sample",
            new Dictionary<string, string>
            {
                ["zh-TW"] = "測試遊戲",
                ["en"] = "Sample Game"
            },
            new[] { "zh-TW", "en" });

        Assert.That(service.GetDisplayName(displayDefinition), Is.EqualTo("測試遊戲"));
    }

    private static UniversalCatalog CreateCatalog(params string[] languageIds)
    {
        var languages = new List<LanguageDefinition>();
        foreach (string languageId in languageIds)
        {
            languages.Add(new LanguageDefinition(
                languageId,
                new Dictionary<string, string> { [languageId] = languageId }));
        }

        return CreateCatalog(languages.ToArray());
    }

    private static UniversalCatalog CreateCatalog(params LanguageDefinition[] languages)
    {
        return new UniversalCatalog(
            languages,
            Array.Empty<GameDefinition>(),
            Array.Empty<SetDefinition>(),
            Array.Empty<CollectibleItemDefinition>(),
            Array.Empty<RarityDefinition>(),
            Array.Empty<VariantDefinition>(),
            Array.Empty<PrintingDefinition>(),
            Array.Empty<ProductDefinition>());
    }
}
