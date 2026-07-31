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
            public PrintingLanguageGroupRecordDto LanguageGroup;
        }

        public PrivateCatalogImportResult Build(
            IEnumerable<PrivateContentManifestDocument> documents,
            string gameId = "pokemon-tcg",
            string gameDisplayName = "Pokémon Trading Card Game",
            PrintingLanguageGroupManifestDto languageGroupManifest = null)
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
            Dictionary<string, PrintingLanguageGroupRecordDto> sourceLanguageGroups =
                IndexLanguageGroups(languageGroupManifest);

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

                    string sourceCardKey = PrintingLanguageGroupManifestReader.SourceKey(
                        languageId, manifest.Set.Id, card.Id, card.LocalId);
                    sourceLanguageGroups.TryGetValue(
                        sourceCardKey, out PrintingLanguageGroupRecordDto languageGroup);
                    // A Set id and local number are not a safe cross-region card identity.
                    // Keep source cards distinct; only the reviewed runtime overlay may link
                    // their printings for language switching.
                    string itemId = Id(
                        gameId,
                        "item",
                        languageId,
                        manifest.Set.Id,
                        card.Id,
                        card.LocalId);
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
                            ImageSha256 = card.ImageSha256,
                            LanguageGroup = languageGroup
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
            PrintingLanguageGroupDefinition[] languageGroupDefinitions =
                BuildLanguageGroupDefinitions(pendingPrintings, gameId);

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
                products,
                languageGroupDefinitions);

            return new PrivateCatalogImportResult(
                catalog,
                sets.Count,
                items.Count,
                new ReadOnlyCollection<string>(warnings));
        }

        private static Dictionary<string, PrintingLanguageGroupRecordDto> IndexLanguageGroups(
            PrintingLanguageGroupManifestDto manifest)
        {
            var result = new Dictionary<string, PrintingLanguageGroupRecordDto>(StringComparer.Ordinal);
            if (manifest == null)
                return result;
            foreach (PrintingLanguageGroupRecordDto group in manifest.Groups ??
                     new List<PrintingLanguageGroupRecordDto>())
            foreach (PrintingLanguageGroupMemberDto member in group.Members ??
                     new List<PrintingLanguageGroupMemberDto>())
            {
                string key = PrintingLanguageGroupManifestReader.SourceKey(
                    member.Language, member.SetId, member.CardId, member.LocalId);
                if (!result.TryAdd(key, group))
                    throw new PrivateContentManifestException(
                        $"Printing language group source card '{key}' is duplicated.");
            }
            return result;
        }

        private static PrintingLanguageGroupDefinition[] BuildLanguageGroupDefinitions(
            IEnumerable<PendingPrinting> printings,
            string gameId)
        {
            var result = new List<PrintingLanguageGroupDefinition>();
            foreach (IGrouping<string, PendingPrinting> sourceGroup in printings
                         .Where(value => value.LanguageGroup != null)
                         .GroupBy(value => value.LanguageGroup.Id, StringComparer.Ordinal)
                         .OrderBy(value => value.Key, StringComparer.Ordinal))
            {
                IGrouping<string, PendingPrinting>[] languages = sourceGroup
                    .GroupBy(value => value.Identity.LanguageId, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value.Key, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (languages.Length < 2)
                    continue;

                PrintingLanguageGroupRecordDto source = sourceGroup.First().LanguageGroup;
                string[] commonVariants = languages
                    .Select(language => new HashSet<string>(
                        language.Select(value => value.Identity.VariantId), StringComparer.Ordinal))
                    .Aggregate((left, right) =>
                    {
                        left.IntersectWith(right);
                        return left;
                    })
                    .OrderBy(VariantRank)
                    .ThenBy(value => value, StringComparer.Ordinal)
                    .ToArray();
                string primaryVariant = commonVariants.FirstOrDefault();
                PendingPrinting[] primary = languages.Select(language => language
                        .OrderBy(value => primaryVariant == null ||
                                          value.Identity.VariantId != primaryVariant)
                        .ThenBy(value => VariantRank(value.Identity.VariantId))
                        .ThenBy(value => value.Identity.VariantId, StringComparer.Ordinal)
                        .ThenBy(value => value.Id, StringComparer.Ordinal)
                        .First())
                    .ToArray();
                AddLanguageGroupDefinition(
                    result, gameId, source, "primary", primary);

                var claimed = new HashSet<string>(primary.Select(value => value.Id), StringComparer.Ordinal);
                foreach (IGrouping<string, PendingPrinting> variant in sourceGroup
                             .Where(value => !claimed.Contains(value.Id))
                             .GroupBy(value => value.Identity.VariantId, StringComparer.Ordinal)
                             .OrderBy(value => VariantRank(value.Key))
                             .ThenBy(value => value.Key, StringComparer.Ordinal))
                {
                    PendingPrinting[] members = variant
                        .OrderBy(value => value.Identity.LanguageId, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(value => value.Id, StringComparer.Ordinal)
                        .ToArray();
                    if (members.Select(value => value.Identity.LanguageId)
                            .Distinct(StringComparer.OrdinalIgnoreCase).Count() < 2)
                        continue;
                    AddLanguageGroupDefinition(result, gameId, source, variant.Key, members);
                    foreach (PendingPrinting member in members)
                        claimed.Add(member.Id);
                }
            }
            return result.ToArray();
        }

        private static void AddLanguageGroupDefinition(
            ICollection<PrintingLanguageGroupDefinition> result,
            string gameId,
            PrintingLanguageGroupRecordDto source,
            string suffix,
            IEnumerable<PendingPrinting> members)
        {
            result.Add(new PrintingLanguageGroupDefinition(
                Id(gameId, "printing-language-group", source.Id, suffix),
                members.Select(value => value.Id),
                ParseMatchMethod(source.MatchMethod),
                source.Confidence,
                ParseReviewStatus(source.ReviewStatus)));
        }

        private static int VariantRank(string variantId)
        {
            string value = variantId ?? string.Empty;
            if (value.EndsWith(":normal", StringComparison.Ordinal)) return 0;
            if (value.EndsWith(":holo", StringComparison.Ordinal)) return 1;
            if (value.EndsWith(":reverse", StringComparison.Ordinal)) return 2;
            return 3;
        }

        private static PrintingLanguageMatchMethod ParseMatchMethod(string value)
        {
            return string.Equals(value, "manual-override", StringComparison.Ordinal)
                ? PrintingLanguageMatchMethod.ManualOverride
                : PrintingLanguageMatchMethod.SourceIdentity;
        }

        private static PrintingLanguageReviewStatus ParseReviewStatus(string value)
        {
            return string.Equals(value, "reviewed", StringComparison.Ordinal)
                ? PrintingLanguageReviewStatus.Reviewed
                : PrintingLanguageReviewStatus.AutoAccepted;
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
