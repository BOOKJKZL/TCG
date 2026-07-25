using System;
using System.Collections.Generic;
using System.Linq;
using Gacha.Application;
using Gacha.Domain;

namespace Gacha.Infrastructure.Rules
{
    public sealed class PokemonModernRuleProvider : IProductRuleProvider
    {
        public const string SwordShieldSetId = "pokemon-tcg:set:swsh1";
        public const string SwordShieldProfileId = "pokemon-swsh1-sourced-simulation-v1";
        public const string OfficialBoosterSupportUrl =
            "https://support.pokemon.com/hc/en-us/articles/360000981613-What-can-I-expect-in-a-Pok%C3%A9mon-Trading-Card-Game-booster-pack";
        public const string EliteFourumPullRateUrl =
            "https://www.elitefourum.com/t/pull-rates-in-sun-moon-sword-shield-sets/25220";
        public const string CardCodexPullRateUrl =
            "https://cardcodex.com/pokemon/sword-shield/sword-shield-base/";
        public const string ScarletVioletSetId = "pokemon-tcg:set:sv01";
        public const string ScarletVioletProfileId = "pokemon-sv01-sourced-simulation-v1";
        public const string TcgPlayerScarletVioletPullRateUrl =
            "https://www.tcgplayer.com/content/article/Pok%C3%A9mon-TCG-Scarlet-Violet-Pull-Rates/a7702fce-dd64-4a58-beb1-0f871c853215/";

        private static readonly DateTime EvidenceCheckedOn = new DateTime(2026, 7, 25);

        public ProductRuleProfile GetProfile(
            UniversalCatalog catalog,
            string productId,
            string languageId = null)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            if (!catalog.Products.TryGetValue(productId, out ProductDefinition product))
                throw new ArgumentException($"Unknown product '{productId}'.", nameof(productId));
            if (!string.Equals(languageId, "en", StringComparison.OrdinalIgnoreCase))
                return null;
            if (string.Equals(product.SetId, SwordShieldSetId, StringComparison.Ordinal))
                return BuildSwordShieldBase(catalog, product);
            if (string.Equals(product.SetId, ScarletVioletSetId, StringComparison.Ordinal))
                return BuildScarletVioletBase(catalog, product);
            return null;
        }

        private static ProductRuleProfile BuildSwordShieldBase(
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
            PrintingDefinition[] regularHoloRares = eligible.Where(printing =>
                HasTrait(catalog, printing, "holo") &&
                (IsRarity(printing, "holo-rare") || IsCinderaceHoloCorrection(catalog, printing))).ToArray();
            PrintingDefinition[] holoRareV = eligible.Where(printing =>
                IsRarity(printing, "holo-rare-v") && HasTrait(catalog, printing, "holo")).ToArray();
            PrintingDefinition[] holoRareVMax = eligible.Where(printing =>
                IsRarity(printing, "holo-rare-vmax") &&
                HasTrait(catalog, printing, "holo") &&
                !IsCinderaceHoloCorrection(catalog, printing)).ToArray();
            PrintingDefinition[] ultraRares = eligible.Where(printing =>
                IsRarity(printing, "ultra-rare") && HasTrait(catalog, printing, "holo")).ToArray();
            PrintingDefinition[] secretRares = eligible.Where(printing =>
                IsRarity(printing, "secret-rare") && HasTrait(catalog, printing, "holo")).ToArray();

            RequireCount(commons, 60, "Sword & Shield Common");
            RequireCount(uncommons, 56, "Sword & Shield Uncommon");
            RequireCount(reverses, 164, "Sword & Shield Reverse Holo");
            RequireCount(nonHoloRares, 32, "Sword & Shield non-Holo Rare");
            RequireCount(regularHoloRares, 17, "Sword & Shield regular Holo Rare");
            RequireCount(holoRareV, 17, "Sword & Shield Holo Rare V");
            RequireCount(holoRareVMax, 4, "Sword & Shield Holo Rare VMAX");
            RequireCount(ultraRares, 16, "Sword & Shield Ultra Rare");
            RequireCount(secretRares, 14, "Sword & Shield Secret/Rainbow Rare");

            string prefix = product.Id + ":sourced:swsh1";
            var commonPool = Pool(prefix + ":pool:common", commons);
            var uncommonPool = Pool(prefix + ":pool:uncommon", uncommons);
            var reversePool = Pool(prefix + ":pool:reverse", reverses);
            var rareEntries = WeightedGroup(nonHoloRares, 59.52d)
                .Concat(WeightedGroup(regularHoloRares, 18.20d))
                .Concat(WeightedGroup(holoRareV, 14.20d))
                .Concat(WeightedGroup(holoRareVMax, 2.20d))
                .Concat(WeightedGroup(ultraRares, 3.74d))
                .Concat(WeightedGroup(secretRares, 2.14d));
            var rarePool = new WeightedPool(prefix + ":pool:rare", rareEntries);
            var rules = new ProductDrawRules(
                product.Id,
                new[] { commonPool, uncommonPool, reversePool, rarePool },
                new[]
                {
                    new SlotRule(prefix + ":slot:common", commonPool.Id, 5, 0, false),
                    new SlotRule(prefix + ":slot:uncommon", uncommonPool.Id, 3, 5, false),
                    new SlotRule(prefix + ":slot:reverse", reversePool.Id, 1, 8, true),
                    new SlotRule(prefix + ":slot:rare", rarePool.Id, 1, 9, true)
                });
            return new ProductRuleProfile(
                SwordShieldProfileId,
                rules,
                ProductRuleTrust.SourceInformedSimulation,
                ProductRuleConfidence.Corroborated,
                PokemonHistoricalRuleProvider.InternationalRegionId,
                Regions("International English market", "国际英文市场"),
                new[]
                {
                    Evidence("Official Pokémon booster support", OfficialBoosterSupportUrl),
                    Evidence("Elite Fourum 4,628-pack pull-rate study", EliteFourumPullRateUrl),
                    Evidence("CardCodex Sword & Shield pack breakdown", CardCodexPullRateUrl)
                },
                Descriptions(
                    "Sword & Shield sourced simulation · 5 Common / 3 Uncommon / 1 Reverse / 1 Rare · 10 collected set cards; Basic Energy and code inserts omitted",
                    "剑与盾有来源模拟 · 5 普通 / 3 非普通 / 1 反向闪 / 1 稀有 · 收藏 10 张系列卡；不计基础能量与代码卡插入物"));
        }

        private static ProductRuleProfile BuildScarletVioletBase(
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
            PrintingDefinition[] standardReverses = eligible.Where(printing =>
                HasTrait(catalog, printing, "reverse") &&
                (IsRarity(printing, "common") ||
                 IsRarity(printing, "uncommon") ||
                 IsRarity(printing, "rare"))).ToArray();
            PrintingDefinition[] regularRares = eligible.Where(printing =>
                IsRarity(printing, "rare") && HasTrait(catalog, printing, "holo")).ToArray();
            PrintingDefinition[] doubleRares = eligible.Where(printing =>
                IsRarity(printing, "double-rare") && HasTrait(catalog, printing, "holo")).ToArray();
            PrintingDefinition[] ultraRares = eligible.Where(printing =>
                IsRarity(printing, "ultra-rare") && HasTrait(catalog, printing, "holo")).ToArray();
            PrintingDefinition[] illustrationRares = eligible.Where(printing =>
                IsRarity(printing, "illustration-rare") && HasTrait(catalog, printing, "holo")).ToArray();
            PrintingDefinition[] specialIllustrationRares = eligible.Where(printing =>
                IsRarity(printing, "special-illustration-rare") && HasTrait(catalog, printing, "holo")).ToArray();
            PrintingDefinition[] hyperRares = eligible.Where(printing =>
                IsRarity(printing, "hyper-rare") && HasTrait(catalog, printing, "holo")).ToArray();

            RequireCount(commons, 105, "Scarlet & Violet Common");
            RequireCount(uncommons, 60, "Scarlet & Violet Uncommon");
            RequireCount(standardReverses, 186, "Scarlet & Violet standard Reverse Holo");
            RequireCount(regularRares, 21, "Scarlet & Violet regular Holo Rare");
            RequireCount(doubleRares, 12, "Scarlet & Violet Double Rare");
            RequireCount(ultraRares, 20, "Scarlet & Violet Ultra Rare");
            RequireCount(illustrationRares, 24, "Scarlet & Violet Illustration Rare");
            RequireCount(specialIllustrationRares, 10, "Scarlet & Violet Special Illustration Rare");
            RequireCount(hyperRares, 6, "Scarlet & Violet Hyper Rare");

            string prefix = product.Id + ":sourced:sv01";
            var commonPool = Pool(prefix + ":pool:common", commons);
            var uncommonPool = Pool(prefix + ":pool:uncommon", uncommons);
            var firstReversePool = Pool(prefix + ":pool:first-reverse", standardReverses);
            var secondFoilEntries = WeightedGroup(standardReverses, 87.33d)
                .Concat(WeightedGroup(illustrationRares, 7.67d))
                .Concat(WeightedGroup(specialIllustrationRares, 3.15d))
                .Concat(WeightedGroup(hyperRares, 1.85d));
            var secondFoilPool = new WeightedPool(prefix + ":pool:second-foil", secondFoilEntries);
            var rareEntries = WeightedGroup(regularRares, 79.67d)
                .Concat(WeightedGroup(doubleRares, 13.76d))
                .Concat(WeightedGroup(ultraRares, 6.57d));
            var rarePool = new WeightedPool(prefix + ":pool:rare", rareEntries);
            var rules = new ProductDrawRules(
                product.Id,
                new[] { commonPool, uncommonPool, firstReversePool, secondFoilPool, rarePool },
                new[]
                {
                    new SlotRule(prefix + ":slot:common", commonPool.Id, 4, 0, false),
                    new SlotRule(prefix + ":slot:uncommon", uncommonPool.Id, 3, 4, false),
                    new SlotRule(prefix + ":slot:first-reverse", firstReversePool.Id, 1, 7, false),
                    new SlotRule(prefix + ":slot:second-foil", secondFoilPool.Id, 1, 8, false),
                    new SlotRule(prefix + ":slot:rare", rarePool.Id, 1, 9, false)
                });
            return new ProductRuleProfile(
                ScarletVioletProfileId,
                rules,
                ProductRuleTrust.SourceInformedSimulation,
                ProductRuleConfidence.Corroborated,
                PokemonHistoricalRuleProvider.InternationalRegionId,
                Regions("International English market", "国际英文市场"),
                new[]
                {
                    Evidence("Official Pokémon booster support", OfficialBoosterSupportUrl),
                    Evidence("TCGplayer 8,000+ pack pull-rate study", TcgPlayerScarletVioletPullRateUrl)
                },
                Descriptions(
                    "Scarlet & Violet sourced simulation · 4 Common / 3 Uncommon / 2 foil slots / 1 Rare-or-higher · 10 collected set cards; Basic Energy and code inserts omitted",
                    "朱与紫有来源模拟 · 4 普通 / 3 非普通 / 2 闪卡位 / 1 稀有以上 · 收藏 10 张系列卡；不计基础能量与代码卡插入物"));
        }

        private static WeightedPool Pool(string id, IEnumerable<PrintingDefinition> printings)
        {
            return new WeightedPool(
                id,
                printings.Select(printing => new WeightedPoolEntry(printing.Id, 1d)));
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

        private static bool IsCinderaceHoloCorrection(
            UniversalCatalog catalog,
            PrintingDefinition printing)
        {
            string number = printing.Identity.CardNumber;
            return (string.Equals(number, "34", StringComparison.Ordinal) ||
                    string.Equals(number, "35", StringComparison.Ordinal)) &&
                   catalog.Items[printing.ItemId].Names.Values.Any(name =>
                       string.Equals(name, "Cinderace", StringComparison.OrdinalIgnoreCase));
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

        private static ProductRuleEvidence Evidence(string title, string sourceReference)
        {
            return new ProductRuleEvidence(title, sourceReference, EvidenceCheckedOn);
        }

        private static void RequireCount(
            IReadOnlyCollection<PrintingDefinition> printings,
            int expected,
            string label)
        {
            if (printings.Count != expected)
            {
                throw new InvalidOperationException(
                    $"Sourced rules expected {expected} {label} printings, but found {printings.Count}.");
            }
        }
    }
}
