using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Platform.Android;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

public static class LanguageLocalizationSeeder
{
    private const string CollectionName = "Card_UI";
    private const string AppDisplayNameKey = "app.display_name";
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
            [AppDisplayNameKey] = "Universal Gacha Simulator",
            ["settings.language.title"] = "Language",
            ["settings.language.ui"] = "Interface language",
            ["settings.language.content"] = "Card content language",
            ["settings.language.fallback"] = "Requested {0}; using {1}.",
            ["settings.language.only_installed"] = "Only one card language is installed.",
            ["language.en"] = "English",
            ["language.zh"] = "Simplified Chinese",
            ["content.title"] = "Content Library",
            ["content.subtitle"] = "Download, pause, update, or repair individual content packs",
            ["content.action.back"] = "Main menu",
            ["content.action.refresh"] = "Refresh catalog",
            ["content.action.install"] = "Install",
            ["content.action.update"] = "Update",
            ["content.action.repair"] = "Repair",
            ["content.action.resume"] = "Resume",
            ["content.action.retry"] = "Retry",
            ["content.action.pause"] = "Pause",
            ["content.action.cancel"] = "Cancel",
            ["content.catalog.loading"] = "Checking available content...",
            ["content.catalog.loaded"] = "{0} content packs available.",
            ["content.catalog.empty"] = "No downloadable content is listed in this catalog.",
            ["content.catalog.unavailable"] = "The content catalog is unavailable: {0}",
            ["content.catalog.not_configured"] = "Remote content is not configured yet.",
            ["content.package.metadata"] = "Version {0} · {1}",
            ["content.status.ready"] = "Ready to install",
            ["content.status.checking"] = "Checking storage and installed version...",
            ["content.status.blocked"] = "Cannot start",
            ["content.status.insufficient_space"] = "Not enough storage",
            ["content.status.invalid_package"] = "Package metadata is invalid",
            ["content.status.storage_unavailable"] = "Storage is unavailable",
            ["content.status.downloading"] = "Downloading",
            ["content.status.paused"] = "Download paused",
            ["content.status.installing"] = "Verifying and installing...",
            ["content.status.installed"] = "Installed",
            ["content.status.current"] = "Already up to date",
            ["content.status.cancelled"] = "Cancelled",
            ["content.status.failed"] = "Operation failed",
            ["content.status.warning"] = "Installed with cleanup warning: {0}",
            ["content.progress"] = "{0}% · {1} / {2}"
        });

        Apply(chinese, new Dictionary<string, string>
        {
            [AppDisplayNameKey] = "万能抽卡模拟器",
            ["settings.language.title"] = "语言设置",
            ["settings.language.ui"] = "界面语言",
            ["settings.language.content"] = "卡牌内容语言",
            ["settings.language.fallback"] = "未安装 {0} 内容，当前使用 {1}。",
            ["settings.language.only_installed"] = "目前只安装了一种卡牌内容语言。",
            ["language.en"] = "英语",
            ["language.zh"] = "简体中文",
            ["content.title"] = "内容库",
            ["content.subtitle"] = "按内容包下载、暂停、更新或修复游戏资源",
            ["content.action.back"] = "主菜单",
            ["content.action.refresh"] = "刷新目录",
            ["content.action.install"] = "安装",
            ["content.action.update"] = "更新",
            ["content.action.repair"] = "修复",
            ["content.action.resume"] = "继续",
            ["content.action.retry"] = "重试",
            ["content.action.pause"] = "暂停",
            ["content.action.cancel"] = "取消",
            ["content.catalog.loading"] = "正在检查可用内容……",
            ["content.catalog.loaded"] = "可下载 {0} 个内容包。",
            ["content.catalog.empty"] = "此目录没有可下载内容。",
            ["content.catalog.unavailable"] = "内容目录暂不可用：{0}",
            ["content.catalog.not_configured"] = "尚未配置远程内容。",
            ["content.package.metadata"] = "版本 {0} · {1}",
            ["content.status.ready"] = "可以安装",
            ["content.status.checking"] = "正在检查储存空间与已安装版本……",
            ["content.status.blocked"] = "暂时无法开始",
            ["content.status.insufficient_space"] = "储存空间不足",
            ["content.status.invalid_package"] = "内容包资料无效",
            ["content.status.storage_unavailable"] = "无法读取储存空间",
            ["content.status.downloading"] = "下载中",
            ["content.status.paused"] = "下载已暂停",
            ["content.status.installing"] = "正在验证并安装……",
            ["content.status.installed"] = "已安装",
            ["content.status.current"] = "已是最新版本",
            ["content.status.cancelled"] = "已取消",
            ["content.status.failed"] = "操作失败",
            ["content.status.warning"] = "已安装，但清理下载文件失败：{0}",
            ["content.progress"] = "{0}% · {1} / {2}"
        });

        EditorUtility.SetDirty(collection.SharedData);
        EditorUtility.SetDirty(english);
        EditorUtility.SetDirty(chinese);
        EnsureAndroidAppInfo(collection);
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

    private static void EnsureAndroidAppInfo(StringTableCollection collection)
    {
        LocalizationSettings settings = LocalizationEditorSettings.ActiveLocalizationSettings;
        SharedTableData.SharedTableEntry appNameEntry = collection.SharedData.GetEntry(AppDisplayNameKey);
        if (settings == null || appNameEntry == null)
            throw new InvalidOperationException("Localization settings and the app display-name entry are required.");

        AppInfo appInfo = LocalizationSettings.Metadata.GetMetadata<AppInfo>();
        if (appInfo == null)
        {
            appInfo = new AppInfo();
            LocalizationSettings.Metadata.AddMetadata(appInfo);
        }

        appInfo.DisplayName = new LocalizedString(
            collection.SharedData.TableCollectionNameGuid,
            appNameEntry.Id);
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
