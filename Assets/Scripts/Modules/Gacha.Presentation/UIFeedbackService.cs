using System;
using UnityEngine;

namespace Gacha.Presentation
{
    public interface IAudioFeedbackSink
    {
        bool TryPlay(string cueKey);
    }

    public interface IHapticFeedbackSink
    {
        void Pulse();
    }

    public static class UIFeedbackService
    {
        private sealed class MobileHapticFeedbackSink : IHapticFeedbackSink
        {
            public void Pulse()
            {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
                Handheld.Vibrate();
#endif
            }
        }

        private static IAudioFeedbackSink audioSink;
        private static IHapticFeedbackSink hapticSink = new MobileHapticFeedbackSink();

        public static bool ReduceMotion { get; private set; }
        public static bool HapticsEnabled { get; private set; } = true;
        public static float AnimationSpeed { get; private set; } = 1f;

        public static event Action<FeedbackCue> FeedbackPlayed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            audioSink = null;
            hapticSink = new MobileHapticFeedbackSink();
            ReduceMotion = false;
            HapticsEnabled = true;
            AnimationSpeed = 1f;
            FeedbackPlayed = null;
        }

        public static void Configure(bool reduceMotion, bool hapticsEnabled, float animationSpeed)
        {
            ReduceMotion = reduceMotion;
            HapticsEnabled = hapticsEnabled;
            AnimationSpeed = Mathf.Clamp(animationSpeed, 0.5f, 2f);
        }

        public static void RegisterAudioSink(IAudioFeedbackSink sink)
        {
            audioSink = sink;
        }

        public static void UnregisterAudioSink(IAudioFeedbackSink sink)
        {
            if (ReferenceEquals(audioSink, sink))
            {
                audioSink = null;
            }
        }

        public static void RegisterHapticSink(IHapticFeedbackSink sink)
        {
            hapticSink = sink ?? new MobileHapticFeedbackSink();
        }

        public static bool Play(FeedbackCue cue, bool requestHaptic = false)
        {
            bool playedAudio = audioSink != null && audioSink.TryPlay(FeedbackCueKeys.FromCue(cue));

            if (requestHaptic && HapticsEnabled)
            {
                hapticSink.Pulse();
            }

            FeedbackPlayed?.Invoke(cue);
            return playedAudio;
        }
    }
}
