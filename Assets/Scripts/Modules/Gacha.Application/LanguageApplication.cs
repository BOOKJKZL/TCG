using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Gacha.Domain;

namespace Gacha.Application
{
    public sealed class LanguagePreferences
    {
        public LanguagePreferences(string uiLanguageId, string contentLanguageId)
        {
            UiLanguageId = uiLanguageId;
            ContentLanguageId = contentLanguageId;
        }

        public string UiLanguageId { get; }
        public string ContentLanguageId { get; }
    }

    public interface ILanguagePreferenceStore
    {
        LanguagePreferences Load();
        void Save(LanguagePreferences preferences);
    }

    public sealed class ContentLanguageSelection
    {
        public ContentLanguageSelection(string requestedLanguageId, string resolvedLanguageId)
        {
            RequestedLanguageId = requestedLanguageId;
            ResolvedLanguageId = resolvedLanguageId;
        }

        public string RequestedLanguageId { get; }
        public string ResolvedLanguageId { get; }
        public bool UsedFallback => !string.Equals(
            RequestedLanguageId,
            ResolvedLanguageId,
            StringComparison.OrdinalIgnoreCase);
    }

    public sealed class LanguageSelectionService
    {
        private readonly ILanguagePreferenceStore store;
        private readonly string defaultUiLanguageId;
        private readonly string defaultContentLanguageId;
        private readonly ReadOnlyCollection<string> availableUiLanguageIds;
        private UniversalCatalog currentCatalog;

        public LanguageSelectionService(
            ILanguagePreferenceStore store,
            IEnumerable<string> availableUiLanguageIds,
            string defaultUiLanguageId = "en",
            string defaultContentLanguageId = "en")
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.defaultUiLanguageId = Required(defaultUiLanguageId, nameof(defaultUiLanguageId));
            this.defaultContentLanguageId = Required(defaultContentLanguageId, nameof(defaultContentLanguageId));

            string[] uiLanguages = (availableUiLanguageIds ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (uiLanguages.Length == 0)
                throw new ArgumentException("At least one UI language is required.", nameof(availableUiLanguageIds));

            this.availableUiLanguageIds = new ReadOnlyCollection<string>(uiLanguages);
            LanguagePreferences saved = store.Load();
            UiLanguageId = ResolveUiLanguage(saved?.UiLanguageId);
            RequestedContentLanguageId = NormalizeOrDefault(saved?.ContentLanguageId, this.defaultContentLanguageId);
            ContentLanguage = new ContentLanguageSelection(RequestedContentLanguageId, RequestedContentLanguageId);
        }

        public IReadOnlyList<string> AvailableUiLanguageIds => availableUiLanguageIds;
        public string UiLanguageId { get; private set; }
        public string RequestedContentLanguageId { get; private set; }
        public ContentLanguageSelection ContentLanguage { get; private set; }

        public event Action<string> UiLanguageChanged;
        public event Action<ContentLanguageSelection> ContentLanguageChanged;

        public bool SelectUiLanguage(string languageId)
        {
            string resolved = ResolveUiLanguage(languageId);
            if (string.Equals(UiLanguageId, resolved, StringComparison.OrdinalIgnoreCase))
                return false;

            store.Save(new LanguagePreferences(resolved, RequestedContentLanguageId));
            UiLanguageId = resolved;
            UiLanguageChanged?.Invoke(UiLanguageId);
            return true;
        }

        public ContentLanguageSelection SelectContentLanguage(string languageId, UniversalCatalog catalog)
        {
            string requested = NormalizeOrDefault(languageId, defaultContentLanguageId);
            string resolved = ResolveAvailableContentLanguage(requested, catalog);
            var next = new ContentLanguageSelection(requested, resolved);
            bool changed = ContentLanguage == null ||
                           !string.Equals(ContentLanguage.RequestedLanguageId, next.RequestedLanguageId, StringComparison.OrdinalIgnoreCase) ||
                           !string.Equals(ContentLanguage.ResolvedLanguageId, next.ResolvedLanguageId, StringComparison.OrdinalIgnoreCase);
            store.Save(new LanguagePreferences(UiLanguageId, requested));
            currentCatalog = catalog;
            RequestedContentLanguageId = requested;
            ContentLanguage = next;
            if (changed)
                ContentLanguageChanged?.Invoke(ContentLanguage);
            return ContentLanguage;
        }

        public ContentLanguageSelection RefreshContentLanguage(UniversalCatalog catalog)
        {
            return ResolveContentLanguage(catalog);
        }

        public void ApplyPreferences(LanguagePreferences preferences, UniversalCatalog catalog)
        {
            if (preferences == null)
                throw new ArgumentNullException(nameof(preferences));

            string nextUiLanguageId = ResolveUiLanguage(preferences.UiLanguageId);
            string nextRequestedContentLanguageId = NormalizeOrDefault(
                preferences.ContentLanguageId,
                defaultContentLanguageId);
            string nextResolvedContentLanguageId = ResolveAvailableContentLanguage(
                nextRequestedContentLanguageId,
                catalog);
            var nextContentLanguage = new ContentLanguageSelection(
                nextRequestedContentLanguageId,
                nextResolvedContentLanguageId);
            store.Save(new LanguagePreferences(
                nextUiLanguageId,
                nextRequestedContentLanguageId));

            bool uiChanged = !string.Equals(
                UiLanguageId,
                nextUiLanguageId,
                StringComparison.OrdinalIgnoreCase);
            bool contentChanged = ContentLanguage == null ||
                !string.Equals(
                    ContentLanguage.RequestedLanguageId,
                    nextContentLanguage.RequestedLanguageId,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    ContentLanguage.ResolvedLanguageId,
                    nextContentLanguage.ResolvedLanguageId,
                    StringComparison.OrdinalIgnoreCase);
            currentCatalog = catalog;
            UiLanguageId = nextUiLanguageId;
            RequestedContentLanguageId = nextRequestedContentLanguageId;
            ContentLanguage = nextContentLanguage;
            if (uiChanged) UiLanguageChanged?.Invoke(UiLanguageId);
            if (contentChanged) ContentLanguageChanged?.Invoke(ContentLanguage);
        }

        public string GetDisplayName(Definition definition)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));

            foreach (string languageId in DisplayNameFallbacks(ContentLanguage.ResolvedLanguageId))
            {
                if (definition.Names.TryGetValue(languageId, out string localized))
                    return localized;
            }

            return definition.Names.Values.First();
        }

        private ContentLanguageSelection ResolveContentLanguage(UniversalCatalog catalog)
        {
            currentCatalog = catalog;
            string resolved = ResolveAvailableContentLanguage(RequestedContentLanguageId, catalog);
            ContentLanguageSelection next = new ContentLanguageSelection(RequestedContentLanguageId, resolved);
            bool changed = ContentLanguage == null ||
                           !string.Equals(ContentLanguage.RequestedLanguageId, next.RequestedLanguageId, StringComparison.OrdinalIgnoreCase) ||
                           !string.Equals(ContentLanguage.ResolvedLanguageId, next.ResolvedLanguageId, StringComparison.OrdinalIgnoreCase);
            ContentLanguage = next;

            if (changed)
                ContentLanguageChanged?.Invoke(ContentLanguage);
            return ContentLanguage;
        }

        private string ResolveUiLanguage(string requested)
        {
            return MatchAvailable(requested, availableUiLanguageIds) ??
                   MatchAvailable(ParentLanguage(requested), availableUiLanguageIds) ??
                   MatchAvailable(defaultUiLanguageId, availableUiLanguageIds) ??
                   availableUiLanguageIds[0];
        }

        private string ResolveAvailableContentLanguage(string requested, UniversalCatalog catalog)
        {
            if (catalog == null || catalog.Languages.Count == 0)
                return NormalizeOrDefault(requested, defaultContentLanguageId);

            string[] available = catalog.Languages.Keys.ToArray();
            string match = MatchAvailable(requested, available) ??
                           MatchAvailable(ParentLanguage(requested), available);
            if (match != null)
                return match;

            foreach (string candidate in RegionalFallbacks(requested))
            {
                match = MatchAvailable(candidate, available);
                if (match != null)
                    return match;
            }

            return MatchAvailable(defaultContentLanguageId, available) ??
                   available.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).First();
        }

        private IEnumerable<string> DisplayNameFallbacks(string languageId)
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string current = languageId;
            while (!string.IsNullOrWhiteSpace(current) && visited.Add(current))
            {
                yield return current;
                if (currentCatalog == null || !currentCatalog.Languages.TryGetValue(current, out LanguageDefinition definition))
                    break;
                current = definition.FallbackLanguageId;
            }

            foreach (string regional in RegionalFallbacks(languageId))
                if (visited.Add(regional))
                    yield return regional;

            if (visited.Add(defaultContentLanguageId))
                yield return defaultContentLanguageId;
        }

        private static IEnumerable<string> RegionalFallbacks(string languageId)
        {
            if (string.IsNullOrWhiteSpace(languageId))
                yield break;

            string normalized = languageId.Trim().Replace('_', '-');
            if (string.Equals(normalized, "zh-CN", StringComparison.OrdinalIgnoreCase))
                yield return "zh-TW";
            else if (string.Equals(normalized, "zh-TW", StringComparison.OrdinalIgnoreCase))
                yield return "zh-CN";
        }

        private static string MatchAvailable(string requested, IEnumerable<string> available)
        {
            if (string.IsNullOrWhiteSpace(requested))
                return null;

            return available.FirstOrDefault(value =>
                string.Equals(value, requested.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        private static string ParentLanguage(string languageId)
        {
            if (string.IsNullOrWhiteSpace(languageId))
                return null;
            string normalized = languageId.Trim().Replace('_', '-');
            int separator = normalized.IndexOf('-');
            return separator > 0 ? normalized.Substring(0, separator) : null;
        }

        private static string NormalizeOrDefault(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().Replace('_', '-');
        }

        private static string Required(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException($"{name} cannot be empty.", name);
            return value.Trim();
        }
    }
}
