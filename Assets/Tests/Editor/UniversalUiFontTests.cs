using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gacha.EditorTools;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.Localization.Tables;

public class UniversalUiFontTests
{
    [Test]
    public void Rebuild_ClonedFallbackRetainsAssetAndSubassetReferences()
    {
        string directoryName = "FontFixture-" + System.Guid.NewGuid().ToString("N");
        string directory = "Assets/Tests/Editor/" + directoryName;
        string target = directory + "/fallback.asset";
        AssetDatabase.CreateFolder("Assets/Tests/Editor", directoryName);
        try
        {
            Assert.That(AssetDatabase.CopyAsset(UniversalUiFontBuilder.FontAssetPath, target), Is.True);
            string[] expected = AssetIds(AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(target));
            for (int iteration = 0; iteration < 2; iteration++)
            {
                UniversalUiFontBuilder.Build(UniversalUiFontBuilder.SourceFontPath, target);
                AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceSynchronousImport);
                var rebuilt = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(target);
                Assert.That(AssetIds(rebuilt), Is.EqualTo(expected));
                Assert.That(rebuilt.material.mainTexture, Is.EqualTo(rebuilt.atlasTextures[0]));
            }
            byte[] before = File.ReadAllBytes(target);
            Assert.Throws<System.InvalidOperationException>(() => UniversalUiFontBuilder.Build(target, target));
            Assert.That(File.ReadAllBytes(target), Is.EqualTo(before));
            var fallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(target);
            Assert.That(AssetIds(fallback), Is.EqualTo(expected));
            var collection = LocalizationEditorSettings.GetStringTableCollection("Card_UI");
            uint[] sample = new[] { "zh", "ja" }.Select(locale => (uint)((StringTable)collection.GetTable(locale))
                .Values.SelectMany(entry => entry.Value ?? string.Empty).First(c => c >= 0x2e80)).ToArray();
            Assert.That(fallback.TryAddCharacters(sample, out uint[] _, false), Is.True);
        }
        finally
        {
            AssetDatabase.DeleteAsset(directory);
        }
    }

    private static string[] AssetIds(TMP_FontAsset font)
    {
        return new UnityEngine.Object[] { font, font.atlasTextures[0], font.material }.Select(asset =>
        {
            Assert.That(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long fileId), Is.True);
            return guid + "/" + fileId;
        }).ToArray();
    }

    [Test]
    public void CjkFallback_CoversEveryLocalizedUiCharacterWithinSizeBudget()
    {
        TMP_FontAsset fallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            UniversalUiFontBuilder.FontAssetPath);
        Assert.That(fallback, Is.Not.Null);
        Assert.That(TMP_Settings.fallbackFontAssets, Does.Contain(fallback));

        StringTableCollection collection = LocalizationEditorSettings.GetStringTableCollection("Card_UI");
        StringTable chinese = collection.GetTable("zh") as StringTable;
        Assert.That(chinese, Is.Not.Null);
        Assert.That(collection.GetTable("ja") as StringTable, Is.Not.Null);
        Assert.That(collection.GetTable("en") as StringTable, Is.Not.Null);
        var required = new HashSet<char>(collection.StringTables
            .SelectMany(table => table.Values)
            .SelectMany(entry => entry.Value ?? string.Empty)
            .Where(character => character >= 0x2E80)
            .Distinct());
        foreach (string guid in AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets/Scripts" }))
        {
            MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));
            if (script == null)
                continue;
            foreach (char character in script.text)
                if (character >= 0x2E80)
                    required.Add(character);
        }
        Assert.That(fallback.sourceFontFile, Is.Not.Null);
        TMP_FontAsset probe = TMP_FontAsset.CreateFontAsset(
            fallback.sourceFontFile,
            64,
            8,
            GlyphRenderMode.SDFAA,
            1024,
            1024,
            AtlasPopulationMode.Dynamic,
            true);
        Assert.That(probe, Is.Not.Null);
        try
        {
            uint[] requiredCodepoints = required.Select(character => (uint)character).ToArray();
            bool allAdded = probe.TryAddCharacters(
                requiredCodepoints,
                out uint[] missingCodepoints,
                false);
            string missingText = string.Concat((missingCodepoints ?? new uint[0])
                .Select(codepoint => char.ConvertFromUtf32((int)codepoint)));
            Assert.That(allAdded, Is.True,
                $"Missing UI glyphs: {missingText}");
        }
        finally
        {
            if (probe.material != null)
                UnityEngine.Object.DestroyImmediate(probe.material);
            foreach (UnityEngine.Texture2D atlas in probe.atlasTextures)
                if (atlas != null)
                    UnityEngine.Object.DestroyImmediate(atlas);
            UnityEngine.Object.DestroyImmediate(probe);
        }
        long sourceBytes = new FileInfo(UniversalUiFontBuilder.SourceFontPath).Length;
        Assert.That(sourceBytes, Is.LessThan(256 * 1024),
            $"The CJK UI subset grew to {sourceBytes / 1024f:0.0} KiB.");
    }
}
