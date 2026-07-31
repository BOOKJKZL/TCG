using System.Linq;
using Gacha.Presentation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization.Platform.Android;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.UI;

public class LanguageSettingsPresentationTests
{
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
    }

    [Test]
    public void AndroidAppInfo_UsesLocalizedDisplayNameFromCardUiCollection()
    {
        StringTableCollection collection = LocalizationEditorSettings.GetStringTableCollection("Card_UI");
        StringTable english = collection.GetTable("en") as StringTable;
        StringTable chinese = collection.GetTable("zh") as StringTable;
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
            availableUiLanguageCount: 2), Is.True);
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
                "ConfirmImportButton"
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
}
