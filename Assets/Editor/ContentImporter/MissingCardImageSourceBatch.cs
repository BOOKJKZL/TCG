using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class MissingCardImageSourceBatch
{
    private static readonly string[] Languages = { "en", "ja", "zh-cn" };
    private static readonly MissingCardImageExpectation[] Expectations =
    {
        new MissingCardImageExpectation("en", 1616),
        new MissingCardImageExpectation("ja", 4862),
        new MissingCardImageExpectation("zh-cn", 10)
    };

    [MenuItem("Tools/Gacha/Audit Missing Card Image Sources")]
    public static void AuditFromMenu()
    {
        try
        {
            MissingCardImageSourceAuditReport report = Run();
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
            MissingCardImageSourceAuditReport report = Run();
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

    private static MissingCardImageSourceAuditReport Run()
    {
        using var client = new HttpMissingImageSourceClient();
        return MissingCardImageSourceAuditor.Audit(
            ImportRoot,
            Languages,
            Expectations,
            client,
            jsonOutputPath: JsonReportPath,
            markdownOutputPath: MarkdownReportPath,
            queueOutputPath: DownloadQueuePath);
    }

    private static string Format(MissingCardImageSourceAuditReport report) =>
        $"Missing card image source audit valid={report.IsValid}: " +
        $"missing={report.MissingImageCount}, available={report.AvailableAtSourceCount}, " +
        $"unavailable={report.SourceUnavailableCount}, not-found={report.SourceNotFoundCount}, " +
        $"not-declared={report.SourceNotDeclaredCount}, source-card-missing={report.SourceCardMissingCount}, " +
        $"invalid={report.InvalidSourceCount}, probe-failed={report.ProbeFailedCount}, " +
        $"requests={report.RemoteRequestCount}, failures={report.Failures.Count}, " +
        $"snapshot={report.SnapshotSha256}.";

    private static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    private static string ImportRoot => Path.Combine(ProjectRoot, "LocalContent", "Imports");
    private static string InventoryRoot => Path.Combine(ProjectRoot, "LocalContent", "Inventory");
    private static string JsonReportPath => Path.Combine(
        InventoryRoot, "missing-card-image-sources.json");
    private static string MarkdownReportPath => Path.Combine(
        InventoryRoot, "missing-card-image-sources.md");
    private static string DownloadQueuePath => Path.Combine(
        InventoryRoot, "missing-card-image-download-queue.json");
}
