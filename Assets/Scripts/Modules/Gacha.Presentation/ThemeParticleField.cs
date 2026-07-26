using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gacha.Presentation
{
    public sealed class ThemeParticleField : IDisposable
    {
        public const int MaximumParticleCount = 12;

        private enum PlaybackMode
        {
            None,
            Ambient,
            Burst
        }

        private readonly VisualElement root;
        private readonly VisualElement[] particles = new VisualElement[MaximumParticleCount];
        private IVisualElementScheduledItem animation;
        private ProductOpeningParticleTheme theme;
        private PlaybackMode mode;
        private float startedAt;

        public ThemeParticleField(VisualElement root)
        {
            this.root = root ?? throw new ArgumentNullException(nameof(root));
            this.root.pickingMode = PickingMode.Ignore;
            this.root.Clear();
            for (int index = 0; index < particles.Length; index++)
            {
                var particle = new VisualElement
                {
                    name = $"theme-particle-{index + 1}",
                    pickingMode = PickingMode.Ignore
                };
                particle.AddToClassList("theme-particle");
                particle.AddToClassList(index % 3 == 0
                    ? "theme-particle--diamond"
                    : index % 3 == 1
                        ? "theme-particle--dot"
                        : "theme-particle--shard");
                particle.style.display = DisplayStyle.None;
                particles[index] = particle;
                this.root.Add(particle);
            }

            this.root.style.display = DisplayStyle.None;
        }

        public int ActiveParticleCount { get; private set; }
        public bool IsRunning => mode != PlaybackMode.None;

        public void PlayAmbient(ProductOpeningParticleTheme particleTheme)
        {
            if (particleTheme == null)
                throw new ArgumentNullException(nameof(particleTheme));
            if (UIFeedbackService.ReduceMotion)
            {
                Stop();
                return;
            }

            Begin(particleTheme, PlaybackMode.Ambient, particleTheme.AmbientParticleCount);
            RenderAmbient(0f);
        }

        public void PlayBurst(ProductOpeningParticleTheme particleTheme)
        {
            if (particleTheme == null)
                throw new ArgumentNullException(nameof(particleTheme));
            if (UIFeedbackService.ReduceMotion)
            {
                Stop();
                return;
            }

            Begin(particleTheme, PlaybackMode.Burst, particleTheme.BurstParticleCount);
            RenderBurst(0f);
        }

        public void Stop()
        {
            animation?.Pause();
            animation = null;
            mode = PlaybackMode.None;
            theme = null;
            ActiveParticleCount = 0;
            root.style.display = DisplayStyle.None;
            foreach (VisualElement particle in particles)
                particle.style.display = DisplayStyle.None;
        }

        public void Dispose()
        {
            Stop();
            root.Clear();
        }

        private void Begin(
            ProductOpeningParticleTheme particleTheme,
            PlaybackMode playbackMode,
            int particleCount)
        {
            animation?.Pause();
            theme = particleTheme;
            mode = playbackMode;
            startedAt = Time.realtimeSinceStartup;
            ActiveParticleCount = particleCount;
            root.style.display = DisplayStyle.Flex;
            for (int index = 0; index < particles.Length; index++)
                particles[index].style.display = index < particleCount
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            animation = root.schedule.Execute(Tick).Every(33);
        }

        private void Tick()
        {
            if (mode == PlaybackMode.None)
                return;
            if (UIFeedbackService.ReduceMotion)
            {
                Stop();
                return;
            }

            float elapsed = (Time.realtimeSinceStartup - startedAt) * UIFeedbackService.AnimationSpeed;
            if (mode == PlaybackMode.Ambient)
            {
                RenderAmbient(elapsed);
                return;
            }

            float progress = Mathf.Clamp01(elapsed / theme.BurstDurationSeconds);
            RenderBurst(progress);
            if (progress >= 1f)
                Stop();
        }

        private void RenderAmbient(float elapsed)
        {
            float cycle = elapsed / theme.AmbientCycleSeconds;
            for (int index = 0; index < ActiveParticleCount; index++)
            {
                float offset = index / (float)ActiveParticleCount;
                float progress = Mathf.Repeat(cycle + offset, 1f);
                float phase = (progress + index * 0.173f) * Mathf.PI * 2f;
                float x = Mathf.Clamp(
                    50f + Mathf.Sin(phase) * theme.AmbientDriftPercent,
                    4f,
                    96f);
                float y = 94f - progress * 88f;
                float opacity = Mathf.Sin(progress * Mathf.PI) * 0.72f;
                float scale = 0.58f + (0.32f * (0.5f + 0.5f * Mathf.Sin(phase + 0.8f)));
                Place(particles[index], x, y, opacity, scale);
            }
        }

        private void RenderBurst(float progress)
        {
            float eased = Mathf.SmoothStep(0f, 1f, progress);
            float radius = eased * theme.BurstRadiusPercent;
            float opacity = (1f - progress) * 0.92f;
            float pulse = Mathf.Sin(progress * Mathf.PI);
            float scale = Mathf.Lerp(0.52f, theme.BurstPulseScale, pulse);
            for (int index = 0; index < ActiveParticleCount; index++)
            {
                float angle = (index / (float)ActiveParticleCount) * Mathf.PI * 2f +
                    (index % 2) * 0.14f;
                float x = 50f + Mathf.Cos(angle) * radius;
                float y = 50f + Mathf.Sin(angle) * radius;
                Place(particles[index], x, y, opacity, scale);
            }
        }

        private static void Place(
            VisualElement particle,
            float xPercent,
            float yPercent,
            float opacity,
            float scale)
        {
            particle.style.left = Length.Percent(xPercent);
            particle.style.top = Length.Percent(yPercent);
            particle.style.opacity = Mathf.Clamp01(opacity);
            particle.style.scale = new Scale(new Vector3(scale, scale, 1f));
        }
    }
}
