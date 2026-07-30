using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gacha.EditorTools.Content;
using Gacha.Infrastructure.Content;
using UnityEditor;
using UnityEngine;

public sealed class PrivateContentPublisherWindow : EditorWindow
{
    private sealed class Candidate
    {
        public bool Selected;
        public string Language;
        public string SetId;
        public string SetName;
        public string SourceDirectory;
    }

    private readonly List<Candidate> candidates = new List<Candidate>();
    private Vector2 scroll;
    private string sourceRoot;
    private string outputRoot;
    private long catalogRevision = 1;
    private long packageRevision = 1;
    private string version = "1.0.0";

    [MenuItem("Tools/Universal Gacha/Private Content Publisher")]
    private static void Open()
    {
        GetWindow<PrivateContentPublisherWindow>("Content Publisher");
    }

    private void OnEnable()
    {
        sourceRoot = ContentPackagePublisherBatch.DefaultImportRoot;
        outputRoot = ContentPackagePublisherBatch.DefaultReleaseRoot;
        RefreshCandidates();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Deterministic Content Packages", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Builds versioned ZIP files and a schema v1 catalog. Output stays under LocalContent and is never added to Git.",
            MessageType.Info);

        sourceRoot = EditorGUILayout.TextField("Import root", sourceRoot);
        outputRoot = EditorGUILayout.TextField("Release root", outputRoot);
        catalogRevision = EditorGUILayout.LongField("Catalog revision", catalogRevision);
        packageRevision = EditorGUILayout.LongField("Package revision", packageRevision);
        version = EditorGUILayout.TextField("Package version", version);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Refresh imports"))
                RefreshCandidates();
            if (GUILayout.Button("Select none"))
            {
                foreach (Candidate candidate in candidates)
                    candidate.Selected = false;
            }
        }

        EditorGUILayout.Space();
        scroll = EditorGUILayout.BeginScrollView(scroll);
        foreach (Candidate candidate in candidates)
        {
            candidate.Selected = EditorGUILayout.ToggleLeft(
                $"{candidate.Language}/{candidate.SetId} — {candidate.SetName}",
                candidate.Selected);
        }
        EditorGUILayout.EndScrollView();

        EditorGUI.BeginDisabledGroup(!candidates.Any(item => item.Selected));
        if (GUILayout.Button("Publish selected packages", GUILayout.Height(34f)))
            PublishSelected();
        EditorGUI.EndDisabledGroup();
    }

    private void RefreshCandidates()
    {
        candidates.Clear();
        if (string.IsNullOrWhiteSpace(sourceRoot) || !Directory.Exists(sourceRoot))
            return;

        var reader = new PrivateContentManifestReader();
        foreach (string manifestPath in Directory.GetFiles(sourceRoot, "manifest.json", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            try
            {
                PrivateContentManifestDocument document = reader.LoadFile(manifestPath);
                candidates.Add(new Candidate
                {
                    Language = document.Manifest.Language,
                    SetId = document.Manifest.Set.Id,
                    SetName = document.Manifest.Set.Name,
                    SourceDirectory = Path.GetDirectoryName(manifestPath)
                });
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Skipping invalid private manifest: " + exception.Message);
            }
        }
        Repaint();
    }

    private void PublishSelected()
    {
        try
        {
            EditorUtility.DisplayProgressBar("Content Publisher", "Building deterministic packages...", 0.5f);
            ContentPackagePublishResult result = ContentPackagePublisherBatch.Publish(
                outputRoot,
                catalogRevision,
                packageRevision,
                version,
                candidates.Where(item => item.Selected).Select(item =>
                    new ContentPackagePublisherBatch.ImportedSet(
                        item.Language,
                        item.SetId,
                        item.SourceDirectory)));
            Debug.Log(
                $"Published {result.Packages.Count} content packages to '{result.CatalogPath}'. " +
                $"Catalog bytes={new FileInfo(result.CatalogPath).Length}.");
            EditorUtility.RevealInFinder(result.CatalogPath);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog("Content publication failed", exception.Message, "OK");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }
}

public static class ContentPackagePublisherBatch
{
    public sealed class ImportedSet
    {
        public ImportedSet(string language, string setId, string sourceDirectory)
        {
            Language = language;
            SetId = setId;
            SourceDirectory = sourceDirectory;
        }

        public string Language { get; }
        public string SetId { get; }
        public string SourceDirectory { get; }
    }

    public static string ProjectRoot => Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
    public static string DefaultImportRoot => Path.Combine(ProjectRoot, "LocalContent", "Imports");
    public static string DefaultReleaseRoot => Path.Combine(ProjectRoot, "LocalContent", "Releases", "android");

    [MenuItem("Tools/Universal Gacha/Publish Base + Neo Fixtures")]
    public static void PublishHistoricalFixtures()
    {
        IReadOnlyList<ImportedSet> imports = DiscoverImports(DefaultImportRoot)
            .Where(item => string.Equals(item.SetId, "base1", StringComparison.Ordinal) ||
                           string.Equals(item.SetId, "neo1", StringComparison.Ordinal))
            .ToArray();
        if (imports.Count != 2)
            throw new InvalidOperationException("The Base Set and Neo Genesis private imports are required for this fixture.");

        ContentPackagePublishResult result = Publish(
            DefaultReleaseRoot,
            1,
            1,
            "1.0.0",
            imports);
        Debug.Log(
            $"Historical content fixture published: packages={result.Packages.Count}, " +
            $"catalog='{result.CatalogPath}'.");
    }

    [MenuItem("Tools/Universal Gacha/Publish All English Sets")]
    public static void PublishAllEnglishSets()
    {
        IReadOnlyList<ImportedSet> imports = DiscoverImports(DefaultImportRoot)
            .Where(item => string.Equals(item.Language, "en", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (imports.Count == 0)
            throw new InvalidOperationException("No English private imports were found.");

        ContentPackagePublishResult result = Publish(
            DefaultReleaseRoot,
            2,
            2,
            "2.0.0",
            imports);
        Debug.Log(
            $"All English content published locally: packages={result.Packages.Count}, " +
            $"catalog='{result.CatalogPath}'.");
    }

    public static ContentPackagePublishResult Publish(
        string outputRoot,
        long catalogRevision,
        long packageRevision,
        string version,
        IEnumerable<ImportedSet> imports)
    {
        ContentPackagePublishDefinition[] definitions = (imports ?? throw new ArgumentNullException(nameof(imports)))
            .Select(item => new ContentPackagePublishDefinition(
                item.Language + "." + item.SetId,
                item.SourceDirectory,
                item.Language + "/" + item.SetId,
                packageRevision,
                version,
                RuntimePackagePaths(item)))
            .ToArray();
        ContentPackagePublishResult result = new DeterministicContentPackagePublisher().Publish(new ContentPackagePublishRequest(
            outputRoot,
            catalogRevision,
            definitions));
        VerifyRuntimeInstallation(result, definitions.Length, outputRoot);
        return result;
    }

    private static IReadOnlyList<string> RuntimePackagePaths(ImportedSet importedSet)
    {
        string manifestPath = Path.Combine(importedSet.SourceDirectory, "manifest.json");
        PrivateContentManifestDocument document = new PrivateContentManifestReader().LoadFile(manifestPath);
        if (!string.Equals(document.Manifest.Language, importedSet.Language, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(document.Manifest.Set.Id, importedSet.SetId, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Imported Set identity does not match its manifest: {importedSet.Language}/{importedSet.SetId}.");
        }

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "manifest.json" };
        foreach (string imagePath in document.Manifest.Cards
                     .Select(card => card?.ImageRelativePath)
                     .Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            string portablePath = imagePath.Replace('\\', '/');
            if (!portablePath.StartsWith("images/", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Manifest image must stay under images/: {importedSet.SetId}/{imagePath}");
            }
            paths.Add(portablePath);
        }
        return paths.OrderBy(path => path, StringComparer.Ordinal).ToArray();
    }

    private static void VerifyRuntimeInstallation(
        ContentPackagePublishResult publication,
        int expectedSetCount,
        string outputRoot)
    {
        string verificationRoot = Path.Combine(
            Path.GetFullPath(outputRoot),
            ".verification-" + Guid.NewGuid().ToString("N"));
        string contentRoot = Path.Combine(verificationRoot, "Content");
        try
        {
            var registry = new FileSystemInstalledContentPackageRegistry(contentRoot);
            var planner = new Gacha.Application.ContentPackagePlanner(
                registry,
                new FileSystemContentStorageProbe(contentRoot),
                0);
            var installer = new FileSystemContentPackageInstaller(contentRoot);
            foreach (PublishedContentPackage package in publication.Packages)
            {
                Gacha.Application.ContentInstallPlan plan = planner.Plan(package.Package);
                if (!plan.CanStart)
                {
                    throw new InvalidOperationException(
                        $"Published package '{package.Package.PackageId}' failed install planning: {plan.ErrorMessage}");
                }
                Gacha.Application.ContentPackageInstallResult installed = installer
                    .InstallAsync(plan, package.ArchivePath)
                    .GetAwaiter()
                    .GetResult();
                if (!installed.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Published package '{package.Package.PackageId}' failed runtime installation: {installed.ErrorMessage}");
                }
            }

            Gacha.Application.CatalogLoadResult catalog = new PrivateContentCatalogProvider(contentRoot).Load();
            if (!catalog.Succeeded || catalog.SourceSetCount != expectedSetCount)
            {
                throw new InvalidOperationException(
                    $"Installed publication failed runtime catalog loading: sets={catalog.SourceSetCount}/{expectedSetCount}, " +
                    (catalog.ErrorMessage ?? "unknown error"));
            }
        }
        finally
        {
            if (Directory.Exists(verificationRoot))
                Directory.Delete(verificationRoot, true);
        }
    }

    private static IReadOnlyList<ImportedSet> DiscoverImports(string root)
    {
        if (!Directory.Exists(root))
            return Array.Empty<ImportedSet>();
        var reader = new PrivateContentManifestReader();
        return Directory.GetFiles(root, "manifest.json", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => reader.LoadFile(path))
            .Select(document => new ImportedSet(
                document.Manifest.Language,
                document.Manifest.Set.Id,
                Path.GetDirectoryName(document.ManifestPath)))
            .ToArray();
    }
}
