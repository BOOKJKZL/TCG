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
    public void ExplicitRuntimeAssemblies_OwnEveryLegacyApplicationScriptOutsideModules()
    {
        string projectRoot = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
        string scriptsRoot = Path.Combine(UnityEngine.Application.dataPath, "Scripts");
        var expected = new HashSet<string>(Directory.GetFiles(scriptsRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !Normalize(path).Contains("/Assets/Scripts/Modules/"))
            .Select(path => ProjectRelative(projectRoot, path)), StringComparer.OrdinalIgnoreCase);
        var expectedCore = new HashSet<string>(expected.Where(path =>
            Normalize(path).Contains("Assets/Scripts/002_Core/")), StringComparer.OrdinalIgnoreCase);
        var expectedFoundation = new HashSet<string>(expected.Where(path =>
            Normalize(path).Contains("Assets/Scripts/001_Baisc/")), StringComparer.OrdinalIgnoreCase);
        var expectedUtility = new HashSet<string>(expected.Where(path =>
            Normalize(path).Contains("Assets/Scripts/005_Helper/")), StringComparer.OrdinalIgnoreCase);
        var expectedControllers = new HashSet<string>(expected.Where(path =>
            Normalize(path).Contains("Assets/Scripts/004_Controller/")), StringComparer.OrdinalIgnoreCase);

        UnityEditor.Compilation.Assembly[] playerAssemblies =
            CompilationPipeline.GetAssemblies(AssembliesType.Player);
        UnityEditor.Compilation.Assembly core = playerAssemblies.SingleOrDefault(assembly =>
            string.Equals(assembly.name, "Gacha.Runtime.Core", StringComparison.Ordinal));
        UnityEditor.Compilation.Assembly foundation = playerAssemblies.SingleOrDefault(assembly =>
            string.Equals(assembly.name, "Gacha.Runtime.Foundation", StringComparison.Ordinal));
        UnityEditor.Compilation.Assembly utility = playerAssemblies.SingleOrDefault(assembly =>
            string.Equals(assembly.name, "Gacha.Runtime.Utility", StringComparison.Ordinal));
        UnityEditor.Compilation.Assembly controllers = playerAssemblies.SingleOrDefault(assembly =>
            string.Equals(assembly.name, "Gacha.Runtime.Controllers", StringComparison.Ordinal));

        Assert.That(playerAssemblies.Any(assembly =>
                string.Equals(assembly.name, "Gacha.Runtime", StringComparison.Ordinal)),
            Is.False,
            "The empty composition-root assembly must remain retired.");
        Assert.That(core, Is.Not.Null, "Gacha.Runtime.Core must be present in the player compilation graph.");
        Assert.That(foundation, Is.Not.Null,
            "Gacha.Runtime.Foundation must be present in the player compilation graph.");
        Assert.That(utility, Is.Not.Null,
            "Gacha.Runtime.Utility must be present in the player compilation graph.");
        Assert.That(controllers, Is.Not.Null,
            "Gacha.Runtime.Controllers must be present in the player compilation graph.");
        var actualCore = new HashSet<string>(core.sourceFiles
            .Select(path => ProjectRelative(projectRoot, path)), StringComparer.OrdinalIgnoreCase);
        var actualFoundation = new HashSet<string>(foundation.sourceFiles
            .Select(path => ProjectRelative(projectRoot, path)), StringComparer.OrdinalIgnoreCase);
        var actualUtility = new HashSet<string>(utility.sourceFiles
            .Select(path => ProjectRelative(projectRoot, path)), StringComparer.OrdinalIgnoreCase);
        var actualControllers = new HashSet<string>(controllers.sourceFiles
            .Select(path => ProjectRelative(projectRoot, path)), StringComparer.OrdinalIgnoreCase);
        var actual = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        actual.UnionWith(actualCore);
        actual.UnionWith(actualFoundation);
        actual.UnionWith(actualUtility);
        actual.UnionWith(actualControllers);

        Assert.That(actualCore, Is.EquivalentTo(expectedCore));
        Assert.That(actualFoundation, Is.EquivalentTo(expectedFoundation));
        Assert.That(actualUtility, Is.EquivalentTo(expectedUtility));
        Assert.That(actualControllers, Is.EquivalentTo(expectedControllers));
        Assert.That(actual, Is.EquivalentTo(expected));
        Assert.That(actual.Count, Is.GreaterThanOrEqualTo(33));

        Assert.That(File.Exists(Path.Combine(scriptsRoot, "Gacha.Runtime.asmdef")), Is.False);
        string coreDefinition = File.ReadAllText(Path.Combine(scriptsRoot, "002_Core", "Gacha.Runtime.Core.asmdef"));
        string foundationDefinition = File.ReadAllText(Path.Combine(
            scriptsRoot,
            "001_Baisc",
            "Gacha.Runtime.Foundation.asmdef"));
        string utilityDefinition = File.ReadAllText(Path.Combine(
            scriptsRoot,
            "005_Helper",
            "Gacha.Runtime.Utility.asmdef"));
        string controllersDefinition = File.ReadAllText(Path.Combine(
            scriptsRoot,
            "004_Controller",
            "Gacha.Runtime.Controllers.asmdef"));
        Assert.That(ReferencesAssembly(coreDefinition, "Gacha.Runtime"), Is.False,
            "The core runtime boundary must not reference its composition root.");
        Assert.That(ReferencesAssembly(foundationDefinition, "Gacha.Runtime.Core"), Is.True);
        Assert.That(ReferencesAssembly(foundationDefinition, "Gacha.Runtime.Utility"), Is.True);
        Assert.That(ReferencesAssembly(foundationDefinition, "Gacha.Runtime"), Is.False,
            "The foundation must not reference its composition root.");
        Assert.That(ReferencesAssembly(utilityDefinition, "Gacha.Runtime"), Is.False);
        Assert.That(ReferencesAssembly(utilityDefinition, "Gacha.Runtime.Foundation"), Is.False);
        Assert.That(ReferencesAssembly(controllersDefinition, "Gacha.Runtime.Foundation"), Is.True);
        Assert.That(ReferencesAssembly(controllersDefinition, "Gacha.Runtime.Core"), Is.True);
        Assert.That(ReferencesAssembly(controllersDefinition, "Gacha.Runtime"), Is.False,
            "Player controllers must depend on explicit runtime boundaries, not the composition root.");

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

    private static bool ReferencesAssembly(string definition, string assemblyName)
    {
        return Regex.IsMatch(
            definition,
            "\\\"references\\\"\\s*:\\s*\\[[^\\]]*\\\"" + Regex.Escape(assemblyName) + "\\\"\\s*(?:,|\\])",
            RegexOptions.Singleline);
    }
}
