using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Gacha.Infrastructure.Content;
using Newtonsoft.Json;

[Serializable]
public sealed class PrintingLanguageGroupRuntimeCompilationResult
{
    public bool IsValid;
    public string SourceCoverageSnapshotSha256;
    public string SourceIdentitySnapshotSha256;
    public int GroupCount;
    public int MemberCount;
    public long OutputBytes;
    public string OutputSha256;
    public int RuntimeDefinitionCount;
    public int RuntimeSourceCardCount;
    public int RuntimeItemCount;
    public int RuntimePrintingCount;
    public List<string> Failures = new List<string>();
}

public static class PrintingLanguageGroupRuntimeCompiler
{
    public static PrintingLanguageGroupRuntimeCompilationResult Compile(
        string identityReportPath,
        string outputPath)
    {
        if (string.IsNullOrWhiteSpace(identityReportPath))
            throw new ArgumentException("Identity report path is required.", nameof(identityReportPath));
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Runtime output path is required.", nameof(outputPath));

        var result = new PrintingLanguageGroupRuntimeCompilationResult();
        MultilingualIdentityCompilationReport source;
        try
        {
            source = JsonConvert.DeserializeObject<MultilingualIdentityCompilationReport>(
                File.ReadAllText(identityReportPath, Encoding.UTF8));
        }
        catch (Exception exception) when (exception is IOException || exception is JsonException)
        {
            result.Failures.Add("Identity report could not be read: " + exception.Message);
            return result;
        }

        if (source == null)
        {
            result.Failures.Add("Identity report is empty.");
            return result;
        }
        result.SourceCoverageSnapshotSha256 = source.SourceCoverageSnapshotSha256;
        result.SourceIdentitySnapshotSha256 = source.SnapshotSha256;
        ValidateSource(source, result.Failures);
        if (result.Failures.Count > 0)
            return result;

        Dictionary<string, MultilingualIdentityCardResult> cards = source.Cards
            .ToDictionary(value => value.RecordId, StringComparer.Ordinal);
        var manifest = new PrintingLanguageGroupManifestDto
        {
            SchemaVersion = PrintingLanguageGroupManifestReader.SupportedSchemaVersion,
            SourceCoverageSnapshotSha256 = source.SourceCoverageSnapshotSha256,
            SourceIdentitySnapshotSha256 = source.SnapshotSha256,
            Groups = source.Groups
                .Where(value => IsAccepted(value.Classification))
                .OrderBy(value => value.Id, StringComparer.Ordinal)
                .Select(value => RuntimeGroup(value, cards))
                .ToList()
        };

        try
        {
            string json = JsonConvert.SerializeObject(manifest, Formatting.Indented);
            WriteAtomic(outputPath, json);
            // Re-read through the player-facing strict reader so the compiler can
            // never publish a file that runtime would reject.
            PrintingLanguageGroupManifestDto verified =
                new PrintingLanguageGroupManifestReader().LoadFile(outputPath);
            result.GroupCount = verified.Groups.Count;
            result.MemberCount = verified.Groups.Sum(value => value.Members.Count);
            byte[] bytes = File.ReadAllBytes(outputPath);
            result.OutputBytes = bytes.LongLength;
            result.OutputSha256 = Sha256(bytes);
            result.IsValid = true;
        }
        catch (Exception exception)
        {
            result.Failures.Add("Runtime overlay could not be written or verified: " + exception.Message);
        }
        return result;
    }

    private static void ValidateSource(
        MultilingualIdentityCompilationReport source,
        ICollection<string> failures)
    {
        if (source.SchemaVersion != MultilingualIdentityCompiler.SupportedSchemaVersion)
            failures.Add("Identity report schema is unsupported.");
        if (!source.IsValid || (source.Failures?.Count ?? 0) != 0)
            failures.Add("Identity report is not valid.");
        if (!IsSha256(source.SourceCoverageSnapshotSha256))
            failures.Add("Coverage snapshot SHA-256 is missing or invalid.");
        if (!IsSha256(source.SnapshotSha256))
            failures.Add("Identity snapshot SHA-256 is missing or invalid.");

        source.Cards ??= new List<MultilingualIdentityCardResult>();
        source.Groups ??= new List<MultilingualIdentityGroupResult>();
        string[] duplicateCards = source.Cards
            .Where(value => value != null && !string.IsNullOrWhiteSpace(value.RecordId))
            .GroupBy(value => value.RecordId, StringComparer.Ordinal)
            .Where(value => value.Count() > 1)
            .Select(value => value.Key)
            .ToArray();
        if (source.Cards.Any(value => value == null || string.IsNullOrWhiteSpace(value.RecordId)))
            failures.Add("Identity report contains a card without a record id.");
        if (duplicateCards.Length > 0)
            failures.Add("Identity report repeats card record ids: " + string.Join(",", duplicateCards));
        if (source.TotalCardCount != source.Cards.Count)
            failures.Add($"Identity report card count drifted: {source.TotalCardCount}/{source.Cards.Count}.");

        var knownCards = new HashSet<string>(
            source.Cards.Where(value => value != null).Select(value => value.RecordId),
            StringComparer.Ordinal);
        var acceptedClaims = new HashSet<string>(StringComparer.Ordinal);
        foreach (MultilingualIdentityGroupResult group in source.Groups
                     .Where(value => value != null && IsAccepted(value.Classification)))
        {
            if (string.IsNullOrWhiteSpace(group.Id))
                failures.Add("Accepted identity group has no id.");
            group.RecordIds ??= new List<string>();
            if (group.RecordIds.Count < 2)
                failures.Add($"Accepted identity group '{group.Id}' has fewer than two members.");
            foreach (string recordId in group.RecordIds)
            {
                if (!knownCards.Contains(recordId))
                    failures.Add($"Accepted identity group '{group.Id}' references missing '{recordId}'.");
                if (!acceptedClaims.Add(recordId))
                    failures.Add($"Accepted identity card '{recordId}' is claimed more than once.");
            }
        }
    }

    private static PrintingLanguageGroupRecordDto RuntimeGroup(
        MultilingualIdentityGroupResult source,
        IReadOnlyDictionary<string, MultilingualIdentityCardResult> cards)
    {
        bool reviewed = string.Equals(
            source.Classification, "reviewed-accepted", StringComparison.Ordinal);
        return new PrintingLanguageGroupRecordDto
        {
            Id = source.Id,
            MatchMethod = reviewed ? "manual-override" : "source-identity",
            ReviewStatus = reviewed ? "reviewed" : "auto-accepted",
            Confidence = source.Confidence,
            Evidence = (source.Signals ?? new List<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToList(),
            Members = source.RecordIds
                .Select(recordId => cards[recordId])
                .OrderBy(value => value.Language, StringComparer.OrdinalIgnoreCase)
                .ThenBy(value => value.SetId, StringComparer.Ordinal)
                .ThenBy(value => value.CardId, StringComparer.Ordinal)
                .ThenBy(value => value.LocalId, StringComparer.Ordinal)
                .Select(value => new PrintingLanguageGroupMemberDto
                {
                    Language = value.Language,
                    SetId = value.SetId,
                    CardId = value.CardId,
                    LocalId = value.LocalId
                })
                .ToList()
        };
    }

    private static bool IsAccepted(string classification) =>
        string.Equals(classification, "auto-accepted", StringComparison.Ordinal) ||
        string.Equals(classification, "reviewed-accepted", StringComparison.Ordinal);

    private static bool IsSha256(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length == 64 &&
        value.Trim().All(Uri.IsHexDigit);

    private static string Sha256(byte[] bytes)
    {
        using SHA256 sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(bytes).Select(value => value.ToString("x2")));
    }

    private static void WriteAtomic(string path, string text)
    {
        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("Runtime overlay output has no parent directory.");
        Directory.CreateDirectory(directory);
        string temporary = fullPath + ".tmp." + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, text, new UTF8Encoding(false));
            if (File.Exists(fullPath))
                File.Replace(temporary, fullPath, null);
            else
                File.Move(temporary, fullPath);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}
