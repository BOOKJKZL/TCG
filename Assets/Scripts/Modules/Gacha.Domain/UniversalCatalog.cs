using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Gacha.Domain
{
    public sealed class UniversalCatalogValidationException : Exception
    {
        public UniversalCatalogValidationException(IReadOnlyList<string> errors)
            : base(string.Join(Environment.NewLine, errors))
        {
            Errors = errors;
        }

        public IReadOnlyList<string> Errors { get; }
    }

    public sealed class UniversalCatalog
    {
        public UniversalCatalog(
            IEnumerable<LanguageDefinition> languages,
            IEnumerable<GameDefinition> games,
            IEnumerable<SetDefinition> sets,
            IEnumerable<CollectibleItemDefinition> items,
            IEnumerable<RarityDefinition> rarities,
            IEnumerable<VariantDefinition> variants,
            IEnumerable<PrintingDefinition> printings,
            IEnumerable<ProductDefinition> products,
            IEnumerable<PrintingLanguageGroupDefinition> printingLanguageGroups = null)
        {
            Languages = Index(languages, "language");
            Games = Index(games, "game");
            Sets = Index(sets, "set");
            Items = Index(items, "item");
            Rarities = Index(rarities, "rarity");
            Variants = Index(variants, "variant");
            Printings = Index(printings, "printing");
            Products = Index(products, "product");
            PrintingLanguageGroups = new ReadOnlyCollection<PrintingLanguageGroupDefinition>(
                (printingLanguageGroups ?? Array.Empty<PrintingLanguageGroupDefinition>()).ToArray());

            IReadOnlyList<string> errors = Validate();
            if (errors.Count > 0)
            {
                throw new UniversalCatalogValidationException(errors);
            }

            PrintingLanguages = new PrintingLanguageIndex(Printings.Values, PrintingLanguageGroups);
        }

        public IReadOnlyDictionary<string, LanguageDefinition> Languages { get; }
        public IReadOnlyDictionary<string, GameDefinition> Games { get; }
        public IReadOnlyDictionary<string, SetDefinition> Sets { get; }
        public IReadOnlyDictionary<string, CollectibleItemDefinition> Items { get; }
        public IReadOnlyDictionary<string, RarityDefinition> Rarities { get; }
        public IReadOnlyDictionary<string, VariantDefinition> Variants { get; }
        public IReadOnlyDictionary<string, PrintingDefinition> Printings { get; }
        public IReadOnlyDictionary<string, ProductDefinition> Products { get; }
        public IReadOnlyList<PrintingLanguageGroupDefinition> PrintingLanguageGroups { get; }
        public PrintingLanguageIndex PrintingLanguages { get; }

        public IEnumerable<PrintingDefinition> GetPrintings(string setId, string languageId = null)
        {
            return Printings.Values.Where(printing =>
                printing.Identity.SetId == setId &&
                (languageId == null || string.Equals(printing.Identity.LanguageId, languageId, StringComparison.OrdinalIgnoreCase)));
        }

        private IReadOnlyList<string> Validate()
        {
            List<string> errors = new List<string>();

            foreach (LanguageDefinition language in Languages.Values)
            {
                if (language.FallbackLanguageId != null && !Languages.ContainsKey(language.FallbackLanguageId))
                    errors.Add($"Language '{language.Id}' references missing fallback '{language.FallbackLanguageId}'.");
            }

            foreach (GameDefinition game in Games.Values)
            {
                foreach (string languageId in game.SupportedLanguageIds)
                    if (!Languages.ContainsKey(languageId)) errors.Add($"Game '{game.Id}' references missing language '{languageId}'.");
            }

            foreach (SetDefinition set in Sets.Values)
                ValidateGame(errors, set.Id, "Set", set.GameId);
            foreach (CollectibleItemDefinition item in Items.Values)
                ValidateGame(errors, item.Id, "Item", item.GameId);
            foreach (RarityDefinition rarity in Rarities.Values)
                ValidateGame(errors, rarity.Id, "Rarity", rarity.GameId);
            foreach (VariantDefinition variant in Variants.Values)
                ValidateGame(errors, variant.Id, "Variant", variant.GameId);

            HashSet<PrintingIdentity> identities = new HashSet<PrintingIdentity>();
            foreach (PrintingDefinition printing in Printings.Values)
            {
                if (!Items.TryGetValue(printing.ItemId, out CollectibleItemDefinition item))
                    errors.Add($"Printing '{printing.Id}' references missing item '{printing.ItemId}'.");
                if (!Sets.TryGetValue(printing.Identity.SetId, out SetDefinition set))
                    errors.Add($"Printing '{printing.Id}' references missing set '{printing.Identity.SetId}'.");
                if (!Languages.ContainsKey(printing.Identity.LanguageId))
                    errors.Add($"Printing '{printing.Id}' references missing language '{printing.Identity.LanguageId}'.");
                if (!Rarities.TryGetValue(printing.RarityId, out RarityDefinition rarity))
                    errors.Add($"Printing '{printing.Id}' references missing rarity '{printing.RarityId}'.");
                if (!Variants.TryGetValue(printing.Identity.VariantId, out VariantDefinition variant))
                    errors.Add($"Printing '{printing.Id}' references missing variant '{printing.Identity.VariantId}'.");
                if (!identities.Add(printing.Identity))
                    errors.Add($"Printing identity '{printing.Identity}' is duplicated.");

                ValidateSameGame(errors, printing, item, set, rarity, variant);
            }

            foreach (ProductDefinition product in Products.Values)
            {
                ValidateGame(errors, product.Id, "Product", product.GameId);
                if (!Sets.TryGetValue(product.SetId, out SetDefinition set))
                    errors.Add($"Product '{product.Id}' references missing set '{product.SetId}'.");
                else if (set.GameId != product.GameId)
                    errors.Add($"Product '{product.Id}' and set '{set.Id}' belong to different games.");

                foreach (string printingId in product.EligiblePrintingIds)
                {
                    if (!Printings.TryGetValue(printingId, out PrintingDefinition printing))
                        errors.Add($"Product '{product.Id}' references missing printing '{printingId}'.");
                    else if (printing.Identity.GameId != product.GameId)
                        errors.Add($"Product '{product.Id}' references printing '{printingId}' from another game.");
                }
            }

            ValidatePrintingLanguageGroups(errors);

            return new ReadOnlyCollection<string>(errors);
        }

        private void ValidatePrintingLanguageGroups(List<string> errors)
        {
            var groupIds = new HashSet<string>(StringComparer.Ordinal);
            var claimedPrintings = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (PrintingLanguageGroupDefinition group in PrintingLanguageGroups)
            {
                if (group == null)
                {
                    errors.Add("The printing language group collection contains null.");
                    continue;
                }
                if (!groupIds.Add(group.Id))
                    errors.Add($"Printing language group id '{group.Id}' is duplicated.");

                var games = new HashSet<string>(StringComparer.Ordinal);
                var languages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string printingId in group.PrintingIds)
                {
                    if (!Printings.TryGetValue(printingId, out PrintingDefinition printing))
                    {
                        errors.Add($"Printing language group '{group.Id}' references missing printing '{printingId}'.");
                        continue;
                    }
                    games.Add(printing.Identity.GameId);
                    if (!languages.Add(printing.Identity.LanguageId))
                        errors.Add($"Printing language group '{group.Id}' contains more than one '{printing.Identity.LanguageId}' printing.");
                    if (claimedPrintings.TryGetValue(printingId, out string previousGroup))
                        errors.Add($"Printing '{printingId}' belongs to both language groups '{previousGroup}' and '{group.Id}'.");
                    else
                        claimedPrintings.Add(printingId, group.Id);
                }
                if (games.Count > 1)
                    errors.Add($"Printing language group '{group.Id}' combines different games.");
            }
        }

        private void ValidateGame(List<string> errors, string id, string type, string gameId)
        {
            if (!Games.ContainsKey(gameId))
                errors.Add($"{type} '{id}' references missing game '{gameId}'.");
        }

        private static void ValidateSameGame(
            List<string> errors,
            PrintingDefinition printing,
            CollectibleItemDefinition item,
            SetDefinition set,
            RarityDefinition rarity,
            VariantDefinition variant)
        {
            if (item == null || set == null || rarity == null || variant == null)
                return;

            string gameId = printing.Identity.GameId;
            if (item.GameId != gameId || set.GameId != gameId || rarity.GameId != gameId || variant.GameId != gameId)
                errors.Add($"Printing '{printing.Id}' combines definitions from different games.");
        }

        private static IReadOnlyDictionary<string, T> Index<T>(IEnumerable<T> values, string type)
            where T : Definition
        {
            Dictionary<string, T> result = new Dictionary<string, T>(StringComparer.Ordinal);
            foreach (T value in values ?? Array.Empty<T>())
            {
                if (value == null)
                    throw new ArgumentException($"The {type} collection contains null.", nameof(values));
                if (result.ContainsKey(value.Id))
                    throw new ArgumentException($"Duplicate {type} id '{value.Id}'.", nameof(values));
                result.Add(value.Id, value);
            }

            return new ReadOnlyDictionary<string, T>(result);
        }
    }
}
