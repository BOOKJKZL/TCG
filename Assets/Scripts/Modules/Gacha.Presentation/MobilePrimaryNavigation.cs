using System;

namespace Gacha.Presentation
{
    public enum MobileDestination
    {
        Home,
        Gacha,
        Collection,
        Content,
        Settings,
        Pokedex
    }

    public sealed class MobilePrimaryNavigation : IDisposable
    {
        private readonly MobileActionControl[] actions;
        private readonly MobileDestination[] visibleDestinations;

        public MobilePrimaryNavigation(
            MobileDestination selected,
            Action<MobileDestination> navigate,
            params MobileDestination[] destinations)
        {
            if (navigate == null)
                throw new ArgumentNullException(nameof(navigate));
            actions = new[]
            {
                Action(MobileDestination.Home, navigate),
                Action(MobileDestination.Gacha, navigate),
                Action(MobileDestination.Collection, navigate),
                Action(MobileDestination.Content, navigate),
                Action(MobileDestination.Settings, navigate),
                Action(MobileDestination.Pokedex, navigate)
            };
            visibleDestinations = destinations == null || destinations.Length == 0
                ? new[] { MobileDestination.Home, MobileDestination.Gacha, MobileDestination.Collection, MobileDestination.Content }
                : (MobileDestination[])destinations.Clone();
            var visibleActions = new MobileActionControl[visibleDestinations.Length];
            for (int index = 0; index < visibleDestinations.Length; index++)
                visibleActions[index] = GetAction(visibleDestinations[index]);
            BottomNavigation = new MobileBottomNavigation(visibleActions);
            SelectDestination(selected);
            RefreshText();
            CardUiText.TextChanged += RefreshText;
        }

        public MobileBottomNavigation BottomNavigation { get; }
        public int Count => BottomNavigation.Count;

        public void AttachSettingsAction(MobileTopBar topBar)
        {
            if (topBar == null)
                throw new ArgumentNullException(nameof(topBar));
            topBar.AddAction(GetAction(MobileDestination.Settings));
        }

        public MobileActionControl GetAction(MobileDestination destination) => actions[(int)destination];

        public static string SceneName(MobileDestination destination)
        {
            switch (destination)
            {
                case MobileDestination.Home: return "002_MainMenuScene";
                case MobileDestination.Gacha: return "003_GachaScene";
                case MobileDestination.Collection: return "004_CollectionScene";
                case MobileDestination.Content: return "006_ContentScene";
                case MobileDestination.Settings: return "005_SettingScene";
                case MobileDestination.Pokedex: return "004_CollectionScene";
                default: throw new ArgumentOutOfRangeException(nameof(destination), destination, null);
            }
        }

        public void SetPending(MobileDestination destination)
        {
            SelectDestination(destination);
            MobileActionControl pendingAction = GetAction(destination);
            foreach (MobileActionControl action in actions)
            {
                action.SetLoading(ReferenceEquals(action, pendingAction));
                action.SetEnabled(false);
            }
        }

        public void ClearPending(MobileDestination selected)
        {
            SelectDestination(selected);
            foreach (MobileActionControl action in actions)
            {
                action.SetLoading(false);
                action.SetEnabled(true);
            }
        }

        public void RefreshText()
        {
            GetAction(MobileDestination.Home).SetLabel(CardUiText.Get("home.nav.home"));
            GetAction(MobileDestination.Gacha).SetLabel(CardUiText.Get("main_menu.action.gacha"));
            GetAction(MobileDestination.Collection).SetLabel(CardUiText.Get("main_menu.action.collection"));
            GetAction(MobileDestination.Content).SetLabel(CardUiText.Get("main_menu.action.content"));
            GetAction(MobileDestination.Settings).SetLabel(CardUiText.Get("main_menu.action.settings"));
            GetAction(MobileDestination.Pokedex).SetLabel(CardUiText.Get("home.nav.pokedex"));
        }

        private void SelectDestination(MobileDestination destination) =>
            BottomNavigation.Select(Array.IndexOf(visibleDestinations, destination));

        public void SetEnabled(bool enabled)
        {
            foreach (MobileActionControl action in actions)
                action.SetEnabled(enabled);
        }

        public void Dispose()
        {
            CardUiText.TextChanged -= RefreshText;
            foreach (MobileActionControl action in actions)
                action.Dispose();
        }

        private static MobileActionControl Action(
            MobileDestination destination,
            Action<MobileDestination> navigate)
        {
            return new MobileActionControl(
                "nav-" + destination.ToString().ToLowerInvariant(),
                string.Empty,
                () => navigate(destination),
                destination == MobileDestination.Settings ? MobileActionTone.Quiet : MobileActionTone.Navigation);
        }
    }
}
