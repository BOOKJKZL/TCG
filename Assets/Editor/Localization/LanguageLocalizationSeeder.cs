using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

public static class LanguageLocalizationSeeder
{
    private const string CollectionName = "Card_UI";
    private const string SettingsAssetPath = "Assets/Resources/Data/Localization/Localization Settings.asset";

    [MenuItem("Tools/Universal Gacha/Seed Language Configuration")]
    public static void Seed()
    {
        EnsureLocalizationSettings();

        StringTableCollection collection = LocalizationEditorSettings.GetStringTableCollection(CollectionName);
        if (collection == null)
            throw new InvalidOperationException($"Localization collection '{CollectionName}' was not found.");

        StringTable english = collection.GetTable("en") as StringTable;
        StringTable chinese = collection.GetTable("zh") as StringTable;
        if (english == null || chinese == null)
            throw new InvalidOperationException("English and Simplified Chinese string tables are required.");

        Apply(english, new Dictionary<string, string>
        {
            ["settings.language.title"] = "Language",
            ["settings.language.ui"] = "Interface language",
            ["settings.language.content"] = "Card content language",
            ["settings.language.fallback"] = "Requested {0}; using {1}.",
            ["settings.language.only_installed"] = "Only one card language is installed.",
            ["language.en"] = "English",
            ["language.zh"] = "Simplified Chinese"
        });

        Apply(chinese, new Dictionary<string, string>
        {
            ["settings.language.title"] = "语言设置",
            ["settings.language.ui"] = "界面语言",
            ["settings.language.content"] = "卡牌内容语言",
            ["settings.language.fallback"] = "未安装 {0} 内容，当前使用 {1}。",
            ["settings.language.only_installed"] = "目前只安装了一种卡牌内容语言。",
            ["language.en"] = "英语",
            ["language.zh"] = "简体中文"
        });

        EditorUtility.SetDirty(collection.SharedData);
        EditorUtility.SetDirty(english);
        EditorUtility.SetDirty(chinese);
        AssetDatabase.SaveAssets();
        Debug.Log("Language settings and localized entries are up to date.");
    }

    private static void EnsureLocalizationSettings()
    {
        LocalizationSettings settings = AssetDatabase.LoadAssetAtPath<LocalizationSettings>(SettingsAssetPath);
        if (settings == null)
        {
            settings = ScriptableObject.CreateInstance<LocalizationSettings>();
            settings.name = "Universal Gacha Localization Settings";
            AssetDatabase.CreateAsset(settings, SettingsAssetPath);
        }

        if (LocalizationEditorSettings.ActiveLocalizationSettings != settings)
            LocalizationEditorSettings.ActiveLocalizationSettings = settings;

        EditorUtility.SetDirty(settings);
    }

    private static void Apply(StringTable table, IReadOnlyDictionary<string, string> values)
    {
        foreach (KeyValuePair<string, string> pair in values)
        {
            StringTableEntry entry = table.GetEntry(pair.Key) ?? table.AddEntry(pair.Key, pair.Value);
            entry.Value = pair.Value;
        }
    }
}
