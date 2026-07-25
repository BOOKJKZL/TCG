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
        public const BuildOptions SmokeBuildOptions =
            BuildOptions.Development |
            BuildOptions.CompressWithLz4 |
            BuildOptions.CleanBuildCache;

        [MenuItem("Tools/Gacha/Build Android Smoke APK")]
        public static void Build()
        {
            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
            if (scenes.Length == 0)
                throw new InvalidOperationException("The Android smoke build requires at least one enabled scene.");

            string outputDirectory = Path.GetDirectoryName(OutputPath);
            if (!string.IsNullOrWhiteSpace(outputDirectory))
                Directory.CreateDirectory(outputDirectory);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = OutputPath,
                target = BuildTarget.Android,
                options = SmokeBuildOptions
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
    }
}
