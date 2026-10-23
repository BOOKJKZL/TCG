using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

public class TrainerUiVisualContractTests
{
    [Test]
    public void PrimaryScreens_ShareNeutralTopBarWithAnUnadornedLeadingSlot()
    {
        VisualTreeAsset mobileTopBar = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
            "Assets/Resources/UI/Mobile/MobileTopBar.uxml");
        Assert.That(mobileTopBar, Is.Not.Null);
        TemplateContainer mobileRoot = mobileTopBar.CloneTree();
        VisualElement leading = mobileRoot.Q<VisualElement>("top-bar-leading");
        Assert.That(leading, Is.Not.Null);
        Assert.That(leading.childCount, Is.Zero);
        Assert.That(leading.Q<VisualElement>(className: "mobile-top-bar__lens"), Is.Null);
        Assert.That(leading.Query<VisualElement>(className: "mobile-top-bar__light").ToList().Count,
            Is.Zero);
        foreach (string controllerPath in new[]
                     {
                         "Assets/Scripts/004_Controller/GachaViewController.cs",
                         "Assets/Scripts/Modules/Gacha.Presentation/ContentManagementController.cs",
                         "Assets/Scripts/004_Controller/CollectionViewController.cs"
                     })
            Assert.That(File.ReadAllText(controllerPath), Does.Contain("new MobileTopBar"), controllerPath);
    }

    [Test]
    public void PokedexScreen_UsesTheSharedMobileDeviceGrammarWithAnIndependentStylesheet()
    {
        const string path = "Assets/Resources/UI/PokedexView.uxml";
        VisualTreeAsset asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
        Assert.That(asset, Is.Not.Null);
        TemplateContainer root = asset.CloneTree();
        string controller = File.ReadAllText(
            "Assets/Scripts/Modules/Gacha.Pokemon.Presentation/PokemonPokedexController.cs");

        Assert.That(root.Q<VisualElement>("pokedex-body"), Is.Not.Null);
        Assert.That(root.Query<Button>().ToList(), Is.Empty);
        Assert.That(root.Q<VisualElement>("pokedex-overlay").styleSheets.count, Is.GreaterThan(0));
        Assert.That(controller, Does.Contain("new MobilePageShell"));
        Assert.That(controller, Does.Contain("new MobileTopBar"));
    }

    [Test]
    public void CoreNeutral_DefinesSemanticPaletteWithoutChangingOpeningThemeIdentity()
    {
        string tokens = File.ReadAllText("Assets/Resources/UI/CoreNeutralTokens.uss");

        foreach (KeyValuePair<string, string> token in new Dictionary<string, string>
                 {
                     ["--ui-canvas"] = "#F7F8FA",
                     ["--ui-surface"] = "#FFFFFF",
                     ["--ui-text"] = "#14171A",
                     ["--ui-primary"] = "#2563EB"
                 })
        {
            Assert.That(tokens, Does.Contain(token.Key), token.Key);
            Assert.That(tokens.ToUpperInvariant(), Does.Contain(token.Value), token.Key);
        }

        // Opening themes remain content-scoped; the core palette must not replace their identity.
        string opening = File.ReadAllText("Assets/UI/Styles.uss");
        foreach (string theme in new[] { "vintage", "forest", "ruby", "electric", "gallery" })
            Assert.That(opening, Does.Contain(".gacha-theme--" + theme), theme);
    }
}
