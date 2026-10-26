using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Accessibility;
using UnityEngine.UIElements;

namespace Gacha.Presentation
{
    public interface IUiFontScaleSource
    {
        float Scale { get; }
        event Action<float> Changed;
    }

    /// <summary>System preference only; this does not change panel scale or font assets.</summary>
    public sealed class SystemUiFontScaleSource : IUiFontScaleSource
    {
        private Action<float> changed;
        private float lastScale = 1f;
        public float Scale => ReadScale();

        public event Action<float> Changed
        {
            add
            {
                if (changed == null)
                {
                    lastScale = ReadScale();
                    AccessibilitySettings.fontScaleChanged += OnFontScaleChanged;
                    UnityEngine.Application.focusChanged += OnApplicationFocusChanged;
                }
                changed += value;
            }
            remove
            {
                changed -= value;
                if (changed == null)
                {
                    AccessibilitySettings.fontScaleChanged -= OnFontScaleChanged;
                    UnityEngine.Application.focusChanged -= OnApplicationFocusChanged;
                }
            }
        }

        private static float ReadScale()
        {
            RuntimePlatform platform = UnityEngine.Application.platform;
            return platform == RuntimePlatform.Android || platform == RuntimePlatform.IPhonePlayer
                ? MobileTypography.NormalizeScale(AccessibilitySettings.fontScale)
                : 1f;
        }

        private void OnFontScaleChanged(float value) => Publish(value);
        private void OnApplicationFocusChanged(bool focused)
        {
            if (focused) Publish(ReadScale());
        }

        private void Publish(float value)
        {
            float next = MobileTypography.NormalizeScale(value);
            if (Mathf.Approximately(next, lastScale)) return;
            lastScale = next;
            changed?.Invoke(next);
        }
    }

    public static class MobileTypography
    {
        private sealed class ScopeBindings
        {
            internal readonly Dictionary<MobileTypographyProfile, MobileTypographyBinding> Values =
                new Dictionary<MobileTypographyProfile, MobileTypographyBinding>();
        }

        private static readonly ConditionalWeakTable<VisualElement, ScopeBindings> Bindings =
            new ConditionalWeakTable<VisualElement, ScopeBindings>();
        private sealed class TextOwner
        {
            internal MobileTypographyBinding Binding;
            internal MobileTypographyRule Rule;
        }
        private static readonly ConditionalWeakTable<VisualElement, TextOwner> TextOwners =
            new ConditionalWeakTable<VisualElement, TextOwner>();
        private static IUiFontScaleSource testSource;
        public static IUiFontScaleSource SystemSource { get; } = new SystemUiFontScaleSource();

        /// <summary>Test scopes must create and dispose their UI before restoring this source.</summary>
        public static IDisposable OverrideSourceForTests(IUiFontScaleSource source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var restore = new SourceRestore(testSource);
            testSource = source;
            return restore;
        }
        private sealed class SourceRestore : IDisposable
        {
            private readonly IUiFontScaleSource previous;
            private bool disposed;
            internal SourceRestore(IUiFontScaleSource previous) => this.previous = previous;
            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                testSource = previous;
            }
        }

        internal static bool Claim(VisualElement element, MobileTypographyBinding binding, MobileTypographyRule rule)
        {
            if (TextOwners.TryGetValue(element, out TextOwner owner) && !owner.Binding.IsDisposed)
            {
                if (!ReferenceEquals(owner.Binding.Source, binding.Source) ||
                    owner.Rule.BaseSize != rule.BaseSize || owner.Rule.CompactSize != rule.CompactSize)
                    throw new InvalidOperationException("Conflicting typography owners for '" + element.name + "'.");
                return false;
            }
            TextOwners.Remove(element);
            TextOwners.Add(element, new TextOwner { Binding = binding, Rule = rule });
            return true;
        }
        internal static void Release(VisualElement element, MobileTypographyBinding binding)
        {
            if (TextOwners.TryGetValue(element, out TextOwner owner) && ReferenceEquals(owner.Binding, binding))
                TextOwners.Remove(element);
        }

        public static float NormalizeScale(float scale) =>
            float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0f ? 1f : scale;

        /// <summary>
        /// Bind one explicitly owned template/row subtree. The profile is a finite set of
        /// typography roles, not a guess based on resolved sizes. Repeated binding is idempotent.
        /// </summary>
        public static MobileTypographyBinding Bind(
            VisualElement root, MobileTypographyProfile profile, IUiFontScaleSource source = null)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            source ??= testSource ?? SystemSource;
            ScopeBindings scopes = Bindings.GetValue(root, _ => new ScopeBindings());
            if (scopes.Values.TryGetValue(profile, out MobileTypographyBinding existing) && !existing.IsDisposed)
            {
                if (!ReferenceEquals(existing.Source, source))
                    throw new InvalidOperationException("This typography subtree already has a different scale source.");
                existing.RegisterSubtree(root);
                return existing;
            }
            var binding = new MobileTypographyBinding(root, profile, source);
            scopes.Values[profile] = binding;
            return binding;
        }

        public static void Unbind(VisualElement root, MobileTypographyProfile profile)
        {
            if (root != null && Bindings.TryGetValue(root, out ScopeBindings scopes) &&
                scopes.Values.TryGetValue(profile, out MobileTypographyBinding binding))
            {
                binding.Dispose();
                scopes.Values.Remove(profile);
            }
        }

        internal static bool IsCompact(VisualElement element)
        {
            for (VisualElement current = element; current != null; current = current.parent)
                if (current.ClassListContains(UiToolkitSafeArea.CompactClass)) return true;
            return false;
        }
    }

    public sealed class MobileTypographyBinding : IDisposable
    {
        private sealed class Target : IDisposable
        {
            public readonly VisualElement Element;
            public readonly MobileTypographyRule Rule;
            private readonly StyleLength original;
            private readonly MobileTypographyInputBinding input;
            public Target(VisualElement element, MobileTypographyRule rule)
            {
                Element = element;
                Rule = rule;
                original = element.style.fontSize;
                if (MobileTypographyInputBinding.Supports(element))
                    input = new MobileTypographyInputBinding(element);
            }
            public void Apply(float scale)
            {
                float baseline = MobileTypography.IsCompact(Element) ? Rule.CompactSize : Rule.BaseSize;
                float pixels = baseline * scale;
                if (input != null) input.Apply(pixels);
                else if (Element.style.fontSize.keyword != StyleKeyword.Undefined ||
                         !Mathf.Approximately(Element.style.fontSize.value.value, pixels))
                    Element.style.fontSize = pixels;
            }
            public void Dispose()
            {
                input?.Dispose();
                Element.style.fontSize = original;
            }
        }

        private readonly VisualElement root;
        private readonly MobileTypographyProfile profile;
        private readonly Dictionary<VisualElement, Target> targets = new Dictionary<VisualElement, Target>();
        private bool subscribed;
        public IUiFontScaleSource Source { get; }
        public bool IsDisposed { get; private set; }
        public int TargetCount => targets.Count;

        internal MobileTypographyBinding(VisualElement root, MobileTypographyProfile profile, IUiFontScaleSource source)
        {
            this.root = root;
            this.profile = profile;
            Source = source;
            root.RegisterCallback<AttachToPanelEvent>(OnAttach);
            root.RegisterCallback<DetachFromPanelEvent>(OnDetach);
            root.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            try
            {
                RegisterSubtree(root);
                if (root.panel != null) Subscribe();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void RegisterSubtree(VisualElement subtree)
        {
            if (IsDisposed || subtree == null) return;
            Register(subtree);
            subtree.Query<VisualElement>().ForEach(Register);
            if (root.panel != null) Subscribe();
            Refresh();
        }

        public void UnregisterSubtree(VisualElement subtree)
        {
            if (IsDisposed || subtree == null) return;
            var removed = new List<VisualElement>();
            foreach (KeyValuePair<VisualElement, Target> pair in targets)
                if (ReferenceEquals(pair.Key, subtree) || subtree.Contains(pair.Key)) removed.Add(pair.Key);
            foreach (VisualElement element in removed)
            {
                targets[element].Dispose();
                MobileTypography.Release(element, this);
                targets.Remove(element);
            }
            if (targets.Count == 0) Unsubscribe();
        }

        public void Refresh()
        {
            if (IsDisposed) return;
            float scale = MobileTypography.NormalizeScale(Source.Scale);
            foreach (Target target in targets.Values) target.Apply(scale);
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            Unsubscribe();
            root.UnregisterCallback<AttachToPanelEvent>(OnAttach);
            root.UnregisterCallback<DetachFromPanelEvent>(OnDetach);
            root.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            foreach (Target target in targets.Values)
            {
                target.Dispose();
                MobileTypography.Release(target.Element, this);
            }
            targets.Clear();
        }

        private void Register(VisualElement element)
        {
            if (targets.ContainsKey(element) ||
                (!(element is TextElement) && !MobileTypographyInputBinding.Supports(element))) return;
            MobileTypographyRule rule = profile.Match(element);
            if (rule != null && MobileTypography.Claim(element, this, rule))
                targets.Add(element, new Target(element, rule));
        }
        private void Subscribe()
        {
            if (subscribed || IsDisposed || targets.Count == 0) return;
            Source.Changed += OnScaleChanged;
            UiToolkitSafeArea.LayoutChanged += OnLayoutChanged;
            subscribed = true;
            Refresh();
        }
        private void Unsubscribe()
        {
            if (!subscribed) return;
            Source.Changed -= OnScaleChanged;
            UiToolkitSafeArea.LayoutChanged -= OnLayoutChanged;
            subscribed = false;
        }
        private void OnAttach(AttachToPanelEvent evt)
        {
            if (ReferenceEquals(evt.target, root)) Subscribe();
        }
        private void OnDetach(DetachFromPanelEvent evt)
        {
            if (ReferenceEquals(evt.target, root)) Unsubscribe();
        }
        private void OnGeometryChanged(GeometryChangedEvent _) => Refresh();
        private void OnScaleChanged(float _) => Refresh();
        private void OnLayoutChanged(VisualElement safeArea)
        {
            if (safeArea.Contains(root) || root.Contains(safeArea) || ReferenceEquals(root, safeArea))
            {
                Refresh();
                return;
            }
            // Filters/details can be explicitly moved from a page into its sheet.
            foreach (Target target in targets.Values)
                if (safeArea.Contains(target.Element)) { Refresh(); return; }
        }
    }
}
