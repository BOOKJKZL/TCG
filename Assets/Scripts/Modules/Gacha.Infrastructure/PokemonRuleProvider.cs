using Gacha.Application;
using Gacha.Domain;

namespace Gacha.Infrastructure.Rules
{
    public sealed class PokemonRuleProvider : IProductRuleProvider
    {
        private readonly IProductRuleProvider historical = new PokemonHistoricalRuleProvider();
        private readonly IProductRuleProvider modern = new PokemonModernRuleProvider();

        public ProductRuleProfile GetProfile(
            UniversalCatalog catalog,
            string productId,
            string languageId = null)
        {
            return historical.GetProfile(catalog, productId, languageId) ??
                   modern.GetProfile(catalog, productId, languageId);
        }
    }
}
