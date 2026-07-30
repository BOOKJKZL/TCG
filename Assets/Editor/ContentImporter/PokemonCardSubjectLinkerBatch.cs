using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gacha.EditorTools.Content;
using UnityEditor;
using UnityEngine;

public static class PokemonCardSubjectLinkerBatch
{
    [MenuItem("Tools/Gacha/Build and Audit Card Subject Links")]
    public static void LinkAndAuditFromMenu()
    {
        try
        {
            PokemonCardSubjectIntegrityReport report = Run("en", 218, 23444, true);
            Debug.Log(Format("en", report));
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    public static void LinkAndAuditFromCommandLine()
    {
        try
        {
            PokemonCardSubjectIntegrityReport report = Run("en", 218, 23444, true);
            Debug.Log(Format("en", report));
            if (!report.IsValid && Application.isBatchMode)
                EditorApplication.Exit(2);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            if (Application.isBatchMode)
                EditorApplication.Exit(1);
        }
    }

    [MenuItem("Tools/Gacha/Build and Audit All Card Language Links")]
    public static void LinkAndAuditAllCardLanguagesFromMenu()
    {
        try
        {
            foreach ((string language, int sets, int cards) in Jobs)
                Debug.Log(Format(language, Run(language, sets, cards, false)));
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    public static void LinkAndAuditAllCardLanguagesFromCommandLine()
    {
        try
        {
            PokemonCardSubjectIntegrityReport[] reports = Jobs
                .Select(job =>
                {
                    PokemonCardSubjectIntegrityReport report =
                        Run(job.language, job.sets, job.cards, false);
                    Debug.Log(Format(job.language, report));
                    return report;
                }).ToArray();
            if (reports.Any(value => !value.IsValid) && Application.isBatchMode)
                EditorApplication.Exit(2);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            if (Application.isBatchMode)
                EditorApplication.Exit(1);
        }
    }

    public static void LinkAndAuditSimplifiedChineseFromCommandLine()
    {
        try
        {
            PokemonCardSubjectIntegrityReport report = Run("zh-cn", 129, 12473, false);
            Debug.Log(Format("zh-cn", report));
            if (!report.IsValid && Application.isBatchMode)
                EditorApplication.Exit(2);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            if (Application.isBatchMode)
                EditorApplication.Exit(1);
        }
    }

    private static PokemonCardSubjectIntegrityReport Run(
        string language,
        int expectedSetCount,
        int expectedCardCount,
        bool publishLegacyEnglishPackage)
    {
        PokemonCardSubjectLinkResult result = PokemonCardSubjectLinker.LinkFiles(
            ImportRoot, language, TaxonomyPath, OverridePath, SnapshotPath(language));
        if (result.SetCount != expectedSetCount || result.CardCount != expectedCardCount)
            throw new InvalidDataException(
                $"Expected audited '{language}' source {expectedSetCount} Sets/{expectedCardCount} cards, " +
                $"found {result.SetCount}/{result.CardCount}.");
        PokemonCardSubjectIntegrityReport report = PokemonCardSubjectIntegrityAuditor.Audit(
            ImportRoot, language, TaxonomyPath, OverridePath, SnapshotPath(language),
            expectedSetCount, expectedCardCount, AuditPath(language));
        if (!report.IsValid || !publishLegacyEnglishPackage)
            return report;

        ContentPackagePublishResult publication = PokemonCardSubjectPackagePublisher.Publish(
            SnapshotPath(language), PackageReleaseRoot);
        PublishedContentPackage package = publication.Packages[0];
        report.PackageId = package.Package.PackageId;
        report.PackageSha256 = package.Package.Sha256;
        report.PackageDownloadBytes = package.Package.DownloadBytes;
        report.PackageInstalledBytes = package.Package.InstalledBytes;
        report.PackageCatalogPath = publication.CatalogPath;
        PokemonCardSubjectIntegrityAuditor.WriteReport(AuditPath(language), report);
        return report;
    }

    private static string Format(string language, PokemonCardSubjectIntegrityReport report) =>
        $"Card subject audit language={language} valid={report.IsValid}: {report.SetCount} Sets, " +
        $"{report.CardCount} cards, {report.PrintingCount} printings; " +
        $"matched form/species/multi={report.MatchedFormCount}/{report.MatchedSpeciesCount}/" +
        $"{report.MultiSpeciesCount}, not-applicable={report.NotApplicableCount}, " +
        $"needs-review={report.NeedsReviewCount}, package={report.PackageDownloadBytes} bytes, " +
        $"failures={report.Failures.Count}.";

    private static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    private static string ImportRoot => Path.Combine(ProjectRoot, "LocalContent", "Imports");
    private static string TaxonomyPath => Path.Combine(
        ProjectRoot, "LocalContent", "Pokedex", "snapshot", "pokemon-taxonomy.json");
    private static string OverridePath => Path.Combine(
        Application.dataPath, "Editor", "ContentImporter", "Overrides", "card-subject-overrides.json");
    private static string SnapshotPath(string language) => Path.Combine(
        ProjectRoot, "LocalContent", "Pokedex", "links", $"pokemon-card-subject-links.{language}.json");
    private static string AuditPath(string language) => Path.Combine(
        ProjectRoot, "LocalContent", "Pokedex", "links", $"pokemon-card-subject-links.{language}.audit.json");
    private static string PackageReleaseRoot => Path.Combine(
        ProjectRoot, "LocalContent", "Pokedex", "releases", "android-links");

    private static readonly IReadOnlyList<(string language, int sets, int cards)> Jobs =
        new[]
        {
            ("en", 218, 23444),
            ("ja", 177, 8159),
            ("zh-cn", 129, 12473)
        };
}
