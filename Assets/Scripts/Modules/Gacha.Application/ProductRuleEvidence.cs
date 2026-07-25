using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Gacha.Domain;

namespace Gacha.Application
{
    public enum ProductRuleTrust
    {
        Simulated,
        SourceInformedSimulation,
        HistoricallyVerified
    }

    public enum ProductRuleConfidence
    {
        Unverified,
        Corroborated,
        Authoritative
    }

    public sealed class ProductRuleEvidence
    {
        public ProductRuleEvidence(string title, string sourceReference, DateTime checkedOn)
        {
            if (string.IsNullOrWhiteSpace(title))
                throw new ArgumentException("Rule evidence needs a title.", nameof(title));
            if (!Uri.TryCreate(sourceReference, UriKind.Absolute, out Uri sourceUri) ||
                !string.Equals(sourceUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Rule evidence needs an absolute HTTPS source.", nameof(sourceReference));
            }
            if (checkedOn == default)
                throw new ArgumentException("Rule evidence needs a check date.", nameof(checkedOn));

            Title = title.Trim();
            SourceReference = sourceUri.AbsoluteUri;
            CheckedOn = checkedOn.Date;
        }

        public string Title { get; }
        public string SourceReference { get; }
        public DateTime CheckedOn { get; }
    }

    public sealed class ProductRuleProfile
    {
        public ProductRuleProfile(
            string id,
            ProductDrawRules rules,
            ProductRuleTrust trust,
            ProductRuleConfidence confidence,
            string regionId,
            IReadOnlyDictionary<string, string> localizedRegionNames,
            IEnumerable<ProductRuleEvidence> evidence,
            IReadOnlyDictionary<string, string> localizedDescriptions)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("A rule profile needs an id.", nameof(id));
            if (!Enum.IsDefined(typeof(ProductRuleTrust), trust))
                throw new ArgumentOutOfRangeException(nameof(trust));
            if (!Enum.IsDefined(typeof(ProductRuleConfidence), confidence))
                throw new ArgumentOutOfRangeException(nameof(confidence));
            if (string.IsNullOrWhiteSpace(regionId))
                throw new ArgumentException("A rule profile needs an explicit region id.", nameof(regionId));

            ProductRuleEvidence[] evidenceCopy = (evidence ?? Array.Empty<ProductRuleEvidence>())
                .Where(item => item != null)
                .GroupBy(item => item.SourceReference, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(item => item.CheckedOn).First())
                .ToArray();
            bool isUnverifiedSimulation = trust == ProductRuleTrust.Simulated;
            if (isUnverifiedSimulation && confidence != ProductRuleConfidence.Unverified)
            {
                throw new ArgumentException(
                    "An unverified simulation cannot claim corroborated or authoritative confidence.",
                    nameof(confidence));
            }
            if (!isUnverifiedSimulation &&
                (confidence == ProductRuleConfidence.Unverified || evidenceCopy.Length == 0))
            {
                throw new ArgumentException(
                    "Source-informed and historically verified rules need evidence and a stated confidence.",
                    nameof(evidence));
            }

            Id = id.Trim();
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            Trust = trust;
            Confidence = confidence;
            RegionId = regionId.Trim();
            LocalizedRegionNames = CopyLocalizedValues(localizedRegionNames, RegionId);
            Evidence = new ReadOnlyCollection<ProductRuleEvidence>(evidenceCopy);
            SourceReferences = new ReadOnlyCollection<string>(evidenceCopy
                .Select(item => item.SourceReference)
                .ToArray());
            LocalizedDescriptions = CopyLocalizedValues(localizedDescriptions, Id);
        }

        public string Id { get; }
        public ProductDrawRules Rules { get; }
        public ProductRuleTrust Trust { get; }
        public ProductRuleConfidence Confidence { get; }
        public string RegionId { get; }
        public IReadOnlyDictionary<string, string> LocalizedRegionNames { get; }
        public IReadOnlyList<ProductRuleEvidence> Evidence { get; }
        public IReadOnlyList<string> SourceReferences { get; }
        public IReadOnlyDictionary<string, string> LocalizedDescriptions { get; }
        public string SourceReference => SourceReferences.FirstOrDefault();
        public DateTime? LastCheckedOn => Evidence.Count == 0
            ? (DateTime?)null
            : Evidence.Max(item => item.CheckedOn);
        public bool IsHistoricallyVerified => Trust == ProductRuleTrust.HistoricallyVerified;
        public bool IsSimulation => !IsHistoricallyVerified;

        public string GetDescription(string languageId, string fallbackLanguageId = "en")
        {
            return GetLocalizedValue(LocalizedDescriptions, languageId, fallbackLanguageId);
        }

        public string GetRegionName(string languageId, string fallbackLanguageId = "en")
        {
            return GetLocalizedValue(LocalizedRegionNames, languageId, fallbackLanguageId);
        }

        private static IReadOnlyDictionary<string, string> CopyLocalizedValues(
            IReadOnlyDictionary<string, string> source,
            string fallback)
        {
            var copy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (source != null)
            {
                foreach (KeyValuePair<string, string> entry in source)
                {
                    if (!string.IsNullOrWhiteSpace(entry.Key) && !string.IsNullOrWhiteSpace(entry.Value))
                        copy[entry.Key.Trim()] = entry.Value.Trim();
                }
            }

            if (copy.Count == 0)
                copy["en"] = fallback;
            return new ReadOnlyDictionary<string, string>(copy);
        }

        private static string GetLocalizedValue(
            IReadOnlyDictionary<string, string> values,
            string languageId,
            string fallbackLanguageId)
        {
            if (!string.IsNullOrWhiteSpace(languageId) &&
                values.TryGetValue(languageId, out string localized))
            {
                return localized;
            }

            if (!string.IsNullOrWhiteSpace(fallbackLanguageId) &&
                values.TryGetValue(fallbackLanguageId, out string fallback))
            {
                return fallback;
            }

            return values.Values.First();
        }
    }
}
