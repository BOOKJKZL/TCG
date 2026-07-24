using System.Linq;
using Gacha.Presentation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
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
        Assert.That(english.GetEntry("settings.experience.reduce_motion").Value, Is.EqualTo("Reduce motion"));
        Assert.That(english.GetEntry("settings.experience.animation_speed").Value, Is.EqualTo("Animation speed"));
        Assert.That(chinese.GetEntry("settings.experience.reduce_motion").Value, Is.EqualTo("减少动态效果"));
        Assert.That(chinese.GetEntry("settings.experience.animation_speed").Value, Is.EqualTo("动画速度"));
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
}
