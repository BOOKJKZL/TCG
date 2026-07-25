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
        Assert.That(profile.Confidence, Is.EqualTo(ProductRuleConfidence.Corroborated));
        Assert.That(profile.RegionId, Is.EqualTo(PokemonHistoricalRuleProvider.InternationalRegionId));
        Assert.That(profile.GetRegionName("zh"), Is.EqualTo("国际英文市场"));
        Assert.That(profile.LastCheckedOn, Is.EqualTo(new DateTime(2026, 7, 23)));
        Assert.That(profile.Evidence.Select(item => item.Title), Is.EquivalentTo(new[]
        {
            "SJSU Base Set empirical study",
            "PokéBeach Base Set theme deck reference"
        }));
        Assert.That(profile.SourceReferences, Has.Count.EqualTo(2));
        Assert.That(profile.GetDescription("zh"), Does.Contain("无限版"));
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
    public void NeoGenesisFirstEdition_BuildsSourcedElevenCardProfile()
    {
        UniversalCatalog catalog = LoadInstalledCatalog();
        ProductDefinition product = catalog.Products.Values.Single(value =>
            value.SetId == PokemonHistoricalRuleProvider.NeoGenesisSetId);

        ProductRuleProfile profile = new PokemonHistoricalRuleProvider()
            .GetProfile(catalog, product.Id, "en");

        Assert.That(profile, Is.Not.Null);
        Assert.That(profile.Id, Is.EqualTo(PokemonHistoricalRuleProvider.NeoGenesisProfileId));
        Assert.That(profile.Trust, Is.EqualTo(ProductRuleTrust.HistoricallyVerified));
        Assert.That(profile.Confidence, Is.EqualTo(ProductRuleConfidence.Corroborated));
        Assert.That(profile.RegionId, Is.EqualTo(PokemonHistoricalRuleProvider.InternationalRegionId));
        Assert.That(profile.LastCheckedOn, Is.EqualTo(new DateTime(2026, 7, 23)));
        Assert.That(profile.Evidence.Single().Title, Is.EqualTo("PSA Neo Genesis guide"));
        Assert.That(profile.SourceReferences, Is.EquivalentTo(new[]
        {
            PokemonHistoricalRuleProvider.NeoGenesisSourceUrl
        }));
        Assert.That(profile.GetDescription("en"), Does.Contain("First Edition"));
        Assert.That(profile.GetDescription("zh"), Does.Contain("第一版"));
        Assert.That(profile.Rules.Slots.Sum(slot => slot.DrawCount), Is.EqualTo(11));
        Assert.That(profile.Rules.Slots.Select(slot => slot.DrawCount),
            Is.EquivalentTo(new[] { 7, 3, 1 }));
        Assert.That(profile.Rules.Pools.Values.Select(pool => pool.Entries.Count),
            Is.EquivalentTo(new[] { 41, 35, 35 }));

        WeightedPool rarePool = profile.Rules.Pools.Values.Single(pool =>
            pool.Id.EndsWith(":pool:rare", StringComparison.Ordinal));
        WeightedPoolEntry[] holoEntries = rarePool.Entries.Where(entry =>
            HasTrait(catalog, entry.PrintingId, "holo")).ToArray();
        WeightedPoolEntry[] normalEntries = rarePool.Entries.Where(entry =>
            HasTrait(catalog, entry.PrintingId, "normal")).ToArray();
        Assert.That(holoEntries, Has.Length.EqualTo(19));
        Assert.That(normalEntries, Has.Length.EqualTo(16));
        Assert.That(rarePool.Entries.All(entry =>
            HasTrait(catalog, entry.PrintingId, "first-edition")), Is.True);
        double holoWeight = holoEntries.Sum(entry => entry.Weight);
        double totalWeight = rarePool.Entries.Sum(entry => entry.Weight);
        Assert.That(holoWeight / totalWeight, Is.EqualTo(1d / 3d).Within(0.000001d));

        ProductDrawResult draw = new GachaEngine().Draw(
            catalog,
            profile.Rules,
            0,
            new SystemGachaRandomSource(2000));
        Assert.That(draw.Printings, Has.Count.EqualTo(11));
        Assert.That(draw.Printings.Select(entry => entry.PrintingId).Distinct().Count(), Is.EqualTo(11));
        Assert.That(draw.Printings.All(entry =>
            HasTrait(catalog, entry.PrintingId, "first-edition")), Is.True);
    }

    [Test]
    public void ExRubySapphire_BuildsSourcedNineCardProfile()
    {
        UniversalCatalog catalog = LoadInstalledCatalog();
        ProductDefinition product = catalog.Products.Values.Single(value =>
            value.SetId == PokemonHistoricalRuleProvider.ExRubySapphireSetId);

        ProductRuleProfile profile = new PokemonHistoricalRuleProvider()
            .GetProfile(catalog, product.Id, "en");

        Assert.That(profile, Is.Not.Null);
        Assert.That(profile.Id, Is.EqualTo(PokemonHistoricalRuleProvider.ExRubySapphireProfileId));
        Assert.That(profile.Trust, Is.EqualTo(ProductRuleTrust.HistoricallyVerified));
        Assert.That(profile.Confidence, Is.EqualTo(ProductRuleConfidence.Corroborated));
        Assert.That(profile.RegionId, Is.EqualTo(PokemonHistoricalRuleProvider.InternationalRegionId));
        Assert.That(profile.LastCheckedOn, Is.EqualTo(new DateTime(2026, 7, 25)));
        Assert.That(profile.SourceReferences, Is.EqualTo(new[]
        {
            PokemonHistoricalRuleProvider.ExRubySapphireSourceUrl
        }));
        Assert.That(profile.GetDescription("en"), Does.Contain("1 Reverse Holo"));
        Assert.That(profile.GetDescription("zh"), Does.Contain("1 反向闪"));
        Assert.That(profile.Rules.Slots.Sum(slot => slot.DrawCount), Is.EqualTo(9));
        Assert.That(profile.Rules.Slots.Select(slot => slot.DrawCount),
            Is.EquivalentTo(new[] { 5, 2, 1, 1 }));
        Assert.That(profile.Rules.Pools.Values.Select(pool => pool.Entries.Count),
            Is.EquivalentTo(new[] { 40, 34, 101, 37 }));

        WeightedPool reversePool = profile.Rules.Pools.Values.Single(pool =>
            pool.Id.EndsWith(":pool:reverse", StringComparison.Ordinal));
        Assert.That(reversePool.Entries.All(entry =>
            HasTrait(catalog, entry.PrintingId, "reverse")), Is.True);

        WeightedPool rarePool = profile.Rules.Pools.Values.Single(pool =>
            pool.Id.EndsWith(":pool:rare", StringComparison.Ordinal));
        WeightedPoolEntry[] exEntries = rarePool.Entries.Where(entry =>
            IsPokemonEx(catalog, entry.PrintingId)).ToArray();
        WeightedPoolEntry[] holoEntries = rarePool.Entries.Where(entry =>
            HasTrait(catalog, entry.PrintingId, "holo") &&
            !IsPokemonEx(catalog, entry.PrintingId)).ToArray();
        WeightedPoolEntry[] nonHoloEntries = rarePool.Entries.Where(entry =>
            HasTrait(catalog, entry.PrintingId, "normal")).ToArray();
        Assert.That(exEntries, Has.Length.EqualTo(8));
        Assert.That(holoEntries, Has.Length.EqualTo(16));
        Assert.That(nonHoloEntries, Has.Length.EqualTo(13));
        double totalWeight = rarePool.Entries.Sum(entry => entry.Weight);
        Assert.That(exEntries.Sum(entry => entry.Weight) / totalWeight,
            Is.EqualTo(3d / 36d).Within(0.000001d));
        Assert.That(holoEntries.Sum(entry => entry.Weight) / totalWeight,
            Is.EqualTo(6.5d / 36d).Within(0.000001d));

        ProductDrawResult draw = new GachaEngine().Draw(
            catalog,
            profile.Rules,
            0,
            new SystemGachaRandomSource(2003));
        Assert.That(draw.Printings, Has.Count.EqualTo(9));
        Assert.That(draw.Printings.Count(entry => entry.SlotId.EndsWith(":slot:reverse")), Is.EqualTo(1));
        Assert.That(draw.Printings.Count(entry => entry.SlotId.EndsWith(":slot:rare")), Is.EqualTo(1));
    }

    [Test]
    public void Provider_FallsBackForOtherSetsAndLanguages()
    {
        UniversalCatalog catalog = LoadInstalledCatalog();
        var historical = new PokemonHistoricalRuleProvider();
        ProductDefinition baseProduct = catalog.Products.Values.Single(value =>
            value.SetId == PokemonHistoricalRuleProvider.BaseSetId);
        ProductDefinition unsupportedProduct = catalog.Products.Values.First(value =>
            value.SetId != PokemonHistoricalRuleProvider.BaseSetId &&
            value.SetId != PokemonHistoricalRuleProvider.ExRubySapphireSetId &&
            value.SetId != PokemonHistoricalRuleProvider.NeoGenesisSetId);

        Assert.That(historical.GetProfile(catalog, baseProduct.Id, "zh-CN"), Is.Null);
        Assert.That(historical.GetProfile(catalog, unsupportedProduct.Id, "en"), Is.Null);

        var fallback = new FallbackProductRuleProvider(
            historical,
            new UniformSimulationRuleProvider(5));
        ProductRuleProfile simulated = fallback.GetProfile(catalog, unsupportedProduct.Id, "en");
        Assert.That(simulated.Trust, Is.EqualTo(ProductRuleTrust.Simulated));
        Assert.That(simulated.Rules.Slots.Sum(slot => slot.DrawCount), Is.EqualTo(5));
    }

    private static UniversalCatalog LoadInstalledCatalog()
    {
        string contentRoot = Path.Combine(Directory.GetCurrentDirectory(), "LocalContent", "Imports");
        if (!Directory.Exists(contentRoot))
            Assert.Ignore("Private LocalContent fixture is not installed on this machine.");
        return new PrivateManifestCatalogAdapter(new PokemonImportedCardVariantPolicy())
            .Build(new PrivateContentManifestReader().LoadDirectory(contentRoot))
            .Catalog;
    }

    private static bool HasTrait(UniversalCatalog catalog, string printingId, string trait)
    {
        PrintingDefinition printing = catalog.Printings[printingId];
        return catalog.Variants[printing.Identity.VariantId].Traits.Any(value =>
            string.Equals(value, trait, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPokemonEx(UniversalCatalog catalog, string printingId)
    {
        PrintingDefinition printing = catalog.Printings[printingId];
        return catalog.Items[printing.ItemId].Names.Values.Any(name =>
            name.EndsWith(" ex", StringComparison.OrdinalIgnoreCase));
    }
}
