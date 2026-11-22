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
        public static void Build() => Build(SourceFontPath, FontAssetPath);

        internal static void Build(string sourcePath, string destination)
        {
            AssetDatabase.ImportAsset(sourcePath, ImportAssetOptions.ForceSynchronousImport);
            Font sourceFont = AssetDatabase.LoadAssetAtPath<Font>(sourcePath);
            if (sourceFont == null)
                throw new InvalidOperationException($"UI subset font is missing at '{sourcePath}'.");

            var mainAsset = AssetDatabase.LoadMainAssetAtPath(destination);
            var existing = mainAsset as TMP_FontAsset;
            if (mainAsset != null && existing == null)
                throw new InvalidOperationException($"Expected a TextMesh Pro font at '{destination}'.");

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
            if (existing == null)
            {
                AssetDatabase.CreateAsset(fontAsset, destination);
                try
                {
                    AssetDatabase.AddObjectToAsset(fontAsset.atlasTextures[0], fontAsset);
                    AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
                    EditorUtility.SetDirty(fontAsset);
                    AssetDatabase.SaveAssetIfDirty(fontAsset);
                }
                catch
                {
                    AssetDatabase.DeleteAsset(destination);
                    throw;
                }
            }
            else
            {
                try
                {
                    UpdateExisting(existing, fontAsset, destination);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(fontAsset);
                }
            }
            Debug.Log($"Generated TextMesh Pro fallback font at '{destination}'.");
        }

        private static void UpdateExisting(TMP_FontAsset target, TMP_FontAsset generated, string path)
        {
            Texture2D atlas = target.atlasTextures?.Length > 0 ? target.atlasTextures[0] : null;
            Material material = target.material;
            if (atlas == null || material == null || AssetDatabase.GetAssetPath(atlas) != path ||
                AssetDatabase.GetAssetPath(material) != path)
                throw new InvalidOperationException($"Font atlas or material is missing from '{path}'.");

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.RegisterCompleteObjectUndo(new UnityEngine.Object[] { target, atlas, material }, "Rebuild UI font");
            try
            {
                EditorUtility.CopySerialized(generated.atlasTextures[0], atlas);
                EditorUtility.CopySerialized(generated.material, material);
                material.mainTexture = atlas;
                EditorUtility.CopySerialized(generated, target);
                target.atlasTextures = new[] { atlas };
                target.material = material;
                target.ReadFontAssetDefinition();
                EditorUtility.SetDirty(atlas);
                EditorUtility.SetDirty(material);
                EditorUtility.SetDirty(target);
                AssetDatabase.SaveAssetIfDirty(target);
            }
            catch
            {
                Undo.RevertAllDownToGroup(group);
                throw;
            }
            Undo.CollapseUndoOperations(group);
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
