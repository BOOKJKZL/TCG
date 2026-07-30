using System;
using System.IO;
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
            PokemonCardSubjectIntegrityReport report = Run();
            Debug.Log(Format(report));
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
            PokemonCardSubjectIntegrityReport report = Run();
            Debug.Log(Format(report));
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

    private static PokemonCardSubjectIntegrityReport Run()
    {
        PokemonCardSubjectLinkResult result = PokemonCardSubjectLinker.LinkFiles(
            ImportRoot, "en", TaxonomyPath, OverridePath, SnapshotPath);
        if (result.SetCount != 218 || result.CardCount != 23444)
            throw new InvalidDataException(
                $"Expected audited source 218 Sets/23,444 cards, found {result.SetCount}/{result.CardCount}.");
        PokemonCardSubjectIntegrityReport report = PokemonCardSubjectIntegrityAuditor.Audit(
            ImportRoot, "en", TaxonomyPath, OverridePath, SnapshotPath,
            218, 23444, AuditPath);
        if (!report.IsValid)
            return report;

        ContentPackagePublishResult publication = PokemonCardSubjectPackagePublisher.Publish(
            SnapshotPath, PackageReleaseRoot);
        PublishedContentPackage package = publication.Packages[0];
        report.PackageId = package.Package.PackageId;
        report.PackageSha256 = package.Package.Sha256;
        report.PackageDownloadBytes = package.Package.DownloadBytes;
        report.PackageInstalledBytes = package.Package.InstalledBytes;
        report.PackageCatalogPath = publication.CatalogPath;
        PokemonCardSubjectIntegrityAuditor.WriteReport(AuditPath, report);
        return report;
    }

    private static string Format(PokemonCardSubjectIntegrityReport report) =>
        $"Card subject audit valid={report.IsValid}: {report.SetCount} Sets, " +
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
    private static string SnapshotPath => Path.Combine(
        ProjectRoot, "LocalContent", "Pokedex", "links", "pokemon-card-subject-links.en.json");
    private static string AuditPath => Path.Combine(
        ProjectRoot, "LocalContent", "Pokedex", "links", "pokemon-card-subject-links.en.audit.json");
    private static string PackageReleaseRoot => Path.Combine(
        ProjectRoot, "LocalContent", "Pokedex", "releases", "android-links");
}
