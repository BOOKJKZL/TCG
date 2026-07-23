using System;
using System.IO;
using System.Linq;
using Gacha.Application;
using Gacha.Domain;
using Gacha.Infrastructure.Content;
using Gacha.Infrastructure.Rules;
using NUnit.Framework;

public class PokemonHistoricalRuleProviderTests
{
    [Test]
    public void BaseSetUnlimited_BuildsSourcedElevenCardProfile()
    {
        UniversalCatalog catalog = LoadInstalledCatalog();
        ProductDefinition product = catalog.Products.Values.Single(value =>
            value.SetId == PokemonHistoricalRuleProvider.BaseSetId);

        ProductRuleProfile profile = new PokemonHistoricalRuleProvider()
            .GetProfile(catalog, product.Id, "en");

        Assert.That(profile, Is.Not.Null);
        Assert.That(profile.Trust, Is.EqualTo(ProductRuleTrust.HistoricallyVerified));
        Assert.That(profile.SourceReferences, Has.Count.EqualTo(2));
        Assert.That(profile.Rules.Slots.Sum(slot => slot.DrawCount), Is.EqualTo(11));
        Assert.That(profile.Rules.Slots.Select(slot => slot.DrawCount),
            Is.EquivalentTo(new[] { 5, 2, 3, 1 }));
        Assert.That(profile.Rules.Pools.Values.Select(pool => pool.Entries.Count),
            Is.EquivalentTo(new[] { 32, 6, 32, 31 }));

        WeightedPool rarePool = profile.Rules.Pools.Values.Single(pool =>
            pool.Id.EndsWith(":pool:rare", StringComparison.Ordinal));
        WeightedPoolEntry[] holoEntries = rarePool.Entries.Where(entry =>
            HasTrait(catalog, entry.PrintingId, "holo")).ToArray();
        WeightedPoolEntry[] normalEntries = rarePool.Entries.Where(entry =>
            HasTrait(catalog, entry.PrintingId, "normal")).ToArray();
        Assert.That(holoEntries, Has.Length.EqualTo(15));
        Assert.That(normalEntries, Has.Length.EqualTo(16));
        Assert.That(holoEntries.Any(entry =>
            catalog.Printings[entry.PrintingId].Identity.CardNumber == "8"), Is.False);
        double holoWeight = holoEntries.Sum(entry => entry.Weight);
        double totalWeight = rarePool.Entries.Sum(entry => entry.Weight);
        Assert.That(holoWeight / totalWeight, Is.EqualTo(1d / 3d).Within(0.000001d));
        Assert.That(profile.Rules.Pools.Values.SelectMany(pool => pool.Entries).All(entry =>
            !HasTrait(catalog, entry.PrintingId, "first-edition")), Is.True);
    }

    [Test]
    public void Provider_FallsBackForOtherSetsAndLanguages()
    {
        UniversalCatalog catalog = LoadInstalledCatalog();
        var historical = new PokemonHistoricalRuleProvider();
        ProductDefinition baseProduct = catalog.Products.Values.Single(value =>
            value.SetId == PokemonHistoricalRuleProvider.BaseSetId);
        ProductDefinition modernProduct = catalog.Products.Values.First(value =>
            value.SetId != PokemonHistoricalRuleProvider.BaseSetId);

        Assert.That(historical.GetProfile(catalog, baseProduct.Id, "zh-CN"), Is.Null);
        Assert.That(historical.GetProfile(catalog, modernProduct.Id, "en"), Is.Null);

        var fallback = new FallbackProductRuleProvider(
            historical,
            new UniformSimulationRuleProvider(5));
        ProductRuleProfile simulated = fallback.GetProfile(catalog, modernProduct.Id, "en");
        Assert.That(simulated.Trust, Is.EqualTo(ProductRuleTrust.Simulated));
        Assert.That(simulated.Rules.Slots.Sum(slot => slot.DrawCount), Is.EqualTo(5));
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
}
