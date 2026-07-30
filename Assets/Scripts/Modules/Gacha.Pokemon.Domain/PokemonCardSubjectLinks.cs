using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Gacha.Pokemon.Domain
{
    public enum PokemonCardMatchStatus
    {
        MatchedForm,
        MatchedSpecies,
        MultiSpecies,
        NotApplicable,
        NeedsReview
    }

    public enum PokemonCardMatchMethod
    {
        SourceDexId,
        SourceDexIdAndFormName,
        CanonicalEnglishName,
        ManualOverride,
        Category
    }

    public sealed class PokemonCardSubjectLink
    {
        public PokemonCardSubjectLink(
            string cardId,
            string setId,
            string localId,
            string itemId,
            IEnumerable<string> printingIds,
            string category,
            string cardName,
            IEnumerable<string> speciesIds,
            IEnumerable<string> formIds,
            PokemonCardMatchStatus status,
            PokemonCardMatchMethod method,
            double confidence,
            string reason = null,
            string overrideId = null)
        {
            CardId = PokemonCardSubjectValue.Required(cardId, nameof(cardId));
            SetId = PokemonCardSubjectValue.Required(setId, nameof(setId));
            LocalId = PokemonCardSubjectValue.Required(localId, nameof(localId));
            ItemId = PokemonCardSubjectValue.Required(itemId, nameof(itemId));
            PrintingIds = PokemonCardSubjectValue.CopyStrings(printingIds);
            Category = PokemonCardSubjectValue.Required(category, nameof(category));
            CardName = PokemonCardSubjectValue.Required(cardName, nameof(cardName));
            SpeciesIds = PokemonCardSubjectValue.CopyStrings(speciesIds);
            FormIds = PokemonCardSubjectValue.CopyStrings(formIds);
            Status = status;
            Method = method;
            if (double.IsNaN(confidence) || double.IsInfinity(confidence) || confidence < 0d || confidence > 1d)
                throw new ArgumentOutOfRangeException(nameof(confidence));
            Confidence = confidence;
            Reason = PokemonCardSubjectValue.Optional(reason);
            OverrideId = PokemonCardSubjectValue.Optional(overrideId);
            ValidateState();
        }

        public string CardId { get; }
        public string SetId { get; }
        public string LocalId { get; }
        public string ItemId { get; }
        public IReadOnlyList<string> PrintingIds { get; }
        public string Category { get; }
        public string CardName { get; }
        public IReadOnlyList<string> SpeciesIds { get; }
        public IReadOnlyList<string> FormIds { get; }
        public PokemonCardMatchStatus Status { get; }
        public PokemonCardMatchMethod Method { get; }
        public double Confidence { get; }
        public string Reason { get; }
        public string OverrideId { get; }

        private void ValidateState()
        {
            if (PrintingIds.Count == 0)
                throw new ArgumentException("Card subject link requires at least one printing id.");
            switch (Status)
            {
                case PokemonCardMatchStatus.NotApplicable:
                    if (SpeciesIds.Count != 0 || FormIds.Count != 0 || Method != PokemonCardMatchMethod.Category)
                        throw new ArgumentException("Not-applicable links cannot reference Pokemon subjects.");
                    break;
                case PokemonCardMatchStatus.MatchedSpecies:
                    if (SpeciesIds.Count != 1 || FormIds.Count != 0)
                        throw new ArgumentException("Matched-species links require exactly one species and no form.");
                    break;
                case PokemonCardMatchStatus.MatchedForm:
                    if (SpeciesIds.Count != 1 || FormIds.Count == 0)
                        throw new ArgumentException("Matched-form links require one species and at least one form.");
                    break;
                case PokemonCardMatchStatus.MultiSpecies:
                    if (SpeciesIds.Count < 2)
                        throw new ArgumentException("Multi-species links require at least two species.");
                    break;
                case PokemonCardMatchStatus.NeedsReview:
                    if (string.IsNullOrWhiteSpace(Reason))
                        throw new ArgumentException("Needs-review links require a reason.");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(Status));
            }
            if (Method == PokemonCardMatchMethod.ManualOverride && string.IsNullOrWhiteSpace(OverrideId))
                throw new ArgumentException("Manual override links require OverrideId.");
        }
    }

    public sealed class PokemonCardSubjectCatalog
    {
        private readonly IReadOnlyDictionary<string, PokemonCardSubjectLink> cards;
        private readonly IReadOnlyDictionary<string, PokemonCardSubjectLink> printings;
        private readonly IReadOnlyDictionary<string, IReadOnlyList<PokemonCardSubjectLink>> bySpecies;
        private readonly IReadOnlyDictionary<string, IReadOnlyList<PokemonCardSubjectLink>> byForm;

        public PokemonCardSubjectCatalog(
            IEnumerable<PokemonCardSubjectLink> links,
            PokemonTaxonomyCatalog taxonomy = null)
        {
            var cardIndex = new Dictionary<string, PokemonCardSubjectLink>(StringComparer.Ordinal);
            var printingIndex = new Dictionary<string, PokemonCardSubjectLink>(StringComparer.Ordinal);
            foreach (PokemonCardSubjectLink link in links ?? Enumerable.Empty<PokemonCardSubjectLink>())
            {
                if (link == null)
                    throw new ArgumentException("Card subject catalog contains a null link.", nameof(links));
                if (!cardIndex.TryAdd(link.CardId, link))
                    throw new ArgumentException("Duplicate card subject link: " + link.CardId, nameof(links));
                foreach (string printingId in link.PrintingIds)
                    if (!printingIndex.TryAdd(printingId, link))
                        throw new ArgumentException("Duplicate printing subject link: " + printingId, nameof(links));
                if (taxonomy != null)
                    ValidateTaxonomy(link, taxonomy);
            }
            if (cardIndex.Count == 0)
                throw new ArgumentException("Card subject catalog requires at least one link.", nameof(links));

            cards = new ReadOnlyDictionary<string, PokemonCardSubjectLink>(cardIndex);
            printings = new ReadOnlyDictionary<string, PokemonCardSubjectLink>(printingIndex);
            bySpecies = BuildLookup(cardIndex.Values, link => link.SpeciesIds);
            byForm = BuildLookup(cardIndex.Values, link => link.FormIds);
        }

        public IReadOnlyDictionary<string, PokemonCardSubjectLink> Cards => cards;
        public IReadOnlyDictionary<string, PokemonCardSubjectLink> Printings => printings;

        public IReadOnlyList<PokemonCardSubjectLink> GetBySpecies(string speciesId) =>
            bySpecies.TryGetValue(speciesId ?? string.Empty, out IReadOnlyList<PokemonCardSubjectLink> result)
                ? result
                : Array.Empty<PokemonCardSubjectLink>();

        public IReadOnlyList<PokemonCardSubjectLink> GetByForm(string formId) =>
            byForm.TryGetValue(formId ?? string.Empty, out IReadOnlyList<PokemonCardSubjectLink> result)
                ? result
                : Array.Empty<PokemonCardSubjectLink>();

        private static void ValidateTaxonomy(
            PokemonCardSubjectLink link, PokemonTaxonomyCatalog taxonomy)
        {
            foreach (string speciesId in link.SpeciesIds)
                if (!taxonomy.Species.ContainsKey(speciesId))
                    throw new ArgumentException($"Card '{link.CardId}' references unknown species '{speciesId}'.");
            foreach (string formId in link.FormIds)
            {
                if (!taxonomy.Forms.TryGetValue(formId, out PokemonFormDefinition form))
                    throw new ArgumentException($"Card '{link.CardId}' references unknown form '{formId}'.");
                if (!link.SpeciesIds.Contains(form.SpeciesId, StringComparer.Ordinal))
                    throw new ArgumentException($"Card '{link.CardId}' form '{formId}' is outside its species links.");
            }
        }

        private static IReadOnlyDictionary<string, IReadOnlyList<PokemonCardSubjectLink>> BuildLookup(
            IEnumerable<PokemonCardSubjectLink> links,
            Func<PokemonCardSubjectLink, IReadOnlyList<string>> keys)
        {
            return new ReadOnlyDictionary<string, IReadOnlyList<PokemonCardSubjectLink>>(
                links.SelectMany(link => keys(link).Select(key => (key, link)))
                    .GroupBy(value => value.key, StringComparer.Ordinal)
                    .ToDictionary(
                        group => group.Key,
                        group => (IReadOnlyList<PokemonCardSubjectLink>)group.Select(value => value.link)
                            .OrderBy(value => value.SetId, StringComparer.Ordinal)
                            .ThenBy(value => value.LocalId, StringComparer.Ordinal)
                            .ThenBy(value => value.CardId, StringComparer.Ordinal)
                            .ToArray(),
                        StringComparer.Ordinal));
        }
    }

    internal static class PokemonCardSubjectValue
    {
        public static string Required(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException(name + " cannot be empty.", name);
            return value.Trim();
        }

        public static string Optional(string value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        public static IReadOnlyList<string> CopyStrings(IEnumerable<string> values) =>
            new ReadOnlyCollection<string>((values ?? Enumerable.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToList());
    }
}
