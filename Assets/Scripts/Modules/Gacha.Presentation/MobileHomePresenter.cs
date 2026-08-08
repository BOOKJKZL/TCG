using System;
using Gacha.Application;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UIElements;

namespace Gacha.Presentation
{
    public sealed class MobileHomePresenter : IDisposable
    {
        private sealed class Feature : IDisposable
        {
            public Feature(
                string name,
                string number,
                Action clicked,
                MobileActionTone tone)
            {
                Card = new MobileCard(name + "-card");
                Card.Root.AddToClassList("home-feature-card");
                var header = new VisualElement { pickingMode = PickingMode.Ignore };
                header.AddToClassList("home-feature-card__header");
                Number = new Label(number) { pickingMode = PickingMode.Ignore };
                Number.AddToClassList("home-feature-card__number");
                Title = new Label { pickingMode = PickingMode.Ignore };
                Title.AddToClassList("home-feature-card__title");
                Body = new Label { pickingMode = PickingMode.Ignore };
                Body.AddToClassList("home-feature-card__body");
                Action = new MobileActionControl(name + "-action", string.Empty, clicked, tone);
                Action.Root.AddToClassList("home-feature-card__action");
                header.Add(Number);
                header.Add(Title);
                Card.Header.Add(header);
                Card.Body.Add(Body);
                Card.Footer.Add(Action.Root);
            }

            public MobileCard Card { get; }
            public Label Number { get; }
            public Label Title { get; }
            public Label Body { get; }
            public MobileActionControl Action { get; }

            public void SetText(string title, string body)
            {
                Title.text = title ?? string.Empty;
                Body.text = body ?? string.Empty;
                Action.SetLabel(title);
            }

            public void Dispose() => Action.Dispose();
        }

        private readonly LanguageSelectionService languages;
        private readonly Feature gachaFeature;
        private readonly Feature collectionFeature;
        private readonly Feature contentFeature;
        private readonly Feature settingsFeature;
        private readonly MobileActionControl settingsAction;
        private bool disposed;

        public MobileHomePresenter(
            GameObject host,
            Action openGacha,
            Action openCollection,
            Action openContent,
            Action openSettings)
        {
            if (host == null)
                throw new ArgumentNullException(nameof(host));
            openGacha = openGacha ?? throw new ArgumentNullException(nameof(openGacha));
            openCollection = openCollection ?? throw new ArgumentNullException(nameof(openCollection));
            openContent = openContent ?? throw new ArgumentNullException(nameof(openContent));
            openSettings = openSettings ?? throw new ArgumentNullException(nameof(openSettings));

            PanelSettings panelSettings = Resources.Load<PanelSettings>("UI/Collection Panel Settings");
            if (panelSettings == null)
                throw new InvalidOperationException("The mobile home Panel Settings asset is missing.");

            Document = host.GetComponent<UIDocument>() ?? host.AddComponent<UIDocument>();
            Document.panelSettings = panelSettings;
            Document.sortingOrder = 10;
            Document.rootVisualElement.Clear();
            Document.rootVisualElement.name = "mobile-home-document";

            Shell = new MobilePageShell("mobile-home-page");
            Document.rootVisualElement.Add(Shell.Root);
            TopBar = new MobileTopBar(string.Empty);
            settingsAction = new MobileActionControl(
                "home-settings-utility-action",
                string.Empty,
                openSettings,
                MobileActionTone.Quiet);
            TopBar.AddAction(settingsAction);
            Shell.HeaderSlot.Add(TopBar.Root);

            Content = MobileGameDesignSystem.CloneTemplateRoot("UI/HomeView", "home-scroll");
            Hero = Content.Q<VisualElement>("home-hero");
            Kicker = Content.Q<Label>("home-kicker");
            Title = Content.Q<Label>("home-title");
            Body = Content.Q<Label>("home-body");
            SectionTitle = Content.Q<Label>("home-section-title");
            Shell.ContentSlot.Add(Content);

            gachaFeature = new Feature("home-gacha", "01", openGacha, MobileActionTone.Primary);
            collectionFeature = new Feature("home-collection", "02", openCollection, MobileActionTone.Standard);
            contentFeature = new Feature("home-content", "03", openContent, MobileActionTone.Standard);
            settingsFeature = new Feature("home-settings", "04", openSettings, MobileActionTone.Quiet);
            RequiredSlot("home-gacha-slot").Add(gachaFeature.Card.Root);
            RequiredSlot("home-collection-slot").Add(collectionFeature.Card.Root);
            RequiredSlot("home-content-slot").Add(contentFeature.Card.Root);
            RequiredSlot("home-settings-slot").Add(settingsFeature.Card.Root);

            PrimaryNavigation = new MobilePrimaryNavigation(
                MobileDestination.Home,
                destination => Navigate(
                    destination,
                    openGacha,
                    openCollection,
                    openContent,
                    openSettings));
            Shell.BottomNavigationSlot.Add(PrimaryNavigation.BottomNavigation.Root);

            if (ApplicationServices.IsConfigured)
            {
                languages = ApplicationServices.Languages;
                languages.UiLanguageChanged += OnUiLanguageChanged;
            }
            LocalizationSettings.SelectedLocaleChanged += OnSelectedLocaleChanged;
            RefreshText();
        }

        public UIDocument Document { get; }
        public MobilePageShell Shell { get; }
        public MobileTopBar TopBar { get; }
        public MobilePrimaryNavigation PrimaryNavigation { get; }
        public VisualElement Content { get; }
        public VisualElement Hero { get; }
        public Label Kicker { get; }
        public Label Title { get; }
        public Label Body { get; }
        public Label SectionTitle { get; }
        public MobileActionControl HomeNavigation => PrimaryNavigation.GetAction(MobileDestination.Home);
        public MobileActionControl GachaNavigation => PrimaryNavigation.GetAction(MobileDestination.Gacha);
        public MobileActionControl CollectionNavigation => PrimaryNavigation.GetAction(MobileDestination.Collection);
        public MobileActionControl ContentNavigation => PrimaryNavigation.GetAction(MobileDestination.Content);
        public MobileActionControl SettingsNavigation => PrimaryNavigation.GetAction(MobileDestination.Settings);
        public MobileActionControl GachaFeatureAction => gachaFeature.Action;
        public MobileActionControl CollectionFeatureAction => collectionFeature.Action;
        public MobileActionControl ContentFeatureAction => contentFeature.Action;
        public MobileActionControl SettingsFeatureAction => settingsFeature.Action;

        public void SetNavigationPending(MobileDestination destination)
        {
            if (disposed || destination == MobileDestination.Home)
                return;
            PrimaryNavigation.SetPending(destination);
            SetFeaturePending(gachaFeature.Action, destination == MobileDestination.Gacha);
            SetFeaturePending(collectionFeature.Action, destination == MobileDestination.Collection);
            SetFeaturePending(contentFeature.Action, destination == MobileDestination.Content);
            SetFeaturePending(settingsFeature.Action, destination == MobileDestination.Settings);
            settingsAction.SetLoading(destination == MobileDestination.Settings);
            settingsAction.SetEnabled(false);
        }

        public void ClearNavigationPending()
        {
            if (disposed)
                return;
            PrimaryNavigation.ClearPending(MobileDestination.Home);
            SetFeatureReady(gachaFeature.Action);
            SetFeatureReady(collectionFeature.Action);
            SetFeatureReady(contentFeature.Action);
            SetFeatureReady(settingsFeature.Action);
            settingsAction.SetLoading(false);
            settingsAction.SetEnabled(true);
        }

        public void RefreshText()
        {
            if (disposed)
                return;
            TopBar.SetText(CardUiText.Get("home.top.title"), CardUiText.Get("home.top.subtitle"));
            settingsAction.SetLabel(CardUiText.Get("main_menu.action.settings"));
            Kicker.text = CardUiText.Get("home.kicker");
            Title.text = CardUiText.Get("home.title");
            Body.text = CardUiText.Get("home.body");
            SectionTitle.text = CardUiText.Get("home.section.destinations");
            gachaFeature.SetText(
                CardUiText.Get("main_menu.action.gacha"),
                CardUiText.Get("home.feature.gacha"));
            collectionFeature.SetText(
                CardUiText.Get("main_menu.action.collection"),
                CardUiText.Get("home.feature.collection"));
            contentFeature.SetText(
                CardUiText.Get("main_menu.action.content"),
                CardUiText.Get("home.feature.content"));
            settingsFeature.SetText(
                CardUiText.Get("main_menu.action.settings"),
                CardUiText.Get("home.feature.settings"));
            PrimaryNavigation.RefreshText();
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            LocalizationSettings.SelectedLocaleChanged -= OnSelectedLocaleChanged;
            if (languages != null)
                languages.UiLanguageChanged -= OnUiLanguageChanged;
            gachaFeature.Dispose();
            collectionFeature.Dispose();
            contentFeature.Dispose();
            settingsFeature.Dispose();
            settingsAction.Dispose();
            PrimaryNavigation.Dispose();
            Shell.Dispose();
            if (Document != null && Document.rootVisualElement != null)
                Document.rootVisualElement.Clear();
        }

        private VisualElement RequiredSlot(string name)
        {
            VisualElement slot = Content.Q<VisualElement>(name);
            return slot ?? throw new InvalidOperationException("The mobile home view is missing '" + name + "'.");
        }

        private void ScrollHome()
        {
            if (Content is ScrollView scrollView)
                scrollView.scrollOffset = Vector2.zero;
        }

        private void Navigate(
            MobileDestination destination,
            Action openGacha,
            Action openCollection,
            Action openContent,
            Action openSettings)
        {
            switch (destination)
            {
                case MobileDestination.Gacha:
                    openGacha();
                    break;
                case MobileDestination.Collection:
                    openCollection();
                    break;
                case MobileDestination.Content:
                    openContent();
                    break;
                case MobileDestination.Settings:
                    openSettings();
                    break;
                default:
                    ScrollHome();
                    break;
            }
        }

        private void OnUiLanguageChanged(string _) => RefreshText();
        private void OnSelectedLocaleChanged(Locale _) => RefreshText();

        private static void SetFeaturePending(MobileActionControl action, bool loading)
        {
            action.SetLoading(loading);
            action.SetEnabled(false);
        }

        private static void SetFeatureReady(MobileActionControl action)
        {
            action.SetLoading(false);
            action.SetEnabled(true);
        }
    }
}
