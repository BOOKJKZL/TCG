using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gacha.Presentation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

public sealed class HomePresentationTests
{
    private static readonly string[] HomeKeys =
    {
        "home.brand",
        "home.pack.choose",
        "home.collection.title",
        "home.collection.unavailable",
        "home.action.library",
        "home.action.view_all",
        "home.action.inspect",
        "home.action.previous",
        "home.action.next",
        "home.action.download",
        "home.action.open",
        "home.library.installed",
        "home.library.language",
        "home.empty.packs",
        "home.empty.collection",
        "home.preview.note",
        "home.nav.showcase",
        "home.nav.collection",
        "home.nav.pokedex"
    };

    [Test]
    public void HomeView_ImportsScrollablePackCarouselAndOwnedCollectionWithoutNativeButtons()
    {
        const string path = "Assets/Resources/UI/HomeView.uxml";
        VisualTreeAsset asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
        Assert.That(asset, Is.Not.Null);
        VisualElement tree = asset.CloneTree();
        Assert.That(tree.Q<ScrollView>("home-scroll"), Is.Not.Null);
        Assert.That(tree.Q<VisualElement>("home-hero"), Is.Not.Null);
        Assert.That(tree.Q<VisualElement>("home-owned-cards"), Is.Not.Null);
        foreach (string name in new[]
                 {
                     "home-gacha-slot", "home-collection-slot", "home-content-slot", "home-retry-slot",
                     "home-previous-slot", "home-next-slot", "home-inspect-slot",
                     "home-pack-left", "home-pack-center", "home-pack-right"
                 })
            Assert.That(tree.Q<VisualElement>(name), Is.Not.Null, name);

        string source = File.ReadAllText(path);
        Assert.That(source, Does.Not.Contain("<ui:Button"));
        Assert.That(source, Does.Not.Match(@"\sstyle\s*="));
    }

    [Test]
    public void HomeText_UsesSharedThreeLanguageLocalizationContract()
    {
        foreach (string key in HomeKeys)
        {
            Assert.That(CardUiText.EnglishFallbacks, Contains.Key(key));
            Assert.That(CardUiText.EnglishFallbacks[key], Is.Not.Empty);
            Assert.That(JapaneseCardUiLocalization.Values, Contains.Key(key));
            Assert.That(JapaneseCardUiLocalization.Values[key], Is.Not.Empty);
        }
    }

    [Test]
    public void HomeController_BuildsShellBeforeContentLoadAndKeepsLoadingOutsidePresenter()
    {
        string controller = File.ReadAllText("Assets/Scripts/004_Controller/MainMenuController.cs");
        string presenter = File.ReadAllText(
            "Assets/Scripts/Modules/Gacha.Presentation/MobileHomePresenter.cs");

        int buildShell = controller.IndexOf("new MobileHomePresenter", System.StringComparison.Ordinal);
        int loadContent = controller.IndexOf("BeginContentLoad(false)", System.StringComparison.Ordinal);
        Assert.That(buildShell, Is.GreaterThanOrEqualTo(0));
        Assert.That(loadContent, Is.GreaterThan(buildShell));
        Assert.That(controller, Does.Contain("HideLegacyCanvas"));
        Assert.That(presenter, Does.Contain("MobilePageShell"));
        Assert.That(presenter, Does.Contain("MobilePrimaryNavigation"));
        Assert.That(presenter, Does.Not.Contain("EnsureLoaded"));
        Assert.That(presenter, Does.Not.Contain("SceneManager"));
    }

    [Test]
    public void FirstRunOverlay_UsesStableActionsWithoutInlineVisibility()
    {
        const string path = "Assets/Resources/UI/FirstRunContentSetup.uxml";
        VisualTreeAsset asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
        Assert.That(asset, Is.Not.Null);
        VisualElement tree = asset.CloneTree();
        Assert.That(tree.Query<Button>().ToList(), Is.Empty);
        Assert.That(tree.Q<VisualElement>("setup-manage"), Is.Not.Null);
        Assert.That(tree.Q<VisualElement>("setup-retry"), Is.Not.Null);
        Assert.That(tree.Q<VisualElement>("setup-later"), Is.Not.Null);
        Assert.That(File.ReadAllText(path), Does.Not.Match(@"\sstyle\s*="));
    }
}
