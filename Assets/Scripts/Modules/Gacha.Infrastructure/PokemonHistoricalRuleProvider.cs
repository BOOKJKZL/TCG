using System;
using System.Collections.Generic;
using System.Linq;
using Gacha.Application;
using Gacha.Domain;

namespace Gacha.Infrastructure.Rules
{
    public sealed class PokemonHistoricalRuleProvider : IProductRuleProvider
    {
        public const string BaseSetId = "pokemon-tcg:set:base1";
        public const string BaseSetProfileId = "pokemon-base1-unlimited-empirical-v1";
        public const string BaseSetStudyUrl = "https://www.cs.sjsu.edu/~stamp/cv/papers/pokemon.pdf";
        public const string MachampSourceUrl = "https://www.pokebeach.com/tcg/base-set/theme-decks";

        public ProductRuleProfile GetProfile(UniversalCatalog catalog, string productId, string languageId = null)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            if (!catalog.Products.TryGetValue(productId, out ProductDefinition product))
                throw new ArgumentException($"Unknown product '{productId}'.", nameof(productId));
            if (!string.Equals(product.SetId, BaseSetId, StringComparison.Ordinal) ||
                !string.Equals(languageId, "en", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            PrintingDefinition[] eligible = product.EligiblePrintingIds
                .Select(id => catalog.Printings[id])
                .Where(printing =>
                    string.Equals(printing.Identity.LanguageId, "en", StringComparison.OrdinalIgnoreCase) &&
                    !HasTrait(catalog, printing, "first-edition") &&
                    !HasTrait(catalog, printing, "w-promo"))
                .ToArray();
            PrintingDefinition[] commons = eligible.Where(printing =>
                IsRarity(printing, "common") && !IsEnergy(catalog, printing)).ToArray();
            PrintingDefinition[] energies = eligible.Where(printing =>
                IsRarity(printing, "common") && IsEnergy(catalog, printing)).ToArray();
            PrintingDefinition[] uncommons = eligible.Where(printing =>
                IsRarity(printing, "uncommon")).ToArray();
            PrintingDefinition[] holoRares = eligible.Where(printing =>
                IsRarity(printing, "rare") &&
                HasTrait(catalog, printing, "holo") &&
                !string.Equals(printing.Identity.CardNumber, "8", StringComparison.Ordinal)).ToArray();
            PrintingDefinition[] nonHoloRares = eligible.Where(printing =>
                IsRarity(printing, "rare") && HasTrait(catalog, printing, "normal")).ToArray();

            RequireCount(commons, 32, "non-energy Common");
            RequireCount(energies, 6, "Basic Energy");
            RequireCount(uncommons, 32, "Uncommon");
            RequireCount(holoRares, 15, "booster-eligible Holo Rare");
            RequireCount(nonHoloRares, 16, "non-Holo Rare");

            string prefix = product.Id + ":historical:base1-unlimited";
            var commonPool = Pool(prefix + ":pool:common", commons, 1d);
            var energyPool = Pool(prefix + ":pool:energy", energies, 1d);
            var uncommonPool = Pool(prefix + ":pool:uncommon", uncommons, 1d);
            var rareEntries = holoRares
                .Select(printing => new WeightedPoolEntry(printing.Id, 16d))
                .Concat(nonHoloRares.Select(printing => new WeightedPoolEntry(printing.Id, 30d)));
            var rarePool = new WeightedPool(prefix + ":pool:rare", rareEntries);
            var rules = new ProductDrawRules(
                product.Id,
                new[] { commonPool, energyPool, uncommonPool, rarePool },
                new[]
                {
                    new SlotRule(prefix + ":slot:common", commonPool.Id, 5, 0, false),
                    new SlotRule(prefix + ":slot:energy", energyPool.Id, 2, 5, false),
                    new SlotRule(prefix + ":slot:uncommon", uncommonPool.Id, 3, 7, false),
                    new SlotRule(prefix + ":slot:rare", rarePool.Id, 1, 10, true)
                });
            return new ProductRuleProfile(
                BaseSetProfileId,
                rules,
                ProductRuleTrust.HistoricallyVerified,
                new[] { BaseSetStudyUrl, MachampSourceUrl });
        }

        private static WeightedPool Pool(
            string id,
            IEnumerable<PrintingDefinition> printings,
            double weight)
        {
            return new WeightedPool(
                id,
                printings.Select(printing => new WeightedPoolEntry(printing.Id, weight)));
        }

        private static bool HasTrait(
            UniversalCatalog catalog,
            PrintingDefinition printing,
            string trait)
        {
            return catalog.Variants[printing.Identity.VariantId].Traits.Any(value =>
                string.Equals(value, trait, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsEnergy(UniversalCatalog catalog, PrintingDefinition printing)
        {
            return string.Equals(
                catalog.Items[printing.ItemId].Category,
                "Energy",
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRarity(PrintingDefinition printing, string raritySlug)
        {
            return printing.RarityId.EndsWith(":" + raritySlug, StringComparison.OrdinalIgnoreCase);
        }

        private static void RequireCount(
            IReadOnlyCollection<PrintingDefinition> printings,
            int expected,
            string label)
        {
            if (printings.Count != expected)
            {
                throw new InvalidOperationException(
                    $"Base Set historical rules expected {expected} {label} printings, but found {printings.Count}.");
            }
        }
    }
}
