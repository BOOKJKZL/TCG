using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

public sealed class PrivatePokedexImporterWindow : EditorWindow
{
    private bool isRunning;
    private bool refreshExisting;
    private string status = "Ready";
    private float progress;
    private CancellationTokenSource cancellation;

    [MenuItem("Tools/Gacha/Private Pokedex Importer")]
    public static void ShowWindow()
    {
        GetWindow<PrivatePokedexImporterWindow>("Private Pokedex Importer");
    }

    [MenuItem("Tools/Gacha/Import and Audit Full Pokedex")]
    public static async void ImportAndAuditFromMenu()
    {
        try
        {
            PokeApiTaxonomyImportSummary summary = await RunImportAsync(false);
            PokeApiTaxonomyIntegrityReport report = RunAudit();
            Debug.Log(Format(summary, report));
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    public static void ImportAndAuditFromCommandLine()
    {
        try
        {
            PokeApiTaxonomyImportSummary summary = RunImportAsync(false).GetAwaiter().GetResult();
            PokeApiTaxonomyIntegrityReport report = RunAudit();
            Debug.Log(Format(summary, report));
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

    public static void AuditFromCommandLine()
    {
        try
        {
            PokeApiTaxonomyIntegrityReport report = RunAudit();
            Debug.Log($"Pokedex audit valid={report.IsValid}: {report.GenerationCount} generations, " +
                      $"{report.SpeciesCount} species, {report.FormCount} forms, " +
                      $"{report.Failures.Count} failures.");
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

    private void OnGUI()
    {
        EditorGUILayout.LabelField("PokeAPI private Pokedex importer", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Raw source files and the deterministic snapshot are written under LocalContent/Pokedex. " +
            "They are excluded from Git and are never bundled directly into the APK.",
            MessageType.Info);
        using (new EditorGUI.DisabledScope(isRunning))
        {
            refreshExisting = EditorGUILayout.Toggle("Refresh cached resources", refreshExisting);
            if (GUILayout.Button("Import and audit full Pokedex"))
                _ = StartAsync();
        }
        if (isRunning && GUILayout.Button("Cancel"))
            cancellation?.Cancel();
        EditorGUILayout.Space();
        EditorGUILayout.LabelField(status);
        EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(), progress, $"{progress:P0}");
    }

    private async Task StartAsync()
    {
        isRunning = true;
        cancellation = new CancellationTokenSource();
        try
        {
            var reporter = new Progress<PokeApiTaxonomyImportProgress>(value =>
            {
                status = $"{value.Stage}: {value.Completed}/{value.Total} ({value.ItemId})";
                progress = value.Ratio;
                Repaint();
            });
            PokeApiTaxonomyImportSummary summary = await RunImportAsync(
                refreshExisting, reporter, cancellation.Token);
            PokeApiTaxonomyIntegrityReport report = RunAudit();
            status = Format(summary, report);
            progress = report.IsValid ? 1f : 0f;
        }
        catch (OperationCanceledException)
        {
            status = "Import cancelled. Completed raw resources remain reusable.";
        }
        catch (Exception exception)
        {
            status = exception.Message;
            Debug.LogException(exception);
        }
        finally
        {
            cancellation.Dispose();
            cancellation = null;
            isRunning = false;
            Repaint();
        }
    }

    private static async Task<PokeApiTaxonomyImportSummary> RunImportAsync(
        bool refresh,
        IProgress<PokeApiTaxonomyImportProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
        using var service = new PokeApiTaxonomyImportService();
        return await service.ImportAsync(new PokeApiTaxonomyImportOptions
        {
            OutputRoot = OutputRoot,
            FormClassificationPath = ClassificationPath,
            RefreshExistingFiles = refresh,
            MaxConcurrency = 4,
            RequestIntervalMilliseconds = 25,
            MaximumAttempts = 5,
            RetryBaseDelayMilliseconds = 750
        }, progress, cancellationToken).ConfigureAwait(false);
    }

    private static PokeApiTaxonomyIntegrityReport RunAudit()
    {
        return PokeApiTaxonomyIntegrityAuditor.Audit(
            OutputRoot,
            ClassificationPath,
            Path.Combine(OutputRoot, "pokedex-import-audit.json"));
    }

    private static string Format(
        PokeApiTaxonomyImportSummary summary, PokeApiTaxonomyIntegrityReport report)
    {
        return $"Pokedex import audit valid={report.IsValid}: {summary.GenerationCount} generations, " +
               $"{summary.SpeciesCount} species, {summary.PokemonCount} Pokemon varieties, " +
               $"{summary.FormCount} forms, {summary.DownloadedFileCount} downloaded, " +
               $"{summary.ReusedFileCount} reused, {summary.WarningCount} language fallbacks, " +
               $"{summary.ManualReviewCount} manual reviews, {report.Failures.Count} failures.";
    }

    private static string ProjectRoot =>
        Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    private static string OutputRoot =>
        Path.Combine(ProjectRoot, "LocalContent", "Pokedex");
    private static string ClassificationPath =>
        Path.Combine(Application.dataPath, "Editor", "ContentImporter", "Overrides",
            "form-classification-overrides.json");
}
