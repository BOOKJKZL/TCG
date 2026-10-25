using System;
using UnityEngine;

namespace Gacha.Presentation
{
    public static class SettingsReturnNavigation
    {
        private static MobileDestination? returnDestination;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState() => Clear();

        public static void Remember(MobileDestination source)
        {
            switch (source)
            {
                case MobileDestination.Home:
                case MobileDestination.Gacha:
                case MobileDestination.Collection:
                case MobileDestination.Content:
                    returnDestination = source;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(source), source, "Settings requires a primary destination as its return source.");
            }
        }

        public static MobileDestination PeekOrDefault() => returnDestination ?? MobileDestination.Home;

        public static void Clear() => returnDestination = null;
    }
}
