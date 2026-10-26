using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Gacha.Presentation
{
    // One owner per page. A higher-priority document (first-run setup) consumes back first.
    public sealed class SystemBackNavigation : MonoBehaviour
    {
        private static readonly List<SystemBackNavigation> bindings = new List<SystemBackNavigation>();
        private static int lastInputFrame = -1;
        private VisualElement root;
        private VisualElement panelRoot;
        private Func<bool> handleBack;
        private int priority;
        private InputAction escape;
        private int pendingEscapeFrame = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            bindings.Clear();
            lastInputFrame = -1;
        }

        public void Bind(VisualElement documentRoot, Func<bool> callback, int inputPriority = 0)
        {
            DisposeBinding();
            root = documentRoot ?? throw new ArgumentNullException(nameof(documentRoot));
            handleBack = callback ?? throw new ArgumentNullException(nameof(callback));
            priority = inputPriority;
            root.RegisterCallback<AttachToPanelEvent>(OnAttach);
            root.RegisterCallback<DetachFromPanelEvent>(OnDetach);
            AttachPanel(root.panel);
            escape = new InputAction("System back", InputActionType.Button, "<Keyboard>/escape");
            escape.performed += OnEscape;
            if (isActiveAndEnabled)
                escape.Enable();
            bindings.Add(this);
        }

        public bool HandleBack()
        {
            SystemBackNavigation owner = ActiveOwner();
            if (owner != this)
                return false;
            // Android Escape can also generate a UITK cancel in the same input update.
            if (lastInputFrame == Time.frameCount)
                return true;
            // Send through UITK first. Native dropdowns live beside UIDocuments under the panel
            // and stop cancel propagation after closing; only an unconsumed event reaches the page.
            VisualElement target = root.panel.focusController.focusedElement as VisualElement ?? root;
            using (NavigationCancelEvent cancel = NavigationCancelEvent.GetPooled())
            {
                cancel.target = target;
                target.SendEvent(cancel);
            }
            return true;
        }

        public void DisposeBinding()
        {
            bindings.Remove(this);
            root?.UnregisterCallback<AttachToPanelEvent>(OnAttach);
            root?.UnregisterCallback<DetachFromPanelEvent>(OnDetach);
            AttachPanel(null);
            if (escape != null)
            {
                escape.performed -= OnEscape;
                escape.Dispose();
                escape = null;
            }
            root = null;
            handleBack = null;
            pendingEscapeFrame = -1;
        }

        private void OnEnable() => escape?.Enable();
        private void OnDisable()
        {
            escape?.Disable();
            pendingEscapeFrame = -1;
        }
        private void OnDestroy() => DisposeBinding();
        private void OnEscape(InputAction.CallbackContext _) => pendingEscapeFrame = Time.frameCount;

        private void LateUpdate()
        {
            if (pendingEscapeFrame < 0)
                return;
            int inputFrame = pendingEscapeFrame;
            pendingEscapeFrame = -1;
            if (inputFrame == Time.frameCount && lastInputFrame != inputFrame)
                HandleBack();
        }

        private void OnAttach(AttachToPanelEvent evt) => AttachPanel(evt.destinationPanel);
        private void OnDetach(DetachFromPanelEvent _) => AttachPanel(null);

        private void AttachPanel(IPanel panel)
        {
            if (panelRoot != null)
            {
                panelRoot.UnregisterCallback<NavigationCancelEvent>(ObserveCancel, TrickleDown.TrickleDown);
                panelRoot.UnregisterCallback<NavigationCancelEvent>(OnCancel);
                panelRoot.UnregisterCallback<KeyDownEvent>(OnTextEscape, TrickleDown.TrickleDown);
            }
            panelRoot = panel?.visualTree;
            if (panelRoot == null)
                return;
            panelRoot.RegisterCallback<NavigationCancelEvent>(ObserveCancel, TrickleDown.TrickleDown);
            panelRoot.RegisterCallback<NavigationCancelEvent>(OnCancel);
            panelRoot.RegisterCallback<KeyDownEvent>(OnTextEscape, TrickleDown.TrickleDown);
        }

        private void ObserveCancel(NavigationCancelEvent evt)
        {
            if (ActiveOwner() != this)
                return;
            if (lastInputFrame == Time.frameCount)
                evt.StopImmediatePropagation();
            else
                lastInputFrame = Time.frameCount;
        }

        private void OnCancel(NavigationCancelEvent evt)
        {
            if (ActiveOwner() != this)
                return;
            if (DismissTextEditing() || handleBack?.Invoke() == true)
                evt.StopImmediatePropagation();
        }

        private void OnTextEscape(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Escape || ActiveOwner() != this)
                return;
            if (lastInputFrame == Time.frameCount || DismissTextEditing())
            {
                lastInputFrame = Time.frameCount;
                evt.StopImmediatePropagation();
            }
        }

        private bool DismissTextEditing()
        {
            var focused = root.panel?.focusController.focusedElement as VisualElement;
            for (VisualElement element = focused; element != null; element = element.parent)
            {
                if (!(element is TextField))
                    continue;
                focused.Blur();
                root.focusable = true;
                root.Focus();
                return true;
            }
            return false;
        }

        private static SystemBackNavigation ActiveOwner()
        {
            SystemBackNavigation result = null;
            foreach (SystemBackNavigation candidate in bindings)
            {
                if (candidate == null || !candidate.isActiveAndEnabled || candidate.root?.panel == null)
                    continue;
                bool visible = true;
                for (VisualElement element = candidate.root; element != null; element = element.parent)
                    if (element.resolvedStyle.display == DisplayStyle.None)
                        visible = false;
                if (visible && (result == null || candidate.priority >= result.priority))
                    result = candidate;
            }
            return result;
        }
    }
}
