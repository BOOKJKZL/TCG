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

    [Test]
    public void PrintingLanguages_SingleLanguageDoesNotOfferASelector()
    {
        UniversalCatalog catalog = BuildCatalog();
        PrintingDefinition printing = catalog.Printings.Values.Single();

        PrintingLanguageGroup group = catalog.PrintingLanguages.GetGroup(printing.Id);

        Assert.That(group, Is.Not.Null);
        Assert.That(group.HasMultipleLanguages, Is.False);
        Assert.That(group.AvailableLanguageIds, Is.EqualTo(new[] { "en" }));
        Assert.That(catalog.PrintingLanguages.Select(printing.Id, "ja"), Is.SameAs(printing));
    }

    [Test]
    public void PrintingLanguages_SharedItemSwitchesOnlyToAvailableCardLanguages()
    {
        UniversalCatalog catalog = BuildMultilingualCatalog();
        PrintingDefinition english = catalog.Printings["base1-1-en-holo"];
        PrintingLanguageGroup group = catalog.PrintingLanguages.GetGroup(english.Id);

        Assert.That(group.HasMultipleLanguages, Is.True);
        Assert.That(group.AvailableLanguageIds, Is.EqualTo(new[] { "en", "ja", "zh-cn" }));
        Assert.That(catalog.PrintingLanguages.Select(english.Id, "ja").Id, Is.EqualTo("base1-1-ja-holo"));
        Assert.That(catalog.PrintingLanguages.Select(english.Id, "zh-CN").Id, Is.EqualTo("base1-1-zh-cn-holo"));
        Assert.That(catalog.PrintingLanguages.Select(english.Id, "fr"), Is.SameAs(english));
    }

    [Test]
    public void PrintingLanguages_ExplicitGroupCanLinkDifferentRegionalSets()
    {
        UniversalCatalog source = BuildMultilingualCatalog(true);
        PrintingDefinition english = source.Printings["base1-1-en-holo"];

        Assert.That(source.PrintingLanguages.Select(english.Id, "ja").Id, Is.EqualTo("jp-base-1-ja-holo"));
        Assert.That(source.PrintingLanguages.GetGroup(english.Id).MatchMethod,
            Is.EqualTo(PrintingLanguageMatchMethod.ManualOverride));
        Assert.That(source.PrintingLanguages.GetGroup(english.Id).ReviewStatus,
            Is.EqualTo(PrintingLanguageReviewStatus.Reviewed));
    }

    [Test]
    public void Catalog_RejectsLanguageGroupWithTwoPrintingsFromSameLanguage()
    {
        UniversalCatalogValidationException exception = Assert.Throws<UniversalCatalogValidationException>(() =>
            BuildMultilingualCatalog(false, true));

        Assert.That(exception.Errors.Any(value => value.Contains("more than one 'en' printing")), Is.True);
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

    private static UniversalCatalog BuildMultilingualCatalog(
        bool useRegionalSet = false,
        bool invalidSameLanguageGroup = false)
    {
        var languages = new[]
        {
            new LanguageDefinition("en", Names("English")),
            new LanguageDefinition("ja", new Dictionary<string, string> { ["ja"] = "Japanese" }),
            new LanguageDefinition("zh-cn", new Dictionary<string, string> { ["zh-cn"] = "Chinese" })
        };
        GameDefinition game = new GameDefinition("pokemon", Names("Pokemon TCG"), languages.Select(value => value.Id));
        SetDefinition englishSet = new SetDefinition("base1", game.Id, Names("Base Set"));
        SetDefinition japaneseSet = useRegionalSet
            ? new SetDefinition("jp-base", game.Id, new Dictionary<string, string> { ["ja"] = "Japanese Base" })
            : englishSet;
        CollectibleItemDefinition sharedItem = new CollectibleItemDefinition("alakazam", game.Id, Names("Alakazam"), "card");
        CollectibleItemDefinition japaneseItem = useRegionalSet
            ? new CollectibleItemDefinition("alakazam-jp", game.Id,
                new Dictionary<string, string> { ["ja"] = "Alakazam JP" }, "card")
            : sharedItem;
        RarityDefinition rarity = new RarityDefinition("rare", game.Id, Names("Rare"), 1);
        VariantDefinition variant = new VariantDefinition("holo", game.Id, Names("Holo"));
        var printings = new List<PrintingDefinition>
        {
            new PrintingDefinition("base1-1-en-holo", sharedItem.Id,
                new PrintingIdentity(game.Id, englishSet.Id, "1", "en", variant.Id), rarity.Id, Names("Alakazam")),
            new PrintingDefinition(useRegionalSet ? "jp-base-1-ja-holo" : "base1-1-ja-holo", japaneseItem.Id,
                new PrintingIdentity(game.Id, japaneseSet.Id, "1", "ja", variant.Id), rarity.Id,
                new Dictionary<string, string> { ["ja"] = "Alakazam JP" }),
            new PrintingDefinition("base1-1-zh-cn-holo", sharedItem.Id,
                new PrintingIdentity(game.Id, englishSet.Id, "1", "zh-cn", variant.Id), rarity.Id,
                new Dictionary<string, string> { ["zh-cn"] = "Alakazam CN" })
        };
        if (invalidSameLanguageGroup)
        {
            printings.Add(new PrintingDefinition("base1-2-en-holo", sharedItem.Id,
                new PrintingIdentity(game.Id, englishSet.Id, "2", "en", variant.Id), rarity.Id, Names("Alakazam 2")));
        }

        PrintingLanguageGroupDefinition[] groups = useRegionalSet || invalidSameLanguageGroup
            ? new[]
            {
                new PrintingLanguageGroupDefinition(
                    "alakazam-languages",
                    invalidSameLanguageGroup
                        ? new[] { "base1-1-en-holo", "base1-2-en-holo" }
                        : new[] { "base1-1-en-holo", "jp-base-1-ja-holo" },
                    PrintingLanguageMatchMethod.ManualOverride,
                    1d,
                    PrintingLanguageReviewStatus.Reviewed)
            }
            : Array.Empty<PrintingLanguageGroupDefinition>();

        return new UniversalCatalog(
            languages,
            new[] { game },
            useRegionalSet ? new[] { englishSet, japaneseSet } : new[] { englishSet },
            useRegionalSet ? new[] { sharedItem, japaneseItem } : new[] { sharedItem },
            new[] { rarity },
            new[] { variant },
            printings,
            Array.Empty<ProductDefinition>(),
            groups);
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
