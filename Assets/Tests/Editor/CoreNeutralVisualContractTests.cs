using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class CoreNeutralVisualContractTests
{
    private const string TokenPath = "Assets/Resources/UI/CoreNeutralTokens.uss";

    private static readonly IReadOnlyDictionary<string, string> ExpectedColors =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["canvas"] = "#F7F8FA",
            ["surface"] = "#FFFFFF",
            ["raised"] = "#F1F4F8",
            ["text"] = "#14171A",
            ["text-secondary"] = "#556170",
            ["primary"] = "#2563EB",
            ["on-primary"] = "#FFFFFF",
            ["primary-soft"] = "#EAF0FF",
            ["outline"] = "#C7CED8",
            ["success"] = "#176B45",
            ["success-surface"] = "#EAF5EE",
            ["warning"] = "#805600",
            ["warning-surface"] = "#FFF4D6",
            ["error"] = "#B42332",
            ["error-surface"] = "#FFF0F1"
        };

    [Test]
    public void SemanticTokens_MatchTheCoreNeutralBaselineAndHaveOneDefinition()
    {
        Assert.That(AssetDatabase.LoadAssetAtPath<StyleSheet>(TokenPath), Is.Not.Null,
            "The authoritative token file must also import as a Unity StyleSheet.");
        Dictionary<string, Color> colors = ReadTokenColors();
        foreach (KeyValuePair<string, string> expected in ExpectedColors)
        {
            Assert.That(colors.ContainsKey(expected.Key), Is.True, "Missing --ui-" + expected.Key);
            Assert.That(ColorUtility.ToHtmlStringRGBA(colors[expected.Key]),
                Is.EqualTo(ColorUtility.ToHtmlStringRGBA(ParseColor(expected.Value))), expected.Key);
        }

        foreach (string directory in new[] { "Assets/Resources/UI", "Assets/UI" })
        foreach (string path in Directory.EnumerateFiles(directory, "*.uss", SearchOption.AllDirectories))
        {
            if (Path.GetFullPath(path) == Path.GetFullPath(TokenPath))
                continue;
            string source = WithoutComments(File.ReadAllText(path));
            foreach (string token in ExpectedColors.Keys)
                Assert.That(Regex.IsMatch(source, @"--ui-" + Regex.Escape(token) + @"\s*:"), Is.False,
                    path + " must consume, rather than redefine, --ui-" + token);
        }
    }

    [TestCase("text", "canvas")]
    [TestCase("text", "surface")]
    [TestCase("text", "raised")]
    [TestCase("text", "primary-soft")]
    [TestCase("text-secondary", "canvas")]
    [TestCase("text-secondary", "surface")]
    [TestCase("text-secondary", "raised")]
    [TestCase("text-secondary", "primary-soft")]
    [TestCase("on-primary", "primary")]
    [TestCase("primary-soft", "primary")]
    [TestCase("on-primary", "error")]
    [TestCase("success", "success-surface")]
    [TestCase("warning", "warning-surface")]
    [TestCase("error", "error-surface")]
    public void TextAndStatusPairs_HaveAtLeastFourPointFiveContrast(string foreground, string background)
    {
        // Read the shipped source, not the expected palette: a changed token must change this result.
        Dictionary<string, Color> colors = ReadTokenColors();
        double first = Luminance(colors[foreground]);
        double second = Luminance(colors[background]);
        double ratio = (Math.Max(first, second) + 0.05d) / (Math.Min(first, second) + 0.05d);
        Assert.That(ratio, Is.GreaterThanOrEqualTo(4.5d),
            "--ui-" + foreground + " on --ui-" + background + " contrast is " + ratio.ToString("F3"));
    }

    [TestCase("Assets/Resources/UI/MobileGameDesignSystem.uss")]
    [TestCase("Assets/Resources/UI/HomeView.uss")]
    [TestCase("Assets/Resources/UI/SettingsView.uss")]
    [TestCase("Assets/Resources/UI/FirstRunContentSetup.uss")]
    [TestCase("Assets/Resources/UI/PokedexStyles.uss")]
    [TestCase("Assets/UI/Styles.uss")]
    public void PageStyles_ImportAndConsumeSemanticTokens(string path)
    {
        string source = WithoutComments(File.ReadAllText(path));
        Assert.That(source, Does.Contain("CoreNeutralTokens.uss"), path);
        Assert.That(source, Does.Contain("var(--ui-"), path);
        Assert.That(AssetDatabase.GetDependencies(path), Does.Contain(TokenPath),
            "A source import string alone does not prove Unity resolves the token asset.");
    }

    [Test]
    public void SharedTopBar_ContainsNoNonBusinessLensOrStatusLights()
    {
        VisualTreeAsset template = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
            "Assets/Resources/UI/Mobile/MobileTopBar.uxml");
        Assert.That(template, Is.Not.Null);
        TemplateContainer root = template.CloneTree();
        Assert.That(root.Q<Label>("top-bar-title"), Is.Not.Null);
        Assert.That(root.Q<VisualElement>("top-bar-actions"), Is.Not.Null);
        Assert.That(root.Query<VisualElement>(className: "mobile-top-bar__lens").ToList(), Is.Empty);
        Assert.That(root.Query<VisualElement>(className: "mobile-top-bar__light").ToList(), Is.Empty);
    }

    private static Dictionary<string, Color> ReadTokenColors()
    {
        Assert.That(File.Exists(TokenPath), Is.True, TokenPath);
        string source = WithoutComments(File.ReadAllText(TokenPath));
        var colors = new Dictionary<string, Color>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(source, @"--ui-([a-z0-9-]+)\s*:\s*([^;{}]+);"))
        {
            string name = match.Groups[1].Value;
            if (!ExpectedColors.ContainsKey(name))
                continue;
            Assert.That(colors.ContainsKey(name), Is.False, "Duplicate definition of --ui-" + name);
            colors.Add(name, ParseColor(match.Groups[2].Value.Trim()));
        }
        foreach (string expected in ExpectedColors.Keys)
            Assert.That(colors.ContainsKey(expected), Is.True, "Missing raw definition of --ui-" + expected);
        return colors;
    }

    private static string WithoutComments(string source) =>
        Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

    private static Color ParseColor(string value)
    {
        if (ColorUtility.TryParseHtmlString(value, out Color hex))
            return hex;
        Match rgb = Regex.Match(value, @"^rgb\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)$");
        Assert.That(rgb.Success, Is.True, "Expected an opaque literal color, not an alias: " + value);
        var components = new float[3];
        for (int index = 0; index < components.Length; index++)
        {
            int component = int.Parse(rgb.Groups[index + 1].Value, CultureInfo.InvariantCulture);
            Assert.That(component, Is.InRange(0, 255), value);
            components[index] = component / 255f;
        }
        return new Color(components[0], components[1], components[2], 1f);
    }

    private static double Luminance(Color color) =>
        0.2126d * Linear(color.r) + 0.7152d * Linear(color.g) + 0.0722d * Linear(color.b);

    private static double Linear(double component) => component <= 0.04045d
        ? component / 12.92d
        : Math.Pow((component + 0.055d) / 1.055d, 2.4d);
}
