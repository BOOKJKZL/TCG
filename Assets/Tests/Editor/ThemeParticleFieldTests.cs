using Gacha.Presentation;
using NUnit.Framework;
using UnityEngine.UIElements;

public class ThemeParticleFieldTests
{
    [TearDown]
    public void TearDown()
    {
        UIFeedbackService.Configure(false, true, 1f);
    }

    [Test]
    public void Field_ReusesBoundedElementsForAmbientAndBurstEffects()
    {
        var root = new VisualElement();
        var field = new ThemeParticleField(root);
        var theme = new ProductOpeningParticleTheme(5, 9, 3f, 8f, 0.6f, 36f, 1.3f);

        Assert.That(root.childCount, Is.EqualTo(ThemeParticleField.MaximumParticleCount));

        field.PlayAmbient(theme);
        Assert.That(field.IsRunning, Is.True);
        Assert.That(field.ActiveParticleCount, Is.EqualTo(5));

        field.PlayBurst(theme);
        Assert.That(field.IsRunning, Is.True);
        Assert.That(field.ActiveParticleCount, Is.EqualTo(9));
        Assert.That(root.childCount, Is.EqualTo(ThemeParticleField.MaximumParticleCount));

        field.Stop();
        Assert.That(field.IsRunning, Is.False);
        Assert.That(field.ActiveParticleCount, Is.Zero);
        field.Dispose();
        Assert.That(root.childCount, Is.Zero);
    }

    [Test]
    public void Field_DoesNotStartWhenReduceMotionIsEnabled()
    {
        var root = new VisualElement();
        var field = new ThemeParticleField(root);
        UIFeedbackService.Configure(true, true, 1f);

        field.PlayAmbient(ProductOpeningParticleTheme.Default);
        Assert.That(field.IsRunning, Is.False);
        Assert.That(field.ActiveParticleCount, Is.Zero);

        field.PlayBurst(ProductOpeningParticleTheme.Default);
        Assert.That(field.IsRunning, Is.False);
        Assert.That(field.ActiveParticleCount, Is.Zero);
        field.Dispose();
    }
}
