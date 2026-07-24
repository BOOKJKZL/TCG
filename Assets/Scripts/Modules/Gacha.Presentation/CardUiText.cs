using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

namespace Gacha.Presentation
{
    public static class CardUiText
    {
        public const string TableName = "Card_UI";

        public static readonly IReadOnlyDictionary<string, string> EnglishFallbacks =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["common.action.retry"] = "Retry",
                ["common.status.loading"] = "Loading…",
                ["common.action.main_menu"] = "Main menu",
                ["common.action.close"] = "Close",
                ["common.action.clear"] = "Clear",
                ["common.badge.new"] = "NEW",
                ["card_image.error.invalid_path"] = "Invalid image path",
                ["card_image.error.not_installed"] = "Image not installed",
                ["card_image.error.verification_failed"] = "Image verification failed",
                ["card_image.error.loading_failed"] = "Image loading failed",
                ["collection.title"] = "Card Collection",
                ["collection.subtitle"] = "Browse installed sets, search your cards, and track new pulls",
                ["collection.action.all_sets"] = "All sets",
                ["collection.status.unavailable"] = "Collection unavailable: {0}",
                ["collection.set.metadata"] = "{0} · {1}/{2} collected · {3} new · {4}",
                ["collection.filter.empty"] = "No cards match these filters.",
                ["collection.filter.all_rarities"] = "All rarities",
                ["collection.filter.search"] = "Search name or number",
                ["collection.filter.rarity"] = "Rarity",
                ["collection.filter.owned_on"] = "Owned: ON",
                ["collection.filter.owned_off"] = "Owned: OFF",
                ["collection.filter.new_on"] = "New: ON",
                ["collection.filter.new_off"] = "New: OFF",
                ["collection.status.seen_save_failed"] = "Couldn't save the viewed-card status. The NEW badge was kept.",
                ["collection.summary.all"] = "{0} installed sets · {1}/{2} collected · {3} new",
                ["collection.summary.filtered"] = "{0} shown · {1}/{2} collected · {3} new",
                ["collection.owned"] = "Owned ×{0}",
                ["collection.unowned"] = "Not owned"
            };

        private static readonly Dictionary<string, string> LocalizedCache =
            new Dictionary<string, string>(StringComparer.Ordinal);

        public static string Get(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return string.Empty;

            string fallback = EnglishFallbacks.TryGetValue(key, out string value) ? value : key;
            if (!LocalizationSettings.HasSettings)
                return fallback;

            try
            {
                Locale locale = LocalizationSettings.SelectedLocale;
                string localeId = locale?.Identifier.Code ?? string.Empty;
                string cacheKey = localeId + "\n" + key;
                if (LocalizedCache.TryGetValue(cacheKey, out string localized))
                    return localized;

                localized = LocalizationSettings.StringDatabase.GetLocalizedString(
                    TableName,
                    key,
                    locale);
                if (string.IsNullOrWhiteSpace(localized))
                    return fallback;

                LocalizedCache[cacheKey] = localized;
                return localized;
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        public static string Format(string key, params object[] arguments)
        {
            return string.Format(CultureInfo.CurrentCulture, Get(key), arguments ?? Array.Empty<object>());
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            LocalizedCache.Clear();
        }
    }
}
