using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Gacha.Pokemon.Infrastructure;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public sealed class PokeApiTaxonomyRawData
{
    public IDictionary<int, string> Generations { get; } = new SortedDictionary<int, string>();
    public IDictionary<int, string> Species { get; } = new SortedDictionary<int, string>();
    public IDictionary<int, string> Pokemon { get; } = new SortedDictionary<int, string>();
    public IDictionary<int, string> Forms { get; } = new SortedDictionary<int, string>();
    public IDictionary<int, string> VersionGroups { get; } = new SortedDictionary<int, string>();
}

public sealed class PokeApiTaxonomyCompileResult
{
    public PokemonTaxonomySnapshotDto Snapshot;
    public int SeparateEntryCount;
    public int RelatedVariantCount;
    public int ManualReviewCount;
    public int ExcludedCount;
}

public static class PokeApiTaxonomyCompiler
{
    public const string SourceBaseUrl = "https://pokeapi.co/api/v2/";
    private static readonly string[] Languages = { "en", "zh" };
    private static readonly IReadOnlyDictionary<string, string> LanguageAliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["en"] = "en",
            ["zh-hans"] = "zh"
        };
    private static readonly IReadOnlyDictionary<string, string> RegionChineseNames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["alola"] = "阿罗拉",
            ["galar"] = "伽勒尔",
            ["hisui"] = "洗翠",
            ["paldea"] = "帕底亚"
        };

    public static PokeApiTaxonomyCompileResult Compile(
        PokeApiTaxonomyRawData raw,
        PokemonFormClassificationCatalog classification,
        DateTimeOffset capturedAtUtc)
    {
        if (raw == null)
            throw new ArgumentNullException(nameof(raw));
        if (classification == null)
            throw new ArgumentNullException(nameof(classification));
        if (raw.Generations.Count == 0 || raw.Species.Count == 0 ||
            raw.Pokemon.Count == 0 || raw.Forms.Count == 0 || raw.VersionGroups.Count == 0)
            throw new InvalidOperationException("PokeAPI raw snapshot is incomplete.");

        var warnings = new HashSet<string>(StringComparer.Ordinal);
        Dictionary<int, JObject> generations = Parse(raw.Generations, "generation");
        Dictionary<int, JObject> species = Parse(raw.Species, "species");
        Dictionary<int, JObject> pokemon = Parse(raw.Pokemon, "pokemon");
        Dictionary<int, JObject> forms = Parse(raw.Forms, "form");
        Dictionary<int, JObject> versionGroups = Parse(raw.VersionGroups, "version group");

        Dictionary<string, string> generationIds = generations.Values.ToDictionary(
            item => RequiredName(item, "generation"),
            item => GenerationId(RequiredInt(item, "id", "generation")),
            StringComparer.Ordinal);
        Dictionary<string, string> versionGroupGenerations = versionGroups.Values.ToDictionary(
            item => RequiredName(item, "version group"),
            item => ResolveGenerationId(item.SelectToken("generation.name")?.ToString(), generationIds),
            StringComparer.Ordinal);

        var snapshot = new PokemonTaxonomySnapshotDto
        {
            Source = "pokeapi",
            SourceBaseUrl = SourceBaseUrl,
            CapturedAtUtc = capturedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            SourceSha256 = ComputeSourceHash(raw),
            Languages = Languages.ToList()
        };

        foreach (JObject generation in generations.Values.OrderBy(item => RequiredInt(item, "id", "generation")))
        {
            int order = RequiredInt(generation, "id", "generation");
            int[] numbers = (generation["pokemon_species"] as JArray ?? new JArray())
                .Select(item => ResourceId(item?["url"]?.ToString(), "generation species"))
                .OrderBy(value => value)
                .ToArray();
            if (numbers.Length == 0)
                throw new InvalidOperationException($"PokeAPI generation {order} has no species.");
            snapshot.Generations.Add(new PokemonGenerationSnapshotDto
            {
                Id = GenerationId(order),
                Order = order,
                Names = LocalizedValues(generation["names"] as JArray, "name", $"generation:{order}", warnings),
                SpeciesStartNumber = numbers.First(),
                SpeciesEndNumber = numbers.Last(),
                SourceUrl = SourceBaseUrl + "generation/" + order + "/"
            });
        }

        foreach (JObject speciesObject in species.Values.OrderBy(item => RequiredInt(item, "id", "species")))
            CompileSpecies(snapshot, speciesObject, pokemon, forms, generationIds,
                versionGroupGenerations, classification, warnings);

        snapshot.Species = snapshot.Species.OrderBy(item => item.NationalDexNumber).ToList();
        snapshot.Forms = snapshot.Forms.OrderBy(item => item.SpeciesId, StringComparer.Ordinal)
            .ThenBy(item => item.Id, StringComparer.Ordinal).ToList();
        snapshot.Warnings = warnings.OrderBy(value => value, StringComparer.Ordinal).ToList();

        // The runtime reader is the final schema/domain gate for compiler output.
        new PokemonTaxonomySnapshotReader().Read(
            JsonConvert.SerializeObject(snapshot, Formatting.None));

        return new PokeApiTaxonomyCompileResult
        {
            Snapshot = snapshot,
            SeparateEntryCount = snapshot.Forms.Count(item => item.Disposition == "separate-entry"),
            RelatedVariantCount = snapshot.Forms.Count(item => item.Disposition == "related-variant"),
            ManualReviewCount = snapshot.Forms.Count(item => item.Disposition == "manual-review"),
            ExcludedCount = snapshot.Forms.Count(item => item.Disposition == "exclude")
        };
    }

    private static void CompileSpecies(
        PokemonTaxonomySnapshotDto snapshot,
        JObject species,
        IReadOnlyDictionary<int, JObject> pokemon,
        IReadOnlyDictionary<int, JObject> forms,
        IReadOnlyDictionary<string, string> generationIds,
        IReadOnlyDictionary<string, string> versionGroupGenerations,
        PokemonFormClassificationCatalog classification,
        HashSet<string> warnings)
    {
        int speciesNumber = RequiredInt(species, "id", "species");
        string speciesId = "pokemon-species:" + speciesNumber;
        Dictionary<string, string> speciesNames = LocalizedValues(
            species["names"] as JArray, "name", speciesId + ":name", warnings);
        var candidates = new List<FormCandidate>();
        JArray varieties = species["varieties"] as JArray ?? new JArray();
        foreach (JToken variety in varieties)
        {
            int pokemonId = ResourceId(variety.SelectToken("pokemon.url")?.ToString(), speciesId + " variety");
            if (!pokemon.TryGetValue(pokemonId, out JObject pokemonObject))
                throw new InvalidOperationException($"{speciesId} references missing Pokemon {pokemonId}.");
            bool isDefaultVariety = variety["is_default"]?.Value<bool>() == true;
            string[] typeIds = (pokemonObject["types"] as JArray ?? new JArray())
                .OrderBy(item => item["slot"]?.Value<int>() ?? int.MaxValue)
                .Select(item => item.SelectToken("type.name")?.ToString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();
            string officialArtwork = pokemonObject.SelectToken(
                "sprites.other.official-artwork.front_default")?.ToString();

            foreach (JToken formReference in pokemonObject["forms"] as JArray ?? new JArray())
            {
                int formId = ResourceId(formReference["url"]?.ToString(), $"Pokemon {pokemonId} form");
                if (!forms.TryGetValue(formId, out JObject form))
                    throw new InvalidOperationException($"Pokemon {pokemonId} references missing form {formId}.");
                candidates.Add(new FormCandidate(
                    form, pokemonObject, pokemonId, isDefaultVariety, typeIds, officialArtwork));
            }
        }
        if (candidates.Count == 0)
            throw new InvalidOperationException($"{speciesId} has no form candidates.");

        string[] formIds = candidates.Select(item => "pokemon-form:" + item.FormId)
            .Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        if (formIds.Length != candidates.Count)
            throw new InvalidOperationException($"{speciesId} contains duplicate form candidates.");
        FormCandidate defaultCandidate = candidates
            .Where(item => item.IsDefaultVariety && item.IsDefaultForm)
            .OrderBy(item => item.FormId).FirstOrDefault()
            ?? candidates.Where(item => item.IsDefaultVariety).OrderBy(item => item.FormId).FirstOrDefault()
            ?? candidates.OrderBy(item => item.FormId).First();

        snapshot.Species.Add(new PokemonSpeciesSnapshotDto
        {
            Id = speciesId,
            NationalDexNumber = speciesNumber,
            DebutGenerationId = ResolveGenerationId(
                species.SelectToken("generation.name")?.ToString(), generationIds),
            Names = speciesNames,
            Genera = LocalizedValues(species["genera"] as JArray, "genus", speciesId + ":genus", warnings),
            Descriptions = FlavorTexts(species["flavor_text_entries"] as JArray, speciesId, warnings),
            DefaultFormId = "pokemon-form:" + defaultCandidate.FormId,
            FormIds = formIds.ToList(),
            IsBaby = species["is_baby"]?.Value<bool>() == true,
            IsLegendary = species["is_legendary"]?.Value<bool>() == true,
            IsMythical = species["is_mythical"]?.Value<bool>() == true,
            ColorId = species.SelectToken("color.name")?.ToString(),
            HabitatId = species.SelectToken("habitat.name")?.ToString(),
            SourceUrl = SourceBaseUrl + "pokemon-species/" + speciesNumber + "/"
        });

        foreach (FormCandidate candidate in candidates.OrderBy(item => item.FormId))
        {
            string formId = "pokemon-form:" + candidate.FormId;
            string regionId;
            string formKind = ClassifyFormKind(candidate, species, out regionId);
            string disposition = formKind == "default"
                ? "separate-entry"
                : classification.GetPolicy(formKind).DefaultDisposition;
            if (classification.TryGetOverride(formId, out PokemonFormClassificationOverride formOverride))
            {
                if (!string.Equals(formOverride.SpeciesId, speciesId, StringComparison.Ordinal))
                    throw new InvalidOperationException($"Form override '{formId}' targets the wrong species.");
                disposition = formOverride.Disposition;
            }

            string versionGroupName = candidate.Form.SelectToken("version_group.name")?.ToString();
            if (string.IsNullOrWhiteSpace(versionGroupName) ||
                !versionGroupGenerations.TryGetValue(versionGroupName, out string introducedGeneration))
                throw new InvalidOperationException($"Form '{formId}' has an unknown version group.");

            Dictionary<string, string> names = FormNames(
                candidate.Form, speciesNames, formKind, regionId, formId, warnings);
            snapshot.Forms.Add(new PokemonFormSnapshotDto
            {
                Id = formId,
                SpeciesId = speciesId,
                PokemonId = candidate.PokemonId,
                FormKind = formKind,
                Disposition = disposition,
                Names = names,
                IntroducedGenerationId = introducedGeneration,
                RelatedFormIds = formIds.Where(value => value != formId).ToList(),
                TypeIds = candidate.TypeIds.ToList(),
                IsDefault = candidate.IsDefaultVariety && candidate.IsDefaultForm,
                IsBattleOnly = candidate.Form["is_battle_only"]?.Value<bool>() == true,
                IsMega = candidate.Form["is_mega"]?.Value<bool>() == true,
                IsGigantamax = formKind == "gigantamax",
                RegionId = regionId,
                ImageSourceUrl = FirstText(candidate.OfficialArtwork,
                    candidate.Form.SelectToken("sprites.front_default")?.ToString()),
                SourceUrl = SourceBaseUrl + "pokemon-form/" + candidate.FormId + "/"
            });
        }
    }

    private static string ClassifyFormKind(FormCandidate candidate, JObject species, out string regionId)
    {
        regionId = DetectRegion(candidate.Form, candidate.Pokemon);
        if (candidate.Form["is_mega"]?.Value<bool>() == true)
            return "mega";
        if (ContainsToken(candidate.Form, candidate.Pokemon, "gmax"))
            return "gigantamax";
        if (regionId != null)
            return "regional";
        if (candidate.Form["is_battle_only"]?.Value<bool>() == true)
            return "battle-only";
        if (species["has_gender_differences"]?.Value<bool>() == true &&
            (ContainsToken(candidate.Form, candidate.Pokemon, "female") ||
             ContainsToken(candidate.Form, candidate.Pokemon, "male")))
            return "gender-difference";
        if (candidate.IsDefaultVariety && candidate.IsDefaultForm)
            return "default";
        if (!candidate.IsDefaultVariety && candidate.IsDefaultForm)
            return "alternate";
        return "cosmetic";
    }

    private static Dictionary<string, string> FormNames(
        JObject form,
        IReadOnlyDictionary<string, string> speciesNames,
        string formKind,
        string regionId,
        string context,
        HashSet<string> warnings)
    {
        if (formKind == "default")
            return new Dictionary<string, string>(speciesNames, StringComparer.Ordinal);
        Dictionary<string, string> values = ExtractLocalized(form["names"] as JArray, "name");
        if (formKind == "regional" && regionId != null && !values.ContainsKey("zh") &&
            speciesNames.TryGetValue("zh", out string speciesChinese) &&
            RegionChineseNames.TryGetValue(regionId, out string regionChinese))
            values["zh"] = regionChinese + speciesChinese;
        if (!values.ContainsKey("en"))
        {
            if (!speciesNames.TryGetValue("en", out string speciesEnglish))
                throw new InvalidOperationException($"{context}:name has no species English fallback.");
            string formToken = FirstText(form["form_name"]?.ToString(), form["name"]?.ToString());
            if (string.IsNullOrWhiteSpace(formToken))
                throw new InvalidOperationException($"{context}:name has no form slug fallback.");
            string suffix = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(
                formToken.Replace('-', ' '));
            values["en"] = string.Equals(speciesEnglish, suffix, StringComparison.OrdinalIgnoreCase)
                ? speciesEnglish
                : speciesEnglish + " (" + suffix + ")";
            warnings.Add($"fallback:{context}:name:en->slug");
        }
        FillFallbacks(values, context + ":name", warnings);
        return values;
    }

    private static Dictionary<string, string> FlavorTexts(
        JArray entries, string context, HashSet<string> warnings)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JToken entry in entries ?? new JArray())
        {
            string sourceLanguage = entry.SelectToken("language.name")?.ToString();
            if (sourceLanguage == null || !LanguageAliases.TryGetValue(sourceLanguage, out string language))
                continue;
            string value = NormalizeText(entry["flavor_text"]?.ToString());
            if (!string.IsNullOrWhiteSpace(value))
                values[language] = value;
        }
        FillFallbacks(values, context + ":description", warnings);
        return values;
    }

    private static Dictionary<string, string> LocalizedValues(
        JArray entries, string valueProperty, string context, HashSet<string> warnings)
    {
        Dictionary<string, string> result = ExtractLocalized(entries, valueProperty);
        FillFallbacks(result, context, warnings);
        return result;
    }

    private static Dictionary<string, string> ExtractLocalized(JArray entries, string valueProperty)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JToken entry in entries ?? new JArray())
        {
            string sourceLanguage = entry.SelectToken("language.name")?.ToString();
            if (sourceLanguage == null || !LanguageAliases.TryGetValue(sourceLanguage, out string language))
                continue;
            string value = NormalizeText(entry[valueProperty]?.ToString());
            if (!string.IsNullOrWhiteSpace(value))
                result[language] = value;
        }
        return result;
    }

    private static void FillFallbacks(
        IDictionary<string, string> values, string context, HashSet<string> warnings)
    {
        if (!values.TryGetValue("en", out string english) || string.IsNullOrWhiteSpace(english))
            throw new InvalidOperationException($"{context} has no English value.");
        foreach (string language in Languages)
        {
            if (values.ContainsKey(language))
                continue;
            values[language] = english;
            warnings.Add($"fallback:{context}:{language}->en");
        }
    }

    private static Dictionary<int, JObject> Parse(IDictionary<int, string> source, string label)
    {
        var result = new Dictionary<int, JObject>();
        foreach (KeyValuePair<int, string> entry in source)
        {
            JObject value;
            try
            {
                value = JObject.Parse(entry.Value);
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException($"PokeAPI {label} {entry.Key} is invalid JSON.", exception);
            }
            int actualId = RequiredInt(value, "id", label);
            if (actualId != entry.Key)
                throw new InvalidOperationException($"PokeAPI {label} key {entry.Key} contains id {actualId}.");
            result.Add(entry.Key, value);
        }
        return result;
    }

    private static string ComputeSourceHash(PokeApiTaxonomyRawData raw)
    {
        using var sha = SHA256.Create();
        void Add(string kind, IDictionary<int, string> entries)
        {
            foreach (KeyValuePair<int, string> entry in entries.OrderBy(item => item.Key))
            {
                string canonical = JToken.Parse(entry.Value).ToString(Formatting.None);
                byte[] bytes = Encoding.UTF8.GetBytes(kind + ":" + entry.Key + "\n" + canonical + "\n");
                sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
            }
        }
        Add("generation", raw.Generations);
        Add("species", raw.Species);
        Add("pokemon", raw.Pokemon);
        Add("form", raw.Forms);
        Add("version-group", raw.VersionGroups);
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return string.Concat(sha.Hash.Select(value => value.ToString("x2")));
    }

    private static string ResolveGenerationId(
        string sourceName, IReadOnlyDictionary<string, string> generationIds)
    {
        if (string.IsNullOrWhiteSpace(sourceName) || !generationIds.TryGetValue(sourceName, out string result))
            throw new InvalidOperationException("PokeAPI references an unknown generation: " + sourceName);
        return result;
    }

    private static int ResourceId(string url, string context)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException($"{context} has an invalid HTTPS resource URL.");
        string segment = uri.Segments.Select(value => value.Trim('/'))
            .LastOrDefault(value => int.TryParse(value, out _));
        if (!int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out int id) || id < 1)
            throw new InvalidOperationException($"{context} has an invalid resource id.");
        return id;
    }

    private static string GenerationId(int order) => "generation-" + order;

    private static string RequiredName(JObject value, string context)
    {
        string result = value["name"]?.ToString();
        if (string.IsNullOrWhiteSpace(result))
            throw new InvalidOperationException($"PokeAPI {context} has no name.");
        return result;
    }

    private static int RequiredInt(JObject value, string property, string context)
    {
        int? result = value[property]?.Value<int?>();
        if (!result.HasValue || result.Value < 1)
            throw new InvalidOperationException($"PokeAPI {context} has invalid {property}.");
        return result.Value;
    }

    private static string DetectRegion(JObject form, JObject pokemon)
    {
        foreach (string region in RegionChineseNames.Keys)
            if (ContainsToken(form, pokemon, region))
                return region;
        return null;
    }

    private static bool ContainsToken(JObject form, JObject pokemon, string token)
    {
        string[] values = { form["name"]?.ToString(), form["form_name"]?.ToString(), pokemon["name"]?.ToString() };
        return values.Any(value => !string.IsNullOrWhiteSpace(value) &&
            value.Split('-').Contains(token, StringComparer.Ordinal));
    }

    private static string NormalizeText(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : string.Join(" ", value.Replace('\n', ' ').Replace('\f', ' ')
                .Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string FirstText(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private sealed class FormCandidate
    {
        public FormCandidate(
            JObject form, JObject pokemon, int pokemonId, bool isDefaultVariety,
            IReadOnlyList<string> typeIds, string officialArtwork)
        {
            Form = form;
            Pokemon = pokemon;
            PokemonId = pokemonId;
            IsDefaultVariety = isDefaultVariety;
            TypeIds = typeIds;
            OfficialArtwork = officialArtwork;
            FormId = RequiredInt(form, "id", "form");
            IsDefaultForm = form["is_default"]?.Value<bool>() == true;
        }

        public JObject Form { get; }
        public JObject Pokemon { get; }
        public int PokemonId { get; }
        public int FormId { get; }
        public bool IsDefaultVariety { get; }
        public bool IsDefaultForm { get; }
        public IReadOnlyList<string> TypeIds { get; }
        public string OfficialArtwork { get; }
    }
}
