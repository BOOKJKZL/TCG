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
            : this(
                id,
                rules,
                trust,
                string.IsNullOrWhiteSpace(sourceReference) ? Array.Empty<string>() : new[] { sourceReference },
                null)
        {
        }

        public ProductRuleProfile(
            string id,
            ProductDrawRules rules,
            ProductRuleTrust trust,
            IEnumerable<string> sourceReferences)
            : this(id, rules, trust, sourceReferences, null)
        {
        }

        public ProductRuleProfile(
            string id,
            ProductDrawRules rules,
            ProductRuleTrust trust,
            IEnumerable<string> sourceReferences,
            IReadOnlyDictionary<string, string> localizedDescriptions)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("A rule profile needs an id.", nameof(id));

            Id = id.Trim();
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            Trust = trust;
            SourceReferences = new ReadOnlyCollection<string>((sourceReferences ?? Array.Empty<string>())
                .Where(reference => !string.IsNullOrWhiteSpace(reference))
                .Select(reference => reference.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray());
            LocalizedDescriptions = CopyDescriptions(localizedDescriptions, Id);
        }

        public string Id { get; }
        public ProductDrawRules Rules { get; }
        public ProductRuleTrust Trust { get; }
        public IReadOnlyList<string> SourceReferences { get; }
        public IReadOnlyDictionary<string, string> LocalizedDescriptions { get; }
        public string SourceReference => SourceReferences.FirstOrDefault();
        public bool IsHistoricallyVerified => Trust == ProductRuleTrust.HistoricallyVerified;

        public string GetDescription(string languageId, string fallbackLanguageId = "en")
        {
            if (!string.IsNullOrWhiteSpace(languageId) &&
                LocalizedDescriptions.TryGetValue(languageId, out string localized))
            {
                return localized;
            }

            if (!string.IsNullOrWhiteSpace(fallbackLanguageId) &&
                LocalizedDescriptions.TryGetValue(fallbackLanguageId, out string fallback))
            {
                return fallback;
            }

            return LocalizedDescriptions.Values.First();
        }

        private static IReadOnlyDictionary<string, string> CopyDescriptions(
            IReadOnlyDictionary<string, string> source,
            string fallback)
        {
            var copy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (source != null)
            {
                foreach (KeyValuePair<string, string> entry in source)
                {
                    if (!string.IsNullOrWhiteSpace(entry.Key) && !string.IsNullOrWhiteSpace(entry.Value))
                        copy[entry.Key.Trim()] = entry.Value.Trim();
                }
            }

            if (copy.Count == 0)
                copy["en"] = fallback;
            return new ReadOnlyDictionary<string, string>(copy);
        }
    }

    public interface IProductRuleProvider
    {
        ProductRuleProfile GetProfile(UniversalCatalog catalog, string productId, string languageId = null);
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

        public ProductRuleProfile GetProfile(UniversalCatalog catalog, string productId, string requestedLanguageId = null)
        {
            string effectiveLanguageId = string.IsNullOrWhiteSpace(requestedLanguageId)
                ? languageId
                : requestedLanguageId;
            ProductDrawRules rules = SimulatedProductRuleFactory.CreateUniform(
                catalog,
                productId,
                cardsPerPack,
                effectiveLanguageId);
            return new ProductRuleProfile(
                "uniform-simulation-v1",
                rules,
                ProductRuleTrust.Simulated,
                new[] { "generated:uniform-simulation-v1" },
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["en"] = $"Uniform simulation · {cardsPerPack} cards · equal odds per installed printing",
                    ["zh"] = $"均匀模拟 · {cardsPerPack} 张 · 每个已安装印刷版本等概率"
                });
        }
    }

    public sealed class FallbackProductRuleProvider : IProductRuleProvider
    {
        private readonly IProductRuleProvider primary;
        private readonly IProductRuleProvider fallback;

        public FallbackProductRuleProvider(IProductRuleProvider primary, IProductRuleProvider fallback)
        {
            this.primary = primary ?? throw new ArgumentNullException(nameof(primary));
            this.fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
        }

        public ProductRuleProfile GetProfile(UniversalCatalog catalog, string productId, string languageId = null)
        {
            return primary.GetProfile(catalog, productId, languageId) ??
                   fallback.GetProfile(catalog, productId, languageId);
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
        private readonly string contentLanguageId;
        private readonly Dictionary<string, ProductRuleProfile> profileCache =
            new Dictionary<string, ProductRuleProfile>(StringComparer.Ordinal);

        public ProductOpeningService(
            UniversalCatalog catalog,
            IProductRuleProvider ruleProvider,
            IInventoryProgressStore inventory,
            GachaEngine engine = null,
            string contentLanguageId = null)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.ruleProvider = ruleProvider ?? throw new ArgumentNullException(nameof(ruleProvider));
            this.inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            this.engine = engine ?? new GachaEngine();
            this.contentLanguageId = string.IsNullOrWhiteSpace(contentLanguageId)
                ? null
                : contentLanguageId.Trim();
        }

        public ProductRuleProfile GetProfile(string productId)
        {
            if (string.IsNullOrWhiteSpace(productId))
                throw new ArgumentException("A product id is required.", nameof(productId));
            if (profileCache.TryGetValue(productId, out ProductRuleProfile cached))
                return cached;

            ProductRuleProfile profile = ruleProvider.GetProfile(catalog, productId, contentLanguageId);
            if (profile == null)
                throw new InvalidOperationException("The product rule provider returned no profile.");
            if (!string.Equals(profile.Rules.ProductId, productId, StringComparison.Ordinal))
                throw new InvalidOperationException("The product rule provider returned rules for another product.");
            profileCache.Add(productId, profile);
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
