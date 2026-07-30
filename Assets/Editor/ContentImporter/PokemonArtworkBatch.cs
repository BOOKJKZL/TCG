using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class PokemonArtworkBatch
{
    [MenuItem("Tools/Gacha/Import Audit and Package Pokemon Artwork")]
    public static void ImportAuditAndPublishFromMenu() => Run(false);

    public static void ImportAuditAndPublishFromCommandLine() => Run(true);

    private static void Run(bool commandLine)
    {
        try
        {
            using var service = new PokemonArtworkImportService();
            PokemonArtworkImportSummary summary = service.ImportAsync(
                new PokemonArtworkImportOptions
                {
                    TaxonomySnapshotPath = TaxonomyPath,
                    OutputRoot = ArtworkRoot,
                    MaxConcurrency = 8,
                    RequestIntervalMilliseconds = 10,
                    MaximumAttempts = 5,
                    RetryBaseDelayMilliseconds = 500
                },
                new LoggingProgress()).GetAwaiter().GetResult();
            PokemonArtworkIntegrityReport report = PokemonArtworkIntegrityAuditor.AuditAndPublish(
                TaxonomyPath, ArtworkRoot, ReleaseRoot, AuditPath);
            Debug.Log(
                $"Pokemon artwork valid={report.IsValid}: images={report.ImageCount}, " +
                $"missing-source={report.MissingSourceCount}, bytes={report.ImageBytes}, " +
                $"downloaded/reused={summary.DownloadedCount}/{summary.ReusedCount}, " +
                $"packages={report.PackageCount}, package-bytes={report.PackageDownloadBytes}, " +
                $"failures={report.Failures.Count}.");
            if (!report.IsValid && commandLine && Application.isBatchMode)
                EditorApplication.Exit(2);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            if (commandLine && Application.isBatchMode)
                EditorApplication.Exit(1);
        }
    }

    private sealed class LoggingProgress : IProgress<PokemonArtworkImportProgress>
    {
        private int lastBucket = -1;

        public void Report(PokemonArtworkImportProgress value)
        {
            int bucket = value.Completed / 100;
            if (bucket == lastBucket && value.Completed != value.Total)
                return;
            lastBucket = bucket;
            Debug.Log($"Pokemon artwork import {value.Completed}/{value.Total}: {value.FormId}");
        }
    }

    private static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    private static string TaxonomyPath => Path.Combine(
        ProjectRoot, "LocalContent", "Pokedex", "snapshot", "pokemon-taxonomy.json");
    private static string ArtworkRoot => Path.Combine(ProjectRoot, "LocalContent", "Pokedex", "artwork");
    private static string ReleaseRoot => Path.Combine(
        ProjectRoot, "LocalContent", "Pokedex", "releases", "android-artwork");
    private static string AuditPath => Path.Combine(ArtworkRoot, "artwork-import-audit.json");
}
