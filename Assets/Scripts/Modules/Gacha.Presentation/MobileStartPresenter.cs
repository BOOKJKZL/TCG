using System;
using Gacha.Application;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UIElements;

namespace Gacha.Presentation
{
    public sealed class MobileStartPresenter : IDisposable
    {
        private readonly LanguageSelectionService languages;
        private readonly MobileTypographyBinding typography;
        private readonly MobileAccessibilityPageBindings accessibility;
        private bool pending;
        private bool failed;
        private bool disposed;

        public MobileStartPresenter(GameObject host, Action start)
        {
            if (host == null)
                throw new ArgumentNullException(nameof(host));
            if (start == null)
                throw new ArgumentNullException(nameof(start));
            PanelSettings panel = Resources.Load<PanelSettings>("UI/Collection Panel Settings");
            if (panel == null)
                throw new InvalidOperationException("The mobile start Panel Settings asset is missing.");

            Document = host.GetComponent<UIDocument>() ?? host.AddComponent<UIDocument>();
            Document.panelSettings = panel;
            Document.sortingOrder = 10;
            Document.rootVisualElement.Clear();
            Document.rootVisualElement.name = "mobile-start-document";
            Shell = new MobilePageShell("mobile-start-page");
            Document.rootVisualElement.Add(Shell.Root);
            Content = (ScrollView)MobileGameDesignSystem.CloneTemplateRoot("UI/StartView", "start-scroll");
            // Bind our layout role to the public container, independently of the
            // extra scroll wrappers introduced by the Unity runtime version.
            Content.contentContainer.AddToClassList("start-scroll__content");
            Title = Content.Q<Label>("start-title");
            Body = Content.Q<Label>("start-body");
            Status = Content.Q<Label>("start-status");
            StartAction = new MobileActionControl("start-action", string.Empty, start, MobileActionTone.Primary);
            Content.Q<VisualElement>("start-action-slot").Add(StartAction.Root);
            Shell.ContentSlot.Add(Content);
            ExitConfirmation = new MobileConfirmationPresenter();
            Shell.ModalLayer.Add(ExitConfirmation.Root);
            typography = MobileTypography.Bind(Content, MobileTypographyProfiles.Start);

            if (ApplicationServices.IsConfigured)
            {
                languages = ApplicationServices.Languages;
                languages.UiLanguageChanged += OnUiLanguageChanged;
            }
            LocalizationSettings.SelectedLocaleChanged += OnSelectedLocaleChanged;
            CardUiText.TextChanged += RefreshText;
            RefreshText();
            accessibility = MobileAccessibilityPageBindings.ForStart(this);
        }

        public UIDocument Document { get; }
        public MobilePageShell Shell { get; }
        public ScrollView Content { get; }
        public Label Title { get; }
        public Label Body { get; }
        public Label Status { get; }
        public MobileActionControl StartAction { get; }
        public MobileConfirmationPresenter ExitConfirmation { get; }
        public bool IsPending => pending;

        public void SetPending(bool value)
        {
            if (disposed)
                return;
            pending = value;
            failed = false;
            StartAction.SetLoading(value);
            StartAction.SetEnabled(!value);
            RefreshText();
        }

        public void SetFailed()
        {
            SetPending(false);
            failed = true;
            RefreshText();
        }

        public void RefreshText()
        {
            if (disposed)
                return;
            Title.text = CardUiText.Get("start.title");
            Body.text = CardUiText.Get("start.body");
            StartAction.SetLabel(CardUiText.Get("start.action.start"));
            Status.text = pending ? CardUiText.Get("start.action.loading")
                : failed ? CardUiText.Get("common.navigation.failed") : string.Empty;
            Status.EnableInClassList("is-hidden", !pending && !failed);
            if (ExitConfirmation.IsVisible)
            {
                ExitConfirmation.Sheet.Title.text = CardUiText.Get("home.exit.title");
                ExitConfirmation.Sheet.Body.text = CardUiText.Get("home.exit.body");
                ExitConfirmation.Confirm.SetLabel(CardUiText.Get("home.exit.confirm"));
                ExitConfirmation.Cancel.SetLabel(CardUiText.Get("common.action.cancel"));
            }
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            accessibility.Dispose();
            LocalizationSettings.SelectedLocaleChanged -= OnSelectedLocaleChanged;
            CardUiText.TextChanged -= RefreshText;
            if (languages != null)
                languages.UiLanguageChanged -= OnUiLanguageChanged;
            StartAction.Dispose();
            typography.Dispose();
            ExitConfirmation.Dispose();
            Shell.Dispose();
            if (Document != null && Document.rootVisualElement != null)
                Document.rootVisualElement.Clear();
        }

        private void OnUiLanguageChanged(string _) => RefreshText();
        private void OnSelectedLocaleChanged(Locale _) => RefreshText();
    }
}
