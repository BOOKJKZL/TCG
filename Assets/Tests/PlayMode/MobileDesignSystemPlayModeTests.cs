using System;
using System.Collections;
using Gacha.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Gacha.Tests.PlayMode
{
    public class MobileDesignSystemPlayModeTests
    {
        [UnityTest]
        public IEnumerator ActionAndPageShell_KeepTouchGeometryStableAcrossRuntimeStates()
        {
            GameObject host = new GameObject("Mobile Design System Host");
            MobilePageShell shell = null;
            MobileActionControl action = null;
            MobileActionControl home = null;
            MobileActionControl content = null;
            int clicks = 0;
            int feedbackCount = 0;
            Action<FeedbackCue> feedback = cue =>
            {
                if (cue == FeedbackCue.ButtonClick)
                    feedbackCount++;
            };
            try
            {
                UIFeedbackService.Configure(
                    reduceMotion: false,
                    hapticsEnabled: false,
                    animationSpeed: 1f,
                    soundEnabled: false);
                UIFeedbackService.FeedbackPlayed += feedback;
                var document = host.AddComponent<UIDocument>();
                document.panelSettings = Resources.Load<PanelSettings>("UI/Collection Panel Settings");
                Assert.That(document.panelSettings, Is.Not.Null);
                yield return null;

                var viewport = new VisualElement { name = "mobile-contract-viewport" };
                viewport.style.position = Position.Relative;
                viewport.style.width = 720f;
                viewport.style.height = 1600f;
                document.rootVisualElement.Add(viewport);

                shell = new MobilePageShell("contract-page");
                viewport.Add(shell.Root);
                var topBar = new MobileTopBar(
                    "モバイル UI 契約の長い見出し",
                    "安全領域の内側で折り返し、操作を隠さない説明文です。");
                shell.HeaderSlot.Add(topBar.Root);

                action = new MobileActionControl(
                    "contract-action",
                    "繰り返し押して状態を確認する",
                    () => clicks++,
                    MobileActionTone.Primary);
                action.Root.style.width = 260f;
                shell.ContentSlot.Add(action.Root);

                home = new MobileActionControl("nav-home", "ホーム", () => { }, MobileActionTone.Navigation);
                content = new MobileActionControl("nav-content", "コンテンツ", () => { }, MobileActionTone.Navigation);
                var navigation = new MobileBottomNavigation(home, content);
                navigation.Select(0);
                shell.BottomNavigationSlot.Add(navigation.Root);

                yield return null;
                yield return null;

                Assert.That(action.Root, Is.Not.TypeOf<Button>());
                Assert.That(action.Root.resolvedStyle.height,
                    Is.GreaterThanOrEqualTo(MobileUiTokens.MinimumTouchTarget - 0.5f));
                Assert.That(home.Root.resolvedStyle.height,
                    Is.GreaterThanOrEqualTo(MobileUiTokens.MinimumTouchTarget - 0.5f));
                Assert.That(action.Label.resolvedStyle.whiteSpace, Is.EqualTo(WhiteSpace.Normal));
                AssertContained(viewport.worldBound, topBar.Root.worldBound, "top bar");
                AssertContained(viewport.worldBound, navigation.Root.worldBound, "bottom navigation");
                AssertInsetOverlayMatchesSafeContent(shell);

                Rect geometry = action.Root.worldBound;
                int childCount = action.Root.childCount;
                SendPointerDown(action.Root);
                Assert.That(action.Label.ClassListContains("is-pressed"), Is.True);
                SendPointerUp(action.Root, action.Root.worldBound.center);
                Assert.That(action.Label.ClassListContains("is-pressed"), Is.False);
                Assert.That(clicks, Is.EqualTo(1));
                Assert.That(feedbackCount, Is.EqualTo(1));

                for (int index = 0; index < 3; index++)
                {
                    SendPointerDown(action.Root);
                    SendPointerUp(action.Root, action.Root.worldBound.center);
                }
                Assert.That(clicks, Is.EqualTo(4));
                Assert.That(feedbackCount, Is.EqualTo(4));

                action.SetEnabled(false);
                SendPointerDown(action.Root);
                SendPointerUp(action.Root, action.Root.worldBound.center);
                Assert.That(clicks, Is.EqualTo(4));
                Assert.That(action.Label.ClassListContains("is-disabled"), Is.True);
                Assert.That(action.Root.enabledSelf, Is.True);
                Assert.That(action.Root.pickingMode, Is.EqualTo(PickingMode.Position));

                action.SetEnabled(true);
                action.SetLoading(true);
                SendPointerDown(action.Root);
                SendPointerUp(action.Root, action.Root.worldBound.center);
                Assert.That(clicks, Is.EqualTo(4));
                Assert.That(action.LoadingIndicator.ClassListContains("is-visible"), Is.True);
                action.SetLoading(false);
                yield return null;

                Assert.That(action.Root.childCount, Is.EqualTo(childCount));
                AssertRectApproximately(action.Root.worldBound, geometry, "action geometry");
                Assert.That(action.Root.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(action.Root.resolvedStyle.visibility, Is.EqualTo(Visibility.Visible));
                Assert.That(action.Root.resolvedStyle.opacity, Is.EqualTo(1f).Within(0.01f));

                SendPointerDown(action.Root);
                SendPointerUp(action.Root, action.Root.worldBound.max + new Vector2(80f, 80f));
                Assert.That(clicks, Is.EqualTo(4), "Pointer release outside must cancel activation.");
                Assert.That(action.Label.ClassListContains("is-pressed"), Is.False);
            }
            finally
            {
                UIFeedbackService.FeedbackPlayed -= feedback;
                UIFeedbackService.Configure(false, true, 1f, true);
                action?.Dispose();
                home?.Dispose();
                content?.Dispose();
                shell?.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [UnityTest]
        public IEnumerator StateAndOverlayPresenters_RespectReducedMotionAndDisposeSchedules()
        {
            GameObject host = new GameObject("Mobile Overlay Contract Host");
            MobilePageShell shell = null;
            MobileErrorState error = null;
            MobileToastPresenter toast = null;
            MobileConfirmationPresenter confirmation = null;
            try
            {
                UIFeedbackService.Configure(
                    reduceMotion: true,
                    hapticsEnabled: false,
                    animationSpeed: 1f,
                    soundEnabled: false);
                var document = host.AddComponent<UIDocument>();
                document.panelSettings = Resources.Load<PanelSettings>("UI/Collection Panel Settings");
                yield return null;

                shell = new MobilePageShell("overlay-contract-page");
                document.rootVisualElement.Add(shell.Root);
                error = new MobileErrorState(() => { }, () => { }, () => { }, () => { });
                shell.ContentSlot.Add(error.Root);
                error.Presenter.Show(PlayerUiErrorMapper.Create(PlayerUiErrorCode.Offline));
                yield return null;
                Assert.That(error.Presenter.IsVisible, Is.True);
                Assert.That(error.Presenter.IsAnimating, Is.False);
                Assert.That(error.Actions.childCount, Is.EqualTo(4));
                error.Presenter.Hide();
                Assert.That(error.Presenter.IsVisible, Is.False);
                Assert.That(error.Root.style.display.value, Is.EqualTo(DisplayStyle.None));

                toast = new MobileToastPresenter();
                shell.OverlayLayer.Add(toast.Root);
                Assert.That(toast.Root.parent, Is.SameAs(shell.OverlayLayer));
                Assert.That(shell.OverlayLayer.parent, Is.SameAs(shell.SafeArea),
                    "Toast positioning must inherit the active page Safe Area.");
                toast.Show("Catalog refreshed", MobileStatusTone.Success, 20);
                yield return null;
                Assert.That(toast.IsVisible, Is.True);
                AssertInsetOverlayMatchesSafeContent(shell);
                AssertContained(shell.OverlayLayer.worldBound, toast.Root.worldBound, "safe-area toast");
                toast.Dispose();
                yield return new WaitForSecondsRealtime(0.08f);
                Assert.That(toast.IsVisible, Is.False,
                    "Disposed toast schedules must not write back into a navigated-away page.");

                confirmation = new MobileConfirmationPresenter();
                shell.ModalLayer.Add(confirmation.Root);
                confirmation.Show(
                    "Remove downloaded cards?",
                    "Collection progress stays saved.",
                    "Remove",
                    "Cancel",
                    null,
                    destructive: true);
                yield return null;
                Assert.That(confirmation.IsVisible, Is.True);
                Assert.That(confirmation.Sheet.SafeArea.ClassListContains("safe-area-bound"), Is.True);
                Assert.That(confirmation.DangerConfirmSlot.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(confirmation.ConfirmSlot.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
                Assert.That(confirmation.DangerConfirm.Root.resolvedStyle.height,
                    Is.GreaterThanOrEqualTo(MobileUiTokens.MinimumTouchTarget - 0.5f));
                confirmation.Hide();
                Assert.That(confirmation.IsVisible, Is.False,
                    "Reduced motion must settle sheet visibility immediately.");
            }
            finally
            {
                UIFeedbackService.Configure(false, true, 1f, true);
                confirmation?.Dispose();
                toast?.Dispose();
                error?.Dispose();
                shell?.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [UnityTest]
        public IEnumerator Confirmation_NormalMotionDoesNotStealFocusAfterCallbackNavigation()
        {
            GameObject host = new GameObject("Mobile Focus Contract Host");
            MobilePageShell shell = null;
            MobileConfirmationPresenter confirmation = null;
            try
            {
                UIFeedbackService.Configure(
                    reduceMotion: false,
                    hapticsEnabled: false,
                    animationSpeed: 1f,
                    soundEnabled: false);
                var document = host.AddComponent<UIDocument>();
                document.panelSettings = Resources.Load<PanelSettings>("UI/Collection Panel Settings");
                yield return null;

                shell = new MobilePageShell("focus-contract-page");
                document.rootVisualElement.Add(shell.Root);
                var prior = new VisualElement { name = "prior-focus", focusable = true };
                var navigated = new VisualElement { name = "navigated-focus", focusable = true };
                prior.style.width = 50f;
                prior.style.height = 50f;
                navigated.style.width = 50f;
                navigated.style.height = 50f;
                shell.ContentSlot.Add(prior);
                shell.ContentSlot.Add(navigated);
                confirmation = new MobileConfirmationPresenter();
                shell.ModalLayer.Add(confirmation.Root);
                yield return null;

                prior.Focus();
                confirmation.Show("Continue?", "Focus must follow navigation.", "Continue", "Cancel", () =>
                {
                    navigated.Focus();
                });
                yield return null;
                Assert.That(document.rootVisualElement.panel.focusController.focusedElement,
                    Is.SameAs(confirmation.Sheet.Panel));

                navigated.Focus();
                yield return null;
                Assert.That(document.rootVisualElement.panel.focusController.focusedElement,
                    Is.SameAs(confirmation.Sheet.Panel),
                    "Visible modal sheets must trap focus inside their hierarchy.");

                SendPointerDown(confirmation.DangerConfirm.Root);
                SendPointerUp(
                    confirmation.DangerConfirm.Root,
                    confirmation.DangerConfirm.Root.worldBound.center);
                Assert.That(document.rootVisualElement.panel.focusController.focusedElement,
                    Is.SameAs(navigated),
                    "The confirmation callback should be free to establish the next page focus.");

                yield return new WaitForSecondsRealtime(0.2f);
                Assert.That(document.rootVisualElement.panel.focusController.focusedElement,
                    Is.SameAs(navigated),
                    "A delayed sheet animation must not restore stale focus after navigation.");
                Assert.That(confirmation.IsVisible, Is.False);
            }
            finally
            {
                UIFeedbackService.Configure(false, true, 1f, true);
                confirmation?.Dispose();
                shell?.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        private static void SendPointerDown(VisualElement control)
        {
            using (PointerDownEvent evt = PointerDownEvent.GetPooled(new Event
                   {
                       type = EventType.MouseDown,
                       button = 0,
                       mousePosition = control.worldBound.center
                   }))
            {
                control.SendEvent(evt);
            }
        }

        private static void SendPointerUp(VisualElement control, Vector2 position)
        {
            using (PointerUpEvent evt = PointerUpEvent.GetPooled(new Event
                   {
                       type = EventType.MouseUp,
                       button = 0,
                       mousePosition = position
                   }))
            {
                control.SendEvent(evt);
            }
        }

        private static void AssertContained(Rect outer, Rect inner, string label)
        {
            Assert.That(inner.xMin, Is.GreaterThanOrEqualTo(outer.xMin - 1f), label);
            Assert.That(inner.yMin, Is.GreaterThanOrEqualTo(outer.yMin - 1f), label);
            Assert.That(inner.xMax, Is.LessThanOrEqualTo(outer.xMax + 1f), label);
            Assert.That(inner.yMax, Is.LessThanOrEqualTo(outer.yMax + 1f), label);
        }

        private static void AssertRectApproximately(Rect actual, Rect expected, string label)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(1f), label + " x");
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(1f), label + " y");
            Assert.That(actual.width, Is.EqualTo(expected.width).Within(1f), label + " width");
            Assert.That(actual.height, Is.EqualTo(expected.height).Within(1f), label + " height");
        }

        private static void AssertInsetOverlayMatchesSafeContent(MobilePageShell shell)
        {
            Rect safe = shell.SafeArea.worldBound;
            Rect overlay = shell.OverlayLayer.worldBound;
            Assert.That(overlay.xMin,
                Is.EqualTo(safe.xMin + shell.SafeArea.resolvedStyle.paddingLeft).Within(1f));
            Assert.That(overlay.yMin,
                Is.EqualTo(safe.yMin + shell.SafeArea.resolvedStyle.paddingTop).Within(1f));
            Assert.That(overlay.xMax,
                Is.EqualTo(safe.xMax - shell.SafeArea.resolvedStyle.paddingRight).Within(1f));
            Assert.That(overlay.yMax,
                Is.EqualTo(safe.yMax - shell.SafeArea.resolvedStyle.paddingBottom).Within(1f));
        }
    }
}
