using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class MultilingualProductionCoverageBatch
{
    private static readonly string[] Languages = { "en", "ja", "zh-cn" };

    private static readonly MultilingualCoverageExpectation[] Expectations =
    {
        new MultilingualCoverageExpectation("en", 218, 23444),
        new MultilingualCoverageExpectation("ja", 177, 8159),
        new MultilingualCoverageExpectation("zh-cn", 129, 12473)
    };

    [MenuItem("Tools/Gacha/Audit Multilingual Production Coverage")]
    public static void AuditFromMenu()
    {
        try
        {
            MultilingualProductionCoverageReport report = Run();
            Debug.Log(Format(report));
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    public static void AuditFromCommandLine()
    {
        try
        {
            MultilingualProductionCoverageReport report = Run();
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

    private static MultilingualProductionCoverageReport Run() =>
        MultilingualProductionCoverageAuditor.Audit(
            ImportRoot,
            Languages,
            Expectations,
            JsonReportPath,
            MarkdownReportPath);

    private static string Format(MultilingualProductionCoverageReport report) =>
        $"Multilingual production coverage valid={report.IsValid}: " +
        $"sets={report.TotalSetCount}, cards={report.TotalCardCount}, " +
        $"images={report.TotalImageCount}, missing={report.TotalMissingImageCount}, " +
        $"candidate-groups={report.DirectCandidateGroupCount}, " +
        $"candidate-cards={report.DirectCandidateCardCount}, " +
        $"unmatched={report.UnmatchedCardCount}, failures={report.Failures.Count}, " +
        $"snapshot={report.SnapshotSha256}.";

    private static string ProjectRoot =>
        Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    private static string ImportRoot =>
        Path.Combine(ProjectRoot, "LocalContent", "Imports");
    private static string InventoryRoot =>
        Path.Combine(ProjectRoot, "LocalContent", "Inventory");
    private static string JsonReportPath =>
        Path.Combine(InventoryRoot, "multilingual-production-coverage.json");
    private static string MarkdownReportPath =>
        Path.Combine(InventoryRoot, "multilingual-production-coverage.md");
}
