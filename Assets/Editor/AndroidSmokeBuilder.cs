using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gacha.EditorTools
{
    public static class AndroidSmokeBuilder
    {
        public const string OutputPath = "Builds/Android/UniversalGachaSimulator-smoke.apk";
        public const string EmulatorOutputPath = "Builds/Android/UniversalGachaSimulator-emulator-x86_64.apk";
        public const AndroidArchitecture SmokeArchitecture = AndroidArchitecture.ARM64;
        public const AndroidArchitecture EmulatorArchitecture = AndroidArchitecture.X86_64;
        public const BuildOptions SmokeBuildOptions =
            BuildOptions.Development |
            BuildOptions.CompressWithLz4 |
            BuildOptions.CleanBuildCache;
        public const BuildOptions EmulatorBuildOptions =
            BuildOptions.Development |
            BuildOptions.CompressWithLz4 |
            BuildOptions.CleanBuildCache;
        public const bool EmulatorUsesBuiltInRenderPipeline = true;

        [MenuItem("Tools/Gacha/Build Android Smoke APK")]
        public static void Build()
        {
            BuildForArchitecture(OutputPath, SmokeBuildOptions, false, SmokeArchitecture);
        }

        [MenuItem("Tools/Gacha/Build Android Emulator Acceptance APK")]
        public static void BuildEmulator()
        {
            BuildForArchitecture(
                EmulatorOutputPath,
                EmulatorBuildOptions,
                EmulatorUsesBuiltInRenderPipeline,
                EmulatorArchitecture);
        }

        private static void BuildForArchitecture(
            string outputPath,
            BuildOptions buildOptions,
            bool useBuiltInRenderPipeline,
            AndroidArchitecture architecture)
        {
            AndroidArchitecture originalArchitectures = PlayerSettings.Android.targetArchitectures;
            try
            {
                // Build artifacts declare their own ABI instead of trusting mutable
                // editor state left behind by an earlier emulator acceptance build.
                PlayerSettings.Android.targetArchitectures = architecture;
                BuildAtPath(outputPath, buildOptions, useBuiltInRenderPipeline);
            }
            finally
            {
                PlayerSettings.Android.targetArchitectures = originalArchitectures;
            }
        }

        private static void BuildAtPath(
            string outputPath,
            BuildOptions buildOptions,
            bool useBuiltInRenderPipeline)
        {
            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
            if (scenes.Length == 0)
                throw new InvalidOperationException("The Android smoke build requires at least one enabled scene.");

            string outputDirectory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(outputDirectory))
                Directory.CreateDirectory(outputDirectory);

            AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult addressables);
            if (addressables == null || !string.IsNullOrWhiteSpace(addressables.Error))
            {
                string error = addressables?.Error ?? "Addressables returned no build result.";
                throw new InvalidOperationException("Addressables player content build failed: " + error);
            }

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.Android,
                options = buildOptions
            };
            RenderPipelineAsset originalDefaultPipeline = null;
            RenderPipelineAsset originalQualityPipeline = null;
            int originalQualityLevel = -1;
            if (useBuiltInRenderPipeline)
            {
                // SwiftShader on the Android emulator cannot reliably construct the
                // project's URP runtime settings. Keep production builds on URP while
                // using the built-in pipeline for software/UI acceptance only.
                originalDefaultPipeline = GraphicsSettings.defaultRenderPipeline;
                originalQualityLevel = QualitySettings.GetQualityLevel();
                QualitySettings.SetQualityLevel(0, false);
                originalQualityPipeline = QualitySettings.renderPipeline;
                GraphicsSettings.defaultRenderPipeline = null;
                QualitySettings.renderPipeline = null;
            }

            BuildReport report;
            try
            {
                report = BuildPipeline.BuildPlayer(options);
            }
            finally
            {
                if (useBuiltInRenderPipeline)
                {
                    QualitySettings.renderPipeline = originalQualityPipeline;
                    GraphicsSettings.defaultRenderPipeline = originalDefaultPipeline;
                    QualitySettings.SetQualityLevel(originalQualityLevel, false);
                }
            }
            BuildSummary summary = report.summary;
            Debug.Log(
                $"Android smoke build result={summary.result} scenes={scenes.Length} " +
                $"bytes={summary.totalSize} duration={summary.totalTime} output='{summary.outputPath}'.");
            if (summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException(
                    $"Android smoke build failed with {summary.totalErrors} errors and {summary.totalWarnings} warnings.");
        }

        public static void BuildBatch()
        {
            Build();
        }

        public static void BuildEmulatorBatch()
        {
            BuildEmulator();
        }
    }
}
