using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gacha.Presentation
{
    public static class MobileAccessibilityGeometry
    {
        // The supplied mapping consumes top-left screen pixels, as RuntimePanelUtils.ScreenToPanel does.
        // Invert the measured mapping, not Screen.width / a guessed panel design resolution.
        public static bool TryScreenFrame(Rect panelFrame, Vector2 screenSize,
            Func<Vector2, Vector2> screenToPanel, out Rect screenFrame)
        {
            screenFrame = Rect.zero;
            if (screenToPanel == null || !Finite(screenSize.x) || !Finite(screenSize.y) || screenSize.x <= 0f || screenSize.y <= 0f ||
                !Finite(panelFrame.x) || !Finite(panelFrame.y) || !Finite(panelFrame.width) || !Finite(panelFrame.height))
                return false;
            Vector2 origin = screenToPanel(Vector2.zero);
            Vector2 x = screenToPanel(new Vector2(screenSize.x, 0f));
            Vector2 y = screenToPanel(new Vector2(0f, screenSize.y));
            Vector2 corner = screenToPanel(screenSize);
            Vector2 middle = screenToPanel(screenSize * 0.5f);
            Vector2 dx = x - origin;
            Vector2 dy = y - origin;
            if (!Finite(origin.x) || !Finite(origin.y) || !Finite(dx.x) || !Finite(dx.y) ||
                !Finite(dy.x) || !Finite(dy.y) || !Finite(corner.x) || !Finite(corner.y) ||
                !Finite(middle.x) || !Finite(middle.y) || dx.x <= 0f || dy.y <= 0f ||
                Mathf.Abs(dx.y) > 0.01f || Mathf.Abs(dy.x) > 0.01f ||
                Vector2.Distance(corner, origin + dx + dy) > 0.1f ||
                Vector2.Distance(middle, origin + (dx + dy) * 0.5f) > 0.1f)
                return false;
            float scaleX = screenSize.x / dx.x;
            float scaleY = screenSize.y / dy.y;
            screenFrame = new Rect((panelFrame.x - origin.x) * scaleX,
                (panelFrame.y - origin.y) * scaleY, panelFrame.width * scaleX, panelFrame.height * scaleY);
            return screenFrame.width > 0f && screenFrame.height > 0f;
        }

        public static Rect OverlayFrame(VisualElement element)
        {
            if (element?.panel == null)
                return Rect.zero;
            // RenderTexture/world-space surfaces need a dedicated projection. Do not report fake screen frames.
            var runtimePanel = element.panel as IRuntimePanel;
            if (runtimePanel?.panelSettings == null || runtimePanel.panelSettings.targetTexture != null)
                return Rect.zero;
            return TryScreenFrame(element.worldBound, new Vector2(Screen.width, Screen.height),
                point => RuntimePanelUtils.ScreenToPanel(element.panel, point), out Rect frame) ? frame : Rect.zero;
        }

        public static void RevealInScrollViews(VisualElement element)
        {
            // Scroll only. Reading/focusing an item must never select it or invoke its business callback.
            for (VisualElement ancestor = element?.parent; ancestor != null; ancestor = ancestor.parent)
                if (ancestor is ScrollView scroll && scroll.contentContainer.Contains(element))
                    scroll.ScrollTo(element);
        }

        public static bool IsVisible(VisualElement element)
        {
            if (element?.panel == null)
                return false;
            for (VisualElement current = element; current != null; current = current.parent)
                if (current.resolvedStyle.display == DisplayStyle.None ||
                    current.resolvedStyle.visibility == Visibility.Hidden || current.resolvedStyle.opacity <= 0f)
                    return false;
            return true;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
