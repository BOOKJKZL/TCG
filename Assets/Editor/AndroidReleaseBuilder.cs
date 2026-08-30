using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Gacha.EditorTools
{
    public static class AndroidReleaseBuilder
    {
        public const string OutputDirectory = "Builds/Android/Release";
        public const AndroidArchitecture ReleaseArchitecture = AndroidArchitecture.ARM64;
        public const BuildOptions ReleaseBuildOptions =
            BuildOptions.CleanBuildCache |
            BuildOptions.CompressWithLz4HC |
            BuildOptions.StrictMode;
        public const BuildOptions ForbiddenReleaseBuildOptions =
            BuildOptions.Development |
            BuildOptions.AllowDebugging |
            BuildOptions.ConnectWithProfiler |
            BuildOptions.EnableDeepProfilingSupport |
            BuildOptions.IncludeTestAssemblies |
            BuildOptions.EnableCodeCoverage |
            BuildOptions.ForceEnableAssertions |
            BuildOptions.WaitForPlayerConnection |
            BuildOptions.ConnectToHost;

        public const string VersionNameVariable = "TCG_ANDROID_VERSION_NAME";
        public const string VersionCodeVariable = "TCG_ANDROID_VERSION_CODE";
        public const string PublishedVersionCodeVariable = "TCG_ANDROID_PUBLISHED_LATEST_VERSION_CODE";
        public const string KeystorePathVariable = "TCG_ANDROID_KEYSTORE_PATH";
        public const string KeystorePasswordVariable = "TCG_ANDROID_KEYSTORE_PASSWORD";
        public const string KeyAliasVariable = "TCG_ANDROID_KEY_ALIAS";
        public const string KeyPasswordVariable = "TCG_ANDROID_KEY_PASSWORD";

        private static readonly Regex VersionNamePattern = new Regex(
            @"^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$",
            RegexOptions.CultureInvariant);

        [MenuItem("Tools/Gacha/Build Signed Android Release APK From Environment")]
        public static void Build()
        {
            ReleaseConfiguration configuration = ReadConfigurationFromEnvironment();
            Build(configuration);
        }

        public static void BuildBatch()
        {
            Build();
        }

        public static string GetOutputPath(string versionName, int versionCode)
        {
            ValidateVersion(versionName, versionCode, versionCode - 1);
            string fileVersion = Regex.Replace(versionName, @"[^0-9A-Za-z.-]", "-");
            return $"{OutputDirectory}/UniversalGachaSimulator-release-{fileVersion}+{versionCode}.apk";
        }

        public static void ValidateVersion(string versionName, int versionCode, int previousStableVersionCode)
        {
            if (string.IsNullOrWhiteSpace(versionName) || !VersionNamePattern.IsMatch(versionName))
                throw new ArgumentException(
                    "Release version name must use semantic version form such as 0.1.1 or 0.2.0-rc.1.",
                    nameof(versionName));
            if (previousStableVersionCode < 1)
                throw new ArgumentOutOfRangeException(
                    nameof(previousStableVersionCode),
                    "Previous stable versionCode must be a positive integer.");
            if (versionCode <= previousStableVersionCode)
                throw new ArgumentOutOfRangeException(
                    nameof(versionCode),
                    $"Release versionCode {versionCode} must be greater than stable versionCode {previousStableVersionCode}.");
        }

        public static void ValidateBuildOptions(BuildOptions options)
        {
            BuildOptions forbidden = options & ForbiddenReleaseBuildOptions;
            if (forbidden != BuildOptions.None)
                throw new InvalidOperationException($"Release build options contain forbidden flags: {forbidden}.");
            if ((options & BuildOptions.CleanBuildCache) == 0)
                throw new InvalidOperationException("Release builds must use CleanBuildCache.");
        }

        private static ReleaseConfiguration ReadConfigurationFromEnvironment()
        {
            string versionName = GetRequiredEnvironmentVariable(VersionNameVariable);
            int versionCode = ParsePositiveInteger(VersionCodeVariable);
            int publishedVersionCode = ParsePositiveInteger(PublishedVersionCodeVariable);
            string keystorePath = Path.GetFullPath(GetRequiredEnvironmentVariable(KeystorePathVariable));
            string keystorePassword = GetRequiredEnvironmentVariable(KeystorePasswordVariable);
            string keyAlias = GetRequiredEnvironmentVariable(KeyAliasVariable);
            string keyPassword = GetRequiredEnvironmentVariable(KeyPasswordVariable);

            ValidateVersion(versionName, versionCode, publishedVersionCode);
            ValidateBuildOptions(ReleaseBuildOptions);
            ValidateKeystorePath(keystorePath);

            return new ReleaseConfiguration(
                versionName,
                versionCode,
                publishedVersionCode,
                keystorePath,
                keystorePassword,
                keyAlias,
                keyPassword);
        }

        private static void ValidateKeystorePath(string keystorePath)
        {
            if (!File.Exists(keystorePath))
                throw new FileNotFoundException("Release keystore was not found.", keystorePath);

            string projectRoot = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
            string assetsRoot = Path.GetFullPath(UnityEngine.Application.dataPath) + Path.DirectorySeparatorChar;
            string normalizedKeystore = Path.GetFullPath(keystorePath);
            if (normalizedKeystore.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Release keystore must never be stored under Assets.");

            string projectPrefix = projectRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!normalizedKeystore.StartsWith(projectPrefix, StringComparison.OrdinalIgnoreCase))
                return;

            string privateRoot = Path.GetFullPath(Path.Combine(projectRoot, "LocalContent")) +
                Path.DirectorySeparatorChar;
            if (!normalizedKeystore.StartsWith(privateRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "A repository-local release keystore must be stored under the Git-ignored LocalContent directory.");
        }

        private static void Build(ReleaseConfiguration configuration)
        {
            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
            if (scenes.Length == 0)
                throw new InvalidOperationException("The Android release build requires at least one enabled scene.");

            string outputPath = GetOutputPath(configuration.VersionName, configuration.VersionCode);
            Directory.CreateDirectory(OutputDirectory);

            var snapshot = new AndroidBuildSettingsSnapshot();
            try
            {
                ApplyReleaseSettings(configuration);
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
                    options = ReleaseBuildOptions
                };
                BuildReport report = BuildPipeline.BuildPlayer(options);
                BuildSummary summary = report.summary;
                Debug.Log(
                    $"Android signed release result={summary.result} version={configuration.VersionName}+{configuration.VersionCode} " +
                    $"scenes={scenes.Length} bytes={summary.totalSize} duration={summary.totalTime} output='{summary.outputPath}'.");
                if (summary.result != BuildResult.Succeeded)
                    throw new InvalidOperationException(
                        $"Android release build failed with {summary.totalErrors} errors and {summary.totalWarnings} warnings.");
            }
            finally
            {
                try
                {
                    snapshot.Restore();
                    AssetDatabase.SaveAssets();
                }
                finally
                {
                    configuration.ClearSecrets();
                }
            }
        }

        private static void ApplyReleaseSettings(ReleaseConfiguration configuration)
        {
            ValidateBuildOptions(ReleaseBuildOptions);
            PlayerSettings.bundleVersion = configuration.VersionName;
            PlayerSettings.Android.bundleVersionCode = configuration.VersionCode;
            PlayerSettings.Android.targetArchitectures = ReleaseArchitecture;
            PlayerSettings.Android.buildApkPerCpuArchitecture = false;
            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = configuration.KeystorePath;
            PlayerSettings.Android.keystorePass = configuration.KeystorePassword;
            PlayerSettings.Android.keyaliasName = configuration.KeyAlias;
            PlayerSettings.Android.keyaliasPass = configuration.KeyPassword;

            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.development = false;
            EditorUserBuildSettings.allowDebugging = false;
            EditorUserBuildSettings.connectProfiler = false;
            EditorUserBuildSettings.buildWithDeepProfilingSupport = false;
        }

        private static string GetRequiredEnvironmentVariable(string name)
        {
            string value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException($"Required release environment variable {name} is missing.");
            return value;
        }

        private static int ParsePositiveInteger(string name)
        {
            string value = GetRequiredEnvironmentVariable(name);
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int result) || result < 1)
                throw new InvalidOperationException($"Release environment variable {name} must be a positive integer.");
            return result;
        }

        private sealed class ReleaseConfiguration
        {
            public ReleaseConfiguration(
                string versionName,
                int versionCode,
                int publishedVersionCode,
                string keystorePath,
                string keystorePassword,
                string keyAlias,
                string keyPassword)
            {
                VersionName = versionName;
                VersionCode = versionCode;
                PublishedVersionCode = publishedVersionCode;
                KeystorePath = keystorePath;
                KeystorePassword = keystorePassword;
                KeyAlias = keyAlias;
                KeyPassword = keyPassword;
            }

            public string VersionName { get; }
            public int VersionCode { get; }
            public int PublishedVersionCode { get; }
            public string KeystorePath { get; }
            public string KeystorePassword { get; private set; }
            public string KeyAlias { get; }
            public string KeyPassword { get; private set; }

            public void ClearSecrets()
            {
                KeystorePassword = string.Empty;
                KeyPassword = string.Empty;
            }
        }

        private sealed class AndroidBuildSettingsSnapshot
        {
            private readonly string versionName = PlayerSettings.bundleVersion;
            private readonly int versionCode = PlayerSettings.Android.bundleVersionCode;
            private readonly AndroidArchitecture architectures = PlayerSettings.Android.targetArchitectures;
            private readonly bool splitByArchitecture = PlayerSettings.Android.buildApkPerCpuArchitecture;
            private readonly bool useCustomKeystore = PlayerSettings.Android.useCustomKeystore;
            private readonly string keystoreName = PlayerSettings.Android.keystoreName;
            private readonly string keystorePass = PlayerSettings.Android.keystorePass;
            private readonly string keyAliasName = PlayerSettings.Android.keyaliasName;
            private readonly string keyAliasPass = PlayerSettings.Android.keyaliasPass;
            private readonly bool buildAppBundle = EditorUserBuildSettings.buildAppBundle;
            private readonly bool development = EditorUserBuildSettings.development;
            private readonly bool allowDebugging = EditorUserBuildSettings.allowDebugging;
            private readonly bool connectProfiler = EditorUserBuildSettings.connectProfiler;
            private readonly bool deepProfiling = EditorUserBuildSettings.buildWithDeepProfilingSupport;

            public void Restore()
            {
                PlayerSettings.bundleVersion = versionName;
                PlayerSettings.Android.bundleVersionCode = versionCode;
                PlayerSettings.Android.targetArchitectures = architectures;
                PlayerSettings.Android.buildApkPerCpuArchitecture = splitByArchitecture;
                PlayerSettings.Android.useCustomKeystore = useCustomKeystore;
                PlayerSettings.Android.keystoreName = keystoreName;
                PlayerSettings.Android.keystorePass = keystorePass;
                PlayerSettings.Android.keyaliasName = keyAliasName;
                PlayerSettings.Android.keyaliasPass = keyAliasPass;
                EditorUserBuildSettings.buildAppBundle = buildAppBundle;
                EditorUserBuildSettings.development = development;
                EditorUserBuildSettings.allowDebugging = allowDebugging;
                EditorUserBuildSettings.connectProfiler = connectProfiler;
                EditorUserBuildSettings.buildWithDeepProfilingSupport = deepProfiling;
            }
        }
    }
}
