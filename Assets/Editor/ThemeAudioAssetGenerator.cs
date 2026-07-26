using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gacha.Presentation;
using UnityEditor;
using UnityEngine;

namespace Gacha.EditorTools
{
    public static class ThemeAudioAssetGenerator
    {
        public const string OutputDirectory = "Assets/Resources/Audio/GachaThemes";
        public const string ConfigAssetPath = "Assets/Resources/Data/AudioClipConfig.asset";
        public const int SampleRate = 44100;

        public sealed class SoundSpec
        {
            public SoundSpec(
                string key,
                string fileName,
                string displayName,
                bool rareReveal,
                float durationSeconds,
                float startFrequency,
                float endFrequency,
                float noiseAmount,
                float brightness,
                uint seed)
            {
                Key = key;
                FileName = fileName;
                DisplayName = displayName;
                RareReveal = rareReveal;
                DurationSeconds = durationSeconds;
                StartFrequency = startFrequency;
                EndFrequency = endFrequency;
                NoiseAmount = noiseAmount;
                Brightness = brightness;
                Seed = seed;
            }

            public string Key { get; }
            public string FileName { get; }
            public string DisplayName { get; }
            public bool RareReveal { get; }
            public float DurationSeconds { get; }
            public float StartFrequency { get; }
            public float EndFrequency { get; }
            public float NoiseAmount { get; }
            public float Brightness { get; }
            public uint Seed { get; }
            public string AssetPath => $"{OutputDirectory}/{FileName}";
        }

        private static readonly SoundSpec[] Specs =
        {
            new SoundSpec(ProductOpeningThemeAudioKeys.VintagePackOpen,
                "vintage-pack-open.wav", "Vintage Pack Open", false,
                0.56f, 330f, 145f, 0.72f, 0.22f, 0x41A2B3C4u),
            new SoundSpec(ProductOpeningThemeAudioKeys.VintageRareReveal,
                "vintage-rare-reveal.wav", "Vintage Rare Reveal", true,
                0.94f, 392f, 784f, 0.10f, 0.32f, 0x51A2B3C4u),
            new SoundSpec(ProductOpeningThemeAudioKeys.ForestPackOpen,
                "forest-pack-open.wav", "Forest Pack Open", false,
                0.58f, 420f, 180f, 0.64f, 0.30f, 0x42B3C4D5u),
            new SoundSpec(ProductOpeningThemeAudioKeys.ForestRareReveal,
                "forest-rare-reveal.wav", "Forest Rare Reveal", true,
                0.98f, 440f, 1046.5f, 0.16f, 0.42f, 0x52B3C4D5u),
            new SoundSpec(ProductOpeningThemeAudioKeys.RubyPackOpen,
                "ruby-pack-open.wav", "Ruby Pack Open", false,
                0.48f, 510f, 205f, 0.68f, 0.48f, 0x43C4D5E6u),
            new SoundSpec(ProductOpeningThemeAudioKeys.RubyRareReveal,
                "ruby-rare-reveal.wav", "Ruby Rare Reveal", true,
                0.86f, 523.25f, 1396.9f, 0.08f, 0.58f, 0x53C4D5E6u),
            new SoundSpec(ProductOpeningThemeAudioKeys.ElectricPackOpen,
                "electric-pack-open.wav", "Electric Pack Open", false,
                0.42f, 780f, 260f, 0.58f, 0.82f, 0x44D5E6F7u),
            new SoundSpec(ProductOpeningThemeAudioKeys.ElectricRareReveal,
                "electric-rare-reveal.wav", "Electric Rare Reveal", true,
                0.78f, 659.25f, 1760f, 0.12f, 0.88f, 0x54D5E6F7u),
            new SoundSpec(ProductOpeningThemeAudioKeys.GalleryPackOpen,
                "gallery-pack-open.wav", "Gallery Pack Open", false,
                0.52f, 620f, 235f, 0.54f, 0.68f, 0x45E6F708u),
            new SoundSpec(ProductOpeningThemeAudioKeys.GalleryRareReveal,
                "gallery-rare-reveal.wav", "Gallery Rare Reveal", true,
                1.02f, 587.33f, 1567.98f, 0.14f, 0.76f, 0x55E6F708u)
        };

        public static IReadOnlyList<SoundSpec> ThemeSounds => Specs;

        [MenuItem("Tools/Gacha/Generate Original Theme Audio")]
        public static void GenerateAll()
        {
            Directory.CreateDirectory(OutputDirectory);
            foreach (SoundSpec spec in Specs)
            {
                float[] samples = spec.RareReveal
                    ? GenerateRareReveal(spec)
                    : GeneratePackOpen(spec);
                WriteIfChanged(spec.AssetPath, EncodeWave(samples));
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (SoundSpec spec in Specs)
                ConfigureImporter(spec.AssetPath);
            PopulateAudioConfig();
            AssetDatabase.SaveAssets();
            Debug.Log($"Generated {Specs.Length} original theme audio assets in '{OutputDirectory}'.");
        }

        public static void GenerateBatch()
        {
            GenerateAll();
        }

        private static float[] GeneratePackOpen(SoundSpec spec)
        {
            int sampleCount = Mathf.CeilToInt(SampleRate * spec.DurationSeconds);
            var samples = new float[sampleCount];
            uint state = spec.Seed;
            float lowPass = 0f;
            float primaryPhase = 0f;
            float foilPhase = 0f;

            for (int index = 0; index < sampleCount; index++)
            {
                float time = index / (float)SampleRate;
                float progress = index / (float)(sampleCount - 1);
                float white = NextNoise(ref state);
                float smoothing = Mathf.Lerp(0.055f, 0.19f, spec.Brightness);
                lowPass += (white - lowPass) * smoothing;
                float paper = white - lowPass;
                float tearPulse = 0.58f + 0.42f * Mathf.Abs(
                    Mathf.Sin((19f + spec.Brightness * 17f) * Mathf.PI * time));
                float tearEnvelope = SmoothAttack(progress, 0.025f) *
                    Mathf.Pow(1f - progress, 0.72f);

                float frequency = Mathf.Lerp(
                    spec.StartFrequency,
                    spec.EndFrequency,
                    Mathf.Pow(progress, 0.68f));
                primaryPhase += 2f * Mathf.PI * frequency / SampleRate;
                foilPhase += 2f * Mathf.PI * frequency * (1.9f + spec.Brightness) / SampleRate;
                float body = Mathf.Sin(primaryPhase) * 0.22f +
                    Mathf.Sin(foilPhase) * spec.Brightness * 0.09f;

                float transientGate = Mathf.Repeat(progress * (13f + spec.Brightness * 9f), 1f);
                float crackle = transientGate < 0.035f
                    ? NextNoise(ref state) * (1f - transientGate / 0.035f)
                    : 0f;
                float closingImpact = progress > 0.68f
                    ? Mathf.Sin(2f * Mathf.PI * spec.EndFrequency * 0.5f * time) *
                        Mathf.Exp(-(progress - 0.68f) * 18f)
                    : 0f;

                samples[index] = (paper * spec.NoiseAmount * tearPulse + body +
                    crackle * 0.20f + closingImpact * 0.13f) * tearEnvelope;
            }

            return FinalizeSamples(samples, 0.76f);
        }

        private static float[] GenerateRareReveal(SoundSpec spec)
        {
            int sampleCount = Mathf.CeilToInt(SampleRate * spec.DurationSeconds);
            var samples = new float[sampleCount];
            uint state = spec.Seed;
            float shimmer = 0f;
            float[] onsetFractions = { 0f, 0.16f, 0.32f, 0.50f };

            for (int index = 0; index < sampleCount; index++)
            {
                float time = index / (float)SampleRate;
                float progress = index / (float)(sampleCount - 1);
                float value = 0f;
                for (int noteIndex = 0; noteIndex < onsetFractions.Length; noteIndex++)
                {
                    float onset = onsetFractions[noteIndex] * spec.DurationSeconds;
                    float localTime = time - onset;
                    if (localTime < 0f)
                        continue;
                    float noteProgress = noteIndex / (float)(onsetFractions.Length - 1);
                    float frequency = spec.StartFrequency * Mathf.Pow(
                        spec.EndFrequency / spec.StartFrequency,
                        noteProgress);
                    float noteDuration = spec.DurationSeconds - onset;
                    float envelope = SmoothAttack(localTime / noteDuration, 0.035f) *
                        Mathf.Exp(-localTime * Mathf.Lerp(3.8f, 5.2f, spec.Brightness));
                    float fundamental = Mathf.Sin(2f * Mathf.PI * frequency * localTime);
                    float harmonic = Mathf.Sin(2f * Mathf.PI * frequency * 2f * localTime + 0.28f) *
                        spec.Brightness * 0.34f;
                    float glass = Mathf.Sin(2f * Mathf.PI * frequency * 3.01f * localTime + 0.61f) *
                        spec.Brightness * 0.12f;
                    value += (fundamental + harmonic + glass) * envelope *
                        Mathf.Lerp(0.32f, 0.21f, noteProgress);
                }

                float white = NextNoise(ref state);
                shimmer += (white - shimmer) * 0.28f;
                float air = (white - shimmer) * spec.NoiseAmount *
                    Mathf.Sin(progress * Mathf.PI) * 0.26f;
                float bloom = Mathf.Sin(2f * Mathf.PI * spec.StartFrequency * 0.5f * time) *
                    Mathf.Sin(progress * Mathf.PI) * 0.08f;
                samples[index] = value + air + bloom;
            }

            return FinalizeSamples(samples, 0.74f);
        }

        private static float SmoothAttack(float progress, float attackFraction)
        {
            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress / attackFraction));
        }

        private static float NextNoise(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state / (float)uint.MaxValue) * 2f - 1f;
        }

        private static float[] FinalizeSamples(float[] samples, float targetPeak)
        {
            float mean = samples.Average();
            float peak = 0f;
            for (int index = 0; index < samples.Length; index++)
            {
                samples[index] -= mean;
                peak = Mathf.Max(peak, Mathf.Abs(samples[index]));
            }

            float gain = peak > 0f ? targetPeak / peak : 1f;
            int fadeSamples = Mathf.Min(256, samples.Length / 8);
            for (int index = 0; index < samples.Length; index++)
            {
                float edgeGain = 1f;
                if (index < fadeSamples)
                    edgeGain = index / (float)fadeSamples;
                else if (index >= samples.Length - fadeSamples)
                    edgeGain = (samples.Length - 1 - index) / (float)fadeSamples;
                samples[index] = Mathf.Clamp(samples[index] * gain * edgeGain, -1f, 1f);
            }

            return samples;
        }

        private static byte[] EncodeWave(IReadOnlyList<float> samples)
        {
            using var stream = new MemoryStream(44 + samples.Count * 2);
            using var writer = new BinaryWriter(stream);
            writer.Write(new[] { 'R', 'I', 'F', 'F' });
            writer.Write(36 + samples.Count * 2);
            writer.Write(new[] { 'W', 'A', 'V', 'E' });
            writer.Write(new[] { 'f', 'm', 't', ' ' });
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(SampleRate);
            writer.Write(SampleRate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(new[] { 'd', 'a', 't', 'a' });
            writer.Write(samples.Count * 2);
            foreach (float sample in samples)
                writer.Write((short)Mathf.RoundToInt(Mathf.Clamp(sample, -1f, 1f) * short.MaxValue));
            writer.Flush();
            return stream.ToArray();
        }

        private static void WriteIfChanged(string path, byte[] bytes)
        {
            if (File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(bytes))
                return;
            File.WriteAllBytes(path, bytes);
        }

        private static void ConfigureImporter(string assetPath)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as AudioImporter;
            if (importer == null)
                throw new InvalidOperationException($"Audio importer was not created for '{assetPath}'.");

            var settings = new AudioImporterSampleSettings
            {
                loadType = AudioClipLoadType.DecompressOnLoad,
                compressionFormat = AudioCompressionFormat.ADPCM,
                quality = 0.82f,
                sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate,
                sampleRateOverride = SampleRate,
                preloadAudioData = true
            };
            importer.forceToMono = true;
            importer.loadInBackground = false;
            importer.ambisonic = false;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
        }

        private static void PopulateAudioConfig()
        {
            AudioClipConfig config = AssetDatabase.LoadAssetAtPath<AudioClipConfig>(ConfigAssetPath);
            if (config == null)
                throw new InvalidOperationException($"Audio config is missing at '{ConfigAssetPath}'.");

            var themeKeys = new HashSet<string>(Specs.Select(spec => spec.Key), StringComparer.Ordinal);
            var entries = (config.audioEntries ?? new List<AudioClipConfig.AudioEntry>())
                .Where(entry => entry != null && !themeKeys.Contains(entry.key))
                .ToList();
            foreach (SoundSpec spec in Specs)
            {
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(spec.AssetPath);
                if (clip == null)
                    throw new InvalidOperationException($"Generated audio could not be loaded from '{spec.AssetPath}'.");
                entries.Add(new AudioClipConfig.AudioEntry { key = spec.Key, clip = clip });
            }

            config.audioEntries = entries;
            EditorUtility.SetDirty(config);
        }
    }
}
