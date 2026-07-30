using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

public sealed class PrivateContentImporterWindow : EditorWindow
{
    private const string HistoricalSetIds = "base1,neo1,ex1,swsh1,sv01";
    private string _setIds = HistoricalSetIds;
    private string _language = "en";
    private string _imageQuality = "low";
    private string _imageExtension = "jpg";
    private int _maximumCardsPerSet;
    private int _maxConcurrency = 4;
    private bool _refreshExisting;
    private bool _isRunning;
    private string _status = "Ready";
    private float _progress;
    private CancellationTokenSource _cancellation;

    [MenuItem("Tools/Gacha/Private Content Importer")]
    public static void ShowWindow()
    {
        GetWindow<PrivateContentImporterWindow>("Private Content Importer");
    }

    [MenuItem("Tools/Gacha/Import Historical Sample Sets")]
    public static async void ImportHistoricalSamplesFromMenu()
    {
        try
        {
            ContentImportSummary summary = await RunHistoricalImportAsync();
            Debug.Log(FormatSummary(summary));
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    [MenuItem("Tools/Gacha/Build TCGdex Metadata Inventory")]
    public static async void BuildMetadataInventoryFromMenu()
    {
        try
        {
            ContentInventorySnapshot snapshot = await RunMetadataInventoryAsync();
            Debug.Log(FormatInventorySummary(snapshot));
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    public static void BuildMetadataInventoryFromCommandLine()
    {
        try
        {
            ContentInventorySnapshot snapshot = RunMetadataInventoryAsync().GetAwaiter().GetResult();
            Debug.Log(FormatInventorySummary(snapshot));
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            if (Application.isBatchMode)
                EditorApplication.Exit(1);
        }
    }

    [MenuItem("Tools/Gacha/Compile English Set Generation Overrides")]
    public static void CompileEnglishSetGenerationOverridesFromMenu()
    {
        try
        {
            PokemonSetGenerationCompileResult result = CompileEnglishSetGenerationOverrides();
            AssetDatabase.Refresh();
            Debug.Log($"Compiled {result.SourceSetCount} Set generation overrides from " +
                      $"{result.PolicyCount} policies.");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    public static void CompileEnglishSetGenerationOverridesFromCommandLine()
    {
        try
        {
            PokemonSetGenerationCompileResult result = CompileEnglishSetGenerationOverrides();
            Debug.Log($"Compiled {result.SourceSetCount} Set generation overrides from " +
                      $"{result.PolicyCount} policies.");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            if (Application.isBatchMode)
                EditorApplication.Exit(1);
        }
    }

    [MenuItem("Tools/Gacha/Import All English Sets (Resumable WebP)")]
    public static async void ImportAllEnglishSetsFromMenu()
    {
        try
        {
            ContentImportSummary summary = await RunAllEnglishImportAsync();
            Debug.Log(FormatSummary(summary));
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    public static void ImportAllEnglishSetsFromCommandLine()
    {
        try
        {
            ContentImportSummary summary = RunAllEnglishImportAsync().GetAwaiter().GetResult();
            Debug.Log(FormatSummary(summary));
            if (Application.isBatchMode && (summary.ErrorCount > 0 || summary.FailedSetCount > 0))
                EditorApplication.Exit(2);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            if (Application.isBatchMode)
                EditorApplication.Exit(1);
        }
    }

    // Entry point for repeatable batch imports from CI or PowerShell.
    public static void ImportHistoricalSamplesFromCommandLine()
    {
        try
        {
            ContentImportSummary summary = RunHistoricalImportAsync().GetAwaiter().GetResult();
            Debug.Log(FormatSummary(summary));
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
        EditorGUILayout.LabelField("TCGdex private importer", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Output is written to LocalContent and ignored by Git. API keys and copyrighted artwork must never be placed in Assets.",
            MessageType.Info);

        using (new EditorGUI.DisabledScope(_isRunning))
        {
            _setIds = EditorGUILayout.TextField("Set IDs", _setIds);
            _language = EditorGUILayout.TextField("Language", _language);
            _imageQuality = EditorGUILayout.Popup("Image quality", _imageQuality == "high" ? 1 : 0,
                new[] { "low", "high" }) == 1 ? "high" : "low";
            _imageExtension = EditorGUILayout.Popup("Image format", ExtensionIndex(_imageExtension),
                new[] { "jpg", "png", "webp" }) switch
            {
                1 => "png",
                2 => "webp",
                _ => "jpg"
            };
            _maximumCardsPerSet = EditorGUILayout.IntField("Card limit (0 = all)", _maximumCardsPerSet);
            _maxConcurrency = EditorGUILayout.IntSlider("Concurrent downloads", _maxConcurrency, 1, 8);
            _refreshExisting = EditorGUILayout.Toggle("Refresh existing files", _refreshExisting);

            if (GUILayout.Button("Import"))
                _ = StartImportAsync();
        }

        if (_isRunning && GUILayout.Button("Cancel"))
            _cancellation?.Cancel();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField(_status);
        EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(), _progress, $"{_progress:P0}");
    }

    private async Task StartImportAsync()
    {
        _isRunning = true;
        _cancellation = new CancellationTokenSource();
        try
        {
            var progress = new Progress<ContentImportProgress>(value =>
            {
                _status = $"{value.SetId}: {value.Stage} {value.Completed}/{value.Total}";
                _progress = value.Ratio;
                Repaint();
            });
            using var service = new TcgdexImportService();
            ContentImportSummary summary = await service.ImportSetsAsync(
                SplitSetIds(_setIds), CreateOptions(_language, _imageQuality, _imageExtension,
                    _maxConcurrency, _maximumCardsPerSet, _refreshExisting), progress,
                _cancellation.Token);
            _status = FormatSummary(summary);
            _progress = 1f;
        }
        catch (OperationCanceledException)
        {
            _status = "Import cancelled. Completed files remain reusable.";
        }
        catch (Exception exception)
        {
            _status = exception.Message;
            Debug.LogException(exception);
        }
        finally
        {
            _isRunning = false;
            _cancellation.Dispose();
            _cancellation = null;
            Repaint();
        }
    }

    private static async Task<ContentImportSummary> RunHistoricalImportAsync()
    {
        using var service = new TcgdexImportService();
        return await service.ImportSetsAsync(
                SplitSetIds(HistoricalSetIds), CreateOptions("en", "low", "jpg", 4, 0, false))
            .ConfigureAwait(false);
    }

    private static async Task<ContentInventorySnapshot> RunMetadataInventoryAsync()
    {
        using var service = new TcgdexInventoryService();
        return await service.BuildAsync(new ContentInventoryOptions
        {
            OutputRoot = Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "LocalContent", "Inventory")),
            ReferenceLanguage = "en",
            Languages = TcgdexInventoryService.SupportedLanguages.ToList(),
            DetailedLanguages = new System.Collections.Generic.List<string> { "en" },
            SetGenerationOverridesPath = Path.Combine(
                Application.dataPath,
                "Editor",
                "ContentImporter",
                "Overrides",
                "set-generation-overrides.json"),
            MaxConcurrency = 4,
            ImageSampleCount = 12
        }).ConfigureAwait(false);
    }

    private static PokemonSetGenerationCompileResult CompileEnglishSetGenerationOverrides()
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        return PokemonSetGenerationCompiler.CompileFiles(
            Path.Combine(projectRoot, "LocalContent", "Inventory", "tcgdex-inventory.json"),
            Path.Combine(Application.dataPath, "Editor", "ContentImporter", "Overrides",
                "set-generation-policies.json"),
            Path.Combine(Application.dataPath, "Editor", "ContentImporter", "Overrides",
                "set-generation-overrides.json"));
    }

    private static async Task<ContentImportSummary> RunAllEnglishImportAsync()
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        ContentInventorySnapshot inventory = Newtonsoft.Json.JsonConvert.DeserializeObject<ContentInventorySnapshot>(
            File.ReadAllText(Path.Combine(
                projectRoot, "LocalContent", "Inventory", "tcgdex-inventory.json")));
        if (inventory == null || inventory.Sets == null)
            throw new InvalidDataException("TCGdex inventory is missing or invalid.");
        string[] setIds = inventory.Sets
            .Where(item => item.Language == "en")
            .OrderBy(item => item.ReleaseDate, StringComparer.Ordinal)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .Select(item => item.Id)
            .ToArray();
        if (setIds.Length != 218)
            throw new InvalidDataException(
                $"Expected the audited 218 English Sets, found {setIds.Length}.");

        using var service = new TcgdexImportService();
        return await service.ImportSetsAsync(setIds, new ContentImportOptions
        {
            Language = "en",
            OutputRoot = Path.Combine(projectRoot, "LocalContent", "Imports"),
            SetGenerationOverridesPath = Path.Combine(
                Application.dataPath, "Editor", "ContentImporter", "Overrides",
                "set-generation-overrides.json"),
            ImageQuality = "low",
            ImageExtension = "webp",
            MaxConcurrency = 4,
            MaximumCardsPerSet = 0,
            RefreshExistingFiles = false,
            RequestIntervalMilliseconds = 25,
            MaximumAttempts = 5,
            RetryBaseDelayMilliseconds = 750
        }).ConfigureAwait(false);
    }

    private static ContentImportOptions CreateOptions(
        string language, string quality, string extension, int concurrency,
        int maximumCards, bool refresh)
    {
        return new ContentImportOptions
        {
            Language = language.Trim().ToLowerInvariant(),
            OutputRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "LocalContent", "Imports")),
            SetGenerationOverridesPath = Path.Combine(
                Application.dataPath,
                "Editor",
                "ContentImporter",
                "Overrides",
                "set-generation-overrides.json"),
            ImageQuality = quality,
            ImageExtension = extension,
            MaxConcurrency = concurrency,
            MaximumCardsPerSet = Mathf.Max(0, maximumCards),
            RefreshExistingFiles = refresh
        };
    }

    private static string[] SplitSetIds(string value)
    {
        return value.Split(new[] { ',', ';', '\n', '\r', ' ' },
            StringSplitOptions.RemoveEmptyEntries);
    }

    private static int ExtensionIndex(string extension)
    {
        return extension == "png" ? 1 : extension == "webp" ? 2 : 0;
    }

    private static string FormatSummary(ContentImportSummary summary)
    {
        double megabytes = summary.ImageBytes / 1024d / 1024d;
        return $"Imported {summary.SetCount} sets, {summary.CardCount} cards, " +
               $"{megabytes:F1} MB images, {summary.ReusedMetadataCount} metadata and " +
               $"{summary.ReusedImageCount} images reused, {summary.ErrorCount} errors, " +
               $"{summary.FailedSetCount} failed Sets.";
    }

    private static string FormatInventorySummary(ContentInventorySnapshot snapshot)
    {
        int sets = snapshot.Languages.Sum(item => item.SetCount);
        int cards = snapshot.Languages.Sum(item => item.TotalCardCount);
        return $"Inventory complete: {snapshot.Languages.Count} languages, {sets} sets, " +
               $"{cards} cards listed, {snapshot.Errors.Count} errors. " +
               $"Hash {snapshot.ContentSha256}.";
    }
}
