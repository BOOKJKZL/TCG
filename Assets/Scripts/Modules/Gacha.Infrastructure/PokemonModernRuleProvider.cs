using Gacha.Application;
using Gacha.Domain;

namespace Gacha.Infrastructure.Rules
{
    // Compatibility facade for sourced simulations. Definitions are data-driven.
    public sealed class PokemonModernRuleProvider : IProductRuleProvider
    {
        public const string SwordShieldSetId = "pokemon-tcg:set:swsh1";
        public const string SwordShieldProfileId = "pokemon-swsh1-sourced-simulation-v1";
        public const string OfficialBoosterSupportUrl =
            "https://support.pokemon.com/hc/en-us/articles/360000981613-What-can-I-expect-in-a-Pok%C3%A9mon-Trading-Card-Game-booster-pack";
        public const string EliteFourumPullRateUrl =
            "https://www.elitefourum.com/t/pull-rates-in-sun-moon-sword-shield-sets/25220";
        public const string CardCodexPullRateUrl =
            "https://cardcodex.com/pokemon/sword-shield/sword-shield-base/";
        public const string ScarletVioletSetId = "pokemon-tcg:set:sv01";
        public const string ScarletVioletProfileId = "pokemon-sv01-sourced-simulation-v1";
        public const string TcgPlayerScarletVioletPullRateUrl =
            "https://www.tcgplayer.com/content/article/Pok%C3%A9mon-TCG-Scarlet-Violet-Pull-Rates/a7702fce-dd64-4a58-beb1-0f871c853215/";

        private readonly DataDrivenProductRuleProvider provider =
            PokemonRuleDefinitionLoader.CreateProvider();

        public ProductRuleProfile GetProfile(
            UniversalCatalog catalog,
            string productId,
            string languageId = null)
        {
            ProductRuleProfile profile = provider.GetProfile(catalog, productId, languageId);
            return profile?.Trust == ProductRuleTrust.SourceInformedSimulation ? profile : null;
        }
    }
}
