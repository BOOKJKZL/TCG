using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

[Serializable]
public sealed class MultilingualCoverageExpectation
{
    public string Language;
    public int SetCount;
    public int CardCount;

    public MultilingualCoverageExpectation(string language, int setCount, int cardCount)
    {
        Language = language;
        SetCount = setCount;
        CardCount = cardCount;
    }
}

[Serializable]
public sealed class MultilingualCoverageLanguageSummary
{
    public string Language;
    public int SetCount;
    public int CardCount;
    public int ImageCount;
    public int MissingImageCount;
    public long ImageBytes;
    public int SourceErrorCount;
    public int DirectCandidateCardCount;
    public int UnmatchedCardCount;
}

[Serializable]
public sealed class MultilingualCoverageCardRecord
{
    public string RecordId;
    public string ManifestPath;
    public string Source;
    public string Language;
    public string SetId;
    public string SetName;
    public string SeriesId;
    public string ReleaseDate;
    public string CardId;
    public string LocalId;
    public string CardName;
    public string Category;
    public string Rarity;
    public string Illustrator;
    public List<string> Types = new List<string>();
    public string VariantKey;
    public string ImageStatus;
    public string ImageSha256;
    public long ImageBytes;
    public string Status;
    public List<string> DirectCandidateStrategies = new List<string>();
}

[Serializable]
public sealed class MultilingualCoverageCandidateMember
{
    public string RecordId;
    public string Language;
    public string SetId;
    public string CardId;
    public string LocalId;
    public string CardName;
}

[Serializable]
public sealed class MultilingualCoverageCandidateGroup
{
    public string Strategy;
    public string Key;
    public string LanguageCombination;
    public List<MultilingualCoverageCandidateMember> Members =
        new List<MultilingualCoverageCandidateMember>();
}

[Serializable]
public sealed class MultilingualProductionCoverageReport
{
    public int SchemaVersion = 1;
    public bool IsValid;
    public string SnapshotSha256;
    public int TotalSetCount;
    public int TotalCardCount;
    public int TotalImageCount;
    public int TotalMissingImageCount;
    public long TotalImageBytes;
    public int TotalSourceErrorCount;
    public int DirectCandidateGroupCount;
    public int DirectCandidateCardCount;
    public int UnmatchedCardCount;
    public List<MultilingualCoverageLanguageSummary> Languages =
        new List<MultilingualCoverageLanguageSummary>();
    public List<MultilingualCoverageCandidateGroup> CandidateGroups =
        new List<MultilingualCoverageCandidateGroup>();
    public List<MultilingualCoverageCardRecord> Cards =
        new List<MultilingualCoverageCardRecord>();
    public List<string> Failures = new List<string>();
}

public static class MultilingualProductionCoverageAuditor
{
    private const string SetLocalStrategy = "same-set-and-local-id";
    private const string SourceCardStrategy = "same-source-card-id";
    private const string ImageHashStrategy = "same-image-sha256";

    public static MultilingualProductionCoverageReport Audit(
        string importRoot,
        IEnumerable<string> languages,
        IEnumerable<MultilingualCoverageExpectation> expectations = null,
        string jsonOutputPath = null,
        string markdownOutputPath = null)
    {
        if (string.IsNullOrWhiteSpace(importRoot))
            throw new ArgumentException("Import root is required.", nameof(importRoot));

        string root = Path.GetFullPath(importRoot);
        string[] requestedLanguages = (languages ?? Array.Empty<string>())
            .Select(NormalizeLanguage)
            .Where(value => value != null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (requestedLanguages.Length == 0)
            throw new ArgumentException("At least one language is required.", nameof(languages));

        Dictionary<string, MultilingualCoverageExpectation> expected =
            (expectations ?? Array.Empty<MultilingualCoverageExpectation>())
            .Where(value => value != null && NormalizeLanguage(value.Language) != null)
            .ToDictionary(value => NormalizeLanguage(value.Language), value => value,
                StringComparer.OrdinalIgnoreCase);
        var report = new MultilingualProductionCoverageReport();
        var sourceErrorCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var setCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (string language in requestedLanguages)
            ReadLanguage(root, language, report, setCounts, sourceErrorCounts);

        report.Cards = report.Cards
            .OrderBy(value => value.Language, StringComparer.Ordinal)
            .ThenBy(value => value.SetId, StringComparer.Ordinal)
            .ThenBy(value => value.LocalId, NaturalStringComparer.Instance)
            .ThenBy(value => value.CardId, StringComparer.Ordinal)
            .ThenBy(value => value.RecordId, StringComparer.Ordinal)
            .ToList();
        RejectDuplicateRecordIds(report);
        report.CandidateGroups = BuildCandidateGroups(report.Cards);
        ApplyCandidateClassifications(report.Cards, report.CandidateGroups);

        foreach (string language in requestedLanguages)
        {
            MultilingualCoverageCardRecord[] cards = report.Cards
                .Where(value => string.Equals(value.Language, language,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var summary = new MultilingualCoverageLanguageSummary
            {
                Language = language,
                SetCount = Value(setCounts, language),
                CardCount = cards.Length,
                ImageCount = cards.Count(value => value.ImageStatus == "available"),
                MissingImageCount = cards.Count(value => value.ImageStatus != "available"),
                ImageBytes = cards.Where(value => value.ImageStatus == "available")
                    .Sum(value => value.ImageBytes),
                SourceErrorCount = Value(sourceErrorCounts, language),
                DirectCandidateCardCount = cards.Count(value => value.Status == "direct-candidate"),
                UnmatchedCardCount = cards.Count(value => value.Status == "unmatched")
            };
            report.Languages.Add(summary);

            if (expected.TryGetValue(language, out MultilingualCoverageExpectation expectation))
            {
                if (summary.SetCount != expectation.SetCount)
                    report.Failures.Add(
                        $"Language '{language}' expected {expectation.SetCount} Sets, found {summary.SetCount}.");
                if (summary.CardCount != expectation.CardCount)
                    report.Failures.Add(
                        $"Language '{language}' expected {expectation.CardCount} cards, found {summary.CardCount}.");
            }
        }

        report.TotalSetCount = report.Languages.Sum(value => value.SetCount);
        report.TotalCardCount = report.Languages.Sum(value => value.CardCount);
        report.TotalImageCount = report.Languages.Sum(value => value.ImageCount);
        report.TotalMissingImageCount = report.Languages.Sum(value => value.MissingImageCount);
        report.TotalImageBytes = report.Languages.Sum(value => value.ImageBytes);
        report.TotalSourceErrorCount = report.Languages.Sum(value => value.SourceErrorCount);
        report.DirectCandidateGroupCount = report.CandidateGroups.Count;
        report.DirectCandidateCardCount = report.Cards.Count(value => value.Status == "direct-candidate");
        report.UnmatchedCardCount = report.Cards.Count(value => value.Status == "unmatched");
        if (report.DirectCandidateCardCount + report.UnmatchedCardCount != report.TotalCardCount)
            report.Failures.Add("Card classifications do not cover every source card exactly once.");

        report.Failures = report.Failures
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();
        report.IsValid = report.Failures.Count == 0;
        report.SnapshotSha256 = ComputeSnapshotSha256(report);

        if (!string.IsNullOrWhiteSpace(jsonOutputPath))
            WriteAtomic(jsonOutputPath, JsonConvert.SerializeObject(report, Formatting.Indented));
        if (!string.IsNullOrWhiteSpace(markdownOutputPath))
            WriteAtomic(markdownOutputPath, BuildMarkdown(report));
        return report;
    }

    private static void ReadLanguage(
        string root,
        string language,
        MultilingualProductionCoverageReport report,
        IDictionary<string, int> setCounts,
        IDictionary<string, int> sourceErrorCounts)
    {
        string languageRoot = Path.GetFullPath(Path.Combine(root, language));
        if (!Directory.Exists(languageRoot))
        {
            report.Failures.Add($"Language import directory is missing: {language}.");
            setCounts[language] = 0;
            sourceErrorCounts[language] = 0;
            return;
        }

        string[] manifestPaths = Directory.GetFiles(
                languageRoot, "manifest.json", SearchOption.AllDirectories)
            .OrderBy(value => Relative(languageRoot, value), StringComparer.Ordinal)
            .ToArray();
        setCounts[language] = manifestPaths.Length;
        int sourceErrors = 0;
        foreach (string manifestPath in manifestPaths)
        {
            PrivateContentManifest manifest;
            string relativeManifest = Relative(languageRoot, manifestPath);
            try
            {
                manifest = JsonConvert.DeserializeObject<PrivateContentManifest>(
                    File.ReadAllText(manifestPath, Encoding.UTF8));
            }
            catch (Exception exception) when (exception is IOException || exception is JsonException)
            {
                report.Failures.Add($"{language}/{relativeManifest}: {exception.Message}");
                continue;
            }

            if (manifest?.Set == null)
            {
                report.Failures.Add($"{language}/{relativeManifest}: Manifest or Set record is missing.");
                continue;
            }
            if (manifest.SchemaVersion != 2)
                report.Failures.Add(
                    $"{language}/{relativeManifest}: Expected schema 2, got {manifest.SchemaVersion}.");
            if (!string.Equals(NormalizeLanguage(manifest.Language), language,
                    StringComparison.OrdinalIgnoreCase))
                report.Failures.Add(
                    $"{language}/{relativeManifest}: Manifest language is '{manifest.Language}'.");

            sourceErrors += (manifest.Errors ?? new List<ContentImportError>()).Count;
            string setDirectory = Path.GetDirectoryName(manifestPath) ?? languageRoot;
            string setId = RequiredOrFallback(manifest.Set.Id, Path.GetFileName(setDirectory));
            int cardIndex = 0;
            foreach (ImportedCardRecord card in manifest.Cards ?? new List<ImportedCardRecord>())
            {
                cardIndex++;
                if (card == null || string.IsNullOrWhiteSpace(card.Id) ||
                    string.IsNullOrWhiteSpace(card.LocalId))
                {
                    report.Failures.Add(
                        $"{language}/{relativeManifest}: Card {cardIndex} has no source ID or local ID.");
                    continue;
                }

                string cardId = card.Id.Trim();
                string localId = card.LocalId.Trim();
                string recordId = string.Join("|", language, relativeManifest.Replace('\\', '/'),
                    cardId, localId);
                string imageStatus = ResolveImageStatus(setDirectory, card, report, recordId);
                report.Cards.Add(new MultilingualCoverageCardRecord
                {
                    RecordId = recordId,
                    ManifestPath = relativeManifest.Replace('\\', '/'),
                    Source = RequiredOrFallback(manifest.Source, "unknown"),
                    Language = language,
                    SetId = setId,
                    SetName = RequiredOrFallback(manifest.Set.Name, setId),
                    SeriesId = manifest.Set.SeriesId?.Trim(),
                    ReleaseDate = manifest.Set.ReleaseDate?.Trim(),
                    CardId = cardId,
                    LocalId = localId,
                    CardName = RequiredOrFallback(card.Name, cardId),
                    Category = card.Category?.Trim(),
                    Rarity = card.Rarity?.Trim(),
                    Illustrator = card.Illustrator?.Trim(),
                    Types = (card.Types ?? new List<string>())
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Select(value => value.Trim())
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(value => value, StringComparer.Ordinal)
                        .ToList(),
                    VariantKey = VariantKey(card.Variants),
                    ImageStatus = imageStatus,
                    ImageSha256 = NormalizeSha256(card.ImageSha256),
                    ImageBytes = imageStatus == "available" ? card.ImageBytes : 0L,
                    Status = "unmatched"
                });
            }
        }
        sourceErrorCounts[language] = sourceErrors;
    }

    private static string ResolveImageStatus(
        string setDirectory,
        ImportedCardRecord card,
        MultilingualProductionCoverageReport report,
        string recordId)
    {
        if (string.IsNullOrWhiteSpace(card.ImageRelativePath))
            return "not-referenced";
        string imagePath = ResolveWithin(setDirectory, card.ImageRelativePath);
        if (imagePath == null)
        {
            report.Failures.Add($"{recordId}: Image path escapes its Set directory.");
            return "unsafe-path";
        }
        if (!File.Exists(imagePath))
        {
            report.Failures.Add($"{recordId}: Referenced image is missing.");
            return "file-missing";
        }
        long length = new FileInfo(imagePath).Length;
        if (card.ImageBytes <= 0 || length != card.ImageBytes)
        {
            report.Failures.Add(
                $"{recordId}: Image bytes are invalid (manifest={card.ImageBytes}, file={length}).");
            return "invalid-size";
        }
        if (NormalizeSha256(card.ImageSha256) == null)
        {
            report.Failures.Add($"{recordId}: Image SHA-256 is missing or invalid.");
            return "invalid-hash";
        }
        return "available";
    }

    private static List<MultilingualCoverageCandidateGroup> BuildCandidateGroups(
        IEnumerable<MultilingualCoverageCardRecord> cards)
    {
        var groups = new List<MultilingualCoverageCandidateGroup>();
        AddCandidateGroups(groups, cards, SetLocalStrategy,
            value => Key(value.SetId, value.LocalId));
        AddCandidateGroups(groups, cards, SourceCardStrategy,
            value => Key(value.Source, value.CardId));
        AddCandidateGroups(groups, cards.Where(value => value.ImageStatus == "available"),
            ImageHashStrategy, value => value.ImageSha256);
        return groups
            .OrderBy(value => value.Strategy, StringComparer.Ordinal)
            .ThenBy(value => value.Key, StringComparer.Ordinal)
            .ToList();
    }

    private static void AddCandidateGroups(
        ICollection<MultilingualCoverageCandidateGroup> destination,
        IEnumerable<MultilingualCoverageCardRecord> cards,
        string strategy,
        Func<MultilingualCoverageCardRecord, string> keySelector)
    {
        foreach (IGrouping<string, MultilingualCoverageCardRecord> candidate in cards
                     .Select(value => new { Card = value, Key = keySelector(value) })
                     .Where(value => !string.IsNullOrWhiteSpace(value.Key))
                     .GroupBy(value => value.Key, value => value.Card, StringComparer.Ordinal)
                     .OrderBy(value => value.Key, StringComparer.Ordinal))
        {
            string[] languages = candidate.Select(value => value.Language)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            if (languages.Length < 2)
                continue;
            destination.Add(new MultilingualCoverageCandidateGroup
            {
                Strategy = strategy,
                Key = candidate.Key,
                LanguageCombination = string.Join("+", languages),
                Members = candidate
                    .OrderBy(value => value.Language, StringComparer.Ordinal)
                    .ThenBy(value => value.SetId, StringComparer.Ordinal)
                    .ThenBy(value => value.LocalId, NaturalStringComparer.Instance)
                    .ThenBy(value => value.CardId, StringComparer.Ordinal)
                    .Select(value => new MultilingualCoverageCandidateMember
                    {
                        RecordId = value.RecordId,
                        Language = value.Language,
                        SetId = value.SetId,
                        CardId = value.CardId,
                        LocalId = value.LocalId,
                        CardName = value.CardName
                    }).ToList()
            });
        }
    }

    private static void ApplyCandidateClassifications(
        IEnumerable<MultilingualCoverageCardRecord> cards,
        IEnumerable<MultilingualCoverageCandidateGroup> groups)
    {
        var strategies = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (MultilingualCoverageCandidateGroup group in groups)
        foreach (MultilingualCoverageCandidateMember member in group.Members)
        {
            if (!strategies.TryGetValue(member.RecordId, out HashSet<string> values))
            {
                values = new HashSet<string>(StringComparer.Ordinal);
                strategies.Add(member.RecordId, values);
            }
            values.Add(group.Strategy);
        }

        foreach (MultilingualCoverageCardRecord card in cards)
        {
            card.DirectCandidateStrategies = strategies.TryGetValue(card.RecordId,
                    out HashSet<string> values)
                ? values.OrderBy(value => value, StringComparer.Ordinal).ToList()
                : new List<string>();
            card.Status = card.DirectCandidateStrategies.Count > 0
                ? "direct-candidate"
                : "unmatched";
        }
    }

    private static void RejectDuplicateRecordIds(MultilingualProductionCoverageReport report)
    {
        foreach (IGrouping<string, MultilingualCoverageCardRecord> duplicate in report.Cards
                     .GroupBy(value => value.RecordId, StringComparer.Ordinal)
                     .Where(value => value.Count() > 1))
            report.Failures.Add($"Duplicate semantic record ID: {duplicate.Key}.");
    }

    private static string ComputeSnapshotSha256(MultilingualProductionCoverageReport report)
    {
        string previous = report.SnapshotSha256;
        report.SnapshotSha256 = null;
        byte[] bytes = Encoding.UTF8.GetBytes(
            JsonConvert.SerializeObject(report, Formatting.None));
        report.SnapshotSha256 = previous;
        using SHA256 sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(bytes).Select(value => value.ToString("x2")));
    }

    private static string BuildMarkdown(MultilingualProductionCoverageReport report)
    {
        var text = new StringBuilder();
        text.AppendLine("# Multilingual production coverage audit");
        text.AppendLine();
        text.AppendLine($"Valid: `{report.IsValid}`");
        text.AppendLine($"Snapshot SHA-256: `{report.SnapshotSha256}`");
        text.AppendLine();
        text.AppendLine("| Language | Sets | Cards | Images | Missing | Image bytes | Direct candidates | Unmatched | Source errors |");
        text.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (MultilingualCoverageLanguageSummary language in report.Languages)
            text.AppendLine($"| {language.Language} | {language.SetCount} | {language.CardCount} | " +
                            $"{language.ImageCount} | {language.MissingImageCount} | {language.ImageBytes} | " +
                            $"{language.DirectCandidateCardCount} | {language.UnmatchedCardCount} | " +
                            $"{language.SourceErrorCount} |");
        text.AppendLine();
        text.AppendLine($"Candidate groups: {report.DirectCandidateGroupCount}");
        text.AppendLine();
        text.AppendLine("| Strategy | Language combination | Groups | Members |");
        text.AppendLine("|---|---|---:|---:|");
        foreach (IGrouping<string, MultilingualCoverageCandidateGroup> strategy in
                 report.CandidateGroups.GroupBy(
                     value => value.Strategy + "\n" + value.LanguageCombination,
                     StringComparer.Ordinal).OrderBy(value => value.Key, StringComparer.Ordinal))
        {
            string[] parts = strategy.Key.Split('\n');
            text.AppendLine($"| {parts[0]} | {parts[1]} | {strategy.Count()} | " +
                            $"{strategy.Sum(value => value.Members.Count)} |");
        }
        text.AppendLine();
        text.AppendLine("## Failures");
        text.AppendLine();
        if (report.Failures.Count == 0)
            text.AppendLine("- None.");
        else
            foreach (string failure in report.Failures)
                text.AppendLine("- " + failure);
        return text.ToString().Replace("\r\n", "\n");
    }

    private static void WriteAtomic(string path, string content)
    {
        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("Output path has no directory.");
        Directory.CreateDirectory(directory);
        string temporary = fullPath + ".download";
        File.WriteAllText(temporary, content.Replace("\r\n", "\n"), new UTF8Encoding(false));
        if (File.Exists(fullPath))
            File.Delete(fullPath);
        File.Move(temporary, fullPath);
    }

    private static string ResolveWithin(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            return null;
        string normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        string candidate;
        try
        {
            candidate = Path.GetFullPath(Path.Combine(root, relativePath));
        }
        catch (Exception exception) when (exception is ArgumentException ||
                                          exception is NotSupportedException ||
                                          exception is PathTooLongException)
        {
            return null;
        }
        return candidate.StartsWith(normalizedRoot, PathComparison()) ? candidate : null;
    }

    private static string Relative(string root, string path)
    {
        Uri rootUri = new Uri(Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar);
        return Uri.UnescapeDataString(rootUri.MakeRelativeUri(new Uri(Path.GetFullPath(path))).ToString());
    }

    private static string VariantKey(ImportedCardVariants variants)
    {
        variants ??= new ImportedCardVariants();
        var values = new List<string>();
        if (variants.Normal) values.Add("normal");
        if (variants.Reverse) values.Add("reverse");
        if (variants.Holo) values.Add("holo");
        if (variants.FirstEdition) values.Add("first-edition");
        if (variants.WPromo) values.Add("w-promo");
        return values.Count == 0 ? "unspecified" : string.Join("+", values);
    }

    private static string NormalizeLanguage(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().Replace('_', '-').ToLowerInvariant();

    private static string NormalizeSha256(string value)
    {
        string normalized = value?.Trim().ToLowerInvariant();
        return normalized != null && normalized.Length == 64 &&
               normalized.All(character => character >= '0' && character <= '9' ||
                                           character >= 'a' && character <= 'f')
            ? normalized
            : null;
    }

    private static string Key(params string[] values)
    {
        if (values.Any(string.IsNullOrWhiteSpace))
            return null;
        return string.Join("|", values.Select(value => value.Trim().ToLowerInvariant()));
    }

    private static string RequiredOrFallback(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static int Value(IDictionary<string, int> values, string key) =>
        values.TryGetValue(key, out int value) ? value : 0;

    private static StringComparison PathComparison() =>
        Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private sealed class NaturalStringComparer : IComparer<string>
    {
        public static readonly NaturalStringComparer Instance = new NaturalStringComparer();

        public int Compare(string left, string right)
        {
            if (int.TryParse(left, NumberStyles.Integer, CultureInfo.InvariantCulture, out int leftNumber) &&
                int.TryParse(right, NumberStyles.Integer, CultureInfo.InvariantCulture, out int rightNumber))
            {
                int numeric = leftNumber.CompareTo(rightNumber);
                if (numeric != 0)
                    return numeric;
            }
            return string.Compare(left, right, StringComparison.Ordinal);
        }
    }
}
