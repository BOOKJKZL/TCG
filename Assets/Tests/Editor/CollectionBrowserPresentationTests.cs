using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public class CollectionBrowserPresentationTests
{
    [Test]
    public void CollectionView_ContainsVirtualizedBrowserAndDetailsElements()
    {
        VisualTreeAsset asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/CollectionView.uxml");
        Assert.That(asset, Is.Not.Null);

        TemplateContainer root = asset.CloneTree();
        Assert.That(root.Q<ListView>("set-list"), Is.Not.Null);
        Assert.That(root.Q<ListView>("card-list"), Is.Not.Null);
        Assert.That(root.Q<VisualElement>("details-panel"), Is.Not.Null);
        Assert.That(root.Q<VisualElement>("detail-art-slot"), Is.Not.Null);
        Assert.That(root.styleSheets.count, Is.GreaterThan(0));
    }

    [Test]
    public void CollectionScene_HasConfiguredUiDocument()
    {
        const string scenePath = "Assets/Scenes/004_CollectionScene.unity";
        Scene scene = SceneManager.GetSceneByPath(scenePath);
        bool openedForTest = !scene.IsValid() || !scene.isLoaded;
        if (openedForTest)
            scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
        try
        {
            CollectionViewController controller = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<CollectionViewController>(true))
                .Single();
            UIDocument document = controller.GetComponent<UIDocument>();
            var serializedController = new SerializedObject(controller);

            Assert.That(document, Is.Not.Null);
            Assert.That(document.visualTreeAsset, Is.Not.Null);
            Assert.That(document.visualTreeAsset.name, Is.EqualTo("CollectionView"));
            Assert.That(serializedController.FindProperty("viewAsset").objectReferenceValue, Is.Not.Null);
            Assert.That(
                AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/Resources/UI/Collection Panel Settings.asset"),
                Is.Not.Null);
        }
        finally
        {
            if (openedForTest)
                EditorSceneManager.CloseScene(scene, true);
        }
    }
}
