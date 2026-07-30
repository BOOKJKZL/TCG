using System;
using Gacha.Application;

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
            var documents = new PrivateContentManifestReader().LoadCardSetDirectory(contentRoot);
            PrivateCatalogImportResult import = new PrivateManifestCatalogAdapter(variantPolicy).Build(
                documents,
                gameId,
                gameDisplayName);

            return CatalogLoadResult.Success(
                import.Catalog,
                import.SourceSetCount,
                import.SourceCardCount,
                import.PrintingCount,
                import.Warnings);
        }
    }
}
