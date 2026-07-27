using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Gacha.EditorTools
{
    public static class AndroidSmokeBuilder
    {
        public const string OutputPath = "Builds/Android/UniversalGachaSimulator-smoke.apk";
        public const string EmulatorOutputPath = "Builds/Android/UniversalGachaSimulator-emulator-x86_64.apk";
        public const BuildOptions SmokeBuildOptions =
            BuildOptions.Development |
            BuildOptions.CompressWithLz4 |
            BuildOptions.CleanBuildCache;
        public const BuildOptions EmulatorBuildOptions =
            BuildOptions.Development |
            BuildOptions.CompressWithLz4;

        [MenuItem("Tools/Gacha/Build Android Smoke APK")]
        public static void Build()
        {
            BuildAtPath(OutputPath, SmokeBuildOptions);
        }

        [MenuItem("Tools/Gacha/Build Android Emulator Acceptance APK")]
        public static void BuildEmulator()
        {
            AndroidArchitecture originalArchitectures = PlayerSettings.Android.targetArchitectures;
            try
            {
                // The production smoke APK remains ARM64. This isolated artifact avoids
                // relying on Android Emulator's ARM translation during acceptance runs.
                PlayerSettings.Android.targetArchitectures = AndroidArchitecture.X86_64;
                BuildAtPath(EmulatorOutputPath, EmulatorBuildOptions);
            }
            finally
            {
                PlayerSettings.Android.targetArchitectures = originalArchitectures;
            }
        }

        private static void BuildAtPath(string outputPath, BuildOptions buildOptions)
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

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.Android,
                options = buildOptions
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
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
