using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Gacha.Domain
{
    public enum PrintingLanguageMatchMethod
    {
        SharedItemIdentity,
        SourceIdentity,
        ManualOverride
    }

    public enum PrintingLanguageReviewStatus
    {
        AutoAccepted,
        Reviewed
    }

    public sealed class PrintingLanguageGroupDefinition
    {
        public PrintingLanguageGroupDefinition(
            string id,
            IEnumerable<string> printingIds,
            PrintingLanguageMatchMethod matchMethod,
            double confidence,
            PrintingLanguageReviewStatus reviewStatus = PrintingLanguageReviewStatus.AutoAccepted)
        {
            Id = Definition.Required(id, nameof(id));
            string[] members = (printingIds ?? Array.Empty<string>())
                .Select(value => Definition.Required(value, nameof(printingIds)))
                .ToArray();
            if (members.Length < 2)
                throw new ArgumentException("A language group requires at least two printings.", nameof(printingIds));
            if (members.Distinct(StringComparer.Ordinal).Count() != members.Length)
                throw new ArgumentException("A language group cannot repeat a printing.", nameof(printingIds));
            if (double.IsNaN(confidence) || double.IsInfinity(confidence) || confidence < 0d || confidence > 1d)
                throw new ArgumentOutOfRangeException(nameof(confidence), "Confidence must be between zero and one.");

            PrintingIds = new ReadOnlyCollection<string>(members);
            MatchMethod = matchMethod;
            Confidence = confidence;
            ReviewStatus = reviewStatus;
        }

        public string Id { get; }
        public IReadOnlyList<string> PrintingIds { get; }
        public PrintingLanguageMatchMethod MatchMethod { get; }
        public double Confidence { get; }
        public PrintingLanguageReviewStatus ReviewStatus { get; }
    }

    public sealed class PrintingLanguageGroup
    {
        internal PrintingLanguageGroup(
            string id,
            IEnumerable<PrintingDefinition> printings,
            PrintingLanguageMatchMethod matchMethod,
            double confidence,
            PrintingLanguageReviewStatus reviewStatus)
        {
            Id = id;
            Printings = new ReadOnlyCollection<PrintingDefinition>((printings ?? Array.Empty<PrintingDefinition>())
                .OrderBy(value => value.Identity.LanguageId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(value => value.Id, StringComparer.Ordinal)
                .ToArray());
            MatchMethod = matchMethod;
            Confidence = confidence;
            ReviewStatus = reviewStatus;
            AvailableLanguageIds = new ReadOnlyCollection<string>(Printings
                .Select(value => value.Identity.LanguageId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray());
        }

        public string Id { get; }
        public IReadOnlyList<PrintingDefinition> Printings { get; }
        public IReadOnlyList<string> AvailableLanguageIds { get; }
        public PrintingLanguageMatchMethod MatchMethod { get; }
        public double Confidence { get; }
        public PrintingLanguageReviewStatus ReviewStatus { get; }
        public bool HasMultipleLanguages => AvailableLanguageIds.Count > 1;

        public PrintingDefinition Find(string languageId)
        {
            if (string.IsNullOrWhiteSpace(languageId))
                return null;
            string normalized = NormalizeLanguage(languageId);
            PrintingDefinition exact = Printings.FirstOrDefault(value =>
                string.Equals(NormalizeLanguage(value.Identity.LanguageId), normalized, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
                return exact;

            string parent = ParentLanguage(normalized);
            if (parent == null)
                parent = normalized;
            PrintingDefinition[] regional = Printings.Where(value =>
                    string.Equals(ParentLanguage(NormalizeLanguage(value.Identity.LanguageId)) ??
                                  NormalizeLanguage(value.Identity.LanguageId), parent, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            return regional.Length == 1 ? regional[0] : null;
        }

        private static string NormalizeLanguage(string value) => value.Trim().Replace('_', '-');

        private static string ParentLanguage(string value)
        {
            int separator = value.IndexOf('-');
            return separator > 0 ? value.Substring(0, separator) : null;
        }
    }

    public sealed class PrintingLanguageIndex
    {
        private const string AutomaticPrefix = "auto|";
        private const string SingletonPrefix = "single|";
        private readonly IReadOnlyDictionary<string, PrintingDefinition> printingsById;
        private readonly Dictionary<string, PrintingLanguageGroup> multiLanguageGroups;
        private readonly Dictionary<string, PrintingLanguageGroup> singletonGroups =
            new Dictionary<string, PrintingLanguageGroup>(StringComparer.Ordinal);
        private readonly IReadOnlyDictionary<string, PrintingLanguageGroup> groupsByPrinting;
        private readonly object singletonGate = new object();
        private IReadOnlyDictionary<string, PrintingLanguageGroup> allGroups;

        internal PrintingLanguageIndex(
            IEnumerable<PrintingDefinition> printings,
            IEnumerable<PrintingLanguageGroupDefinition> definitions)
        {
            PrintingDefinition[] source = (printings ?? Array.Empty<PrintingDefinition>())
                .OrderBy(value => value.Id, StringComparer.Ordinal)
                .ToArray();
            var byId = source.ToDictionary(value => value.Id, StringComparer.Ordinal);
            var result = new Dictionary<string, PrintingLanguageGroup>(StringComparer.Ordinal);
            var byPrinting = new Dictionary<string, PrintingLanguageGroup>(StringComparer.Ordinal);

            foreach (PrintingLanguageGroupDefinition definition in definitions ??
                     Array.Empty<PrintingLanguageGroupDefinition>())
            {
                PrintingDefinition[] members = definition.PrintingIds.Select(id => byId[id]).ToArray();
                var group = new PrintingLanguageGroup(
                    definition.Id,
                    members,
                    definition.MatchMethod,
                    definition.Confidence,
                    definition.ReviewStatus);
                result.Add(group.Id, group);
                foreach (PrintingDefinition member in members)
                    byPrinting.Add(member.Id, group);
            }

            foreach (IGrouping<string, PrintingDefinition> candidate in source
                         .Where(value => !byPrinting.ContainsKey(value.Id))
                         .GroupBy(AutomaticKey, StringComparer.Ordinal)
                         .OrderBy(value => value.Key, StringComparer.Ordinal))
            {
                PrintingDefinition[] members = candidate.ToArray();
                bool onePerLanguage = members
                    .GroupBy(value => value.Identity.LanguageId, StringComparer.OrdinalIgnoreCase)
                    .All(value => value.Count() == 1);
                if (members.Length > 1 && onePerLanguage)
                {
                    AddAutomaticGroup(candidate.Key, members, result, byPrinting);
                }
            }

            printingsById = new ReadOnlyDictionary<string, PrintingDefinition>(byId);
            multiLanguageGroups = result;
            groupsByPrinting = new ReadOnlyDictionary<string, PrintingLanguageGroup>(byPrinting);
        }

        public IReadOnlyDictionary<string, PrintingLanguageGroup> Groups
        {
            get
            {
                lock (singletonGate)
                {
                    if (allGroups != null)
                        return allGroups;
                    foreach (PrintingDefinition printing in printingsById.Values)
                        if (!groupsByPrinting.ContainsKey(printing.Id))
                            GetOrCreateSingleton(printing);
                    var complete = new Dictionary<string, PrintingLanguageGroup>(multiLanguageGroups,
                        StringComparer.Ordinal);
                    foreach (PrintingLanguageGroup singleton in singletonGroups.Values)
                        complete.Add(singleton.Id, singleton);
                    allGroups = new ReadOnlyDictionary<string, PrintingLanguageGroup>(complete);
                    return allGroups;
                }
            }
        }

        public PrintingLanguageGroup GetGroup(string printingId)
        {
            if (string.IsNullOrWhiteSpace(printingId))
                return null;
            if (groupsByPrinting.TryGetValue(printingId, out PrintingLanguageGroup group))
                return group;
            if (!printingsById.TryGetValue(printingId, out PrintingDefinition printing))
                return null;
            lock (singletonGate)
                return GetOrCreateSingleton(printing);
        }

        public PrintingDefinition Select(
            string printingId,
            string cardLanguageId,
            string fallbackCardLanguageId = null)
        {
            PrintingLanguageGroup group = GetGroup(printingId);
            if (group == null)
                return null;

            PrintingDefinition selected = group.Find(cardLanguageId) ?? group.Find(fallbackCardLanguageId);
            if (selected != null)
                return selected;

            selected = group.Printings.FirstOrDefault(value => string.Equals(value.Id, printingId, StringComparison.Ordinal));
            return selected ?? group.Printings[0];
        }

        private static void AddAutomaticGroup(
            string key,
            IEnumerable<PrintingDefinition> members,
            IDictionary<string, PrintingLanguageGroup> groups,
            IDictionary<string, PrintingLanguageGroup> groupsByPrinting)
        {
            string id = UniqueId(AutomaticPrefix + key, groups);
            var group = new PrintingLanguageGroup(
                id,
                members,
                PrintingLanguageMatchMethod.SharedItemIdentity,
                1d,
                PrintingLanguageReviewStatus.AutoAccepted);
            groups.Add(id, group);
            foreach (PrintingDefinition member in group.Printings)
                groupsByPrinting.Add(member.Id, group);
        }

        private PrintingLanguageGroup GetOrCreateSingleton(PrintingDefinition printing)
        {
            if (singletonGroups.TryGetValue(printing.Id, out PrintingLanguageGroup existing))
                return existing;
            string id = UniqueId(SingletonPrefix + printing.Id, multiLanguageGroups);
            var group = new PrintingLanguageGroup(
                id,
                new[] { printing },
                PrintingLanguageMatchMethod.SharedItemIdentity,
                1d,
                PrintingLanguageReviewStatus.AutoAccepted);
            singletonGroups.Add(printing.Id, group);
            allGroups = null;
            return group;
        }

        private static string AutomaticKey(PrintingDefinition printing) =>
            printing.ItemId + "|" + printing.Identity.VariantId;

        private static string UniqueId(string preferred, IDictionary<string, PrintingLanguageGroup> groups)
        {
            string candidate = preferred;
            int suffix = 2;
            while (groups.ContainsKey(candidate))
                candidate = preferred + "|" + suffix++;
            return candidate;
        }
    }
}
