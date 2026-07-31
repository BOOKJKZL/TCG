using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class WebpQualityExperimentBatch
{
    [MenuItem("Tools/Gacha/Audit Simplified Chinese WebP Quality")]
    public static void RunFromMenu()
    {
        try
        {
            WebpQualityExperimentReport report = Run();
            Debug.Log(Format(report));
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    public static void RunFromCommandLine()
    {
        try
        {
            WebpQualityExperimentReport report = Run();
            Debug.Log(Format(report));
            if (Application.isBatchMode)
                EditorApplication.Exit(report.IsValid ? 0 : 2);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            if (Application.isBatchMode)
                EditorApplication.Exit(1);
        }
    }

    private static WebpQualityExperimentReport Run() => WebpQualityExperiment.Run(
        ImportRoot,
        "zh-cn",
        WebpQualityExperiment.DefaultSampleCount,
        WebpQualityExperiment.DefaultQualityLevels,
        JsonReportPath,
        MarkdownReportPath,
        ReviewRoot,
        WebpQualityExperiment.DefaultReviewSampleCount);

    private static string Format(WebpQualityExperimentReport report) =>
        $"WebP quality experiment valid={report.IsValid}: " +
        $"available={report.AvailableImageCount}/{report.AvailableImageBytes}, " +
        $"samples={report.SampleCount}, review={report.ReviewSampleCount}, " +
        $"qualities={string.Join(",", report.QualityLevels)}, failures={report.Failures.Count}, " +
        $"snapshot={report.SnapshotSha256}.";

    private static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    private static string ImportRoot => Path.Combine(ProjectRoot, "LocalContent", "Imports");
    private static string InventoryRoot => Path.Combine(ProjectRoot, "LocalContent", "Inventory");
    private static string JsonReportPath => Path.Combine(
        InventoryRoot, "zh-cn-webp-quality-experiment.json");
    private static string MarkdownReportPath => Path.Combine(
        InventoryRoot, "zh-cn-webp-quality-experiment.md");
    private static string ReviewRoot => Path.Combine(
        InventoryRoot, "zh-cn-webp-quality-review");
}
