using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

public class TrainerUiVisualContractTests
{
    [Test]
    public void PrimaryScreens_ShareTrainerDeviceRailAndFourStatusLights()
    {
        VisualTreeAsset mobileTopBar = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
            "Assets/Resources/UI/Mobile/MobileTopBar.uxml");
        Assert.That(mobileTopBar, Is.Not.Null);
        TemplateContainer mobileRoot = mobileTopBar.CloneTree();
        VisualElement leading = mobileRoot.Q<VisualElement>("top-bar-leading");
        Assert.That(leading, Is.Not.Null);
        Assert.That(leading.childCount, Is.EqualTo(4));
        Assert.That(leading.Q<VisualElement>(className: "mobile-top-bar__lens"), Is.Not.Null);
        Assert.That(leading.Query<VisualElement>(className: "mobile-top-bar__light").ToList().Count,
            Is.EqualTo(3));
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
    public void TrainerTheme_DefinesRedBlueYellowDevicePaletteAndReadableLightCards()
    {
        string shared = File.ReadAllText("Assets/UI/Styles.uss");
        string pokedex = File.ReadAllText("Assets/Resources/UI/PokedexStyles.uss");

        foreach (KeyValuePair<string, string> token in new Dictionary<string, string>
                 {
                     ["red"] = "rgb(205, 49, 61)",
                     ["blue"] = "rgb(43, 118, 190)",
                     ["yellow"] = "rgb(255, 211, 65)",
                     ["light card"] = "rgb(243, 247, 250)"
                 })
            Assert.That(shared, Does.Contain(token.Value), token.Key);

        Assert.That(shared, Does.Contain(".trainer-status-light--lens"));
        Assert.That(pokedex, Does.Contain("rgb(190, 49, 61)"));
        Assert.That(pokedex, Does.Contain("rgb(35, 91, 143)"));
        Assert.That(pokedex, Does.Contain("rgb(255, 211, 92)"));
    }
}
