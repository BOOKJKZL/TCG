using Gacha.Presentation;
using NUnit.Framework;

public class UIFeedbackServiceTests
{
    private sealed class AudioSink : IAudioFeedbackSink
    {
        public string LastKey { get; private set; }

        public bool TryPlay(string cueKey)
        {
            LastKey = cueKey;
            return true;
        }
    }

    private sealed class HapticSink : IHapticFeedbackSink
    {
        public int PulseCount { get; private set; }
        public void Pulse() => PulseCount++;
    }

    private AudioSink audio;
    private HapticSink haptic;

    [SetUp]
    public void SetUp()
    {
        audio = new AudioSink();
        haptic = new HapticSink();
        UIFeedbackService.RegisterAudioSink(audio);
        UIFeedbackService.RegisterHapticSink(haptic);
        UIFeedbackService.Configure(false, true, 1f);
    }

    [TearDown]
    public void TearDown()
    {
        UIFeedbackService.UnregisterAudioSink(audio);
        UIFeedbackService.RegisterHapticSink(null);
        UIFeedbackService.Configure(false, true, 1f);
    }

    [Test]
    public void Play_MapsCueToStableAudioKey()
    {
        bool played = UIFeedbackService.Play(FeedbackCue.CardFlip);

        Assert.That(played, Is.True);
        Assert.That(audio.LastKey, Is.EqualTo(FeedbackCueKeys.CardFlip));
    }

    [Test]
    public void Play_OnlyPulsesWhenRequestedAndEnabled()
    {
        UIFeedbackService.Play(FeedbackCue.RareReveal, true);
        UIFeedbackService.Configure(false, false, 1f);
        UIFeedbackService.Play(FeedbackCue.RareReveal, true);

        Assert.That(haptic.PulseCount, Is.EqualTo(1));
    }

    [Test]
    public void Configure_ClampsAnimationSpeedAndStoresAccessibilityOptions()
    {
        UIFeedbackService.Configure(true, false, 9f);

        Assert.That(UIFeedbackService.ReduceMotion, Is.True);
        Assert.That(UIFeedbackService.HapticsEnabled, Is.False);
        Assert.That(UIFeedbackService.AnimationSpeed, Is.EqualTo(2f));
    }
}
