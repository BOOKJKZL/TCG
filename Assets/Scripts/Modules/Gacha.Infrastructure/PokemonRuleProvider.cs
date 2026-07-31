using Gacha.Application;
using Gacha.Domain;

namespace Gacha.Infrastructure.Rules
{
    public sealed class PokemonRuleProvider : IProductRuleProvider
    {
        private readonly DataDrivenProductRuleProvider provider =
            PokemonRuleDefinitionLoader.CreateProvider();

        public ProductRuleProfile GetProfile(
            UniversalCatalog catalog,
            string productId,
            string languageId = null)
        {
            return provider.GetProfile(catalog, productId, languageId);
        }
    }
}
