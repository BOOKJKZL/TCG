using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor.Compilation;

public class RuntimeAssemblyBoundaryTests
{
    [Test]
    public void RuntimeAssemblies_OwnEveryLegacyApplicationScriptOutsideModules()
    {
        string projectRoot = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
        string scriptsRoot = Path.Combine(UnityEngine.Application.dataPath, "Scripts");
        var expected = new HashSet<string>(Directory.GetFiles(scriptsRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !Normalize(path).Contains("/Assets/Scripts/Modules/"))
            .Select(path => ProjectRelative(projectRoot, path)), StringComparer.OrdinalIgnoreCase);
        var expectedCore = new HashSet<string>(expected.Where(path =>
            Normalize(path).Contains("Assets/Scripts/002_Core/")), StringComparer.OrdinalIgnoreCase);
        var expectedCompositionRoot = new HashSet<string>(expected.Except(
            expectedCore,
            StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);

        UnityEditor.Compilation.Assembly[] playerAssemblies =
            CompilationPipeline.GetAssemblies(AssembliesType.Player);
        UnityEditor.Compilation.Assembly runtime = playerAssemblies.SingleOrDefault(assembly =>
            string.Equals(assembly.name, "Gacha.Runtime", StringComparison.Ordinal));
        UnityEditor.Compilation.Assembly core = playerAssemblies.SingleOrDefault(assembly =>
            string.Equals(assembly.name, "Gacha.Runtime.Core", StringComparison.Ordinal));

        Assert.That(runtime, Is.Not.Null, "Gacha.Runtime must be present in the player compilation graph.");
        Assert.That(core, Is.Not.Null, "Gacha.Runtime.Core must be present in the player compilation graph.");
        var actualCompositionRoot = new HashSet<string>(runtime.sourceFiles
            .Select(path => ProjectRelative(projectRoot, path)), StringComparer.OrdinalIgnoreCase);
        var actualCore = new HashSet<string>(core.sourceFiles
            .Select(path => ProjectRelative(projectRoot, path)), StringComparer.OrdinalIgnoreCase);
        var actual = new HashSet<string>(actualCompositionRoot, StringComparer.OrdinalIgnoreCase);
        actual.UnionWith(actualCore);

        Assert.That(actualCompositionRoot, Is.EquivalentTo(expectedCompositionRoot));
        Assert.That(actualCore, Is.EquivalentTo(expectedCore));
        Assert.That(actual, Is.EquivalentTo(expected));
        Assert.That(actual.Count, Is.GreaterThanOrEqualTo(33));

        string runtimeDefinition = File.ReadAllText(Path.Combine(scriptsRoot, "Gacha.Runtime.asmdef"));
        string coreDefinition = File.ReadAllText(Path.Combine(scriptsRoot, "002_Core", "Gacha.Runtime.Core.asmdef"));
        StringAssert.Contains("\"Gacha.Runtime.Core\"", runtimeDefinition);
        Assert.That(Regex.IsMatch(
                coreDefinition,
                "\\\"references\\\"\\s*:\\s*\\[[^\\]]*\\\"Gacha\\.Runtime\\\"\\s*(?:,|\\])",
                RegexOptions.Singleline),
            Is.False,
            "The core runtime boundary must not reference its composition root.");

        UnityEditor.Compilation.Assembly predefined = playerAssemblies.SingleOrDefault(assembly =>
            string.Equals(assembly.name, "Assembly-CSharp", StringComparison.Ordinal));
        if (predefined == null)
            return;

        Assert.That(predefined.sourceFiles
            .Select(path => ProjectRelative(projectRoot, path))
            .Intersect(expected, StringComparer.OrdinalIgnoreCase), Is.Empty);
    }

    private static string ProjectRelative(string projectRoot, string path)
    {
        string fullPath = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(projectRoot, path));
        return Normalize(fullPath.Substring(projectRoot.Length).TrimStart(Path.DirectorySeparatorChar));
    }

    private static string Normalize(string path)
    {
        return path.Replace('\\', '/');
    }
}
