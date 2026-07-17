using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Gacha.Domain
{
    public interface IGachaRandomSource
    {
        double Value { get; }
        int Range(int minInclusive, int maxExclusive);
    }

    public sealed class SystemGachaRandomSource : IGachaRandomSource
    {
        private readonly Random random;

        public SystemGachaRandomSource() : this(Environment.TickCount) { }
        public SystemGachaRandomSource(int seed) => random = new Random(seed);
        public double Value => random.NextDouble();
        public int Range(int minInclusive, int maxExclusive) => random.Next(minInclusive, maxExclusive);
    }

    public sealed class GachaRuleException : Exception
    {
        public GachaRuleException(string message) : base(message) { }
    }

    public sealed class DrawnPrinting
    {
        internal DrawnPrinting(string printingId, string slotId, int revealOrder, bool guaranteeReplacement)
        {
            PrintingId = printingId;
            SlotId = slotId;
            RevealOrder = revealOrder;
            IsGuaranteeReplacement = guaranteeReplacement;
        }

        public string PrintingId { get; }
        public string SlotId { get; }
        public int RevealOrder { get; }
        public bool IsGuaranteeReplacement { get; }
    }

    public sealed class ProductDrawResult
    {
        internal ProductDrawResult(string productId, IReadOnlyList<DrawnPrinting> printings, bool guaranteeApplied)
        {
            ProductId = productId;
            Printings = printings;
            GuaranteeApplied = guaranteeApplied;
        }

        public string ProductId { get; }
        public IReadOnlyList<DrawnPrinting> Printings { get; }
        public bool GuaranteeApplied { get; }
    }

    public sealed class GachaEngine
    {
        public ProductDrawResult Draw(
            UniversalCatalog catalog,
            ProductDrawRules rules,
            int productsOpenedBeforeDraw,
            IGachaRandomSource random = null)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (productsOpenedBeforeDraw < 0) throw new ArgumentOutOfRangeException(nameof(productsOpenedBeforeDraw));
            random ??= new SystemGachaRandomSource();

            ValidateAgainstCatalog(catalog, rules);
            List<DrawnPrinting> results = new List<DrawnPrinting>();
            HashSet<string> usedPrintingIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (SlotRule slot in rules.Slots.OrderBy(slot => slot.RevealOrder))
            {
                WeightedPool pool = rules.Pools[slot.PoolId];
                for (int drawIndex = 0; drawIndex < slot.DrawCount; drawIndex++)
                {
                    string printingId = DrawFromPool(pool, slot.AllowDuplicates ? null : usedPrintingIds, random);
                    results.Add(new DrawnPrinting(printingId, slot.Id, slot.RevealOrder + drawIndex, false));
                    usedPrintingIds.Add(printingId);
                }
            }

            bool guaranteeApplied = false;
            foreach (GuaranteeRule guarantee in rules.Guarantees)
            {
                if (!guarantee.ShouldApply(productsOpenedBeforeDraw)) continue;
                guaranteeApplied |= ApplyGuarantee(catalog, rules, guarantee, results, random);
            }

            IReadOnlyList<DrawnPrinting> ordered = new ReadOnlyCollection<DrawnPrinting>(results
                .OrderBy(result => result.RevealOrder)
                .ToList());
            return new ProductDrawResult(rules.ProductId, ordered, guaranteeApplied);
        }

        private static bool ApplyGuarantee(
            UniversalCatalog catalog,
            ProductDrawRules rules,
            GuaranteeRule guarantee,
            List<DrawnPrinting> results,
            IGachaRandomSource random)
        {
            HashSet<string> qualifyingRarities = new HashSet<string>(guarantee.QualifyingRarityIds, StringComparer.Ordinal);
            int qualifyingCount = results.Count(result =>
                qualifyingRarities.Contains(catalog.Printings[result.PrintingId].RarityId));
            int replacementsNeeded = guarantee.MinimumCount - qualifyingCount;
            if (replacementsNeeded <= 0) return false;

            HashSet<string> eligibleSlots = new HashSet<string>(guarantee.EligibleSlotIds, StringComparer.Ordinal);
            List<int> candidates = results
                .Select((result, index) => new { result, index })
                .Where(pair => eligibleSlots.Contains(pair.result.SlotId) &&
                               !qualifyingRarities.Contains(catalog.Printings[pair.result.PrintingId].RarityId))
                .Select(pair => pair.index)
                .ToList();
            if (candidates.Count < replacementsNeeded)
                throw new GachaRuleException($"Guarantee '{guarantee.Id}' needs {replacementsNeeded} replacements but only {candidates.Count} slots are eligible.");

            WeightedPool guaranteePool = rules.Pools[guarantee.GuaranteePoolId];
            bool changed = false;
            for (int replacement = 0; replacement < replacementsNeeded; replacement++)
            {
                int candidateListIndex = random.Range(0, candidates.Count);
                int resultIndex = candidates[candidateListIndex];
                candidates.RemoveAt(candidateListIndex);
                DrawnPrinting original = results[resultIndex];
                SlotRule slot = rules.Slots.First(rule => rule.Id == original.SlotId);
                HashSet<string> exclusions = slot.AllowDuplicates
                    ? null
                    : new HashSet<string>(results.Where((_, index) => index != resultIndex).Select(item => item.PrintingId), StringComparer.Ordinal);
                string replacementId = DrawFromPool(guaranteePool, exclusions, random);
                results[resultIndex] = new DrawnPrinting(replacementId, original.SlotId, original.RevealOrder, true);
                changed = true;
            }
            return changed;
        }

        private static string DrawFromPool(WeightedPool pool, HashSet<string> exclusions, IGachaRandomSource random)
        {
            WeightedPoolEntry[] available = pool.Entries
                .Where(entry => exclusions == null || !exclusions.Contains(entry.PrintingId))
                .ToArray();
            if (available.Length == 0)
                throw new GachaRuleException($"Pool '{pool.Id}' has no available entries after duplicate filtering.");

            double totalWeight = available.Sum(entry => entry.Weight);
            double roll = Math.Max(0d, Math.Min(0.999999999999d, random.Value)) * totalWeight;
            double cumulative = 0d;
            foreach (WeightedPoolEntry entry in available)
            {
                cumulative += entry.Weight;
                if (roll < cumulative) return entry.PrintingId;
            }
            return available[available.Length - 1].PrintingId;
        }

        private static void ValidateAgainstCatalog(UniversalCatalog catalog, ProductDrawRules rules)
        {
            if (!catalog.Products.TryGetValue(rules.ProductId, out ProductDefinition product))
                throw new GachaRuleException($"Rules reference missing product '{rules.ProductId}'.");
            HashSet<string> eligible = new HashSet<string>(product.EligiblePrintingIds, StringComparer.Ordinal);
            foreach (WeightedPool pool in rules.Pools.Values)
            foreach (WeightedPoolEntry entry in pool.Entries)
            {
                if (!catalog.Printings.ContainsKey(entry.PrintingId))
                    throw new GachaRuleException($"Pool '{pool.Id}' references missing printing '{entry.PrintingId}'.");
                if (!eligible.Contains(entry.PrintingId))
                    throw new GachaRuleException($"Pool '{pool.Id}' uses printing '{entry.PrintingId}' outside product '{product.Id}'.");
            }

            foreach (GuaranteeRule guarantee in rules.Guarantees)
            {
                HashSet<string> qualifying = new HashSet<string>(guarantee.QualifyingRarityIds, StringComparer.Ordinal);
                WeightedPool pool = rules.Pools[guarantee.GuaranteePoolId];
                if (pool.Entries.Any(entry => !qualifying.Contains(catalog.Printings[entry.PrintingId].RarityId)))
                    throw new GachaRuleException($"Guarantee pool '{pool.Id}' contains a non-qualifying rarity.");
            }
        }
    }
}
