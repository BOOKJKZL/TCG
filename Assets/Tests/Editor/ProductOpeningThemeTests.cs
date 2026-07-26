using System;
using System.Collections.Generic;
using System.Linq;
using Gacha.Domain;
using Gacha.Pokemon.Presentation;
using Gacha.Presentation;
using NUnit.Framework;

public class ProductOpeningThemeTests
{
    [TearDown]
    public void TearDown()
    {
        ProductOpeningThemeService.Reset();
    }

    [Test]
    public void PokemonProvider_AssignsDistinctThemesToAllInstalledEras()
    {
        var provider = new PokemonProductOpeningThemeProvider();
        string[] setIds =
        {
            PokemonProductOpeningThemeProvider.BaseSetId,
            PokemonProductOpeningThemeProvider.NeoGenesisSetId,
            PokemonProductOpeningThemeProvider.ExRubySapphireSetId,
            PokemonProductOpeningThemeProvider.SwordShieldSetId,
            PokemonProductOpeningThemeProvider.ScarletVioletSetId
        };

        ProductOpeningTheme[] themes = setIds
            .Select(setId => provider.Resolve(Product(setId)))
            .ToArray();

        Assert.That(themes, Has.All.Not.Null);
        Assert.That(themes.Select(theme => theme.Id).Distinct().Count(), Is.EqualTo(5));
        Assert.That(themes.Select(theme => theme.StyleClass).Distinct().Count(), Is.EqualTo(5));
        Assert.That(themes.Select(theme => theme.PackOpenAudioKey).Distinct().Count(), Is.EqualTo(5));
        Assert.That(themes.Select(theme => theme.RareRevealAudioKey).Distinct().Count(), Is.EqualTo(5));
        Assert.That(themes.All(theme => theme.PackTearDurationSeconds > 0f), Is.True);
        Assert.That(themes.All(theme => theme.RevealDurationSeconds > 0f), Is.True);
    }

    [Test]
    public void PokemonTheme_HighlightsRareIdsWithoutRequiringImportedPresentationKey()
    {
        ProductOpeningTheme theme = new PokemonProductOpeningThemeProvider()
            .Resolve(Product(PokemonProductOpeningThemeProvider.ScarletVioletSetId));
        var rare = new RarityDefinition(
            "pokemon-tcg:rarity:special-illustration-rare",
            "pokemon-tcg",
            Names("Special Illustration Rare"),
            7);
        var common = new RarityDefinition(
            "pokemon-tcg:rarity:common",
            "pokemon-tcg",
            Names("Common"),
            0);

        Assert.That(theme.Highlights(rare), Is.True);
        Assert.That(theme.Highlights(common), Is.False);
    }

    [Test]
    public void ThemeService_UsesProviderAndFallsBackForUnknownGames()
    {
        ProductOpeningThemeService.Configure(new PokemonProductOpeningThemeProvider());

        ProductOpeningTheme pokemon = ProductOpeningThemeService.Resolve(
            Product(PokemonProductOpeningThemeProvider.ExRubySapphireSetId));
        ProductOpeningTheme unknown = ProductOpeningThemeService.Resolve(Product("other-game:set:first"));

        Assert.That(pokemon.Id, Is.EqualTo("pokemon-ex1-ruby"));
        Assert.That(unknown, Is.SameAs(ProductOpeningThemeService.DefaultTheme));
    }

    [Test]
    public void Theme_RejectsInvalidTimingAndScaleValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProductOpeningTheme(
            "bad",
            "bad-class",
            "bad.pack",
            "bad.rare",
            0f,
            1f,
            1f,
            0.2f,
            0.7f,
            1.1f));
    }

    private static ProductDefinition Product(string setId)
    {
        return new ProductDefinition(
            "product:" + setId,
            "pokemon-tcg",
            setId,
            Names("Booster"),
            "booster");
    }

    private static IReadOnlyDictionary<string, string> Names(string value)
    {
        return new Dictionary<string, string> { ["en"] = value };
    }
}
