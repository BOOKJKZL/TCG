using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
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
        Assert.That(root.Q<VisualElement>("pack-tear-line"), Is.Not.Null);
        Assert.That(root.Q<VisualElement>("reveal-stage"), Is.Not.Null);
        Assert.That(root.Q<VisualElement>("reveal-art-slot"), Is.Not.Null);
        Assert.That(root.Q<ScrollView>("summary-list"), Is.Not.Null);
        Assert.That(root.Q<Button>("tear-pack-button"), Is.Not.Null);
        Assert.That(root.Q<Button>("reveal-next-button"), Is.Not.Null);
        Assert.That(root.styleSheets.count, Is.GreaterThan(0));
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
