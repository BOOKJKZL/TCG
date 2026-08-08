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
            BottomNavigation = new MobileBottomNavigation(actions);
            BottomNavigation.Select((int)selected);
            RefreshText();
        }

        public MobileBottomNavigation BottomNavigation { get; }
        public int Count => actions.Length;

        public MobileActionControl GetAction(MobileDestination destination) => actions[(int)destination];

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

        public void Dispose()
        {
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
                MobileActionTone.Navigation);
        }
    }
}
