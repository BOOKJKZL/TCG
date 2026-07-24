using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace Gacha.EditorTools
{
    public static class UniversalUiFontBuilder
    {
        public const string SourceFontPath = "Assets/Fonts/UniversalUiChineseSubset.ttf";
        public const string FontAssetPath = "Assets/Fonts/Universal UI Chinese Fallback SDF.asset";

        [MenuItem("Tools/Gacha/Rebuild Universal UI Font Asset")]
        public static void Build()
        {
            AssetDatabase.ImportAsset(SourceFontPath, ImportAssetOptions.ForceSynchronousImport);
            Font sourceFont = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
            if (sourceFont == null)
                throw new InvalidOperationException($"UI subset font is missing at '{SourceFontPath}'.");

            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath) != null)
                AssetDatabase.DeleteAsset(FontAssetPath);

            TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(
                sourceFont,
                64,
                8,
                GlyphRenderMode.SDFAA,
                1024,
                1024,
                AtlasPopulationMode.Dynamic,
                true);
            if (fontAsset == null)
                throw new InvalidOperationException("TextMesh Pro could not create the UI fallback font asset.");

            fontAsset.name = "Universal UI Chinese Fallback SDF";
            fontAsset.atlasTextures[0].name = "Universal UI Chinese Fallback Atlas";
            fontAsset.material.name = "Universal UI Chinese Fallback Material";
            AssetDatabase.CreateAsset(fontAsset, FontAssetPath);
            AssetDatabase.AddObjectToAsset(fontAsset.atlasTextures[0], fontAsset);
            AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            EditorUtility.SetDirty(fontAsset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Generated TextMesh Pro fallback font at '{FontAssetPath}'.");
        }

        public static void BuildBatch()
        {
            try
            {
                Build();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                throw;
            }
        }
    }
}
