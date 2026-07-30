using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Gacha.Pokemon.Infrastructure;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

[Serializable]
public sealed class PokeApiTaxonomyIntegrityReport
{
    public int SchemaVersion = 1;
    public string GeneratedAtUtc;
    public string SourceSha256;
    public bool IsValid;
    public int GenerationCount;
    public int SpeciesCount;
    public int PokemonCount;
    public int FormCount;
    public int VersionGroupCount;
    public int GenerationOneSpeciesCount;
    public int FallbackWarningCount;
    public int ManualReviewCount;
    public int TemporaryFileCount;
    public int OrphanFileCount;
    public List<string> Failures = new List<string>();
}

public static class PokeApiTaxonomyIntegrityAuditor
{
    public static PokeApiTaxonomyIntegrityReport Audit(
        string outputRoot,
        string formClassificationPath,
        string reportPath = null)
    {
        if (string.IsNullOrWhiteSpace(outputRoot))
            throw new ArgumentException("Output root is required.", nameof(outputRoot));
        string root = Path.GetFullPath(outputRoot);
        string rawRoot = Path.Combine(root, "raw");
        var report = new PokeApiTaxonomyIntegrityReport
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
        };

        try
        {
            report.TemporaryFileCount = Directory.Exists(root)
                ? Directory.GetFiles(root, "*.download", SearchOption.AllDirectories).Length
                : 0;
            if (report.TemporaryFileCount > 0)
                report.Failures.Add($"Found {report.TemporaryFileCount} temporary download files.");

            PokeApiTaxonomyImportCheckpoint checkpoint = Read<PokeApiTaxonomyImportCheckpoint>(
                Path.Combine(root, "import-checkpoint.json"), "import checkpoint");
            if (!checkpoint.Complete || checkpoint.Failures == null || checkpoint.Failures.Count > 0)
                report.Failures.Add("Import checkpoint is not complete or still contains failures.");

            int[] generationIds = ReadListIds(Path.Combine(rawRoot, "lists", "generations.json"));
            int[] speciesIds = ReadListIds(Path.Combine(rawRoot, "lists", "species.json"));
            int[] versionGroupIds = ReadListIds(Path.Combine(rawRoot, "lists", "version-groups.json"));
            int[] pokemonIds = ReferencedIds(rawRoot, "species", speciesIds, "varieties[*].pokemon.url");
            int[] formIds = ReferencedIds(rawRoot, "pokemon", pokemonIds, "forms[*].url");

            report.GenerationCount = generationIds.Length;
            report.SpeciesCount = speciesIds.Length;
            report.PokemonCount = pokemonIds.Length;
            report.FormCount = formIds.Length;
            report.VersionGroupCount = versionGroupIds.Length;
            report.OrphanFileCount += ValidateFiles(rawRoot, "generations", generationIds, report.Failures);
            report.OrphanFileCount += ValidateFiles(rawRoot, "species", speciesIds, report.Failures);
            report.OrphanFileCount += ValidateFiles(rawRoot, "pokemon", pokemonIds, report.Failures);
            report.OrphanFileCount += ValidateFiles(rawRoot, "forms", formIds, report.Failures);
            report.OrphanFileCount += ValidateFiles(rawRoot, "version-groups", versionGroupIds, report.Failures);

            var raw = new PokeApiTaxonomyRawData();
            Load(raw.Generations, rawRoot, "generations", generationIds);
            Load(raw.Species, rawRoot, "species", speciesIds);
            Load(raw.Pokemon, rawRoot, "pokemon", pokemonIds);
            Load(raw.Forms, rawRoot, "forms", formIds);
            Load(raw.VersionGroups, rawRoot, "version-groups", versionGroupIds);

            string snapshotPath = Path.Combine(root, "snapshot", "pokemon-taxonomy.json");
            string snapshotJson = File.ReadAllText(snapshotPath, Encoding.UTF8);
            PokemonTaxonomySnapshotDto snapshot = JsonConvert.DeserializeObject<PokemonTaxonomySnapshotDto>(snapshotJson)
                ?? throw new InvalidDataException("Taxonomy snapshot is empty.");
            PokemonTaxonomySnapshotLoadResult loaded = new PokemonTaxonomySnapshotReader().Read(snapshotJson);
            PokemonFormClassificationCatalog classification =
                PokemonContentOverrideLoader.LoadFormClassification(formClassificationPath);
            PokeApiTaxonomyCompileResult rebuilt = PokeApiTaxonomyCompiler.Compile(
                raw,
                classification,
                DateTimeOffset.Parse(snapshot.CapturedAtUtc, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal));
            string rebuiltJson = JsonConvert.SerializeObject(rebuilt.Snapshot, Formatting.None);
            if (!JToken.DeepEquals(JToken.Parse(snapshotJson), JToken.Parse(rebuiltJson)))
                report.Failures.Add("Taxonomy snapshot does not match a deterministic rebuild from raw resources.");

            report.SourceSha256 = loaded.SourceSha256;
            report.FallbackWarningCount = snapshot.Warnings?.Count(value =>
                value.StartsWith("fallback:", StringComparison.Ordinal)) ?? 0;
            report.ManualReviewCount = snapshot.Forms?.Count(value =>
                value.Disposition == "manual-review") ?? 0;
            int[] generationOne = loaded.Catalog.GetSpeciesByGeneration("generation-1")
                .Select(value => value.NationalDexNumber).ToArray();
            report.GenerationOneSpeciesCount = generationOne.Length;
            if (!generationOne.SequenceEqual(Enumerable.Range(1, 151)))
                report.Failures.Add("Generation 1 must contain exactly national Pokedex #001-#151.");

            foreach (PokemonFormSnapshotDto form in snapshot.Forms ?? new List<PokemonFormSnapshotDto>())
            {
                if (!string.IsNullOrWhiteSpace(form.ImageSourceUrl) &&
                    (!Uri.TryCreate(form.ImageSourceUrl, UriKind.Absolute, out Uri imageUri) ||
                     imageUri.Scheme != Uri.UriSchemeHttps))
                    report.Failures.Add($"Form '{form.Id}' has a non-HTTPS image source.");
            }
            if (checkpoint.GenerationCount != generationIds.Length ||
                checkpoint.SpeciesCount != speciesIds.Length ||
                checkpoint.PokemonCount != pokemonIds.Length ||
                checkpoint.FormCount != formIds.Length ||
                checkpoint.VersionGroupCount != versionGroupIds.Length)
                report.Failures.Add("Import checkpoint counts do not match the raw resource graph.");
        }
        catch (Exception exception)
        {
            report.Failures.Add(exception.Message);
        }

        report.Failures = report.Failures.Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal).ToList();
        report.IsValid = report.Failures.Count == 0;
        if (!string.IsNullOrWhiteSpace(reportPath))
            WriteTextAtomic(reportPath, JsonConvert.SerializeObject(report, Formatting.Indented));
        return report;
    }

    private static int[] ReadListIds(string path)
    {
        JObject list = JObject.Parse(File.ReadAllText(path, Encoding.UTF8));
        JArray results = list["results"] as JArray ?? throw new InvalidDataException(
            "PokeAPI discovery list has no results: " + path);
        int count = list["count"]?.Value<int>() ?? -1;
        int[] ids = results.Select(value => ResourceId(value["url"]?.ToString()))
            .Distinct().OrderBy(value => value).ToArray();
        if (ids.Length == 0 || ids.Length != results.Count || ids.Length != count ||
            list["next"]?.Type != JTokenType.Null)
            throw new InvalidDataException("PokeAPI discovery list is incomplete: " + path);
        return ids;
    }

    private static int[] ReferencedIds(
        string rawRoot, string directory, IEnumerable<int> owners, string jsonPath)
    {
        var result = new SortedSet<int>();
        foreach (int id in owners)
        {
            JObject value = JObject.Parse(File.ReadAllText(
                Path.Combine(rawRoot, directory, id + ".json"), Encoding.UTF8));
            foreach (JToken token in value.SelectTokens(jsonPath))
                result.Add(ResourceId(token.ToString()));
        }
        if (result.Count == 0)
            throw new InvalidDataException($"PokeAPI {directory} resources contain no references.");
        return result.ToArray();
    }

    private static int ValidateFiles(
        string rawRoot, string directory, IEnumerable<int> expected, ICollection<string> failures)
    {
        string path = Path.Combine(rawRoot, directory);
        var expectedNames = new HashSet<string>(
            expected.Select(value => value + ".json"), StringComparer.OrdinalIgnoreCase);
        string[] actual = Directory.Exists(path)
            ? Directory.GetFiles(path, "*.json", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName).ToArray()
            : Array.Empty<string>();
        foreach (string missing in expectedNames.Except(actual, StringComparer.OrdinalIgnoreCase))
            failures.Add($"Missing raw {directory} resource: {missing}.");
        string[] orphans = actual.Except(expectedNames, StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (string orphan in orphans)
            failures.Add($"Orphan raw {directory} resource: {orphan}.");
        return orphans.Length;
    }

    private static void Load(
        IDictionary<int, string> target, string rawRoot, string directory, IEnumerable<int> ids)
    {
        foreach (int id in ids)
            target.Add(id, File.ReadAllText(
                Path.Combine(rawRoot, directory, id + ".json"), Encoding.UTF8));
    }

    private static int ResourceId(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("PokeAPI resource URL must use HTTPS.");
        string segment = uri.Segments.Select(value => value.Trim('/'))
            .LastOrDefault(value => int.TryParse(value, out _));
        if (!int.TryParse(segment, out int result) || result < 1)
            throw new InvalidDataException("PokeAPI resource URL has no numeric id.");
        return result;
    }

    private static T Read<T>(string path, string label)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Missing " + label + ".", path);
        return JsonConvert.DeserializeObject<T>(File.ReadAllText(path, Encoding.UTF8))
            ?? throw new InvalidDataException("Invalid " + label + ".");
    }

    private static void WriteTextAtomic(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
        string temporaryPath = path + ".download";
        File.WriteAllText(temporaryPath, content, new UTF8Encoding(false));
        if (File.Exists(path))
            File.Delete(path);
        File.Move(temporaryPath, path);
    }
}
