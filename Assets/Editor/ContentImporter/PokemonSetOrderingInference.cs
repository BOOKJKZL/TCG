using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

public static class PokemonSetOrderingInference
{
    private static readonly IReadOnlyDictionary<string, int> SeriesGenerations =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["PMCG"] = 1,
            ["base"] = 1,
            ["gym"] = 1,
            ["neo"] = 2,
            ["VS"] = 2,
            ["web"] = 2,
            ["e"] = 2,
            ["ecard"] = 2,
            ["ADV"] = 3,
            ["PCG"] = 3,
            ["ex"] = 3,
            ["DP"] = 4,
            ["DPt"] = 4,
            ["L"] = 4,
            ["dp"] = 4,
            ["pl"] = 4,
            ["hgss"] = 4,
            ["BW"] = 5,
            ["bw"] = 5,
            ["XY"] = 6,
            ["XYb"] = 6,
            ["xy"] = 6,
            ["SM"] = 7,
            ["sm"] = 7,
            ["S"] = 8,
            ["swsh"] = 8,
            ["SV"] = 9,
            ["sv"] = 9,
            ["M"] = 9
        };

    public static bool TryApply(ImportedSetRecord set)
    {
        if (set == null)
            throw new ArgumentNullException(nameof(set));
        if (!string.IsNullOrWhiteSpace(set.GenerationId) &&
            !string.Equals(set.GenerationId, "unmapped", StringComparison.OrdinalIgnoreCase) &&
            set.GenerationOrder.HasValue && set.SetOrdinal.HasValue)
            return true;

        int generation;
        if (!string.IsNullOrWhiteSpace(set.SeriesId) &&
            SeriesGenerations.TryGetValue(set.SeriesId.Trim(), out int mapped))
        {
            generation = mapped;
        }
        else if (DateTime.TryParseExact(set.ReleaseDate, "yyyy-MM-dd",
                     CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime releaseDate))
        {
            generation = GenerationAt(releaseDate);
        }
        else
        {
            return false;
        }

        set.GenerationId = "generation-" + generation;
        set.GenerationOrder = generation;
        set.SetOrdinal = set.SetOrdinal ?? 1;
        if (string.IsNullOrWhiteSpace(set.EraId))
            set.EraId = string.IsNullOrWhiteSpace(set.SeriesId)
                ? "generation-" + generation
                : set.SeriesId.Trim();
        if (string.IsNullOrWhiteSpace(set.SetCode))
            set.SetCode = set.Id;
        return true;
    }

    private static int GenerationAt(DateTime releaseDate)
    {
        if (releaseDate < new DateTime(1999, 11, 21)) return 1;
        if (releaseDate < new DateTime(2002, 11, 21)) return 2;
        if (releaseDate < new DateTime(2006, 9, 28)) return 3;
        if (releaseDate < new DateTime(2010, 9, 18)) return 4;
        if (releaseDate < new DateTime(2013, 10, 12)) return 5;
        if (releaseDate < new DateTime(2016, 11, 18)) return 6;
        if (releaseDate < new DateTime(2019, 11, 15)) return 7;
        if (releaseDate < new DateTime(2022, 11, 18)) return 8;
        return 9;
    }

    public static IReadOnlyDictionary<string, int> BuildSequentialOrdinals(
        IEnumerable<ContentInventorySetRecord> records,
        string language)
    {
        string normalizedLanguage = (language ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedLanguage.Length == 0)
            throw new ArgumentException("Language is required.", nameof(language));
        ContentInventorySetRecord[] selected = (records ?? Enumerable.Empty<ContentInventorySetRecord>())
            .Where(value => value != null && string.Equals(
                value.Language, normalizedLanguage, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (selected.Length == 0)
            throw new InvalidDataException("Inventory has no Sets for language: " + normalizedLanguage);
        if (selected.Any(value => string.IsNullOrWhiteSpace(value.Id) || !value.GenerationOrder.HasValue))
            throw new InvalidDataException("Every ordered Set requires an id and generation order.");

        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (IGrouping<int, ContentInventorySetRecord> generation in selected
                     .GroupBy(value => value.GenerationOrder.Value)
                     .OrderBy(value => value.Key))
        {
            int ordinal = 1;
            foreach (ContentInventorySetRecord set in generation
                         .OrderBy(value => string.IsNullOrWhiteSpace(value.ReleaseDate) ? 1 : 0)
                         .ThenBy(value => value.ReleaseDate ?? string.Empty, StringComparer.Ordinal)
                         .ThenBy(value => value.SetCode ?? value.Id, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(value => value.Id, StringComparer.Ordinal))
            {
                if (!result.TryAdd(set.Id, ordinal++))
                    throw new InvalidDataException("Duplicate inventory Set id: " + set.Id);
            }
        }
        return result;
    }
}

public sealed class ContentImportSetOrderingNormalizationResult
{
    public string Language;
    public int SetCount;
    public int UpdatedSetCount;
}

public static class ContentImportSetOrderingNormalizer
{
    public static ContentImportSetOrderingNormalizationResult Normalize(
        string importRoot,
        string language,
        IReadOnlyDictionary<string, int> ordinals)
    {
        if (string.IsNullOrWhiteSpace(importRoot))
            throw new ArgumentException("Import root is required.", nameof(importRoot));
        string normalizedLanguage = (language ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedLanguage.Length == 0)
            throw new ArgumentException("Language is required.", nameof(language));
        if (ordinals == null || ordinals.Count == 0)
            throw new ArgumentException("Set ordinals are required.", nameof(ordinals));
        string languageRoot = Path.Combine(importRoot, normalizedLanguage);
        if (!Directory.Exists(languageRoot))
            throw new DirectoryNotFoundException("Imported language root was not found: " + languageRoot);

        var result = new ContentImportSetOrderingNormalizationResult
        {
            Language = normalizedLanguage
        };
        foreach (string manifestPath in Directory.GetFiles(
                     languageRoot, "manifest.json", SearchOption.AllDirectories)
                 .OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            PrivateContentManifest manifest = JsonConvert.DeserializeObject<PrivateContentManifest>(
                File.ReadAllText(manifestPath, Encoding.UTF8));
            if (manifest?.Set == null || !string.Equals(
                    manifest.Language, normalizedLanguage, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Manifest language or Set is invalid: " + manifestPath);
            if (!ordinals.TryGetValue(manifest.Set.Id, out int ordinal) || ordinal < 1)
                throw new InvalidDataException("Missing valid ordinal for Set: " + manifest.Set.Id);
            result.SetCount++;
            if (manifest.Set.SetOrdinal == ordinal)
                continue;
            manifest.Set.SetOrdinal = ordinal;
            WriteTextAtomic(manifestPath, JsonConvert.SerializeObject(manifest, Formatting.Indented));
            result.UpdatedSetCount++;
        }
        if (result.SetCount != ordinals.Count)
            throw new InvalidDataException(
                $"Imported Set count {result.SetCount} differs from ordering map {ordinals.Count}.");
        return result;
    }

    private static void WriteTextAtomic(string path, string value)
    {
        string temporary = path + ".download";
        File.WriteAllText(temporary, value, new UTF8Encoding(false));
        if (File.Exists(path))
            File.Delete(path);
        File.Move(temporary, path);
    }
}
