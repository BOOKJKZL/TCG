using System;
using System.Collections.Generic;
using System.Linq;
using Gacha.Application;
using Gacha.Domain;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UIElements;

namespace Gacha.Presentation
{
    public enum HomeContentState { Loading, Empty, Ready, Error }

    /// <summary>Home composition only. Catalog, inventory and scene ownership stay in the controller.</summary>
    public sealed class MobileHomePresenter : IDisposable
    {
        private readonly LanguageSelectionService languages;
        private readonly Action openGacha, openContent;
        private readonly Action<string, bool> openProduct;
        private readonly Action<string> openPrinting;
        private readonly List<AsyncCardImageView> packImages = new List<AsyncCardImageView>();
        private readonly List<AsyncCardImageView> ownedImages = new List<AsyncCardImageView>();
        private readonly List<MobileActionControl> ownedActions = new List<MobileActionControl>();
        private readonly List<IDisposable> ownedSemantics = new List<IDisposable>();
        private readonly MobileTypographyBinding typography;
        private readonly MobileTypographyBinding brandTypography, openTypography;
        private readonly List<MobileTypographyBinding> packTypography = new List<MobileTypographyBinding>();
        private readonly MobileAccessibilityPageBindings accessibility;
        private HomeShowcaseData data;
        private ICardTextureCache imageCache;
        private PlayerUiError contentError;
        private int selectedIndex;
        private bool disposed, navigationPending, navigationFailed;
        private MobileDestination pendingDestination;

        public MobileHomePresenter(GameObject host, Action openGacha, Action openCollection,
            Action openContent, Action openSettings, Action openPokedex = null, Action retryContent = null,
            Action<string, bool> openProduct = null, Action<string> openPrinting = null)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            this.openGacha = openGacha ?? throw new ArgumentNullException(nameof(openGacha));
            this.openContent = openContent ?? throw new ArgumentNullException(nameof(openContent));
            if (openCollection == null) throw new ArgumentNullException(nameof(openCollection));
            if (openSettings == null) throw new ArgumentNullException(nameof(openSettings));
            this.openProduct = openProduct;
            this.openPrinting = openPrinting;
            var panel = Resources.Load<PanelSettings>("UI/Collection Panel Settings");
            if (panel == null) throw new InvalidOperationException("The mobile home Panel Settings asset is missing.");
            Document = host.GetComponent<UIDocument>() ?? host.AddComponent<UIDocument>();
            Document.panelSettings = panel;
            Document.sortingOrder = 10;
            Document.rootVisualElement.Clear();
            Document.rootVisualElement.name = "mobile-home-document";
            Shell = new MobilePageShell("mobile-home-page");
            Shell.Root.AddToClassList("home-showcase");
            Document.rootVisualElement.Add(Shell.Root);
            TopBar = new MobileTopBar(string.Empty);
            MobileTypography.Unbind(TopBar.Root, MobileTypographyProfiles.Shared);
            TopBar.Title.AddToClassList("home-brand-title");
            brandTypography = MobileTypography.Bind(TopBar.Root, MobileTypographyProfiles.Home);
            TopBar.Leading.Add(new HomeIcon("cards", true));
            SettingsUtilityAction = MakeAction("home-settings-utility-action", openSettings);
            TopBar.AddAction(SettingsUtilityAction);
            Shell.HeaderSlot.Add(TopBar.Root);
            Content = MobileGameDesignSystem.CloneTemplateRoot("UI/HomeView", "home-scroll");
            // Use the stylesheet reference already resolved by UXML; Resources contains
            // both HomeView.uxml and HomeView.uss under the same resource name.
            for (int index = 0; index < Content.styleSheets.count; index++)
                Shell.Root.styleSheets.Add(Content.styleSheets[index]);
            Hero = Content.Q("home-hero");
            Kicker = Content.Q<Label>("home-kicker");
            Title = Content.Q<Label>("home-title");
            Body = Content.Q<Label>("home-body");
            SectionTitle = Content.Q<Label>("home-section-title");
            Shell.ContentSlot.Add(Content);
            GachaFeatureAction = MakeAction("home-gacha-action", () => OpenSelected(true), "home-open-action");
            MobileTypography.Unbind(GachaFeatureAction.Root, MobileTypographyProfiles.Shared);
            GachaFeatureAction.Label.AddToClassList("home-open-label");
            openTypography = MobileTypography.Bind(GachaFeatureAction.Root, MobileTypographyProfiles.Home);
            CollectionFeatureAction = MakeAction("home-collection-action", openCollection);
            ContentFeatureAction = MakeAction("home-content-action", openContent);
            InspectPackAction = MakeAction("home-inspect-action", () => OpenSelected(false));
            PrevPackAction = MakeAction("home-previous-action", () => MovePack(-1), "home-text-action home-step-action");
            NextPackAction = MakeAction("home-next-action", () => MovePack(1), "home-text-action home-step-action");
            RetryContentAction = MakeAction("home-retry-action", retryContent ?? openContent);
            Slot("home-gacha-slot").Add(GachaFeatureAction.Root);
            Slot("home-collection-slot").Add(CollectionFeatureAction.Root);
            Slot("home-content-slot").Add(ContentFeatureAction.Root);
            Slot("home-inspect-slot").Add(InspectPackAction.Root);
            Slot("home-previous-slot").Add(PrevPackAction.Root);
            Slot("home-next-slot").Add(NextPackAction.Root);
            Slot("home-retry-slot").Add(RetryContentAction.Root);
            PrimaryNavigation = new MobilePrimaryNavigation(MobileDestination.Home, destination =>
            {
                switch (destination)
                {
                    case MobileDestination.Gacha: openGacha(); break;
                    case MobileDestination.Collection: openCollection(); break;
                    case MobileDestination.Content: openContent(); break;
                    case MobileDestination.Settings: openSettings(); break;
                    case MobileDestination.Pokedex: (openPokedex ?? openCollection)(); break;
                    default: ((ScrollView)Content).scrollOffset = Vector2.zero; break;
                }
            }, MobileDestination.Home, MobileDestination.Collection, MobileDestination.Pokedex);
            AddNavIcon(HomeNavigation, "home");
            AddNavIcon(CollectionNavigation, "cards");
            AddNavIcon(PokedexNavigation, "book");
            HomeNavigation.Root.AddToClassList("home-nav--selected");
            Shell.BottomNavigationSlot.Add(PrimaryNavigation.BottomNavigation.Root);
            ExitConfirmation = new MobileConfirmationPresenter();
            Shell.ModalLayer.Add(ExitConfirmation.Root);
            typography = MobileTypography.Bind(Content, MobileTypographyProfiles.Home);
            if (ApplicationServices.IsConfigured)
            {
                languages = ApplicationServices.Languages;
                languages.UiLanguageChanged += OnUiLanguageChanged;
            }
            LocalizationSettings.SelectedLocaleChanged += OnSelectedLocaleChanged;
            CardUiText.TextChanged += RefreshText;
            ContentState = HomeContentState.Empty;
            RefreshText();
            ApplyAvailability();
            accessibility = MobileAccessibilityPageBindings.ForHome(this);
        }

        public UIDocument Document { get; }
        public MobilePageShell Shell { get; }
        public MobileTopBar TopBar { get; }
        public MobilePrimaryNavigation PrimaryNavigation { get; }
        public MobileConfirmationPresenter ExitConfirmation { get; }
        public VisualElement Content { get; }
        public VisualElement Hero { get; }
        public Label Kicker { get; }
        public Label Title { get; }
        public Label Body { get; }
        public Label SectionTitle { get; }
        public HomeContentState ContentState { get; private set; }
        public string SelectedProductId => Selected?.Product.Id;
        private HomePackPreview Selected => data != null && selectedIndex < data.Packs.Count ? data.Packs[selectedIndex] : null;
        public MobileActionControl HomeNavigation => PrimaryNavigation.GetAction(MobileDestination.Home);
        public MobileActionControl GachaNavigation => PrimaryNavigation.GetAction(MobileDestination.Gacha);
        public MobileActionControl CollectionNavigation => PrimaryNavigation.GetAction(MobileDestination.Collection);
        public MobileActionControl ContentNavigation => PrimaryNavigation.GetAction(MobileDestination.Content);
        public MobileActionControl SettingsNavigation => PrimaryNavigation.GetAction(MobileDestination.Settings);
        public MobileActionControl PokedexNavigation => PrimaryNavigation.GetAction(MobileDestination.Pokedex);
        public MobileActionControl GachaFeatureAction { get; }
        public MobileActionControl CollectionFeatureAction { get; }
        public MobileActionControl ContentFeatureAction { get; }
        public MobileActionControl SettingsFeatureAction => SettingsUtilityAction;
        public MobileActionControl SettingsUtilityAction { get; }
        public MobileActionControl PrevPackAction { get; }
        public MobileActionControl NextPackAction { get; }
        public MobileActionControl InspectPackAction { get; }
        public MobileActionControl RetryContentAction { get; }

        public void BindContent(HomeShowcaseData content, ICardTextureCache cache)
        {
            if (disposed) return;
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (cache == null && (content.Packs.Any(p => p.Cover != null) || content.OwnedCards.Count > 0))
                throw new ArgumentNullException(nameof(cache));
            string previous = SelectedProductId;
            ReleaseImages();
            data = content;
            imageCache = cache;
            selectedIndex = 0;
            for (int index = 0; index < data.Packs.Count; index++)
                if (data.Packs[index].Product.Id == previous) selectedIndex = index;
            contentError = null;
            ContentState = data.Packs.Count == 0 ? HomeContentState.Empty : HomeContentState.Ready;
            RenderPacks();
            RenderOwnedCards();
            RefreshText();
            ApplyAvailability();
        }

        public void ShowLoading()
        {
            if (disposed) return;
            ReleaseImages();
            data = null;
            ContentState = HomeContentState.Loading;
            contentError = null;
            RefreshText();
            ApplyAvailability();
        }

        public void ShowContentError(PlayerUiError error)
        {
            if (disposed) return;
            ReleaseImages();
            data = null;
            ContentState = HomeContentState.Error;
            contentError = error ?? PlayerUiErrorMapper.Create(PlayerUiErrorCode.ServiceUnavailable);
            RefreshText();
            ApplyAvailability();
        }

        private void MovePack(int delta)
        {
            if (navigationPending || ContentState != HomeContentState.Ready || data.Packs.Count < 2) return;
            selectedIndex = (selectedIndex + delta + data.Packs.Count) % data.Packs.Count;
            RenderPacks();
            RefreshText();
        }

        private void OpenSelected(bool prepare)
        {
            if (navigationPending || ContentState == HomeContentState.Loading) return;
            if (Selected == null) { openContent(); return; }
            if (openProduct != null) openProduct(Selected.Product.Id, prepare);
            else openGacha();
        }

        public void SetNavigationPending(MobileDestination destination)
        {
            if (disposed || destination == MobileDestination.Home) return;
            navigationPending = true;
            navigationFailed = false;
            pendingDestination = destination;
            PrimaryNavigation.SetPending(destination);
            ApplyAvailability();
            RefreshText();
        }

        public void ClearNavigationPending()
        {
            if (disposed) return;
            navigationPending = false;
            navigationFailed = false;
            PrimaryNavigation.ClearPending(MobileDestination.Home);
            ApplyAvailability();
            RefreshText();
        }

        public void ShowNavigationFailure()
        {
            ClearNavigationPending();
            navigationFailed = true;
            RefreshText();
        }

        public void RefreshText()
        {
            if (disposed) return;
            TopBar.SetText(CardUiText.Get("home.brand"), null);
            SettingsUtilityAction.SetLabel(CardUiText.Get("main_menu.action.settings"));
            ContentFeatureAction.SetLabel(CardUiText.Get("home.action.library"));
            SectionTitle.text = CardUiText.Get("home.collection.title");
            CollectionFeatureAction.SetLabel(CardUiText.Get("home.action.view_all"));
            InspectPackAction.SetLabel(CardUiText.Get("home.action.inspect"));
            PrevPackAction.SetLabel(CardUiText.Get("home.action.previous"));
            NextPackAction.SetLabel(CardUiText.Get("home.action.next"));
            RetryContentAction.SetLabel(CardUiText.Get("common.action.retry"));
            GachaFeatureAction.SetLabel(CardUiText.Get(Selected == null ? "home.action.download" : "home.action.open"));
            Title.text = Selected?.Product.GetDisplayName(data.ContentLanguageId) ?? CardUiText.Get("home.pack.choose");
            Kicker.text = data == null || string.IsNullOrEmpty(data.ContentLanguageId)
                ? CardUiText.Get("home.library.installed")
                : CardUiText.Format("home.library.language", CardUiText.Get("language." + data.ContentLanguageId));
            Content.Q<Label>("home-pack-position").text = data != null && data.Packs.Count > 0
                ? (selectedIndex + 1) + " / " + data.Packs.Count : string.Empty;
            Body.text = navigationPending ? CardUiText.Get("common.navigation.loading")
                : navigationFailed ? CardUiText.Get("common.navigation.failed")
                : ContentState == HomeContentState.Loading ? CardUiText.Get("common.status.loading")
                : ContentState == HomeContentState.Error ? PlayerUiErrorText.Body(contentError)
                : ContentState == HomeContentState.Empty ? CardUiText.Get("home.empty.packs")
                : CardUiText.Get("home.preview.note");
            Content.Q<Label>("home-owned-empty").text = CardUiText.Get(
                ContentState == HomeContentState.Loading ? "common.status.loading" :
                ContentState == HomeContentState.Error ? "home.collection.unavailable" : "home.empty.collection");
            PrimaryNavigation.RefreshText();
            HomeNavigation.SetLabel(CardUiText.Get("home.nav.showcase"));
            CollectionNavigation.SetLabel(CardUiText.Get("home.nav.collection"));
            PokedexNavigation.SetLabel(CardUiText.Get("home.nav.pokedex"));
            if (ExitConfirmation.IsVisible)
            {
                ExitConfirmation.Sheet.Title.text = CardUiText.Get("home.exit.title");
                ExitConfirmation.Sheet.Body.text = CardUiText.Get("home.exit.body");
                ExitConfirmation.Confirm.SetLabel(CardUiText.Get("home.exit.confirm"));
                ExitConfirmation.Cancel.SetLabel(CardUiText.Get("common.action.cancel"));
            }
        }

        private void ApplyAvailability()
        {
            bool ready = ContentState == HomeContentState.Ready && Selected != null;
            foreach (MobileActionControl action in AllActions())
            {
                action.SetLoading(false);
                action.SetEnabled(!navigationPending);
            }
            GachaFeatureAction.SetEnabled(!navigationPending && ContentState != HomeContentState.Loading);
            InspectPackAction.SetEnabled(!navigationPending && ready);
            PrevPackAction.SetEnabled(!navigationPending && ready && data.Packs.Count > 1);
            NextPackAction.SetEnabled(!navigationPending && ready && data.Packs.Count > 1);
            Slot("home-inspect-slot").EnableInClassList("is-hidden", !ready);
            Slot("home-pack-controls").EnableInClassList("is-hidden", !ready);
            Slot("home-retry-slot").EnableInClassList("is-hidden", ContentState != HomeContentState.Error);
            Slot("home-empty-art").EnableInClassList("is-hidden", ready);
            Slot("home-owned-empty").EnableInClassList("is-hidden", data != null && data.OwnedCards.Count > 0);
            if (navigationPending)
            {
                GachaFeatureAction.SetLoading(pendingDestination == MobileDestination.Gacha);
                CollectionFeatureAction.SetLoading(pendingDestination == MobileDestination.Collection);
                ContentFeatureAction.SetLoading(pendingDestination == MobileDestination.Content);
                SettingsUtilityAction.SetLoading(pendingDestination == MobileDestination.Settings);
            }
        }

        private IEnumerable<MobileActionControl> AllActions()
        {
            yield return GachaFeatureAction; yield return CollectionFeatureAction;
            yield return ContentFeatureAction; yield return SettingsUtilityAction;
            yield return InspectPackAction; yield return PrevPackAction; yield return NextPackAction;
            yield return RetryContentAction;
            foreach (MobileActionControl action in ownedActions) yield return action;
        }

        private void RenderPacks()
        {
            foreach (MobileTypographyBinding binding in packTypography) binding.Dispose();
            packTypography.Clear();
            foreach (AsyncCardImageView image in packImages) image.Dispose();
            packImages.Clear();
            foreach (string name in new[] { "home-pack-left", "home-pack-center", "home-pack-right" }) Slot(name).Clear();
            if (Selected == null) return;
            AddPack(Slot("home-pack-center"), Selected, selectedIndex, false);
            if (data.Packs.Count > 1)
                AddPack(Slot("home-pack-left"), data.Packs[(selectedIndex + data.Packs.Count - 1) % data.Packs.Count],
                    (selectedIndex + data.Packs.Count - 1) % data.Packs.Count, true);
            if (data.Packs.Count > 2)
                AddPack(Slot("home-pack-right"), data.Packs[(selectedIndex + 1) % data.Packs.Count],
                    (selectedIndex + 1) % data.Packs.Count, true);
        }

        private void AddPack(VisualElement slot, HomePackPreview preview, int index, bool side)
        {
            var pack = new VisualElement { name = "home-pack-" + index, tooltip = preview.Product.GetDisplayName(data.ContentLanguageId) };
            pack.AddToClassList("home-pack");
            if (side) pack.AddToClassList("home-pack--side");
            string resource = ProductOpeningThemeService.Resolve(preview.Product).PackArtworkResourcePath;
            Texture2D theme = string.IsNullOrEmpty(resource) ? null : Resources.Load<Texture2D>(resource);
            if (theme != null) pack.style.backgroundImage = new StyleBackground(theme);
            pack.Add(Decoration("home-pack-seal"));
            var label = new Label(preview.Product.GetDisplayName(data.ContentLanguageId));
            label.AddToClassList("home-pack-label");
            pack.Add(label);
            if (preview.Cover != null && imageCache != null)
            {
                var image = new AsyncCardImageView(imageCache);
                packImages.Add(image);
                pack.Add(image.Element);
                image.Bind(preview.Cover);
            }
            pack.Add(Decoration("home-pack-seal", "home-pack-seal--bottom"));
            slot.Add(pack);
            packTypography.Add(MobileTypography.Bind(pack, MobileTypographyProfiles.Home));
        }

        private void RenderOwnedCards()
        {
            VisualElement row = Slot("home-owned-cards");
            row.Clear();
            for (int index = 0; data != null && index < data.OwnedCards.Count; index++)
            {
                PrintingDefinition printing = data.OwnedCards[index];
                var card = new VisualElement { name = "home-owned-card-" + index };
                card.AddToClassList("home-owned-card");
                var image = new AsyncCardImageView(imageCache);
                ownedImages.Add(image);
                card.Add(image.Element);
                image.Bind(printing);
                MobileActionControl details = MakeAction("home-owned-details-" + index,
                    () => { if (openPrinting != null) openPrinting(printing.Id); else CollectionFeatureAction.TryActivate(); });
                details.SetLabel(printing.GetDisplayName(data.ContentLanguageId));
                ownedActions.Add(details);
                card.Add(details.Root);
                row.Add(card);
                ownedSemantics.Add(MobileAccessibility.RegisterButton(details.Root, "owned/" + index,
                    () => details.Label.text, () => details.IsAvailable, () => false,
                    () => false, details.TryActivate));
            }
        }

        private void ReleaseImages()
        {
            foreach (MobileTypographyBinding binding in packTypography) binding.Dispose();
            packTypography.Clear();
            foreach (IDisposable semantic in ownedSemantics) semantic.Dispose();
            ownedSemantics.Clear();
            foreach (AsyncCardImageView image in packImages) image.Dispose();
            foreach (AsyncCardImageView image in ownedImages) image.Dispose();
            foreach (MobileActionControl action in ownedActions) action.Dispose();
            packImages.Clear(); ownedImages.Clear(); ownedActions.Clear();
            foreach (string name in new[] { "home-pack-left", "home-pack-center", "home-pack-right", "home-owned-cards" }) Slot(name).Clear();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            accessibility?.Dispose();
            ReleaseImages();
            typography?.Dispose();
            brandTypography?.Dispose();
            openTypography?.Dispose();
            LocalizationSettings.SelectedLocaleChanged -= OnSelectedLocaleChanged;
            CardUiText.TextChanged -= RefreshText;
            if (languages != null) languages.UiLanguageChanged -= OnUiLanguageChanged;
            foreach (MobileActionControl action in AllActions()) action.Dispose();
            PrimaryNavigation.Dispose();
            ExitConfirmation.Dispose();
            Shell.Dispose();
            Shell.Root.RemoveFromHierarchy();
        }

        private VisualElement Slot(string name) => Content.Q(name) ??
            throw new InvalidOperationException("The home view is missing '" + name + "'.");
        private static MobileActionControl MakeAction(string name, Action callback, string classes = "home-text-action")
        {
            var action = new MobileActionControl(name, string.Empty, callback, MobileActionTone.Quiet);
            foreach (string className in classes.Split(' ')) action.Root.AddToClassList(className);
            return action;
        }
        private static VisualElement Decoration(params string[] classes)
        {
            var element = new VisualElement { pickingMode = PickingMode.Ignore };
            foreach (string name in classes) element.AddToClassList(name);
            return element;
        }
        private static void AddNavIcon(MobileActionControl action, string kind) =>
            action.Root.Insert(0, new HomeIcon(kind));
        private void OnUiLanguageChanged(string _) => RefreshText();
        private void OnSelectedLocaleChanged(Locale _) => RefreshText();

        private sealed class HomeIcon : VisualElement
        {
            public HomeIcon(string kind, bool brand = false)
            {
                pickingMode = PickingMode.Ignore;
                AddToClassList("home-icon");
                if (brand) AddToClassList("home-brand-icon");
                generateVisualContent += context =>
                {
                    var p = context.painter2D;
                    p.strokeColor = kind == "home" || brand ? new Color(0.03f, 0.5f, 0.53f) : new Color(0.2f, 0.27f, 0.36f);
                    p.lineWidth = 2.5f;
                    float w = contentRect.width, h = contentRect.height;
                    void Line(params Vector2[] points)
                    {
                        p.BeginPath();
                        p.MoveTo(new Vector2(points[0].x * w, points[0].y * h));
                        for (int i = 1; i < points.Length; i++) p.LineTo(new Vector2(points[i].x * w, points[i].y * h));
                        p.Stroke();
                    }
                    if (kind == "home")
                        Line(new Vector2(.08f,.45f),new Vector2(.5f,.1f),new Vector2(.92f,.45f),new Vector2(.82f,.45f),
                            new Vector2(.82f,.9f),new Vector2(.59f,.9f),new Vector2(.59f,.63f),new Vector2(.4f,.63f),
                            new Vector2(.4f,.9f),new Vector2(.18f,.9f),new Vector2(.18f,.45f));
                    else if (kind == "book")
                    {
                        Line(new Vector2(.5f,.2f),new Vector2(.3f,.1f),new Vector2(.1f,.1f),new Vector2(.1f,.84f),
                            new Vector2(.3f,.84f),new Vector2(.5f,.94f),new Vector2(.7f,.84f),new Vector2(.9f,.84f),
                            new Vector2(.9f,.1f),new Vector2(.7f,.1f),new Vector2(.5f,.2f),new Vector2(.5f,.94f));
                    }
                    else
                    {
                        Line(new Vector2(.35f,.1f),new Vector2(.92f,.22f),new Vector2(.78f,.9f),new Vector2(.21f,.78f),new Vector2(.35f,.1f));
                        Line(new Vector2(.3f,.22f),new Vector2(.08f,.3f),new Vector2(.27f,.96f),new Vector2(.57f,.88f));
                    }
                };
            }
        }
    }
}
