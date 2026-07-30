using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Gacha.Domain;
using Gacha.Infrastructure.Content;
using Gacha.Infrastructure.Rules;
using Gacha.Pokemon.Domain;
using Gacha.Pokemon.Infrastructure;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

[Serializable]
public sealed class PokemonCardSubjectOverrideFile
{
    public int SchemaVersion = 1;
    public List<PokemonCardSubjectOverride> Overrides = new List<PokemonCardSubjectOverride>();
}

[Serializable]
public sealed class PokemonCardSubjectOverride
{
    public string Id;
    public string CardId;
    public List<string> SpeciesIds = new List<string>();
    public List<string> FormIds = new List<string>();
    public string Status;
    public string Reason;
}

public sealed class PokemonCardSubjectLinkResult
{
    public PokemonCardSubjectSnapshotDto Snapshot;
    public int SetCount;
    public int CardCount;
    public int PrintingCount;
    public int MatchedFormCount;
    public int MatchedSpeciesCount;
    public int MultiSpeciesCount;
    public int NotApplicableCount;
    public int NeedsReviewCount;
}

public static class PokemonCardSubjectLinker
{
    private const string GameId = "pokemon-tcg";
    private static readonly string[] MechanicSuffixes =
    {
        "special illustration rare", "illustration rare", "radiant", "shining",
        "v-union", "vstar", "vmax", "break", "legend", "lv.x", "star", "gx", "ex", "v", "δ"
    };

    public static PokemonCardSubjectLinkResult LinkFiles(
        string importRoot,
        string language,
        string taxonomySnapshotPath,
        string overridePath,
        string outputPath)
    {
        if (string.IsNullOrWhiteSpace(importRoot) || !Directory.Exists(importRoot))
            throw new DirectoryNotFoundException("Card import root was not found: " + importRoot);
        string normalizedLanguage = (language ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedLanguage.Length == 0)
            throw new ArgumentException("Card language is required.", nameof(language));
        PokemonTaxonomySnapshotLoadResult taxonomyLoad =
            new PokemonTaxonomySnapshotReader().LoadFile(taxonomySnapshotPath);
        PokemonCardSubjectOverrideCatalog overrides = LoadOverrides(overridePath);
        string languageRoot = Path.Combine(importRoot, normalizedLanguage);
        IReadOnlyList<PrivateContentManifestDocument> documents =
            new PrivateContentManifestReader().LoadDirectory(languageRoot)
                .Where(document => string.Equals(
                    document.Manifest.Language, normalizedLanguage, StringComparison.OrdinalIgnoreCase))
                .OrderBy(document => document.Manifest.Set.Id, StringComparer.Ordinal)
                .ToArray();
        if (documents.Count == 0)
            throw new InvalidDataException("No card manifests matched language: " + normalizedLanguage);
        PrivateCatalogImportResult runtimeCatalog =
            new PrivateManifestCatalogAdapter(new PokemonImportedCardVariantPolicy()).Build(documents);
        Dictionary<string, string[]> printingIds = runtimeCatalog.Catalog.Printings.Values
            .GroupBy(printing => printing.ItemId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(value => value.Id).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);

        Dictionary<int, PokemonSpeciesDefinition> speciesByNumber = taxonomyLoad.Catalog.Species.Values
            .ToDictionary(value => value.NationalDexNumber);
        Dictionary<string, PokemonSpeciesDefinition[]> speciesByName = taxonomyLoad.Catalog.Species.Values
            .SelectMany(species => species.Names
                .Where(name => name.Key.Equals("en", StringComparison.OrdinalIgnoreCase))
                .Select(name => (key: NormalizeName(name.Value), species)))
            .GroupBy(value => value.key, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(value => value.species).Distinct().ToArray(),
                StringComparer.Ordinal);

        var links = new List<PokemonCardSubjectLinkDto>();
        var warnings = new HashSet<string>(StringComparer.Ordinal);
        using var sourceHash = SHA256.Create();
        foreach (PrivateContentManifestDocument document in documents)
        {
            AddHash(sourceHash, "manifest:" + document.Manifest.Set.Id,
                JToken.Parse(File.ReadAllText(document.ManifestPath)).ToString(Formatting.None));
            string setDirectory = Path.GetDirectoryName(document.ManifestPath);
            foreach (ImportedCardDto card in document.Manifest.Cards.OrderBy(value => value.LocalId, StringComparer.Ordinal))
            {
                string rawPath = Path.GetFullPath(Path.Combine(setDirectory, card.RawDataRelativePath));
                if (!IsWithin(setDirectory, rawPath) || !File.Exists(rawPath))
                    throw new InvalidDataException($"Card '{card.Id}' has a missing or unsafe raw path.");
                JObject raw = JObject.Parse(File.ReadAllText(rawPath, Encoding.UTF8));
                AddHash(sourceHash, "card:" + card.Id, raw.ToString(Formatting.None));
                string itemId = RuntimeId(GameId, "item", document.Manifest.Set.Id, card.LocalId);
                if (!printingIds.TryGetValue(itemId, out string[] cardPrintings) || cardPrintings.Length == 0)
                    throw new InvalidDataException($"Card '{card.Id}' has no runtime printing identities.");
                links.Add(LinkCard(
                    document.Manifest.Set.Id,
                    card,
                    raw,
                    itemId,
                    cardPrintings,
                    speciesByNumber,
                    speciesByName,
                    taxonomyLoad.Catalog,
                    overrides,
                    warnings));
            }
        }
        sourceHash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

        var snapshot = new PokemonCardSubjectSnapshotDto
        {
            Source = "tcgdex",
            Language = normalizedLanguage,
            GeneratedAtUtc = taxonomyLoad.CapturedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            TaxonomySourceSha256 = taxonomyLoad.SourceSha256,
            CardContentSha256 = string.Concat(sourceHash.Hash.Select(value => value.ToString("x2"))),
            Links = links.OrderBy(value => value.SetId, StringComparer.Ordinal)
                .ThenBy(value => value.LocalId, StringComparer.Ordinal)
                .ThenBy(value => value.CardId, StringComparer.Ordinal)
                .ToList(),
            Warnings = warnings.OrderBy(value => value, StringComparer.Ordinal).ToList()
        };
        PokemonCardSubjectSnapshotLoadResult loaded = new PokemonCardSubjectSnapshotReader().Read(
            JsonConvert.SerializeObject(snapshot, Formatting.None), taxonomyLoad.Catalog);
        if (loaded.Catalog.Cards.Count != documents.Sum(value => value.Manifest.Cards.Count))
            throw new InvalidDataException("Card subject output did not cover every source card.");
        WriteTextAtomic(outputPath, JsonConvert.SerializeObject(snapshot, Formatting.Indented));

        return new PokemonCardSubjectLinkResult
        {
            Snapshot = snapshot,
            SetCount = documents.Count,
            CardCount = snapshot.Links.Count,
            PrintingCount = snapshot.Links.Sum(value => value.PrintingIds.Count),
            MatchedFormCount = Count(snapshot, "matched-form"),
            MatchedSpeciesCount = Count(snapshot, "matched-species"),
            MultiSpeciesCount = Count(snapshot, "multi-species"),
            NotApplicableCount = Count(snapshot, "not-applicable"),
            NeedsReviewCount = Count(snapshot, "needs-review")
        };
    }

    private static PokemonCardSubjectLinkDto LinkCard(
        string setId,
        ImportedCardDto card,
        JObject raw,
        string itemId,
        IReadOnlyList<string> printingIds,
        IReadOnlyDictionary<int, PokemonSpeciesDefinition> speciesByNumber,
        IReadOnlyDictionary<string, PokemonSpeciesDefinition[]> speciesByName,
        PokemonTaxonomyCatalog taxonomy,
        PokemonCardSubjectOverrideCatalog overrides,
        ISet<string> warnings)
    {
        if (overrides.TryGet(card.Id, out PokemonCardSubjectOverride manual))
            return OverrideLink(setId, card, itemId, printingIds, manual);
        if (!string.Equals(card.Category, "Pokemon", StringComparison.OrdinalIgnoreCase))
            return Dto(setId, card, itemId, printingIds, Array.Empty<string>(), Array.Empty<string>(),
                "not-applicable", "category", 1d, "non-pokemon-category", null);

        int[] dexNumbers = (raw["dexId"] as JArray ?? new JArray())
            .Values<int>().Distinct().OrderBy(value => value).ToArray();
        var species = new List<PokemonSpeciesDefinition>();
        foreach (int number in dexNumbers)
        {
            if (!speciesByNumber.TryGetValue(number, out PokemonSpeciesDefinition definition))
                return Dto(setId, card, itemId, printingIds, Array.Empty<string>(), Array.Empty<string>(),
                    "needs-review", "source-dex-id", 0d, "unknown-source-dex-id:" + number, null);
            species.Add(definition);
        }
        if (species.Count > 1)
            return Dto(setId, card, itemId, printingIds,
                species.Select(value => value.Id), Array.Empty<string>(),
                "multi-species", "source-dex-id", 1d, "source-multi-dex-id", null);

        bool ownerPrefix;
        string baseName = BaseCardName(card.Name, raw["suffix"]?.ToString(), out ownerPrefix);
        if (species.Count == 0)
        {
            string nameKey = NormalizeName(baseName);
            if (!speciesByName.TryGetValue(nameKey, out PokemonSpeciesDefinition[] candidates) ||
                candidates.Length != 1)
                return Dto(setId, card, itemId, printingIds, Array.Empty<string>(), Array.Empty<string>(),
                    "needs-review", "canonical-english-name", 0d, "missing-source-dex-id", null);
            species.Add(candidates[0]);
            warnings.Add("source-dex-fallback:" + card.Id);
            return Dto(setId, card, itemId, printingIds,
                new[] { candidates[0].Id }, Array.Empty<string>(),
                "needs-review", "canonical-english-name", ownerPrefix ? 0.85d : 0.9d,
                ownerPrefix ? "trainer-owned-name-requires-review" : "source-dex-id-missing", null);
        }

        PokemonSpeciesDefinition matchedSpecies = species[0];
        PokemonFormDefinition[] formCandidates = taxonomy.GetForms(matchedSpecies.Id)
            .Where(form => !form.IsDefault && form.Names.TryGetValue("en", out string formName) &&
                           NormalizeName(formName) == NormalizeName(baseName))
            .ToArray();
        if (formCandidates.Length == 1)
        {
            PokemonFormDefinition form = formCandidates[0];
            if (form.Disposition == PokemonFormDisposition.ManualReview ||
                form.Disposition == PokemonFormDisposition.Excluded)
                return Dto(setId, card, itemId, printingIds,
                    new[] { matchedSpecies.Id }, new[] { form.Id },
                    "needs-review", "source-dex-id-and-form-name", 0.95d,
                    "form-policy-requires-review", null);
            return Dto(setId, card, itemId, printingIds,
                new[] { matchedSpecies.Id }, new[] { form.Id },
                "matched-form", "source-dex-id-and-form-name", 1d, "exact-form-name", null);
        }
        if (formCandidates.Length > 1)
            return Dto(setId, card, itemId, printingIds,
                new[] { matchedSpecies.Id }, formCandidates.Select(value => value.Id),
                "needs-review", "source-dex-id-and-form-name", 0.5d,
                "ambiguous-form-name", null);
        return Dto(setId, card, itemId, printingIds,
            new[] { matchedSpecies.Id }, Array.Empty<string>(),
            "matched-species", "source-dex-id", 1d, "source-dex-id", null);
    }

    private static PokemonCardSubjectLinkDto OverrideLink(
        string setId,
        ImportedCardDto card,
        string itemId,
        IReadOnlyList<string> printingIds,
        PokemonCardSubjectOverride entry)
    {
        return Dto(setId, card, itemId, printingIds, entry.SpeciesIds, entry.FormIds,
            entry.Status, "manual-override", 1d, entry.Reason, entry.Id);
    }

    private static PokemonCardSubjectLinkDto Dto(
        string setId,
        ImportedCardDto card,
        string itemId,
        IEnumerable<string> printingIds,
        IEnumerable<string> speciesIds,
        IEnumerable<string> formIds,
        string status,
        string method,
        double confidence,
        string reason,
        string overrideId)
    {
        return new PokemonCardSubjectLinkDto
        {
            CardId = card.Id,
            SetId = setId,
            LocalId = card.LocalId,
            ItemId = itemId,
            PrintingIds = printingIds.OrderBy(value => value, StringComparer.Ordinal).ToList(),
            Category = card.Category,
            CardName = card.Name,
            SpeciesIds = speciesIds.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToList(),
            FormIds = formIds.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToList(),
            Status = status,
            Method = method,
            Confidence = confidence,
            Reason = reason,
            OverrideId = overrideId
        };
    }

    private static string BaseCardName(string value, string sourceSuffix, out bool ownerPrefix)
    {
        string result = (value ?? string.Empty).Trim();
        ownerPrefix = false;
        int owner = result.IndexOf("'s ", StringComparison.OrdinalIgnoreCase);
        int curlyOwner = result.IndexOf("’s ", StringComparison.OrdinalIgnoreCase);
        int split = owner >= 0 && curlyOwner >= 0 ? Math.Min(owner, curlyOwner) : Math.Max(owner, curlyOwner);
        if (split >= 0)
        {
            result = result.Substring(split + 3).Trim();
            ownerPrefix = true;
        }
        string suffix = (sourceSuffix ?? string.Empty).Trim();
        if (suffix.Length > 0)
            result = RemoveSuffix(result, suffix);
        foreach (string known in MechanicSuffixes)
            result = RemoveSuffix(result, known);
        return result.Trim(' ', '-', '–', '—');
    }

    private static string RemoveSuffix(string value, string suffix)
    {
        if (value.EndsWith(" " + suffix, StringComparison.OrdinalIgnoreCase) ||
            value.EndsWith("-" + suffix, StringComparison.OrdinalIgnoreCase))
            return value.Substring(0, value.Length - suffix.Length - 1).Trim();
        return value;
    }

    private static string NormalizeName(string value)
    {
        string decomposed = (value ?? string.Empty).Normalize(NormalizationForm.FormD).ToLowerInvariant();
        var builder = new StringBuilder(decomposed.Length);
        foreach (char character in decomposed)
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsLetterOrDigit(character))
                builder.Append(character);
        }
        return builder.ToString();
    }

    private static PokemonCardSubjectOverrideCatalog LoadOverrides(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException("Card subject override file was not found.", path);
        PokemonCardSubjectOverrideFile file = JsonConvert.DeserializeObject<PokemonCardSubjectOverrideFile>(
            File.ReadAllText(path)) ?? throw new InvalidDataException("Card subject override file is empty.");
        if (file.SchemaVersion != 1)
            throw new InvalidDataException("Unsupported card subject override schema: " + file.SchemaVersion);
        var entries = new Dictionary<string, PokemonCardSubjectOverride>(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (PokemonCardSubjectOverride entry in file.Overrides ?? new List<PokemonCardSubjectOverride>())
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Id) || string.IsNullOrWhiteSpace(entry.CardId) ||
                string.IsNullOrWhiteSpace(entry.Status) || string.IsNullOrWhiteSpace(entry.Reason))
                throw new InvalidDataException("Card subject override requires Id, CardId, Status, and Reason.");
            entry.Id = entry.Id.Trim();
            entry.CardId = entry.CardId.Trim();
            entry.Status = entry.Status.Trim();
            entry.Reason = entry.Reason.Trim();
            entry.SpeciesIds = NormalizeIds(entry.SpeciesIds);
            entry.FormIds = NormalizeIds(entry.FormIds);
            if (!ids.Add(entry.Id) || !entries.TryAdd(entry.CardId, entry))
                throw new InvalidDataException("Duplicate card subject override id or CardId: " + entry.Id);
        }
        return new PokemonCardSubjectOverrideCatalog(entries);
    }

    private static List<string> NormalizeIds(IEnumerable<string> values) =>
        (values ?? Enumerable.Empty<string>()).Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim()).Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal).ToList();

    private static int Count(PokemonCardSubjectSnapshotDto snapshot, string status) =>
        snapshot.Links.Count(value => value.Status == status);

    private static string RuntimeId(params string[] parts) =>
        string.Join(":", parts.Select(Slug));

    private static string Slug(string value)
    {
        string normalized = string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim().ToLowerInvariant();
        var result = new StringBuilder(normalized.Length);
        bool separator = false;
        foreach (char character in normalized)
        {
            if (char.IsLetterOrDigit(character))
            {
                result.Append(character);
                separator = false;
            }
            else if (!separator && result.Length > 0)
            {
                result.Append('-');
                separator = true;
            }
        }
        string slug = result.ToString().Trim('-');
        return slug.Length == 0 ? "unknown" : slug;
    }

    private static bool IsWithin(string root, string path)
    {
        string boundary = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(boundary, StringComparison.OrdinalIgnoreCase);
    }

    private static void AddHash(HashAlgorithm hash, string label, string content)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(label + "\n" + content + "\n");
        hash.TransformBlock(bytes, 0, bytes.Length, null, 0);
    }

    private static void WriteTextAtomic(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
        string temporary = path + ".download";
        File.WriteAllText(temporary, content, new UTF8Encoding(false));
        if (File.Exists(path))
            File.Delete(path);
        File.Move(temporary, path);
    }

    private sealed class PokemonCardSubjectOverrideCatalog
    {
        private readonly IReadOnlyDictionary<string, PokemonCardSubjectOverride> entries;

        public PokemonCardSubjectOverrideCatalog(IReadOnlyDictionary<string, PokemonCardSubjectOverride> entries)
        {
            this.entries = entries;
        }

        public bool TryGet(string cardId, out PokemonCardSubjectOverride entry) =>
            entries.TryGetValue(cardId ?? string.Empty, out entry);
    }
}
