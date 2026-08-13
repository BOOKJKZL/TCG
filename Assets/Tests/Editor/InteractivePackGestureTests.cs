using Gacha.Presentation;
using NUnit.Framework;
using UnityEngine;

public class InteractivePackGestureTests
{
    [Test]
    public void MobileSinglePointer_RotatesButNeverTears()
    {
        var gesture = new InteractivePackGesture(true);

        Assert.That(gesture.PointerDown(7, new Vector2(0.5f, 0.5f)), Is.True);
        Assert.That(gesture.PointerMove(7, new Vector2(0.9f, 0.5f)), Is.False);

        Assert.That(gesture.Phase, Is.EqualTo(InteractivePackGesturePhase.Rotating));
        Assert.That(gesture.RotationDegrees, Is.EqualTo(72f).Within(0.01f));
        Assert.That(gesture.TearProgress, Is.Zero);
        gesture.PointerUp(7);
        Assert.That(gesture.Phase, Is.EqualTo(InteractivePackGesturePhase.Idle));
        Assert.That(gesture.RotationDegrees, Is.Zero);
    }

    [Test]
    public void OppositeSidePointers_AcceptTearExactlyOnce()
    {
        var gesture = new InteractivePackGesture(true);
        Assert.That(gesture.PointerDown(11, new Vector2(0.46f, 0.5f)), Is.True);
        Assert.That(gesture.PointerDown(29, new Vector2(0.54f, 0.5f)), Is.True);

        Assert.That(gesture.PointerMove(29, new Vector2(0.70f, 0.5f)), Is.False);
        Assert.That(gesture.PointerMove(11, new Vector2(0.30f, 0.5f)), Is.True);
        Assert.That(gesture.Phase, Is.EqualTo(InteractivePackGesturePhase.Accepted));
        Assert.That(gesture.TearProgress, Is.EqualTo(1f));

        Assert.That(gesture.PointerMove(29, new Vector2(0.9f, 0.5f)), Is.False);
        gesture.PointerUp(11);
        gesture.PointerUp(29);
        Assert.That(gesture.IsAccepted, Is.True);
        Assert.That(gesture.PointerDown(42, Vector2.one * 0.5f), Is.False);
    }

    [Test]
    public void SameSidePointersAndThirdPointer_CannotStartTear()
    {
        var gesture = new InteractivePackGesture(true);
        Assert.That(gesture.PointerDown(1, new Vector2(0.15f, 0.5f)), Is.True);
        Assert.That(gesture.PointerDown(2, new Vector2(0.35f, 0.5f)), Is.True);
        Assert.That(gesture.PointerDown(3, new Vector2(0.8f, 0.5f)), Is.False);

        Assert.That(gesture.PointerMove(1, new Vector2(0f, 0.5f)), Is.False);
        Assert.That(gesture.PointerMove(2, new Vector2(1f, 0.5f)), Is.False);
        Assert.That(gesture.IsAccepted, Is.False);
        Assert.That(gesture.TearProgress, Is.Zero);
    }

    [Test]
    public void CancelAndPointerLoss_ResetTransientInput()
    {
        var gesture = new InteractivePackGesture(true);
        gesture.PointerDown(8, new Vector2(0.45f, 0.5f));
        gesture.PointerDown(3, new Vector2(0.55f, 0.5f));
        gesture.PointerMove(8, new Vector2(0.35f, 0.5f));
        Assert.That(gesture.Phase, Is.EqualTo(InteractivePackGesturePhase.Tearing));

        gesture.PointerUp(3);
        Assert.That(gesture.Phase, Is.EqualTo(InteractivePackGesturePhase.Idle));
        Assert.That(gesture.ActivePointerCount, Is.EqualTo(1));
        Assert.That(gesture.TearProgress, Is.Zero);
        gesture.Cancel();
        Assert.That(gesture.ActivePointerCount, Is.Zero);
        Assert.That(gesture.Phase, Is.EqualTo(InteractivePackGesturePhase.Idle));
    }

    [Test]
    public void DesktopSeamDrag_AcceptsWithoutSecondPointer()
    {
        var gesture = new InteractivePackGesture(false);
        gesture.PointerDown(0, new Vector2(0.47f, 0.5f));

        Assert.That(gesture.PointerMove(0, new Vector2(0.20f, 0.5f)), Is.True);
        Assert.That(gesture.IsAccepted, Is.True);
    }

    [Test]
    public void Constructor_RejectsNonFiniteOrUnsafeThresholds()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() =>
            new InteractivePackGesture(true, acceptanceThreshold: float.NaN));
        Assert.Throws<System.ArgumentOutOfRangeException>(() =>
            new InteractivePackGesture(true, dualPointerPullNormalized: 0f));
    }
}
