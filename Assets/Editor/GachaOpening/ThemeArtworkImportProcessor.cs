using System;
using UnityEditor;
using UnityEngine;

internal sealed class ThemeArtworkImportProcessor : AssetPostprocessor
{
    internal const string ThemeArtworkRoot = "Assets/Resources/Gacha/Themes/";
    internal const int MaximumTextureSize = 512;

    private void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(ThemeArtworkRoot, StringComparison.Ordinal))
            return;

        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.alphaIsTransparency = false;
        importer.mipmapEnabled = false;
        importer.maxTextureSize = MaximumTextureSize;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;

        TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
        android.name = "Android";
        android.overridden = true;
        android.maxTextureSize = MaximumTextureSize;
        android.format = TextureImporterFormat.ASTC_6x6;
        android.compressionQuality = 75;
        importer.SetPlatformTextureSettings(android);
    }
}
