using System;
using System.Collections.Generic;
using System.Linq;
using Gacha.Domain;

namespace Gacha.Application
{
    // Serializable, brand-neutral rule contract. Infrastructure may load this from
    // Resources today and from a downloaded content package in the future.
    public sealed class ProductRuleCatalogDefinition
    {
        public int SchemaVersion { get; set; }
        public string Revision { get; set; }
        public List<ProductRuleDefinition> Rules { get; set; }
    }

    public sealed class ProductRuleDefinition
    {
        public string ProfileId { get; set; }
        public string RuleNamespace { get; set; }
        public string SetId { get; set; }
        public string LanguageId { get; set; }
        public DateTime? ReleaseDate { get; set; }
        public ProductRuleTrust Trust { get; set; }
        public ProductRuleConfidence Confidence { get; set; }
        public string RegionId { get; set; }
        public Dictionary<string, string> RegionNames { get; set; }
        public Dictionary<string, string> Descriptions { get; set; }
        public List<string> Exclusions { get; set; }
        public PrintingFilterDefinition Eligibility { get; set; }
        public List<ProductRuleEvidenceDefinition> Evidence { get; set; }
        public List<RulePoolDefinition> Pools { get; set; }
        public List<RuleSlotDefinition> Slots { get; set; }
        public List<RuleGuaranteeDefinition> Guarantees { get; set; }
    }

    public sealed class ProductRuleEvidenceDefinition
    {
        public string Title { get; set; }
        public string SourceReference { get; set; }
        public DateTime CheckedOn { get; set; }
    }

    public sealed class RulePoolDefinition
    {
        public string Id { get; set; }
        public List<RulePoolComponentDefinition> Components { get; set; }
    }

    public sealed class RulePoolComponentDefinition
    {
        public string Id { get; set; }
        public int ExpectedPrintingCount { get; set; }
        public double TotalWeight { get; set; }
        public List<PrintingFilterDefinition> AnyOf { get; set; }
    }

    public sealed class PrintingFilterDefinition
    {
        public List<string> RaritySlugs { get; set; }
        public List<string> RequiredTraits { get; set; }
        public List<string> ExcludedTraits { get; set; }
        public List<string> ItemCategories { get; set; }
        public List<string> ExcludedItemCategories { get; set; }
        public List<string> CardNumbers { get; set; }
        public List<string> ExcludedCardNumbers { get; set; }
        public List<string> ItemNames { get; set; }
        public List<string> ItemNameSuffixes { get; set; }
        public List<string> ExcludedItemNames { get; set; }
        public List<string> ExcludedItemNameSuffixes { get; set; }
    }

    public sealed class RuleSlotDefinition
    {
        public string Id { get; set; }
        public string PoolId { get; set; }
        public int DrawCount { get; set; }
        public int RevealOrder { get; set; }
        public bool AllowDuplicates { get; set; }
    }

    public sealed class RuleGuaranteeDefinition
    {
        public string Id { get; set; }
        public int EveryNProducts { get; set; }
        public int MinimumCount { get; set; }
        public string PoolId { get; set; }
        public List<string> QualifyingRarityIds { get; set; }
        public List<string> EligibleSlotIds { get; set; }
    }

    public interface IProductRuleDefinitionSource
    {
        ProductRuleCatalogDefinition Load();
    }

    public sealed class DataDrivenProductRuleProvider : IProductRuleProvider
    {
        public const int SupportedSchemaVersion = 1;

        private readonly ProductRuleCatalogDefinition catalogDefinition;

        public DataDrivenProductRuleProvider(IProductRuleDefinitionSource source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            catalogDefinition = source.Load() ??
                throw new InvalidOperationException("The product rule source returned no catalog.");
            ValidateCatalog(catalogDefinition);
        }

        public ProductRuleProfile GetProfile(
            UniversalCatalog catalog,
            string productId,
            string languageId = null)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (!catalog.Products.TryGetValue(productId, out ProductDefinition product))
                throw new ArgumentException($"Unknown product '{productId}'.", nameof(productId));
            if (string.IsNullOrWhiteSpace(languageId)) return null;

            ProductRuleDefinition definition = catalogDefinition.Rules.SingleOrDefault(rule =>
                string.Equals(rule.SetId, product.SetId, StringComparison.Ordinal) &&
                string.Equals(rule.LanguageId, languageId, StringComparison.OrdinalIgnoreCase));
            return definition == null
                ? null
                : ProductRuleDefinitionCompiler.Compile(
                    catalog,
                    product,
                    catalogDefinition.Revision,
                    definition);
        }

        private static void ValidateCatalog(ProductRuleCatalogDefinition definition)
        {
            if (definition.SchemaVersion != SupportedSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"Unsupported product rule schema {definition.SchemaVersion}; expected {SupportedSchemaVersion}.");
            }
            if (string.IsNullOrWhiteSpace(definition.Revision))
                throw new InvalidOperationException("A product rule catalog revision is required.");
            if (definition.Rules == null || definition.Rules.Count == 0)
                throw new InvalidOperationException("A product rule catalog needs at least one rule.");
            if (definition.Rules.Any(rule => rule == null))
                throw new InvalidOperationException("A product rule catalog cannot contain null rules.");

            string duplicate = definition.Rules
                .GroupBy(
                    rule => (rule.SetId ?? string.Empty) + "\n" + (rule.LanguageId ?? string.Empty).ToLowerInvariant(),
                    StringComparer.Ordinal)
                .FirstOrDefault(group => group.Count() > 1)?.Key;
            if (duplicate != null)
                throw new InvalidOperationException("Duplicate set/language product rule: " + duplicate.Replace("\n", "/"));
        }
    }

    public static class ProductRuleDefinitionCompiler
    {
        public static ProductRuleProfile Compile(
            UniversalCatalog catalog,
            ProductDefinition product,
            string catalogRevision,
            ProductRuleDefinition definition)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (product == null) throw new ArgumentNullException(nameof(product));
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            Require(definition.ProfileId, "profile id");
            Require(definition.RuleNamespace, "rule namespace");
            Require(definition.SetId, "set id");
            Require(definition.LanguageId, "language id");
            Require(definition.RegionId, "region id");
            if (!string.Equals(product.SetId, definition.SetId, StringComparison.Ordinal))
                throw new InvalidOperationException("The rule definition targets another set.");

            if (!catalog.Sets.TryGetValue(product.SetId, out SetDefinition set))
                throw new InvalidOperationException($"Product '{product.Id}' references missing set '{product.SetId}'.");
            if (definition.ReleaseDate.HasValue &&
                (!set.ReleaseDate.HasValue || set.ReleaseDate.Value.Date != definition.ReleaseDate.Value.Date))
            {
                throw new InvalidOperationException(
                    $"Rule '{definition.ProfileId}' expects release {definition.ReleaseDate:yyyy-MM-dd}, " +
                    $"but catalog set '{set.Id}' reports {set.ReleaseDate:yyyy-MM-dd}.");
            }

            string prefix = product.Id + ":" + definition.RuleNamespace.Trim();
            var poolIds = new HashSet<string>(StringComparer.Ordinal);
            WeightedPool[] pools = (definition.Pools ?? new List<RulePoolDefinition>())
                .Select(pool => CompilePool(
                    catalog,
                    product,
                    definition.LanguageId,
                    definition.Eligibility,
                    prefix,
                    pool,
                    poolIds))
                .ToArray();
            if (pools.Length == 0)
                throw new InvalidOperationException($"Rule '{definition.ProfileId}' needs at least one pool.");

            SlotRule[] slots = (definition.Slots ?? new List<RuleSlotDefinition>())
                .Select(slot => new SlotRule(
                    Id(prefix, slot?.Id, "slot"),
                    Id(prefix, slot?.PoolId, "slot pool"),
                    slot.DrawCount,
                    slot.RevealOrder,
                    slot.AllowDuplicates))
                .ToArray();
            GuaranteeRule[] guarantees = (definition.Guarantees ?? new List<RuleGuaranteeDefinition>())
                .Select(guarantee => new GuaranteeRule(
                    Id(prefix, guarantee?.Id, "guarantee"),
                    guarantee.EveryNProducts,
                    guarantee.MinimumCount,
                    Id(prefix, guarantee.PoolId, "guarantee pool"),
                    guarantee.QualifyingRarityIds,
                    (guarantee.EligibleSlotIds ?? new List<string>()).Select(id => Id(prefix, id, "eligible slot"))))
                .ToArray();
            var rules = new ProductDrawRules(product.Id, pools, slots, guarantees);

            ProductRuleEvidence[] evidence = (definition.Evidence ?? new List<ProductRuleEvidenceDefinition>())
                .Select(item => new ProductRuleEvidence(item.Title, item.SourceReference, item.CheckedOn))
                .ToArray();
            return new ProductRuleProfile(
                definition.ProfileId,
                rules,
                definition.Trust,
                definition.Confidence,
                definition.RegionId,
                definition.RegionNames,
                evidence,
                definition.Descriptions,
                catalogRevision,
                definition.LanguageId,
                definition.ReleaseDate,
                definition.Exclusions);
        }

        private static WeightedPool CompilePool(
            UniversalCatalog catalog,
            ProductDefinition product,
            string languageId,
            PrintingFilterDefinition eligibility,
            string prefix,
            RulePoolDefinition definition,
            HashSet<string> poolIds)
        {
            if (definition == null) throw new InvalidOperationException("A rule pool cannot be null.");
            string poolId = Id(prefix, definition.Id, "pool");
            if (!poolIds.Add(poolId))
                throw new InvalidOperationException($"Duplicate pool id '{definition.Id}'.");
            var entries = new List<WeightedPoolEntry>();
            var usedPrintingIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (RulePoolComponentDefinition component in definition.Components ??
                     new List<RulePoolComponentDefinition>())
            {
                if (component == null) throw new InvalidOperationException($"Pool '{definition.Id}' has a null component.");
                Require(component.Id, "pool component id");
                if (component.ExpectedPrintingCount <= 0)
                    throw new InvalidOperationException($"Pool component '{component.Id}' needs an expected printing count.");
                if (double.IsNaN(component.TotalWeight) || double.IsInfinity(component.TotalWeight) || component.TotalWeight <= 0d)
                    throw new InvalidOperationException($"Pool component '{component.Id}' needs a positive finite total weight.");
                if (component.AnyOf == null || component.AnyOf.Count == 0)
                    throw new InvalidOperationException($"Pool component '{component.Id}' needs at least one filter.");

                PrintingDefinition[] selected = product.EligiblePrintingIds
                    .Select(id => catalog.Printings[id])
                    .Where(printing => string.Equals(
                        printing.Identity.LanguageId,
                        languageId,
                        StringComparison.OrdinalIgnoreCase))
                    .Where(printing => eligibility == null || Matches(catalog, printing, eligibility))
                    .Where(printing => component.AnyOf.Any(filter => Matches(catalog, printing, filter)))
                    .Distinct(new PrintingIdComparer())
                    .OrderBy(printing => printing.Id, StringComparer.Ordinal)
                    .ToArray();
                if (selected.Length != component.ExpectedPrintingCount)
                {
                    throw new InvalidOperationException(
                        $"Rule pool component '{component.Id}' expected {component.ExpectedPrintingCount} printings, " +
                        $"but found {selected.Length}.");
                }
                foreach (PrintingDefinition printing in selected)
                {
                    if (!usedPrintingIds.Add(printing.Id))
                        throw new InvalidOperationException($"Pool '{definition.Id}' selects printing '{printing.Id}' more than once.");
                    entries.Add(new WeightedPoolEntry(printing.Id, component.TotalWeight / selected.Length));
                }
            }
            return new WeightedPool(poolId, entries);
        }

        private static bool Matches(
            UniversalCatalog catalog,
            PrintingDefinition printing,
            PrintingFilterDefinition filter)
        {
            if (filter == null) return false;
            VariantDefinition variant = catalog.Variants[printing.Identity.VariantId];
            CollectibleItemDefinition item = catalog.Items[printing.ItemId];
            return MatchesSuffix(printing.RarityId, filter.RaritySlugs) &&
                   ContainsAll(variant.Traits, filter.RequiredTraits) &&
                   ContainsNone(variant.Traits, filter.ExcludedTraits) &&
                   MatchesAny(item.Category, filter.ItemCategories) &&
                   MatchesNone(item.Category, filter.ExcludedItemCategories) &&
                   MatchesAny(printing.Identity.CardNumber, filter.CardNumbers) &&
                   MatchesNone(printing.Identity.CardNumber, filter.ExcludedCardNumbers) &&
                   MatchesNames(item.Names.Values, filter.ItemNames, filter.ItemNameSuffixes, false) &&
                   MatchesNames(item.Names.Values, filter.ExcludedItemNames, filter.ExcludedItemNameSuffixes, true);
        }

        private static bool MatchesSuffix(string value, IReadOnlyCollection<string> suffixes)
        {
            return suffixes == null || suffixes.Count == 0 || suffixes.Any(suffix =>
                !string.IsNullOrWhiteSpace(suffix) &&
                value.EndsWith(":" + suffix.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        private static bool ContainsAll(IReadOnlyCollection<string> values, IReadOnlyCollection<string> required)
        {
            return required == null || required.Count == 0 || required.All(item =>
                values.Any(value => string.Equals(value, item, StringComparison.OrdinalIgnoreCase)));
        }

        private static bool ContainsNone(IReadOnlyCollection<string> values, IReadOnlyCollection<string> excluded)
        {
            return excluded == null || excluded.Count == 0 || excluded.All(item =>
                values.All(value => !string.Equals(value, item, StringComparison.OrdinalIgnoreCase)));
        }

        private static bool MatchesAny(string value, IReadOnlyCollection<string> allowed)
        {
            return allowed == null || allowed.Count == 0 || allowed.Any(item =>
                string.Equals(value, item, StringComparison.OrdinalIgnoreCase));
        }

        private static bool MatchesNone(string value, IReadOnlyCollection<string> excluded)
        {
            return excluded == null || excluded.Count == 0 || excluded.All(item =>
                !string.Equals(value, item, StringComparison.OrdinalIgnoreCase));
        }

        private static bool MatchesNames(
            IEnumerable<string> names,
            IReadOnlyCollection<string> exact,
            IReadOnlyCollection<string> suffixes,
            bool exclusion)
        {
            bool hasRule = exact != null && exact.Count > 0 || suffixes != null && suffixes.Count > 0;
            if (!hasRule) return true;
            bool matched = names.Any(name =>
                exact != null && exact.Any(value => string.Equals(name, value, StringComparison.OrdinalIgnoreCase)) ||
                suffixes != null && suffixes.Any(value => name.EndsWith(value, StringComparison.OrdinalIgnoreCase)));
            return exclusion ? !matched : matched;
        }

        private static string Id(string prefix, string suffix, string label)
        {
            Require(suffix, label + " id");
            return prefix + ":" + suffix.Trim();
        }

        private static void Require(string value, string label)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException("A product rule needs an explicit " + label + ".");
        }

        private sealed class PrintingIdComparer : IEqualityComparer<PrintingDefinition>
        {
            public bool Equals(PrintingDefinition left, PrintingDefinition right) =>
                ReferenceEquals(left, right) || left != null && right != null &&
                string.Equals(left.Id, right.Id, StringComparison.Ordinal);

            public int GetHashCode(PrintingDefinition value) =>
                value == null ? 0 : StringComparer.Ordinal.GetHashCode(value.Id);
        }
    }
}
