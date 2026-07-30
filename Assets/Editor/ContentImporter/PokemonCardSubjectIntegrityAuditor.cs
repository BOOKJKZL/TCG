using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Gacha.Pokemon.Infrastructure;
using Newtonsoft.Json;

[Serializable]
public sealed class PokemonCardSubjectReviewRecord
{
    public string CardId;
    public string CardName;
    public string SetId;
    public List<string> SpeciesIds = new List<string>();
    public List<string> FormIds = new List<string>();
    public string Method;
    public double Confidence;
    public string Reason;
}

[Serializable]
public sealed class PokemonCardSubjectIntegrityReport
{
    public int SchemaVersion = 1;
    public string GeneratedAtUtc;
    public bool IsValid;
    public string SnapshotSha256;
    public string CardContentSha256;
    public string TaxonomySourceSha256;
    public int SetCount;
    public int CardCount;
    public int PrintingCount;
    public int MatchedFormCount;
    public int MatchedSpeciesCount;
    public int MultiSpeciesCount;
    public int NotApplicableCount;
    public int NeedsReviewCount;
    public int TemporaryFileCount;
    public List<PokemonCardSubjectReviewRecord> ReviewQueue = new List<PokemonCardSubjectReviewRecord>();
    public List<string> Failures = new List<string>();
}

public static class PokemonCardSubjectIntegrityAuditor
{
    public static PokemonCardSubjectIntegrityReport Audit(
        string importRoot,
        string language,
        string taxonomySnapshotPath,
        string overridePath,
        string snapshotPath,
        int expectedSetCount,
        int expectedCardCount,
        string reportPath)
    {
        var report = new PokemonCardSubjectIntegrityReport
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
        };
        string verificationPath = snapshotPath + ".verification";
        try
        {
            PokemonTaxonomySnapshotLoadResult taxonomy =
                new PokemonTaxonomySnapshotReader().LoadFile(taxonomySnapshotPath);
            PokemonCardSubjectSnapshotLoadResult loaded =
                new PokemonCardSubjectSnapshotReader().LoadFile(snapshotPath, taxonomy.Catalog);
            PokemonCardSubjectLinkResult rebuilt = PokemonCardSubjectLinker.LinkFiles(
                importRoot, language, taxonomySnapshotPath, overridePath, verificationPath);
            byte[] actual = File.ReadAllBytes(snapshotPath);
            byte[] verification = File.ReadAllBytes(verificationPath);
            if (!actual.SequenceEqual(verification))
                report.Failures.Add("Card subject snapshot is not byte-for-byte deterministic.");

            report.SnapshotSha256 = Sha256(actual);
            report.CardContentSha256 = loaded.CardContentSha256;
            report.TaxonomySourceSha256 = loaded.TaxonomySourceSha256;
            report.SetCount = rebuilt.SetCount;
            report.CardCount = rebuilt.CardCount;
            report.PrintingCount = rebuilt.PrintingCount;
            report.MatchedFormCount = rebuilt.MatchedFormCount;
            report.MatchedSpeciesCount = rebuilt.MatchedSpeciesCount;
            report.MultiSpeciesCount = rebuilt.MultiSpeciesCount;
            report.NotApplicableCount = rebuilt.NotApplicableCount;
            report.NeedsReviewCount = rebuilt.NeedsReviewCount;
            if (report.SetCount != expectedSetCount)
                report.Failures.Add($"Expected {expectedSetCount} Sets, found {report.SetCount}.");
            if (report.CardCount != expectedCardCount)
                report.Failures.Add($"Expected {expectedCardCount} cards, found {report.CardCount}.");
            int statusTotal = report.MatchedFormCount + report.MatchedSpeciesCount +
                              report.MultiSpeciesCount + report.NotApplicableCount + report.NeedsReviewCount;
            if (statusTotal != report.CardCount)
                report.Failures.Add("Card match status totals do not cover every source card exactly once.");
            if (loaded.Catalog.Cards.Count != report.CardCount ||
                loaded.Catalog.Printings.Count != report.PrintingCount)
                report.Failures.Add("Runtime link indexes do not match snapshot card/printing counts.");
            if (!string.Equals(loaded.TaxonomySourceSha256, taxonomy.SourceSha256, StringComparison.Ordinal))
                report.Failures.Add("Card links target a different taxonomy source hash.");

            PokemonCardSubjectSnapshotDto dto = JsonConvert.DeserializeObject<PokemonCardSubjectSnapshotDto>(
                File.ReadAllText(snapshotPath));
            report.ReviewQueue = (dto.Links ?? new List<PokemonCardSubjectLinkDto>())
                .Where(value => value.Status == "needs-review")
                .OrderBy(value => value.Reason, StringComparer.Ordinal)
                .ThenBy(value => value.SetId, StringComparer.Ordinal)
                .ThenBy(value => value.LocalId, StringComparer.Ordinal)
                .Select(value => new PokemonCardSubjectReviewRecord
                {
                    CardId = value.CardId,
                    CardName = value.CardName,
                    SetId = value.SetId,
                    SpeciesIds = value.SpeciesIds,
                    FormIds = value.FormIds,
                    Method = value.Method,
                    Confidence = value.Confidence,
                    Reason = value.Reason
                }).ToList();
            if (report.ReviewQueue.Count != report.NeedsReviewCount)
                report.Failures.Add("Review queue count does not match needs-review status count.");
            string outputRoot = Path.GetDirectoryName(snapshotPath);
            report.TemporaryFileCount = Directory.Exists(outputRoot)
                ? Directory.GetFiles(outputRoot, "*.download", SearchOption.AllDirectories).Length
                : 0;
            if (report.TemporaryFileCount > 0)
                report.Failures.Add($"Found {report.TemporaryFileCount} temporary link files.");
        }
        catch (Exception exception)
        {
            report.Failures.Add(exception.Message);
        }
        finally
        {
            if (File.Exists(verificationPath))
                File.Delete(verificationPath);
        }
        report.Failures = report.Failures.Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal).ToList();
        report.IsValid = report.Failures.Count == 0;
        WriteReport(reportPath, report);
        return report;
    }

    private static string Sha256(byte[] bytes)
    {
        using SHA256 sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(bytes).Select(value => value.ToString("x2")));
    }

    private static void WriteReport(string path, PokemonCardSubjectIntegrityReport report)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
        string temporary = path + ".download";
        File.WriteAllText(temporary, JsonConvert.SerializeObject(report, Formatting.Indented));
        if (File.Exists(path))
            File.Delete(path);
        File.Move(temporary, path);
    }
}
