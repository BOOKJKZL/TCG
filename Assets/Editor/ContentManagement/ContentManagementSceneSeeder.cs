using System;
using System.Linq;
using Gacha.Presentation;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.UIElements;

public static class ContentManagementSceneSeeder
{
    private const string ScenePath = "Assets/Scenes/006_ContentScene.unity";
    private const string MainMenuScenePath = "Assets/Scenes/002_MainMenuScene.unity";
    private const string ViewPath = "Assets/UI/ContentManagementView.uxml";
    private const string PanelSettingsPath = "Assets/Resources/UI/Collection Panel Settings.asset";

    [MenuItem("Tools/Universal Gacha/Configure Content Management Scene")]
    public static void Seed()
    {
        ConfigureContentScene();
        ConfigureMainMenu();
        ConfigureBuildScenes();
        AssetDatabase.SaveAssets();
        Debug.Log("Content management scene, menu entry, and build list are up to date.");
    }

    private static void ConfigureContentScene()
    {
        bool createScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null;
        Scene scene = createScene
            ? EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single)
            : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        if (createScene && !EditorSceneManager.SaveScene(scene, ScenePath))
            throw new InvalidOperationException("Content management scene could not be created.");

        VisualTreeAsset view = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(ViewPath);
        PanelSettings panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
        if (view == null || panelSettings == null)
            throw new InvalidOperationException("Content management UI assets are missing.");

        ContentManagementController controller = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<ContentManagementController>(true))
            .FirstOrDefault();
        if (controller == null)
        {
            var page = new GameObject("ContentManagement");
            controller = page.AddComponent<ContentManagementController>();
        }

        UIDocument document = controller.GetComponent<UIDocument>();
        if (document == null)
            document = controller.gameObject.AddComponent<UIDocument>();
        document.panelSettings = panelSettings;
        document.visualTreeAsset = view;
        document.sortingOrder = 20f;

        var serializedDocument = new SerializedObject(document);
        serializedDocument.Update();
        serializedDocument.FindProperty("m_PanelSettings").objectReferenceValue = panelSettings;
        serializedDocument.FindProperty("sourceAsset").objectReferenceValue = view;
        serializedDocument.FindProperty("m_SortingOrder").floatValue = 20f;
        serializedDocument.ApplyModifiedPropertiesWithoutUndo();

        var serializedController = new SerializedObject(controller);
        serializedController.Update();
        serializedController.FindProperty("uiDocument").objectReferenceValue = document;
        serializedController.FindProperty("viewAsset").objectReferenceValue = view;
        serializedController.ApplyModifiedPropertiesWithoutUndo();

        serializedDocument.Update();
        serializedController.Update();
        if (serializedDocument.FindProperty("sourceAsset").objectReferenceValue == null ||
            serializedController.FindProperty("viewAsset").objectReferenceValue == null)
        {
            throw new InvalidOperationException(
                $"Content management view references were not assigned. Asset path='{AssetDatabase.GetAssetPath(view)}'.");
        }

        EditorUtility.SetDirty(document);
        EditorUtility.SetDirty(controller);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
    }

    private static void ConfigureMainMenu()
    {
        Scene scene = EditorSceneManager.OpenScene(MainMenuScenePath, OpenSceneMode.Single);
        MainMenuController controller = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<MainMenuController>(true))
            .FirstOrDefault();
        if (controller == null)
            throw new InvalidOperationException("MainMenuController was not found in the main menu scene.");

        GameObject contentRoot = Find(scene, "ContentBtn");
        if (contentRoot == null)
        {
            GameObject template = Find(scene, "SettingBtn");
            if (template == null)
                throw new InvalidOperationException("SettingBtn could not be used as the content menu template.");
            contentRoot = UnityEngine.Object.Instantiate(template, template.transform.parent);
            contentRoot.name = "ContentBtn";
        }

        var rootTransform = (RectTransform)contentRoot.transform;
        rootTransform.localRotation = Quaternion.Euler(0f, 0f, -112.5f);
        rootTransform.localEulerAngles = new Vector3(0f, 0f, -112.5f);
        Transform buttonTransform = contentRoot.transform.Find("Btn");
        if (buttonTransform == null)
            throw new InvalidOperationException("ContentBtn has no Btn child.");
        var buttonRect = (RectTransform)buttonTransform;
        buttonRect.localRotation = Quaternion.Euler(0f, 0f, 112.5f);
        buttonRect.localEulerAngles = new Vector3(0f, 0f, 112.5f);

        UnityEngine.UI.Button button = buttonTransform.GetComponent<UnityEngine.UI.Button>();
        if (button == null)
            throw new InvalidOperationException("ContentBtn has no Unity UI Button.");
        while (button.onClick.GetPersistentEventCount() > 0)
            UnityEventTools.RemovePersistentListener(button.onClick, 0);
        UnityEventTools.AddPersistentListener(button.onClick, controller.ContentBtnClick);

        TMP_Text[] labels = buttonTransform.GetComponentsInChildren<TMP_Text>(true);
        TMP_Text label = labels.FirstOrDefault(item => item.gameObject.name == "Text") ?? labels.FirstOrDefault();
        if (label != null)
            label.text = "CONTENT";

        EditorUtility.SetDirty(contentRoot);
        EditorUtility.SetDirty(button);
        if (label != null)
            EditorUtility.SetDirty(label);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static void ConfigureBuildScenes()
    {
        EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
        if (scenes.Any(scene => string.Equals(scene.path, ScenePath, StringComparison.Ordinal)))
            return;
        EditorBuildSettings.scenes = scenes
            .Concat(new[] { new EditorBuildSettingsScene(ScenePath, true) })
            .ToArray();
    }

    private static GameObject Find(Scene scene, string name)
    {
        return scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Select(transform => transform.gameObject)
            .FirstOrDefault(gameObject => gameObject.name == name);
    }
}
