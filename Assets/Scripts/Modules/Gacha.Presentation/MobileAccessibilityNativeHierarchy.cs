using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Accessibility;

namespace Gacha.Presentation
{
    // Detached hierarchy updates are testable in Editor; publishing to TalkBack is a separate boundary.
    public sealed class MobileAccessibilityNativeHierarchy : IDisposable
    {
        private sealed class Binding
        {
            public AccessibilityNode Node;
            public Func<bool> Selected;
            public Func<bool> Dismissed;
            public Action<AccessibilityNode, bool> Focus;
        }

        private readonly Dictionary<string, Binding> nodes = new Dictionary<string, Binding>(StringComparer.Ordinal);
        private readonly bool platformSupported;
        private readonly Func<string, Rect> frame;
        private readonly Func<string, bool> select;
        private readonly Action<string> focus;
        private readonly Func<bool> dismiss;
        private bool disposed;

        public MobileAccessibilityNativeHierarchy(Func<string, Rect> frame, Func<string, bool> select,
            Action<string> focus, Func<bool> dismiss, bool connectToPlatform = true)
        {
            this.frame = frame ?? throw new ArgumentNullException(nameof(frame));
            this.select = select ?? throw new ArgumentNullException(nameof(select));
            this.focus = focus ?? throw new ArgumentNullException(nameof(focus));
            this.dismiss = dismiss ?? throw new ArgumentNullException(nameof(dismiss));
            Hierarchy = new AccessibilityHierarchy();
            platformSupported = connectToPlatform && SupportedRuntime();
            if (platformSupported)
                AssistiveSupport.screenReaderStatusChanged += OnScreenReaderChanged;
        }

        public AccessibilityHierarchy Hierarchy { get; }
        public bool IsPublished => platformSupported && ReferenceEquals(AssistiveSupport.activeHierarchy, Hierarchy);
        public static bool SupportsAndroidApi(int apiLevel) => apiLevel >= 26;
        public AccessibilityNode FindNode(string semanticId) => nodes.TryGetValue(semanticId, out Binding binding) ? binding.Node : null;

        public void Update(MobileAccessibilityModel model, bool screenChanged)
        {
            if (disposed)
                return;
            bool changed = false;
            var wanted = new HashSet<string>(model.Nodes.Select(data => data.Id), StringComparer.Ordinal);
            foreach (string old in nodes.Keys.Where(key => !wanted.Contains(key)).ToArray())
            {
                Remove(old);
                changed = true;
            }
            for (int index = 0; index < model.Nodes.Count; index++)
            {
                MobileAccessibilityNodeData data = model.Nodes[index];
                if (!nodes.TryGetValue(data.Id, out Binding binding))
                {
                    string id = data.Id;
                    var node = Hierarchy.InsertNode(index, data.Label);
                    binding = new Binding
                    {
                        Node = node, Selected = () => !disposed && select(id),
                        Dismissed = () => !disposed && dismiss(),
                        Focus = (_, focused) => { if (!disposed && focused) focus(id); }
                    };
                    node.selected += binding.Selected;
                    node.dismissed += binding.Dismissed;
                    node.focusChanged += binding.Focus;
                    node.frameGetter = () => disposed ? Rect.zero : frame(id);
                    nodes.Add(id, binding);
                    changed = true;
                }
                AccessibilityNode target = binding.Node;
                AccessibilityState state = data.IsSelected ? AccessibilityState.Selected : AccessibilityState.None;
                if (data.Role == MobileAccessibilityRole.Button && (!data.CanActivate || data.IsLoading))
                    state |= AccessibilityState.Disabled;
                AccessibilityRole role = data.Role == MobileAccessibilityRole.Button ? AccessibilityRole.Button :
                    data.Role == MobileAccessibilityRole.Heading ? AccessibilityRole.Header : AccessibilityRole.StaticText;
                Rect currentFrame = frame(data.Id);
                bool hasFrame = currentFrame.width > 0f && currentFrame.height > 0f;
                changed |= target.label != data.Label || target.value != (data.Value ?? string.Empty) ||
                    target.hint != (data.Hint ?? string.Empty) || target.state != state || target.role != role ||
                    target.frame != currentFrame || target.isActive != hasFrame;
                target.label = data.Label;
                target.value = data.Value ?? string.Empty;
                target.hint = data.Hint ?? string.Empty;
                target.state = state;
                target.role = role;
                target.isActive = hasFrame;
                target.frame = currentFrame;
            }
            bool newlyPublished = PublishIfAvailable();
            if (!IsPublished)
                return;
            AccessibilityNode requested = model.RequestedFocusId == null ? null : FindNode(model.RequestedFocusId);
            if (screenChanged || newlyPublished)
                AssistiveSupport.notificationDispatcher.SendScreenChanged(requested);
            else if (changed)
                AssistiveSupport.notificationDispatcher.SendLayoutChanged();
        }

        private bool PublishIfAvailable()
        {
            if (!platformSupported || !AssistiveSupport.isScreenReaderEnabled || !nodes.Values.Any(binding => binding.Node.isActive))
            {
                if (IsPublished)
                    AssistiveSupport.activeHierarchy = null;
                return false;
            }
            // Another native owner may be active (for example a future system dialog). Never replace it.
            if (AssistiveSupport.activeHierarchy != null)
                return false;
            AssistiveSupport.activeHierarchy = Hierarchy;
            return true;
        }

        private void OnScreenReaderChanged(bool enabled)
        {
            if (!disposed && enabled && PublishIfAvailable())
                AssistiveSupport.notificationDispatcher.SendScreenChanged();
        }

        private void Remove(string id)
        {
            Binding binding = nodes[id];
            binding.Node.selected -= binding.Selected;
            binding.Node.dismissed -= binding.Dismissed;
            binding.Node.focusChanged -= binding.Focus;
            binding.Node.frameGetter = null;
            Hierarchy.RemoveNode(binding.Node);
            nodes.Remove(id);
        }

        public void Dispose()
        {
            if (disposed)
                return;
            if (IsPublished)
                AssistiveSupport.activeHierarchy = null;
            disposed = true;
            if (platformSupported)
                AssistiveSupport.screenReaderStatusChanged -= OnScreenReaderChanged;
            foreach (string id in nodes.Keys.ToArray())
                Remove(id);
            Hierarchy.Clear();
        }

        private static bool SupportedRuntime()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                    return SupportsAndroidApi(version.GetStatic<int>("SDK_INT"));
            }
            catch (Exception)
            {
                return false;
            }
#else
            return false;
#endif
        }
    }
}
