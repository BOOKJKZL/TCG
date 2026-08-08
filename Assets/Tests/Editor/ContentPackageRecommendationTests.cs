using System;
using System.Collections.Generic;
using Gacha.Application;
using NUnit.Framework;

public sealed class ContentPackageRecommendationTests
{
    [Test]
    public void FindSmallestPlayable_UsesExactCardLanguageAndDependencyClosureBytes()
    {
        ContentPackageCatalogEntry shared = Entry("shared", null, "pokedex-taxonomy", 80, 80);
        ContentPackageCatalog catalog = Catalog(
            shared,
            Entry("en.large", "en", "card-set", 50, 100, new[] { "shared" }),
            Entry("en.small", "en", "card-set", 90, 90),
            Entry("ja.tiny", "ja", "card-set", 1, 1));

        ContentPackageRecommendation result =
            ContentPackageRecommendations.FindSmallestPlayable(catalog, "en");

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Entry.Package.PackageId, Is.EqualTo("en.small"));
        Assert.That(result.Selection.DownloadBytes, Is.EqualTo(90));
        Assert.That(result.Selection.PackageIds, Is.EqualTo(new[] { "en.small" }));
    }

    [Test]
    public void FindSmallestPlayable_ExcludesCurrentAndNonCardPackages()
    {
        ContentPackageCatalogEntry current = Entry("en.current", "en", "card-set", 1, 1);
        ContentPackageCatalog catalog = Catalog(
            current,
            Entry("en.art", "en", "pokedex-artwork", 1, 1),
            Entry("en.next", "en", "card-set", 20, 30));

        ContentPackageRecommendation result = ContentPackageRecommendations.FindSmallestPlayable(
            catalog,
            "en",
            id => id == current.Package.PackageId ? Installed(current.Package) : null);

        Assert.That(result.Entry.Package.PackageId, Is.EqualTo("en.next"));
    }

    [Test]
    public void FindSmallestPlayable_IsDeterministicAndDoesNotFallBackLanguage()
    {
        ContentPackageCatalog catalog = Catalog(
            Entry("en.z", "en", "card-set", 10, 20, generation: 2, ordinal: 1),
            Entry("en.a", "en", "card-set", 10, 20, generation: 1, ordinal: 2));

        Assert.That(
            ContentPackageRecommendations.FindSmallestPlayable(catalog, "en").Entry.Package.PackageId,
            Is.EqualTo("en.a"));
        Assert.That(ContentPackageRecommendations.FindSmallestPlayable(catalog, "ja"), Is.Null);
        Assert.That(ContentPackageRecommendations.FindSmallestPlayable(catalog, null), Is.Null);
    }

    private static ContentPackageCatalog Catalog(params ContentPackageCatalogEntry[] entries) =>
        new ContentPackageCatalog(2, 1, entries);

    private static ContentPackageCatalogEntry Entry(
        string id,
        string language,
        string kind,
        long downloadBytes,
        long installedBytes,
        IEnumerable<string> dependencies = null,
        int? generation = null,
        int? ordinal = null)
    {
        string hash = new string('a', 64);
        return new ContentPackageCatalogEntry(
            new ContentPackageDescriptor(
                id,
                id.Replace('.', '/'),
                1,
                "1.0.0",
                downloadBytes,
                installedBytes,
                hash),
            new Uri("https://content.example.test/" + id + ".zip"),
            new ContentPackageMetadata(
                kind,
                new Dictionary<string, string> { [language ?? "en"] = id },
                contentLanguageId: language,
                generationOrder: generation,
                sortOrdinal: ordinal,
                dependencies: dependencies));
    }

    private static InstalledContentPackage Installed(ContentPackageDescriptor package) =>
        new InstalledContentPackage(
            package.PackageId,
            package.InstallRelativePath,
            package.Revision,
            package.Version,
            package.InstalledBytes,
            package.Sha256);
}
