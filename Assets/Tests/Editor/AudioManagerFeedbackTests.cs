using Gacha.Presentation;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public class AudioManagerFeedbackTests
{
    [Test]
    public void RuntimeFallback_ProvidesCoreAndAllThemeAudioCues()
    {
        var host = new GameObject("AudioManagerFeedbackTests");
        try
        {
            AudioManager manager = host.AddComponent<AudioManager>();
            MethodInfo awake = typeof(AudioManager).GetMethod(
                "Awake",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(awake, Is.Not.Null);
            awake.Invoke(manager, null);
            string[] keys =
            {
                FeedbackCueKeys.ButtonClick,
                FeedbackCueKeys.Back,
                FeedbackCueKeys.Confirm,
                FeedbackCueKeys.Error,
                FeedbackCueKeys.DownloadStart,
                FeedbackCueKeys.DownloadComplete,
                FeedbackCueKeys.PackOpen,
                FeedbackCueKeys.CardFlip,
                FeedbackCueKeys.RareReveal,
                FeedbackCueKeys.CollectionNew,
                ProductOpeningThemeAudioKeys.VintagePackOpen,
                ProductOpeningThemeAudioKeys.VintageRareReveal,
                ProductOpeningThemeAudioKeys.ForestPackOpen,
                ProductOpeningThemeAudioKeys.ForestRareReveal,
                ProductOpeningThemeAudioKeys.RubyPackOpen,
                ProductOpeningThemeAudioKeys.RubyRareReveal,
                ProductOpeningThemeAudioKeys.ElectricPackOpen,
                ProductOpeningThemeAudioKeys.ElectricRareReveal,
                ProductOpeningThemeAudioKeys.GalleryPackOpen,
                ProductOpeningThemeAudioKeys.GalleryRareReveal
            };

            foreach (string key in keys)
                Assert.That(manager.GetAudioClip(key), Is.Not.Null, key);
        }
        finally
        {
            Object.DestroyImmediate(host);
        }
    }

    [Test]
    public void ConfiguredThemeAssets_TakePriorityOverProceduralFallbacks()
    {
        var host = new GameObject("ConfiguredThemeAudioTests");
        try
        {
            AudioManager manager = host.AddComponent<AudioManager>();
            manager.audioConfig = AssetDatabase.LoadAssetAtPath<AudioClipConfig>(
                "Assets/Resources/Data/AudioClipConfig.asset");
            FieldInfo clipsField = typeof(AudioManager).GetField(
                "audioClips",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo awake = typeof(AudioManager).GetMethod(
                "Awake",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(manager.audioConfig, Is.Not.Null);
            Assert.That(clipsField, Is.Not.Null);
            Assert.That(awake, Is.Not.Null);

            clipsField.SetValue(manager, new Dictionary<string, AudioClip>());
            awake.Invoke(manager, null);
            foreach (AudioClipConfig.AudioEntry entry in manager.audioConfig.audioEntries.Where(
                entry => entry != null && !string.IsNullOrWhiteSpace(entry.key)))
            {
                Assert.That(manager.GetAudioClip(entry.key), Is.SameAs(entry.clip), entry.key);
            }
        }
        finally
        {
            Object.DestroyImmediate(host);
        }
    }
}
