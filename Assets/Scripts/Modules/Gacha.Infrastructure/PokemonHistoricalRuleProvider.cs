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
        public const string ExRubySapphireSetId = "pokemon-tcg:set:ex1";
        public const string ExRubySapphireProfileId = "pokemon-ex1-psa-empirical-v1";
        public const string ExRubySapphireSourceUrl =
            "https://www.psacard.com/articles/articleview/9800/psa-set-registry-collecting-2003-poke-mon-ex-ruby-sapphire-first-nintendo-card-issue";
        public const string NeoGenesisSetId = "pokemon-tcg:set:neo1";
        public const string NeoGenesisProfileId = "pokemon-neo1-first-edition-psa-v1";
        public const string NeoGenesisSourceUrl = "https://www.psacard.com/articles/articleview/9409/public/locales";
        public const string InternationalRegionId = "pokemon-international-en";

        private static readonly DateTime OriginalEvidenceCheckedOn = new DateTime(2026, 7, 23);
        private static readonly DateTime ExEvidenceCheckedOn = new DateTime(2026, 7, 25);

        public ProductRuleProfile GetProfile(UniversalCatalog catalog, string productId, string languageId = null)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            if (!catalog.Products.TryGetValue(productId, out ProductDefinition product))
                throw new ArgumentException($"Unknown product '{productId}'.", nameof(productId));
            if (!string.Equals(languageId, "en", StringComparison.OrdinalIgnoreCase))
                return null;

            if (string.Equals(product.SetId, BaseSetId, StringComparison.Ordinal))
                return BuildBaseSetUnlimited(catalog, product);
            if (string.Equals(product.SetId, ExRubySapphireSetId, StringComparison.Ordinal))
                return BuildExRubySapphire(catalog, product);
            if (string.Equals(product.SetId, NeoGenesisSetId, StringComparison.Ordinal))
                return BuildNeoGenesisFirstEdition(catalog, product);
            return null;
        }

        private static ProductRuleProfile BuildBaseSetUnlimited(
            UniversalCatalog catalog,
            ProductDefinition product)
        {
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
                ProductRuleConfidence.Corroborated,
                InternationalRegionId,
                Regions("International English market", "国际英文市场"),
                new[]
                {
                    Evidence("SJSU Base Set empirical study", BaseSetStudyUrl, OriginalEvidenceCheckedOn),
                    Evidence("PokéBeach Base Set theme deck reference", MachampSourceUrl, OriginalEvidenceCheckedOn)
                },
                Descriptions(
                    "Base Set Unlimited · 5 Common / 2 Energy / 3 Uncommon / 1 Rare · Holo ≈ 1 in 3",
                    "Base Set 无限版 · 5 普通 / 2 能量 / 3 非普通 / 1 稀有 · 闪卡约 3 包 1 张"));
        }

        private static ProductRuleProfile BuildExRubySapphire(
            UniversalCatalog catalog,
            ProductDefinition product)
        {
            PrintingDefinition[] eligible = product.EligiblePrintingIds
                .Select(id => catalog.Printings[id])
                .Where(printing =>
                    string.Equals(printing.Identity.LanguageId, "en", StringComparison.OrdinalIgnoreCase) &&
                    !HasTrait(catalog, printing, "first-edition") &&
                    !HasTrait(catalog, printing, "w-promo"))
                .ToArray();
            PrintingDefinition[] commons = eligible.Where(printing =>
                IsRarity(printing, "common") && HasTrait(catalog, printing, "normal")).ToArray();
            PrintingDefinition[] uncommons = eligible.Where(printing =>
                IsRarity(printing, "uncommon") && HasTrait(catalog, printing, "normal")).ToArray();
            PrintingDefinition[] reverses = eligible.Where(printing =>
                HasTrait(catalog, printing, "reverse")).ToArray();
            PrintingDefinition[] nonHoloRares = eligible.Where(printing =>
                IsRarity(printing, "rare") && HasTrait(catalog, printing, "normal")).ToArray();
            PrintingDefinition[] holoRares = eligible.Where(printing =>
                IsRarity(printing, "rare") &&
                HasTrait(catalog, printing, "holo") &&
                !IsPokemonEx(catalog, printing)).ToArray();
            PrintingDefinition[] pokemonEx = eligible.Where(printing =>
                IsRarity(printing, "rare") &&
                HasTrait(catalog, printing, "holo") &&
                IsPokemonEx(catalog, printing)).ToArray();

            RequireCount(commons, 40, "EX Ruby & Sapphire Common");
            RequireCount(uncommons, 34, "EX Ruby & Sapphire Uncommon");
            RequireCount(reverses, 101, "EX Ruby & Sapphire Reverse Holo");
            RequireCount(nonHoloRares, 13, "EX Ruby & Sapphire non-Holo Rare");
            RequireCount(holoRares, 16, "EX Ruby & Sapphire regular Holo Rare");
            RequireCount(pokemonEx, 8, "EX Ruby & Sapphire Pokémon-ex");

            string prefix = product.Id + ":historical:ex1";
            var commonPool = Pool(prefix + ":pool:common", commons, 1d);
            var uncommonPool = Pool(prefix + ":pool:uncommon", uncommons, 1d);
            var reversePool = Pool(prefix + ":pool:reverse", reverses, 1d);
            var rareEntries = WeightedGroup(nonHoloRares, 53d)
                .Concat(WeightedGroup(holoRares, 13d))
                .Concat(WeightedGroup(pokemonEx, 6d));
            var rarePool = new WeightedPool(prefix + ":pool:rare", rareEntries);
            var rules = new ProductDrawRules(
                product.Id,
                new[] { commonPool, uncommonPool, reversePool, rarePool },
                new[]
                {
                    new SlotRule(prefix + ":slot:common", commonPool.Id, 5, 0, false),
                    new SlotRule(prefix + ":slot:uncommon", uncommonPool.Id, 2, 5, false),
                    new SlotRule(prefix + ":slot:reverse", reversePool.Id, 1, 7, true),
                    new SlotRule(prefix + ":slot:rare", rarePool.Id, 1, 8, true)
                });
            return new ProductRuleProfile(
                ExRubySapphireProfileId,
                rules,
                ProductRuleTrust.HistoricallyVerified,
                ProductRuleConfidence.Corroborated,
                InternationalRegionId,
                Regions("International English market", "国际英文市场"),
                new[]
                {
                    Evidence(
                        "PSA EX Ruby & Sapphire set guide",
                        ExRubySapphireSourceUrl,
                        ExEvidenceCheckedOn)
                },
                Descriptions(
                    "EX Ruby & Sapphire · 5 Common / 2 Uncommon / 1 Reverse Holo / 1 Rare · empirical Holo ≈ 6.5/36 and Pokémon-ex ≈ 3/36",
                    "EX 红宝石与蓝宝石 · 5 普通 / 2 非普通 / 1 反向闪 / 1 稀有 · 经验值：普通闪约 6.5/36、Pokémon-ex 约 3/36"));
        }

        private static ProductRuleProfile BuildNeoGenesisFirstEdition(
            UniversalCatalog catalog,
            ProductDefinition product)
        {
            PrintingDefinition[] eligible = product.EligiblePrintingIds
                .Select(id => catalog.Printings[id])
                .Where(printing =>
                    string.Equals(printing.Identity.LanguageId, "en", StringComparison.OrdinalIgnoreCase) &&
                    HasTrait(catalog, printing, "first-edition") &&
                    !HasTrait(catalog, printing, "w-promo"))
                .ToArray();
            PrintingDefinition[] commons = eligible.Where(printing =>
                IsRarity(printing, "common")).ToArray();
            PrintingDefinition[] uncommons = eligible.Where(printing =>
                IsRarity(printing, "uncommon")).ToArray();
            PrintingDefinition[] holoRares = eligible.Where(printing =>
                IsRarity(printing, "rare") && HasTrait(catalog, printing, "holo")).ToArray();
            PrintingDefinition[] nonHoloRares = eligible.Where(printing =>
                IsRarity(printing, "rare") && HasTrait(catalog, printing, "normal")).ToArray();

            RequireCount(commons, 41, "Neo Genesis First Edition Common");
            RequireCount(uncommons, 35, "Neo Genesis First Edition Uncommon");
            RequireCount(holoRares, 19, "Neo Genesis First Edition Holo Rare");
            RequireCount(nonHoloRares, 16, "Neo Genesis First Edition non-Holo Rare");

            string prefix = product.Id + ":historical:neo1-first-edition";
            var commonPool = Pool(prefix + ":pool:common", commons, 1d);
            var uncommonPool = Pool(prefix + ":pool:uncommon", uncommons, 1d);
            var rareEntries = holoRares
                .Select(printing => new WeightedPoolEntry(printing.Id, 16d))
                .Concat(nonHoloRares.Select(printing => new WeightedPoolEntry(printing.Id, 38d)));
            var rarePool = new WeightedPool(prefix + ":pool:rare", rareEntries);
            var rules = new ProductDrawRules(
                product.Id,
                new[] { commonPool, uncommonPool, rarePool },
                new[]
                {
                    new SlotRule(prefix + ":slot:common", commonPool.Id, 7, 0, false),
                    new SlotRule(prefix + ":slot:uncommon", uncommonPool.Id, 3, 7, false),
                    new SlotRule(prefix + ":slot:rare", rarePool.Id, 1, 10, true)
                });
            return new ProductRuleProfile(
                NeoGenesisProfileId,
                rules,
                ProductRuleTrust.HistoricallyVerified,
                ProductRuleConfidence.Corroborated,
                InternationalRegionId,
                Regions("International English market", "国际英文市场"),
                new[]
                {
                    Evidence("PSA Neo Genesis guide", NeoGenesisSourceUrl, OriginalEvidenceCheckedOn)
                },
                Descriptions(
                    "Neo Genesis First Edition · 7 Common / 3 Uncommon / 1 Rare · Holo ≈ 1 in 3",
                    "Neo Genesis 第一版 · 7 普通 / 3 非普通 / 1 稀有 · 闪卡约 3 包 1 张"));
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

        private static IEnumerable<WeightedPoolEntry> WeightedGroup(
            IReadOnlyCollection<PrintingDefinition> printings,
            double totalWeight)
        {
            double weightPerPrinting = totalWeight / printings.Count;
            return printings.Select(printing => new WeightedPoolEntry(printing.Id, weightPerPrinting));
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

        private static bool IsPokemonEx(UniversalCatalog catalog, PrintingDefinition printing)
        {
            return catalog.Items[printing.ItemId].Names.Values.Any(name =>
                name.EndsWith(" ex", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsRarity(PrintingDefinition printing, string raritySlug)
        {
            return printing.RarityId.EndsWith(":" + raritySlug, StringComparison.OrdinalIgnoreCase);
        }

        private static IReadOnlyDictionary<string, string> Descriptions(string english, string chinese)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = english,
                ["zh"] = chinese
            };
        }

        private static IReadOnlyDictionary<string, string> Regions(string english, string chinese)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = english,
                ["zh"] = chinese
            };
        }

        private static ProductRuleEvidence Evidence(
            string title,
            string sourceReference,
            DateTime checkedOn)
        {
            return new ProductRuleEvidence(title, sourceReference, checkedOn);
        }

        private static void RequireCount(
            IReadOnlyCollection<PrintingDefinition> printings,
            int expected,
            string label)
        {
            if (printings.Count != expected)
            {
                throw new InvalidOperationException(
                    $"Historical rules expected {expected} {label} printings, but found {printings.Count}.");
            }
        }
    }
}
