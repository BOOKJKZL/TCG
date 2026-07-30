using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        PrivateCatalogImportResult result = new PrivateManifestCatalogAdapter().Build(documents);

        int expectedCards = documents.Sum(document => document.Manifest.Cards.Count);
        Assert.That(result.SourceSetCount, Is.EqualTo(documents.Count));
        Assert.That(result.SourceSetCount, Is.GreaterThanOrEqualTo(5));
        Assert.That(result.SourceCardCount, Is.EqualTo(expectedCards));
        Assert.That(result.Catalog.Items.Count, Is.EqualTo(expectedCards));
        Assert.That(result.Catalog.Rarities.Count, Is.GreaterThanOrEqualTo(12));
        Assert.That(result.Catalog.Printings.Count, Is.GreaterThanOrEqualTo(expectedCards));
        Assert.That(result.Warnings, Is.Empty);
        Assert.That(result.Catalog.Printings.Values
            .Where(printing => !string.IsNullOrWhiteSpace(printing.ImageRelativePath))
            .All(printing => File.Exists(Path.Combine(
                contentRoot,
                printing.ImageRelativePath.Replace('/', Path.DirectorySeparatorChar)))), Is.True);
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
