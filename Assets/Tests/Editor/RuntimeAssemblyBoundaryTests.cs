using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor.Compilation;

public class RuntimeAssemblyBoundaryTests
{
    [Test]
    public void RuntimeAssembly_OwnsEveryLegacyApplicationScriptOutsideModules()
    {
        string projectRoot = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
        string scriptsRoot = Path.Combine(UnityEngine.Application.dataPath, "Scripts");
        var expected = new HashSet<string>(Directory.GetFiles(scriptsRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !Normalize(path).Contains("/Assets/Scripts/Modules/"))
            .Select(path => ProjectRelative(projectRoot, path)), StringComparer.OrdinalIgnoreCase);

        UnityEditor.Compilation.Assembly[] playerAssemblies =
            CompilationPipeline.GetAssemblies(AssembliesType.Player);
        UnityEditor.Compilation.Assembly runtime = playerAssemblies.SingleOrDefault(assembly =>
            string.Equals(assembly.name, "Gacha.Runtime", StringComparison.Ordinal));

        Assert.That(runtime, Is.Not.Null, "Gacha.Runtime must be present in the player compilation graph.");
        var actual = new HashSet<string>(runtime.sourceFiles
            .Select(path => ProjectRelative(projectRoot, path)), StringComparer.OrdinalIgnoreCase);

        Assert.That(actual, Is.EquivalentTo(expected));
        Assert.That(actual.Count, Is.GreaterThanOrEqualTo(33));

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
