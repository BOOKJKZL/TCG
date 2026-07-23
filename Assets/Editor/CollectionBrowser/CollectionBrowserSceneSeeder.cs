using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public static class CollectionBrowserSceneSeeder
{
    private const string ScenePath = "Assets/Scenes/004_CollectionScene.unity";
    private const string ViewPath = "Assets/UI/CollectionView.uxml";
    private const string PanelSettingsPath = "Assets/Resources/UI/Collection Panel Settings.asset";

    [MenuItem("Tools/Universal Gacha/Configure Collection Browser Scene")]
    public static void Seed()
    {
        VisualTreeAsset view = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(ViewPath);
        PanelSettings panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
        if (view == null || panelSettings == null)
            throw new InvalidOperationException("Collection browser UI assets are missing.");

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        CollectionViewController controller = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<CollectionViewController>(true))
            .FirstOrDefault();
        if (controller == null)
            throw new InvalidOperationException("CollectionViewController was not found in the collection scene.");

        UIDocument document = controller.GetComponent<UIDocument>();
        if (document == null)
            document = controller.gameObject.AddComponent<UIDocument>();

        document.visualTreeAsset = view;
        document.sortingOrder = 20f;
        var serializedDocument = new SerializedObject(document);
        serializedDocument.Update();
        serializedDocument.FindProperty("sourceAsset").objectReferenceValue = view;
        serializedDocument.FindProperty("m_SortingOrder").floatValue = 20f;
        serializedDocument.ApplyModifiedProperties();

        var serializedController = new SerializedObject(controller);
        serializedController.FindProperty("uiDocument").objectReferenceValue = document;
        serializedController.FindProperty("viewAsset").objectReferenceValue = view;
        serializedController.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(document);
        EditorUtility.SetDirty(controller);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Collection browser scene configuration is up to date.");
    }
}
