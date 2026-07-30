using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

[Serializable]
public sealed class PokemonSetGenerationPolicyFile
{
    public int SchemaVersion = 1;
    public List<PokemonSetGenerationPolicy> Policies = new List<PokemonSetGenerationPolicy>();
}

[Serializable]
public sealed class PokemonSetGenerationPolicy
{
    public string SeriesId;
    public string EraId;
    public string GenerationId;
    public int GenerationOrder;
    public string ReleaseDateFrom;
    public string ReleaseDateTo;
}

public sealed class PokemonSetGenerationCompileResult
{
    public PokemonSetGenerationOverrideFile File;
    public int SourceSetCount;
    public int PolicyCount;
}

public static class PokemonSetGenerationCompiler
{
    public const int SupportedPolicySchemaVersion = 1;

    public static PokemonSetGenerationCompileResult CompileFiles(
        string inventoryPath, string policyPath, string outputPath)
    {
        ContentInventorySnapshot inventory = Read<ContentInventorySnapshot>(inventoryPath, "inventory");
        PokemonSetGenerationPolicyFile policies =
            Read<PokemonSetGenerationPolicyFile>(policyPath, "generation policy");
        PokemonSetGenerationCompileResult result = Compile(inventory, policies);
        WriteAtomic(outputPath, JsonConvert.SerializeObject(result.File, Formatting.Indented,
            new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            }));
        return result;
    }

    public static PokemonSetGenerationCompileResult Compile(
        ContentInventorySnapshot inventory, PokemonSetGenerationPolicyFile policyFile)
    {
        if (inventory == null)
            throw new ArgumentNullException(nameof(inventory));
        if (policyFile == null)
            throw new ArgumentNullException(nameof(policyFile));
        if (inventory.SchemaVersion != 1)
            throw new PokemonContentOverrideException(
                $"Inventory schema {inventory.SchemaVersion} is not supported by the generation compiler.");
        if (policyFile.SchemaVersion != SupportedPolicySchemaVersion)
            throw new PokemonContentOverrideException(
                $"Generation policy schema {policyFile.SchemaVersion} is not supported.");
        if (string.IsNullOrWhiteSpace(inventory.ReferenceLanguage))
            throw new PokemonContentOverrideException("Inventory requires ReferenceLanguage.");

        List<CompiledPolicy> policies = ValidatePolicies(policyFile.Policies);
        string language = inventory.ReferenceLanguage.Trim().ToLowerInvariant();
        List<ContentInventorySetRecord> sourceSets = (inventory.Sets ?? new List<ContentInventorySetRecord>())
            .Where(item => string.Equals(item.Language, language, StringComparison.Ordinal))
            .OrderBy(item => item.ReleaseDate, StringComparer.Ordinal)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToList();
        if (sourceSets.Count == 0)
            throw new PokemonContentOverrideException(
                $"Inventory contains no detailed Sets for reference language '{language}'.");
        EnsureUniqueSetIds(sourceSets);

        var compiled = new List<CompiledSet>(sourceSets.Count);
        foreach (ContentInventorySetRecord set in sourceSets)
        {
            DateTime releaseDate = ParseDate(set.ReleaseDate, $"Set '{set.Id}' release date");
            List<CompiledPolicy> matches = policies
                .Where(policy => policy.Matches(set.SeriesId, releaseDate))
                .ToList();
            if (matches.Count != 1)
                throw new PokemonContentOverrideException(
                    $"Set '{set.Id}' ({set.SeriesId}, {set.ReleaseDate}) matched {matches.Count} generation policies; expected exactly one.");
            if (string.IsNullOrWhiteSpace(set.SetCode))
                throw new PokemonContentOverrideException($"Set '{set.Id}' requires SetCode.");
            compiled.Add(new CompiledSet(set, matches[0]));
        }

        var file = new PokemonSetGenerationOverrideFile
        {
            SourceInventorySha256 = inventory.ContentSha256,
            SourceLanguage = language
        };
        foreach (IGrouping<int, CompiledSet> generation in compiled
                     .GroupBy(item => item.Policy.GenerationOrder)
                     .OrderBy(group => group.Key))
        {
            int ordinal = 0;
            foreach (CompiledSet item in generation
                         .OrderBy(value => value.Set.ReleaseDate, StringComparer.Ordinal)
                         .ThenBy(value => value.Set.Id, StringComparer.Ordinal))
            {
                ordinal++;
                file.Sets.Add(new PokemonSetGenerationOverride
                {
                    SetId = item.Set.Id.Trim(),
                    SetCode = item.Set.SetCode.Trim(),
                    EraId = item.Policy.EraId,
                    GenerationId = item.Policy.GenerationId,
                    GenerationOrder = item.Policy.GenerationOrder,
                    SetOrdinal = ordinal
                });
            }
        }

        return new PokemonSetGenerationCompileResult
        {
            File = file,
            SourceSetCount = sourceSets.Count,
            PolicyCount = policies.Count
        };
    }

    private static List<CompiledPolicy> ValidatePolicies(
        IEnumerable<PokemonSetGenerationPolicy> source)
    {
        var result = new List<CompiledPolicy>();
        foreach (PokemonSetGenerationPolicy policy in source ??
                 Enumerable.Empty<PokemonSetGenerationPolicy>())
        {
            if (policy == null || string.IsNullOrWhiteSpace(policy.SeriesId) ||
                string.IsNullOrWhiteSpace(policy.EraId) ||
                string.IsNullOrWhiteSpace(policy.GenerationId))
                throw new PokemonContentOverrideException(
                    "Generation policy requires SeriesId, EraId, and GenerationId.");
            if (policy.GenerationOrder < 1)
                throw new PokemonContentOverrideException(
                    $"Generation policy '{policy.SeriesId}' requires GenerationOrder >= 1.");
            DateTime? from = ParseOptionalDate(
                policy.ReleaseDateFrom, $"Policy '{policy.SeriesId}' ReleaseDateFrom");
            DateTime? to = ParseOptionalDate(
                policy.ReleaseDateTo, $"Policy '{policy.SeriesId}' ReleaseDateTo");
            if (from.HasValue && to.HasValue && from.Value > to.Value)
                throw new PokemonContentOverrideException(
                    $"Generation policy '{policy.SeriesId}' has an inverted date range.");
            result.Add(new CompiledPolicy(
                policy.SeriesId.Trim(), policy.EraId.Trim(), policy.GenerationId.Trim(),
                policy.GenerationOrder, from, to));
        }
        if (result.Count == 0)
            throw new PokemonContentOverrideException("At least one generation policy is required.");

        foreach (IGrouping<string, CompiledPolicy> series in result.GroupBy(
                     item => item.SeriesId, StringComparer.Ordinal))
        {
            List<CompiledPolicy> ordered = series
                .OrderBy(item => item.From ?? DateTime.MinValue)
                .ThenBy(item => item.To ?? DateTime.MaxValue)
                .ToList();
            for (int index = 1; index < ordered.Count; index++)
            {
                DateTime previousEnd = ordered[index - 1].To ?? DateTime.MaxValue;
                DateTime currentStart = ordered[index].From ?? DateTime.MinValue;
                if (currentStart <= previousEnd)
                    throw new PokemonContentOverrideException(
                        $"Generation policies for series '{series.Key}' overlap.");
            }
        }
        return result;
    }

    private static void EnsureUniqueSetIds(IEnumerable<ContentInventorySetRecord> sets)
    {
        string duplicate = sets
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .FirstOrDefault();
        if (duplicate != null)
            throw new PokemonContentOverrideException(
                $"Inventory contains duplicate reference-language Set ID '{duplicate}'.");
    }

    private static DateTime ParseDate(string value, string context)
    {
        if (!DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateTime result))
            throw new PokemonContentOverrideException(
                $"{context} must use yyyy-MM-dd, got '{value}'.");
        return result;
    }

    private static DateTime? ParseOptionalDate(string value, string context)
    {
        return string.IsNullOrWhiteSpace(value) ? null : ParseDate(value.Trim(), context);
    }

    private static T Read<T>(string path, string description) where T : class
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException($"{description} file was not found.", path);
        try
        {
            T result = JsonConvert.DeserializeObject<T>(File.ReadAllText(path));
            return result ?? throw new PokemonContentOverrideException(
                $"{description} file is empty: {path}");
        }
        catch (JsonException exception)
        {
            throw new PokemonContentOverrideException(
                $"Failed to parse {description} file: {path}", exception);
        }
    }

    private static void WriteAtomic(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
        string temporaryPath = path + ".download";
        File.WriteAllText(temporaryPath, text, new UTF8Encoding(false));
        if (File.Exists(path))
            File.Delete(path);
        File.Move(temporaryPath, path);
    }

    private sealed class CompiledSet
    {
        public CompiledSet(ContentInventorySetRecord set, CompiledPolicy policy)
        {
            Set = set;
            Policy = policy;
        }

        public ContentInventorySetRecord Set { get; }
        public CompiledPolicy Policy { get; }
    }

    private sealed class CompiledPolicy
    {
        public CompiledPolicy(
            string seriesId, string eraId, string generationId, int generationOrder,
            DateTime? from, DateTime? to)
        {
            SeriesId = seriesId;
            EraId = eraId;
            GenerationId = generationId;
            GenerationOrder = generationOrder;
            From = from;
            To = to;
        }

        public string SeriesId { get; }
        public string EraId { get; }
        public string GenerationId { get; }
        public int GenerationOrder { get; }
        public DateTime? From { get; }
        public DateTime? To { get; }

        public bool Matches(string seriesId, DateTime releaseDate)
        {
            return string.Equals(SeriesId, seriesId, StringComparison.Ordinal) &&
                   (!From.HasValue || releaseDate >= From.Value) &&
                   (!To.HasValue || releaseDate <= To.Value);
        }
    }
}
