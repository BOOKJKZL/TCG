using System;
using System.IO;
using System.Linq;
using Gacha.Application;
using Gacha.Domain;
using Gacha.Infrastructure.Content;
using Gacha.Infrastructure.Rules;
using NUnit.Framework;

public class PokemonModernRuleProviderTests
{
    [Test]
    public void SwordShieldBase_BuildsCorroboratedTenCardSimulation()
    {
        UniversalCatalog catalog = LoadInstalledCatalog();
        ProductDefinition product = catalog.Products.Values.Single(value =>
            value.SetId == PokemonModernRuleProvider.SwordShieldSetId);

        ProductRuleProfile profile = new PokemonModernRuleProvider()
            .GetProfile(catalog, product.Id, "en");

        Assert.That(profile, Is.Not.Null);
        Assert.That(profile.Id, Is.EqualTo(PokemonModernRuleProvider.SwordShieldProfileId));
        Assert.That(profile.Trust, Is.EqualTo(ProductRuleTrust.SourceInformedSimulation));
        Assert.That(profile.Confidence, Is.EqualTo(ProductRuleConfidence.Corroborated));
        Assert.That(profile.IsSimulation, Is.True);
        Assert.That(profile.LastCheckedOn, Is.EqualTo(new DateTime(2026, 7, 25)));
        Assert.That(profile.SourceReferences, Is.EquivalentTo(new[]
        {
            PokemonModernRuleProvider.OfficialBoosterSupportUrl,
            PokemonModernRuleProvider.EliteFourumPullRateUrl,
            PokemonModernRuleProvider.CardCodexPullRateUrl
        }));
        Assert.That(profile.GetDescription("en"), Does.Contain("Basic Energy"));
        Assert.That(profile.GetDescription("zh"), Does.Contain("不计基础能量"));
        Assert.That(profile.Rules.Slots.Sum(slot => slot.DrawCount), Is.EqualTo(10));
        Assert.That(profile.Rules.Slots.Select(slot => slot.DrawCount),
            Is.EquivalentTo(new[] { 5, 3, 1, 1 }));
        Assert.That(profile.Rules.Pools.Values.Select(pool => pool.Entries.Count),
            Is.EquivalentTo(new[] { 60, 56, 164, 100 }));

        WeightedPool rarePool = profile.Rules.Pools.Values.Single(pool =>
            pool.Id.EndsWith(":pool:rare", StringComparison.Ordinal));
        AssertProbability(catalog, rarePool, printing =>
            IsRarity(printing, "rare") && HasTrait(catalog, printing.Id, "normal"), 0.5952d);
        AssertProbability(catalog, rarePool, printing =>
            IsRegularHolo(catalog, printing), 0.1820d);
        AssertProbability(catalog, rarePool, printing =>
            IsRarity(printing, "holo-rare-v"), 0.1420d);
        AssertProbability(catalog, rarePool, printing =>
            IsRarity(printing, "holo-rare-vmax") && !IsCorrectedCinderace(catalog, printing), 0.0220d);
        AssertProbability(catalog, rarePool, printing =>
            IsRarity(printing, "ultra-rare"), 0.0374d);
        AssertProbability(catalog, rarePool, printing =>
            IsRarity(printing, "secret-rare"), 0.0214d);

        PrintingDefinition[] corrected = rarePool.Entries
            .Select(entry => catalog.Printings[entry.PrintingId])
            .Where(printing => IsCorrectedCinderace(catalog, printing))
            .ToArray();
        Assert.That(corrected.Select(printing => printing.Identity.CardNumber),
            Is.EquivalentTo(new[] { "34", "35" }));

        ProductDrawResult draw = new GachaEngine().Draw(
            catalog,
            profile.Rules,
            0,
            new SystemGachaRandomSource(2020));
        Assert.That(draw.Printings, Has.Count.EqualTo(10));
        Assert.That(draw.Printings.Count(entry => entry.SlotId.EndsWith(":slot:reverse")), Is.EqualTo(1));
        Assert.That(draw.Printings.Count(entry => entry.SlotId.EndsWith(":slot:rare")), Is.EqualTo(1));
    }

    [Test]
    public void PokemonRuleProvider_ComposesHistoricalModernAndFallbackBoundary()
    {
        UniversalCatalog catalog = LoadInstalledCatalog();
        var provider = new PokemonRuleProvider();
        ProductDefinition historical = catalog.Products.Values.Single(value =>
            value.SetId == PokemonHistoricalRuleProvider.ExRubySapphireSetId);
        ProductDefinition modern = catalog.Products.Values.Single(value =>
            value.SetId == PokemonModernRuleProvider.SwordShieldSetId);
        ProductDefinition unsupported = catalog.Products.Values.Single(value =>
            value.SetId.EndsWith(":sv01", StringComparison.Ordinal));

        Assert.That(provider.GetProfile(catalog, historical.Id, "en").Trust,
            Is.EqualTo(ProductRuleTrust.HistoricallyVerified));
        Assert.That(provider.GetProfile(catalog, modern.Id, "en").Trust,
            Is.EqualTo(ProductRuleTrust.SourceInformedSimulation));
        Assert.That(provider.GetProfile(catalog, unsupported.Id, "en"), Is.Null);
        Assert.That(provider.GetProfile(catalog, modern.Id, "zh-CN"), Is.Null);
    }

    private static void AssertProbability(
        UniversalCatalog catalog,
        WeightedPool pool,
        Func<PrintingDefinition, bool> predicate,
        double expected)
    {
        double total = pool.Entries.Sum(entry => entry.Weight);
        double matching = pool.Entries
            .Where(entry => predicate(catalog.Printings[entry.PrintingId]))
            .Sum(entry => entry.Weight);
        Assert.That(matching / total, Is.EqualTo(expected).Within(0.000001d));
    }

    private static UniversalCatalog LoadInstalledCatalog()
    {
        string contentRoot = Path.Combine(Directory.GetCurrentDirectory(), "LocalContent", "Imports");
        if (!Directory.Exists(contentRoot))
            Assert.Ignore("Private LocalContent fixture is not installed on this machine.");
        return new PrivateManifestCatalogAdapter()
            .Build(new PrivateContentManifestReader().LoadDirectory(contentRoot))
            .Catalog;
    }

    private static bool HasTrait(UniversalCatalog catalog, string printingId, string trait)
    {
        PrintingDefinition printing = catalog.Printings[printingId];
        return catalog.Variants[printing.Identity.VariantId].Traits.Any(value =>
            string.Equals(value, trait, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsRegularHolo(UniversalCatalog catalog, PrintingDefinition printing)
    {
        return IsRarity(printing, "holo-rare") || IsCorrectedCinderace(catalog, printing);
    }

    private static bool IsCorrectedCinderace(
        UniversalCatalog catalog,
        PrintingDefinition printing)
    {
        string number = printing.Identity.CardNumber;
        return (number == "34" || number == "35") &&
               catalog.Items[printing.ItemId].Names.Values.Any(name =>
                   string.Equals(name, "Cinderace", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsRarity(PrintingDefinition printing, string raritySlug)
    {
        return printing.RarityId.EndsWith(":" + raritySlug, StringComparison.OrdinalIgnoreCase);
    }
}
