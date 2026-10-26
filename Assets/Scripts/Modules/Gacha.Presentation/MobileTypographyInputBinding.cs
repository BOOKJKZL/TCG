using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gacha.Presentation
{
    /// <summary>Typed font-size adapter; editing, selection and focus remain with the existing controls.</summary>
    public sealed class MobileTypographyInputBinding : IDisposable
    {
        private sealed class PopupOwner { internal MobileTypographyInputBinding Binding; }
        private static readonly ConditionalWeakTable<VisualElement, PopupOwner> PopupOwners =
            new ConditionalWeakTable<VisualElement, PopupOwner>();
        private readonly VisualElement field;
        private readonly Dictionary<VisualElement, StyleLength> originals = new Dictionary<VisualElement, StyleLength>();
        private IVisualElementScheduledItem pendingCapture;
        private VisualElement popup;
        private VisualElement popupDocument;
        private readonly HashSet<VisualElement> priorMenus = new HashSet<VisualElement>();
        private StyleLength originalPopupSize;
        private float pixels;
        private bool disposed;

        public static bool Supports(VisualElement element) =>
            element is TextField || element is DropdownField || element is Toggle;

        public MobileTypographyInputBinding(VisualElement field)
        {
            if (!Supports(field)) throw new ArgumentException("A TextField, DropdownField or Toggle is required.", nameof(field));
            this.field = field;
            Remember(field);
            // BaseField removes an empty label from its hierarchy. Localization may
            // fill it later, so retain the typed label even while it is detached.
            if (field is TextField textField) Remember(textField.labelElement);
            else if (field is DropdownField dropdown) Remember(dropdown.labelElement);
            else if (field is Toggle toggle) Remember(toggle.labelElement);
            // Input internals are deliberately scoped to these three known field types.
            // Absolute sizes prevent inherited font scaling from being applied twice.
            field.Query<TextElement>().ForEach(text => Remember(text));
            if (field is DropdownField)
            {
                field.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
                field.RegisterCallback<NavigationSubmitEvent>(OnSubmit, TrickleDown.TrickleDown);
            }
            field.RegisterCallback<DetachFromPanelEvent>(OnDetach);
        }

        public void Apply(float value)
        {
            if (disposed) return;
            pixels = value;
            foreach (VisualElement element in originals.Keys) SetSize(element, pixels);
            if (popup?.parent != null) SetSize(popup, pixels);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            pendingCapture?.Pause();
            pendingCapture = null;
            field.UnregisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            field.UnregisterCallback<NavigationSubmitEvent>(OnSubmit, TrickleDown.TrickleDown);
            field.UnregisterCallback<DetachFromPanelEvent>(OnDetach);
            foreach (KeyValuePair<VisualElement, StyleLength> pair in originals) pair.Key.style.fontSize = pair.Value;
            originals.Clear();
            if (popup?.parent != null) popup.style.fontSize = originalPopupSize;
            popup = null;
            ReleasePopupOwnership();
        }

        private void Remember(VisualElement element)
        {
            if (element != null && !originals.ContainsKey(element)) originals.Add(element, element.style.fontSize);
        }
        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button == 0) ScheduleCapture();
        }
        private void OnSubmit(NavigationSubmitEvent _) => ScheduleCapture();
        private void ScheduleCapture()
        {
            pendingCapture?.Pause();
            if (field.panel == null) return;
            // Native dropdowns are portaled to the panel's root visual container;
            // that can be outside the UIDocument subtree when it has no UXML asset.
            // Keep one explicit opener per portal, then accept only a newly created menu.
            popupDocument = field.panel.visualTree;
            PopupOwners.GetValue(popupDocument, _ => new PopupOwner()).Binding = this;
            priorMenus.Clear();
            popupDocument.Query<VisualElement>(className: GenericDropdownMenu.ussClassName)
                .ForEach(menu => priorMenus.Add(menu));
            // DropdownField schedules its own menu creation after pointer input.
            pendingCapture = field.schedule.Execute(() => CapturePopup(2)).StartingIn(1);
        }
        private void CapturePopup(int retries)
        {
            pendingCapture = null;
            if (disposed || field.panel == null) return;
            if (popupDocument == null || !PopupOwners.TryGetValue(popupDocument, out PopupOwner owner) ||
                !ReferenceEquals(owner.Binding, this)) return;
            VisualElement candidate = null;
            popupDocument.Query<VisualElement>(className: GenericDropdownMenu.ussClassName).ForEach(menu =>
            {
                if (!priorMenus.Contains(menu)) candidate = menu;
            });
            if (candidate == null)
            {
                if (retries > 0) pendingCapture = field.schedule.Execute(() => CapturePopup(retries - 1)).StartingIn(1);
                return;
            }
            popup = candidate;
            originalPopupSize = popup.style.fontSize;
            popup.style.fontSize = pixels;
        }
        private void OnDetach(DetachFromPanelEvent evt)
        {
            if (!ReferenceEquals(evt.target, field)) return;
            pendingCapture?.Pause();
            pendingCapture = null;
            popup = null;
            ReleasePopupOwnership();
        }

        private void ReleasePopupOwnership()
        {
            if (popupDocument != null && PopupOwners.TryGetValue(popupDocument, out PopupOwner owner) &&
                ReferenceEquals(owner.Binding, this)) owner.Binding = null;
            popupDocument = null;
            priorMenus.Clear();
        }
        private static void SetSize(VisualElement element, float value)
        {
            if (element.style.fontSize.keyword != StyleKeyword.Undefined ||
                !Mathf.Approximately(element.style.fontSize.value.value, value)) element.style.fontSize = value;
        }
    }
}
