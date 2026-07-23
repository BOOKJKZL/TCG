using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public static class GachaOpeningSceneSeeder
{
    private const string ScenePath = "Assets/Scenes/003_GachaScene.unity";
    private const string ViewPath = "Assets/UI/GachaView.uxml";
    private const string PanelSettingsPath = "Assets/Resources/UI/Collection Panel Settings.asset";

    [MenuItem("Tools/Universal Gacha/Configure Pack Opening Scene")]
    public static void Seed()
    {
        VisualTreeAsset view = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(ViewPath);
        PanelSettings panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
        if (view == null || panelSettings == null)
            throw new InvalidOperationException("Pack opening UI assets are missing.");

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GachaViewController controller = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<GachaViewController>(true))
            .SingleOrDefault();
        if (controller == null)
            throw new InvalidOperationException("GachaViewController was not found in the gacha scene.");

        UIDocument document = controller.GetComponent<UIDocument>();
        if (document == null)
            document = controller.gameObject.AddComponent<UIDocument>();
        document.visualTreeAsset = view;
        document.sortingOrder = 20f;

        var serializedDocument = new SerializedObject(document);
        serializedDocument.Update();
        serializedDocument.FindProperty("sourceAsset").objectReferenceValue = view;
        serializedDocument.FindProperty("m_SortingOrder").floatValue = 20f;
        serializedDocument.ApplyModifiedPropertiesWithoutUndo();

        var serializedController = new SerializedObject(controller);
        serializedController.Update();
        serializedController.FindProperty("uiDocument").objectReferenceValue = document;
        serializedController.FindProperty("viewAsset").objectReferenceValue = view;
        serializedController.FindProperty("cardsPerPack").intValue = 5;
        serializedController.FindProperty("textureCacheCapacity").intValue = 24;
        serializedController.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(document);
        EditorUtility.SetDirty(controller);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Pack opening scene configuration is up to date.");
    }
}
