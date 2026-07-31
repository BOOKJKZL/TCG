using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Gacha.Application
{
    public enum ContentPackageInstallFilter
    {
        All,
        Installed,
        NotInstalled,
        UpdateAvailable
    }

    public sealed class ContentPackageLibraryQuery
    {
        public ContentPackageLibraryQuery(
            string uiLanguageId = "en",
            string search = null,
            string contentLanguageId = null,
            int? generationOrder = null,
            string kind = null,
            ContentPackageInstallFilter installFilter = ContentPackageInstallFilter.All)
        {
            UiLanguageId = Normalize(uiLanguageId) ?? "en";
            Search = Optional(search);
            ContentLanguageId = Normalize(contentLanguageId);
            GenerationOrder = generationOrder;
            Kind = Optional(kind)?.ToLowerInvariant();
            InstallFilter = installFilter;
        }

        public string UiLanguageId { get; }
        public string Search { get; }
        public string ContentLanguageId { get; }
        public int? GenerationOrder { get; }
        public string Kind { get; }
        public ContentPackageInstallFilter InstallFilter { get; }

        private static string Normalize(string value) =>
            string.IsNullOrWhiteSpace(value)
                ? null
                : value.Trim().Replace('_', '-').ToLowerInvariant();

        private static string Optional(string value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public sealed class ContentPackageLibraryItem
    {
        internal ContentPackageLibraryItem(
            ContentPackageCatalogEntry entry,
            InstalledContentPackage installed,
            string displayName)
        {
            Entry = entry;
            Installed = installed;
            DisplayName = displayName;
        }

        public ContentPackageCatalogEntry Entry { get; }
        public ContentPackageDescriptor Package => Entry.Package;
        public ContentPackageMetadata Metadata => Entry.Metadata;
        public InstalledContentPackage Installed { get; }
        public string DisplayName { get; }
        public bool IsInstalled => Installed != null;
        public bool IsCurrent => ContentPackageLibrary.IsCurrent(Installed, Package);
        public bool HasUpdate => IsInstalled && !IsCurrent;
    }

    public sealed class ContentPackageLibrarySnapshot
    {
        internal ContentPackageLibrarySnapshot(
            IReadOnlyList<ContentPackageLibraryItem> items,
            int catalogCount)
        {
            Items = items;
            CatalogCount = catalogCount;
        }

        public IReadOnlyList<ContentPackageLibraryItem> Items { get; }
        public int CatalogCount { get; }
        public int FilteredCount => Items.Count;
    }

    public sealed class ContentPackageSelectionSummary
    {
        internal ContentPackageSelectionSummary(
            IReadOnlyList<string> packageIds,
            int selectedCount,
            int dependencyCount,
            long downloadBytes,
            long installedBytes)
        {
            PackageIds = packageIds;
            SelectedCount = selectedCount;
            DependencyCount = dependencyCount;
            DownloadBytes = downloadBytes;
            InstalledBytes = installedBytes;
        }

        public IReadOnlyList<string> PackageIds { get; }
        public int SelectedCount { get; }
        public int DependencyCount { get; }
        public long DownloadBytes { get; }
        public long InstalledBytes { get; }
    }

    public static class ContentPackageLibrary
    {
        public static ContentPackageLibrarySnapshot Project(
            ContentPackageCatalog catalog,
            Func<string, InstalledContentPackage> installedLookup,
            ContentPackageLibraryQuery query = null)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            query ??= new ContentPackageLibraryQuery();
            installedLookup ??= _ => null;
            ContentPackageLibraryItem[] items = catalog.Packages
                .Select(entry => new ContentPackageLibraryItem(
                    entry,
                    installedLookup(entry.Package.PackageId),
                    entry.Metadata.GetDisplayName(
                        query.UiLanguageId,
                        entry.Package.PackageId)))
                .Where(item => Matches(item, query))
                .OrderBy(item => LanguageMissing(item.Metadata.ContentLanguageId))
                .ThenBy(item => item.Metadata.ContentLanguageId, StringComparer.Ordinal)
                .ThenBy(item => Missing(item.Metadata.GenerationOrder))
                .ThenBy(item => item.Metadata.GenerationOrder)
                .ThenBy(item => Missing(item.Metadata.ReleaseDate))
                .ThenBy(item => item.Metadata.ReleaseDate)
                .ThenBy(item => Missing(item.Metadata.SortOrdinal))
                .ThenBy(item => item.Metadata.SortOrdinal)
                .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Package.PackageId, StringComparer.Ordinal)
                .ToArray();
            return new ContentPackageLibrarySnapshot(
                new ReadOnlyCollection<ContentPackageLibraryItem>(items),
                catalog.Packages.Count);
        }

        public static ContentPackageSelectionSummary SummarizeSelection(
            ContentPackageCatalog catalog,
            IEnumerable<string> selectedPackageIds,
            Func<string, InstalledContentPackage> installedLookup = null)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            installedLookup ??= _ => null;
            string[] selected = (selectedPackageIds ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            var closure = new HashSet<string>(StringComparer.Ordinal);
            foreach (string packageId in selected)
                AddWithDependencies(catalog, packageId, closure);

            long downloadBytes = 0;
            long installedBytes = 0;
            foreach (string packageId in closure)
            {
                ContentPackageDescriptor package = catalog.Find(packageId).Package;
                if (IsCurrent(installedLookup(packageId), package))
                    continue;
                checked
                {
                    downloadBytes += package.DownloadBytes;
                    installedBytes += package.InstalledBytes;
                }
            }
            string[] packages = closure.OrderBy(value => value, StringComparer.Ordinal).ToArray();
            return new ContentPackageSelectionSummary(
                new ReadOnlyCollection<string>(packages),
                selected.Length,
                packages.Length - selected.Length,
                downloadBytes,
                installedBytes);
        }

        public static bool IsCurrent(
            InstalledContentPackage installed,
            ContentPackageDescriptor package) =>
            installed != null && package != null &&
            installed.Revision >= package.Revision &&
            string.Equals(
                installed.InstallRelativePath,
                package.InstallRelativePath,
                StringComparison.Ordinal) &&
            string.Equals(installed.Sha256, package.Sha256, StringComparison.OrdinalIgnoreCase);

        private static bool Matches(
            ContentPackageLibraryItem item,
            ContentPackageLibraryQuery query)
        {
            if (query.ContentLanguageId != null &&
                !string.Equals(
                    item.Metadata.ContentLanguageId,
                    query.ContentLanguageId,
                    StringComparison.OrdinalIgnoreCase))
                return false;
            if (query.GenerationOrder.HasValue &&
                item.Metadata.GenerationOrder != query.GenerationOrder)
                return false;
            if (query.Kind != null &&
                !string.Equals(item.Metadata.Kind, query.Kind, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!MatchesInstall(item, query.InstallFilter))
                return false;
            if (query.Search == null)
                return true;
            return Contains(item.DisplayName, query.Search) ||
                   Contains(item.Package.PackageId, query.Search) ||
                   Contains(item.Metadata.SetId, query.Search) ||
                   Contains(item.Metadata.SetCode, query.Search);
        }

        private static bool MatchesInstall(
            ContentPackageLibraryItem item,
            ContentPackageInstallFilter filter)
        {
            switch (filter)
            {
                case ContentPackageInstallFilter.All:
                    return true;
                case ContentPackageInstallFilter.Installed:
                    return item.IsInstalled;
                case ContentPackageInstallFilter.NotInstalled:
                    return !item.IsInstalled;
                case ContentPackageInstallFilter.UpdateAvailable:
                    return item.HasUpdate;
                default:
                    throw new ArgumentOutOfRangeException(nameof(filter), filter, null);
            }
        }

        private static bool Contains(string value, string search) =>
            value?.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;

        private static void AddWithDependencies(
            ContentPackageCatalog catalog,
            string packageId,
            ISet<string> closure)
        {
            ContentPackageCatalogEntry entry = catalog.Find(packageId) ??
                                               throw new ArgumentException(
                                                   "Selected package is not in the catalog: " + packageId,
                                                   nameof(packageId));
            if (!closure.Add(packageId))
                return;
            foreach (string dependency in entry.Metadata.Dependencies)
                AddWithDependencies(catalog, dependency, closure);
        }

        private static int Missing<T>(T? value) where T : struct => value.HasValue ? 0 : 1;
        private static int LanguageMissing(string value) => string.IsNullOrWhiteSpace(value) ? 1 : 0;
    }
}
