using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Gacha.Domain;

namespace Gacha.Pokemon.Domain
{
    public enum PokemonFormDisposition
    {
        SeparateEntry,
        RelatedVariant,
        Excluded,
        ManualReview
    }

    public sealed class PokemonGenerationDefinition : Definition
    {
        public PokemonGenerationDefinition(
            string id,
            int order,
            IReadOnlyDictionary<string, string> names,
            int speciesStartNumber,
            int speciesEndNumber,
            string sourceUrl = null)
            : base(id, names)
        {
            if (order < 1)
                throw new ArgumentOutOfRangeException(nameof(order));
            if (speciesStartNumber < 1 || speciesEndNumber < speciesStartNumber)
                throw new ArgumentOutOfRangeException(nameof(speciesStartNumber));

            Order = order;
            SpeciesStartNumber = speciesStartNumber;
            SpeciesEndNumber = speciesEndNumber;
            SourceUrl = PokemonTaxonomyValue.Optional(sourceUrl);
        }

        public int Order { get; }
        public int SpeciesStartNumber { get; }
        public int SpeciesEndNumber { get; }
        public string SourceUrl { get; }
    }

    public sealed class PokemonSpeciesDefinition : Definition
    {
        public PokemonSpeciesDefinition(
            string id,
            int nationalDexNumber,
            string debutGenerationId,
            IReadOnlyDictionary<string, string> names,
            IReadOnlyDictionary<string, string> genera,
            IReadOnlyDictionary<string, string> descriptions,
            string defaultFormId,
            IEnumerable<string> formIds,
            bool isBaby,
            bool isLegendary,
            bool isMythical,
            string colorId = null,
            string habitatId = null,
            string sourceUrl = null)
            : base(id, names)
        {
            if (nationalDexNumber < 1)
                throw new ArgumentOutOfRangeException(nameof(nationalDexNumber));

            NationalDexNumber = nationalDexNumber;
            DebutGenerationId = PokemonTaxonomyValue.Required(debutGenerationId, nameof(debutGenerationId));
            Genera = CopyLocalized(genera);
            Descriptions = CopyLocalized(descriptions);
            DefaultFormId = PokemonTaxonomyValue.Required(defaultFormId, nameof(defaultFormId));
            FormIds = PokemonTaxonomyValue.CopyStrings(formIds);
            if (!FormIds.Contains(DefaultFormId, StringComparer.Ordinal))
                throw new ArgumentException("Species FormIds must contain DefaultFormId.", nameof(formIds));

            IsBaby = isBaby;
            IsLegendary = isLegendary;
            IsMythical = isMythical;
            ColorId = PokemonTaxonomyValue.Optional(colorId);
            HabitatId = PokemonTaxonomyValue.Optional(habitatId);
            SourceUrl = PokemonTaxonomyValue.Optional(sourceUrl);
        }

        public int NationalDexNumber { get; }
        public string DebutGenerationId { get; }
        public IReadOnlyDictionary<string, string> Genera { get; }
        public IReadOnlyDictionary<string, string> Descriptions { get; }
        public string DefaultFormId { get; }
        public IReadOnlyList<string> FormIds { get; }
        public bool IsBaby { get; }
        public bool IsLegendary { get; }
        public bool IsMythical { get; }
        public string ColorId { get; }
        public string HabitatId { get; }
        public string SourceUrl { get; }

        private static IReadOnlyDictionary<string, string> CopyLocalized(
            IReadOnlyDictionary<string, string> values)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> entry in values ??
                     new Dictionary<string, string>())
            {
                if (!string.IsNullOrWhiteSpace(entry.Key) && !string.IsNullOrWhiteSpace(entry.Value))
                    result[entry.Key.Trim()] = entry.Value.Trim();
            }
            return new ReadOnlyDictionary<string, string>(result);
        }
    }

    public sealed class PokemonFormDefinition : Definition
    {
        public PokemonFormDefinition(
            string id,
            string speciesId,
            int pokemonId,
            string formKind,
            PokemonFormDisposition disposition,
            IReadOnlyDictionary<string, string> names,
            string introducedGenerationId,
            IEnumerable<string> relatedFormIds,
            IEnumerable<string> typeIds,
            bool isDefault,
            bool isBattleOnly,
            bool isMega,
            bool isGigantamax,
            string regionId = null,
            string imageRelativePath = null,
            string imageSourceUrl = null,
            string imageSha256 = null,
            string sourceUrl = null)
            : base(id, names)
        {
            if (pokemonId < 1)
                throw new ArgumentOutOfRangeException(nameof(pokemonId));

            SpeciesId = PokemonTaxonomyValue.Required(speciesId, nameof(speciesId));
            PokemonId = pokemonId;
            FormKind = PokemonTaxonomyValue.Required(formKind, nameof(formKind));
            Disposition = disposition;
            IntroducedGenerationId = PokemonTaxonomyValue.Required(
                introducedGenerationId,
                nameof(introducedGenerationId));
            RelatedFormIds = PokemonTaxonomyValue.CopyStrings(relatedFormIds);
            TypeIds = PokemonTaxonomyValue.CopyStrings(typeIds);
            IsDefault = isDefault;
            IsBattleOnly = isBattleOnly;
            IsMega = isMega;
            IsGigantamax = isGigantamax;
            RegionId = PokemonTaxonomyValue.Optional(regionId);
            ImageRelativePath = PokemonTaxonomyValue.Optional(imageRelativePath)?.Replace('\\', '/');
            ImageSourceUrl = PokemonTaxonomyValue.Optional(imageSourceUrl);
            ImageSha256 = PokemonTaxonomyValue.Optional(imageSha256)?.ToLowerInvariant();
            SourceUrl = PokemonTaxonomyValue.Optional(sourceUrl);

            if (string.Equals(FormKind, "regional", StringComparison.Ordinal) && RegionId == null)
                throw new ArgumentException("Regional forms require RegionId.", nameof(regionId));
        }

        public string SpeciesId { get; }
        public int PokemonId { get; }
        public string FormKind { get; }
        public PokemonFormDisposition Disposition { get; }
        public string IntroducedGenerationId { get; }
        public IReadOnlyList<string> RelatedFormIds { get; }
        public IReadOnlyList<string> TypeIds { get; }
        public bool IsDefault { get; }
        public bool IsBattleOnly { get; }
        public bool IsMega { get; }
        public bool IsGigantamax { get; }
        public string RegionId { get; }
        public string ImageRelativePath { get; }
        public string ImageSourceUrl { get; }
        public string ImageSha256 { get; }
        public string SourceUrl { get; }
    }

    public sealed class PokemonTaxonomyCatalog
    {
        public PokemonTaxonomyCatalog(
            IEnumerable<PokemonGenerationDefinition> generations,
            IEnumerable<PokemonSpeciesDefinition> species,
            IEnumerable<PokemonFormDefinition> forms)
        {
            Generations = Index(generations, item => item.Id, "generation");
            Species = Index(species, item => item.Id, "species");
            Forms = Index(forms, item => item.Id, "form");
            Validate();
        }

        public IReadOnlyDictionary<string, PokemonGenerationDefinition> Generations { get; }
        public IReadOnlyDictionary<string, PokemonSpeciesDefinition> Species { get; }
        public IReadOnlyDictionary<string, PokemonFormDefinition> Forms { get; }

        public IReadOnlyList<PokemonSpeciesDefinition> GetSpeciesByGeneration(string generationId)
        {
            if (!Generations.ContainsKey(generationId ?? string.Empty))
                throw new KeyNotFoundException("Unknown Pokémon generation: " + generationId);
            return Species.Values
                .Where(item => string.Equals(item.DebutGenerationId, generationId, StringComparison.Ordinal))
                .OrderBy(item => item.NationalDexNumber)
                .ThenBy(item => item.Id, StringComparer.Ordinal)
                .ToArray();
        }

        public IReadOnlyList<PokemonFormDefinition> GetForms(string speciesId)
        {
            if (!Species.TryGetValue(speciesId ?? string.Empty, out PokemonSpeciesDefinition definition))
                throw new KeyNotFoundException("Unknown Pokémon species: " + speciesId);
            return definition.FormIds.Select(formId => Forms[formId]).ToArray();
        }

        private void Validate()
        {
            EnsureUnique(Generations.Values, item => item.Order, "generation order");
            EnsureUnique(Species.Values, item => item.NationalDexNumber, "national Pokédex number");

            foreach (PokemonSpeciesDefinition item in Species.Values)
            {
                if (!Generations.TryGetValue(item.DebutGenerationId, out PokemonGenerationDefinition generation))
                    throw new InvalidOperationException($"Species '{item.Id}' references an unknown debut generation.");
                if (item.NationalDexNumber < generation.SpeciesStartNumber ||
                    item.NationalDexNumber > generation.SpeciesEndNumber)
                {
                    throw new InvalidOperationException(
                        $"Species '{item.Id}' is outside its debut generation Pokédex range.");
                }

                foreach (string formId in item.FormIds)
                {
                    if (!Forms.TryGetValue(formId, out PokemonFormDefinition form) ||
                        !string.Equals(form.SpeciesId, item.Id, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Species '{item.Id}' references a missing or foreign form '{formId}'.");
                    }
                }

                if (!Forms[item.DefaultFormId].IsDefault)
                    throw new InvalidOperationException($"Species '{item.Id}' default form is not marked default.");
            }

            foreach (PokemonFormDefinition form in Forms.Values)
            {
                if (!Species.ContainsKey(form.SpeciesId))
                    throw new InvalidOperationException($"Form '{form.Id}' references an unknown species.");
                if (!Generations.ContainsKey(form.IntroducedGenerationId))
                    throw new InvalidOperationException($"Form '{form.Id}' references an unknown introduced generation.");
                foreach (string relatedId in form.RelatedFormIds)
                {
                    if (!Forms.TryGetValue(relatedId, out PokemonFormDefinition related) ||
                        !string.Equals(related.SpeciesId, form.SpeciesId, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Form '{form.Id}' references a missing or foreign related form '{relatedId}'.");
                    }
                    if (!related.RelatedFormIds.Contains(form.Id, StringComparer.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Related form link '{form.Id}' ↔ '{relatedId}' is not bidirectional.");
                    }
                }
            }
        }

        private static IReadOnlyDictionary<string, T> Index<T>(
            IEnumerable<T> values,
            Func<T, string> id,
            string label) where T : class
        {
            var result = new Dictionary<string, T>(StringComparer.Ordinal);
            foreach (T value in values ?? Enumerable.Empty<T>())
            {
                if (value == null)
                    throw new ArgumentException($"Taxonomy contains a null {label}.");
                string key = id(value);
                if (!result.TryAdd(key, value))
                    throw new ArgumentException($"Taxonomy contains duplicate {label} id '{key}'.");
            }
            if (result.Count == 0)
                throw new ArgumentException($"Taxonomy requires at least one {label}.");
            return new ReadOnlyDictionary<string, T>(result);
        }

        private static void EnsureUnique<T, TKey>(IEnumerable<T> values, Func<T, TKey> key, string label)
        {
            var seen = new HashSet<TKey>();
            foreach (T value in values)
            {
                TKey current = key(value);
                if (!seen.Add(current))
                    throw new InvalidOperationException($"Taxonomy contains duplicate {label} '{current}'.");
            }
        }
    }

    internal static class PokemonTaxonomyValue
    {
        public static string Required(string value, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException(fieldName + " cannot be empty.", fieldName);
            return value.Trim();
        }

        public static string Optional(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        public static IReadOnlyList<string> CopyStrings(IEnumerable<string> values)
        {
            return new ReadOnlyCollection<string>((values ?? Enumerable.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList());
        }
    }
}
