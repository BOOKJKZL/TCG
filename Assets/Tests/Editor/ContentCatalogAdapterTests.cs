using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gacha.Application;
using Gacha.Domain;
using Gacha.Infrastructure.Content;
using NUnit.Framework;

public class ContentCatalogAdapterTests
{
    [Test]
    public void PrivateAdapter_ExpandsFinishAndEditionVariantsWithoutChangingDomainEnums()
    {
        PrivateContentManifestDto manifest = CreateManifest();
        manifest.Cards.Add(new ImportedCardDto
        {
            Id = "sample1-1",
            LocalId = "1",
            Name = "Sample Card",
            Category = "Character",
            Rarity = "Mythic Rainbow",
            ImageRelativePath = "images\\sample1-1.jpg",
            ImageSha256 = new string('a', 64),
            Variants = new ImportedCardVariantsDto
            {
                Normal = true,
                Holo = true,
                FirstEdition = true
            }
        });

        PrivateCatalogImportResult result = new PrivateManifestCatalogAdapter().Build(new[]
        {
            new PrivateContentManifestDocument("sample/manifest.json", manifest)
        }, "sample-game", "Sample Game");

        Assert.That(result.SourceCardCount, Is.EqualTo(1));
        Assert.That(result.Catalog.Rarities.Count, Is.EqualTo(1));
        Assert.That(result.PrintingCount, Is.EqualTo(4));
        Assert.That(result.Catalog.Variants.Values.Select(variant => variant.GetDisplayName("en")),
            Is.EquivalentTo(new[] { "Normal", "Normal First Edition", "Holo", "Holo First Edition" }));
        Assert.That(result.Catalog.Printings.Values.All(printing => printing.ImageRelativePath == "en/sample1/images/sample1-1.jpg"), Is.True);
    }

    [Test]
    public void InstalledContentFixture_BuildsCompleteCatalogAtCurrentScale()
    {
        string contentRoot = Path.Combine(Directory.GetCurrentDirectory(), "LocalContent", "Imports");
        if (!Directory.Exists(contentRoot))
            Assert.Ignore("Private LocalContent fixture is not installed on this machine.");

        IReadOnlyList<PrivateContentManifestDocument> documents = new PrivateContentManifestReader().LoadDirectory(contentRoot);
        PrintingLanguageGroupManifestDto languageGroups =
            new PrintingLanguageGroupManifestReader().LoadOptional(contentRoot);
        PrivateCatalogImportResult result = new PrivateManifestCatalogAdapter().Build(
            documents,
            languageGroupManifest: languageGroups);

        int expectedCards = documents.Sum(document => document.Manifest.Cards.Count);
        Assert.That(result.SourceSetCount, Is.EqualTo(result.Catalog.Sets.Count));
        Assert.That(result.SourceSetCount, Is.LessThanOrEqualTo(documents.Count),
            "The same logical set may have separate manifests for each card language.");
        Assert.That(result.SourceSetCount, Is.GreaterThanOrEqualTo(5));
        Assert.That(result.SourceCardCount, Is.EqualTo(expectedCards));
        Assert.That(result.Catalog.Items.Count, Is.EqualTo(expectedCards),
            "Source cards stay distinct; only explicit printing groups provide language switching.");
        Assert.That(result.Catalog.Languages.Count, Is.EqualTo(documents
            .Select(document => document.Manifest.Language)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count()));
        Assert.That(result.Catalog.Rarities.Count, Is.GreaterThanOrEqualTo(12));
        Assert.That(result.Catalog.Printings.Count, Is.GreaterThanOrEqualTo(result.SourceCardCount));
        Assert.That(result.Warnings, Is.Empty);
        Assert.That(result.Catalog.Printings.Values
            .Where(printing => !string.IsNullOrWhiteSpace(printing.ImageRelativePath))
            .All(printing => File.Exists(Path.Combine(
                contentRoot,
                printing.ImageRelativePath.Replace('/', Path.DirectorySeparatorChar)))), Is.True);
        if (languageGroups != null)
        {
            Assert.That(languageGroups.Groups, Has.Count.EqualTo(147));
            Assert.That(result.Catalog.PrintingLanguageGroups.Count,
                Is.GreaterThanOrEqualTo(languageGroups.Groups.Count),
                "Every accepted source group must expose at least one common runtime variant.");
            Assert.That(result.Catalog.PrintingLanguageGroups.All(group =>
                group.PrintingIds.Select(id => result.Catalog.Printings[id].Identity.LanguageId)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count() == group.PrintingIds.Count), Is.True);
        }
    }

    [Test]
    public void CatalogProvider_IgnoresNonCardModuleManifestsUnderSharedContentRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "gacha-shared-content-" + Guid.NewGuid().ToString("N"));
        try
        {
            string cardDirectory = Path.Combine(root, "en", "sample1");
            Directory.CreateDirectory(cardDirectory);
            File.WriteAllText(
                Path.Combine(cardDirectory, "manifest.json"),
                Newtonsoft.Json.JsonConvert.SerializeObject(CreateManifest()));

            string artworkDirectory = Path.Combine(root, "pokedex", "artwork", "generation-1");
            Directory.CreateDirectory(artworkDirectory);
            File.WriteAllText(Path.Combine(artworkDirectory, "manifest.json"), "{\"SchemaVersion\":1,\"Entries\":[]}");

            CatalogLoadResult result = new PrivateContentCatalogProvider(root).Load();

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.SourceSetCount, Is.EqualTo(1));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static PrivateContentManifestDto CreateManifest()
    {
        return new PrivateContentManifestDto
        {
            SchemaVersion = 2,
            Source = "test",
            Language = "en",
            Set = new ImportedSetDto
            {
                Id = "sample1",
                Name = "Sample Set",
                SetCode = "S1",
                SeriesId = "sample",
                EraId = "sample",
                GenerationId = "generation-1",
                GenerationOrder = 1,
                SetOrdinal = 1,
                ReleaseDate = "2026-01-01"
            }
        };
    }
}
