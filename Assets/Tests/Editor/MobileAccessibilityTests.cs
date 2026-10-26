using System;
using Gacha.Presentation;
using NUnit.Framework;
using UnityEngine;

public sealed class MobileAccessibilityTests
{
    [Test]
    public void ModalProjectionRestoresOnlyAnExistingSourceInTheCurrentScreen()
    {
        var model = new MobileAccessibilityModel();
        var home = Layer("home", null, true, 1);
        var exit = Layer("home/exit", "home", false, 2);
        var title = Node("home/title", "home");
        var source = Node("home/settings", "home");
        var cancel = Node("home/exit/cancel", "home/exit");
        var layers = new[] { home, exit };
        var nodes = new[] { title, source, cancel };
        model.Update(layers, nodes);
        model.RememberFocus(source.Id);
        exit.IsVisible = true;
        Assert.That(model.Update(layers, nodes), Is.True);
        Assert.That(model.Nodes.Count, Is.EqualTo(1));
        Assert.That(model.RequestedFocusId, Is.EqualTo(cancel.Id));
        exit.IsVisible = false;
        model.Update(layers, nodes);
        Assert.That(model.RequestedFocusId, Is.EqualTo(source.Id));
        exit.IsVisible = true;
        model.Update(layers, nodes);
        exit.IsVisible = false;
        model.Update(layers, new[] { title, cancel });
        Assert.That(model.RequestedFocusId, Is.EqualTo(title.Id), "A removed/hidden source must not receive focus.");
        var settings = Layer("settings", null, true, 3);
        model.Update(new[] { home, exit, settings }, new[] { title, source, Node("settings/title", "settings") });
        Assert.That(model.RequestedFocusId, Is.EqualTo("settings/title"));
    }

    [Test]
    public void HighestScreenAndNestedModalExcludeBackgroundSemantics()
    {
        var model = new MobileAccessibilityModel();
        var home = Layer("home", null, true, 1);
        var setup = Layer("setup", null, true, 2);
        setup.Priority = 100;
        var modal = Layer("setup/review", "setup", true, 3);
        var nested = Layer("setup/review/confirm", "setup/review", true, 4);
        model.Update(new[] { home, setup, modal, nested }, new[]
        {
            Node("home/a", "home"), Node("setup/a", "setup"),
            Node("setup/review/a", "setup/review"), Node("setup/review/confirm/a", "setup/review/confirm")
        });
        Assert.That(model.ActiveScreenId, Is.EqualTo("setup"));
        Assert.That(model.ActiveScopeId, Is.EqualTo("setup/review/confirm"));
        Assert.That(model.Nodes.Count, Is.EqualTo(1));
    }

    [Test]
    public void OrdinaryTextUpdateDoesNotRequestFocusAgainAndDuplicateIdentityIsRejected()
    {
        var model = new MobileAccessibilityModel();
        var layers = new[] { Layer("home", null, true, 1) };
        var node = Node("home/a", "home");
        model.Update(layers, new[] { node });
        node.Label = "设置";
        Assert.That(model.Update(layers, new[] { node }), Is.False);
        Assert.That(model.RequestedFocusId, Is.Null);
        Assert.Throws<ArgumentException>(() => model.Update(layers, new[] { node, Node("home/a", "home") }));
    }

    [Test]
    public void ScreenFrameInvertsMeasuredScaleAndOffsetWithoutTreatingPanelUnitsAsPixels()
    {
        bool valid = MobileAccessibilityGeometry.TryScreenFrame(new Rect(56, 100, 100, 30),
            new Vector2(1080, 2400), point => point / 2f + new Vector2(20, 40), out Rect frame);
        Assert.That(valid, Is.True);
        Assert.That(frame, Is.EqualTo(new Rect(72, 120, 200, 60)));
    }

    [Test]
    public void UnsupportedAndInvalidProjectionCannotProduceAFalseNativeFrame()
    {
        var bounds = new Rect(1, 2, 30, 40);
        var size = new Vector2(1080, 2400);
        Assert.That(MobileAccessibilityGeometry.TryScreenFrame(bounds, size,
            p => new Vector2(p.x + p.y * 0.1f, p.y), out _), Is.False, "Sheared/world-space mapping needs a dedicated adapter.");
        Assert.That(MobileAccessibilityGeometry.TryScreenFrame(bounds, size,
            p => new Vector2(float.NaN, p.y), out _), Is.False);
        Assert.That(MobileAccessibilityGeometry.TryScreenFrame(bounds, size,
            p => new Vector2(p.x * p.x, p.y), out _), Is.False);
    }

    [TestCase(23, false)]
    [TestCase(25, false)]
    [TestCase(26, true)]
    [TestCase(35, true)]
    public void AndroidNativeSupportStartsAtApi26(int api, bool supported) =>
        Assert.That(MobileAccessibilityNativeHierarchy.SupportsAndroidApi(api), Is.EqualTo(supported));

    private static MobileAccessibilityLayer Layer(string id, string parent, bool visible, long order) =>
        new MobileAccessibilityLayer { Id = id, ParentId = parent, IsModal = parent != null,
            IsVisible = visible, Order = order };
    private static MobileAccessibilityNodeData Node(string id, string scope) =>
        new MobileAccessibilityNodeData { Id = id, ScopeId = scope, Label = "Readable label" };
}
