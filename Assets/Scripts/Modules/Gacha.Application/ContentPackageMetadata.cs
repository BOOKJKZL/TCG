using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Gacha.Application
{
    public sealed class ContentPackageMetadata
    {
        public ContentPackageMetadata(
            string kind,
            IReadOnlyDictionary<string, string> localizedNames,
            string gameId = null,
            string contentLanguageId = null,
            string setId = null,
            string setCode = null,
            DateTime? releaseDate = null,
            int? generationOrder = null,
            int? sortOrdinal = null,
            IEnumerable<string> tags = null,
            IEnumerable<string> dependencies = null)
        {
            Kind = Required(kind, nameof(kind)).ToLowerInvariant();
            LocalizedNames = Names(localizedNames);
            GameId = Optional(gameId);
            ContentLanguageId = NormalizeLanguage(contentLanguageId);
            SetId = Optional(setId);
            SetCode = Optional(setCode);
            ReleaseDate = releaseDate?.Date;
            if (generationOrder < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(generationOrder), "Generation order cannot be negative.");
            if (sortOrdinal < 0)
                throw new ArgumentOutOfRangeException(nameof(sortOrdinal), "Sort ordinal cannot be negative.");
            GenerationOrder = generationOrder;
            SortOrdinal = sortOrdinal;
            Tags = Strings(tags, true);
            Dependencies = Strings(dependencies, false);
        }

        public string Kind { get; }
        public string GameId { get; }
        public string ContentLanguageId { get; }
        public IReadOnlyDictionary<string, string> LocalizedNames { get; }
        public string SetId { get; }
        public string SetCode { get; }
        public DateTime? ReleaseDate { get; }
        public int? GenerationOrder { get; }
        public int? SortOrdinal { get; }
        public IReadOnlyList<string> Tags { get; }
        public IReadOnlyList<string> Dependencies { get; }
        public bool IsLegacy => string.Equals(Kind, "legacy", StringComparison.Ordinal);

        public string GetDisplayName(string languageId, string fallback)
        {
            string normalized = NormalizeLanguage(languageId);
            if (normalized != null && LocalizedNames.TryGetValue(normalized, out string exact))
                return exact;
            string parent = ParentLanguage(normalized);
            if (parent != null && LocalizedNames.TryGetValue(parent, out string parentValue))
                return parentValue;
            if (LocalizedNames.TryGetValue("en", out string english))
                return english;
            return LocalizedNames.Values.FirstOrDefault() ?? Optional(fallback) ?? Kind;
        }

        public static ContentPackageMetadata Legacy(string packageId) =>
            new ContentPackageMetadata(
                "legacy",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["en"] = Required(packageId, nameof(packageId))
                });

        private static IReadOnlyDictionary<string, string> Names(
            IReadOnlyDictionary<string, string> source)
        {
            if (source == null || source.Count == 0)
                throw new ArgumentException("At least one localized package name is required.",
                    nameof(source));
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> pair in source
                         .OrderBy(value => value.Key, StringComparer.OrdinalIgnoreCase))
            {
                string language = NormalizeLanguage(pair.Key);
                if (language == null)
                    throw new ArgumentException("Localized package name has no language id.", nameof(source));
                if (!values.TryAdd(language, Required(pair.Value, nameof(source))))
                    throw new ArgumentException(
                        $"Localized package name repeats language '{language}'.", nameof(source));
            }
            return new ReadOnlyDictionary<string, string>(values);
        }

        private static IReadOnlyList<string> Strings(IEnumerable<string> source, bool lowerCase)
        {
            return new ReadOnlyCollection<string>((source ?? Array.Empty<string>())
                .Select(value => Required(value, nameof(source)))
                .Select(value => lowerCase ? value.ToLowerInvariant() : value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray());
        }

        private static string NormalizeLanguage(string value) =>
            string.IsNullOrWhiteSpace(value)
                ? null
                : value.Trim().Replace('_', '-').ToLowerInvariant();

        private static string ParentLanguage(string value)
        {
            int separator = value?.IndexOf('-') ?? -1;
            return separator > 0 ? value.Substring(0, separator) : null;
        }

        private static string Required(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException(name + " cannot be empty.", name);
            return value.Trim();
        }

        private static string Optional(string value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
