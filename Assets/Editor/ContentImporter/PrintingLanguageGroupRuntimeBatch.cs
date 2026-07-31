using System;
using System.IO;
using System.Linq;
using Gacha.Infrastructure.Content;
using Gacha.Infrastructure.Rules;
using UnityEditor;
using UnityEngine;

public static class PrintingLanguageGroupRuntimeBatch
{
    [MenuItem("Tools/Gacha/Compile Runtime Printing Language Groups")]
    public static void RunFromMenu()
    {
        try
        {
            PrintingLanguageGroupRuntimeCompilationResult result = Run();
            Debug.Log(Format(result));
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
            PrintingLanguageGroupRuntimeCompilationResult result = Run();
            Debug.Log(Format(result));
            if (Application.isBatchMode)
                EditorApplication.Exit(result.IsValid ? 0 : 2);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            if (Application.isBatchMode)
                EditorApplication.Exit(1);
        }
    }

    private static PrintingLanguageGroupRuntimeCompilationResult Run() =>
        VerifyRuntime(PrintingLanguageGroupRuntimeCompiler.Compile(IdentityReportPath, OutputPath));

    private static PrintingLanguageGroupRuntimeCompilationResult VerifyRuntime(
        PrintingLanguageGroupRuntimeCompilationResult result)
    {
        if (!result.IsValid)
            return result;
        try
        {
            var reader = new PrintingLanguageGroupManifestReader();
            PrintingLanguageGroupManifestDto overlay = reader.LoadFile(OutputPath);
            var documents = new PrivateContentManifestReader().LoadCardSetDirectory(ImportRoot);
            PrivateCatalogImportResult import = new PrivateManifestCatalogAdapter(
                new PokemonImportedCardVariantPolicy()).Build(
                documents,
                languageGroupManifest: overlay);
            result.RuntimeDefinitionCount = import.Catalog.PrintingLanguageGroups.Count;
            result.RuntimeSourceCardCount = import.SourceCardCount;
            result.RuntimeItemCount = import.Catalog.Items.Count;
            result.RuntimePrintingCount = import.Catalog.Printings.Count;
            if (result.RuntimeDefinitionCount < result.GroupCount)
                result.Failures.Add(
                    $"Only {result.RuntimeDefinitionCount}/{result.GroupCount} accepted source groups " +
                    "produced a runtime language definition.");
            if (import.Catalog.PrintingLanguageGroups.Any(group =>
                    group.PrintingIds.Select(id => import.Catalog.Printings[id].Identity.LanguageId)
                        .Distinct(System.StringComparer.OrdinalIgnoreCase).Count() !=
                    group.PrintingIds.Count))
                result.Failures.Add("Runtime language definition repeats a card language.");
            result.IsValid = result.Failures.Count == 0;
        }
        catch (Exception exception)
        {
            result.Failures.Add("Runtime catalog verification failed: " + exception.Message);
            result.IsValid = false;
        }
        return result;
    }

    private static string Format(PrintingLanguageGroupRuntimeCompilationResult result) =>
        $"Runtime printing language groups valid={result.IsValid}: " +
        $"groups={result.GroupCount}, members={result.MemberCount}, " +
        $"runtime={result.RuntimeDefinitionCount}, cards/items/printings=" +
        $"{result.RuntimeSourceCardCount}/{result.RuntimeItemCount}/{result.RuntimePrintingCount}, " +
        $"bytes={result.OutputBytes}, sha256={result.OutputSha256}, " +
        $"failures={result.Failures.Count}.";

    private static string ProjectRoot =>
        Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

    private static string IdentityReportPath => Path.Combine(
        ProjectRoot, "LocalContent", "Inventory", "multilingual-card-identities.json");

    private static string OutputPath => Path.Combine(
        ProjectRoot,
        "LocalContent",
        "Imports",
        PrintingLanguageGroupManifestReader.InstallRelativeDirectory
            .Replace('/', Path.DirectorySeparatorChar),
        PrintingLanguageGroupManifestReader.FileName);

    private static string ImportRoot => Path.Combine(ProjectRoot, "LocalContent", "Imports");
}
