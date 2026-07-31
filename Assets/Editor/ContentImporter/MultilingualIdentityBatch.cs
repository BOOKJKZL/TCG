using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class MultilingualIdentityBatch
{
    private static readonly string[] Languages = { "en", "ja", "zh-cn" };
    private static readonly MultilingualCoverageExpectation[] Expectations =
    {
        new MultilingualCoverageExpectation("en", 218, 23444),
        new MultilingualCoverageExpectation("ja", 177, 8159),
        new MultilingualCoverageExpectation("zh-cn", 129, 12473)
    };

    [MenuItem("Tools/Gacha/Compile Multilingual Card Identities")]
    public static void CompileFromMenu()
    {
        try
        {
            MultilingualIdentityCompilationReport report = Run();
            Debug.Log(Format(report));
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    public static void CompileFromCommandLine()
    {
        try
        {
            MultilingualIdentityCompilationReport report = Run();
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

    private static MultilingualIdentityCompilationReport Run()
    {
        MultilingualProductionCoverageReport coverage =
            MultilingualProductionCoverageAuditor.Audit(ImportRoot, Languages, Expectations);
        return MultilingualIdentityCompiler.Compile(
            coverage,
            SetMappingPath,
            CardOverridePath,
            JsonReportPath,
            MarkdownReportPath,
            ReviewQueuePath);
    }

    private static string Format(MultilingualIdentityCompilationReport report) =>
        $"Multilingual identity compilation valid={report.IsValid}: " +
        $"cards={report.TotalCardCount}, groups={report.CandidateGroupCount}, " +
        $"auto={report.AutoAcceptedGroupCount}/{report.AutoAcceptedCardCount}, " +
        $"reviewed={report.ReviewedAcceptedGroupCount}/{report.ReviewedAcceptedCardCount}, " +
        $"pending={report.PendingReviewGroupCount}/{report.PendingReviewCardCount}, " +
        $"rejected={report.ReviewedRejectedGroupCount}/{report.ReviewedRejectedCardCount}, " +
        $"unmatched={report.UnmatchedCardCount}, failures={report.Failures.Count}, " +
        $"snapshot={report.SnapshotSha256}.";

    private static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    private static string ImportRoot => Path.Combine(ProjectRoot, "LocalContent", "Imports");
    private static string InventoryRoot => Path.Combine(ProjectRoot, "LocalContent", "Inventory");
    private static string OverrideRoot => Path.Combine(
        Application.dataPath, "Editor", "ContentImporter", "Overrides");
    private static string SetMappingPath => Path.Combine(
        OverrideRoot, "cross-region-set-mappings.json");
    private static string CardOverridePath => Path.Combine(
        OverrideRoot, "multilingual-card-identity-overrides.json");
    private static string JsonReportPath => Path.Combine(
        InventoryRoot, "multilingual-card-identities.json");
    private static string MarkdownReportPath => Path.Combine(
        InventoryRoot, "multilingual-card-identities.md");
    private static string ReviewQueuePath => Path.Combine(
        InventoryRoot, "multilingual-card-review-queue.json");
}
