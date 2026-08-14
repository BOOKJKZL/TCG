using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Gacha.Presentation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization.Platform.Android;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public class LanguageSettingsPresentationTests
{
    [Test]
    public void RecoveryPicker_AbandonedNativeRequestCannotConsumeTheNextPageCallback()
    {
        var host = new GameObject("Recovery Picker Contract Host");
        var picker = host.AddComponent<RecoveryDocumentPicker>();
        MethodInfo begin = typeof(RecoveryDocumentPicker).GetMethod(
            "Begin",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(begin, Is.Not.Null);
        var busyTransitions = new List<bool>();
        picker.BusyChanged += busyTransitions.Add;
        int firstCallbacks = 0;
        int secondCallbacks = 0;

        try
        {
            string first = (string)begin.Invoke(
                picker,
                new object[] { new Action<RecoveryDocumentPickerResult>(_ => firstCallbacks++) });
            Assert.That(picker.IsBusy, Is.True);
            picker.CancelPending();
            Assert.That(picker.IsBusy, Is.True,
                "Abandoning a page callback must retain the native picker tombstone.");
            TargetInvocationException blocked = Assert.Throws<TargetInvocationException>(() => begin.Invoke(
                picker,
                new object[] { new Action<RecoveryDocumentPickerResult>(_ => secondCallbacks++) }));
            Assert.That(blocked.InnerException, Is.TypeOf<InvalidOperationException>());

            picker.OnDocumentPickerResult(
                "{\"requestId\":\"wrong-request\",\"succeeded\":true,\"path\":\"C:/private/old.gachasave\"}");
            Assert.That(picker.IsBusy, Is.True);
            picker.OnDocumentPickerResult(
                "{\"requestId\":\"" + first + "\",\"succeeded\":false,\"error\":\"cancelled\"}");
            Assert.That(picker.IsBusy, Is.False);
            Assert.That(firstCallbacks, Is.Zero);

            string second = (string)begin.Invoke(
                picker,
                new object[] { new Action<RecoveryDocumentPickerResult>(_ => secondCallbacks++) });
            picker.OnDocumentPickerResult(
                "{\"requestId\":\"" + second + "\",\"succeeded\":true,\"path\":\"content://safe\"}");
            picker.OnDocumentPickerResult(
                "{\"requestId\":\"" + second + "\",\"succeeded\":true,\"path\":\"content://duplicate\"}");
            Assert.That(secondCallbacks, Is.EqualTo(1));
            Assert.That(picker.IsBusy, Is.False);
            Assert.That(busyTransitions, Is.EqualTo(new[] { true, false, true, false }));

            string java = File.ReadAllText("Assets/Plugins/Android/RecoveryDocumentBridge.java");
            Assert.That(java, Does.Contain("requestId"));
            Assert.That(java, Does.Contain("onSaveInstanceState"));
            Assert.That(java, Does.Contain("getBoolean(\"launched\""));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(host);
        }
    }

    [Test]
    public void MobileSettingsView_UsesScrollableStableUiToolkitContract()
    {
        const string uxmlPath = "Assets/Resources/UI/SettingsView.uxml";
        const string ussPath = "Assets/Resources/UI/SettingsView.uss";
        string uxml = File.ReadAllText(uxmlPath);
        string uss = File.ReadAllText(ussPath);

        Assert.That(AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.VisualTreeAsset>(uxmlPath), Is.Not.Null);
        Assert.That(AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.StyleSheet>(ussPath), Is.Not.Null);
        Assert.That(uxml, Does.Contain("<ui:ScrollView"));
        Assert.That(uxml, Does.Not.Contain("<ui:Button"));
        Assert.That(uxml, Does.Not.Contain(" style="));
        Assert.That(uxml, Does.Contain("settings-ui-language-slot"));
        Assert.That(uxml, Does.Contain("settings-confirm-import-slot"));
        Assert.That(uxml, Does.Contain("settings-cloud-merge-slot"));
        foreach (string unsupported in new[]
                 {
                     "gap:", "z-index:", "box-shadow:", "filter:", "outline:", "gradient("
                 })
        {
            Assert.That(uss, Does.Not.Contain(unsupported), unsupported);
        }
    }

    [Test]
    public void MobileSettingsLocalization_IsCompleteAndUsesSafeStatusMessages()
    {
        StringTableCollection collection = LocalizationEditorSettings.GetStringTableCollection("Card_UI");
        string[] keys =
        {
            "settings.subtitle", "settings.language.description", "settings.language.status.ready",
            "settings.experience.description", "settings.experience.save_failed_safe",
            "settings.download.title", "settings.download.description", "settings.download.status.failed",
            "settings.recovery.status.exported_safe", "settings.recovery.status.error_safe",
            "settings.recovery.status.imported_safe", "settings.recovery.confirm.title",
            "settings.recovery.confirm.body", "settings.account.title", "settings.account.description",
            "settings.cloud.status.resolved_safe", "settings.cloud.status.failed_safe",
            "settings.cloud.status.backup_failed_safe", "settings.cloud.confirm.title",
            "settings.cloud.confirm.body", "settings.identity.status.cloud_pending_safe",
            "settings.identity.status.failed_safe"
        };
        foreach (string locale in new[] { "en", "zh", "ja" })
        {
            StringTable table = collection.GetTable(locale) as StringTable;
            Assert.That(table, Is.Not.Null, locale);
            foreach (string key in keys)
            {
                StringTableEntry entry = table.GetEntry(key);
                Assert.That(entry, Is.Not.Null, locale + ":" + key);
                Assert.That(entry.Value, Is.Not.Empty, locale + ":" + key);
            }
        }

        foreach (string key in new[]
                 {
                     "settings.recovery.status.exported_safe", "settings.recovery.status.error_safe",
                     "settings.recovery.status.imported_safe", "settings.cloud.status.resolved_safe",
                     "settings.cloud.status.failed_safe", "settings.identity.status.failed_safe",
                     "settings.recovery.status.exported", "settings.recovery.status.error",
                     "settings.recovery.status.imported", "settings.cloud.status.resolved",
                     "settings.cloud.status.failed", "settings.cloud.status.backup_failed",
                     "settings.identity.status.cloud_pending", "settings.identity.status.failed"
                 })
        {
            Assert.That(CardUiText.EnglishFallbacks[key], Does.Not.Contain("{0}"), key);
        }
    }

    [Test]
    public void Project_UsesARealLocalizationSettingsAsset()
    {
        LocalizationSettings settings = LocalizationEditorSettings.ActiveLocalizationSettings;

        Assert.That(settings, Is.Not.Null);
        Assert.That(
            AssetDatabase.GetAssetPath(settings),
            Is.EqualTo("Assets/Resources/Data/Localization/Localization Settings.asset"));
        Assert.That(settings.GetAvailableLocales(), Is.Not.Null);
        Assert.That(settings.GetStringDatabase(), Is.Not.Null);
    }

    [Test]
    public void LanguageTables_ContainUiAndContentLanguageEntries()
    {
        StringTableCollection collection = LocalizationEditorSettings.GetStringTableCollection("Card_UI");
        StringTable english = collection.GetTable("en") as StringTable;
        StringTable chinese = collection.GetTable("zh") as StringTable;
        StringTable japanese = collection.GetTable("ja") as StringTable;

        Assert.That(english.GetEntry("settings.language.ui").Value, Is.EqualTo("Interface language"));
        Assert.That(english.GetEntry("settings.language.content").Value, Is.EqualTo("Card content language"));
        Assert.That(chinese.GetEntry("settings.language.ui").Value, Is.EqualTo("界面语言"));
        Assert.That(chinese.GetEntry("settings.language.content").Value, Is.EqualTo("卡牌内容语言"));
        Assert.That(english.GetEntry("language.ja").Value, Is.EqualTo("Japanese"));
        Assert.That(english.GetEntry("gacha.status.no_products").Value,
            Is.EqualTo("No products are installed for this content language."));
        Assert.That(chinese.GetEntry("language.ja").Value, Is.EqualTo("日语"));
        Assert.That(english.GetEntry("settings.experience.reduce_motion").Value, Is.EqualTo("Reduce motion"));
        Assert.That(english.GetEntry("settings.experience.animation_speed").Value, Is.EqualTo("Animation speed"));
        Assert.That(chinese.GetEntry("settings.experience.reduce_motion").Value, Is.EqualTo("减少动态效果"));
        Assert.That(chinese.GetEntry("settings.experience.animation_speed").Value, Is.EqualTo("动画速度"));
        Assert.That(japanese, Is.Not.Null);
        Assert.That(japanese.GetEntry("settings.language.ui").Value, Is.EqualTo("インターフェース言語"));
        Assert.That(japanese.GetEntry("settings.language.content").Value, Is.EqualTo("カードコンテンツ言語"));
        Assert.That(japanese.GetEntry("language.ja").Value, Is.EqualTo("日本語"));
        Assert.That(japanese.GetEntry("settings.experience.reduce_motion").Value, Is.EqualTo("モーションを減らす"));
    }

    [Test]
    public void JapaneseTable_CoversEverySharedKeyAndPreservesFormatArguments()
    {
        StringTableCollection collection = LocalizationEditorSettings.GetStringTableCollection("Card_UI");
        StringTable english = collection.GetTable("en") as StringTable;
        StringTable japanese = collection.GetTable("ja") as StringTable;

        Assert.That(japanese, Is.Not.Null);
        Assert.That(japanese.Values.Count, Is.EqualTo(collection.SharedData.Entries.Count));
        foreach (SharedTableData.SharedTableEntry sharedEntry in collection.SharedData.Entries)
        {
            StringTableEntry englishEntry = english.GetEntry(sharedEntry.Id);
            StringTableEntry japaneseEntry = japanese.GetEntry(sharedEntry.Id);
            Assert.That(japaneseEntry, Is.Not.Null, $"Missing Japanese translation: {sharedEntry.Key}");
            Assert.That(japaneseEntry.Value, Is.Not.Empty, $"Blank Japanese translation: {sharedEntry.Key}");
            Assert.That(
                FormatArguments(japaneseEntry.Value),
                Is.EquivalentTo(FormatArguments(englishEntry.Value)),
                $"Format arguments differ for: {sharedEntry.Key}");
        }
    }

    [Test]
    public void AndroidAppInfo_UsesLocalizedDisplayNameFromCardUiCollection()
    {
        StringTableCollection collection = LocalizationEditorSettings.GetStringTableCollection("Card_UI");
        StringTable english = collection.GetTable("en") as StringTable;
        StringTable chinese = collection.GetTable("zh") as StringTable;
        StringTable japanese = collection.GetTable("ja") as StringTable;
        SharedTableData.SharedTableEntry appNameEntry = collection.SharedData.GetEntry("app.display_name");
        AppInfo appInfo = LocalizationSettings.Metadata.GetMetadata<AppInfo>();

        Assert.That(appInfo, Is.Not.Null);
        Assert.That(appInfo.DisplayName.TableReference.ReferenceType, Is.EqualTo(TableReference.Type.Guid));
        Assert.That(
            appInfo.DisplayName.TableReference.TableCollectionNameGuid,
            Is.EqualTo(collection.SharedData.TableCollectionNameGuid));
        Assert.That(appInfo.DisplayName.TableEntryReference.ReferenceType, Is.EqualTo(TableEntryReference.Type.Id));
        Assert.That(appInfo.DisplayName.TableEntryReference.KeyId, Is.EqualTo(appNameEntry.Id));
        Assert.That(english.GetEntry(appNameEntry.Id).Value, Is.EqualTo("Universal Gacha Simulator"));
        Assert.That(chinese.GetEntry(appNameEntry.Id).Value, Is.EqualTo("万能抽卡模拟器"));
        Assert.That(japanese.GetEntry(appNameEntry.Id).Value, Is.EqualTo("万能パック開封シミュレーター"));
    }

    [Test]
    public void LanguagePanel_CreatesSeparateFeedbackEnabledSelectors()
    {
        GameObject canvasObject = new GameObject("Test Canvas", typeof(RectTransform), typeof(Canvas));
        canvasObject.SetActive(false);

        try
        {
            LanguageSettingsPanel panel = LanguageSettingsPanel.Create(canvasObject.transform);
            Button[] buttons = panel.GetComponentsInChildren<Button>(true);

            Assert.That(buttons.Length, Is.EqualTo(2));
            Assert.That(buttons.All(button => button.GetComponent<GameFeedbackButton>() != null), Is.True);
            Assert.That(panel.GetComponent<CanvasGroup>(), Is.Not.Null);
        }
        finally
        {
            Object.DestroyImmediate(canvasObject);
        }
    }

    [Test]
    public void LanguagePanel_KeepsUiLanguageIndependentFromCardCatalogAvailability()
    {
        Assert.That(LanguageSettingsPanel.CanSelectUiLanguage(
            servicesAvailable: true,
            availableUiLanguageCount: 3), Is.True);
        Assert.That(LanguageSettingsPanel.CanSelectContentLanguage(
            servicesAvailable: true,
            catalogReady: false,
            availableContentLanguageCount: 0), Is.False);
    }

    [Test]
    public void ExperiencePanel_CreatesFourFeedbackEnabledControls()
    {
        GameObject canvasObject = new GameObject("Test Canvas", typeof(RectTransform), typeof(Canvas));
        canvasObject.SetActive(false);

        try
        {
            ExperienceSettingsPanel panel = ExperienceSettingsPanel.Create(canvasObject.transform);
            Button[] buttons = panel.GetComponentsInChildren<Button>(true);

            Assert.That(buttons.Select(button => button.name), Is.EquivalentTo(new[]
            {
                "SoundButton",
                "ReduceMotionButton",
                "HapticsButton",
                "AnimationSpeedButton"
            }));
            Assert.That(buttons.All(button => button.GetComponent<GameFeedbackButton>() != null), Is.True);
            Assert.That(panel.GetComponent<CanvasGroup>(), Is.Not.Null);
        }
        finally
        {
            Object.DestroyImmediate(canvasObject);
        }
    }

    [Test]
    public void RecoveryPanel_CreatesThreeFeedbackControlsAndFitsReferenceCanvas()
    {
        GameObject canvasObject = new GameObject("Test Canvas", typeof(RectTransform), typeof(Canvas));
        canvasObject.SetActive(false);

        try
        {
            LanguageSettingsPanel language = LanguageSettingsPanel.Create(canvasObject.transform);
            ExperienceSettingsPanel experience = ExperienceSettingsPanel.Create(canvasObject.transform);
            SaveRecoverySettingsPanel recovery = SaveRecoverySettingsPanel.Create(canvasObject.transform);
            Button[] buttons = recovery.GetComponentsInChildren<Button>(true);

            Assert.That(buttons.Select(button => button.name), Is.EquivalentTo(new[]
            {
                "ExportSaveButton",
                "ChooseImportButton",
                "ConfirmImportButton",
                "CloudConflictButton"
            }));
            Assert.That(buttons.All(button => button.GetComponent<GameFeedbackButton>() != null), Is.True);
            Assert.That(recovery.GetComponent<CanvasGroup>(), Is.Not.Null);

            RectTransform languageRect = language.GetComponent<RectTransform>();
            RectTransform experienceRect = experience.GetComponent<RectTransform>();
            RectTransform recoveryRect = recovery.GetComponent<RectTransform>();
            float languageTop = languageRect.anchoredPosition.y + languageRect.rect.height * 0.5f;
            float experienceBottom = experienceRect.anchoredPosition.y - experienceRect.rect.height * 0.5f;
            float recoveryTop = recoveryRect.anchoredPosition.y + recoveryRect.rect.height * 0.5f;
            float recoveryBottom = recoveryRect.anchoredPosition.y - recoveryRect.rect.height * 0.5f;

            Assert.That(languageTop, Is.LessThanOrEqualTo(1000f));
            Assert.That(recoveryTop, Is.LessThan(experienceBottom), "Recovery and experience panels must not overlap.");
            Assert.That(recoveryBottom, Is.GreaterThanOrEqualTo(-1000f),
                "Recovery controls must remain inside the 1000x2000 reference canvas.");
        }
        finally
        {
            Object.DestroyImmediate(canvasObject);
        }
    }

    [Test]
    public void CloudConflictDialog_CreatesSafeExplicitChoices()
    {
        GameObject canvasObject = new GameObject("Test Canvas", typeof(RectTransform), typeof(Canvas));
        canvasObject.SetActive(false);
        GameCloudConflictSession.Current.Reset();

        try
        {
            InventoryData local = new InventoryData { LastModifiedUtcTicks = 20 };
            local.Cards["local-card"] = 2;
            InventoryData cloud = new InventoryData { LastModifiedUtcTicks = 10 };
            cloud.Cards["cloud-card"] = 3;
            GameCloudConflictSession.Current.Prepare(local, cloud, true);

            CloudConflictSettingsDialog dialog = CloudConflictSettingsDialog.Create(canvasObject.transform);
            Button[] buttons = dialog.GetComponentsInChildren<Button>(true);

            Assert.That(buttons.Select(button => button.name), Is.EquivalentTo(new[]
            {
                "KeepLocalButton",
                "UseCloudButton",
                "SafeMergeButton",
                "ConnectIdentityButton",
                "CloseConflictButton"
            }));
            Assert.That(buttons.All(button => button.GetComponent<GameFeedbackButton>() != null), Is.True);
            Assert.That(buttons.Where(button => button.name != "CloseConflictButton")
                .Where(button => button.name != "ConnectIdentityButton")
                .All(button => button.interactable), Is.True);
            Assert.That(buttons.Single(button => button.name == "ConnectIdentityButton").interactable, Is.False,
                "The identity action must stay disabled until the external Player Accounts client id is configured.");
            Assert.That(dialog.GetComponent<CanvasGroup>().blocksRaycasts, Is.False);

            dialog.Open();

            Assert.That(dialog.IsOpen, Is.True);
            Assert.That(dialog.GetComponent<CanvasGroup>().blocksRaycasts, Is.True);
            Assert.That(dialog.GetComponentsInChildren<TMPro.TMP_Text>(true)
                .Single(text => text.name == "LocalSaveSummary").text, Does.Contain("2"));
            Assert.That(dialog.GetComponentsInChildren<TMPro.TMP_Text>(true)
                .Single(text => text.name == "CloudSaveSummary").text, Does.Contain("3"));
            Assert.That(dialog.GetComponentsInChildren<TMPro.TMP_Text>(true)
                .Single(text => text.name == "PlayerIdentityStatus").text, Does.Contain("PLAYER ID"));
        }
        finally
        {
            GameCloudConflictSession.Current.Reset();
            Object.DestroyImmediate(canvasObject);
        }
    }

    private static IReadOnlyCollection<string> FormatArguments(string value) =>
        Regex.Matches(value ?? string.Empty, @"\{\d+(?::[^}]*)?\}")
            .Cast<Match>()
            .Select(match => match.Value)
            .ToArray();
}
