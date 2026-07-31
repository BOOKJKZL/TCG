using System;
using Gacha.Application;
using NUnit.Framework;

public class ExperienceSettingsApplicationTests
{
    private sealed class MemoryStore : IExperienceSettingsStore
    {
        public ExperienceSettings Stored { get; set; }
        public bool FailSaves { get; set; }
        public int SaveCount { get; private set; }

        public ExperienceSettings Load()
        {
            return Stored;
        }

        public void Save(ExperienceSettings settings)
        {
            SaveCount++;
            if (FailSaves)
                throw new InvalidOperationException("Disk unavailable");
            Stored = settings;
        }
    }

    [Test]
    public void Constructor_LoadsAndNormalizesStoredSettings()
    {
        var store = new MemoryStore
        {
            Stored = new ExperienceSettings(false, true, false, 9f)
        };

        var service = new ExperienceSettingsService(store);

        Assert.That(service.Current.SoundEnabled, Is.False);
        Assert.That(service.Current.ReduceMotion, Is.True);
        Assert.That(service.Current.HapticsEnabled, Is.False);
        Assert.That(service.Current.AnimationSpeed, Is.EqualTo(2f));
    }

    [Test]
    public void Updates_SaveBeforePublishingChangedState()
    {
        var store = new MemoryStore { Stored = new ExperienceSettings() };
        var service = new ExperienceSettingsService(store);
        ExperienceSettings published = null;
        service.Changed += value => published = value;

        ExperienceSettingsUpdateResult result = service.SetReduceMotion(true);

        Assert.That(result.Succeeded, Is.True);
        Assert.That(store.SaveCount, Is.EqualTo(1));
        Assert.That(store.Stored.ReduceMotion, Is.True);
        Assert.That(service.Current, Is.SameAs(store.Stored));
        Assert.That(published, Is.SameAs(store.Stored));
    }

    [Test]
    public void FailedSave_RollsBackAndDoesNotPublishChange()
    {
        var original = new ExperienceSettings(true, false, true, 1f);
        var store = new MemoryStore { Stored = original, FailSaves = true };
        var service = new ExperienceSettingsService(store);
        int changeCount = 0;
        service.Changed += _ => changeCount++;

        ExperienceSettingsUpdateResult result = service.SetAnimationSpeed(1.5f);

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("Disk unavailable"));
        Assert.That(service.Current, Is.SameAs(original));
        Assert.That(service.Current.AnimationSpeed, Is.EqualTo(1f));
        Assert.That(changeCount, Is.Zero);
    }

    [Test]
    public void Apply_StoresWholeRecoverySnapshotInOneUpdate()
    {
        var store = new MemoryStore { Stored = new ExperienceSettings() };
        var service = new ExperienceSettingsService(store);

        ExperienceSettingsUpdateResult result = service.Apply(
            new ExperienceSettings(false, true, false, 1.5f));

        Assert.That(result.Succeeded, Is.True);
        Assert.That(store.SaveCount, Is.EqualTo(1));
        Assert.That(service.Current.SoundEnabled, Is.False);
        Assert.That(service.Current.ReduceMotion, Is.True);
        Assert.That(service.Current.HapticsEnabled, Is.False);
        Assert.That(service.Current.AnimationSpeed, Is.EqualTo(1.5f));
    }
}
