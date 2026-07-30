using System;
using System.Collections.Generic;
using System.Linq;
using Gacha.Pokemon.Domain;

namespace Gacha.Pokemon.Application
{
    public sealed class PokemonPokedexBrowser
    {
        private readonly PokemonTaxonomyCatalog taxonomy;
        private readonly PokemonCardSubjectCatalog cardSubjects;
        private readonly Stack<Selection> history = new Stack<Selection>();
        private string generationId;
        private string query = string.Empty;

        public PokemonPokedexBrowser(
            PokemonTaxonomyCatalog taxonomy,
            PokemonCardSubjectCatalog cardSubjects = null)
        {
            this.taxonomy = taxonomy ?? throw new ArgumentNullException(nameof(taxonomy));
            this.cardSubjects = cardSubjects;
            Generations = taxonomy.Generations.Values
                .OrderBy(value => value.Order)
                .ThenBy(value => value.Id, StringComparer.Ordinal)
                .ToArray();
            generationId = Generations[0].Id;
        }

        public IReadOnlyList<PokemonGenerationDefinition> Generations { get; }
        public string GenerationId => generationId;
        public string Query => query;
        public PokemonSpeciesDefinition SelectedSpecies { get; private set; }
        public PokemonFormDefinition SelectedForm { get; private set; }
        public bool CanNavigateBack => history.Count > 0;

        public IReadOnlyList<PokemonSpeciesDefinition> VisibleSpecies =>
            taxonomy.GetSpeciesByGeneration(generationId)
                .Where(MatchesQuery)
                .OrderBy(value => value.NationalDexNumber)
                .ThenBy(value => value.Id, StringComparer.Ordinal)
                .ToArray();

        public IReadOnlyList<PokemonFormDefinition> SelectableForms
        {
            get
            {
                if (SelectedSpecies == null)
                    return Array.Empty<PokemonFormDefinition>();
                return taxonomy.GetForms(SelectedSpecies.Id)
                    .Where(value => value.IsDefault ||
                                    value.Disposition == PokemonFormDisposition.SeparateEntry ||
                                    value.Disposition == PokemonFormDisposition.RelatedVariant)
                    .OrderByDescending(value => value.IsDefault)
                    .ThenBy(value => value.PokemonId)
                    .ThenBy(value => value.Id, StringComparer.Ordinal)
                    .ToArray();
            }
        }

        public void SelectGeneration(string id)
        {
            if (!taxonomy.Generations.ContainsKey(id ?? string.Empty))
                throw new KeyNotFoundException("Unknown Pokémon generation: " + id);
            generationId = id;
            query = string.Empty;
            SelectedSpecies = null;
            SelectedForm = null;
            history.Clear();
        }

        public void Search(string value)
        {
            query = (value ?? string.Empty).Trim();
        }

        public bool OpenSpecies(string speciesId, string formId = null)
        {
            if (!taxonomy.Species.TryGetValue(speciesId ?? string.Empty, out PokemonSpeciesDefinition species))
                return false;
            string resolvedFormId = string.IsNullOrWhiteSpace(formId) ? species.DefaultFormId : formId;
            if (!taxonomy.Forms.TryGetValue(resolvedFormId, out PokemonFormDefinition form) ||
                !string.Equals(form.SpeciesId, species.Id, StringComparison.Ordinal))
                return false;

            if (SelectedSpecies != null)
                history.Push(new Selection(SelectedSpecies.Id, SelectedForm?.Id));
            SelectedSpecies = species;
            SelectedForm = form;
            return true;
        }

        public bool OpenForm(string formId)
        {
            if (SelectedSpecies == null ||
                !taxonomy.Forms.TryGetValue(formId ?? string.Empty, out PokemonFormDefinition form) ||
                !string.Equals(form.SpeciesId, SelectedSpecies.Id, StringComparison.Ordinal) ||
                !SelectableForms.Any(value => string.Equals(value.Id, form.Id, StringComparison.Ordinal)))
                return false;
            if (SelectedForm != null && string.Equals(SelectedForm.Id, form.Id, StringComparison.Ordinal))
                return true;
            history.Push(new Selection(SelectedSpecies.Id, SelectedForm?.Id));
            SelectedForm = form;
            return true;
        }

        public bool NavigateBack()
        {
            if (history.Count == 0)
                return false;
            Selection previous = history.Pop();
            SelectedSpecies = taxonomy.Species[previous.SpeciesId];
            SelectedForm = taxonomy.Forms[previous.FormId ?? SelectedSpecies.DefaultFormId];
            return true;
        }

        public IReadOnlyList<PokemonCardSubjectLink> GetSpeciesCards(string speciesId) =>
            cardSubjects?.GetBySpecies(speciesId) ?? Array.Empty<PokemonCardSubjectLink>();

        public IReadOnlyList<PokemonCardSubjectLink> GetFormCards(string formId) =>
            cardSubjects?.GetByForm(formId) ?? Array.Empty<PokemonCardSubjectLink>();

        public static string Localized(
            IReadOnlyDictionary<string, string> values,
            string languageId,
            string fallbackLanguageId = "en")
        {
            if (values == null || values.Count == 0)
                return string.Empty;
            if (!string.IsNullOrWhiteSpace(languageId) && values.TryGetValue(languageId, out string exact))
                return exact;
            if (!string.IsNullOrWhiteSpace(fallbackLanguageId) &&
                values.TryGetValue(fallbackLanguageId, out string fallback))
                return fallback;
            return values.OrderBy(value => value.Key, StringComparer.OrdinalIgnoreCase).First().Value;
        }

        private bool MatchesQuery(PokemonSpeciesDefinition species)
        {
            if (query.Length == 0)
                return true;
            string normalized = query.TrimStart('#').TrimStart('0');
            if (normalized.Length == 0)
                normalized = "0";
            if (int.TryParse(normalized, out int number) && species.NationalDexNumber == number)
                return true;
            return species.Names.Values.Any(name =>
                name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private readonly struct Selection
        {
            public Selection(string speciesId, string formId)
            {
                SpeciesId = speciesId;
                FormId = formId;
            }

            public string SpeciesId { get; }
            public string FormId { get; }
        }
    }
}
