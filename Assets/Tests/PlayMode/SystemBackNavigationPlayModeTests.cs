using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Threading.Tasks;
using Gacha.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Gacha.Tests.PlayMode
{
    public sealed class SystemBackNavigationPlayModeTests
    {
        [UnityTest]
        public IEnumerator BackConsumesTopDocumentOnceAndReleasesOnHideDisableAndDispose()
        {
            var lower = new GameObject("System back lower fixture");
            var upper = new GameObject("System back upper fixture");
            try
            {
                UIDocument lowerDocument = Document(lower);
                UIDocument upperDocument = Document(upper);
                var low = lower.AddComponent<SystemBackNavigation>();
                var high = upper.AddComponent<SystemBackNavigation>();
                int lowCount = 0;
                int highCount = 0;
                low.Bind(lowerDocument.rootVisualElement, () => { lowCount++; return true; }, 1000);
                high.Bind(upperDocument.rootVisualElement, () => { highCount++; return true; }, 2000);
                yield return null;
                Assert.That(low.HandleBack(), Is.False);
                Focus(upperDocument.rootVisualElement);
                Assert.That(high.HandleBack(), Is.True);
                using (NavigationCancelEvent cancel = NavigationCancelEvent.GetPooled())
                    upperDocument.rootVisualElement.SendEvent(cancel);
                Assert.That(highCount, Is.EqualTo(1), "Escape and UITK cancel must not close two levels in one frame.");
                Assert.That(lowCount, Is.Zero);

                upperDocument.rootVisualElement.style.display = DisplayStyle.None;
                yield return null;
                Assert.That(low.HandleBack(), Is.True);
                Assert.That(lowCount, Is.EqualTo(1));
                upperDocument.rootVisualElement.style.display = DisplayStyle.Flex;
                high.enabled = false;
                yield return null;
                Assert.That(low.HandleBack(), Is.True);
                Assert.That(highCount, Is.EqualTo(1));
                high.enabled = true;
                high.DisposeBinding();
                yield return null;
                Focus(lowerDocument.rootVisualElement);
                using (NavigationCancelEvent cancel = NavigationCancelEvent.GetPooled())
                    lowerDocument.rootVisualElement.SendEvent(cancel);
                Assert.That(lowCount, Is.EqualTo(3));
                Assert.That(highCount, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(upper);
                Object.DestroyImmediate(lower);
            }
        }

        [UnityTest]
        public IEnumerator HomeBackRequiresExplicitExitConfirmationAndCancelsSafely()
        {
            var host = new GameObject("Home exit fixture");
            MobileHomePresenter presenter = null;
            try
            {
                Type type = RuntimeType("MainMenuController");
                var controller = (MonoBehaviour)host.AddComponent(type);
                controller.enabled = false;
                presenter = new MobileHomePresenter(host, () => { }, () => { }, () => { }, () => { });
                Set(controller, "homePresenter", presenter);
                int exitCalls = 0;
                Set(controller, "quitOverrideForTests", new Action(() => exitCalls++));
                host.SetActive(true);
                yield return null;
                MethodInfo back = type.GetMethod("TryHandleBack");
                Assert.That(back.Invoke(controller, null), Is.EqualTo(true));
                Assert.That(presenter.ExitConfirmation.IsVisible, Is.True);
                Assert.That(exitCalls, Is.Zero);
                back.Invoke(controller, null);
                Assert.That(presenter.ExitConfirmation.IsVisible, Is.False);
                Assert.That(exitCalls, Is.Zero);
                back.Invoke(controller, null);
                VisualElement confirm = presenter.ExitConfirmation.Confirm.Root;
                float focusDeadline = Time.realtimeSinceStartup + 2f;
                do
                {
                    yield return null;
                    confirm.Focus();
                }
                while ((presenter.ExitConfirmation.Root.resolvedStyle.display != DisplayStyle.Flex ||
                        confirm.panel?.focusController?.focusedElement != confirm) &&
                       Time.realtimeSinceStartup < focusDeadline);
                Assert.That(presenter.ExitConfirmation.Root.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(confirm.panel?.focusController?.focusedElement, Is.SameAs(confirm));
                using (KeyDownEvent down = KeyDownEvent.GetPooled(new Event
                       { type = EventType.KeyDown, keyCode = KeyCode.Return }))
                    confirm.SendEvent(down);
                using (KeyUpEvent up = KeyUpEvent.GetPooled(new Event
                       { type = EventType.KeyUp, keyCode = KeyCode.Return }))
                    confirm.SendEvent(up);
                Assert.That(exitCalls, Is.EqualTo(1));
                Assert.That(presenter.ExitConfirmation.IsVisible, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [UnityTest]
        public IEnumerator FadeTimeFailureRestoresLoadingStateAndCanRetry()
        {
            var host = new GameObject("Transition failure fixture");
            try
            {
                Type transitionType = RuntimeType("SceneTransition");
                Component transition = host.AddComponent(transitionType);
                Set(transition, "sceneLoaderOverrideForTests", new Func<string, AsyncOperation>(
                    _ => throw new InvalidOperationException("Synthetic failure after fade")));
                Component loader = host.AddComponent(RuntimeType("LoadManager"));
                Exception failure = null;
                int failures = 0;
                MethodInfo load = loader.GetType().GetMethod("LoadScene", new[]
                {
                    typeof(string), typeof(Action), typeof(Action<Exception>)
                });
                Action<Exception> failed = exception => { failure = exception; failures++; };
                load.Invoke(loader, new object[] { "fixture", new Action(() => Assert.Fail("Failure cannot complete")), failed });
                Assert.That(failure, Is.Null, "Failure must occur beyond the original synchronous caller.");
                for (int frame = 0; frame < 20 && failure == null; frame++)
                    yield return null;
                Assert.That(failure, Is.TypeOf<InvalidOperationException>());
                Assert.That(loader.GetType().GetField("load").GetValue(loader), Is.EqualTo(false));
                Assert.That(Get(loader, "loadCounter"), Is.EqualTo(0));
                failure = null;
                load.Invoke(loader, new object[] { "fixture", null, failed });
                for (int frame = 0; frame < 20 && failure == null; frame++)
                    yield return null;
                Assert.That(failures, Is.EqualTo(2));
                Assert.That(Get(loader, "loadCounter"), Is.EqualTo(0));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void GachaBackDuringCommitConsumesInputWithoutNavigationOrStateReset()
        {
            var host = new GameObject("Gacha transaction back fixture");
            host.SetActive(false);
            try
            {
                Type type = RuntimeType("GachaViewController");
                Component controller = host.AddComponent(type);
                FieldInfo state = type.GetField("pageState", BindingFlags.NonPublic | BindingFlags.Instance);
                object opening = Enum.Parse(state.FieldType, "Opening");
                state.SetValue(controller, opening);
                Assert.That(type.GetMethod("TryHandleBack").Invoke(controller, null), Is.EqualTo(true));
                Assert.That(state.GetValue(controller), Is.EqualTo(opening));
                Assert.That(Get(controller, "navigationRequested"), Is.EqualTo(false));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [UnityTest]
        public IEnumerator NativeDropdownAndTextEditCancelBeforeTheirContainingPage()
        {
            var host = new GameObject("Native cancel fixture");
            try
            {
                UIDocument document = Document(host);
                VisualElement root = document.rootVisualElement;
                var dropdown = new DropdownField("Filter", new List<string> { "All", "Owned" }, 0);
                var text = new TextField("Search") { value = "keep this query" };
                root.Add(dropdown);
                root.Add(text);
                var binding = host.AddComponent<SystemBackNavigation>();
                int pageBacks = 0;
                binding.Bind(root, () => { pageBacks++; return true; }, 3000);
                yield return null;
                dropdown.Focus();
                using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled())
                    dropdown.SendEvent(submit);
                yield return null;
                yield return null;
                Assert.That(root.panel.visualTree.Q<VisualElement>(className: GenericDropdownMenu.ussClassName), Is.Not.Null);
                Assert.That(binding.HandleBack(), Is.True);
                Assert.That(root.panel.visualTree.Q<VisualElement>(className: GenericDropdownMenu.ussClassName), Is.Null,
                    "The real dropdown must close before the containing filter/page.");
                Assert.That(pageBacks, Is.Zero);
                // A second source reporting the same physical press must not close the page too.
                Set(binding, "pendingEscapeFrame", Time.frameCount);
                typeof(SystemBackNavigation).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(binding, null);
                Assert.That(pageBacks, Is.Zero);

                yield return null;
                text.Focus();
                using (KeyDownEvent escape = KeyDownEvent.GetPooled(new Event
                       { type = EventType.KeyDown, keyCode = KeyCode.Escape }))
                    text.SendEvent(escape);
                using (NavigationCancelEvent cancel = NavigationCancelEvent.GetPooled())
                    root.SendEvent(cancel);
                Assert.That(pageBacks, Is.Zero, "Text editing and the following cancel are one back action.");
                Assert.That(text.value, Is.EqualTo("keep this query"));
                var focused = root.panel.focusController.focusedElement as VisualElement;
                Assert.That(focused == text || (focused != null && text.Contains(focused)), Is.False);
                yield return null;
                Assert.That(binding.HandleBack(), Is.True);
                Assert.That(pageBacks, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void InactiveTransitionFailsImmediatelyWithoutLeavingLoadingPending(bool inactiveObject)
        {
            var host = new GameObject("Inactive transition fixture");
            try
            {
                var transition = (MonoBehaviour)host.AddComponent(RuntimeType("SceneTransition"));
                Component loader = host.AddComponent(RuntimeType("LoadManager"));
                if (inactiveObject)
                    host.SetActive(false);
                else
                    transition.enabled = false;
                int failures = 0;
                Exception failure = null;
                loader.GetType().GetMethod("LoadScene", new[] { typeof(string), typeof(Action), typeof(Action<Exception>) })
                    .Invoke(loader, new object[] { "fixture", new Action(() => Assert.Fail("Inactive owner cannot complete")),
                        new Action<Exception>(exception => { failures++; failure = exception; }) });
                Assert.That(failures, Is.EqualTo(1));
                Assert.That(failure, Is.TypeOf<InvalidOperationException>());
                Assert.That(Get(transition, "pendingFailure"), Is.Null);
                Assert.That(Get(transition, "transition"), Is.Null);
                Assert.That(Get(loader, "loadCounter"), Is.EqualTo(0));
                Assert.That(loader.GetType().GetField("load").GetValue(loader), Is.EqualTo(false));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [UnityTest]
        public IEnumerator ContentFaultAfterDisposalDoesNotUpdateControlsNavigateOrPlayFeedback()
        {
            var host = new GameObject("Disposed content navigation fixture");
            var pending = new TaskCompletionSource<bool>();
            int navigations = 0;
            int errorCues = 0;
            Action<FeedbackCue> observeFeedback = cue => { if (cue == FeedbackCue.Error) errorCues++; };
            UIFeedbackService.FeedbackPlayed += observeFeedback;
            try
            {
                var controller = host.AddComponent<ContentManagementController>();
                controller.enabled = false;
                var label = new Label("initial");
                Set(controller, "catalogStatus", label);
                Set(controller, "suspendOperationsOverrideForTests", new Func<Task>(() => pending.Task));
                Set(controller, "sceneLoaderOverrideForTests", new Action<string>(_ => navigations++));
                Assert.That(controller.TryHandleBack(), Is.True);
                string statusBeforeDisposal = label.text;
                Object.DestroyImmediate(controller);
                LogAssert.Expect(LogType.Warning, "Content shutdown could not suspend the active queue: Synthetic disposal fault");
                pending.SetException(new InvalidOperationException("Synthetic disposal fault"));
                yield return null;
                yield return null;
                Assert.That(label.text, Is.EqualTo(statusBeforeDisposal));
                Assert.That(navigations, Is.Zero);
                Assert.That(errorCues, Is.Zero);
                Assert.That(Get(controller, "navigationRequested"), Is.EqualTo(true), "Disposed view state must not be revived.");
            }
            finally
            {
                UIFeedbackService.FeedbackPlayed -= observeFeedback;
                Object.DestroyImmediate(host);
            }
        }

        private static UIDocument Document(GameObject host)
        {
            var document = host.AddComponent<UIDocument>();
            document.panelSettings = Resources.Load<PanelSettings>("UI/Collection Panel Settings");
            return document;
        }

        private static void Focus(VisualElement root)
        {
            root.focusable = true;
            root.Focus();
        }

        private static Type RuntimeType(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name)).First(type => type != null);
        private static void Set(object owner, string field, object value) => owner.GetType()
            .GetField(field, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance).SetValue(owner, value);
        private static object Get(object owner, string field) => owner.GetType()
            .GetField(field, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance).GetValue(owner);
    }
}
