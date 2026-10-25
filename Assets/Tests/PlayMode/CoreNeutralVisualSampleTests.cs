using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Gacha.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Gacha.Tests.PlayMode
{
    /// <summary>
    /// Actual runtime UI Toolkit samples, not reconstructed mock screens. The cloned production
    /// panel keeps its reference scale and fonts. Insets are deliberately simulated in panel units;
    /// these images do not validate Screen.safeArea mapping, physical density, or Android rendering.
    /// Run with a graphics device (omit -nographics). No WaitForEndOfFrame is used in batch tests.
    /// </summary>
    public sealed class CoreNeutralVisualSampleTests
    {
        private static readonly SampleViewport Narrow = new SampleViewport(720, 1600, 0, 48, 0, 72);

        [UnityTest]
        public IEnumerator Home_RealPresenter_RendersThreeLocalesAtNarrowPortrait()
        {
            yield return LocalizationSettings.InitializationOperation;
            foreach (string locale in new[] { "en", "zh", "ja" })
                yield return CaptureHome(locale, Narrow);
        }

        [UnityTest]
        public IEnumerator Home_RealPresenter_RendersStandardTallAndAsymmetricPortrait()
        {
            yield return LocalizationSettings.InitializationOperation;
            foreach (SampleViewport viewport in new[]
                     {
                         new SampleViewport(1080, 2160, 0, 80, 0, 96),
                         new SampleViewport(1080, 2400, 0, 96, 0, 120),
                         new SampleViewport(1080, 2400, 36, 120, 0, 96)
                     })
                yield return CaptureHome("en", viewport);
        }

        [UnityTest]
        public IEnumerator SharedStates_RealPresenters_RenderOfflineEmptyAndProgressInThreeLocales()
        {
            yield return LocalizationSettings.InitializationOperation;
            foreach (string locale in new[] { "en", "zh", "ja" })
                yield return CaptureSharedStates(locale);
        }

        private static IEnumerator CaptureHome(string locale, SampleViewport viewport)
        {
            using (var sample = new RenderSample(locale, viewport))
            {
                var presenter = new MobileHomePresenter(sample.Host, () => { }, () => { }, () => { }, () => { });
                sample.Own(presenter);
                sample.Attach(presenter.Document, presenter.Shell);
                yield return sample.SettleLayout();

                Assert.That(presenter.Title.text, Is.EqualTo(CardUiText.Get("home.title")));
                Assert.That(presenter.Title.text, Is.Not.Empty);
                Assert.That(presenter.PrimaryNavigation.Count, Is.EqualTo(4));
                Assert.That(presenter.Title.resolvedStyle.whiteSpace, Is.EqualTo(WhiteSpace.Normal));
                AssertColor(presenter.Shell.Root.resolvedStyle.backgroundColor, "#F7F8FA", "canvas");
                AssertColor(presenter.TopBar.Root.resolvedStyle.backgroundColor, "#FFFFFF", "top bar");
                AssertColor(presenter.Title.resolvedStyle.color, "#14171A", "home title");
                AssertColor(presenter.Body.resolvedStyle.color, "#556170", "home body");
                AssertColor(presenter.GachaFeatureAction.Root.resolvedStyle.backgroundColor, "#2563EB", "primary action");
                AssertColor(presenter.GachaFeatureAction.Label.resolvedStyle.color, "#FFFFFF", "primary action label");

                Rect safeContent = SafeContentRect(presenter.Shell.SafeArea);
                AssertContained(safeContent, presenter.TopBar.Root.worldBound, "top bar");
                VisualElement settingsUtility = presenter.TopBar.Root.Q<VisualElement>("home-settings-utility-action");
                Assert.That(settingsUtility, Is.Not.Null, "The top bar settings action must remain reachable.");
                AssertContained(presenter.TopBar.Root.worldBound, settingsUtility.worldBound, "top bar settings action");
                AssertActionLabelContained(settingsUtility, "top bar settings action");
                Assert.That(presenter.Hero.worldBound.yMin,
                    Is.GreaterThanOrEqualTo(presenter.TopBar.Root.worldBound.yMax - 1f),
                    "The home hero must start below the entire top bar, including its settings action.");

                foreach (MobileActionControl action in new[]
                         {
                             presenter.GachaFeatureAction,
                             presenter.CollectionFeatureAction,
                             presenter.ContentFeatureAction,
                             presenter.SettingsFeatureAction
                         })
                    AssertActionLabelContained(action.Root, action.Root.name);

                AssertContained(safeContent, presenter.PrimaryNavigation.BottomNavigation.Root.worldBound, "navigation");
                foreach (MobileDestination destination in new[] { MobileDestination.Home, MobileDestination.Gacha,
                             MobileDestination.Collection, MobileDestination.Content })
                {
                    MobileActionControl action = presenter.PrimaryNavigation.GetAction(destination);
                    AssertContained(safeContent, action.Root.worldBound, destination.ToString());
                    Assert.That(action.Root.resolvedStyle.height,
                        Is.GreaterThanOrEqualTo(MobileUiTokens.MinimumTouchTarget - 0.5f));
                }

                sample.Capture("home", "MobileHomePresenter; no catalog, inventory, network, or saved-game fixture",
                    presenter.Title.text, presenter.Shell.Root, presenter.Title, presenter.GachaFeatureAction.Root);
            }
        }

        private static IEnumerator CaptureSharedStates(string locale)
        {
            using (var sample = new RenderSample(locale, Narrow))
            {
                var document = sample.Host.AddComponent<UIDocument>();
                document.panelSettings = Resources.Load<PanelSettings>("UI/Collection Panel Settings");
                var shell = new MobilePageShell("core-neutral-shared-state-sample");
                sample.Own(shell);
                document.rootVisualElement.Add(shell.Root);
                var topBar = new MobileTopBar(CardUiText.Get("common.action.manage_content"));
                shell.HeaderSlot.Add(topBar.Root);
                var scroll = new ScrollView(ScrollViewMode.Vertical);
                scroll.style.flexGrow = 1f;
                scroll.style.minHeight = 0f;
                shell.ContentSlot.Add(scroll);

                var error = new MobileErrorState(() => { }, () => { }, () => { }, () => { });
                sample.Own(error);
                scroll.Add(error.Root);
                error.Presenter.Show(PlayerUiErrorMapper.Create(PlayerUiErrorCode.Offline));
                var empty = new MobileEmptyState(
                    CardUiText.Get("player_error.not_installed.title"),
                    CardUiText.Get("player_error.not_installed.body"));
                var manageContent = new MobileActionControl("sample-empty-manage-content",
                    CardUiText.Get("common.action.manage_content"), () => { }, MobileActionTone.Primary);
                sample.Own(manageContent);
                empty.AddAction(manageContent);
                scroll.Add(empty.Root);
                var progress = new MobileProgressView(CardUiText.Get("common.status.loading"));
                progress.Set(42f);
                scroll.Add(progress.Root);

                sample.Attach(document, shell);
                yield return sample.SettleLayout();
                Assert.That(error.Presenter.IsVisible, Is.True);
                Assert.That(error.Presenter.IsAnimating, Is.False);
                Assert.That(error.Title.text, Is.EqualTo(PlayerUiErrorText.Title(error.Presenter.Current)));
                Assert.That(error.Retry.Label.text, Is.EqualTo(CardUiText.Get("common.action.retry")));
                Assert.That(error.RetrySlot.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
                AssertColor(shell.Root.resolvedStyle.backgroundColor, "#F7F8FA", "shared canvas");
                AssertColor(error.Root.resolvedStyle.backgroundColor, "#FFF0F1", "error surface");
                AssertColor(error.Title.resolvedStyle.color, "#B42332", "error title");
                AssertColor(error.Body.resolvedStyle.color, "#B42332", "error body");
                AssertColor(empty.Root.resolvedStyle.backgroundColor, "#FFFFFF", "empty surface");
                AssertColor(empty.Title.resolvedStyle.color, "#14171A", "empty title");
                AssertColor(empty.Body.resolvedStyle.color, "#556170", "empty body");
                Assert.That(progress.Bar.value, Is.EqualTo(42f));
                AssertContained(scroll.contentViewport.worldBound, error.Root.worldBound, "offline error");
                AssertContained(scroll.contentViewport.worldBound, empty.Root.worldBound, "empty state");
                AssertContained(scroll.contentViewport.worldBound, progress.Root.worldBound, "progress");

                sample.Capture("shared-states", "Shared component gallery: Offline error; NotInstalled empty; progress=42; no network request",
                    error.Title.text, shell.Root, error.Title, error.Root);
            }
        }

        private sealed class RenderSample : IDisposable
        {
            private readonly Locale originalLocale;
            private readonly bool originalReduceMotion = UIFeedbackService.ReduceMotion;
            private readonly bool originalHaptics = UIFeedbackService.HapticsEnabled;
            private readonly bool originalSound = UIFeedbackService.SoundEnabled;
            private readonly float originalAnimationSpeed = UIFeedbackService.AnimationSpeed;
            private readonly RenderTexture originalActiveTexture = RenderTexture.active;
            private readonly List<IDisposable> owned = new List<IDisposable>();
            private readonly string locale;
            private readonly SampleViewport viewport;
            private UIDocument document;
            private PanelSettings originalPanel;
            private PanelSettings panel;
            private RenderTexture target;
            private MobilePageShell shell;

            public RenderSample(string locale, SampleViewport viewport)
            {
                Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null),
                    "Visual samples require a real graphics device; omit -nographics.");
                Locale selected = LocalizationSettings.AvailableLocales.Locales.FirstOrDefault(
                    candidate => candidate.Identifier.Code == locale);
                Assert.That(selected, Is.Not.Null, "Missing application locale " + locale);
                this.locale = locale;
                this.viewport = viewport;
                originalLocale = LocalizationSettings.SelectedLocale;
                Host = new GameObject("Core Neutral Runtime Visual Sample");
                LocalizationSettings.SelectedLocale = selected;
                UIFeedbackService.Configure(true, false, 1f, false);
            }

            public GameObject Host { get; }

            public void Own(IDisposable value) => owned.Add(value);

            public void Attach(UIDocument nextDocument, MobilePageShell nextShell)
            {
                document = nextDocument;
                shell = nextShell;
                originalPanel = document.panelSettings;
                Assert.That(originalPanel, Is.Not.Null);
                panel = Object.Instantiate(originalPanel);
                panel.name = "Core Neutral Sample Panel (temporary clone)";
                panel.hideFlags = HideFlags.HideAndDontSave;
                target = new RenderTexture(viewport.Width, viewport.Height, 24, RenderTextureFormat.ARGB32)
                {
                    name = "Core Neutral Sample Render Target",
                    hideFlags = HideFlags.HideAndDontSave,
                    antiAliasing = 1
                };
                Assert.That(target.Create(), Is.True, "Sample RenderTexture.Create failed.");
                panel.targetTexture = target;
                panel.clearColor = true;
                panel.colorClearValue = Color.clear;
                document.panelSettings = panel;

                FieldInfo field = typeof(MobilePageShell).GetField("safeAreaBinding",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(field, Is.Not.Null, "Update this fixture if the shared Safe Area binding changes.");
                var binding = field.GetValue(shell) as UiToolkitSafeAreaBinding;
                Assert.That(binding, Is.Not.Null);
                binding.Suspend();
            }

            public IEnumerator SettleLayout()
            {
                for (int frame = 0; frame < 4; frame++)
                    yield return null;
                Assert.That(shell.Root.panel, Is.Not.Null);
                float width = shell.Root.resolvedStyle.width;
                float height = shell.Root.resolvedStyle.height;
                Assert.That(width, Is.GreaterThan(0f));
                Assert.That(height, Is.GreaterThan(0f));
                Assert.That(width / height, Is.EqualTo((float)viewport.Width / viewport.Height).Within(0.002f),
                    "The real page must fill the target texture instead of a forced aspect-ratio viewport.");

                // This is an explicit geometry fixture, not a claim about real Screen.safeArea.
                float xScale = width / viewport.Width;
                float yScale = height / viewport.Height;
                bool compact = viewport.Width < MobileUiTokens.CompactPhysicalWidth;
                float basePadding = compact ? MobileUiTokens.SpaceMd : MobileUiTokens.SpaceXl;
                float left = basePadding + viewport.Left * xScale;
                float top = basePadding + viewport.Top * yScale;
                float right = basePadding + viewport.Right * xScale;
                float bottom = basePadding + viewport.Bottom * yScale;
                shell.SafeArea.EnableInClassList("mobile-layout--compact", compact);
                shell.SafeArea.style.paddingLeft = left;
                shell.SafeArea.style.paddingTop = top;
                shell.SafeArea.style.paddingRight = right;
                shell.SafeArea.style.paddingBottom = bottom;
                shell.OverlayLayer.style.left = left;
                shell.OverlayLayer.style.top = top;
                shell.OverlayLayer.style.right = right;
                shell.OverlayLayer.style.bottom = bottom;
                for (int frame = 0; frame < 4; frame++)
                    yield return null;
            }

            public void Capture(string screen, string fixture, string displayedTitle,
                VisualElement canvas, Label text, VisualElement emphasis)
            {
                RenderTexture previous = RenderTexture.active;
                Texture2D image = null;
                try
                {
                    RenderTexture.active = target;
                    image = new Texture2D(viewport.Width, viewport.Height, TextureFormat.RGBA32, false);
                    image.ReadPixels(new Rect(0, 0, viewport.Width, viewport.Height), 0, 0, false);
                    image.Apply(false, false);
                    Assert.That(image.width, Is.EqualTo(viewport.Width));
                    Assert.That(image.height, Is.EqualTo(viewport.Height));
                    Color32[] pixels = image.GetPixels32();
                    int opaque = 0;
                    int dark = 0;
                    int blue = 0;
                    var distinct = new HashSet<int>();
                    for (int index = 0; index < pixels.Length; index += 4)
                    {
                        Color32 pixel = pixels[index];
                        if (pixel.a < 240)
                            continue;
                        opaque++;
                        if (pixel.r < 160 && pixel.g < 160 && pixel.b < 170)
                            dark++;
                        if (pixel.b > 150 && pixel.b > pixel.r + 40 && pixel.b > pixel.g + 20)
                            blue++;
                        distinct.Add((pixel.r << 16) | (pixel.g << 8) | pixel.b);
                    }
                    Assert.That(opaque, Is.GreaterThan(pixels.Length / 4 * 0.95f), "Blank or transparent panel capture.");
                    Assert.That(distinct.Count, Is.GreaterThan(32), "Uniform clear color is not a rendered screen.");
                    Assert.That(dark, Is.GreaterThan(100), "Expected actual text/foreground pixels.");
                    Assert.That(blue, Is.GreaterThan(100), "Expected a visible primary action, not only a page background.");

                    string directory = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "../TestResults/UIRefresh"));
                    Directory.CreateDirectory(directory);
                    string name = screen + "-" + locale + "-" + viewport.Width + "x" + viewport.Height +
                                  "-simulated-insets-" + viewport.Left + "-" + viewport.Top + "-" + viewport.Right + "-" + viewport.Bottom;
                    byte[] png = image.EncodeToPNG();
                    Assert.That(png.Length, Is.GreaterThan(4096));
                    string imagePath = Path.Combine(directory, name + ".png");
                    File.WriteAllBytes(imagePath, png);
                    var metadata = new SampleMetadata
                    {
                        source = "Unity runtime UI Toolkit / cloned Resources UI/Collection Panel Settings / Texture2D.ReadPixels",
                        screen = screen,
                        language = locale,
                        selectedLocale = LocalizationSettings.SelectedLocale.Identifier.Code,
                        width = viewport.Width,
                        height = viewport.Height,
                        simulatedInsetsLeftTopRightBottom = new[] { viewport.Left, viewport.Top, viewport.Right, viewport.Bottom },
                        safeAreaEvidence = "Simulated insets only; Screen.safeArea conversion and Android/device acceptance are not assessed.",
                        fixture = fixture,
                        reduceMotion = true,
                        displayedTitle = displayedTitle,
                        canvasColor = ColorUtility.ToHtmlStringRGBA(canvas.resolvedStyle.backgroundColor),
                        textColor = ColorUtility.ToHtmlStringRGBA(text.resolvedStyle.color),
                        emphasisColor = ColorUtility.ToHtmlStringRGBA(emphasis.resolvedStyle.backgroundColor),
                        sampledOpaquePixels = opaque,
                        sampledDistinctColors = distinct.Count
                    };
                    File.WriteAllText(Path.Combine(directory, name + ".json"), JsonUtility.ToJson(metadata, true));
                    Debug.Log("Core Neutral runtime sample: " + imagePath);
                }
                finally
                {
                    RenderTexture.active = previous;
                    if (image != null)
                        Object.DestroyImmediate(image);
                }
            }

            public void Dispose()
            {
                for (int index = owned.Count - 1; index >= 0; index--)
                    owned[index].Dispose();
                if (document != null)
                {
                    document.rootVisualElement.Clear();
                    document.panelSettings = originalPanel;
                }
                Object.DestroyImmediate(Host);
                if (panel != null)
                {
                    panel.targetTexture = null;
                    Object.DestroyImmediate(panel);
                }
                RenderTexture.active = originalActiveTexture;
                if (target != null)
                {
                    target.Release();
                    Object.DestroyImmediate(target);
                }
                LocalizationSettings.SelectedLocale = originalLocale;
                UIFeedbackService.Configure(originalReduceMotion, originalHaptics, originalAnimationSpeed, originalSound);
            }
        }

        private readonly struct SampleViewport
        {
            public SampleViewport(int width, int height, int left, int top, int right, int bottom)
            {
                Width = width;
                Height = height;
                Left = left;
                Top = top;
                Right = right;
                Bottom = bottom;
            }
            public int Width { get; }
            public int Height { get; }
            public int Left { get; }
            public int Top { get; }
            public int Right { get; }
            public int Bottom { get; }
        }

        [Serializable]
        private sealed class SampleMetadata
        {
            public string source;
            public string screen;
            public string language;
            public string selectedLocale;
            public int width;
            public int height;
            public int[] simulatedInsetsLeftTopRightBottom;
            public string safeAreaEvidence;
            public string fixture;
            public bool reduceMotion;
            public string displayedTitle;
            public string canvasColor;
            public string textColor;
            public string emphasisColor;
            public int sampledOpaquePixels;
            public int sampledDistinctColors;
        }

        private static void AssertColor(Color actual, string hex, string label)
        {
            Assert.That(ColorUtility.TryParseHtmlString(hex, out Color expected), Is.True);
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.005f), label + " red");
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.005f), label + " green");
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.005f), label + " blue");
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.005f), label + " alpha");
        }

        private static Rect SafeContentRect(VisualElement element)
        {
            Rect bounds = element.worldBound;
            IResolvedStyle style = element.resolvedStyle;
            return Rect.MinMaxRect(bounds.xMin + style.paddingLeft, bounds.yMin + style.paddingTop,
                bounds.xMax - style.paddingRight, bounds.yMax - style.paddingBottom);
        }

        private static void AssertActionLabelContained(VisualElement action, string description)
        {
            Label label = action.Q<Label>("action-label");
            Assert.That(label, Is.Not.Null, description + " label");
            Assert.That(string.IsNullOrWhiteSpace(label.text), Is.False, description + " text");
            Assert.That(label.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex), description + " label display");
            Assert.That(label.resolvedStyle.visibility, Is.EqualTo(Visibility.Visible), description + " label visibility");
            AssertContained(action.worldBound, label.worldBound, description + " label");
        }

        private static void AssertContained(Rect container, Rect element, string label)
        {
            Assert.That(element.width, Is.GreaterThan(0f), label);
            Assert.That(element.height, Is.GreaterThan(0f), label);
            Assert.That(element.xMin, Is.GreaterThanOrEqualTo(container.xMin - 1f), label + " left");
            Assert.That(element.yMin, Is.GreaterThanOrEqualTo(container.yMin - 1f), label + " top");
            Assert.That(element.xMax, Is.LessThanOrEqualTo(container.xMax + 1f), label + " right");
            Assert.That(element.yMax, Is.LessThanOrEqualTo(container.yMax + 1f), label + " bottom");
        }
    }
}
