using System;
using System.Collections.Generic;
using System.Linq;
using Gacha.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public class ThemeAudioAssetTests
{
    [Test]
    public void ThemeAudioAssets_AreCompleteAndMobileOptimized()
    {
        Assert.That(ThemeAudioAssetGenerator.ThemeSounds.Count, Is.EqualTo(10));
        Assert.That(ThemeAudioAssetGenerator.ThemeSounds.Select(spec => spec.Key).Distinct().Count(),
            Is.EqualTo(10));
        Assert.That(ThemeAudioAssetGenerator.ThemeSounds.Select(spec => spec.AssetPath).Distinct().Count(),
            Is.EqualTo(10));

        foreach (ThemeAudioAssetGenerator.SoundSpec spec in ThemeAudioAssetGenerator.ThemeSounds)
        {
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(spec.AssetPath);
            var importer = AssetImporter.GetAtPath(spec.AssetPath) as AudioImporter;
            Assert.That(clip, Is.Not.Null, spec.AssetPath);
            Assert.That(importer, Is.Not.Null, spec.AssetPath);
            Assert.That(clip.channels, Is.EqualTo(1), spec.AssetPath);
            Assert.That(clip.frequency, Is.EqualTo(ThemeAudioAssetGenerator.SampleRate), spec.AssetPath);
            Assert.That(clip.length, Is.EqualTo(spec.DurationSeconds).Within(0.015f), spec.AssetPath);
            Assert.That(importer.forceToMono, Is.True, spec.AssetPath);
            Assert.That(importer.loadInBackground, Is.False, spec.AssetPath);
            Assert.That(importer.ambisonic, Is.False, spec.AssetPath);

            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            Assert.That(settings.loadType, Is.EqualTo(AudioClipLoadType.DecompressOnLoad), spec.AssetPath);
            Assert.That(settings.compressionFormat, Is.EqualTo(AudioCompressionFormat.ADPCM), spec.AssetPath);
            Assert.That(settings.sampleRateSetting,
                Is.EqualTo(AudioSampleRateSetting.OverrideSampleRate), spec.AssetPath);
            Assert.That(settings.sampleRateOverride,
                Is.EqualTo(ThemeAudioAssetGenerator.SampleRate), spec.AssetPath);
            Assert.That(settings.preloadAudioData, Is.True, spec.AssetPath);
        }
    }

    [Test]
    public void AudioConfig_MapsEveryThemeKeyToItsBakedClip()
    {
        AudioClipConfig config = AssetDatabase.LoadAssetAtPath<AudioClipConfig>(
            ThemeAudioAssetGenerator.ConfigAssetPath);
        Assert.That(config, Is.Not.Null);
        Dictionary<string, AudioClipConfig.AudioEntry> entries = config.audioEntries
            .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.key))
            .ToDictionary(entry => entry.key, StringComparer.Ordinal);

        foreach (ThemeAudioAssetGenerator.SoundSpec spec in ThemeAudioAssetGenerator.ThemeSounds)
        {
            Assert.That(entries.TryGetValue(spec.Key, out AudioClipConfig.AudioEntry entry),
                Is.True, spec.Key);
            AudioClip expected = AssetDatabase.LoadAssetAtPath<AudioClip>(spec.AssetPath);
            Assert.That(entry.clip, Is.SameAs(expected), spec.Key);
        }
    }

    [Test]
    public void StartScene_AudioManagerUsesThePopulatedConfig()
    {
        const string scenePath = "Assets/Scenes/001_StartScene.unity";
        Scene scene = SceneManager.GetSceneByPath(scenePath);
        bool openedForTest = !scene.IsValid() || !scene.isLoaded;
        if (openedForTest)
            scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
        try
        {
            AudioManager manager = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<AudioManager>(true))
                .Single();
            AudioClipConfig expected = AssetDatabase.LoadAssetAtPath<AudioClipConfig>(
                ThemeAudioAssetGenerator.ConfigAssetPath);
            Assert.That(manager.audioConfig, Is.SameAs(expected));
            Assert.That(manager.audioConfig.audioEntries.Count(entry => entry != null),
                Is.GreaterThanOrEqualTo(ThemeAudioAssetGenerator.ThemeSounds.Count));
        }
        finally
        {
            if (openedForTest)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    [Test]
    public void ThemeAudioAssets_AreAudibleCleanAndDistinct()
    {
        var fingerprints = new HashSet<ulong>();
        foreach (ThemeAudioAssetGenerator.SoundSpec spec in ThemeAudioAssetGenerator.ThemeSounds)
        {
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(spec.AssetPath);
            Assert.That(clip.LoadAudioData(), Is.True, spec.AssetPath);
            var samples = new float[clip.samples * clip.channels];
            Assert.That(clip.GetData(samples, 0), Is.True, spec.AssetPath);

            float peak = samples.Max(sample => Mathf.Abs(sample));
            double mean = samples.Average(sample => (double)sample);
            double rms = Math.Sqrt(samples.Average(sample => (double)sample * sample));
            Assert.That(peak, Is.InRange(0.45f, 0.92f), spec.AssetPath);
            Assert.That(rms, Is.InRange(0.018d, 0.34d), spec.AssetPath);
            Assert.That(Math.Abs(mean), Is.LessThan(0.02d), spec.AssetPath);
            Assert.That(Mathf.Abs(samples[0]), Is.LessThan(0.02f), spec.AssetPath);
            Assert.That(Mathf.Abs(samples[samples.Length - 1]), Is.LessThan(0.02f), spec.AssetPath);
            Assert.That(fingerprints.Add(Fingerprint(samples)), Is.True,
                $"Audio fingerprint is not unique: {spec.AssetPath}");
        }
    }

    private static ulong Fingerprint(IReadOnlyList<float> samples)
    {
        const ulong offset = 1469598103934665603UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offset;
        int stride = Math.Max(1, samples.Count / 256);
        for (int index = 0; index < samples.Count; index += stride)
        {
            int quantized = Mathf.RoundToInt(samples[index] * 32767f);
            hash ^= unchecked((ushort)quantized);
            hash *= prime;
        }
        return hash;
    }
}
