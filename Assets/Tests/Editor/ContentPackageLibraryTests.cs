using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Gacha.Application;
using NUnit.Framework;

public sealed class ContentPackageLibraryTests
{
    [Test]
    public void Project_SortsByLanguageGenerationDateOrdinalNameAndStableId()
    {
        ContentPackageCatalog catalog = Catalog(
            Entry("ja.late", "ja", "遅い", 2, new DateTime(2002, 1, 1), 2),
            Entry("en.beta", "en", "Beta", 1, null, 1),
            Entry("en.alpha", "en", "Alpha", 1, new DateTime(2001, 1, 1), 2),
            Entry("en.first", "en", "First", 1, new DateTime(2000, 1, 1), 9),
            Entry("en.ordinal", "en", "Ordinal", 1, new DateTime(2001, 1, 1), 1));

        ContentPackageLibrarySnapshot result = ContentPackageLibrary.Project(catalog, null);

        Assert.That(result.Items.Select(value => value.Package.PackageId), Is.EqualTo(new[]
        {
            "en.first", "en.ordinal", "en.alpha", "en.beta", "ja.late"
        }));
    }

    [Test]
    public void Project_FiltersLanguageGenerationKindSearchAndInstallStateIndependently()
    {
        ContentPackageCatalog catalog = Catalog(
            Entry("en.base1", "en", "Base Set", 1, new DateTime(1999, 1, 9), 1),
            Entry("ja.sv1", "ja", "スカーレット", 9, new DateTime(2023, 1, 20), 1),
            Entry("pokemon.taxonomy", null, "Pokédex", null, null, null, "pokedex-taxonomy"));
        InstalledContentPackage installed = Installed(catalog.Find("en.base1").Package);

        ContentPackageLibrarySnapshot result = ContentPackageLibrary.Project(
            catalog,
            id => id == "en.base1" ? installed : null,
            new ContentPackageLibraryQuery(
                search: "base",
                contentLanguageId: "en",
                generationOrder: 1,
                kind: "card-set",
                installFilter: ContentPackageInstallFilter.Installed));

        Assert.That(result.CatalogCount, Is.EqualTo(3));
        Assert.That(result.FilteredCount, Is.EqualTo(1));
        Assert.That(result.Items[0].Package.PackageId, Is.EqualTo("en.base1"));
        Assert.That(result.Items[0].IsCurrent, Is.True);
    }

    [Test]
    public void Project_UsesUiLanguageOnlyForDisplayNameNotContentFiltering()
    {
        ContentPackageCatalogEntry entry = Entry(
            "ja.sv1", "ja", "Scarlet ex", 9, new DateTime(2023, 1, 20), 1);
        entry = new ContentPackageCatalogEntry(
            entry.Package,
            entry.ArchiveUri,
            new ContentPackageMetadata(
                "card-set",
                new Dictionary<string, string> { ["en"] = "Scarlet ex", ["zh-cn"] = "朱 ex" },
                "pokemon-tcg", "ja", "sv1", "SV1", generationOrder: 9, sortOrdinal: 1));

        ContentPackageLibraryItem item = ContentPackageLibrary.Project(
            Catalog(entry), null, new ContentPackageLibraryQuery(uiLanguageId: "zh-cn")).Items.Single();

        Assert.That(item.DisplayName, Is.EqualTo("朱 ex"));
        Assert.That(item.Metadata.ContentLanguageId, Is.EqualTo("ja"));
    }

    [Test]
    public void SummarizeSelection_AddsDependenciesOnceAndExcludesCurrentBytes()
    {
        ContentPackageCatalogEntry taxonomy = Entry(
            "pokemon.taxonomy", null, "Taxonomy", null, null, null, "pokedex-taxonomy",
            downloadBytes: 10, installedBytes: 20);
        ContentPackageCatalogEntry links = Entry(
            "pokemon.links.en", "en", "Links", null, null, null, "card-subject-links",
            new[] { taxonomy.Package.PackageId }, 30, 40);
        ContentPackageCatalogEntry artwork = Entry(
            "pokemon.artwork.1", null, "Artwork", 1, null, 1, "pokedex-artwork",
            new[] { taxonomy.Package.PackageId }, 50, 60);
        ContentPackageCatalog catalog = Catalog(taxonomy, links, artwork);
        InstalledContentPackage installedTaxonomy = Installed(taxonomy.Package);

        ContentPackageSelectionSummary result = ContentPackageLibrary.SummarizeSelection(
            catalog,
            new[] { links.Package.PackageId, artwork.Package.PackageId },
            id => id == taxonomy.Package.PackageId ? installedTaxonomy : null);

        Assert.That(result.PackageIds, Is.EqualTo(new[]
        {
            "pokemon.artwork.1", "pokemon.links.en", "pokemon.taxonomy"
        }));
        Assert.That(result.SelectedCount, Is.EqualTo(2));
        Assert.That(result.DependencyCount, Is.EqualTo(1));
        Assert.That(result.DownloadBytes, Is.EqualTo(80));
        Assert.That(result.InstalledBytes, Is.EqualTo(100));
    }

    [Test]
    public void Project_TwoThousandPackagesRemainsDeterministicAndBounded()
    {
        ContentPackageCatalogEntry[] entries = Enumerable.Range(0, 2000)
            .Select(index => Entry(
                "en.fixture-" + index.ToString("D4"),
                "en",
                "Fixture " + index.ToString("D4"),
                index % 9 + 1,
                new DateTime(2000 + index % 25, index % 12 + 1, index % 27 + 1),
                index + 1))
            .Reverse()
            .ToArray();
        var watch = Stopwatch.StartNew();

        ContentPackageLibrarySnapshot first = ContentPackageLibrary.Project(Catalog(entries), null);
        ContentPackageLibrarySnapshot second = ContentPackageLibrary.Project(Catalog(entries), null);
        watch.Stop();

        Assert.That(first.FilteredCount, Is.EqualTo(2000));
        Assert.That(second.Items.Select(value => value.Package.PackageId),
            Is.EqualTo(first.Items.Select(value => value.Package.PackageId)));
        Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
    }

    private static ContentPackageCatalog Catalog(params ContentPackageCatalogEntry[] entries) =>
        new ContentPackageCatalog(2, 1, entries);

    private static ContentPackageCatalogEntry Entry(
        string id,
        string language,
        string name,
        int? generation,
        DateTime? date,
        int? ordinal,
        string kind = "card-set",
        IEnumerable<string> dependencies = null,
        long downloadBytes = 100,
        long installedBytes = 200)
    {
        string hash = Hash(id);
        return new ContentPackageCatalogEntry(
            new ContentPackageDescriptor(id, id.Replace('.', '/'), 1, "1.0.0",
                downloadBytes, installedBytes, hash),
            new Uri("https://content.example.test/packages/" + id + "/" + hash + ".zip"),
            new ContentPackageMetadata(
                kind,
                new Dictionary<string, string> { [language ?? "en"] = name },
                "pokemon-tcg",
                language,
                id,
                id.ToUpperInvariant(),
                date,
                generation,
                ordinal,
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

    private static string Hash(string value)
    {
        return new string('a', 64);
    }
}
