using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gacha.Application;
using Gacha.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Gacha.Tests.PlayMode
{
    /// <summary>
    /// Opt-in capture of existing scene tests: pass -coreNeutralSceneCapture with a graphics device.
    /// This records the real controller at existing test milestones. It does not manufacture a UI,
    /// draw cards, change locale/feedback, load a catalog, or access an inventory store.
    /// </summary>
    internal sealed class CoreNeutralSceneCapture : IDisposable
    {
        private const int Width = 720;
        private const int Height = 1600;
        private readonly UIDocument document;
        private readonly VisualElement viewport;
        private readonly PanelSettings originalPanel;
        private readonly string fixture;
        private readonly RenderTexture originalActive = RenderTexture.active;
        private PanelSettings panel;
        private RenderTexture target;
        private bool disposed;

        private CoreNeutralSceneCapture(UIDocument document, VisualElement viewport, string fixture)
        {
            this.document = document;
            this.viewport = viewport;
            this.fixture = fixture;
            originalPanel = document.panelSettings;
        }

        public static CoreNeutralSceneCapture BeginIfRequested(
            UIDocument document, VisualElement viewport, string fixture)
        {
            if (!Environment.GetCommandLineArgs().Any(argument =>
                    string.Equals(argument, "-coreNeutralSceneCapture", StringComparison.OrdinalIgnoreCase)))
                return null;
            Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null),
                "Requested scene captures require a graphics device; omit -nographics.");
            Assert.That(document, Is.Not.Null);
            Assert.That(document.panelSettings, Is.Not.Null);
            Assert.That(viewport, Is.Not.Null);
            var capture = new CoreNeutralSceneCapture(document, viewport, fixture);
            try
            {
                capture.panel = Object.Instantiate(capture.originalPanel);
                capture.panel.name = "Core Neutral Scene Capture Panel (temporary clone)";
                capture.panel.hideFlags = HideFlags.HideAndDontSave;
                // Existing tests deliberately use a 720x1600 logical viewport. Render that exact
                // fixture 1:1, rather than presenting it as the production reference-scale layout.
                capture.panel.scaleMode = PanelScaleMode.ConstantPixelSize;
                capture.panel.scale = 1f;
                capture.panel.clearColor = true;
                capture.panel.colorClearValue = Color.clear;
                capture.target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32)
                {
                    name = "Core Neutral Scene Capture Render Target",
                    hideFlags = HideFlags.HideAndDontSave,
                    antiAliasing = 1
                };
                Assert.That(capture.target.Create(), Is.True, "Scene capture RenderTexture.Create failed.");
                capture.panel.targetTexture = capture.target;
                document.panelSettings = capture.panel;
                return capture;
            }
            catch
            {
                capture.Dispose();
                throw;
            }
        }

        public IEnumerator Capture(string screen, string state, VisualElement focus,
            AsyncCardImageView requiredImage = null)
        {
            Assert.That(disposed, Is.False);
            Assert.That(focus, Is.Not.Null, screen + " requires a real scene element.");
            Assert.That(screen.All(character => character >= 'a' && character <= 'z' ||
                                                character >= '0' && character <= '9' || character == '-'), Is.True);
            for (int frame = 0; frame < 4; frame++)
                yield return null;
            float deadline = Time.realtimeSinceStartup + 5f;
            while (VisibleImages(focus).Any(image => image.ClassListContains("is-loading")) &&
                   Time.realtimeSinceStartup < deadline)
                yield return null;
            if (requiredImage != null)
            {
                while (requiredImage.State == AsyncCardImageState.Loading && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That(requiredImage.State, Is.EqualTo(AsyncCardImageState.Ready),
                    screen + ": the installed card image must be ready; this sample depends on local content.");
            }
            for (int frame = 0; frame < 2; frame++)
                yield return null;

            Assert.That(viewport.worldBound.width, Is.EqualTo(Width).Within(1f));
            Assert.That(viewport.worldBound.height, Is.EqualTo(Height).Within(1f));
            Assert.That(viewport.worldBound.xMin, Is.EqualTo(0f).Within(1f));
            Assert.That(viewport.worldBound.yMin, Is.EqualTo(0f).Within(1f));
            Assert.That(IsVisible(focus), Is.True, screen + " is not visible in the runtime test viewport.");
            VisualElement[] images = VisibleImages(focus).ToArray();
            Assert.That(images.Any(image => image.ClassListContains("is-loading")), Is.False,
                screen + ": visible local image loads did not settle before capture.");

            RenderTexture previous = RenderTexture.active;
            Texture2D imageBuffer = null;
            try
            {
                RenderTexture.active = target;
                imageBuffer = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
                imageBuffer.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                imageBuffer.Apply(false, false);
                Color32[] pixels = imageBuffer.GetPixels32();
                int opaque = 0;
                var colors = new HashSet<int>();
                for (int index = 0; index < pixels.Length; index += 4)
                {
                    Color32 pixel = pixels[index];
                    if (pixel.a < 240)
                        continue;
                    opaque++;
                    colors.Add((pixel.r << 16) | (pixel.g << 8) | pixel.b);
                }
                Assert.That(opaque, Is.GreaterThan(pixels.Length / 4 * 0.95f),
                    screen + ": transparent/partial frame is not accepted as a scene screenshot.");
                Assert.That(colors.Count, Is.GreaterThan(32), screen + ": a uniform clear frame is not UI evidence.");
                string language = LocalizationSettings.SelectedLocale?.Identifier.Code ?? "unknown";
                string name = "scene-" + screen + "-" + language + "-720x1600-runtime-fixture-simulated-insets";
                string directory = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath,
                    "../TestResults/UIRefresh"));
                Directory.CreateDirectory(directory);
                byte[] png = imageBuffer.EncodeToPNG();
                Assert.That(png.Length, Is.GreaterThan(4096));
                File.WriteAllBytes(Path.Combine(directory, name + ".png"), png);
                var metadata = new CaptureMetadata
                {
                    source = "Existing PlayMode scene/controller, cloned PanelSettings and Texture2D.ReadPixels",
                    scene = document.gameObject.scene.name,
                    screen = screen,
                    state = state,
                    fixture = fixture,
                    language = language,
                    requestedContentLanguage = ApplicationServices.IsConfigured
                        ? ApplicationServices.Languages.RequestedContentLanguageId : null,
                    width = Width,
                    height = Height,
                    layout = "Existing fixed 720x1600 logical test viewport; cloned ConstantPixelSize panel at scale 1",
                    simulatedPaddingLeftTopRightBottom = new[] { 48, 60, 12, 84 },
                    safeAreaEvidence = "Simulated padding includes base spacing; no physical Screen.safeArea or Android acceptance",
                    goldenImage = false,
                    contentEvidence = "Fixture-specific scene sample: " + fixture +
                        ". See fixture for catalog/storage/callback dependencies; not a deterministic golden image or live service acceptance",
                    reduceMotion = UIFeedbackService.ReduceMotion,
                    readyVisibleImages = images.Count(element => element.ClassListContains("is-ready")),
                    failedVisibleImages = images.Count(element => element.ClassListContains("is-failed")),
                    sampledOpaquePixels = opaque,
                    sampledDistinctColors = colors.Count
                };
                File.WriteAllText(Path.Combine(directory, name + ".json"), JsonUtility.ToJson(metadata, true));
            }
            finally
            {
                RenderTexture.active = previous;
                if (imageBuffer != null)
                    Object.DestroyImmediate(imageBuffer);
            }
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            if (document != null)
                document.panelSettings = originalPanel;
            if (panel != null)
            {
                panel.targetTexture = null;
                Object.DestroyImmediate(panel);
            }
            RenderTexture.active = originalActive;
            if (target != null)
            {
                target.Release();
                Object.DestroyImmediate(target);
            }
        }

        private IEnumerable<VisualElement> VisibleImages(VisualElement root) =>
            root.Query<VisualElement>(className: "async-card-image").ToList().Where(IsVisible);

        private bool IsVisible(VisualElement element)
        {
            if (element.worldBound.width <= 0f || element.worldBound.height <= 0f ||
                !viewport.worldBound.Overlaps(element.worldBound))
                return false;
            for (VisualElement current = element; current != null; current = current.parent)
                if (current.resolvedStyle.display == DisplayStyle.None ||
                    current.resolvedStyle.visibility == Visibility.Hidden || current.resolvedStyle.opacity <= 0f)
                    return false;
            return true;
        }

        [Serializable]
        private sealed class CaptureMetadata
        {
            public string source;
            public string scene;
            public string screen;
            public string state;
            public string fixture;
            public string language;
            public string requestedContentLanguage;
            public int width;
            public int height;
            public string layout;
            public int[] simulatedPaddingLeftTopRightBottom;
            public string safeAreaEvidence;
            public bool goldenImage;
            public string contentEvidence;
            public bool reduceMotion;
            public int readyVisibleImages;
            public int failedVisibleImages;
            public int sampledOpaquePixels;
            public int sampledDistinctColors;
        }
    }
}
