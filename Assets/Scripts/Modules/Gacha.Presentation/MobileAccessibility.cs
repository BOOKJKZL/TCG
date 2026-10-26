using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gacha.Presentation
{
    // Explicit registration only: names, labels and arbitrary visual-tree nodes are never inferred as semantics.
    public static class MobileAccessibility
    {
        private static MobileAccessibilitySession session;
        private static MobileAccessibilitySession Session => session ?? (session = new MobileAccessibilitySession());
        public static IDisposable RegisterScreen(VisualElement root, string semanticId, int priority = 0) =>
            Session.RegisterScreen(root, semanticId, priority);
        public static IDisposable RegisterModal(VisualElement root, string semanticId, Func<bool> isVisible, Func<bool> dismiss = null) =>
            Session.RegisterModal(root, semanticId, isVisible, dismiss);
        public static IDisposable RegisterButton(VisualElement root, string semanticId, Func<string> label,
            Func<bool> canActivate, Func<bool> selected, Func<bool> loading, Func<bool> activate, Func<string> value = null) =>
            Session.RegisterButton(root, semanticId, label, canActivate, selected, loading, activate, value);
        public static IDisposable RegisterText(VisualElement root, string semanticId, Func<string> label, bool heading = false) =>
            Session.RegisterText(root, semanticId, label, heading);
        public static void Refresh() => session?.Refresh();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            session?.Dispose();
            session = null;
        }
    }

    public sealed class MobileAccessibilitySession : IDisposable
    {
        private sealed class Scope : IDisposable
        {
            public MobileAccessibilitySession Owner;
            public VisualElement Root;
            public string LocalId;
            public string Id;
            public bool Modal;
            public int Priority;
            public Func<bool> Visible;
            public Func<bool> Dismiss;
            public bool Attached;
            public bool WasVisible;
            public long Order;
            public bool Disposed;
            public void Attach(AttachToPanelEvent _) { Attached = true; Owner.EnsurePoll(); Owner.Refresh(); }
            public void Detach(DetachFromPanelEvent _) { Attached = false; Owner.EnsurePoll(); Owner.Refresh(); }
            public void Dispose() => Owner.Remove(this);
        }

        private sealed class Registration : IDisposable
        {
            public MobileAccessibilitySession Owner;
            public VisualElement Root;
            public string LocalId;
            public string Id;
            public Func<string> Label;
            public Func<string> Value;
            public Func<bool> CanActivate;
            public Func<bool> Selected;
            public Func<bool> Loading;
            public Func<bool> Activate;
            public MobileAccessibilityRole Role;
            public bool Attached;
            public bool Disposed;
            public IVisualElementScheduledItem FocusRefresh;
            public void Attach(AttachToPanelEvent _) { Attached = true; Owner.Refresh(); }
            public void Detach(DetachFromPanelEvent _) { Attached = false; Owner.Refresh(); }
            public void Dispose() => Owner.Remove(this);
        }

        private readonly List<Scope> scopes = new List<Scope>();
        private readonly List<Registration> registrations = new List<Registration>();
        private readonly Dictionary<string, Registration> current = new Dictionary<string, Registration>(StringComparer.Ordinal);
        private readonly Func<VisualElement, Rect> frameProvider;
        private VisualElement pollRoot;
        private IVisualElementScheduledItem poll;
        private long activationOrder;
        private bool refreshing;
        private bool disposed;

        public MobileAccessibilitySession(bool connectToPlatform = true, Func<VisualElement, Rect> frameProvider = null)
        {
            this.frameProvider = frameProvider ?? MobileAccessibilityGeometry.OverlayFrame;
            Native = new MobileAccessibilityNativeHierarchy(Frame, TryActivate, Focus, DismissModal, connectToPlatform);
        }

        public MobileAccessibilityModel Model { get; } = new MobileAccessibilityModel();
        public MobileAccessibilityNativeHierarchy Native { get; }

        public IDisposable RegisterScreen(VisualElement root, string semanticId, int priority = 0) =>
            AddScope(root, semanticId, false, priority, null, null);
        public IDisposable RegisterModal(VisualElement root, string semanticId, Func<bool> isVisible, Func<bool> dismiss = null) =>
            AddScope(root, semanticId, true, 0, isVisible ?? throw new ArgumentNullException(nameof(isVisible)), dismiss);

        public IDisposable RegisterButton(VisualElement root, string semanticId, Func<string> label,
            Func<bool> canActivate, Func<bool> selected, Func<bool> loading, Func<bool> activate, Func<string> value = null)
        {
            if (canActivate == null || activate == null)
                throw new ArgumentNullException(canActivate == null ? nameof(canActivate) : nameof(activate));
            return Add(root, semanticId, label, MobileAccessibilityRole.Button, canActivate, selected, loading, activate, value);
        }

        public IDisposable RegisterText(VisualElement root, string semanticId, Func<string> label, bool heading = false) =>
            Add(root, semanticId, label, heading ? MobileAccessibilityRole.Heading : MobileAccessibilityRole.Text,
                null, null, null, null, null);

        private IDisposable AddScope(VisualElement root, string id, bool modal, int priority, Func<bool> visible, Func<bool> dismiss)
        {
            Validate(root, id);
            if (scopes.Any(scope => scope.Root == root))
                throw new ArgumentException("This root already owns an accessibility scope.", nameof(root));
            var scope = new Scope { Owner = this, Root = root, LocalId = id, Modal = modal,
                Priority = priority, Visible = visible, Dismiss = dismiss, Attached = root.panel != null };
            scopes.Add(scope);
            root.RegisterCallback<AttachToPanelEvent>(scope.Attach);
            root.RegisterCallback<DetachFromPanelEvent>(scope.Detach);
            EnsurePoll();
            try { Refresh(); }
            catch { Remove(scope); throw; }
            return scope;
        }

        private IDisposable Add(VisualElement root, string id, Func<string> label, MobileAccessibilityRole role,
            Func<bool> canActivate, Func<bool> selected, Func<bool> loading, Func<bool> activate, Func<string> value)
        {
            Validate(root, id);
            var entry = new Registration { Owner = this, Root = root, LocalId = id,
                Label = label ?? throw new ArgumentNullException(nameof(label)), Role = role, CanActivate = canActivate,
                Selected = selected, Loading = loading, Activate = activate, Value = value, Attached = root.panel != null };
            registrations.Add(entry);
            root.RegisterCallback<AttachToPanelEvent>(entry.Attach);
            root.RegisterCallback<DetachFromPanelEvent>(entry.Detach);
            try { Refresh(); }
            catch { Remove(entry); throw; }
            return entry;
        }

        private void Validate(VisualElement root, string id)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(MobileAccessibilitySession));
            if (root == null)
                throw new ArgumentNullException(nameof(root));
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("An explicit, stable semantic ID is required.", nameof(id));
        }

        private Scope NearestScope(VisualElement element)
        {
            for (VisualElement currentElement = element; currentElement != null; currentElement = currentElement.parent)
            {
                Scope scope = scopes.FirstOrDefault(candidate => !candidate.Disposed && candidate.Root == currentElement);
                if (scope != null)
                    return scope;
            }
            return null;
        }

        private string ScopeId(Scope scope)
        {
            if (scope == null)
                return null;
            if (!scope.Modal)
                return scope.LocalId;
            Scope parent = NearestScope(scope.Root.parent);
            string parentId = ScopeId(parent);
            return parentId == null ? null : parentId + "/" + scope.LocalId;
        }

        public void Refresh()
        {
            if (disposed || refreshing)
                return;
            refreshing = true;
            try
            {
                var layers = new List<MobileAccessibilityLayer>();
                foreach (Scope scope in scopes)
                {
                    scope.Id = ScopeId(scope);
                    if (scope.Id == null)
                        continue;
                    bool visible = scope.Attached && MobileAccessibilityGeometry.IsVisible(scope.Root) && (scope.Visible?.Invoke() ?? true);
                    if (visible && !scope.WasVisible)
                        scope.Order = ++activationOrder;
                    scope.WasVisible = visible;
                    layers.Add(new MobileAccessibilityLayer { Id = scope.Id, IsModal = scope.Modal, IsVisible = visible,
                        ParentId = scope.Modal ? ScopeId(NearestScope(scope.Root.parent)) : null,
                        Priority = scope.Priority, Order = scope.Order });
                }
                current.Clear();
                var data = new List<MobileAccessibilityNodeData>();
                foreach (Registration entry in registrations)
                {
                    Scope scope = NearestScope(entry.Root);
                    if (entry.Disposed || !entry.Attached || scope?.Id == null || !MobileAccessibilityGeometry.IsVisible(entry.Root))
                        continue;
                    entry.Id = scope.Id + "/" + entry.LocalId;
                    if (current.ContainsKey(entry.Id))
                        throw new InvalidOperationException("Duplicate accessibility semantic ID: " + entry.Id);
                    current.Add(entry.Id, entry);
                    bool loading = entry.Loading?.Invoke() == true;
                    data.Add(new MobileAccessibilityNodeData { Id = entry.Id, ScopeId = scope.Id, Label = entry.Label(),
                        Role = entry.Role, CanActivate = entry.CanActivate?.Invoke() == true && !loading,
                        IsSelected = entry.Selected?.Invoke() == true, IsLoading = loading,
                        Value = loading ? CardUiText.Get("common.status.loading") : entry.Value?.Invoke() });
                }
                bool screenChanged = Model.Update(layers, data);
                Native.Update(Model, screenChanged);
            }
            finally
            {
                refreshing = false;
            }
        }

        public bool TryActivate(string semanticId)
        {
            Refresh();
            if (disposed || !Model.Contains(semanticId) || !current.TryGetValue(semanticId, out Registration entry) ||
                entry.Disposed || entry.CanActivate?.Invoke() != true || entry.Loading?.Invoke() == true)
                return false;
            return entry.Activate?.Invoke() == true;
        }

        public void Focus(string semanticId)
        {
            Refresh();
            if (disposed || !Model.Contains(semanticId) || !current.TryGetValue(semanticId, out Registration entry))
                return;
            Model.RememberFocus(semanticId);
            MobileAccessibilityGeometry.RevealInScrollViews(entry.Root);
            entry.FocusRefresh?.Pause();
            entry.FocusRefresh = entry.Root.schedule.Execute(Refresh);
        }

        public bool DismissModal()
        {
            Refresh();
            Scope top = scopes.FirstOrDefault(scope => scope.Id == Model.ActiveScopeId && scope.Modal);
            return !disposed && top?.Dismiss?.Invoke() == true;
        }

        private Rect Frame(string id) => !disposed && Model.Contains(id) && current.TryGetValue(id, out Registration entry)
            ? frameProvider(entry.Root) : Rect.zero;

        private void EnsurePoll()
        {
            VisualElement next = disposed ? null : scopes.LastOrDefault(scope => !scope.Disposed && scope.Attached)?.Root;
            if (next == pollRoot)
                return;
            poll?.Pause();
            poll = null;
            pollRoot = next;
            if (next != null)
                poll = next.schedule.Execute(Refresh).Every(100);
        }

        private void Remove(Registration entry)
        {
            if (entry.Disposed)
                return;
            entry.Disposed = true;
            entry.FocusRefresh?.Pause();
            entry.Root.UnregisterCallback<AttachToPanelEvent>(entry.Attach);
            entry.Root.UnregisterCallback<DetachFromPanelEvent>(entry.Detach);
            registrations.Remove(entry);
            Refresh();
        }

        private void Remove(Scope scope)
        {
            if (scope.Disposed)
                return;
            bool wasRefreshing = refreshing;
            refreshing = true;
            try
            {
                scope.Disposed = true;
                // Remove the whole scope atomically: children must never temporarily fall back to the page.
                foreach (Registration entry in registrations.Where(entry => scope.Root == entry.Root || scope.Root.Contains(entry.Root)).ToArray())
                    Remove(entry);
                foreach (Scope child in scopes.Where(child => child != scope && scope.Root.Contains(child.Root)).ToArray())
                    Remove(child);
                scope.Root.UnregisterCallback<AttachToPanelEvent>(scope.Attach);
                scope.Root.UnregisterCallback<DetachFromPanelEvent>(scope.Detach);
                scopes.Remove(scope);
            }
            finally { refreshing = wasRefreshing; }
            EnsurePoll();
            Refresh();
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            poll?.Pause();
            foreach (Scope scope in scopes.ToArray())
                Remove(scope);
            foreach (Registration entry in registrations.ToArray())
                Remove(entry);
            current.Clear();
            Native.Dispose();
            Model.Update(Array.Empty<MobileAccessibilityLayer>(), Array.Empty<MobileAccessibilityNodeData>());
        }
    }
}
