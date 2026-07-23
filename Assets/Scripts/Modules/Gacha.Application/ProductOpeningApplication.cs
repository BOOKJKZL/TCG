using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Gacha.Domain;

namespace Gacha.Application
{
    public enum ProductRuleTrust
    {
        Simulated,
        HistoricallyVerified
    }

    public sealed class ProductRuleProfile
    {
        public ProductRuleProfile(
            string id,
            ProductDrawRules rules,
            ProductRuleTrust trust,
            string sourceReference)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("A rule profile needs an id.", nameof(id));

            Id = id.Trim();
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            Trust = trust;
            SourceReference = string.IsNullOrWhiteSpace(sourceReference)
                ? null
                : sourceReference.Trim();
        }

        public string Id { get; }
        public ProductDrawRules Rules { get; }
        public ProductRuleTrust Trust { get; }
        public string SourceReference { get; }
        public bool IsHistoricallyVerified => Trust == ProductRuleTrust.HistoricallyVerified;
    }

    public interface IProductRuleProvider
    {
        ProductRuleProfile GetProfile(UniversalCatalog catalog, string productId);
    }

    public sealed class UniformSimulationRuleProvider : IProductRuleProvider
    {
        private readonly int cardsPerPack;
        private readonly string languageId;

        public UniformSimulationRuleProvider(int cardsPerPack = 5, string languageId = null)
        {
            if (cardsPerPack < 1 || cardsPerPack > 20)
                throw new ArgumentOutOfRangeException(nameof(cardsPerPack));
            this.cardsPerPack = cardsPerPack;
            this.languageId = string.IsNullOrWhiteSpace(languageId) ? null : languageId.Trim();
        }

        public ProductRuleProfile GetProfile(UniversalCatalog catalog, string productId)
        {
            ProductDrawRules rules = SimulatedProductRuleFactory.CreateUniform(
                catalog,
                productId,
                cardsPerPack,
                languageId);
            return new ProductRuleProfile(
                "uniform-simulation-v1",
                rules,
                ProductRuleTrust.Simulated,
                "generated:uniform-simulation-v1");
        }
    }

    public sealed class RarityOdds
    {
        internal RarityOdds(string rarityId, double averageSlotProbability, double expectedCount)
        {
            RarityId = rarityId;
            AverageSlotProbability = averageSlotProbability;
            ExpectedCount = expectedCount;
        }

        public string RarityId { get; }
        public double AverageSlotProbability { get; }
        public double ExpectedCount { get; }
    }

    public sealed class ProductOddsSummary
    {
        internal ProductOddsSummary(
            int totalDrawCount,
            IReadOnlyList<RarityOdds> rarities,
            bool hasConditionalGuarantees)
        {
            TotalDrawCount = totalDrawCount;
            Rarities = rarities;
            HasConditionalGuarantees = hasConditionalGuarantees;
        }

        public int TotalDrawCount { get; }
        public IReadOnlyList<RarityOdds> Rarities { get; }
        public bool HasConditionalGuarantees { get; }
    }

    public static class ProductOddsAnalyzer
    {
        public static ProductOddsSummary Analyze(UniversalCatalog catalog, ProductDrawRules rules)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            if (rules == null)
                throw new ArgumentNullException(nameof(rules));

            int totalDrawCount = 0;
            var expectedByRarity = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (SlotRule slot in rules.Slots)
            {
                totalDrawCount += slot.DrawCount;
                WeightedPool pool = rules.Pools[slot.PoolId];
                double totalWeight = pool.Entries.Sum(entry => entry.Weight);
                foreach (IGrouping<string, WeightedPoolEntry> group in pool.Entries.GroupBy(entry =>
                {
                    if (!catalog.Printings.TryGetValue(entry.PrintingId, out PrintingDefinition printing))
                        throw new ArgumentException(
                            $"Pool '{pool.Id}' references missing printing '{entry.PrintingId}'.",
                            nameof(rules));
                    return printing.RarityId;
                }, StringComparer.Ordinal))
                {
                    double expected = slot.DrawCount * group.Sum(entry => entry.Weight) / totalWeight;
                    expectedByRarity[group.Key] = expectedByRarity.TryGetValue(group.Key, out double current)
                        ? current + expected
                        : expected;
                }
            }

            if (totalDrawCount <= 0)
                throw new ArgumentException("Product rules do not draw any cards.", nameof(rules));

            RarityOdds[] odds = expectedByRarity
                .OrderBy(pair => catalog.Rarities.TryGetValue(pair.Key, out RarityDefinition rarity)
                    ? rarity.DisplayRank
                    : int.MaxValue)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new RarityOdds(
                    pair.Key,
                    pair.Value / totalDrawCount,
                    pair.Value))
                .ToArray();
            return new ProductOddsSummary(
                totalDrawCount,
                new ReadOnlyCollection<RarityOdds>(odds),
                rules.Guarantees.Count > 0);
        }
    }

    public sealed class InventoryAward
    {
        public InventoryAward(string printingId, int previousCount, int currentCount)
        {
            if (string.IsNullOrWhiteSpace(printingId))
                throw new ArgumentException("A printing id is required.", nameof(printingId));
            if (previousCount < 0 || currentCount <= previousCount)
                throw new ArgumentOutOfRangeException(nameof(currentCount));

            PrintingId = printingId.Trim();
            PreviousCount = previousCount;
            CurrentCount = currentCount;
        }

        public string PrintingId { get; }
        public int PreviousCount { get; }
        public int CurrentCount { get; }
        public bool IsNew => PreviousCount == 0;
    }

    public sealed class ProductInventoryCommit
    {
        public ProductInventoryCommit(
            string productId,
            int productsOpened,
            IReadOnlyList<InventoryAward> awards)
        {
            if (string.IsNullOrWhiteSpace(productId))
                throw new ArgumentException("A product id is required.", nameof(productId));
            if (productsOpened <= 0)
                throw new ArgumentOutOfRangeException(nameof(productsOpened));

            ProductId = productId.Trim();
            ProductsOpened = productsOpened;
            Awards = awards ?? throw new ArgumentNullException(nameof(awards));
        }

        public string ProductId { get; }
        public int ProductsOpened { get; }
        public IReadOnlyList<InventoryAward> Awards { get; }
        public int NewPrintingCount => Awards.Count(award => award.IsNew);
    }

    public interface IInventoryProgressStore
    {
        int GetProductsOpened(string productId);
        ProductInventoryCommit Commit(ProductDrawResult result);
    }

    public sealed class ProductOpeningOutcome
    {
        internal ProductOpeningOutcome(
            ProductRuleProfile profile,
            ProductDrawResult draw,
            ProductInventoryCommit inventory)
        {
            Profile = profile;
            Draw = draw;
            Inventory = inventory;
        }

        public ProductRuleProfile Profile { get; }
        public ProductDrawResult Draw { get; }
        public ProductInventoryCommit Inventory { get; }
    }

    public sealed class ProductOpeningService
    {
        private readonly UniversalCatalog catalog;
        private readonly IProductRuleProvider ruleProvider;
        private readonly IInventoryProgressStore inventory;
        private readonly GachaEngine engine;

        public ProductOpeningService(
            UniversalCatalog catalog,
            IProductRuleProvider ruleProvider,
            IInventoryProgressStore inventory,
            GachaEngine engine = null)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.ruleProvider = ruleProvider ?? throw new ArgumentNullException(nameof(ruleProvider));
            this.inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            this.engine = engine ?? new GachaEngine();
        }

        public ProductRuleProfile GetProfile(string productId)
        {
            ProductRuleProfile profile = ruleProvider.GetProfile(catalog, productId);
            if (profile == null)
                throw new InvalidOperationException("The product rule provider returned no profile.");
            if (!string.Equals(profile.Rules.ProductId, productId, StringComparison.Ordinal))
                throw new InvalidOperationException("The product rule provider returned rules for another product.");
            return profile;
        }

        public ProductOddsSummary GetOdds(string productId)
        {
            return ProductOddsAnalyzer.Analyze(catalog, GetProfile(productId).Rules);
        }

        public ProductOpeningOutcome Open(string productId, IGachaRandomSource random = null)
        {
            ProductRuleProfile profile = GetProfile(productId);
            int productsOpened = inventory.GetProductsOpened(productId);
            ProductDrawResult draw = engine.Draw(catalog, profile.Rules, productsOpened, random);
            ProductInventoryCommit commit = inventory.Commit(draw);
            if (commit == null)
                throw new InvalidOperationException("The inventory store returned no commit result.");
            return new ProductOpeningOutcome(profile, draw, commit);
        }
    }
}
