using System;
using System.Collections.Generic;
using Gacha.Domain;
using Gacha.Presentation;
using NUnit.Framework;

public sealed class SystemBackNavigationTests
{
    [TestCase("en", "Reverse foil")]
    [TestCase("zh-cn", "反向闪卡")]
    [TestCase("ja", "ミラー仕様")]
    [TestCase("unavailable", "Reverse foil")]
    public void VariantNamesUseCatalogLanguageAndKeepIdentity(string language, string expected)
    {
        var variant = new VariantDefinition("reverse-foil-internal", "test", new Dictionary<string, string>
        {
            ["en"] = "Reverse foil", ["zh-cn"] = "反向闪卡", ["ja"] = "ミラー仕様"
        });
        var names = new Dictionary<string, string> { ["en"] = "Test" };
        var catalog = new UniversalCatalog(
            new[] { new LanguageDefinition("en", names) },
            new[] { new GameDefinition("test", names, new[] { "en" }) },
            Array.Empty<SetDefinition>(), Array.Empty<CollectibleItemDefinition>(),
            Array.Empty<RarityDefinition>(), new[] { variant },
            Array.Empty<PrintingDefinition>(), Array.Empty<ProductDefinition>());

        Assert.That(CardVariantDisplayName.Resolve(catalog, variant.Id, language), Is.EqualTo(expected));
        Assert.That(catalog.Variants["reverse-foil-internal"], Is.SameAs(variant));
        Assert.That(CardVariantDisplayName.Resolve(catalog, "removed-variant-internal", language),
            Is.EqualTo(CardUiText.Get("card.variant.unknown")));
        Assert.That(catalog.Variants.Count, Is.EqualTo(1));
    }

    [Test]
    public void MissingVariantAndCatalogUseReadableFallback()
    {
        Assert.That(CardVariantDisplayName.Resolve(null, null, "en"),
            Is.EqualTo(CardUiText.Get("card.variant.unknown")));
    }

    [Test]
    public void ExplicitVariantNameEqualToItsIdIsStillAValidDisplayName()
    {
        var names = new Dictionary<string, string> { ["en"] = "Holo" };
        var catalog = new UniversalCatalog(
            new[] { new LanguageDefinition("en", names) },
            new[] { new GameDefinition("test", names, new[] { "en" }) },
            Array.Empty<SetDefinition>(), Array.Empty<CollectibleItemDefinition>(),
            Array.Empty<RarityDefinition>(), new[] { new VariantDefinition("Holo", "test", names) },
            Array.Empty<PrintingDefinition>(), Array.Empty<ProductDefinition>());
        Assert.That(CardVariantDisplayName.Resolve(catalog, "Holo", "en"), Is.EqualTo("Holo"));
    }
}
