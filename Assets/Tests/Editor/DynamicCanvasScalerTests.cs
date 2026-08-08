using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

public class DynamicCanvasScalerTests
{
    [Test]
    public void AdjustCanvasScaler_UsesExistingReferenceWhenGameManagerIsMissing()
    {
        GameObject canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));

        try
        {
            CanvasScaler canvasScaler = canvasObject.GetComponent<CanvasScaler>();
            canvasScaler.referenceResolution = new Vector2(1000f, 2000f);
            DynamicCanvasScaler dynamicScaler = canvasObject.AddComponent<DynamicCanvasScaler>();

            Assert.DoesNotThrow(dynamicScaler.AdjustCanvasScaler);
            Assert.That(canvasScaler.referenceResolution, Is.EqualTo(new Vector2(1000f, 2000f)));
            Assert.That(canvasScaler.screenMatchMode, Is.EqualTo(CanvasScaler.ScreenMatchMode.MatchWidthOrHeight));
            Assert.That(canvasScaler.matchWidthOrHeight, Is.EqualTo(0.5f));
        }
        finally
        {
            Object.DestroyImmediate(canvasObject);
        }
    }

    [TestCase(0f, 0f, 1f, 1f, true)]
    [TestCase(0.1f, 0f, 0.8f, 1f, false)]
    [TestCase(0f, 0.1f, 1f, 0.8f, false)]
    public void ResolutionManager_OnlyAcceptsFullScreenViewport(
        float x,
        float y,
        float width,
        float height,
        bool expected)
    {
        Assert.That(ResolutionManager.IsFullScreen(new Rect(x, y, width, height)), Is.EqualTo(expected));
    }

    [Test]
    public void ResolutionManager_RestoresCameraToFullScreen()
    {
        GameObject cameraObject = new GameObject("Camera", typeof(Camera));
        GameObject managerObject = new GameObject("Resolution Manager");
        try
        {
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.rect = new Rect(0.1f, 0.2f, 0.8f, 0.6f);
            ResolutionManager manager = managerObject.AddComponent<ResolutionManager>();

            manager.ApplyFullScreenViewports();

            Assert.That(ResolutionManager.IsFullScreen(camera.rect), Is.True);
        }
        finally
        {
            Object.DestroyImmediate(managerObject);
            Object.DestroyImmediate(cameraObject);
        }
    }

    [Test]
    public void ResolutionManager_NormalizesCameraCreatedAfterManager()
    {
        GameObject managerObject = new GameObject("Resolution Manager", typeof(ResolutionManager));
        GameObject cameraObject = new GameObject("Late Camera", typeof(Camera));
        try
        {
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.rect = new Rect(0.2f, 0.1f, 0.6f, 0.8f);

            ResolutionManager.NormalizeCamera(camera);

            Assert.That(ResolutionManager.IsFullScreen(camera.rect), Is.True);
        }
        finally
        {
            Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(managerObject);
        }
    }

    [Test]
    public void ResolutionManager_HooksBothSrpAndBuiltInCameraRendering()
    {
        string source = File.ReadAllText(
            "Assets/Scripts/001_Baisc/004_Screen/ResolutionManager.cs");

        Assert.That(source, Does.Contain(
            "RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;"));
        Assert.That(source, Does.Contain(
            "RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;"));
        Assert.That(source, Does.Contain("Camera.onPreCull += NormalizeCamera;"));
        Assert.That(source, Does.Contain("Camera.onPreCull -= NormalizeCamera;"));
    }
}
