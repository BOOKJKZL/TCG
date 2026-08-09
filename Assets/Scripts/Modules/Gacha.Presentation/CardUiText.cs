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
                ["common.action.manage_content"] = "Manage content",
                ["common.action.close"] = "Close",
                ["common.action.cancel"] = "Cancel",
                ["common.action.clear"] = "Clear",
                ["player_error.not_installed.title"] = "Content not installed",
                ["player_error.not_installed.body"] = "Choose a content pack to continue.",
                ["player_error.offline.title"] = "You're offline",
                ["player_error.offline.body"] = "Connect and retry, or continue with downloaded content.",
                ["player_error.catalog_corrupt.title"] = "Content list needs attention",
                ["player_error.catalog_corrupt.body"] = "Retry. If this continues, manage your downloaded content.",
                ["player_error.verification_failed.title"] = "Content verification failed",
                ["player_error.verification_failed.body"] = "This content was not installed. Download it again.",
                ["player_error.insufficient_space.title"] = "Not enough storage",
                ["player_error.insufficient_space.body"] = "Free some space or remove downloaded content, then retry.",
                ["player_error.service_unavailable.title"] = "Service unavailable",
                ["player_error.service_unavailable.body"] = "Please retry in a moment.",
                ["player_error.unexpected.title"] = "Couldn't continue",
                ["player_error.unexpected.body"] = "Retry or return to the main menu.",
                ["common.badge.new"] = "NEW",
                ["card_image.error.invalid_path"] = "Invalid image path",
                ["card_image.error.not_installed"] = "Image not installed",
                ["card_image.error.verification_failed"] = "Image verification failed",
                ["card_image.error.loading_failed"] = "Image loading failed",
                ["collection.title"] = "Card Collection",
                ["collection.subtitle"] = "Browse installed sets, search your cards, and track new pulls",
                ["collection.action.all_sets"] = "All sets",
                ["collection.status.unavailable"] = "Collection unavailable: {0}",
                ["collection.status.no_content"] = "No card sets are installed yet. Open Content Library to choose what to download.",
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
                ["collection.unowned"] = "Not owned",
                ["gacha.status.unavailable"] = "Pack opening unavailable: {0}",
                ["gacha.pack.hint"] = "Tap to tear this simulated pack",
                ["gacha.status.open_failed"] = "Could not open this pack: {0}",
                ["gacha.badge.owned"] = "OWNED",
                ["gacha.reveal.progress"] = "Card {0} of {1}",
                ["gacha.action.view_results"] = "View results",
                ["gacha.action.reveal_next"] = "Reveal next",
                ["gacha.status.no_products"] = "No products are installed for this content language.",
                ["gacha.rule.verified"] = "VERIFIED RULES",
                ["gacha.rule.sourced_simulation"] = "SOURCED SIMULATION",
                ["gacha.rule.simulation"] = "SIMULATION",
                ["gacha.rule.simulation_notice"] = "Equal odds per installed printing. This is not historical pack collation.",
                ["gacha.rule.evidence.checked"] = "{0} · Confidence: {1} · checked {2}",
                ["gacha.rule.evidence.unverified"] = "Region and historical collation are unverified.",
                ["gacha.rule.confidence.unverified"] = "Unverified",
                ["gacha.rule.confidence.corroborated"] = "Corroborated",
                ["gacha.rule.confidence.authoritative"] = "Authoritative",
                ["gacha.action.rule_source_number"] = "Source {0}: {1}",
                ["gacha.reveal.ready"] = "Cards are ready",
                ["gacha.reveal.one_at_time"] = "Reveal them one at a time",
                ["gacha.reveal.pending_progress"] = "0 of {0} cards",
                ["gacha.action.reveal_first"] = "Reveal first card",
                ["gacha.action.reveal_all"] = "Reveal all",
                ["gacha.summary.title"] = "Pack complete",
                ["gacha.summary.metadata"] = "{0} cards · {1} new · Pack #{2}",
                ["gacha.title"] = "Open a Pack",
                ["gacha.subtitle"] = "Choose installed content, inspect the rule, then reveal every card",
                ["gacha.action.prepare"] = "Prepare pack",
                ["gacha.action.open_one"] = "Open 1 pack",
                ["gacha.action.open_ten"] = "Open 10 packs",
                ["gacha.action.open_ten_again"] = "Open 10 again",
                ["gacha.confirm.title"] = "Confirm pack opening",
                ["gacha.confirm.body"] = "{0} × {1}\nCard language: {2}\nRule: {3}\nYour collection updates when you tear the pack.",
                ["gacha.action.confirm_open"] = "Open now",
                ["gacha.action.rule_source"] = "Rule source",
                ["gacha.action.tear"] = "Tear pack",
                ["gacha.action.all_products"] = "All products",
                ["gacha.action.open_another"] = "Open another",
                ["gacha.action.choose_another"] = "Choose another",
                ["gacha.odds.heading"] = "Average chance per card slot",
                ["gacha.product.metadata"] = "{0} · {1} printings · {2}",
                ["gacha.reveal.metadata"] = "#{0} · {1} · {2} · Owned {3}",
                ["gacha.pack.batch_title"] = "{1} ×{0}",
                ["gacha.pack.batch_hint"] = "The first of {0} packs keeps the full ceremony; later cards use short transitions. Reveal all to skip.",
                ["gacha.reveal.batch_progress"] = "Pack {0}/{1} · Card {2}/{3}",
                ["gacha.summary.batch_title"] = "Batch complete",
                ["gacha.summary.batch_metadata"] = "{0} packs · {1} cards · {2} new · Pack #{3}",
                ["gacha.statistics.empty"] = "No opening history yet.",
                ["gacha.statistics.summary"] = "{0} packs · {1} cards\nLanguages: {2}\nSets: {3}\nRarities: {4}",
                ["gacha.history.empty"] = "Recent batches will appear here.",
                ["gacha.history.row"] = "{0} · {1} ×{2} · {3} cards · {4} new · {5}",
                ["main_menu.action.gacha"] = "Gacha",
                ["main_menu.action.collection"] = "Collection",
                ["main_menu.action.content"] = "Content",
                ["main_menu.action.settings"] = "Settings",
                ["home.top.title"] = "Trainer Home",
                ["home.top.subtitle"] = "Offline-first card archive",
                ["home.kicker"] = "TRAINER HUB",
                ["home.title"] = "Your card archive, one pack at a time",
                ["home.body"] = "Open simulated packs, review your collection, and install only the card languages you want.",
                ["home.section.destinations"] = "Choose your next stop",
                ["home.feature.gacha"] = "Choose an installed product and reveal every card.",
                ["home.feature.collection"] = "Browse owned cards, new pulls, and set completion.",
                ["home.feature.content"] = "Install, update, repair, or remove downloadable card data.",
                ["home.feature.settings"] = "Change language, feedback, save recovery, and account options.",
                ["home.nav.home"] = "Home",
                ["settings.title"] = "Settings",
                ["settings.recovery.title"] = "Save recovery",
                ["settings.recovery.description"] = "Export a verified backup or preview a file before importing.",
                ["settings.recovery.action.export"] = "Export backup",
                ["settings.recovery.action.preview"] = "Choose backup",
                ["settings.recovery.action.confirm"] = "Confirm import",
                ["settings.recovery.status.ready"] = "Ready. Downloaded card images are never included.",
                ["settings.recovery.status.exported"] = "Exported to {0}",
                ["settings.recovery.status.cancelled"] = "File selection cancelled.",
                ["settings.recovery.status.error"] = "Recovery failed: {0}",
                ["settings.recovery.status.preview_ready"] = "Preview verified. Confirm to replace current progress.",
                ["settings.recovery.status.imported"] = "Import complete. Pre-import backup: {0}",
                ["settings.recovery.status.unavailable"] = "Save recovery is unavailable.",
                ["settings.recovery.preview"] = "{0} · {1} printings / {2} cards · {3} packs · {4} batches · UI {5} / cards {6}",
                ["settings.recovery.action.cloud"] = "Cloud sync",
                ["settings.cloud.title"] = "Cloud save conflict",
                ["settings.cloud.description"] = "Review both saves before choosing. Downloaded card images are not affected.",
                ["settings.cloud.status.none"] = "No unresolved cloud conflict.",
                ["settings.cloud.local"] = "Local progress",
                ["settings.cloud.remote"] = "Cloud progress",
                ["settings.cloud.summary"] = "{0}\n{1} printings / {2} cards · {3} packs · {4} batches",
                ["settings.cloud.action.local"] = "Keep local",
                ["settings.cloud.action.remote"] = "Use cloud",
                ["settings.cloud.action.merge"] = "Safe merge",
                ["settings.cloud.action.close"] = "Close",
                ["settings.cloud.merge_notice"] = "Safe merge keeps the highest count for each card and unites distinct recent batches; it never adds duplicate snapshot counts.",
                ["settings.cloud.status.resolving"] = "Saving your choice…",
                ["settings.cloud.status.resolved"] = "Conflict resolved. Safety backup: {0}",
                ["settings.cloud.status.failed"] = "Nothing changed: {0}",
                ["settings.cloud.status.backup_failed"] = "Could not create a safety backup: {0}",
                ["settings.identity.status.setup_required"] = "Recoverable PLAYER ID needs external Unity Player Accounts setup. Progress remains on this device.",
                ["settings.identity.status.available"] = "Add a recoverable PLAYER ID to this progress.",
                ["settings.identity.status.connected"] = "Recoverable PLAYER ID: {0}",
                ["settings.identity.action.connect"] = "Set PLAYER ID",
                ["settings.identity.status.connecting"] = "Opening secure sign-in…",
                ["settings.identity.status.linked"] = "Recoverable PLAYER ID is ready.",
                ["settings.identity.status.cloud_pending"] = "PLAYER ID is linked, but initial cloud sync is pending: {0}",
                ["settings.identity.status.conflict"] = "PLAYER ID is ready. Complete the save choice above.",
                ["settings.identity.status.failed"] = "PLAYER ID was not changed: {0}"
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
