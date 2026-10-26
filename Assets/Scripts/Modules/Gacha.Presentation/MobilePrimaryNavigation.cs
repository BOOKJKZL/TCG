using System;

namespace Gacha.Presentation
{
    public enum MobileDestination
    {
        Home,
        Gacha,
        Collection,
        Content,
        Settings
    }

    public sealed class MobilePrimaryNavigation : IDisposable
    {
        private readonly MobileActionControl[] actions;

        public MobilePrimaryNavigation(
            MobileDestination selected,
            Action<MobileDestination> navigate)
        {
            if (navigate == null)
                throw new ArgumentNullException(nameof(navigate));
            actions = new[]
            {
                Action(MobileDestination.Home, navigate),
                Action(MobileDestination.Gacha, navigate),
                Action(MobileDestination.Collection, navigate),
                Action(MobileDestination.Content, navigate),
                Action(MobileDestination.Settings, navigate)
            };
            BottomNavigation = new MobileBottomNavigation(actions[0], actions[1], actions[2], actions[3]);
            BottomNavigation.Select((int)selected);
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
                default: throw new ArgumentOutOfRangeException(nameof(destination), destination, null);
            }
        }

        public void SetPending(MobileDestination destination)
        {
            BottomNavigation.Select((int)destination);
            MobileActionControl pendingAction = GetAction(destination);
            foreach (MobileActionControl action in actions)
            {
                action.SetLoading(ReferenceEquals(action, pendingAction));
                action.SetEnabled(false);
            }
        }

        public void ClearPending(MobileDestination selected)
        {
            BottomNavigation.Select((int)selected);
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
        }

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
