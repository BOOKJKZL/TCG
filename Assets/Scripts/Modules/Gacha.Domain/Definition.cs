using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Gacha.Domain
{
    public abstract class Definition
    {
        protected Definition(string id, IReadOnlyDictionary<string, string> names)
        {
            Id = Required(id, nameof(id));
            Names = CopyNames(names);
        }

        public string Id { get; }
        public IReadOnlyDictionary<string, string> Names { get; }

        public string GetDisplayName(string languageId, string fallbackLanguageId = "en")
        {
            if (!string.IsNullOrWhiteSpace(languageId) && Names.TryGetValue(languageId, out string localized))
            {
                return localized;
            }

            if (!string.IsNullOrWhiteSpace(fallbackLanguageId) && Names.TryGetValue(fallbackLanguageId, out string fallback))
            {
                return fallback;
            }

            return Names.Values.First();
        }

        internal static string Required(string value, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException($"{fieldName} cannot be empty.", fieldName);
            }

            return value.Trim();
        }

        internal static IReadOnlyList<string> CopyStrings(IEnumerable<string> values)
        {
            return new ReadOnlyCollection<string>((values ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList());
        }

        private static IReadOnlyDictionary<string, string> CopyNames(IReadOnlyDictionary<string, string> names)
        {
            if (names == null || names.Count == 0)
            {
                throw new ArgumentException("At least one localized name is required.", nameof(names));
            }

            Dictionary<string, string> copy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> entry in names)
            {
                if (!string.IsNullOrWhiteSpace(entry.Key) && !string.IsNullOrWhiteSpace(entry.Value))
                {
                    copy[entry.Key.Trim()] = entry.Value.Trim();
                }
            }

            if (copy.Count == 0)
            {
                throw new ArgumentException("At least one valid localized name is required.", nameof(names));
            }

            return new ReadOnlyDictionary<string, string>(copy);
        }
    }
}
