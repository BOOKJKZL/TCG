using System;
using System.Collections.Generic;
using Gacha.Domain;

namespace Gacha.Application
{
    public interface ICatalogProvider
    {
        CatalogLoadResult Load();
    }

    public sealed class CatalogLoadResult
    {
        private CatalogLoadResult(
            UniversalCatalog catalog,
            int sourceSetCount,
            int sourceItemCount,
            int printingCount,
            IReadOnlyList<string> warnings,
            string errorMessage)
        {
            Catalog = catalog;
            SourceSetCount = sourceSetCount;
            SourceItemCount = sourceItemCount;
            PrintingCount = printingCount;
            Warnings = warnings ?? Array.Empty<string>();
            ErrorMessage = errorMessage;
        }

        public bool Succeeded => Catalog != null && string.IsNullOrEmpty(ErrorMessage);
        public UniversalCatalog Catalog { get; }
        public int SourceSetCount { get; }
        public int SourceItemCount { get; }
        public int PrintingCount { get; }
        public IReadOnlyList<string> Warnings { get; }
        public string ErrorMessage { get; }

        public static CatalogLoadResult Success(
            UniversalCatalog catalog,
            int sourceSetCount,
            int sourceItemCount,
            int printingCount,
            IReadOnlyList<string> warnings = null)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));

            return new CatalogLoadResult(
                catalog,
                sourceSetCount,
                sourceItemCount,
                printingCount,
                warnings,
                null);
        }

        public static CatalogLoadResult Failure(string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(errorMessage))
                throw new ArgumentException("A catalog failure needs an error message.", nameof(errorMessage));

            return new CatalogLoadResult(null, 0, 0, 0, Array.Empty<string>(), errorMessage.Trim());
        }
    }

    public sealed class CatalogSession
    {
        private readonly ICatalogProvider provider;
        private CatalogLoadResult current;

        public CatalogSession(ICatalogProvider provider)
        {
            this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        public bool IsReady => current != null && current.Succeeded;
        public UniversalCatalog Catalog => IsReady ? current.Catalog : null;
        public CatalogLoadResult Current => current;

        public event Action<CatalogLoadResult> Changed;

        public CatalogLoadResult EnsureLoaded(bool forceReload = false)
        {
            if (!forceReload && IsReady)
                return current;

            try
            {
                current = provider.Load();
                if (current == null)
                    current = CatalogLoadResult.Failure("The catalog provider returned no result.");
            }
            catch (Exception exception)
            {
                current = CatalogLoadResult.Failure(exception.Message);
            }

            Changed?.Invoke(current);
            return current;
        }
    }

    public static class ApplicationServices
    {
        public static bool IsConfigured => Catalog != null && Languages != null;
        public static bool HasContentImages => Images != null;
        public static CatalogSession Catalog { get; private set; }
        public static LanguageSelectionService Languages { get; private set; }
        public static IContentImageSource Images { get; private set; }
        public static IProductRuleProvider ProductRules { get; private set; }
        public static ExperienceSettingsService ExperienceSettings { get; private set; }
        public static ContentPackagePlanner ContentPackages { get; private set; }
        public static IContentPackageInstaller ContentPackageInstaller { get; private set; }
        public static IContentPackageInstallCoordinatorFactory ContentPackageOperations { get; private set; }
        public static IContentPackageCatalogProvider ContentPackageCatalogs { get; private set; }
        public static IContentPackageLifecycleService ContentPackageLifecycle { get; private set; }
        public static ContentDownloadPolicyService ContentDownloadPolicy { get; private set; }
        public static IContentPackageQueueStateStore ContentPackageQueueState { get; private set; }

        public static void Configure(
            CatalogSession catalog,
            LanguageSelectionService languages,
            IContentImageSource images = null,
            IProductRuleProvider productRules = null,
            ExperienceSettingsService experienceSettings = null,
            ContentPackagePlanner contentPackages = null,
            IContentPackageInstaller contentPackageInstaller = null,
            IContentPackageInstallCoordinatorFactory contentPackageOperations = null,
            IContentPackageCatalogProvider contentPackageCatalogs = null,
            IContentPackageLifecycleService contentPackageLifecycle = null,
            ContentDownloadPolicyService contentDownloadPolicy = null,
            IContentPackageQueueStateStore contentPackageQueueState = null)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            if (languages == null)
                throw new ArgumentNullException(nameof(languages));
            if (!ReferenceEquals(ContentPackageOperations, contentPackageOperations) &&
                ContentPackageOperations is IDisposable previousOperations)
                previousOperations.Dispose();
            if (!ReferenceEquals(ContentPackageCatalogs, contentPackageCatalogs) &&
                ContentPackageCatalogs is IDisposable previousCatalogs)
                previousCatalogs.Dispose();
            Catalog = catalog;
            Languages = languages;
            Images = images;
            ProductRules = productRules;
            ExperienceSettings = experienceSettings;
            ContentPackages = contentPackages;
            ContentPackageInstaller = contentPackageInstaller;
            ContentPackageOperations = contentPackageOperations;
            ContentPackageCatalogs = contentPackageCatalogs;
            ContentPackageLifecycle = contentPackageLifecycle;
            ContentDownloadPolicy = contentDownloadPolicy;
            ContentPackageQueueState = contentPackageQueueState;
        }

        public static void Reset()
        {
            if (ContentPackageOperations is IDisposable operations)
                operations.Dispose();
            if (ContentPackageCatalogs is IDisposable catalogs)
                catalogs.Dispose();
            Catalog = null;
            Languages = null;
            Images = null;
            ProductRules = null;
            ExperienceSettings = null;
            ContentPackages = null;
            ContentPackageInstaller = null;
            ContentPackageOperations = null;
            ContentPackageCatalogs = null;
            ContentPackageLifecycle = null;
            ContentDownloadPolicy = null;
            ContentPackageQueueState = null;
        }
    }
}
