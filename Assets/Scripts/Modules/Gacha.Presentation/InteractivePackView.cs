using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gacha.Presentation
{
    public static class InteractivePackMeshBuilder
    {
        public const int SegmentCount = 8;
        public const int VerticesPerHalf = (SegmentCount + 1) * 2;
        public const int IndicesPerHalf = SegmentCount * 6;
        public const float MaximumTearOffsetRatio = 0.27f;

        public static Rect ResolvePackRect(Rect host, float widthToHeightRatio)
        {
            if (!IsFinite(host.width) || !IsFinite(host.height) ||
                host.width <= 0f || host.height <= 0f)
                return Rect.zero;
            float maximumPackWidth = host.width / (1f + MaximumTearOffsetRatio * 2f);
            float packWidth = Mathf.Min(maximumPackWidth, host.height * widthToHeightRatio);
            float packHeight = packWidth / widthToHeightRatio;
            return new Rect(
                host.x + (host.width - packWidth) * 0.5f,
                host.y + (host.height - packHeight) * 0.5f,
                packWidth,
                packHeight);
        }

        public static void FillHalf(
            Vertex[] vertices,
            ushort[] indices,
            Rect packRect,
            float rotationDegrees,
            float tearProgress,
            bool rightHalf,
            bool frontFacing,
            Color32 tint)
        {
            if (vertices == null || vertices.Length != VerticesPerHalf)
                throw new ArgumentException("Pack half vertex buffer has the wrong size.", nameof(vertices));
            if (indices == null || indices.Length != IndicesPerHalf)
                throw new ArgumentException("Pack half index buffer has the wrong size.", nameof(indices));

            float radians = rotationDegrees * Mathf.Deg2Rad;
            float cosine = Mathf.Cos(radians);
            float sine = Mathf.Sin(radians);
            float projectedHalfWidth = Mathf.Max(
                packRect.width * 0.035f,
                Mathf.Abs(cosine) * packRect.width * 0.5f);
            float centerX = packRect.center.x;
            float tearOffset = Mathf.Clamp01(tearProgress) * packRect.width * MaximumTearOffsetRatio;
            float firstX = rightHalf ? centerX + tearOffset : centerX - projectedHalfWidth - tearOffset;
            float secondX = rightHalf ? centerX + projectedHalfWidth + tearOffset : centerX - tearOffset;
            float firstUv = rightHalf ? 0.5f : 0f;
            float secondUv = rightHalf ? 1f : 0.5f;
            if (!frontFacing)
            {
                firstUv = 1f - firstUv;
                secondUv = 1f - secondUv;
            }

            float skew = sine * packRect.height * 0.035f;
            for (int row = 0; row <= SegmentCount; row++)
            {
                float t = row / (float)SegmentCount;
                float y = Mathf.Lerp(packRect.yMin, packRect.yMax, t);
                float firstY = y + (1f - t * 2f) * (rightHalf ? 0f : skew);
                float secondY = y + (1f - t * 2f) * (rightHalf ? -skew : 0f);
                float seamWave = row == 0 || row == SegmentCount
                    ? 0f
                    : (row % 2 == 0 ? 1f : -1f) * packRect.width * 0.009f;
                if (rightHalf)
                    firstX = centerX + tearOffset + seamWave;
                else
                    secondX = centerX - tearOffset + seamWave;

                int vertex = row * 2;
                vertices[vertex] = NewVertex(firstX, firstY, firstUv, t, tint);
                vertices[vertex + 1] = NewVertex(secondX, secondY, secondUv, t, tint);
            }

            int index = 0;
            for (ushort row = 0; row < SegmentCount; row++)
            {
                ushort topFirst = (ushort)(row * 2);
                ushort topSecond = (ushort)(topFirst + 1);
                ushort bottomFirst = (ushort)(topFirst + 2);
                ushort bottomSecond = (ushort)(topFirst + 3);
                indices[index++] = topFirst;
                indices[index++] = topSecond;
                indices[index++] = bottomSecond;
                indices[index++] = bottomSecond;
                indices[index++] = bottomFirst;
                indices[index++] = topFirst;
            }
        }

        private static Vertex NewVertex(float x, float y, float u, float v, Color32 tint) =>
            new Vertex
            {
                position = new Vector3(x, y, Vertex.nearZ),
                tint = tint,
                uv = new Vector2(Mathf.Clamp01(u), Mathf.Clamp01(v))
            };

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public sealed class InteractivePackView : VisualElement, IDisposable
    {
        private static readonly ushort[] QuadIndices = { 0, 1, 2, 2, 3, 0 };
        private readonly Vertex[] leftVertices = new Vertex[InteractivePackMeshBuilder.VerticesPerHalf];
        private readonly Vertex[] rightVertices = new Vertex[InteractivePackMeshBuilder.VerticesPerHalf];
        private readonly ushort[] leftIndices = new ushort[InteractivePackMeshBuilder.IndicesPerHalf];
        private readonly ushort[] rightIndices = new ushort[InteractivePackMeshBuilder.IndicesPerHalf];
        private readonly Vertex[] quadVertices = new Vertex[4];
        private readonly HashSet<int> capturedPointers = new HashSet<int>();
        private readonly VisualElement focusRing;
        private readonly bool requireTwoPointersToTear;
        private ProductOpeningPackPresentation presentation;
        private InteractivePackGesture gesture;
        private Texture frontTexture;
        private Texture backTexture;
        private bool interactionEnabled = true;
        private bool acceptedRaised;
        private bool disposed;

        public InteractivePackView(
            ProductOpeningPackPresentation packPresentation,
            bool requireTwoPointersToTear)
        {
            presentation = packPresentation ?? throw new ArgumentNullException(nameof(packPresentation));
            this.requireTwoPointersToTear = requireTwoPointersToTear;
            gesture = CreateGesture(presentation);
            AddToClassList("interactive-pack");
            focusable = true;
            tabIndex = 0;
            pickingMode = PickingMode.Position;
            focusRing = new VisualElement { pickingMode = PickingMode.Ignore };
            focusRing.AddToClassList("interactive-pack__focus-ring");
            Add(focusRing);
            generateVisualContent += GeneratePackVisual;
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<PointerCancelEvent>(OnPointerCancel);
            RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
            RegisterCallback<KeyDownEvent>(OnKeyDown);
            RegisterCallback<FocusInEvent>(OnFocusIn);
            RegisterCallback<FocusOutEvent>(OnFocusOut);
        }

        public event Action TearAccepted;
        public InteractivePackGesturePhase Phase => gesture.Phase;
        public float RotationDegrees => gesture.RotationDegrees;
        public float TearProgress => gesture.TearProgress;
        public bool IsInteractionEnabled => interactionEnabled && !disposed;
        public ProductOpeningPackPresentation Presentation => presentation;

        public void SetPresentation(ProductOpeningPackPresentation value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            CancelInteraction(true);
            presentation = value;
            gesture = CreateGesture(value);
            acceptedRaised = false;
            MarkDirtyRepaint();
        }

        public void SetTextures(Texture front, Texture back = null)
        {
            frontTexture = front;
            backTexture = back ?? front;
            MarkDirtyRepaint();
        }

        public void SetInteractionEnabled(bool enabled)
        {
            interactionEnabled = enabled;
            focusRing.EnableInClassList("is-disabled", !enabled);
            if (!enabled)
                CancelInteraction(true);
        }

        public void ResetInteraction(float rotationDegrees = 0f)
        {
            CancelInteraction(true);
            gesture.Reset(rotationDegrees);
            acceptedRaised = false;
            MarkDirtyRepaint();
        }

        public bool AcceptFromAccessibleAction()
        {
            if (!IsInteractionEnabled || !gesture.TryAccept())
                return false;
            RaiseAcceptedOnce();
            MarkDirtyRepaint();
            return true;
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            CancelInteraction(true);
            generateVisualContent -= GeneratePackVisual;
            UnregisterCallback<PointerDownEvent>(OnPointerDown);
            UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            UnregisterCallback<PointerUpEvent>(OnPointerUp);
            UnregisterCallback<PointerCancelEvent>(OnPointerCancel);
            UnregisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            UnregisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
            UnregisterCallback<KeyDownEvent>(OnKeyDown);
            UnregisterCallback<FocusInEvent>(OnFocusIn);
            UnregisterCallback<FocusOutEvent>(OnFocusOut);
            TearAccepted = null;
        }

        private InteractivePackGesture CreateGesture(ProductOpeningPackPresentation value) =>
            new InteractivePackGesture(
                requireTwoPointersToTear,
                singlePointerPullNormalized: value.SinglePointerPullRatio,
                dualPointerPullNormalized: value.DualPointerPullRatio,
                acceptanceThreshold: value.AcceptanceThreshold);

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (!IsInteractionEnabled || evt.button != 0 ||
                !gesture.PointerDown(evt.pointerId, Normalize(evt.position)))
                return;
            capturedPointers.Add(evt.pointerId);
            this.CapturePointer(evt.pointerId);
            Focus();
            evt.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (!capturedPointers.Contains(evt.pointerId))
                return;
            if (gesture.PointerMove(evt.pointerId, Normalize(evt.position)))
                RaiseAcceptedOnce();
            MarkDirtyRepaint();
            evt.StopPropagation();
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (!capturedPointers.Remove(evt.pointerId))
                return;
            gesture.PointerUp(evt.pointerId);
            if (this.HasPointerCapture(evt.pointerId))
                this.ReleasePointer(evt.pointerId);
            MarkDirtyRepaint();
            evt.StopPropagation();
        }

        private void OnPointerCancel(PointerCancelEvent evt)
        {
            if (!capturedPointers.Contains(evt.pointerId))
                return;
            CancelInteraction(true);
            evt.StopPropagation();
        }

        private void OnPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            if (!capturedPointers.Contains(evt.pointerId))
                return;
            CancelInteraction(false);
        }

        private void OnDetachFromPanel(DetachFromPanelEvent evt) => CancelInteraction(false);

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter &&
                evt.keyCode != KeyCode.Space)
                return;
            if (AcceptFromAccessibleAction())
                evt.StopPropagation();
        }

        private void OnFocusIn(FocusInEvent evt) => focusRing.AddToClassList("is-focused");
        private void OnFocusOut(FocusOutEvent evt) => focusRing.RemoveFromClassList("is-focused");

        private Vector2 Normalize(Vector3 worldPosition)
        {
            Vector2 local = this.WorldToLocal((Vector2)worldPosition);
            Rect rect = contentRect;
            return rect.width <= 0f || rect.height <= 0f
                ? new Vector2(0.5f, 0.5f)
                : new Vector2(
                    (local.x - rect.xMin) / rect.width,
                    (local.y - rect.yMin) / rect.height);
        }

        private void CancelInteraction(bool releasePointers)
        {
            if (releasePointers)
            {
                int[] pointerIds = new int[capturedPointers.Count];
                capturedPointers.CopyTo(pointerIds);
                capturedPointers.Clear();
                foreach (int pointerId in pointerIds)
                {
                    if (this.HasPointerCapture(pointerId))
                        this.ReleasePointer(pointerId);
                }
            }
            else
            {
                capturedPointers.Clear();
            }
            gesture.Cancel();
            MarkDirtyRepaint();
        }

        private void RaiseAcceptedOnce()
        {
            if (acceptedRaised)
                return;
            acceptedRaised = true;
            TearAccepted?.Invoke();
        }

        private void GeneratePackVisual(MeshGenerationContext context)
        {
            Rect packRect = InteractivePackMeshBuilder.ResolvePackRect(
                contentRect,
                presentation.WidthToHeightRatio);
            if (packRect.width <= 0f || packRect.height <= 0f)
                return;

            float cosine = Mathf.Cos(gesture.RotationDegrees * Mathf.Deg2Rad);
            bool frontFacing = cosine >= 0f;
            Texture texture = frontFacing ? frontTexture : backTexture;
            byte shade = (byte)Mathf.RoundToInt(Mathf.Lerp(172f, 255f, Mathf.Abs(cosine)));
            var tint = new Color32(shade, shade, shade, 255);
            InteractivePackMeshBuilder.FillHalf(
                leftVertices,
                leftIndices,
                packRect,
                gesture.RotationDegrees,
                gesture.TearProgress,
                false,
                frontFacing,
                tint);
            InteractivePackMeshBuilder.FillHalf(
                rightVertices,
                rightIndices,
                packRect,
                gesture.RotationDegrees,
                gesture.TearProgress,
                true,
                frontFacing,
                tint);
            DrawSurface(context, leftVertices, leftIndices, texture);
            DrawSurface(context, rightVertices, rightIndices, texture);
            DrawFoilDetails(context, packRect, cosine, frontFacing);
        }

        private static void DrawSurface(
            MeshGenerationContext context,
            Vertex[] vertices,
            ushort[] indices,
            Texture texture)
        {
            MeshWriteData mesh = context.Allocate(vertices.Length, indices.Length, texture);
            mesh.SetAllVertices(vertices);
            mesh.SetAllIndices(indices);
        }

        private void DrawFoilDetails(
            MeshGenerationContext context,
            Rect packRect,
            float cosine,
            bool frontFacing)
        {
            float projectedWidth = Mathf.Max(packRect.width * 0.07f, Mathf.Abs(cosine) * packRect.width);
            float left = packRect.center.x - projectedWidth * 0.5f;
            float tearOffset = gesture.TearProgress * packRect.width *
                InteractivePackMeshBuilder.MaximumTearOffsetRatio;
            float sealHeight = packRect.height * presentation.TopSealHeightRatio;
            var sealColor = frontFacing
                ? new Color32(245, 214, 128, 105)
                : new Color32(105, 119, 142, 135);
            DrawQuad(context, new Rect(left - tearOffset, packRect.yMin, projectedWidth * 0.5f, sealHeight), sealColor);
            DrawQuad(context, new Rect(packRect.center.x + tearOffset, packRect.yMin, projectedWidth * 0.5f, sealHeight), sealColor);

            if (!frontFacing)
            {
                float seamWidth = Mathf.Max(2f, projectedWidth * presentation.BackSeamWidthRatio);
                DrawQuad(
                    context,
                    new Rect(packRect.center.x - tearOffset - seamWidth, packRect.yMin, seamWidth, packRect.height),
                    new Color32(36, 44, 62, 145));
                DrawQuad(
                    context,
                    new Rect(packRect.center.x + tearOffset, packRect.yMin, seamWidth, packRect.height),
                    new Color32(36, 44, 62, 145));
            }

            float highlightX = cosine >= 0f ? left + projectedWidth * 0.18f : left + projectedWidth * 0.72f;
            DrawQuad(
                context,
                new Rect(highlightX, packRect.yMin + sealHeight, Mathf.Max(2f, projectedWidth * 0.05f),
                    packRect.height - sealHeight * 1.35f),
                new Color32(255, 255, 255, 44));
        }

        private void DrawQuad(MeshGenerationContext context, Rect rect, Color32 tint)
        {
            if (rect.width <= 0f || rect.height <= 0f)
                return;
            quadVertices[0] = QuadVertex(rect.xMin, rect.yMin, tint);
            quadVertices[1] = QuadVertex(rect.xMax, rect.yMin, tint);
            quadVertices[2] = QuadVertex(rect.xMax, rect.yMax, tint);
            quadVertices[3] = QuadVertex(rect.xMin, rect.yMax, tint);
            MeshWriteData mesh = context.Allocate(4, 6, null);
            mesh.SetAllVertices(quadVertices);
            mesh.SetAllIndices(QuadIndices);
        }

        private static Vertex QuadVertex(float x, float y, Color32 tint) => new Vertex
        {
            position = new Vector3(x, y, Vertex.nearZ),
            tint = tint,
            uv = Vector2.zero
        };
    }

    public sealed class InteractivePackCarousel : IDisposable
    {
        private const int VisibleSlotCount = 5;

        private sealed class Slot
        {
            public VisualElement Root;
            public InteractivePackView Pack;
            public EventCallback<ClickEvent> Click;
            public EventCallback<KeyDownEvent> KeyDown;
            public int Offset;
        }

        private readonly List<Slot> slots = new List<Slot>(VisibleSlotCount);
        private readonly Label positionLabel;
        private bool disposed;
        private int itemCount = 10;
        private int selectedIndex;

        public InteractivePackCarousel(ProductOpeningPackPresentation presentation, bool requireTwoPointersToTear)
        {
            Root = new VisualElement { name = "interactive-pack-carousel" };
            Root.AddToClassList("interactive-pack-carousel");
            var rail = new VisualElement { name = "interactive-pack-carousel-rail" };
            rail.AddToClassList("interactive-pack-carousel__rail");
            Root.Add(rail);

            for (int offset = -2; offset <= 2; offset++)
            {
                var slot = new Slot
                {
                    Root = new VisualElement { focusable = offset != 0, tabIndex = offset == 0 ? -1 : 0 },
                    Pack = new InteractivePackView(presentation, requireTwoPointersToTear),
                    Offset = offset
                };
                slot.Root.AddToClassList("interactive-pack-carousel__slot");
                slot.Root.AddToClassList(SlotClass(offset));
                slot.Pack.SetInteractionEnabled(offset == 0);
                if (offset != 0)
                    slot.Pack.pickingMode = PickingMode.Ignore;
                slot.Root.Add(slot.Pack);
                if (offset == 0)
                    slot.Pack.TearAccepted += OnTearAccepted;
                else
                {
                    Slot captured = slot;
                    slot.Click = _ => SelectOffset(captured.Offset);
                    slot.KeyDown = evt =>
                    {
                        if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter &&
                            evt.keyCode != KeyCode.Space)
                            return;
                        SelectOffset(captured.Offset);
                        evt.StopPropagation();
                    };
                    slot.Root.RegisterCallback(slot.Click);
                    slot.Root.RegisterCallback(slot.KeyDown);
                }
                slots.Add(slot);
                rail.Add(slot.Root);
            }

            positionLabel = new Label { name = "interactive-pack-carousel-position" };
            positionLabel.AddToClassList("interactive-pack-carousel__position");
            Root.Add(positionLabel);
            RefreshSlots();
        }

        public event Action TearAccepted;
        public event Action<int> SelectionChanged;
        public VisualElement Root { get; }
        public InteractivePackView SelectedPack => slots[2].Pack;
        public int ItemCount => itemCount;
        public int SelectedIndex => selectedIndex;

        public void SetItemCount(int count)
        {
            if (count < 1 || count > 10)
                throw new ArgumentOutOfRangeException(nameof(count));
            itemCount = count;
            selectedIndex = Mathf.Clamp(selectedIndex, 0, count - 1);
            RefreshSlots();
        }

        public void SetPresentation(ProductOpeningPackPresentation presentation)
        {
            foreach (Slot slot in slots)
                slot.Pack.SetPresentation(presentation);
        }

        public void SetTextures(Texture front, Texture back = null)
        {
            foreach (Slot slot in slots)
                slot.Pack.SetTextures(front, back);
        }

        public bool Select(int index)
        {
            if (disposed || index < 0 || index >= itemCount || index == selectedIndex)
                return false;
            selectedIndex = index;
            foreach (Slot slot in slots)
                slot.Pack.ResetInteraction();
            RefreshSlots();
            UIFeedbackService.Play(FeedbackCue.ButtonClick);
            SelectionChanged?.Invoke(selectedIndex);
            return true;
        }

        public void Reset()
        {
            selectedIndex = 0;
            foreach (Slot slot in slots)
                slot.Pack.ResetInteraction();
            RefreshSlots();
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            foreach (Slot slot in slots)
            {
                if (slot.Offset == 0)
                    slot.Pack.TearAccepted -= OnTearAccepted;
                else
                {
                    slot.Root.UnregisterCallback(slot.Click);
                    slot.Root.UnregisterCallback(slot.KeyDown);
                }
                slot.Pack.Dispose();
            }
            slots.Clear();
            TearAccepted = null;
            SelectionChanged = null;
        }

        private void SelectOffset(int offset)
        {
            int index = (selectedIndex + offset) % itemCount;
            if (index < 0)
                index += itemCount;
            Select(index);
        }

        private void RefreshSlots()
        {
            foreach (Slot slot in slots)
            {
                int index = (selectedIndex + slot.Offset) % itemCount;
                if (index < 0)
                    index += itemCount;
                slot.Root.userData = index;
                slot.Root.tooltip = $"{index + 1} / {itemCount}";
            }
            positionLabel.text = $"{selectedIndex + 1} / {itemCount}";
        }

        private void OnTearAccepted() => TearAccepted?.Invoke();

        private static string SlotClass(int offset)
        {
            switch (offset)
            {
                case -2: return "interactive-pack-carousel__slot--far-left";
                case -1: return "interactive-pack-carousel__slot--near-left";
                case 0: return "interactive-pack-carousel__slot--selected";
                case 1: return "interactive-pack-carousel__slot--near-right";
                default: return "interactive-pack-carousel__slot--far-right";
            }
        }
    }
}
