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
        Assert.That(
            (AndroidSmokeBuilder.SmokeBuildOptions & BuildOptions.CleanBuildCache) != 0,
            Is.True,
            "Smoke builds must compact incremental Android archive tombstones before package-size verification.");
        Assert.That(
            (AndroidSmokeBuilder.EmulatorBuildOptions & BuildOptions.CleanBuildCache) == 0,
            Is.True,
            "Repeated emulator UI acceptance builds should reuse the native x86_64 cache.");
    }
}
