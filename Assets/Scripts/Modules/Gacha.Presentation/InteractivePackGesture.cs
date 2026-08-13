using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gacha.Presentation
{
    public enum InteractivePackGesturePhase
    {
        Idle,
        Rotating,
        Tearing,
        Accepted
    }

    /// <summary>
    /// Pointer-id aware, panel-coordinate-independent gesture state for an interactive pack.
    /// Callers provide coordinates normalized to the pack hit rectangle.
    /// </summary>
    public sealed class InteractivePackGesture
    {
        private sealed class PointerTrack
        {
            public Vector2 Start;
            public Vector2 Current;
        }

        private const int MaximumPointers = 2;
        private readonly Dictionary<int, PointerTrack> pointers =
            new Dictionary<int, PointerTrack>(MaximumPointers);
        private float rotationAtPointerDown;
        private float dualPointerStartSeparation;
        private bool dualPointerTearEligible;

        public InteractivePackGesture(
            bool requireTwoPointersToTear,
            float dragSlopNormalized = 0.025f,
            float singlePointerPullNormalized = 0.34f,
            float dualPointerPullNormalized = 0.28f,
            float acceptanceThreshold = 0.72f,
            float rotationDegreesPerWidth = 180f)
        {
            RequireTwoPointersToTear = requireTwoPointersToTear;
            DragSlopNormalized = InRange(
                dragSlopNormalized, 0.005f, 0.15f, nameof(dragSlopNormalized));
            SinglePointerPullNormalized = InRange(
                singlePointerPullNormalized, 0.1f, 1f, nameof(singlePointerPullNormalized));
            DualPointerPullNormalized = InRange(
                dualPointerPullNormalized, 0.1f, 1f, nameof(dualPointerPullNormalized));
            AcceptanceThreshold = InRange(
                acceptanceThreshold, 0.5f, 1f, nameof(acceptanceThreshold));
            RotationDegreesPerWidth = InRange(
                rotationDegreesPerWidth, 45f, 360f, nameof(rotationDegreesPerWidth));
        }

        public bool RequireTwoPointersToTear { get; }
        public float DragSlopNormalized { get; }
        public float SinglePointerPullNormalized { get; }
        public float DualPointerPullNormalized { get; }
        public float AcceptanceThreshold { get; }
        public float RotationDegreesPerWidth { get; }
        public InteractivePackGesturePhase Phase { get; private set; }
        public float RotationDegrees { get; private set; }
        public float TearProgress { get; private set; }
        public bool IsAccepted => Phase == InteractivePackGesturePhase.Accepted;
        public int ActivePointerCount => pointers.Count;

        public bool PointerDown(int pointerId, Vector2 normalizedPosition)
        {
            if (IsAccepted || pointers.ContainsKey(pointerId) || pointers.Count >= MaximumPointers)
                return false;

            Vector2 position = Clamp01(normalizedPosition);
            pointers.Add(pointerId, new PointerTrack { Start = position, Current = position });
            if (pointers.Count == 1)
            {
                rotationAtPointerDown = RotationDegrees;
                dualPointerTearEligible = false;
            }
            else
            {
                GetTwoTracks(out PointerTrack first, out PointerTrack second);
                dualPointerStartSeparation = Mathf.Abs(first.Current.x - second.Current.x);
                dualPointerTearEligible = AreOnOppositeSides(first.Start.x, second.Start.x);
                TearProgress = 0f;
            }
            return true;
        }

        /// <summary>Returns true only on the transition that accepts the tear.</summary>
        public bool PointerMove(int pointerId, Vector2 normalizedPosition)
        {
            if (IsAccepted || !pointers.TryGetValue(pointerId, out PointerTrack track))
                return false;

            track.Current = Clamp01(normalizedPosition);
            if (pointers.Count == 2)
                return UpdateDualPointerTear();

            float deltaX = track.Current.x - track.Start.x;
            if (!RequireTwoPointersToTear && IsInTearGutter(track.Start.x))
            {
                float outwardDistance = track.Start.x < 0.5f ? -deltaX : deltaX;
                if (outwardDistance > DragSlopNormalized)
                {
                    Phase = InteractivePackGesturePhase.Tearing;
                    TearProgress = Mathf.Clamp01(outwardDistance / SinglePointerPullNormalized);
                    return AcceptIfReady();
                }
            }

            if (Mathf.Abs(deltaX) <= DragSlopNormalized)
                return false;
            Phase = InteractivePackGesturePhase.Rotating;
            TearProgress = 0f;
            RotationDegrees = NormalizeDegrees(
                rotationAtPointerDown + deltaX * RotationDegreesPerWidth);
            return false;
        }

        public void PointerUp(int pointerId)
        {
            if (!pointers.Remove(pointerId) || IsAccepted)
                return;
            if (pointers.Count == 0)
            {
                ResetTransientState(true);
                return;
            }

            PointerTrack remaining = FirstTrack();
            remaining.Start = remaining.Current;
            rotationAtPointerDown = RotationDegrees;
            TearProgress = 0f;
            Phase = InteractivePackGesturePhase.Idle;
            dualPointerTearEligible = false;
        }

        public void Cancel()
        {
            if (IsAccepted)
                return;
            pointers.Clear();
            ResetTransientState(true);
        }

        public void Reset(float rotationDegrees = 0f)
        {
            pointers.Clear();
            RotationDegrees = NormalizeDegrees(rotationDegrees);
            rotationAtPointerDown = RotationDegrees;
            TearProgress = 0f;
            Phase = InteractivePackGesturePhase.Idle;
            dualPointerTearEligible = false;
        }

        public bool TryAccept()
        {
            if (IsAccepted)
                return false;
            pointers.Clear();
            TearProgress = 1f;
            Phase = InteractivePackGesturePhase.Accepted;
            return true;
        }

        private bool UpdateDualPointerTear()
        {
            if (!dualPointerTearEligible)
                return false;
            GetTwoTracks(out PointerTrack first, out PointerTrack second);
            float separation = Mathf.Abs(first.Current.x - second.Current.x);
            float outwardDistance = separation - dualPointerStartSeparation;
            if (outwardDistance <= DragSlopNormalized)
                return false;
            Phase = InteractivePackGesturePhase.Tearing;
            TearProgress = Mathf.Clamp01(outwardDistance / DualPointerPullNormalized);
            return AcceptIfReady();
        }

        private bool AcceptIfReady()
        {
            if (TearProgress < AcceptanceThreshold)
                return false;
            TearProgress = 1f;
            Phase = InteractivePackGesturePhase.Accepted;
            return true;
        }

        private void ResetTransientState(bool snapRotation)
        {
            TearProgress = 0f;
            Phase = InteractivePackGesturePhase.Idle;
            dualPointerTearEligible = false;
            if (snapRotation)
                RotationDegrees = SnapFace(RotationDegrees);
            rotationAtPointerDown = RotationDegrees;
        }

        private void GetTwoTracks(out PointerTrack first, out PointerTrack second)
        {
            first = null;
            second = null;
            foreach (PointerTrack track in pointers.Values)
            {
                if (first == null)
                    first = track;
                else
                {
                    second = track;
                    break;
                }
            }
            if (first == null || second == null)
                throw new InvalidOperationException("Two active pack pointers are required.");
        }

        private PointerTrack FirstTrack()
        {
            foreach (PointerTrack track in pointers.Values)
                return track;
            throw new InvalidOperationException("No active pack pointer exists.");
        }

        private static bool AreOnOppositeSides(float firstX, float secondX) =>
            (firstX < 0.5f && secondX >= 0.5f) ||
            (secondX < 0.5f && firstX >= 0.5f);

        private static bool IsInTearGutter(float x) => Mathf.Abs(x - 0.5f) <= 0.16f;

        private static Vector2 Clamp01(Vector2 value) => new Vector2(
            Mathf.Clamp01(value.x),
            Mathf.Clamp01(value.y));

        private static float SnapFace(float value)
        {
            float normalized = NormalizeDegrees(value);
            return Mathf.Abs(normalized) <= 90f ? 0f : 180f;
        }

        private static float NormalizeDegrees(float value)
        {
            float normalized = Mathf.Repeat(value + 180f, 360f) - 180f;
            return Mathf.Approximately(normalized, -180f) ? 180f : normalized;
        }

        private static float InRange(float value, float minimum, float maximum, string parameterName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < minimum || value > maximum)
                throw new ArgumentOutOfRangeException(parameterName);
            return value;
        }
    }
}
