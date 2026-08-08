using System.IO;
using System.Linq;
using Gacha.EditorTools;
using NUnit.Framework;
using UnityEditor;

public class AndroidBuildReadinessTests
{
    [Test]
    public void AndroidBuild_UsesStableIdentityAndAllGameplayScenes()
    {
        Assert.That(
            PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android),
            Is.EqualTo("com.personal.universalgacha"));
        Assert.That(PlayerSettings.productName, Is.EqualTo("Universal Gacha Simulator"));

        string[] scenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => Path.GetFileNameWithoutExtension(scene.path))
            .ToArray();
        Assert.That(scenes, Is.EqualTo(new[]
        {
            "001_StartScene",
            "002_MainMenuScene",
            "003_GachaScene",
            "004_CollectionScene",
            "005_SettingScene",
            "006_ContentScene"
        }));
    }

    [Test]
    public void AndroidBuild_DoesNotEmbedPrivateImportedContent()
    {
        Assert.That(Directory.Exists("Assets/StreamingAssets/LocalContent"), Is.False);
        Assert.That(AndroidSmokeBuilder.OutputPath.Replace('\\', '/'), Does.StartWith("Builds/"),
            "Build artifacts must remain outside Assets so they cannot inflate the application package.");
        Assert.That(AndroidSmokeBuilder.EmulatorOutputPath.Replace('\\', '/'), Does.StartWith("Builds/"));
        Assert.That(AndroidSmokeBuilder.EmulatorOutputPath, Does.Contain("x86_64"),
            "The emulator acceptance artifact must state its non-production ABI explicitly.");
        Assert.That(AndroidSmokeBuilder.SmokeArchitecture, Is.EqualTo(AndroidArchitecture.ARM64),
            "The production smoke build must force ARM64 even when mutable editor state still selects x86_64.");
        Assert.That(AndroidSmokeBuilder.EmulatorArchitecture, Is.EqualTo(AndroidArchitecture.X86_64));
        Assert.That(
            (AndroidSmokeBuilder.SmokeBuildOptions & BuildOptions.CleanBuildCache) != 0,
            Is.True,
            "Smoke builds must compact incremental Android archive tombstones before package-size verification.");
        Assert.That(
            (AndroidSmokeBuilder.EmulatorBuildOptions & BuildOptions.CleanBuildCache) != 0,
            Is.True,
            "Emulator acceptance builds must also compact incremental archive tombstones before size verification.");
        Assert.That(AndroidSmokeBuilder.EmulatorUsesBuiltInRenderPipeline, Is.True,
            "The x86_64 emulator artifact must isolate SwiftShader URP failures from software acceptance.");

        Assert.That(AndroidReleaseBuilder.OutputDirectory.Replace('\\', '/'),
            Is.EqualTo("Builds/Android/Release"));
        Assert.That(AndroidReleaseBuilder.ReleaseArchitecture, Is.EqualTo(AndroidArchitecture.ARM64));
        Assert.That(
            AndroidReleaseBuilder.ReleaseBuildOptions & AndroidReleaseBuilder.ForbiddenReleaseBuildOptions,
            Is.EqualTo(BuildOptions.None),
            "Stable builds must not include development, debugging, profiler, test, assertion, or connection flags.");
        Assert.That(
            (AndroidReleaseBuilder.ReleaseBuildOptions & BuildOptions.CleanBuildCache) != 0,
            Is.True);
        Assert.That(
            (AndroidReleaseBuilder.ReleaseBuildOptions & BuildOptions.CompressWithLz4HC) != 0,
            Is.True);
        Assert.That(
            AndroidReleaseBuilder.GetOutputPath("0.1.1", 2).Replace('\\', '/'),
            Is.EqualTo("Builds/Android/Release/UniversalGachaSimulator-release-0.1.1+2.apk"));
    }

    [Test]
    public void AndroidRelease_RequiresStrictlyNewVersionAndNonDevelopmentOptions()
    {
        Assert.DoesNotThrow(() => AndroidReleaseBuilder.ValidateVersion("0.1.1", 2, 1));
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => AndroidReleaseBuilder.ValidateVersion("0.1.1", 1, 1));
        Assert.Throws<System.ArgumentException>(
            () => AndroidReleaseBuilder.ValidateVersion("release-one", 2, 1));
        Assert.DoesNotThrow(
            () => AndroidReleaseBuilder.ValidateBuildOptions(AndroidReleaseBuilder.ReleaseBuildOptions));
        Assert.Throws<System.InvalidOperationException>(
            () => AndroidReleaseBuilder.ValidateBuildOptions(
                AndroidReleaseBuilder.ReleaseBuildOptions | BuildOptions.Development));
        Assert.Throws<System.InvalidOperationException>(
            () => AndroidReleaseBuilder.ValidateBuildOptions(BuildOptions.CompressWithLz4HC));
    }

    [Test]
    public void AndroidBuild_UsesOnlyTheNewInputSystem()
    {
        const string settingsPath = "ProjectSettings/ProjectSettings.asset";
        string settings = File.ReadAllText(settingsPath);

        Assert.That(settings, Does.Contain("  activeInputHandler: 1"),
            "Android does not support Active Input Handling = Both in Unity 6. " +
            "The project uses InputSystemUIInputModule and must keep only the new Input System enabled.");
        Assert.That(settings, Does.Not.Contain("  activeInputHandler: 2"));
    }

    [Test]
    public void AndroidRecovery_UsesSystemDocumentPickerWithoutBroadStoragePermission()
    {
        const string bridgePath = "Assets/Plugins/Android/RecoveryDocumentBridge.java";
        Assert.That(File.Exists(bridgePath), Is.True);
        string bridge = File.ReadAllText(bridgePath);

        Assert.That(bridge, Does.Contain("Intent.ACTION_CREATE_DOCUMENT"));
        Assert.That(bridge, Does.Contain("Intent.ACTION_OPEN_DOCUMENT"));
        Assert.That(bridge, Does.Contain("MAXIMUM_BYTES"));
        Assert.That(bridge, Does.Not.Contain("READ_EXTERNAL_STORAGE"));
        Assert.That(bridge, Does.Not.Contain("WRITE_EXTERNAL_STORAGE"));
        Assert.That(bridge, Does.Not.Contain("MANAGE_EXTERNAL_STORAGE"));
    }
}
