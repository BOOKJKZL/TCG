using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Gacha.Domain
{
    public sealed class WeightedPoolEntry
    {
        public WeightedPoolEntry(string printingId, double weight)
        {
            PrintingId = Definition.Required(printingId, nameof(printingId));
            if (double.IsNaN(weight) || double.IsInfinity(weight) || weight <= 0d)
                throw new ArgumentOutOfRangeException(nameof(weight), "Weight must be finite and greater than zero.");
            Weight = weight;
        }

        public string PrintingId { get; }
        public double Weight { get; }
    }

    public sealed class WeightedPool
    {
        public WeightedPool(string id, IEnumerable<WeightedPoolEntry> entries)
        {
            Id = Definition.Required(id, nameof(id));
            WeightedPoolEntry[] copy = (entries ?? throw new ArgumentNullException(nameof(entries))).ToArray();
            if (copy.Length == 0)
                throw new ArgumentException("A weighted pool must contain at least one entry.", nameof(entries));
            if (copy.Any(entry => entry == null))
                throw new ArgumentException("A weighted pool cannot contain null entries.", nameof(entries));
            Entries = new ReadOnlyCollection<WeightedPoolEntry>(copy);
        }

        public string Id { get; }
        public IReadOnlyList<WeightedPoolEntry> Entries { get; }
    }

    public sealed class SlotRule
    {
        public SlotRule(
            string id,
            string poolId,
            int drawCount = 1,
            int revealOrder = 0,
            bool allowDuplicates = true)
        {
            Id = Definition.Required(id, nameof(id));
            PoolId = Definition.Required(poolId, nameof(poolId));
            if (drawCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(drawCount), "Draw count must be greater than zero.");
            DrawCount = drawCount;
            RevealOrder = revealOrder;
            AllowDuplicates = allowDuplicates;
        }

        public string Id { get; }
        public string PoolId { get; }
        public int DrawCount { get; }
        public int RevealOrder { get; }
        public bool AllowDuplicates { get; }
    }

    public sealed class GuaranteeRule
    {
        public GuaranteeRule(
            string id,
            int everyNProducts,
            int minimumCount,
            string guaranteePoolId,
            IEnumerable<string> qualifyingRarityIds,
            IEnumerable<string> eligibleSlotIds)
        {
            Id = Definition.Required(id, nameof(id));
            if (everyNProducts <= 0)
                throw new ArgumentOutOfRangeException(nameof(everyNProducts));
            if (minimumCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(minimumCount));
            EveryNProducts = everyNProducts;
            MinimumCount = minimumCount;
            GuaranteePoolId = Definition.Required(guaranteePoolId, nameof(guaranteePoolId));
            QualifyingRarityIds = Definition.CopyStrings(qualifyingRarityIds);
            EligibleSlotIds = Definition.CopyStrings(eligibleSlotIds);
            if (QualifyingRarityIds.Count == 0)
                throw new ArgumentException("A guarantee needs at least one qualifying rarity.", nameof(qualifyingRarityIds));
            if (EligibleSlotIds.Count == 0)
                throw new ArgumentException("A guarantee needs at least one eligible slot.", nameof(eligibleSlotIds));
        }

        public string Id { get; }
        public int EveryNProducts { get; }
        public int MinimumCount { get; }
        public string GuaranteePoolId { get; }
        public IReadOnlyList<string> QualifyingRarityIds { get; }
        public IReadOnlyList<string> EligibleSlotIds { get; }

        public bool ShouldApply(int productsOpenedBeforeDraw)
        {
            return (productsOpenedBeforeDraw + 1) % EveryNProducts == 0;
        }
    }

    public sealed class ProductDrawRules
    {
        public ProductDrawRules(
            string productId,
            IEnumerable<WeightedPool> pools,
            IEnumerable<SlotRule> slots,
            IEnumerable<GuaranteeRule> guarantees = null)
        {
            ProductId = Definition.Required(productId, nameof(productId));
            Pools = Index(pools, pool => pool.Id, "pool");
            SlotRule[] slotCopy = (slots ?? throw new ArgumentNullException(nameof(slots))).ToArray();
            if (slotCopy.Length == 0)
                throw new ArgumentException("A product needs at least one slot rule.", nameof(slots));
            if (slotCopy.Any(slot => slot == null))
                throw new ArgumentException("Slot rules cannot contain null.", nameof(slots));
            if (slotCopy.Select(slot => slot.Id).Distinct(StringComparer.Ordinal).Count() != slotCopy.Length)
                throw new ArgumentException("Slot rule ids must be unique.", nameof(slots));
            foreach (SlotRule slot in slotCopy)
                if (!Pools.ContainsKey(slot.PoolId)) throw new ArgumentException($"Slot '{slot.Id}' references missing pool '{slot.PoolId}'.");
            Slots = new ReadOnlyCollection<SlotRule>(slotCopy);

            GuaranteeRule[] guaranteeCopy = (guarantees ?? Array.Empty<GuaranteeRule>()).ToArray();
            if (guaranteeCopy.Any(rule => rule == null))
                throw new ArgumentException("Guarantee rules cannot contain null.", nameof(guarantees));
            if (guaranteeCopy.Select(rule => rule.Id).Distinct(StringComparer.Ordinal).Count() != guaranteeCopy.Length)
                throw new ArgumentException("Guarantee rule ids must be unique.", nameof(guarantees));
            HashSet<string> slotIds = new HashSet<string>(slotCopy.Select(slot => slot.Id), StringComparer.Ordinal);
            foreach (GuaranteeRule guarantee in guaranteeCopy)
            {
                if (!Pools.ContainsKey(guarantee.GuaranteePoolId))
                    throw new ArgumentException($"Guarantee '{guarantee.Id}' references missing pool '{guarantee.GuaranteePoolId}'.");
                foreach (string slotId in guarantee.EligibleSlotIds)
                    if (!slotIds.Contains(slotId)) throw new ArgumentException($"Guarantee '{guarantee.Id}' references missing slot '{slotId}'.");
            }
            Guarantees = new ReadOnlyCollection<GuaranteeRule>(guaranteeCopy);
        }

        public string ProductId { get; }
        public IReadOnlyDictionary<string, WeightedPool> Pools { get; }
        public IReadOnlyList<SlotRule> Slots { get; }
        public IReadOnlyList<GuaranteeRule> Guarantees { get; }

        private static IReadOnlyDictionary<string, T> Index<T>(IEnumerable<T> values, Func<T, string> getId, string label)
            where T : class
        {
            Dictionary<string, T> result = new Dictionary<string, T>(StringComparer.Ordinal);
            foreach (T value in values ?? throw new ArgumentNullException(nameof(values)))
            {
                if (value == null) throw new ArgumentException($"The {label} collection contains null.", nameof(values));
                string id = getId(value);
                if (result.ContainsKey(id)) throw new ArgumentException($"Duplicate {label} id '{id}'.", nameof(values));
                result.Add(id, value);
            }
            if (result.Count == 0) throw new ArgumentException($"At least one {label} is required.", nameof(values));
            return new ReadOnlyDictionary<string, T>(result);
        }
    }

    public static class SimulatedProductRuleFactory
    {
        public static ProductDrawRules CreateUniform(UniversalCatalog catalog, string productId, int cardsPerPack = 5)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (!catalog.Products.TryGetValue(productId, out ProductDefinition product))
                throw new ArgumentException($"Unknown product '{productId}'.", nameof(productId));
            if (product.EligiblePrintingIds.Count == 0)
                throw new ArgumentException($"Product '{productId}' has no eligible printings.", nameof(productId));

            string poolId = productId + ":pool:uniform";
            string slotId = productId + ":slot:main";
            WeightedPool pool = new WeightedPool(poolId,
                product.EligiblePrintingIds.Select(printingId => new WeightedPoolEntry(printingId, 1d)));
            SlotRule slot = new SlotRule(slotId, poolId, cardsPerPack, 0, true);
            return new ProductDrawRules(productId, new[] { pool }, new[] { slot });
        }
    }
}
