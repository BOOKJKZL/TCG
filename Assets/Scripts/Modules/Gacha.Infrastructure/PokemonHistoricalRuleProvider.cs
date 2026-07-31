using System;
using Gacha.Application;
using Gacha.Domain;

namespace Gacha.Infrastructure.Rules
{
    // Compatibility facade for callers that explicitly ask for historical profiles.
    // The rule details now live in the versioned JSON catalog, not in this class.
    public sealed class PokemonHistoricalRuleProvider : IProductRuleProvider
    {
        public const string BaseSetId = "pokemon-tcg:set:base1";
        public const string BaseSetProfileId = "pokemon-base1-unlimited-empirical-v1";
        public const string BaseSetStudyUrl = "https://www.cs.sjsu.edu/~stamp/cv/papers/pokemon.pdf";
        public const string MachampSourceUrl = "https://www.pokebeach.com/tcg/base-set/theme-decks";
        public const string ExRubySapphireSetId = "pokemon-tcg:set:ex1";
        public const string ExRubySapphireProfileId = "pokemon-ex1-psa-empirical-v1";
        public const string ExRubySapphireSourceUrl =
            "https://www.psacard.com/articles/articleview/9800/psa-set-registry-collecting-2003-poke-mon-ex-ruby-sapphire-first-nintendo-card-issue";
        public const string NeoGenesisSetId = "pokemon-tcg:set:neo1";
        public const string NeoGenesisProfileId = "pokemon-neo1-first-edition-psa-v1";
        public const string NeoGenesisSourceUrl = "https://www.psacard.com/articles/articleview/9409/public/locales";
        public const string InternationalRegionId = "pokemon-international-en";

        private readonly DataDrivenProductRuleProvider provider =
            PokemonRuleDefinitionLoader.CreateProvider();

        public ProductRuleProfile GetProfile(
            UniversalCatalog catalog,
            string productId,
            string languageId = null)
        {
            ProductRuleProfile profile = provider.GetProfile(catalog, productId, languageId);
            return profile?.Trust == ProductRuleTrust.HistoricallyVerified ? profile : null;
        }
    }
}
