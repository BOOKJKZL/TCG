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

            UiLanguageId = resolved;
            Save();
            UiLanguageChanged?.Invoke(UiLanguageId);
            return true;
        }

        public ContentLanguageSelection SelectContentLanguage(string languageId, UniversalCatalog catalog)
        {
            RequestedContentLanguageId = NormalizeOrDefault(languageId, defaultContentLanguageId);
            return ResolveContentLanguage(catalog, true);
        }

        public ContentLanguageSelection RefreshContentLanguage(UniversalCatalog catalog)
        {
            return ResolveContentLanguage(catalog, false);
        }

        public string GetDisplayName(Definition definition)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));

            return definition.GetDisplayName(
                ContentLanguage.ResolvedLanguageId,
                defaultContentLanguageId);
        }

        private ContentLanguageSelection ResolveContentLanguage(UniversalCatalog catalog, bool persistRequest)
        {
            string resolved = ResolveAvailableContentLanguage(RequestedContentLanguageId, catalog);
            ContentLanguageSelection next = new ContentLanguageSelection(RequestedContentLanguageId, resolved);
            bool changed = ContentLanguage == null ||
                           !string.Equals(ContentLanguage.RequestedLanguageId, next.RequestedLanguageId, StringComparison.OrdinalIgnoreCase) ||
                           !string.Equals(ContentLanguage.ResolvedLanguageId, next.ResolvedLanguageId, StringComparison.OrdinalIgnoreCase);
            ContentLanguage = next;

            if (persistRequest)
                Save();
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

            string fallback = ResolveDefinitionFallback(requested, catalog, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            return fallback ??
                   MatchAvailable(defaultContentLanguageId, available) ??
                   available.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).First();
        }

        private static string ResolveDefinitionFallback(
            string languageId,
            UniversalCatalog catalog,
            ISet<string> visited)
        {
            string canonical = MatchAvailable(languageId, catalog.Languages.Keys);
            if (canonical == null || !visited.Add(canonical))
                return null;

            LanguageDefinition language = catalog.Languages[canonical];
            if (string.IsNullOrWhiteSpace(language.FallbackLanguageId))
                return null;

            string fallback = MatchAvailable(language.FallbackLanguageId, catalog.Languages.Keys);
            return fallback ?? ResolveDefinitionFallback(language.FallbackLanguageId, catalog, visited);
        }

        private void Save()
        {
            store.Save(new LanguagePreferences(UiLanguageId, RequestedContentLanguageId));
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
