using Gacha.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class UiToolkitSafeAreaTests
{
    [Test]
    public void ConvertsPortraitInsetsThroughPanelMapping()
    {
        SafeAreaInsets insets = UiToolkitSafeArea.CalculateInsets(
            new Rect(0f, 72f, 720f, 1480f),
            720,
            1600,
            point => point / 0.72f);

        Assert.That(insets.Left, Is.EqualTo(0f).Within(0.01f));
        Assert.That(insets.Top, Is.EqualTo(66.67f).Within(0.01f));
        Assert.That(insets.Right, Is.EqualTo(0f).Within(0.01f));
        Assert.That(insets.Bottom, Is.EqualTo(100f).Within(0.01f));
    }

    [Test]
    public void PreservesAllAsymmetricCutoutInsets()
    {
        SafeAreaInsets insets = UiToolkitSafeArea.CalculateInsets(
            new Rect(36f, 96f, 1044f, 2184f),
            1080,
            2400,
            point => point);

        Assert.That(insets.Left, Is.EqualTo(36f).Within(0.01f));
        Assert.That(insets.Top, Is.EqualTo(120f).Within(0.01f));
        Assert.That(insets.Right, Is.Zero);
        Assert.That(insets.Bottom, Is.EqualTo(96f).Within(0.01f));
    }

    [Test]
    public void UsesNonUniformOffsetPanelMappingWithoutAssumingScreenScale()
    {
        SafeAreaInsets insets = UiToolkitSafeArea.CalculateInsets(
            new Rect(10f, 20f, 960f, 1940f),
            1000,
            2000,
            point => new Vector2(point.x * 2f + 17f, point.y * 3f + 23f));

        Assert.That(insets.Left, Is.EqualTo(20f).Within(0.01f));
        Assert.That(insets.Top, Is.EqualTo(120f).Within(0.01f));
        Assert.That(insets.Right, Is.EqualTo(60f).Within(0.01f));
        Assert.That(insets.Bottom, Is.EqualTo(60f).Within(0.01f));
    }

    [TestCase(1080, 2160)]
    [TestCase(1080, 2400)]
    [TestCase(720, 1600)]
    public void FullSafeAreaProducesNoInset(int width, int height)
    {
        SafeAreaInsets insets = UiToolkitSafeArea.CalculateInsets(
            new Rect(0f, 0f, width, height),
            width,
            height,
            point => point * 1.25f + new Vector2(13f, 29f));

        Assert.That(insets.Left, Is.Zero);
        Assert.That(insets.Top, Is.Zero);
        Assert.That(insets.Right, Is.Zero);
        Assert.That(insets.Bottom, Is.Zero);
    }

    [Test]
    public void InvalidViewportOrMappingFailsClosedWithoutInsets()
    {
        SafeAreaInsets invalidViewport = UiToolkitSafeArea.CalculateInsets(
            new Rect(10f, 10f, 100f, 100f),
            0,
            0,
            point => point);
        SafeAreaInsets invalidMapping = UiToolkitSafeArea.CalculateInsets(
            new Rect(10f, 10f, 100f, 100f),
            1000,
            2000,
            null);

        Assert.That(Sum(invalidViewport), Is.Zero);
        Assert.That(Sum(invalidMapping), Is.Zero);
    }

    [Test]
    public void NarrowPhysicalOrPanelWidthUsesCompactLayout()
    {
        Assert.That(UiToolkitSafeArea.ShouldUseCompactLayout(1000f, 720f), Is.True);
        Assert.That(UiToolkitSafeArea.ShouldUseCompactLayout(966f, 1044f), Is.True);
        Assert.That(UiToolkitSafeArea.ShouldUseCompactLayout(1000f, 1080f), Is.False);
    }

    [Test]
    public void AttachIsIdempotentAndDisposeAllowsCleanReattach()
    {
        VisualElement root = new VisualElement();
        UiToolkitSafeAreaBinding first = UiToolkitSafeArea.Attach(root);
        UiToolkitSafeAreaBinding duplicate = UiToolkitSafeArea.Attach(root);

        Assert.That(duplicate, Is.SameAs(first));
        Assert.That(root.ClassListContains("safe-area-bound"), Is.True);
        Assert.That(first.IsActive, Is.True);

        first.Suspend();
        Assert.That(first.IsActive, Is.False);
        first.Resume();
        Assert.That(first.IsActive, Is.True);

        first.Dispose();
        Assert.That(first.IsDisposed, Is.True);
        Assert.That(root.ClassListContains("safe-area-bound"), Is.False);

        UiToolkitSafeAreaBinding replacement = UiToolkitSafeArea.Attach(root);
        Assert.That(replacement, Is.Not.SameAs(first));
        replacement.Dispose();
    }

    private static float Sum(SafeAreaInsets insets)
    {
        return insets.Left + insets.Top + insets.Right + insets.Bottom;
    }
}
