using System;

namespace Gacha.Application
{
    public sealed class ExperienceSettings
    {
        public ExperienceSettings(
            bool soundEnabled = true,
            bool reduceMotion = false,
            bool hapticsEnabled = true,
            float animationSpeed = 1f)
        {
            SoundEnabled = soundEnabled;
            ReduceMotion = reduceMotion;
            HapticsEnabled = hapticsEnabled;
            AnimationSpeed = ClampAnimationSpeed(animationSpeed);
        }

        public bool SoundEnabled { get; }
        public bool ReduceMotion { get; }
        public bool HapticsEnabled { get; }
        public float AnimationSpeed { get; }

        private static float ClampAnimationSpeed(float speed)
        {
            if (float.IsNaN(speed) || float.IsInfinity(speed))
                return 1f;
            return Math.Max(0.5f, Math.Min(2f, speed));
        }
    }

    public interface IExperienceSettingsStore
    {
        ExperienceSettings Load();
        void Save(ExperienceSettings settings);
    }

    public sealed class ExperienceSettingsUpdateResult
    {
        private ExperienceSettingsUpdateResult(bool succeeded, string errorMessage)
        {
            Succeeded = succeeded;
            ErrorMessage = errorMessage;
        }

        public bool Succeeded { get; }
        public string ErrorMessage { get; }

        public static ExperienceSettingsUpdateResult Success()
        {
            return new ExperienceSettingsUpdateResult(true, null);
        }

        public static ExperienceSettingsUpdateResult Failure(string errorMessage)
        {
            return new ExperienceSettingsUpdateResult(
                false,
                string.IsNullOrWhiteSpace(errorMessage) ? "Unable to save settings." : errorMessage.Trim());
        }
    }

    public sealed class ExperienceSettingsService
    {
        private readonly IExperienceSettingsStore store;

        public ExperienceSettingsService(IExperienceSettingsStore store)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            try
            {
                Current = store.Load() ?? new ExperienceSettings();
            }
            catch
            {
                Current = new ExperienceSettings();
            }
        }

        public ExperienceSettings Current { get; private set; }
        public event Action<ExperienceSettings> Changed;

        public ExperienceSettingsUpdateResult SetSoundEnabled(bool enabled)
        {
            return Update(new ExperienceSettings(
                enabled,
                Current.ReduceMotion,
                Current.HapticsEnabled,
                Current.AnimationSpeed));
        }

        public ExperienceSettingsUpdateResult SetReduceMotion(bool enabled)
        {
            return Update(new ExperienceSettings(
                Current.SoundEnabled,
                enabled,
                Current.HapticsEnabled,
                Current.AnimationSpeed));
        }

        public ExperienceSettingsUpdateResult SetHapticsEnabled(bool enabled)
        {
            return Update(new ExperienceSettings(
                Current.SoundEnabled,
                Current.ReduceMotion,
                enabled,
                Current.AnimationSpeed));
        }

        public ExperienceSettingsUpdateResult SetAnimationSpeed(float speed)
        {
            return Update(new ExperienceSettings(
                Current.SoundEnabled,
                Current.ReduceMotion,
                Current.HapticsEnabled,
                speed));
        }

        public ExperienceSettingsUpdateResult Apply(ExperienceSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            return Update(new ExperienceSettings(
                settings.SoundEnabled,
                settings.ReduceMotion,
                settings.HapticsEnabled,
                settings.AnimationSpeed));
        }

        private ExperienceSettingsUpdateResult Update(ExperienceSettings next)
        {
            if (Matches(Current, next))
                return ExperienceSettingsUpdateResult.Success();

            try
            {
                store.Save(next);
            }
            catch (Exception exception)
            {
                return ExperienceSettingsUpdateResult.Failure(exception.Message);
            }

            Current = next;
            Changed?.Invoke(Current);
            return ExperienceSettingsUpdateResult.Success();
        }

        private static bool Matches(ExperienceSettings left, ExperienceSettings right)
        {
            return left.SoundEnabled == right.SoundEnabled &&
                   left.ReduceMotion == right.ReduceMotion &&
                   left.HapticsEnabled == right.HapticsEnabled &&
                   Math.Abs(left.AnimationSpeed - right.AnimationSpeed) < 0.0001f;
        }
    }
}
