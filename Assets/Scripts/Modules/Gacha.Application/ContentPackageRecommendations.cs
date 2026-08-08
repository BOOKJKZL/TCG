using System;
using System.Collections.Generic;
using System.Linq;

namespace Gacha.Application
{
    public sealed class ContentPackageRecommendation
    {
        internal ContentPackageRecommendation(
            ContentPackageCatalogEntry entry,
            ContentPackageSelectionSummary selection,
            bool explicitlyRecommended)
        {
            Entry = entry;
            Selection = selection;
            ExplicitlyRecommended = explicitlyRecommended;
        }

        public ContentPackageCatalogEntry Entry { get; }
        public ContentPackageSelectionSummary Selection { get; }
        public bool ExplicitlyRecommended { get; }
    }

    public static class ContentPackageRecommendations
    {
        private const string CardSetKind = "card-set";
        private const string RecommendedTag = "recommended";
        private const string StarterTag = "starter";

        public static ContentPackageRecommendation FindSmallestPlayable(
            ContentPackageCatalog catalog,
            string contentLanguageId,
            Func<string, InstalledContentPackage> installedLookup = null)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            string language = NormalizeLanguage(contentLanguageId);
            if (language == null)
                return null;
            installedLookup ??= _ => null;

            return catalog.Packages
                .Where(entry => MatchesLanguage(entry.Metadata.ContentLanguageId, language))
                .Where(entry => IsPlayableCandidate(entry.Metadata))
                .Where(entry => !ContentPackageLibrary.IsCurrent(
                    installedLookup(entry.Package.PackageId),
                    entry.Package))
                .Select(entry => Candidate(catalog, entry, installedLookup))
                .OrderBy(candidate => candidate.Selection.DownloadBytes)
                .ThenBy(candidate => candidate.Selection.InstalledBytes)
                .ThenBy(candidate => candidate.Selection.DependencyCount)
                .ThenBy(candidate => candidate.Rank)
                .ThenBy(candidate => candidate.Entry.Metadata.GenerationOrder ?? int.MaxValue)
                .ThenBy(candidate => candidate.Entry.Metadata.SortOrdinal ?? int.MaxValue)
                .ThenBy(candidate => candidate.Entry.Package.PackageId, StringComparer.Ordinal)
                .Select(candidate => new ContentPackageRecommendation(
                    candidate.Entry,
                    candidate.Selection,
                    candidate.Rank < 2))
                .FirstOrDefault();
        }

        private static CandidateItem Candidate(
            ContentPackageCatalog catalog,
            ContentPackageCatalogEntry entry,
            Func<string, InstalledContentPackage> installedLookup)
        {
            return new CandidateItem(
                entry,
                ContentPackageLibrary.SummarizeSelection(
                    catalog,
                    new[] { entry.Package.PackageId },
                    installedLookup),
                Rank(entry.Metadata));
        }

        private static bool IsPlayableCandidate(ContentPackageMetadata metadata) =>
            string.Equals(metadata.Kind, CardSetKind, StringComparison.Ordinal);

        private static int Rank(ContentPackageMetadata metadata)
        {
            if (metadata.Tags.Contains(RecommendedTag, StringComparer.OrdinalIgnoreCase))
                return 0;
            if (metadata.Tags.Contains(StarterTag, StringComparer.OrdinalIgnoreCase))
                return 1;
            return 2;
        }

        private static bool MatchesLanguage(string candidate, string requested) =>
            string.Equals(NormalizeLanguage(candidate), requested, StringComparison.Ordinal);

        private static string NormalizeLanguage(string value) =>
            string.IsNullOrWhiteSpace(value)
                ? null
                : value.Trim().Replace('_', '-').ToLowerInvariant();

        private sealed class CandidateItem
        {
            public CandidateItem(
                ContentPackageCatalogEntry entry,
                ContentPackageSelectionSummary selection,
                int rank)
            {
                Entry = entry;
                Selection = selection;
                Rank = rank;
            }

            public ContentPackageCatalogEntry Entry { get; }
            public ContentPackageSelectionSummary Selection { get; }
            public int Rank { get; }
        }
    }
}
