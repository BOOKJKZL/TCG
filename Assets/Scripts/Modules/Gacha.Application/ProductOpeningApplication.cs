using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Gacha.Domain;

namespace Gacha.Application
{
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
                ProductRuleConfidence.Unverified,
                "unspecified",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["en"] = "Region unspecified",
                    ["zh"] = "地区未指定"
                },
                Array.Empty<ProductRuleEvidence>(),
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
        ProductInventoryBatchCommit CommitBatch(ProductOpeningBatchCommitRequest request);
        IReadOnlyList<ProductOpeningHistoryEntry> GetOpeningHistory(int maximumCount);
        ProductOpeningStatistics GetOpeningStatistics();
    }

    public sealed class ProductOpeningBatchCommitRequest
    {
        public ProductOpeningBatchCommitRequest(
            string transactionId,
            DateTime openedAtUtc,
            string productId,
            string setId,
            string languageId,
            string profileId,
            IReadOnlyList<ProductDrawResult> draws,
            IReadOnlyDictionary<string, string> rarityByPrintingId)
        {
            if (string.IsNullOrWhiteSpace(transactionId))
                throw new ArgumentException("A transaction id is required.", nameof(transactionId));
            if (openedAtUtc == default)
                throw new ArgumentException("An opening timestamp is required.", nameof(openedAtUtc));
            if (string.IsNullOrWhiteSpace(productId))
                throw new ArgumentException("A product id is required.", nameof(productId));
            if (string.IsNullOrWhiteSpace(setId))
                throw new ArgumentException("A set id is required.", nameof(setId));
            if (string.IsNullOrWhiteSpace(languageId))
                throw new ArgumentException("A card language id is required.", nameof(languageId));
            if (string.IsNullOrWhiteSpace(profileId))
                throw new ArgumentException("A rule profile id is required.", nameof(profileId));
            if (draws == null || draws.Count == 0)
                throw new ArgumentException("A batch needs at least one draw.", nameof(draws));
            if (draws.Count > 10)
                throw new ArgumentOutOfRangeException(nameof(draws), "A player batch cannot exceed ten products.");
            if (draws.Any(draw => draw == null ||
                !string.Equals(draw.ProductId, productId, StringComparison.Ordinal)))
            {
                throw new ArgumentException("Every draw must belong to the batch product.", nameof(draws));
            }

            string[] printingIds = draws.SelectMany(draw => draw.Printings)
                .Select(printing => printing.PrintingId)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (rarityByPrintingId == null ||
                printingIds.Any(id => !rarityByPrintingId.TryGetValue(id, out string rarityId) ||
                                      string.IsNullOrWhiteSpace(rarityId)))
            {
                throw new ArgumentException("Every drawn printing needs a rarity id.", nameof(rarityByPrintingId));
            }

            TransactionId = transactionId.Trim();
            OpenedAtUtc = openedAtUtc.ToUniversalTime();
            ProductId = productId.Trim();
            SetId = setId.Trim();
            LanguageId = languageId.Trim();
            ProfileId = profileId.Trim();
            Draws = new ReadOnlyCollection<ProductDrawResult>(draws.ToArray());
            RarityByPrintingId = new ReadOnlyDictionary<string, string>(
                printingIds.ToDictionary(id => id, id => rarityByPrintingId[id], StringComparer.Ordinal));
        }

        public string TransactionId { get; }
        public DateTime OpenedAtUtc { get; }
        public string ProductId { get; }
        public string SetId { get; }
        public string LanguageId { get; }
        public string ProfileId { get; }
        public IReadOnlyList<ProductDrawResult> Draws { get; }
        public IReadOnlyDictionary<string, string> RarityByPrintingId { get; }
    }

    public sealed class ProductInventoryBatchCommit
    {
        public ProductInventoryBatchCommit(
            string transactionId,
            IReadOnlyList<ProductInventoryCommit> products)
        {
            if (string.IsNullOrWhiteSpace(transactionId))
                throw new ArgumentException("A transaction id is required.", nameof(transactionId));
            if (products == null || products.Count == 0)
                throw new ArgumentException("A batch commit needs at least one product.", nameof(products));
            if (products.Any(product => product == null))
                throw new ArgumentException("A batch commit cannot contain null products.", nameof(products));
            TransactionId = transactionId.Trim();
            Products = new ReadOnlyCollection<ProductInventoryCommit>(products.ToArray());
        }

        public string TransactionId { get; }
        public IReadOnlyList<ProductInventoryCommit> Products { get; }
        public int ProductCount => Products.Count;
        public int CardCount => Products.Sum(product => product.Awards.Count);
        public int NewPrintingCount => Products.Sum(product => product.NewPrintingCount);
        public int ProductsOpened => Products[Products.Count - 1].ProductsOpened;
    }

    public sealed class ProductOpeningHistoryEntry
    {
        public ProductOpeningHistoryEntry(
            string transactionId,
            DateTime openedAtUtc,
            string productId,
            string setId,
            string languageId,
            string profileId,
            int productCount,
            int cardCount,
            int newPrintingCount,
            IReadOnlyDictionary<string, int> rarityCounts)
        {
            TransactionId = string.IsNullOrWhiteSpace(transactionId)
                ? throw new ArgumentException("A transaction id is required.", nameof(transactionId))
                : transactionId.Trim();
            OpenedAtUtc = openedAtUtc.ToUniversalTime();
            ProductId = Required(productId, nameof(productId));
            SetId = Required(setId, nameof(setId));
            LanguageId = Required(languageId, nameof(languageId));
            ProfileId = Required(profileId, nameof(profileId));
            if (productCount <= 0) throw new ArgumentOutOfRangeException(nameof(productCount));
            if (cardCount <= 0) throw new ArgumentOutOfRangeException(nameof(cardCount));
            if (newPrintingCount < 0 || newPrintingCount > cardCount)
                throw new ArgumentOutOfRangeException(nameof(newPrintingCount));
            ProductCount = productCount;
            CardCount = cardCount;
            NewPrintingCount = newPrintingCount;
            RarityCounts = CopyCounts(rarityCounts);
        }

        public string TransactionId { get; }
        public DateTime OpenedAtUtc { get; }
        public string ProductId { get; }
        public string SetId { get; }
        public string LanguageId { get; }
        public string ProfileId { get; }
        public int ProductCount { get; }
        public int CardCount { get; }
        public int NewPrintingCount { get; }
        public IReadOnlyDictionary<string, int> RarityCounts { get; }

        private static string Required(string value, string name) =>
            string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("A value is required.", name)
                : value.Trim();

        internal static IReadOnlyDictionary<string, int> CopyCounts(
            IReadOnlyDictionary<string, int> source)
        {
            var copy = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (source != null)
            {
                foreach (KeyValuePair<string, int> pair in source)
                {
                    if (!string.IsNullOrWhiteSpace(pair.Key) && pair.Value > 0)
                        copy[pair.Key.Trim()] = pair.Value;
                }
            }
            return new ReadOnlyDictionary<string, int>(copy);
        }
    }

    public sealed class ProductOpeningStatistics
    {
        public ProductOpeningStatistics(
            IReadOnlyDictionary<string, int> productsByLanguage,
            IReadOnlyDictionary<string, int> productsBySet,
            IReadOnlyDictionary<string, int> cardsByRarity)
        {
            ProductsByLanguage = ProductOpeningHistoryEntry.CopyCounts(productsByLanguage);
            ProductsBySet = ProductOpeningHistoryEntry.CopyCounts(productsBySet);
            CardsByRarity = ProductOpeningHistoryEntry.CopyCounts(cardsByRarity);
        }

        public IReadOnlyDictionary<string, int> ProductsByLanguage { get; }
        public IReadOnlyDictionary<string, int> ProductsBySet { get; }
        public IReadOnlyDictionary<string, int> CardsByRarity { get; }
        public int TotalProductsOpened => ProductsByLanguage.Values.Sum();
        public int TotalCardsDrawn => CardsByRarity.Values.Sum();
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

    public sealed class ProductOpeningBatchOutcome
    {
        internal ProductOpeningBatchOutcome(
            ProductRuleProfile profile,
            IReadOnlyList<ProductDrawResult> draws,
            ProductInventoryBatchCommit inventory)
        {
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            Draws = draws ?? throw new ArgumentNullException(nameof(draws));
            Inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        }

        public ProductRuleProfile Profile { get; }
        public IReadOnlyList<ProductDrawResult> Draws { get; }
        public ProductInventoryBatchCommit Inventory { get; }
    }

    public sealed class ProductOpeningService
    {
        private readonly UniversalCatalog catalog;
        private readonly IProductRuleProvider ruleProvider;
        private readonly IInventoryProgressStore inventory;
        private readonly GachaEngine engine;
        private readonly string contentLanguageId;
        private readonly Func<DateTime> utcNow;
        private readonly Dictionary<string, ProductRuleProfile> profileCache =
            new Dictionary<string, ProductRuleProfile>(StringComparer.Ordinal);

        public ProductOpeningService(
            UniversalCatalog catalog,
            IProductRuleProvider ruleProvider,
            IInventoryProgressStore inventory,
            GachaEngine engine = null,
            string contentLanguageId = null,
            Func<DateTime> utcNow = null)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.ruleProvider = ruleProvider ?? throw new ArgumentNullException(nameof(ruleProvider));
            this.inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            this.engine = engine ?? new GachaEngine();
            this.contentLanguageId = string.IsNullOrWhiteSpace(contentLanguageId)
                ? null
                : contentLanguageId.Trim();
            this.utcNow = utcNow ?? (() => DateTime.UtcNow);
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
            ProductOpeningBatchOutcome batch = OpenBatch(productId, 1, random);
            return new ProductOpeningOutcome(
                batch.Profile,
                batch.Draws[0],
                batch.Inventory.Products[0]);
        }

        public ProductOpeningBatchOutcome OpenBatch(
            string productId,
            int productCount,
            IGachaRandomSource random = null)
        {
            if (productCount < 1 || productCount > 10)
                throw new ArgumentOutOfRangeException(nameof(productCount), "A player batch must contain 1 to 10 products.");
            ProductRuleProfile profile = GetProfile(productId);
            ProductDefinition product = catalog.Products[productId];
            int productsOpened = inventory.GetProductsOpened(productId);
            var draws = new List<ProductDrawResult>(productCount);
            for (int index = 0; index < productCount; index++)
                draws.Add(engine.Draw(catalog, profile.Rules, productsOpened + index, random));

            var rarityByPrintingId = draws
                .SelectMany(draw => draw.Printings)
                .Select(drawn => catalog.Printings[drawn.PrintingId])
                .GroupBy(printing => printing.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().RarityId, StringComparer.Ordinal);
            string languageId = profile.LanguageId ?? contentLanguageId ??
                draws.SelectMany(draw => draw.Printings)
                    .Select(drawn => catalog.Printings[drawn.PrintingId].Identity.LanguageId)
                    .First();
            var request = new ProductOpeningBatchCommitRequest(
                Guid.NewGuid().ToString("N"),
                utcNow(),
                productId,
                product.SetId,
                languageId,
                profile.Id,
                draws.AsReadOnly(),
                rarityByPrintingId);
            ProductInventoryBatchCommit commit = inventory.CommitBatch(request);
            if (commit == null || commit.Products.Count != productCount)
                throw new InvalidOperationException("The inventory store returned an invalid batch commit.");
            return new ProductOpeningBatchOutcome(profile, draws.AsReadOnly(), commit);
        }

        public IReadOnlyList<ProductOpeningHistoryEntry> GetOpeningHistory(int maximumCount = 50)
        {
            if (maximumCount < 1 || maximumCount > 250)
                throw new ArgumentOutOfRangeException(nameof(maximumCount));
            return inventory.GetOpeningHistory(maximumCount);
        }

        public ProductOpeningStatistics GetOpeningStatistics()
        {
            return inventory.GetOpeningStatistics() ??
                   throw new InvalidOperationException("The inventory store returned no opening statistics.");
        }
    }
}
