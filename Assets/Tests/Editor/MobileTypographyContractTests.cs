using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Gacha.Presentation;
using NUnit.Framework;
using UnityEngine.UIElements;

public sealed class MobileTypographyContractTests
{
    [Test]
    public void EveryExplicitTypographyRole_MatchesItsAuthoringStylesheet()
    {
        foreach (MobileTypographyProfile profile in MobileTypographyProfiles.All)
        foreach (MobileTypographyRule rule in profile.Rules)
        {
            string css = Regex.Replace(File.ReadAllText(rule.StyleSheetPath), @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
            css = Regex.Replace(css, @"@import[^;]+;", string.Empty);
            float? normal = null, compact = null;
            foreach (Match block in Regex.Matches(css, @"([^{}]+)\{([^{}]+)\}"))
            {
                Match size = Regex.Match(block.Groups[2].Value, @"font-size:\s*([0-9.]+)px");
                if (!size.Success) continue;
                foreach (string selector in block.Groups[1].Value.Split(','))
                {
                    string normalized = Regex.Replace(selector.Trim(), @"\s+", " ");
                    float value = float.Parse(size.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    if (normalized == "." + rule.ClassName) normal = value;
                    if (normalized == ".mobile-layout--compact ." + rule.ClassName) compact = value;
                }
            }
            Assert.That(normal, Is.EqualTo(rule.BaseSize), profile.Name + ": " + rule.ClassName);
            Assert.That(compact ?? normal, Is.EqualTo(rule.CompactSize), profile.Name + " compact: " + rule.ClassName);
        }
    }

    [TestCase("Assets/Resources/UI/StartView.uxml")]
    [TestCase("Assets/Resources/UI/HomeView.uxml")]
    [TestCase("Assets/Resources/UI/SettingsView.uxml")]
    [TestCase("Assets/Resources/UI/FirstRunContentSetup.uxml")]
    [TestCase("Assets/Resources/UI/PokedexView.uxml")]
    [TestCase("Assets/UI/CollectionView.uxml")]
    [TestCase("Assets/UI/GachaView.uxml")]
    [TestCase("Assets/UI/ContentManagementView.uxml")]
    public void ProductionPageTextAndInputs_HaveAnExplicitRole(string path)
    {
        foreach (XElement element in XDocument.Load(path).Descendants().Where(element =>
                     new[] { "Label", "TextField", "DropdownField", "Toggle" }.Contains(element.Name.LocalName)))
        {
            string[] classes = ((string)element.Attribute("class") ?? string.Empty).Split(' ');
            // This large number is an artwork placeholder, duplicated by the scalable detail-number label.
            if (classes.Contains("pokedex-artwork__number")) continue;
            Assert.That(MobileTypographyProfiles.All.SelectMany(profile => profile.Rules)
                    .Any(rule => classes.Contains(rule.ClassName)), Is.True,
                path + ": " + ((string)element.Attribute("name") ?? element.Name.LocalName));
        }
    }

    [Test]
    public void SharedAndPageProfiles_DoNotAssignDifferentSizesToTheSameRole()
    {
        foreach (var role in MobileTypographyProfiles.All.SelectMany(profile => profile.Rules).GroupBy(rule => rule.ClassName))
        {
            Assert.That(role.Select(rule => (rule.BaseSize, rule.CompactSize)).Distinct().Count(), Is.EqualTo(1), role.Key);
        }
    }

    [Test]
    public void RoleOwnership_IsIdempotentAndRejectsConflictingBaselines()
    {
        var label = new Label("Sample");
        label.AddToClassList("mobile-action__label");
        StyleLength original = label.style.fontSize;
        MobileTypographyBinding first = MobileTypography.Bind(label, MobileTypographyProfiles.Shared);
        try
        {
            Assert.That(MobileTypography.Bind(label, MobileTypographyProfiles.Shared), Is.SameAs(first));
            Assert.That(first.TargetCount, Is.EqualTo(1));
            var conflict = new MobileTypographyProfile("Conflicting page",
                new MobileTypographyRule("mobile-action__label", 28f, "unused"));
            Assert.Throws<InvalidOperationException>(() => MobileTypography.Bind(label, conflict));
            Assert.That(label.style.fontSize.value.value, Is.EqualTo(17f));
        }
        finally { first.Dispose(); }
        Assert.That(label.style.fontSize, Is.EqualTo(original));
    }

    [TestCase(0f)]
    [TestCase(-1f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void InvalidSystemScale_UsesTheNominalTypography(float value) =>
        Assert.That(MobileTypography.NormalizeScale(value), Is.EqualTo(1f));
}
