using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public class GachaOpeningPresentationTests
{
    [Test]
    public void GachaView_ContainsSelectionRevealAndSummaryStages()
    {
        VisualTreeAsset asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/GachaView.uxml");
        Assert.That(asset, Is.Not.Null);

        TemplateContainer root = asset.CloneTree();
        Assert.That(root.Q<ListView>("product-list"), Is.Not.Null);
        Assert.That(root.Q<VisualElement>("pack-stage"), Is.Not.Null);
        Assert.That(root.Q<VisualElement>("pack-particle-layer"), Is.Not.Null);
        Assert.That(root.Q<VisualElement>("pack-theme-artwork"), Is.Not.Null);
        Assert.That(root.Q<VisualElement>("pack-theme-band"), Is.Not.Null);
        Assert.That(root.Q<VisualElement>("pack-tear-line"), Is.Not.Null);
        Assert.That(root.Q<VisualElement>("reveal-stage"), Is.Not.Null);
        Assert.That(root.Q<VisualElement>("reveal-particle-layer"), Is.Not.Null);
        Assert.That(root.Q<VisualElement>("reveal-aura"), Is.Not.Null);
        Assert.That(root.Q<VisualElement>("reveal-art-slot"), Is.Not.Null);
        Assert.That(root.Q<ScrollView>("summary-list"), Is.Not.Null);
        Assert.That(root.Q<VisualElement>("tear-pack-button")?.Q<Label>(), Is.Not.Null);
        Assert.That(root.Q<VisualElement>("reveal-next-button")?.Q<Label>(), Is.Not.Null);
        Assert.That(root.Q<Label>("rule-evidence-summary"), Is.Not.Null);
        Assert.That(root.Q<VisualElement>("rule-source-list"), Is.Not.Null);
        Assert.That(root.styleSheets.count, Is.GreaterThan(0));
        Assert.That(root.Query<Button>().ToList(), Is.Empty);
        string source = File.ReadAllText("Assets/UI/GachaView.uxml");
        Assert.That(source, Does.Not.Contain("<ui:Button"));
        Assert.That(source, Does.Not.Contain("style="));
        Assert.That(source, Does.Contain("virtualization-method=\"DynamicHeight\""));
    }

    [Test]
    public void GachaLocalization_ContainsConfirmationKeysInEveryLocale()
    {
        string[] keys =
        {
            "common.action.cancel",
            "gacha.confirm.title",
            "gacha.confirm.body",
            "gacha.action.confirm_open"
        };
        string[] tables =
        {
            "Assets/Resources/Data/Localization/Card_UI_en.asset",
            "Assets/Resources/Data/Localization/Card_UI_zh.asset",
            "Assets/Resources/Data/Localization/Card_UI_ja.asset"
        };
        string shared = File.ReadAllText(
            "Assets/Resources/Data/Localization/Card_UI Shared Data.asset");
        foreach (string key in keys)
        {
            Assert.That(shared, Does.Contain("m_Key: " + key), key);
            Assert.That(Gacha.Presentation.CardUiText.EnglishFallbacks.ContainsKey(key), Is.True, key);
        }
        foreach (string table in tables)
        {
            string contents = File.ReadAllText(table);
            foreach (long id in new[]
                     {
                         172203318969020422L,
                         172203318969020423L,
                         172203318969020424L,
                         172203318969020425L
                     })
                Assert.That(contents, Does.Contain("m_Id: " + id), table);
        }
    }

    [Test]
    public void PokemonThemePackArtwork_IsPresentAndMobileSized()
    {
        string[] paths =
        {
            "Assets/Resources/Gacha/Themes/vintage-pack.png",
            "Assets/Resources/Gacha/Themes/forest-pack.png",
            "Assets/Resources/Gacha/Themes/ruby-pack.png",
            "Assets/Resources/Gacha/Themes/electric-pack.png",
            "Assets/Resources/Gacha/Themes/gallery-pack.png"
        };

        foreach (string path in paths)
        {
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.That(texture, Is.Not.Null, path);
            Assert.That(importer, Is.Not.Null, path);
            Assert.That(texture.width, Is.LessThanOrEqualTo(512), path);
            Assert.That(texture.height, Is.LessThanOrEqualTo(512), path);
            Assert.That(importer.maxTextureSize, Is.EqualTo(512), path);
            Assert.That(importer.mipmapEnabled, Is.False, path);
            Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp), path);
            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            Assert.That(android.overridden, Is.True, path);
            Assert.That(android.maxTextureSize, Is.EqualTo(512), path);
            Assert.That(android.format, Is.EqualTo(TextureImporterFormat.ASTC_6x6), path);
        }
    }

    [Test]
    public void GachaScene_HasConfiguredUiDocument()
    {
        const string scenePath = "Assets/Scenes/003_GachaScene.unity";
        Scene scene = SceneManager.GetSceneByPath(scenePath);
        bool openedForTest = !scene.IsValid() || !scene.isLoaded;
        if (openedForTest)
            scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
        try
        {
            GachaViewController controller = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<GachaViewController>(true))
                .Single();
            UIDocument document = controller.GetComponent<UIDocument>();
            var serializedController = new SerializedObject(controller);

            Assert.That(document, Is.Not.Null);
            Assert.That(document.visualTreeAsset, Is.Not.Null);
            Assert.That(document.visualTreeAsset.name, Is.EqualTo("GachaView"));
            Assert.That(serializedController.FindProperty("viewAsset").objectReferenceValue, Is.Not.Null);
            Assert.That(serializedController.FindProperty("cardsPerPack").intValue, Is.EqualTo(5));
            Assert.That(serializedController.FindProperty("textureCacheCapacity").intValue, Is.EqualTo(24));
        }
        finally
        {
            if (openedForTest)
                EditorSceneManager.CloseScene(scene, true);
        }
    }
}
