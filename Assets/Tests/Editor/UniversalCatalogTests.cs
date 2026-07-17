using System;
using System.Collections.Generic;
using System.Linq;
using Gacha.Domain;
using NUnit.Framework;

public class UniversalCatalogTests
{
    private static readonly string[] ImportedRarities =
    {
        "Common",
        "Double rare",
        "Holo Rare",
        "Holo Rare V",
        "Holo Rare VMAX",
        "Hyper rare",
        "Illustration rare",
        "Rare",
        "Secret Rare",
        "Special illustration rare",
        "Ultra Rare",
        "Uncommon"
    };

    [Test]
    public void Catalog_RepresentsAllTwelveImportedRaritiesWithoutEnumChanges()
    {
        UniversalCatalog catalog = BuildCatalog();

        Assert.That(catalog.Rarities.Count, Is.EqualTo(12));
        Assert.That(catalog.Rarities.Keys, Is.EquivalentTo(ImportedRarities.Select(ToId)));
    }

    [Test]
    public void PrintingIdentity_DistinguishesLanguageAndVariant()
    {
        PrintingIdentity englishNormal = new PrintingIdentity("pokemon", "base1", "4", "en", "normal");
        PrintingIdentity chineseNormal = new PrintingIdentity("pokemon", "base1", "4", "zh-CN", "normal");
        PrintingIdentity englishHolo = new PrintingIdentity("pokemon", "base1", "4", "en", "holo");

        Assert.That(englishNormal, Is.Not.EqualTo(chineseNormal));
        Assert.That(englishNormal, Is.Not.EqualTo(englishHolo));
    }

    [Test]
    public void Catalog_RejectsPrintingThatCombinesDifferentGames()
    {
        LanguageDefinition language = new LanguageDefinition("en", Names("English"));
        GameDefinition gameA = new GameDefinition("game-a", Names("Game A"), new[] { "en" });
        GameDefinition gameB = new GameDefinition("game-b", Names("Game B"), new[] { "en" });
        SetDefinition set = new SetDefinition("set-a", "game-a", Names("Set A"));
        CollectibleItemDefinition item = new CollectibleItemDefinition("item-b", "game-b", Names("Item B"), "card");
        RarityDefinition rarity = new RarityDefinition("common", "game-a", Names("Common"), 0);
        VariantDefinition variant = new VariantDefinition("normal", "game-a", Names("Normal"));
        PrintingDefinition printing = new PrintingDefinition(
            "printing-1",
            item.Id,
            new PrintingIdentity("game-a", set.Id, "1", "en", variant.Id),
            rarity.Id,
            Names("Item B"));

        UniversalCatalogValidationException exception = Assert.Throws<UniversalCatalogValidationException>(() =>
            new UniversalCatalog(
                new[] { language },
                new[] { gameA, gameB },
                new[] { set },
                new[] { item },
                new[] { rarity },
                new[] { variant },
                new[] { printing },
                Array.Empty<ProductDefinition>()));

        Assert.That(exception.Errors.Any(error => error.Contains("different games")), Is.True);
    }

    [Test]
    public void DisplayName_UsesRequestedLanguageThenFallback()
    {
        Dictionary<string, string> names = new Dictionary<string, string>
        {
            ["en"] = "Base Set",
            ["zh-CN"] = "基础系列"
        };
        SetDefinition set = new SetDefinition("base1", "pokemon", names);

        Assert.That(set.GetDisplayName("zh-CN"), Is.EqualTo("基础系列"));
        Assert.That(set.GetDisplayName("ja", "en"), Is.EqualTo("Base Set"));
    }

    private static UniversalCatalog BuildCatalog()
    {
        LanguageDefinition language = new LanguageDefinition("en", Names("English"));
        GameDefinition game = new GameDefinition("pokemon", Names("Pokémon TCG"), new[] { language.Id });
        SetDefinition set = new SetDefinition("base1", game.Id, Names("Base Set"), "base", new DateTime(1999, 1, 9));
        CollectibleItemDefinition item = new CollectibleItemDefinition("alakazam", game.Id, Names("Alakazam"), "card");
        RarityDefinition[] rarities = ImportedRarities
            .Select((name, rank) => new RarityDefinition(ToId(name), game.Id, Names(name), rank))
            .ToArray();
        VariantDefinition variant = new VariantDefinition("holo", game.Id, Names("Holo"), new[] { "foil" });
        PrintingDefinition printing = new PrintingDefinition(
            "base1-1-en-holo",
            item.Id,
            new PrintingIdentity(game.Id, set.Id, "1", language.Id, variant.Id),
            ToId("Rare"),
            Names("Alakazam"),
            "images/base1-1.jpg");
        ProductDefinition product = new ProductDefinition(
            "base1-booster",
            game.Id,
            set.Id,
            Names("Base Set Booster"),
            "booster-pack",
            new[] { printing.Id });

        return new UniversalCatalog(
            new[] { language },
            new[] { game },
            new[] { set },
            new[] { item },
            rarities,
            new[] { variant },
            new[] { printing },
            new[] { product });
    }

    private static Dictionary<string, string> Names(string englishName)
    {
        return new Dictionary<string, string> { ["en"] = englishName };
    }

    private static string ToId(string value)
    {
        return value.ToLowerInvariant().Replace(' ', '-');
    }
}
