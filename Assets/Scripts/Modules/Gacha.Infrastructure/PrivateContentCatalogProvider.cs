using System;
using System.IO;
using System.Linq;
using Gacha.Application;
using Gacha.Domain;

namespace Gacha.Infrastructure.Content
{
    public sealed class PrivateContentCatalogProvider : ICatalogProvider
    {
        private readonly string contentRoot;
        private readonly string gameId;
        private readonly string gameDisplayName;
        private readonly IImportedCardVariantPolicy variantPolicy;

        public PrivateContentCatalogProvider(
            string contentRoot,
            string gameId = "pokemon-tcg",
            string gameDisplayName = "Pokémon Trading Card Game",
            IImportedCardVariantPolicy variantPolicy = null)
        {
            if (string.IsNullOrWhiteSpace(contentRoot))
                throw new ArgumentException("Content root cannot be empty.", nameof(contentRoot));

            this.contentRoot = contentRoot;
            this.gameId = gameId;
            this.gameDisplayName = gameDisplayName;
            this.variantPolicy = variantPolicy;
        }

        public CatalogLoadResult Load()
        {
            try
            {
                return LoadCore();
            }
            catch (PrivateContentManifestException exception)
            {
                CatalogFailureReason reason = exception.InnerException is IOException ||
                                              exception.InnerException is UnauthorizedAccessException
                    ? CatalogFailureReason.ServiceUnavailable
                    : CatalogFailureReason.CatalogCorrupt;
                return CatalogLoadResult.Failure(
                    exception.Message,
                    reason);
            }
        }

        private CatalogLoadResult LoadCore()
        {
            var documents = new PrivateContentManifestReader().LoadCardSetDirectory(contentRoot);
            if (documents.Count == 0)
            {
                var empty = new UniversalCatalog(
                    Enumerable.Empty<LanguageDefinition>(),
                    Enumerable.Empty<GameDefinition>(),
                    Enumerable.Empty<SetDefinition>(),
                    Enumerable.Empty<CollectibleItemDefinition>(),
                    Enumerable.Empty<RarityDefinition>(),
                    Enumerable.Empty<VariantDefinition>(),
                    Enumerable.Empty<PrintingDefinition>(),
                    Enumerable.Empty<ProductDefinition>());
                return CatalogLoadResult.Success(empty, 0, 0, 0);
            }
            PrintingLanguageGroupManifestDto languageGroups =
                new PrintingLanguageGroupManifestReader().LoadOptional(contentRoot);
            PrivateCatalogImportResult import = new PrivateManifestCatalogAdapter(variantPolicy).Build(
                documents,
                gameId,
                gameDisplayName,
                languageGroups);

            return CatalogLoadResult.Success(
                import.Catalog,
                import.SourceSetCount,
                import.SourceCardCount,
                import.PrintingCount,
                import.Warnings);
        }
    }
}
