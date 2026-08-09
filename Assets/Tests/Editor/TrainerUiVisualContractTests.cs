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
        foreach (string path in new[]
                 {
                     "Assets/UI/CollectionView.uxml"
                 })
        {
            VisualTreeAsset asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
            Assert.That(asset, Is.Not.Null, path);
            TemplateContainer root = asset.CloneTree();
            VisualElement rail = root.Q<VisualElement>("trainer-device-rail");
            VisualElement lights = root.Q<VisualElement>("trainer-status-lights");
            Assert.That(rail, Is.Not.Null, path);
            Assert.That(rail.childCount, Is.EqualTo(3), path);
            Assert.That(lights, Is.Not.Null, path);
            Assert.That(lights.childCount, Is.EqualTo(4), path);
        }

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
                     "Assets/Scripts/Modules/Gacha.Presentation/ContentManagementController.cs"
                 })
            Assert.That(File.ReadAllText(controllerPath), Does.Contain("new MobileTopBar"), controllerPath);
    }

    [Test]
    public void PokedexScreen_UsesTheSameDeviceGrammarWithAnIndependentStylesheet()
    {
        const string path = "Assets/Resources/UI/PokedexView.uxml";
        VisualTreeAsset asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
        Assert.That(asset, Is.Not.Null);
        TemplateContainer root = asset.CloneTree();
        VisualElement rail = root.Q<VisualElement>("pokedex-device-rail");
        VisualElement lights = root.Q<VisualElement>("pokedex-status-lights");

        Assert.That(rail, Is.Not.Null);
        Assert.That(rail.childCount, Is.EqualTo(3));
        Assert.That(lights, Is.Not.Null);
        Assert.That(lights.childCount, Is.EqualTo(4));
        Assert.That(root.styleSheets.count, Is.GreaterThan(0));
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
        Assert.That(pokedex, Does.Contain(".pokedex-status-light--lens"));
        Assert.That(pokedex, Does.Contain("background-color: rgb(181, 43, 54)"));
        Assert.That(pokedex, Does.Contain("background-color: rgb(255, 255, 255)"));
    }
}
