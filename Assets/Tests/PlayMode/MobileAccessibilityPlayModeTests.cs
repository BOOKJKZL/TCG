using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Gacha.Application;
using Gacha.Domain;
using Gacha.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Accessibility;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Gacha.Tests.PlayMode
{
    public sealed class MobileAccessibilityPlayModeTests
    {
        [UnityTest]
        public IEnumerator ExplicitButtonUpdatesKeepNativeIdentityAndCheckLiveAvailability()
        {
            var host = new GameObject("Accessibility native projection fixture");
            var session = new MobileAccessibilitySession(false, _ => new Rect(72, 120, 200, 60));
            try
            {
                VisualElement root = Document(host).rootVisualElement;
                var button = new VisualElement();
                root.Add(button);
                IDisposable screen = session.RegisterScreen(root, "home");
                string label = string.Empty;
                bool enabled = true;
                bool loading = false;
                bool selected = false;
                int activations = 0;
                IDisposable action = session.RegisterButton(button, "settings", () => label,
                    () => enabled, () => selected, () => loading, () => { activations++; return true; });
                yield return null;
                session.Refresh();
                Assert.That(session.Native.FindNode("home/settings"), Is.Null, "Constructor-time empty labels must not publish unnamed controls.");
                label = "Settings";
                session.Refresh();
                AccessibilityNode node = session.Native.FindNode("home/settings");
                Assert.That(node, Is.Not.Null);
                int nativeId = node.id;
                Assert.That(node.role, Is.EqualTo(AccessibilityRole.Button));
                Assert.That(node.frame, Is.EqualTo(new Rect(72, 120, 200, 60)), "This is an injected frame, not a device measurement.");
                Assert.That(session.Native.IsPublished, Is.False, "A detached managed API test must not publish to OS assistive support.");
                Assert.That(session.TryActivate("home/settings"), Is.True);
                label = "設定";
                selected = true;
                loading = true;
                session.Refresh();
                Assert.That(session.Native.FindNode("home/settings").id, Is.EqualTo(nativeId));
                Assert.That(node.label, Is.EqualTo("設定"));
                Assert.That(node.state.HasFlag(AccessibilityState.Selected), Is.True);
                Assert.That(node.state.HasFlag(AccessibilityState.Disabled), Is.True);
                Assert.That(node.value, Is.EqualTo(CardUiText.Get("common.status.loading")));
                Assert.That(session.TryActivate("home/settings"), Is.False);
                loading = false;
                enabled = false;
                Assert.That(session.TryActivate("home/settings"), Is.False, "Activation must recheck state even before a polling refresh.");
                Assert.That(activations, Is.EqualTo(1));
                label = "   ";
                session.Refresh();
                Assert.That(session.Native.FindNode("home/settings"), Is.Null);
                screen.Dispose();
                action.Dispose();
                screen.Dispose();
                action.Dispose();
                Assert.That(session.Native.Hierarchy.rootNodes, Is.Empty);
                Assert.That(session.TryActivate("home/settings"), Is.False);
            }
            finally
            {
                session.Dispose();
                Object.DestroyImmediate(host);
            }
        }

        [UnityTest]
        public IEnumerator ExitModalExposesOnlyExplicitContentAndDismissesThroughItsExistingCallback()
        {
            var host = new GameObject("Accessibility modal fixture");
            var session = new MobileAccessibilitySession(false, _ => new Rect(10, 20, 300, 80));
            MobileConfirmationPresenter confirmation = null;
            try
            {
                VisualElement root = Document(host).rootVisualElement;
                var source = new Label("Settings");
                root.Add(source);
                session.RegisterScreen(root, "home");
                session.RegisterText(source, "settings", () => source.text);
                confirmation = new MobileConfirmationPresenter();
                root.Add(confirmation.Root);
                session.RegisterModal(confirmation.Root, "exit", () => confirmation.IsVisible, confirmation.TryCancel);
                session.RegisterText(confirmation.Sheet.Title, "title", () => confirmation.Sheet.Title.text, true);
                session.RegisterText(confirmation.Sheet.Body, "body", () => confirmation.Sheet.Body.text);
                yield return null;
                session.Refresh();
                session.Focus("home/settings");
                int cancelled = 0;
                int confirmed = 0;
                confirmation.Show("Leave?", "Your collection stays saved.", "Exit", "Cancel",
                    () => confirmed++, () => cancelled++, false);
                yield return null;
                session.Refresh();
                Assert.That(session.Model.ActiveScopeId, Is.EqualTo("home/exit"));
                Assert.That(session.Native.FindNode("home/settings"), Is.Null);
                Assert.That(session.Native.Hierarchy.rootNodes.Count, Is.EqualTo(2));
                Assert.That(session.Native.FindNode("home/exit/title").role, Is.EqualTo(AccessibilityRole.Header));
                Assert.That(session.DismissModal(), Is.True);
                session.Refresh();
                Assert.That(cancelled, Is.EqualTo(1));
                Assert.That(confirmed, Is.Zero);
                Assert.That(session.Model.RequestedFocusId, Is.EqualTo("home/settings"));
                Assert.That(session.Native.FindNode("home/exit/title"), Is.Null);
            }
            finally
            {
                session.Dispose();
                confirmation?.Dispose();
                Object.DestroyImmediate(host);
            }
        }

        [UnityTest]
        public IEnumerator ReadingOffscreenTextScrollsWithoutActivatingAndHiddenOrDetachedContentRetires()
        {
            var host = new GameObject("Accessibility scroll fixture");
            var session = new MobileAccessibilitySession(false, element => element.worldBound);
            try
            {
                VisualElement root = Document(host).rootVisualElement;
                var scroll = new ScrollView();
                scroll.style.height = 120;
                scroll.style.width = 400;
                var spacer = new VisualElement();
                spacer.style.height = 1000;
                spacer.style.flexShrink = 0;
                var label = new Label("A readable offscreen paragraph");
                label.style.height = 80;
                label.style.flexShrink = 0;
                scroll.Add(spacer);
                scroll.Add(label);
                root.Add(scroll);
                session.RegisterScreen(root, "settings");
                session.RegisterText(label, "paragraph", () => label.text);
                yield return null;
                yield return null;
                session.Refresh();
                Assert.That(session.Model.Contains("settings/paragraph"), Is.True, "Offscreen is not hidden.");
                Assert.That(scroll.scrollOffset.y, Is.EqualTo(0f));
                session.Focus("settings/paragraph");
                yield return null;
                Assert.That(scroll.scrollOffset.y, Is.GreaterThan(0f));
                Assert.That(session.TryActivate("settings/paragraph"), Is.False, "Reading cannot trigger a business action.");
                label.style.display = DisplayStyle.None;
                yield return null;
                session.Refresh();
                Assert.That(session.Native.FindNode("settings/paragraph"), Is.Null);
                label.style.display = DisplayStyle.Flex;
                yield return null;
                session.Refresh();
                Assert.That(session.Native.FindNode("settings/paragraph"), Is.Not.Null);
                root.Remove(scroll);
                session.Refresh();
                Assert.That(session.Native.FindNode("settings/paragraph"), Is.Null);
            }
            finally
            {
                session.Dispose();
                Object.DestroyImmediate(host);
            }
        }

        [UnityTest]
        public IEnumerator FirstRunLanguageSelectionAndNavigationUseLiveStateAndIgnoreLateFailures()
        {
            // Assembly-CSharp owns the scene controller; only its test seam is reflected here.
            Type controllerType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("Gacha.Presentation.FirstRunContentSetupController"))
                .First(type => type != null);
            FieldInfo dismissed = controllerType.GetField("dismissedForSession", BindingFlags.NonPublic | BindingFlags.Static);
            bool previousDismissed = (bool)dismissed.GetValue(null);
            dismissed.SetValue(null, false);
            ApplicationServices.Configure(new CatalogSession(new EmptyCatalogProvider()),
                new LanguageSelectionService(new MemoryLanguageStore(), new[] { "en", "zh", "ja" }));
            var host = new GameObject("Accessible first-run navigation fixture");
            try
            {
                Component controller = host.AddComponent(controllerType);
                int requests = 0;
                Action<Exception> failure = null;
                controllerType.GetField("sceneTransitionOverrideForTests", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(controller, new Action<string, Action<Exception>>((scene, failed) =>
                    {
                        Assert.That(scene, Is.EqualTo("006_ContentScene"));
                        requests++;
                        failure = failed;
                    }));
                yield return null;
                yield return null;
                MobileAccessibility.Refresh();
                var session = (MobileAccessibilitySession)typeof(MobileAccessibility)
                    .GetField("session", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
                Assert.That(session.Model.ActiveScreenId, Is.EqualTo("first-run"));
                Assert.That(session.Native.FindNode("first-run/setup-language-en").state.HasFlag(AccessibilityState.Selected), Is.True);
                Assert.That(session.TryActivate("first-run/setup-language-zh"), Is.True);
                Assert.That(session.TryActivate("first-run/setup-content-language-ja"), Is.True);
                session.Refresh();
                Assert.That(session.Native.FindNode("first-run/setup-language-zh").state.HasFlag(AccessibilityState.Selected), Is.True);
                Assert.That(session.Native.FindNode("first-run/setup-language-en").state.HasFlag(AccessibilityState.Selected), Is.False);
                Assert.That(session.Native.FindNode("first-run/setup-content-language-ja").state.HasFlag(AccessibilityState.Selected), Is.True);
                Assert.That(session.Native.FindNode("first-run/setup-content-language-en").state.HasFlag(AccessibilityState.Selected), Is.False);

                Label status = host.GetComponent<UIDocument>().rootVisualElement.Q<Label>("setup-catalog");
                Assert.That(session.TryActivate("first-run/setup-manage"), Is.True);
                Assert.That(session.TryActivate("first-run/setup-manage"), Is.False);
                Assert.That(session.TryActivate("first-run/setup-later"), Is.False);
                Assert.That(controllerType.GetMethod("TryHandleBack").Invoke(controller, null), Is.True);
                Assert.That(requests, Is.EqualTo(1));
                session.Refresh();
                AccessibilityNode pending = session.Native.FindNode("first-run/setup-manage");
                Assert.That(pending.state.HasFlag(AccessibilityState.Disabled), Is.True);
                Assert.That(pending.value, Is.EqualTo(CardUiText.Get("common.status.loading")));
                Assert.That(status.text, Is.EqualTo(CardUiText.Get("common.navigation.loading")));
                Action<Exception> firstFailure = failure;
                yield return null;
                LogAssert.Expect(LogType.Warning, new Regex("First-run navigation failed:.*fixture navigation failure"));
                firstFailure(new InvalidOperationException("fixture navigation failure"));
                session.Refresh();
                Assert.That(status.text, Is.EqualTo(CardUiText.Get("common.navigation.failed")));
                Assert.That(session.Native.FindNode("first-run/setup-manage").state.HasFlag(AccessibilityState.Disabled), Is.False);
                Assert.That(session.TryActivate("first-run/setup-manage"), Is.True);
                Assert.That(requests, Is.EqualTo(2));
                firstFailure(new InvalidOperationException("obsolete failure must not reset the retry"));
                Assert.That(session.TryActivate("first-run/setup-manage"), Is.False);
                Assert.That(status.text, Is.EqualTo(CardUiText.Get("common.navigation.loading")));

                Object.DestroyImmediate(host);
                string retiredText = status.text;
                failure(new InvalidOperationException("failure after destruction must not touch the retired page"));
                Assert.That(status.text, Is.EqualTo(retiredText));
                Assert.That(session.TryActivate("first-run/setup-manage"), Is.False);
                Assert.That(session.Native.FindNode("first-run/setup-manage"), Is.Null);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                if (host != null) Object.DestroyImmediate(host);
                dismissed.SetValue(null, previousDismissed);
                ContentReturnNavigation.Clear();
                ApplicationServices.Reset();
            }
        }

        private sealed class MemoryLanguageStore : ILanguagePreferenceStore
        {
            public LanguagePreferences Load() => new LanguagePreferences("en", "en");
            public void Save(LanguagePreferences preferences) { }
        }

        private sealed class EmptyCatalogProvider : ICatalogProvider
        {
            public CatalogLoadResult Load() => CatalogLoadResult.Success(new UniversalCatalog(
                Array.Empty<LanguageDefinition>(), Array.Empty<GameDefinition>(), Array.Empty<SetDefinition>(),
                Array.Empty<CollectibleItemDefinition>(), Array.Empty<RarityDefinition>(), Array.Empty<VariantDefinition>(),
                Array.Empty<PrintingDefinition>(), Array.Empty<ProductDefinition>()), 0, 0, 0);
        }

        private static UIDocument Document(GameObject host)
        {
            var document = host.AddComponent<UIDocument>();
            document.panelSettings = Resources.Load<PanelSettings>("UI/Collection Panel Settings");
            return document;
        }
    }
}
