using Gacha.Presentation;
using NUnit.Framework;
using System.Reflection;
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
}
