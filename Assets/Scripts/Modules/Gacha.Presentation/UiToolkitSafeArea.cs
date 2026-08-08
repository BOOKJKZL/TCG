using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gacha.Presentation
{
    public readonly struct SafeAreaInsets
    {
        public SafeAreaInsets(float left, float top, float right, float bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        public float Left { get; }
        public float Top { get; }
        public float Right { get; }
        public float Bottom { get; }
    }

    public sealed class UiToolkitSafeAreaBinding : IDisposable
    {
        private readonly Action<UiToolkitSafeAreaBinding> onDisposed;
        private readonly EventCallback<GeometryChangedEvent> geometryChanged;
        private readonly float? compactBasePadding;
        private VisualElement root;
        private IVisualElementScheduledItem poll;
        private bool active;
        private bool capturedBasePadding;
        private float baseLeft;
        private float baseTop;
        private float baseRight;
        private float baseBottom;
        private bool hasApplied;
        private float appliedLeft;
        private float appliedTop;
        private float appliedRight;
        private float appliedBottom;
        private bool appliedCompact;

        internal UiToolkitSafeAreaBinding(
            VisualElement root,
            Action<UiToolkitSafeAreaBinding> onDisposed,
            float? compactBasePadding)
        {
            this.root = root;
            this.onDisposed = onDisposed;
            this.compactBasePadding = compactBasePadding;
            geometryChanged = OnGeometryChanged;
            root.AddToClassList(UiToolkitSafeArea.BoundClass);
            Resume();
        }

        public bool IsDisposed => root == null;
        public bool IsActive => root != null && active;
        public SafeAreaInsets AppliedPadding { get; private set; }
        public event Action<SafeAreaInsets> PaddingChanged;

        public void Suspend()
        {
            if (root == null || !active)
                return;

            root.UnregisterCallback(geometryChanged);
            poll?.Pause();
            active = false;
        }

        public void Resume()
        {
            if (root == null || active)
                return;

            root.RegisterCallback(geometryChanged);
            if (poll == null)
                poll = root.schedule.Execute(Apply).Every(250);
            else
                poll.Resume();
            active = true;
            Apply();
        }

        public void Dispose()
        {
            VisualElement boundRoot = root;
            if (boundRoot == null)
                return;

            Suspend();
            poll = null;
            boundRoot.RemoveFromClassList(UiToolkitSafeArea.BoundClass);
            boundRoot.RemoveFromClassList(UiToolkitSafeArea.CompactClass);
            if (capturedBasePadding)
            {
                boundRoot.style.paddingLeft = baseLeft;
                boundRoot.style.paddingTop = baseTop;
                boundRoot.style.paddingRight = baseRight;
                boundRoot.style.paddingBottom = baseBottom;
            }

            root = null;
            onDisposed?.Invoke(this);
        }

        internal void Apply()
        {
            VisualElement boundRoot = root;
            VisualElement panelRoot = boundRoot?.panel?.visualTree;
            float panelWidth = panelRoot?.resolvedStyle.width ?? 0f;
            float panelHeight = panelRoot?.resolvedStyle.height ?? 0f;
            if (panelWidth <= 0f || panelHeight <= 0f || Screen.width <= 0 || Screen.height <= 0)
                return;

            if (!capturedBasePadding)
            {
                baseLeft = UiToolkitSafeArea.FiniteOrZero(boundRoot.resolvedStyle.paddingLeft);
                baseTop = UiToolkitSafeArea.FiniteOrZero(boundRoot.resolvedStyle.paddingTop);
                baseRight = UiToolkitSafeArea.FiniteOrZero(boundRoot.resolvedStyle.paddingRight);
                baseBottom = UiToolkitSafeArea.FiniteOrZero(boundRoot.resolvedStyle.paddingBottom);
                capturedBasePadding = true;
            }

            SafeAreaInsets insets = UiToolkitSafeArea.CalculateInsets(
                Screen.safeArea,
                Screen.width,
                Screen.height,
                point => RuntimePanelUtils.ScreenToPanel(boundRoot.panel, point));
            bool nextCompact = UiToolkitSafeArea.ShouldUseCompactLayout(
                panelWidth - insets.Left - insets.Right,
                Screen.safeArea.width);
            float effectiveLeft = UiToolkitSafeArea.ResolveBasePadding(baseLeft, nextCompact, compactBasePadding);
            float effectiveTop = UiToolkitSafeArea.ResolveBasePadding(baseTop, nextCompact, compactBasePadding);
            float effectiveRight = UiToolkitSafeArea.ResolveBasePadding(baseRight, nextCompact, compactBasePadding);
            float effectiveBottom = UiToolkitSafeArea.ResolveBasePadding(baseBottom, nextCompact, compactBasePadding);
            float nextLeft = effectiveLeft + insets.Left;
            float nextTop = effectiveTop + insets.Top;
            float nextRight = effectiveRight + insets.Right;
            float nextBottom = effectiveBottom + insets.Bottom;
            if (!hasApplied ||
                !Mathf.Approximately(appliedLeft, nextLeft) ||
                !Mathf.Approximately(appliedTop, nextTop) ||
                !Mathf.Approximately(appliedRight, nextRight) ||
                !Mathf.Approximately(appliedBottom, nextBottom))
            {
                boundRoot.style.paddingLeft = nextLeft;
                boundRoot.style.paddingTop = nextTop;
                boundRoot.style.paddingRight = nextRight;
                boundRoot.style.paddingBottom = nextBottom;
                AppliedPadding = new SafeAreaInsets(nextLeft, nextTop, nextRight, nextBottom);
                PaddingChanged?.Invoke(AppliedPadding);
                appliedLeft = nextLeft;
                appliedTop = nextTop;
                appliedRight = nextRight;
                appliedBottom = nextBottom;
            }
            if (!hasApplied || appliedCompact != nextCompact)
            {
                boundRoot.EnableInClassList(UiToolkitSafeArea.CompactClass, nextCompact);
                appliedCompact = nextCompact;
            }
            hasApplied = true;
        }

        private void OnGeometryChanged(GeometryChangedEvent _)
        {
            Apply();
        }
    }

    public static class UiToolkitSafeArea
    {
        internal const string BoundClass = "safe-area-bound";
        internal const string CompactClass = "mobile-layout--compact";
        private const float CompactPanelWidth = 980f;
        private const float CompactPixelWidth = 800f;
        private static readonly Dictionary<VisualElement, UiToolkitSafeAreaBinding> Bindings =
            new Dictionary<VisualElement, UiToolkitSafeAreaBinding>();

        public static UiToolkitSafeAreaBinding Attach(
            VisualElement root,
            float? compactBasePadding = null)
        {
            if (root == null)
                throw new ArgumentNullException(nameof(root));
            if (Bindings.TryGetValue(root, out UiToolkitSafeAreaBinding existing) && !existing.IsDisposed)
                return existing;

            UiToolkitSafeAreaBinding binding = null;
            binding = new UiToolkitSafeAreaBinding(root, disposed =>
            {
                if (Bindings.TryGetValue(root, out UiToolkitSafeAreaBinding current) &&
                    ReferenceEquals(current, disposed))
                {
                    Bindings.Remove(root);
                }
            }, compactBasePadding);
            Bindings[root] = binding;
            return binding;
        }

        public static SafeAreaInsets CalculateInsets(
            Rect safeAreaPixels,
            int screenWidth,
            int screenHeight,
            Func<Vector2, Vector2> screenToPanel)
        {
            if (screenWidth <= 0 || screenHeight <= 0 || screenToPanel == null)
                return new SafeAreaInsets(0f, 0f, 0f, 0f);

            float safeLeft = Mathf.Clamp(safeAreaPixels.xMin, 0f, screenWidth);
            float safeRight = Mathf.Clamp(safeAreaPixels.xMax, safeLeft, screenWidth);
            float safeBottom = Mathf.Clamp(safeAreaPixels.yMin, 0f, screenHeight);
            float safeTop = Mathf.Clamp(safeAreaPixels.yMax, safeBottom, screenHeight);

            Vector2 fullTopLeft = screenToPanel(Vector2.zero);
            Vector2 fullBottomRight = screenToPanel(new Vector2(screenWidth, screenHeight));
            Vector2 safeTopLeft = screenToPanel(new Vector2(safeLeft, screenHeight - safeTop));
            Vector2 safeBottomRight = screenToPanel(new Vector2(safeRight, screenHeight - safeBottom));
            if (!IsFinite(fullTopLeft) || !IsFinite(fullBottomRight) ||
                !IsFinite(safeTopLeft) || !IsFinite(safeBottomRight))
            {
                return new SafeAreaInsets(0f, 0f, 0f, 0f);
            }

            return new SafeAreaInsets(
                Mathf.Max(0f, safeTopLeft.x - fullTopLeft.x),
                Mathf.Max(0f, safeTopLeft.y - fullTopLeft.y),
                Mathf.Max(0f, fullBottomRight.x - safeBottomRight.x),
                Mathf.Max(0f, fullBottomRight.y - safeBottomRight.y));
        }

        public static bool ShouldUseCompactLayout(float safePanelWidth, float safePixelWidth)
        {
            return safePanelWidth > 0f && safePixelWidth > 0f &&
                   (safePanelWidth < CompactPanelWidth || safePixelWidth <= CompactPixelWidth);
        }

        public static float ResolveBasePadding(
            float normalBasePadding,
            bool compact,
            float? compactBasePadding)
        {
            return compact && compactBasePadding.HasValue
                ? Mathf.Max(0f, compactBasePadding.Value)
                : Mathf.Max(0f, FiniteOrZero(normalBasePadding));
        }

        internal static float FiniteOrZero(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }

        private static bool IsFinite(Vector2 point)
        {
            return !float.IsNaN(point.x) && !float.IsInfinity(point.x) &&
                   !float.IsNaN(point.y) && !float.IsInfinity(point.y);
        }
    }
}
