using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gacha.Presentation
{
    public static class MobileUiTokens
    {
        public const float MinimumTouchTarget = 50f;
        public const float CompactPhysicalWidth = 800f;
        public const float ContentMaxWidth = 1080f;
        public const float SpaceXs = 4f;
        public const float SpaceSm = 8f;
        public const float SpaceMd = 12f;
        public const float SpaceLg = 16f;
        public const float SpaceXl = 24f;
        public const float Space2Xl = 32f;
        public const float RadiusSm = 10f;
        public const float RadiusMd = 14f;
        public const float RadiusLg = 18f;
        public const float RadiusXl = 24f;
        public const int FastMotionMilliseconds = 90;
        public const int StandardMotionMilliseconds = 140;
        public const int DefaultToastMilliseconds = 2600;

        public static float ClampProgress(float value) => Mathf.Clamp(value, 0f, 100f);
    }

    public enum MobileActionTone
    {
        Standard,
        Primary,
        Quiet,
        Danger,
        Navigation
    }

    public enum MobileStatusTone
    {
        Neutral,
        Success,
        Warning,
        Error
    }

    public static class MobileGameDesignSystem
    {
        public const string StyleResourcePath = "UI/MobileGameDesignSystem";
        public const string StyleAttachedClass = "mobile-design-system";

        internal static VisualElement CloneTemplateRoot(string resourcePath, string rootName)
        {
            VisualTreeAsset template = Resources.Load<VisualTreeAsset>(resourcePath);
            if (template == null)
                throw new InvalidOperationException(
                    "The shared mobile UI template could not be loaded from Resources/" +
                    resourcePath + ".uxml.");

            TemplateContainer instance = template.CloneTree();
            VisualElement root = instance.Q<VisualElement>(rootName);
            if (root == null)
                throw new InvalidOperationException(
                    "The shared mobile UI template " + resourcePath +
                    " does not contain the required root '" + rootName + "'.");
            root.RemoveFromHierarchy();
            return root;
        }

        public static void AttachStyle(VisualElement root)
        {
            if (root == null)
                throw new ArgumentNullException(nameof(root));
            if (root.ClassListContains(StyleAttachedClass))
                return;

            StyleSheet styleSheet = Resources.Load<StyleSheet>(StyleResourcePath);
            if (styleSheet == null)
                throw new InvalidOperationException(
                    "The shared mobile UI stylesheet could not be loaded from Resources/" +
                    StyleResourcePath + ".uss.");

            root.styleSheets.Add(styleSheet);
            root.AddToClassList(StyleAttachedClass);
        }
    }

    /// <summary>
    /// Android-safe action control with a permanent VisualElement + Label render hierarchy.
    /// Runtime state changes only affect child text/indicators; the root background node stays stable.
    /// </summary>
    public sealed class MobileActionControl : IDisposable
    {
        private readonly Action clicked;
        private readonly bool playFeedback;
        private readonly bool showPressWhenUnavailable;
        private int pressedPointerId = -1;
        private bool keyboardPressed;
        private bool disposed;
        private bool enabled = true;
        private bool loading;

        public MobileActionControl(
            string name,
            string label,
            Action clicked,
            MobileActionTone tone = MobileActionTone.Standard)
        {
            this.clicked = clicked ?? throw new ArgumentNullException(nameof(clicked));
            playFeedback = true;
            showPressWhenUnavailable = false;
            Root = MobileGameDesignSystem.CloneTemplateRoot(
                "UI/Mobile/MobileAction",
                "mobile-action");
            Root.name = string.IsNullOrWhiteSpace(name) ? "mobile-action" : name;
            Root.focusable = true;
            Root.pickingMode = PickingMode.Position;
            Root.RemoveFromClassList("mobile-action--standard");
            Root.AddToClassList(ToneClass(tone));
            FocusRing = Root.Q<VisualElement>("action-focus-ring");
            SelectionIndicator = Root.Q<VisualElement>("action-selection-indicator");
            Label = Root.Q<Label>("action-label");
            LoadingIndicator = Root.Q<Label>("action-loading");
            SetLabel(label);

            Root.RegisterCallback<PointerDownEvent>(OnPointerDown);
            Root.RegisterCallback<PointerUpEvent>(OnPointerUp);
            Root.RegisterCallback<PointerCancelEvent>(OnPointerCancel);
            Root.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            Root.RegisterCallback<KeyDownEvent>(OnKeyDown);
            Root.RegisterCallback<KeyUpEvent>(OnKeyUp);
            Root.RegisterCallback<BlurEvent>(OnBlur);
            ApplyAvailability();
        }

        public MobileActionControl(
            VisualElement root,
            Action clicked,
            bool playFeedback = false,
            bool showPressWhenUnavailable = true,
            string fallbackLabelClass = "mobile-action__label",
            Label feedbackLabel = null)
        {
            Root = root ?? throw new ArgumentNullException(nameof(root));
            this.clicked = clicked ?? throw new ArgumentNullException(nameof(clicked));
            this.playFeedback = playFeedback;
            this.showPressWhenUnavailable = showPressWhenUnavailable;
            Label = feedbackLabel ?? Root.Q<Label>();
            if (Label == null)
            {
                Label = new Label { pickingMode = PickingMode.Ignore };
                if (!string.IsNullOrWhiteSpace(fallbackLabelClass))
                    Label.AddToClassList(fallbackLabelClass);
                Root.Add(Label);
            }
            Label.pickingMode = PickingMode.Ignore;
            FocusRing = Root.Q<VisualElement>("action-focus-ring");
            SelectionIndicator = Root.Q<VisualElement>("action-selection-indicator");
            LoadingIndicator = Root.Q<Label>("action-loading");
            Root.focusable = true;
            Root.pickingMode = PickingMode.Position;
            Root.RegisterCallback<PointerDownEvent>(OnPointerDown);
            Root.RegisterCallback<PointerUpEvent>(OnPointerUp);
            Root.RegisterCallback<PointerCancelEvent>(OnPointerCancel);
            Root.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            Root.RegisterCallback<KeyDownEvent>(OnKeyDown);
            Root.RegisterCallback<KeyUpEvent>(OnKeyUp);
            Root.RegisterCallback<BlurEvent>(OnBlur);
            ApplyAvailability();
        }

        public VisualElement Root { get; }
        public VisualElement FocusRing { get; }
        public VisualElement SelectionIndicator { get; }
        public Label Label { get; }
        public Label LoadingIndicator { get; }
        public bool IsEnabled => enabled;
        public bool Allowed
        {
            get => enabled;
            set => SetEnabled(value);
        }
        public bool IsLoading => loading;
        public bool IsSelected { get; private set; }

        public void SetLabel(string value)
        {
            string next = value ?? string.Empty;
            if (!string.Equals(Label.text, next, StringComparison.Ordinal))
                Label.text = next;
        }

        public void SetEnabled(bool value)
        {
            if (enabled == value)
                return;
            enabled = value;
            ApplyAvailability();
        }

        public void SetLoading(bool value)
        {
            if (loading == value)
                return;
            loading = value;
            ApplyAvailability();
        }

        public void SetSelected(bool value)
        {
            IsSelected = value;
            SelectionIndicator?.EnableInClassList("is-selected", value);
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            ResetPointer(pressedPointerId);
            Root.UnregisterCallback<PointerDownEvent>(OnPointerDown);
            Root.UnregisterCallback<PointerUpEvent>(OnPointerUp);
            Root.UnregisterCallback<PointerCancelEvent>(OnPointerCancel);
            Root.UnregisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            Root.UnregisterCallback<KeyDownEvent>(OnKeyDown);
            Root.UnregisterCallback<KeyUpEvent>(OnKeyUp);
            Root.UnregisterCallback<BlurEvent>(OnBlur);
        }

        private bool CanActivate => !disposed && enabled && !loading;

        private void ApplyAvailability()
        {
            bool available = !disposed && enabled && !loading;
            Label.EnableInClassList("is-disabled", !available);
            Label.EnableInClassList("is-loading", loading);
            LoadingIndicator?.EnableInClassList("is-visible", loading);
            if (!available)
            {
                keyboardPressed = false;
                ResetPointer(pressedPointerId);
                SetPressed(false);
            }
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if ((!CanActivate && !showPressWhenUnavailable) || evt.button != 0 || pressedPointerId >= 0)
                return;
            pressedPointerId = evt.pointerId;
            Root.CapturePointer(evt.pointerId);
            Root.Focus();
            SetPressed(true);
            evt.StopPropagation();
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (evt.pointerId != pressedPointerId)
                return;
            bool activate = Root.worldBound.Contains(evt.position);
            ResetPointer(evt.pointerId);
            if (activate && CanActivate)
                Activate();
            evt.StopPropagation();
        }

        private void OnPointerCancel(PointerCancelEvent evt) => ResetPointer(evt.pointerId);

        private void OnPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            if (evt.pointerId != pressedPointerId)
                return;
            pressedPointerId = -1;
            SetPressed(false);
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if ((!CanActivate && !showPressWhenUnavailable) ||
                keyboardPressed || !IsActivationKey(evt.keyCode))
                return;
            keyboardPressed = true;
            SetPressed(true);
            evt.StopPropagation();
        }

        private void OnKeyUp(KeyUpEvent evt)
        {
            if (!keyboardPressed || !IsActivationKey(evt.keyCode))
                return;
            keyboardPressed = false;
            SetPressed(false);
            if (CanActivate)
                Activate();
            evt.StopPropagation();
        }

        private void OnBlur(BlurEvent _)
        {
            keyboardPressed = false;
            SetPressed(false);
        }

        private void ResetPointer(int pointerId)
        {
            if (pointerId < 0 || pointerId != pressedPointerId)
                return;
            pressedPointerId = -1;
            if (Root.HasPointerCapture(pointerId))
                Root.ReleasePointer(pointerId);
            SetPressed(false);
        }

        private void SetPressed(bool value) => Label.EnableInClassList("is-pressed", value);

        private void Activate()
        {
            if (playFeedback)
                UIFeedbackService.Play(FeedbackCue.ButtonClick);
            clicked();
        }

        private static bool IsActivationKey(KeyCode key) =>
            key == KeyCode.Return || key == KeyCode.KeypadEnter || key == KeyCode.Space;

        private static string ToneClass(MobileActionTone tone) => tone switch
        {
            MobileActionTone.Primary => "mobile-action--primary",
            MobileActionTone.Quiet => "mobile-action--quiet",
            MobileActionTone.Danger => "mobile-action--danger",
            MobileActionTone.Navigation => "mobile-action--navigation",
            _ => "mobile-action--standard"
        };
    }

    public sealed class MobilePageShell : IDisposable
    {
        private readonly UiToolkitSafeAreaBinding safeAreaBinding;

        public MobilePageShell(string name)
        {
            Root = MobileGameDesignSystem.CloneTemplateRoot(
                "UI/Mobile/MobilePageShell",
                "mobile-page");
            Root.name = string.IsNullOrWhiteSpace(name) ? "mobile-page" : name;
            SafeArea = Root.Q<VisualElement>("safe-area");
            HeaderSlot = Root.Q<VisualElement>("header-slot");
            ContentSlot = Root.Q<VisualElement>("content-slot");
            BottomNavigationSlot = Root.Q<VisualElement>("bottom-navigation-slot");
            OverlayLayer = Root.Q<VisualElement>("overlay-layer");
            ModalLayer = Root.Q<VisualElement>("modal-layer");
            safeAreaBinding = UiToolkitSafeArea.Attach(SafeArea, MobileUiTokens.SpaceMd);
            safeAreaBinding.PaddingChanged += ApplyOverlayInsets;
            ApplyOverlayInsets(safeAreaBinding.AppliedPadding);
        }

        public VisualElement Root { get; }
        public VisualElement SafeArea { get; }
        public VisualElement HeaderSlot { get; }
        public VisualElement ContentSlot { get; }
        public VisualElement BottomNavigationSlot { get; }
        public VisualElement OverlayLayer { get; }
        public VisualElement ModalLayer { get; }

        public void Dispose()
        {
            safeAreaBinding.PaddingChanged -= ApplyOverlayInsets;
            safeAreaBinding.Dispose();
        }

        private void ApplyOverlayInsets(SafeAreaInsets padding)
        {
            OverlayLayer.style.left = padding.Left;
            OverlayLayer.style.top = padding.Top;
            OverlayLayer.style.right = padding.Right;
            OverlayLayer.style.bottom = padding.Bottom;
        }
    }

    public sealed class MobileTopBar
    {
        public MobileTopBar(string title, string subtitle = null)
        {
            Root = MobileGameDesignSystem.CloneTemplateRoot(
                "UI/Mobile/MobileTopBar",
                "mobile-top-bar");
            Leading = Root.Q<VisualElement>("top-bar-leading");
            Copy = Root.Q<VisualElement>("top-bar-copy");
            Title = Root.Q<Label>("top-bar-title");
            Subtitle = Root.Q<Label>("top-bar-subtitle");
            Actions = Root.Q<VisualElement>("top-bar-actions");
            SetText(title, subtitle);
        }

        public VisualElement Root { get; }
        public VisualElement Leading { get; }
        public VisualElement Copy { get; }
        public Label Title { get; }
        public Label Subtitle { get; }
        public VisualElement Actions { get; }

        public void SetText(string title, string subtitle)
        {
            Title.text = title ?? string.Empty;
            Subtitle.text = subtitle ?? string.Empty;
            Subtitle.EnableInClassList("is-empty", string.IsNullOrWhiteSpace(subtitle));
        }

        public void AddAction(MobileActionControl action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            action.Root.AddToClassList("mobile-top-bar__action");
            Actions.Add(action.Root);
        }
    }

    public sealed class MobileBottomNavigation
    {
        private readonly MobileActionControl[] items;

        public MobileBottomNavigation(params MobileActionControl[] items)
        {
            this.items = items ?? Array.Empty<MobileActionControl>();
            if (this.items.Length > 5)
                throw new ArgumentOutOfRangeException(nameof(items), "Mobile navigation supports at most five destinations.");
            Root = MobileGameDesignSystem.CloneTemplateRoot(
                "UI/Mobile/MobileBottomNavigation",
                "mobile-bottom-navigation");
            foreach (MobileActionControl item in this.items)
            {
                if (item == null)
                    throw new ArgumentException("Navigation items cannot contain null.", nameof(items));
                item.Root.AddToClassList("mobile-bottom-navigation__item");
                Root.Add(item.Root);
            }
        }

        public VisualElement Root { get; }
        public int Count => items.Length;

        public void Select(int selectedIndex)
        {
            for (int index = 0; index < items.Length; index++)
                items[index].SetSelected(index == selectedIndex);
        }
    }

    public sealed class MobileCard
    {
        public MobileCard(string name = "mobile-card")
        {
            Root = MobileGameDesignSystem.CloneTemplateRoot(
                "UI/Mobile/MobileCard",
                "mobile-card");
            Root.name = name;
            Header = Root.Q<VisualElement>("card-header");
            Body = Root.Q<VisualElement>("card-body");
            Footer = Root.Q<VisualElement>("card-footer");
        }

        public VisualElement Root { get; }
        public VisualElement Header { get; }
        public VisualElement Body { get; }
        public VisualElement Footer { get; }
    }

    public sealed class MobileEmptyState
    {
        public MobileEmptyState(string title, string body)
        {
            Root = MobileGameDesignSystem.CloneTemplateRoot(
                "UI/Mobile/MobileStateView",
                "mobile-state-view");
            Root.name = "mobile-empty-state";
            Root.AddToClassList("mobile-empty-state");
            Icon = Root.Q<Label>("state-icon");
            Icon.name = "empty-state-icon";
            Title = Root.Q<Label>("state-title");
            Title.name = "empty-state-title";
            Body = Root.Q<Label>("state-body");
            Body.name = "empty-state-body";
            Actions = Root.Q<VisualElement>("state-actions");
            Actions.name = "empty-state-actions";
            SetText(title, body);
        }

        public VisualElement Root { get; }
        public Label Icon { get; }
        public Label Title { get; }
        public Label Body { get; }
        public VisualElement Actions { get; }

        public void SetText(string title, string body)
        {
            Title.text = title ?? string.Empty;
            Body.text = body ?? string.Empty;
        }

        public void AddAction(MobileActionControl action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            Actions.Add(action.Root);
        }
    }

    public sealed class MobileErrorState : IDisposable
    {
        public MobileErrorState(Action retry, Action manageContent, Action home, Action close)
        {
            Root = MobileGameDesignSystem.CloneTemplateRoot(
                "UI/Mobile/MobileStateView",
                "mobile-state-view");
            Root.name = "mobile-error-state";
            Root.AddToClassList("mobile-error-state");
            Icon = Root.Q<Label>("state-icon");
            Icon.text = "!";
            Title = Root.Q<Label>("state-title");
            Title.name = "error-state-title";
            Body = Root.Q<Label>("state-body");
            Body.name = "error-state-body";
            Actions = Root.Q<VisualElement>("state-actions");
            Actions.name = "error-state-actions";
            Retry = Action("error-retry", retry, MobileActionTone.Primary);
            ManageContent = Action("error-manage-content", manageContent, MobileActionTone.Standard);
            Home = Action("error-home", home, MobileActionTone.Quiet);
            Close = Action("error-close", close, MobileActionTone.Quiet);
            RetrySlot = AddActionSlot("error-retry-slot", Retry);
            ManageContentSlot = AddActionSlot("error-manage-content-slot", ManageContent);
            HomeSlot = AddActionSlot("error-home-slot", Home);
            CloseSlot = AddActionSlot("error-close-slot", Close);
            SetCapability(RetrySlot, retry != null);
            SetCapability(ManageContentSlot, manageContent != null);
            SetCapability(HomeSlot, home != null);
            SetCapability(CloseSlot, close != null);
            Presenter = new PlayerUiErrorPresenter(
                Root,
                Title,
                Body,
                retry != null ? RetrySlot : null,
                manageContent != null ? ManageContentSlot : null,
                home != null ? HomeSlot : null,
                close != null ? CloseSlot : null);
            Presenter.HideImmediately();
        }

        public VisualElement Root { get; }
        public Label Icon { get; }
        public Label Title { get; }
        public Label Body { get; }
        public VisualElement Actions { get; }
        public MobileActionControl Retry { get; }
        public MobileActionControl ManageContent { get; }
        public MobileActionControl Home { get; }
        public MobileActionControl Close { get; }
        public VisualElement RetrySlot { get; }
        public VisualElement ManageContentSlot { get; }
        public VisualElement HomeSlot { get; }
        public VisualElement CloseSlot { get; }
        public PlayerUiErrorPresenter Presenter { get; }

        public void Dispose()
        {
            Presenter.Dispose();
            Retry.Dispose();
            ManageContent.Dispose();
            Home.Dispose();
            Close.Dispose();
        }

        private static MobileActionControl Action(
            string name,
            Action callback,
            MobileActionTone tone) =>
            new MobileActionControl(name, string.Empty, callback ?? (() => { }), tone);

        private VisualElement AddActionSlot(string name, MobileActionControl action)
        {
            var slot = new VisualElement { name = name };
            slot.AddToClassList("mobile-state-card__action-slot");
            slot.Add(action.Root);
            Actions.Add(slot);
            return slot;
        }

        private static void SetCapability(VisualElement slot, bool supported)
        {
            slot.style.display = supported ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    public sealed class MobileProgressView
    {
        public MobileProgressView(string label = null)
        {
            Root = MobileGameDesignSystem.CloneTemplateRoot(
                "UI/Mobile/MobileProgress",
                "mobile-progress");
            Header = Root.Q<VisualElement>("progress-header");
            Label = Root.Q<Label>("progress-label");
            Value = Root.Q<Label>("progress-value");
            Bar = Root.Q<ProgressBar>("progress-bar");
            Label.text = label ?? string.Empty;
        }

        public VisualElement Root { get; }
        public VisualElement Header { get; }
        public Label Label { get; }
        public Label Value { get; }
        public ProgressBar Bar { get; }

        public void Set(float percentage, string label = null)
        {
            float clamped = MobileUiTokens.ClampProgress(percentage);
            Bar.value = clamped;
            Value.text = Mathf.RoundToInt(clamped) + "%";
            if (label != null)
                Label.text = label;
        }
    }

    public sealed class MobileToastPresenter : IDisposable
    {
        private IVisualElementScheduledItem scheduledHide;
        private int scheduleGeneration;
        private bool disposed;

        public MobileToastPresenter()
        {
            Root = MobileGameDesignSystem.CloneTemplateRoot(
                "UI/Mobile/MobileToast",
                "mobile-toast");
            Label = Root.Q<Label>("toast-label");
            HideImmediately();
        }

        public VisualElement Root { get; }
        public Label Label { get; }
        public bool IsVisible { get; private set; }

        public void Show(
            string message,
            MobileStatusTone tone = MobileStatusTone.Neutral,
            int durationMilliseconds = MobileUiTokens.DefaultToastMilliseconds)
        {
            if (disposed)
                return;
            scheduledHide?.Pause();
            scheduledHide = null;
            int generation = ++scheduleGeneration;
            Label.text = message ?? string.Empty;
            SetTone(tone);
            IsVisible = true;
            Root.style.display = DisplayStyle.Flex;
            Root.style.opacity = 1f;
            Root.style.translate = new Translate(0f, 0f, 0f);
            if (durationMilliseconds <= 0)
                return;
            scheduledHide = Root.schedule.Execute(() =>
            {
                if (!disposed && generation == scheduleGeneration)
                    Hide();
            });
            scheduledHide.ExecuteLater(durationMilliseconds);
        }

        public void Hide()
        {
            if (disposed)
                return;
            scheduledHide?.Pause();
            scheduledHide = null;
            int generation = ++scheduleGeneration;
            if (!IsVisible || UIFeedbackService.ReduceMotion)
            {
                HideImmediately();
                return;
            }
            Root.style.opacity = 0f;
            Root.style.translate = new Translate(0f, 6f, 0f);
            scheduledHide = Root.schedule.Execute(() =>
            {
                if (!disposed && generation == scheduleGeneration)
                    HideImmediately();
            });
            scheduledHide.ExecuteLater(
                Mathf.RoundToInt(MobileUiTokens.FastMotionMilliseconds / UIFeedbackService.AnimationSpeed));
        }

        public void HideImmediately()
        {
            scheduledHide?.Pause();
            scheduledHide = null;
            scheduleGeneration++;
            IsVisible = false;
            Root.style.opacity = 1f;
            Root.style.translate = new Translate(0f, 0f, 0f);
            Root.style.display = DisplayStyle.None;
        }

        public void Dispose()
        {
            HideImmediately();
            disposed = true;
        }

        private void SetTone(MobileStatusTone tone)
        {
            Root.EnableInClassList("mobile-toast--success", tone == MobileStatusTone.Success);
            Root.EnableInClassList("mobile-toast--warning", tone == MobileStatusTone.Warning);
            Root.EnableInClassList("mobile-toast--error", tone == MobileStatusTone.Error);
        }
    }

    public sealed class MobileSheetPresenter : IDisposable
    {
        private readonly UiToolkitSafeAreaBinding safeAreaBinding;
        private IVisualElementScheduledItem animation;
        private VisualElement previousFocus;
        private int animationGeneration;
        private bool disposed;

        public MobileSheetPresenter(string name = "mobile-sheet")
        {
            Root = MobileGameDesignSystem.CloneTemplateRoot(
                "UI/Mobile/MobileSheet",
                "mobile-sheet");
            Root.name = name;
            Root.pickingMode = PickingMode.Position;
            Scrim = Root.Q<VisualElement>("sheet-scrim");
            SafeArea = Root.Q<VisualElement>("sheet-safe-area");
            Panel = Root.Q<VisualElement>("sheet-panel");
            Grabber = Root.Q<VisualElement>("sheet-grabber");
            Title = Root.Q<Label>("sheet-title");
            Body = Root.Q<Label>("sheet-body");
            Content = Root.Q<VisualElement>("sheet-content");
            Actions = Root.Q<VisualElement>("sheet-actions");
            safeAreaBinding = UiToolkitSafeArea.Attach(SafeArea, MobileUiTokens.SpaceMd);
            Scrim.RegisterCallback<PointerUpEvent>(OnScrimPointerUp);
            Root.RegisterCallback<FocusOutEvent>(OnFocusOut);
            HideImmediately();
        }

        public VisualElement Root { get; }
        public VisualElement Scrim { get; }
        public VisualElement SafeArea { get; }
        public VisualElement Panel { get; }
        public VisualElement Grabber { get; }
        public Label Title { get; }
        public Label Body { get; }
        public VisualElement Content { get; }
        public VisualElement Actions { get; }
        public bool IsVisible { get; private set; }
        public bool DismissOnScrim { get; set; } = true;
        public event Action DismissRequested;

        public void Show(string title, string body = null)
        {
            if (disposed)
                return;
            int generation = BeginAnimation();
            Title.text = title ?? string.Empty;
            Body.text = body ?? string.Empty;
            Body.EnableInClassList("is-empty", string.IsNullOrWhiteSpace(body));
            if (!IsVisible)
                previousFocus = Root.panel?.focusController?.focusedElement as VisualElement;
            IsVisible = true;
            Root.style.display = DisplayStyle.Flex;
            Root.style.opacity = 1f;
            Panel.focusable = true;
            Panel.Focus();
            if (UIFeedbackService.ReduceMotion)
            {
                Panel.style.translate = new Translate(0f, 0f, 0f);
                return;
            }
            Panel.style.translate = new Translate(0f, 18f, 0f);
            animation = Root.schedule.Execute(() =>
            {
                if (disposed || generation != animationGeneration)
                    return;
                Panel.style.translate = new Translate(0f, 0f, 0f);
                animation = null;
            });
            animation.ExecuteLater(
                Mathf.RoundToInt(MobileUiTokens.StandardMotionMilliseconds / UIFeedbackService.AnimationSpeed));
        }

        public void Hide()
        {
            if (disposed)
                return;
            int generation = BeginAnimation();
            if (!IsVisible || UIFeedbackService.ReduceMotion)
            {
                SetHiddenVisuals();
                RestoreFocus();
                return;
            }
            IsVisible = false;
            RestoreFocus();
            Root.style.opacity = 0f;
            Panel.style.translate = new Translate(0f, 18f, 0f);
            animation = Root.schedule.Execute(() =>
            {
                if (disposed || generation != animationGeneration)
                    return;
                SetHiddenVisuals();
                animation = null;
            });
            animation.ExecuteLater(
                Mathf.RoundToInt(MobileUiTokens.FastMotionMilliseconds / UIFeedbackService.AnimationSpeed));
        }

        public void HideImmediately()
        {
            BeginAnimation();
            SetHiddenVisuals();
            RestoreFocus();
        }

        private void SetHiddenVisuals()
        {
            IsVisible = false;
            Root.style.opacity = 1f;
            Panel.style.translate = new Translate(0f, 0f, 0f);
            Root.style.display = DisplayStyle.None;
        }

        public void Dispose()
        {
            if (disposed)
                return;
            HideImmediately();
            disposed = true;
            Scrim.UnregisterCallback<PointerUpEvent>(OnScrimPointerUp);
            Root.UnregisterCallback<FocusOutEvent>(OnFocusOut);
            safeAreaBinding.Dispose();
        }

        private int BeginAnimation()
        {
            animation?.Pause();
            animation = null;
            return ++animationGeneration;
        }

        private void OnScrimPointerUp(PointerUpEvent evt)
        {
            if (!DismissOnScrim || !ReferenceEquals(evt.target, Scrim))
                return;
            DismissRequested?.Invoke();
            evt.StopPropagation();
        }

        private void OnFocusOut(FocusOutEvent evt)
        {
            if (disposed || !IsVisible)
                return;
            VisualElement next = evt.relatedTarget as VisualElement;
            if (next == null || IsWithin(next, Root))
                return;

            int generation = animationGeneration;
            Root.schedule.Execute(() =>
            {
                if (disposed || !IsVisible || generation != animationGeneration)
                    return;
                VisualElement focused = Root.panel?.focusController?.focusedElement as VisualElement;
                if (focused != null && !IsWithin(focused, Root))
                    Panel.Focus();
            });
        }

        private void RestoreFocus()
        {
            VisualElement target = previousFocus;
            previousFocus = null;
            if (target?.panel != null)
                target.Focus();
        }

        private static bool IsWithin(VisualElement element, VisualElement ancestor)
        {
            for (VisualElement current = element; current != null; current = current.parent)
            {
                if (ReferenceEquals(current, ancestor))
                    return true;
            }
            return false;
        }
    }

    public sealed class MobileConfirmationPresenter : IDisposable
    {
        private Action confirmed;
        private Action cancelled;

        public MobileConfirmationPresenter()
        {
            Sheet = new MobileSheetPresenter("mobile-confirmation");
            Sheet.Root.AddToClassList("mobile-confirmation");
            Confirm = new MobileActionControl(
                "confirmation-confirm",
                string.Empty,
                ConfirmClicked,
                MobileActionTone.Primary);
            DangerConfirm = new MobileActionControl(
                "confirmation-danger-confirm",
                string.Empty,
                ConfirmClicked,
                MobileActionTone.Danger);
            Cancel = new MobileActionControl(
                "confirmation-cancel",
                string.Empty,
                CancelClicked,
                MobileActionTone.Quiet);
            CancelSlot = AddActionSlot("confirmation-cancel-slot", Cancel);
            ConfirmSlot = AddActionSlot("confirmation-confirm-slot", Confirm);
            DangerConfirmSlot = AddActionSlot("confirmation-danger-confirm-slot", DangerConfirm);
            Sheet.DismissRequested += CancelClicked;
        }

        public MobileSheetPresenter Sheet { get; }
        public MobileActionControl Confirm { get; }
        public MobileActionControl DangerConfirm { get; }
        public MobileActionControl Cancel { get; }
        public VisualElement ConfirmSlot { get; }
        public VisualElement DangerConfirmSlot { get; }
        public VisualElement CancelSlot { get; }
        public VisualElement Root => Sheet.Root;
        public bool IsVisible => Sheet.IsVisible;

        public void Show(
            string title,
            string body,
            string confirmLabel,
            string cancelLabel,
            Action onConfirmed,
            Action onCancelled = null,
            bool destructive = true)
        {
            confirmed = onConfirmed;
            cancelled = onCancelled;
            Confirm.SetLabel(confirmLabel);
            DangerConfirm.SetLabel(confirmLabel);
            Cancel.SetLabel(cancelLabel);
            ConfirmSlot.style.display = destructive ? DisplayStyle.None : DisplayStyle.Flex;
            DangerConfirmSlot.style.display = destructive ? DisplayStyle.Flex : DisplayStyle.None;
            Sheet.Show(title, body);
        }

        public void Hide() => Sheet.Hide();

        public void Dispose()
        {
            Sheet.DismissRequested -= CancelClicked;
            Confirm.Dispose();
            DangerConfirm.Dispose();
            Cancel.Dispose();
            Sheet.Dispose();
            confirmed = null;
            cancelled = null;
        }

        private void ConfirmClicked()
        {
            Action callback = confirmed;
            confirmed = null;
            cancelled = null;
            Sheet.Hide();
            callback?.Invoke();
        }

        private void CancelClicked()
        {
            Action callback = cancelled;
            confirmed = null;
            cancelled = null;
            Sheet.Hide();
            callback?.Invoke();
        }

        private VisualElement AddActionSlot(string name, MobileActionControl action)
        {
            var slot = new VisualElement { name = name };
            slot.AddToClassList("mobile-sheet__action-slot");
            slot.Add(action.Root);
            Sheet.Actions.Add(slot);
            return slot;
        }
    }
}
