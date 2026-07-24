using System.Collections.Generic;
using System.Linq;
using Gacha.Presentation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Localization;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Localization.Tables;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public class CollectionBrowserPresentationTests
{
    [Test]
    public void CollectionAndCardImageText_UsesCompleteEnglishAndChineseStringTableEntries()
    {
        StringTableCollection collection = LocalizationEditorSettings.GetStringTableCollection(CardUiText.TableName);
        StringTable english = collection.GetTable("en") as StringTable;
        StringTable chinese = collection.GetTable("zh") as StringTable;

        Assert.That(english, Is.Not.Null);
        Assert.That(chinese, Is.Not.Null);
        foreach (KeyValuePair<string, string> pair in CardUiText.EnglishFallbacks)
        {
            StringTableEntry englishEntry = english.GetEntry(pair.Key);
            StringTableEntry chineseEntry = chinese.GetEntry(pair.Key);
            Assert.That(englishEntry, Is.Not.Null, $"Missing English Card_UI key '{pair.Key}'.");
            Assert.That(chineseEntry, Is.Not.Null, $"Missing Chinese Card_UI key '{pair.Key}'.");
            Assert.That(englishEntry.Value, Is.EqualTo(pair.Value), $"English fallback drifted for '{pair.Key}'.");
            Assert.That(chineseEntry.Value, Is.Not.Empty, $"Chinese Card_UI key '{pair.Key}' is empty.");
            Assert.That(chineseEntry.Value, Is.Not.EqualTo(englishEntry.Value), $"Chinese Card_UI key '{pair.Key}' was not translated.");
        }
    }

    [Test]
    public void CollectionView_ContainsVirtualizedBrowserAndDetailsElements()
    {
        VisualTreeAsset asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/CollectionView.uxml");
        Assert.That(asset, Is.Not.Null);

        TemplateContainer root = asset.CloneTree();
        Assert.That(root.Q<ListView>("set-list"), Is.Not.Null);
        Assert.That(root.Q<ListView>("card-list"), Is.Not.Null);
        Assert.That(root.Q<TextField>("card-search"), Is.Not.Null);
        Assert.That(root.Q<DropdownField>("rarity-filter"), Is.Not.Null);
        Assert.That(root.Q<Button>("owned-only-button"), Is.Not.Null);
        Assert.That(root.Q<Button>("new-only-button"), Is.Not.Null);
        Assert.That(root.Q<Label>("filter-empty"), Is.Not.Null);
        Assert.That(root.Q<VisualElement>("details-panel"), Is.Not.Null);
        Assert.That(root.Q<VisualElement>("detail-art-slot"), Is.Not.Null);
        Assert.That(root.Q<Label>("detail-progress"), Is.Not.Null);
        Assert.That(root.Q<Label>("detail-new-badge"), Is.Not.Null);
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
