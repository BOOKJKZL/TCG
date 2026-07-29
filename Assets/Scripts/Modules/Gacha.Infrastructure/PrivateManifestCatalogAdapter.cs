using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using Gacha.Domain;

namespace Gacha.Infrastructure.Content
{
    public interface IImportedCardVariantPolicy
    {
        ImportedCardVariantsDto Resolve(string setId, ImportedCardDto card);
    }

    public sealed class PrivateCatalogImportResult
    {
        public PrivateCatalogImportResult(
            UniversalCatalog catalog,
            int sourceSetCount,
            int sourceCardCount,
            IReadOnlyList<string> warnings)
        {
            Catalog = catalog;
            SourceSetCount = sourceSetCount;
            SourceCardCount = sourceCardCount;
            Warnings = warnings;
        }

        public UniversalCatalog Catalog { get; }
        public int SourceSetCount { get; }
        public int SourceCardCount { get; }
        public int PrintingCount => Catalog.Printings.Count;
        public IReadOnlyList<string> Warnings { get; }
    }

    public sealed class PrivateManifestCatalogAdapter
    {
        private readonly IImportedCardVariantPolicy variantPolicy;

        public PrivateManifestCatalogAdapter(IImportedCardVariantPolicy variantPolicy = null)
        {
            this.variantPolicy = variantPolicy;
        }

        private class NamedAccumulator
        {
            public readonly Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class SetAccumulator : NamedAccumulator
        {
            public string Id;
            public string SeriesId;
            public DateTime? ReleaseDate;
            public string SetCode;
            public string EraId;
            public string GenerationId;
            public int? GenerationOrder;
            public int? SetOrdinal;
        }

        private sealed class ItemAccumulator : NamedAccumulator
        {
            public string Id;
            public string Category;
            public readonly HashSet<string> Tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class RarityAccumulator : NamedAccumulator
        {
            public string Id;
        }

        private sealed class VariantAccumulator : NamedAccumulator
        {
            public string Id;
            public readonly HashSet<string> Traits = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class PendingPrinting
        {
            public string Id;
            public string ItemId;
            public PrintingIdentity Identity;
            public string RarityId;
            public Dictionary<string, string> Names;
            public string ImageRelativePath;
            public string ImageSha256;
        }

        public PrivateCatalogImportResult Build(
            IEnumerable<PrivateContentManifestDocument> documents,
            string gameId = "pokemon-tcg",
            string gameDisplayName = "Pokémon Trading Card Game")
        {
            PrivateContentManifestDocument[] source = (documents ?? throw new ArgumentNullException(nameof(documents)))
                .OrderBy(document => document.ManifestPath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (source.Length == 0)
                throw new ArgumentException("At least one manifest is required.", nameof(documents));
            if (string.IsNullOrWhiteSpace(gameId))
                throw new ArgumentException("Game id cannot be empty.", nameof(gameId));

            gameId = gameId.Trim();
            Dictionary<string, LanguageDefinition> languages = new Dictionary<string, LanguageDefinition>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, SetAccumulator> sets = new Dictionary<string, SetAccumulator>(StringComparer.Ordinal);
            Dictionary<string, ItemAccumulator> items = new Dictionary<string, ItemAccumulator>(StringComparer.Ordinal);
            Dictionary<string, RarityAccumulator> rarities = new Dictionary<string, RarityAccumulator>(StringComparer.Ordinal);
            Dictionary<string, VariantAccumulator> variants = new Dictionary<string, VariantAccumulator>(StringComparer.Ordinal);
            List<PendingPrinting> pendingPrintings = new List<PendingPrinting>();
            List<string> warnings = new List<string>();

            foreach (PrivateContentManifestDocument document in source)
            {
                PrivateContentManifestDto manifest = document.Manifest;
                string languageId = manifest.Language.Trim();
                if (!languages.ContainsKey(languageId))
                    languages.Add(languageId, new LanguageDefinition(languageId, Name(languageId, languageId)));

                string setId = Id(gameId, "set", manifest.Set.Id);
                if (!sets.TryGetValue(setId, out SetAccumulator set))
                {
                    set = new SetAccumulator
                    {
                        Id = setId,
                        SeriesId = manifest.Set.SeriesId,
                        ReleaseDate = ParseDate(manifest.Set.ReleaseDate),
                        SetCode = ValueOrFallback(manifest.Set.SetCode, manifest.Set.Id),
                        EraId = ValueOrFallback(
                            manifest.Set.EraId,
                            ValueOrFallback(manifest.Set.SeriesId, manifest.Set.Id)),
                        GenerationId = manifest.Set.GenerationId,
                        GenerationOrder = manifest.Set.GenerationOrder,
                        SetOrdinal = manifest.Set.SetOrdinal
                    };
                    sets.Add(setId, set);
                }
                set.Names[languageId] = ValueOrFallback(manifest.Set.Name, manifest.Set.Id);

                foreach (ContentImportErrorDto error in manifest.Errors)
                    warnings.Add($"{manifest.Set.Id}/{error.ItemId}: {error.Message}");

                foreach (ImportedCardDto card in manifest.Cards)
                {
                    if (card == null || string.IsNullOrWhiteSpace(card.LocalId) || string.IsNullOrWhiteSpace(card.Id))
                    {
                        warnings.Add($"{manifest.Set.Id}: skipped a card with no id or local number.");
                        continue;
                    }

                    string itemId = Id(gameId, "item", manifest.Set.Id, card.LocalId);
                    if (!items.TryGetValue(itemId, out ItemAccumulator item))
                    {
                        item = new ItemAccumulator
                        {
                            Id = itemId,
                            Category = ValueOrFallback(card.Category, "collectible")
                        };
                        items.Add(itemId, item);
                    }
                    item.Names[languageId] = ValueOrFallback(card.Name, card.Id);
                    foreach (string type in card.Types ?? Enumerable.Empty<string>())
                        if (!string.IsNullOrWhiteSpace(type)) item.Tags.Add(type.Trim());

                    string raritySlug = Slug(ValueOrFallback(card.Rarity, "unspecified"));
                    string rarityId = Id(gameId, "rarity", raritySlug);
                    if (!rarities.TryGetValue(rarityId, out RarityAccumulator rarity))
                    {
                        rarity = new RarityAccumulator { Id = rarityId };
                        rarities.Add(rarityId, rarity);
                    }
                    rarity.Names[languageId] = ValueOrFallback(card.Rarity, "Unspecified");

                    ImportedCardVariantsDto resolvedVariants =
                        variantPolicy?.Resolve(manifest.Set.Id, card) ?? card.Variants;
                    foreach (VariantDescriptor descriptor in ExpandVariants(resolvedVariants))
                    {
                        string variantId = Id(gameId, "variant", descriptor.Id);
                        if (!variants.TryGetValue(variantId, out VariantAccumulator variant))
                        {
                            variant = new VariantAccumulator { Id = variantId };
                            variants.Add(variantId, variant);
                        }
                        variant.Names[languageId] = descriptor.DisplayName;
                        foreach (string trait in descriptor.Traits) variant.Traits.Add(trait);

                        string printingId = Id(gameId, "printing", manifest.Set.Id, card.LocalId, languageId, descriptor.Id);
                        pendingPrintings.Add(new PendingPrinting
                        {
                            Id = printingId,
                            ItemId = itemId,
                            Identity = new PrintingIdentity(gameId, setId, card.LocalId, languageId, variantId),
                            RarityId = rarityId,
                            Names = Name(languageId, ValueOrFallback(card.Name, card.Id)),
                            ImageRelativePath = ContentPath(languageId, manifest.Set.Id, card.ImageRelativePath),
                            ImageSha256 = card.ImageSha256
                        });
                    }
                }
            }

            Dictionary<string, string> gameNames = languages.Keys.ToDictionary(
                languageId => languageId,
                languageId => gameDisplayName,
                StringComparer.OrdinalIgnoreCase);
            GameDefinition game = new GameDefinition(gameId, gameNames, languages.Keys);
            SetDefinition[] setDefinitions = sets.Values
                .Select(set => new SetDefinition(
                    set.Id,
                    gameId,
                    set.Names,
                    set.SeriesId,
                    set.ReleaseDate,
                    new SetOrderingMetadata(
                        set.SetCode,
                        set.EraId,
                        set.GenerationId,
                        set.GenerationOrder,
                        set.SetOrdinal)))
                .ToArray();
            CollectibleItemDefinition[] itemDefinitions = items.Values
                .Select(item => new CollectibleItemDefinition(item.Id, gameId, item.Names, item.Category, item.Tags))
                .ToArray();
            RarityDefinition[] rarityDefinitions = rarities.Values
                .OrderBy(rarity => rarity.Id, StringComparer.Ordinal)
                .Select((rarity, rank) => new RarityDefinition(rarity.Id, gameId, rarity.Names, rank))
                .ToArray();
            VariantDefinition[] variantDefinitions = variants.Values
                .Select(variant => new VariantDefinition(variant.Id, gameId, variant.Names, variant.Traits))
                .ToArray();
            PrintingDefinition[] printingDefinitions = pendingPrintings
                .Select(printing => new PrintingDefinition(
                    printing.Id,
                    printing.ItemId,
                    printing.Identity,
                    printing.RarityId,
                    printing.Names,
                    printing.ImageRelativePath,
                    printing.ImageSha256))
                .ToArray();

            ProductDefinition[] products = setDefinitions.Select(set =>
            {
                string language = set.Names.Keys.First();
                Dictionary<string, string> productNames = set.Names.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value + " Booster",
                    StringComparer.OrdinalIgnoreCase);
                string[] eligible = printingDefinitions
                    .Where(printing => printing.Identity.SetId == set.Id)
                    .Select(printing => printing.Id)
                    .ToArray();
                return new ProductDefinition(
                    Id(gameId, "product", set.Id, "default-booster"),
                    gameId,
                    set.Id,
                    productNames.Count == 0 ? Name(language, "Booster") : productNames,
                    "simulated-booster-pack",
                    eligible);
            }).ToArray();

            UniversalCatalog catalog = new UniversalCatalog(
                languages.Values,
                new[] { game },
                setDefinitions,
                itemDefinitions,
                rarityDefinitions,
                variantDefinitions,
                printingDefinitions,
                products);

            return new PrivateCatalogImportResult(
                catalog,
                sets.Count,
                items.Count,
                new ReadOnlyCollection<string>(warnings));
        }

        private sealed class VariantDescriptor
        {
            public string Id;
            public string DisplayName;
            public string[] Traits;
        }

        private static IEnumerable<VariantDescriptor> ExpandVariants(ImportedCardVariantsDto source)
        {
            source ??= new ImportedCardVariantsDto();
            List<string> finishes = new List<string>();
            if (source.Normal) finishes.Add("normal");
            if (source.Reverse) finishes.Add("reverse");
            if (source.Holo) finishes.Add("holo");
            if (finishes.Count == 0) finishes.Add("normal");

            List<string> editions = new List<string> { null };
            if (source.FirstEdition) editions.Add("first-edition");
            if (source.WPromo) editions.Add("w-promo");

            foreach (string finish in finishes)
            foreach (string edition in editions)
            {
                string id = edition == null ? finish : finish + "-" + edition;
                string[] traits = edition == null ? new[] { finish } : new[] { finish, edition };
                yield return new VariantDescriptor
                {
                    Id = id,
                    DisplayName = string.Join(" ", id.Split('-').Select(TitleCase)),
                    Traits = traits
                };
            }
        }

        private static string TitleCase(string value)
        {
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value);
        }

        private static DateTime? ParseDate(string value)
        {
            return DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date)
                ? date
                : (DateTime?)null;
        }

        private static Dictionary<string, string> Name(string languageId, string value)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [languageId] = ValueOrFallback(value, "Unnamed")
            };
        }

        private static string ValueOrFallback(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static string ContentPath(string languageId, string setId, string imageRelativePath)
        {
            if (string.IsNullOrWhiteSpace(imageRelativePath)) return null;
            return $"{languageId}/{setId}/{imageRelativePath.Replace('\\', '/').TrimStart('/')}";
        }

        private static string Id(params string[] parts)
        {
            return string.Join(":", parts.Select(Slug));
        }

        private static string Slug(string value)
        {
            string normalized = ValueOrFallback(value, "unknown").Trim().ToLowerInvariant();
            StringBuilder result = new StringBuilder(normalized.Length);
            bool previousWasSeparator = false;
            foreach (char character in normalized)
            {
                if (char.IsLetterOrDigit(character))
                {
                    result.Append(character);
                    previousWasSeparator = false;
                }
                else if (!previousWasSeparator && result.Length > 0)
                {
                    result.Append('-');
                    previousWasSeparator = true;
                }
            }
            string slug = result.ToString().Trim('-');
            return slug.Length == 0 ? "unknown" : slug;
        }
    }
}
