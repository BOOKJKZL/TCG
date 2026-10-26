using System;
using System.Collections;
using System.Reflection;
using Gacha.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Gacha.Tests.PlayMode
{
    public sealed class MobileStartPresenterPlayModeTests
    {
        [UnityTest]
        public IEnumerator Start_LocalizesActionAndBlocksRepeatedInputWhileOpeningHome()
        {
            yield return LocalizationSettings.InitializationOperation;
            Locale originalLocale = LocalizationSettings.SelectedLocale;
            var host = new GameObject("Mobile Start Test Host");
            MobileStartPresenter presenter = null;
            int starts = 0;
            try
            {
                presenter = new MobileStartPresenter(host, () => starts++);
                foreach (string language in new[] { "en", "zh", "ja" })
                {
                    Locale locale = LocalizationSettings.AvailableLocales.GetLocale(language);
                    Assert.That(locale, Is.Not.Null, language);
                    LocalizationSettings.SelectedLocale = locale;
                    presenter.RefreshText();
                    yield return null;
                    yield return null;
                    Assert.That(presenter.Title.text, Is.EqualTo(CardUiText.Get("start.title")));
                    Assert.That(presenter.Title.text, Is.Not.EqualTo("start.title"));
                    Assert.That(presenter.StartAction.Label.text, Is.Not.Empty);
                    Assert.That(presenter.StartAction.Label.text, Is.Not.EqualTo("start.action.start"));
                    Assert.That(presenter.StartAction.Root.resolvedStyle.height,
                        Is.GreaterThanOrEqualTo(MobileUiTokens.MinimumTouchTarget - 0.5f));
                    Assert.That(presenter.Title.resolvedStyle.whiteSpace, Is.EqualTo(WhiteSpace.Normal));
                    Assert.That(presenter.Shell.Root.resolvedStyle.backgroundColor.a, Is.EqualTo(1f));
                }

                presenter.SetPending(true);
                InvokeAction(presenter.StartAction);
                InvokeAction(presenter.StartAction);
                Assert.That(starts, Is.Zero);
                Assert.That(presenter.IsPending, Is.True);
                Assert.That(presenter.StartAction.IsLoading, Is.True);
                Assert.That(presenter.Status.text, Is.EqualTo(CardUiText.Get("start.action.loading")));
                presenter.SetPending(false);
                InvokeAction(presenter.StartAction);
                Assert.That(starts, Is.EqualTo(1));
                Assert.That(presenter.StartAction.IsEnabled, Is.True);
                Assert.That(presenter.Status.ClassListContains("is-hidden"), Is.True);
                presenter.SetFailed();
                Assert.That(presenter.Status.text, Is.EqualTo(CardUiText.Get("common.navigation.failed")));
                Assert.That(presenter.Status.ClassListContains("is-hidden"), Is.False);
                Assert.That(presenter.StartAction.IsEnabled, Is.True);

                presenter.Dispose();
                presenter = null;
                Assert.That(host.GetComponent<UIDocument>().rootVisualElement.childCount, Is.Zero);
            }
            finally
            {
                presenter?.Dispose();
                LocalizationSettings.SelectedLocale = originalLocale;
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [UnityTest]
        public IEnumerator Start_OnRealPortraitPanelCentersAllLanguagesAtNormalAndDoubleFont()
        {
            yield return LocalizationSettings.InitializationOperation;
            Locale originalLocale = LocalizationSettings.SelectedLocale;
            var source = new TestFontScaleSource();
            IDisposable injected = MobileTypography.OverrideSourceForTests(source);
            var host = new GameObject("Mobile Start Portrait Panel");
            MobileStartPresenter presenter = null;
            StartPanelViewport panel = null;
            try
            {
                presenter = new MobileStartPresenter(host, () => { });
                panel = new StartPanelViewport(presenter, 1080, 2220);
                foreach (float scale in new[] { 1f, 2f })
                foreach (string language in new[] { "en", "zh", "ja" })
                {
                    Locale locale = LocalizationSettings.AvailableLocales.GetLocale(language);
                    Assert.That(locale, Is.Not.Null, language);
                    LocalizationSettings.SelectedLocale = locale;
                    presenter.RefreshText();
                    source.Set(scale);
                    yield return SettleLayout();
                    panel.AssertDimensions();
                    Rect visible = presenter.Content.contentViewport.worldBound;
                    Rect card = presenter.Content.Q<VisualElement>("start-card").worldBound;
                    Assert.That(card.height, Is.LessThan(visible.height - 48f), language + " portrait room");
                    Assert.That(card.center.y, Is.EqualTo(visible.center.y).Within(1f),
                        $"{language} scale={scale}: card={card}, viewport={visible}; the card must not stick to the top.");
                    Assert.That(card.center.x, Is.EqualTo(visible.center.x).Within(1f));
                    Assert.That(presenter.Content.scrollOffset.y, Is.EqualTo(0f).Within(0.5f));
                    Assert.That(presenter.Content.contentContainer.worldBound.height,
                        Is.GreaterThanOrEqualTo(visible.height - 1f));
                    AssertPickable(presenter.StartAction.Root);
                }
            }
            finally
            {
                presenter?.Dispose();
                panel?.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
                injected.Dispose();
                LocalizationSettings.SelectedLocale = originalLocale;
            }
        }

        [UnityTest]
        public IEnumerator Start_OnShortViewportKeepsTheActionReachableByScrolling()
        {
            yield return LocalizationSettings.InitializationOperation;
            var source = new TestFontScaleSource();
            source.Set(2f);
            IDisposable injected = MobileTypography.OverrideSourceForTests(source);
            var host = new GameObject("Mobile Start Short Viewport");
            MobileStartPresenter presenter = null;
            StartPanelViewport panel = null;
            int starts = 0;
            try
            {
                presenter = new MobileStartPresenter(host, () => starts++);
                panel = new StartPanelViewport(presenter, 640, 360);
                presenter.Title.text = "ユニバーサルガチャシミュレーターでカードを集めましょう";
                presenter.Body.text = string.Concat(System.Linq.Enumerable.Repeat(
                    "自分のペースでパックを開けて、コレクションを楽しみましょう。", 8));
                yield return SettleLayout();
                panel.AssertDimensions();
                presenter.Content.scrollOffset = Vector2.zero;
                yield return SettleLayout();
                Rect topViewport = presenter.Content.contentViewport.worldBound;
                Assert.That(presenter.Content.Q<VisualElement>("start-card").worldBound.yMin,
                    Is.GreaterThanOrEqualTo(topViewport.yMin - 1f),
                    "Centering oversized content must not place its top above the scrollable origin.");
                Assert.That(presenter.Title.worldBound.yMin, Is.GreaterThanOrEqualTo(topViewport.yMin - 1f));
                Assert.That(presenter.Title.worldBound.yMin, Is.LessThan(topViewport.yMax),
                    "The start of the long title must remain reachable by scrolling to the top.");
                presenter.Content.ScrollTo(presenter.StartAction.Root);
                yield return SettleLayout();
                Rect action = presenter.StartAction.Root.worldBound;
                Rect visible = presenter.Content.contentViewport.worldBound;
                Assert.That(presenter.Content.Q<VisualElement>("start-card").worldBound.height, Is.GreaterThan(visible.height));
                Assert.That(presenter.Content.scrollOffset.y, Is.GreaterThan(0f));
                Assert.That(action.height, Is.GreaterThanOrEqualTo(MobileUiTokens.MinimumTouchTarget - 0.5f));
                Assert.That(action.yMin, Is.GreaterThanOrEqualTo(visible.yMin - 1f));
                Assert.That(action.yMax, Is.LessThanOrEqualTo(visible.yMax + 1f));
                Assert.That(action.xMin, Is.GreaterThanOrEqualTo(visible.xMin - 1f));
                Assert.That(action.xMax, Is.LessThanOrEqualTo(visible.xMax + 1f));
                AssertPickable(presenter.StartAction.Root);
                using (PointerDownEvent down = PointerDownEvent.GetPooled(new Event
                       { type = EventType.MouseDown, button = 0, mousePosition = action.center }))
                    presenter.StartAction.Root.SendEvent(down);
                using (PointerUpEvent up = PointerUpEvent.GetPooled(new Event
                       { type = EventType.MouseUp, button = 0, mousePosition = action.center }))
                    presenter.StartAction.Root.SendEvent(up);
                Assert.That(starts, Is.EqualTo(1));
            }
            finally
            {
                presenter?.Dispose();
                panel?.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
                injected.Dispose();
            }
        }

        private static IEnumerator SettleLayout()
        {
            for (int frame = 0; frame < 5; frame++) yield return null;
        }

        private static void AssertPickable(VisualElement action)
        {
            VisualElement picked = action.panel.Pick(action.worldBound.center);
            Assert.That(picked == action || action.Contains(picked), Is.True,
                $"Start action bounds={action.worldBound}, picked={picked?.name}");
        }

        private sealed class StartPanelViewport : IDisposable
        {
            private readonly UIDocument document;
            private readonly PanelSettings original;
            private readonly PanelSettings panel;
            private readonly RenderTexture target;
            private readonly int width, height;

            public StartPanelViewport(MobileStartPresenter presenter, int width, int height)
            {
                this.width = width;
                this.height = height;
                document = presenter.Document;
                original = document.panelSettings;
                panel = UnityEngine.Object.Instantiate(original);
                panel.hideFlags = HideFlags.HideAndDontSave;
                panel.scaleMode = PanelScaleMode.ConstantPixelSize;
                panel.scale = 1f;
                target = new RenderTexture(width, height, 24) { hideFlags = HideFlags.HideAndDontSave };
                Assert.That(target.Create(), Is.True);
                panel.targetTexture = target;
                document.panelSettings = panel;
                var safeBinding = (UiToolkitSafeAreaBinding)typeof(MobilePageShell)
                    .GetField("safeAreaBinding", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(presenter.Shell);
                safeBinding.Suspend();
                presenter.Shell.SafeArea.EnableInClassList("mobile-layout--compact", width < 980);
                presenter.Shell.SafeArea.style.paddingLeft = 24f;
                presenter.Shell.SafeArea.style.paddingTop = 48f;
                presenter.Shell.SafeArea.style.paddingRight = 24f;
                presenter.Shell.SafeArea.style.paddingBottom = 72f;
            }

            public void AssertDimensions()
            {
                Assert.That(document.rootVisualElement.worldBound.width, Is.EqualTo(width).Within(1f));
                Assert.That(document.rootVisualElement.worldBound.height, Is.EqualTo(height).Within(1f));
            }

            public void Dispose()
            {
                if (document != null) document.panelSettings = original;
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(panel);
            }
        }

        [Test]
        public void StartController_RetiresOnlyLegacyStartAndEdgeCanvases()
        {
            Type managerType = Type.GetType("GameManager, Gacha.Runtime.Foundation", true);
            Type controllerType = Type.GetType("StartController, Gacha.Runtime.Controllers", true);
            FieldInfo instance = managerType.GetField("<Instance>k__BackingField",
                BindingFlags.Static | BindingFlags.NonPublic);
            object original = instance.GetValue(null);
            var persistent = new GameObject("Test Persistent Managers");
            persistent.SetActive(false);
            var legacy = new GameObject("Test Legacy Start Canvas", typeof(Canvas));
            var startButton = new GameObject("StartBtn");
            startButton.transform.SetParent(legacy.transform);
            var controllerHost = new GameObject("Test Start Controller");
            controllerHost.SetActive(false);
            try
            {
                Component manager = persistent.AddComponent(managerType);
                instance.SetValue(null, manager);
                var side = new GameObject("Side");
                side.transform.SetParent(persistent.transform);
                var sideCanvas = new GameObject("Canvas", typeof(Canvas));
                sideCanvas.transform.SetParent(side.transform);
                var loading = new GameObject("Loading", typeof(Canvas));
                loading.transform.SetParent(persistent.transform);
                var transition = new GameObject("ChangeScene", typeof(Canvas));
                transition.transform.SetParent(persistent.transform);
                Component controller = controllerHost.AddComponent(controllerType);
                controllerType.GetMethod("HideLegacyPresentation",
                    BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);

                Assert.That(sideCanvas.GetComponent<Canvas>().enabled, Is.False);
                Assert.That(legacy.GetComponent<Canvas>().enabled, Is.False);
                Assert.That(loading.GetComponent<Canvas>().enabled, Is.True);
                Assert.That(transition.GetComponent<Canvas>().enabled, Is.True);
                Assert.That(side.activeSelf, Is.True);
                Assert.That(((Behaviour)manager).enabled, Is.True);
                Assert.That(instance.GetValue(null), Is.SameAs(manager));
            }
            finally
            {
                instance.SetValue(null, original);
                UnityEngine.Object.DestroyImmediate(controllerHost);
                UnityEngine.Object.DestroyImmediate(legacy);
                UnityEngine.Object.DestroyImmediate(persistent);
            }
        }

        [Test]
        public void StartController_LoadsHomeOnceAndAllowsRetryAfterSynchronousFailure()
        {
            Type type = Type.GetType("StartController, Gacha.Runtime.Controllers", true);
            var host = new GameObject("Start Controller Routes");
            host.SetActive(false);
            try
            {
                Component controller = host.AddComponent(type);
                FieldInfo loader = type.GetField("sceneLoaderOverrideForTests",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo start = type.GetMethod("StartBtnClick");
                loader.SetValue(controller, new Action<string>(_ => throw new InvalidOperationException("test")));
                Assert.DoesNotThrow(() => start.Invoke(controller, null));
                int routes = 0;
                loader.SetValue(controller, new Action<string>(scene =>
                {
                    Assert.That(scene, Is.EqualTo("002_MainMenuScene"));
                    routes++;
                }));
                start.Invoke(controller, null);
                start.Invoke(controller, null);
                Assert.That(routes, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [UnityTest]
        public IEnumerator StartController_BackCancelsExitAndRequiresExplicitConfirmation()
        {
            Type type = Type.GetType("StartController, Gacha.Runtime.Controllers", true);
            var host = new GameObject("Start Back Controller");
            host.SetActive(false);
            var presenterHost = new GameObject("Start Back Presenter");
            MobileStartPresenter presenter = null;
            try
            {
                Component controller = host.AddComponent(type);
                presenter = new MobileStartPresenter(presenterHost, () => { });
                type.GetField("presenter", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(controller, presenter);
                int exits = 0;
                type.GetField("quitOverrideForTests", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(controller, new Action(() => exits++));
                MethodInfo back = type.GetMethod("TryHandleBack");
                yield return null;
                Assert.That((bool)back.Invoke(controller, null), Is.True);
                Assert.That(presenter.ExitConfirmation.IsVisible, Is.True);
                Assert.That(exits, Is.Zero);
                Assert.That((bool)back.Invoke(controller, null), Is.True);
                Assert.That(presenter.ExitConfirmation.IsVisible, Is.False);
                Assert.That(exits, Is.Zero);
                back.Invoke(controller, null);
                yield return null;
                InvokeAction(presenter.ExitConfirmation.Confirm);
                Assert.That(exits, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                presenter?.Dispose();
                UnityEngine.Object.DestroyImmediate(presenterHost);
            }
        }

        private static void InvokeAction(MobileActionControl action)
        {
            action.Root.Focus();
            using (KeyDownEvent down = KeyDownEvent.GetPooled(new Event
                   { type = EventType.KeyDown, keyCode = KeyCode.Return }))
                action.Root.SendEvent(down);
            using (KeyUpEvent up = KeyUpEvent.GetPooled(new Event
                   { type = EventType.KeyUp, keyCode = KeyCode.Return }))
                action.Root.SendEvent(up);
        }
    }
}
