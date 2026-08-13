using System;
using System.Linq;
using Gacha.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

public class InteractivePackViewTests
{
    [Test]
    public void MeshBuilder_ProducesFiniteBoundedTexturedHalves()
    {
        var host = new Rect(0f, 0f, 520f, 520f);
        Rect pack = InteractivePackMeshBuilder.ResolvePackRect(host, 0.7f);
        var left = new Vertex[InteractivePackMeshBuilder.VerticesPerHalf];
        var right = new Vertex[InteractivePackMeshBuilder.VerticesPerHalf];
        var leftIndices = new ushort[InteractivePackMeshBuilder.IndicesPerHalf];
        var rightIndices = new ushort[InteractivePackMeshBuilder.IndicesPerHalf];

        InteractivePackMeshBuilder.FillHalf(
            left, leftIndices, pack, 62f, 1f, false, true, new Color32(255, 255, 255, 255));
        InteractivePackMeshBuilder.FillHalf(
            right, rightIndices, pack, 62f, 1f, true, true, new Color32(255, 255, 255, 255));

        Assert.That(left.Length + right.Length, Is.EqualTo(36));
        Assert.That(leftIndices.Length + rightIndices.Length, Is.EqualTo(96));
        foreach (Vertex vertex in left.Concat(right))
        {
            Assert.That(float.IsNaN(vertex.position.x) || float.IsInfinity(vertex.position.x), Is.False);
            Assert.That(float.IsNaN(vertex.position.y) || float.IsInfinity(vertex.position.y), Is.False);
            Assert.That(host.Contains((Vector2)vertex.position), Is.True, vertex.position.ToString());
            Assert.That(vertex.uv.x, Is.InRange(0f, 1f));
            Assert.That(vertex.uv.y, Is.InRange(0f, 1f));
        }
        Assert.That(left.Max(vertex => vertex.position.x),
            Is.LessThan(right.Min(vertex => vertex.position.x)));
        Assert.That(leftIndices.All(index => index < left.Length), Is.True);
        Assert.That(rightIndices.All(index => index < right.Length), Is.True);
    }

    [Test]
    public void BackFace_ReversesHorizontalUvsAndKeepsGeometryFinite()
    {
        Rect pack = InteractivePackMeshBuilder.ResolvePackRect(new Rect(0f, 0f, 400f, 500f), 0.7f);
        var vertices = new Vertex[InteractivePackMeshBuilder.VerticesPerHalf];
        var indices = new ushort[InteractivePackMeshBuilder.IndicesPerHalf];

        InteractivePackMeshBuilder.FillHalf(
            vertices, indices, pack, 180f, 0f, false, false, new Color32(255, 255, 255, 255));

        Assert.That(vertices[0].uv.x, Is.EqualTo(1f));
        Assert.That(vertices[1].uv.x, Is.EqualTo(0.5f));
        Assert.That(vertices.All(vertex =>
            !float.IsNaN(vertex.position.x) && !float.IsInfinity(vertex.position.x)), Is.True);
    }

    [Test]
    public void Carousel_HasStableFiveSlotsAndOneShotAccessibleAcceptance()
    {
        var presentation = new ProductOpeningPackPresentation(null);
        var carousel = new InteractivePackCarousel(presentation, true);
        try
        {
            int accepted = 0;
            carousel.TearAccepted += () => accepted++;
            Assert.That(carousel.Root.Q<VisualElement>("interactive-pack-carousel-rail").childCount,
                Is.EqualTo(5));
            Assert.That(carousel.Root.Query<Button>().ToList(), Is.Empty);
            Assert.That(carousel.SelectedPack.AcceptFromAccessibleAction(), Is.True);
            Assert.That(carousel.SelectedPack.AcceptFromAccessibleAction(), Is.False);
            Assert.That(accepted, Is.EqualTo(1));
            Assert.That(carousel.Select(4), Is.True);
            Assert.That(carousel.SelectedIndex, Is.EqualTo(4));
            Assert.That(carousel.Root.Q<Label>("interactive-pack-carousel-position").text,
                Is.EqualTo("5 / 10"));
        }
        finally
        {
            carousel.Dispose();
        }
    }

    [Test]
    public void View_DisableCancelAndDisposePreserveRenderHierarchy()
    {
        var view = new InteractivePackView(new ProductOpeningPackPresentation(null), true);
        int initialChildren = view.childCount;

        view.SetInteractionEnabled(false);
        view.SetInteractionEnabled(true);
        view.ResetInteraction(180f);
        view.Dispose();

        Assert.That(view.childCount, Is.EqualTo(initialChildren));
        Assert.That(view.IsInteractionEnabled, Is.False);
        Assert.That(view.AcceptFromAccessibleAction(), Is.False);
    }
}
