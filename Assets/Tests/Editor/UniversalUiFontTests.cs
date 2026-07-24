using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gacha.EditorTools;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine.Localization.Tables;

public class UniversalUiFontTests
{
    [Test]
    public void ChineseFallback_CoversEveryLocalizedUiCharacterWithinSizeBudget()
    {
        TMP_FontAsset fallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            UniversalUiFontBuilder.FontAssetPath);
        Assert.That(fallback, Is.Not.Null);
        Assert.That(TMP_Settings.fallbackFontAssets, Does.Contain(fallback));

        StringTableCollection collection = LocalizationEditorSettings.GetStringTableCollection("Card_UI");
        StringTable chinese = collection.GetTable("zh") as StringTable;
        Assert.That(chinese, Is.Not.Null);
        char[] required = chinese.Values
            .SelectMany(entry => entry.Value ?? string.Empty)
            .Where(character => character >= 0x2E80)
            .Distinct()
            .ToArray();
        var missing = new List<char>();
        foreach (char character in required)
        {
            if (!fallback.HasCharacter(character, false, true))
                missing.Add(character);
        }

        Assert.That(missing, Is.Empty, $"Missing UI glyphs: {string.Concat(missing)}");
        long sourceBytes = new FileInfo(UniversalUiFontBuilder.SourceFontPath).Length;
        Assert.That(sourceBytes, Is.LessThan(100 * 1024),
            $"The CJK UI subset grew to {sourceBytes / 1024f:0.0} KiB.");
    }
}
