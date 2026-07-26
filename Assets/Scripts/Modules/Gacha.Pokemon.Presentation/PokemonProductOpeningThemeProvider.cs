using System;
using System.Collections.Generic;
using Gacha.Domain;
using Gacha.Presentation;

namespace Gacha.Pokemon.Presentation
{
    public sealed class PokemonProductOpeningThemeProvider : IProductOpeningThemeProvider
    {
        public const string BaseSetId = "pokemon-tcg:set:base1";
        public const string NeoGenesisSetId = "pokemon-tcg:set:neo1";
        public const string ExRubySapphireSetId = "pokemon-tcg:set:ex1";
        public const string SwordShieldSetId = "pokemon-tcg:set:swsh1";
        public const string ScarletVioletSetId = "pokemon-tcg:set:sv01";

        private static readonly string[] RareFragments = { "rare" };

        private static readonly IReadOnlyDictionary<string, ProductOpeningTheme> Themes =
            new Dictionary<string, ProductOpeningTheme>(StringComparer.Ordinal)
            {
                [BaseSetId] = Theme(
                    "pokemon-base1-vintage", "gacha-theme--vintage",
                    ProductOpeningThemeAudioKeys.VintagePackOpen,
                    ProductOpeningThemeAudioKeys.VintageRareReveal,
                    0.56f, 1.035f, 1.5f, 0.26f, 0.70f, 1.08f),
                [NeoGenesisSetId] = Theme(
                    "pokemon-neo1-forest", "gacha-theme--forest",
                    ProductOpeningThemeAudioKeys.ForestPackOpen,
                    ProductOpeningThemeAudioKeys.ForestRareReveal,
                    0.52f, 1.045f, 2f, 0.24f, 0.68f, 1.10f),
                [ExRubySapphireSetId] = Theme(
                    "pokemon-ex1-ruby", "gacha-theme--ruby",
                    ProductOpeningThemeAudioKeys.RubyPackOpen,
                    ProductOpeningThemeAudioKeys.RubyRareReveal,
                    0.43f, 1.065f, 3f, 0.20f, 0.66f, 1.13f),
                [SwordShieldSetId] = Theme(
                    "pokemon-swsh1-electric", "gacha-theme--electric",
                    ProductOpeningThemeAudioKeys.ElectricPackOpen,
                    ProductOpeningThemeAudioKeys.ElectricRareReveal,
                    0.36f, 1.08f, 4f, 0.18f, 0.62f, 1.15f),
                [ScarletVioletSetId] = Theme(
                    "pokemon-sv01-gallery", "gacha-theme--gallery",
                    ProductOpeningThemeAudioKeys.GalleryPackOpen,
                    ProductOpeningThemeAudioKeys.GalleryRareReveal,
                    0.40f, 1.06f, 3.5f, 0.23f, 0.64f, 1.18f)
            };

        public ProductOpeningTheme Resolve(ProductDefinition product)
        {
            if (product == null)
                throw new ArgumentNullException(nameof(product));
            return Themes.TryGetValue(product.SetId, out ProductOpeningTheme theme) ? theme : null;
        }

        private static ProductOpeningTheme Theme(
            string id,
            string styleClass,
            string packAudioKey,
            string rareAudioKey,
            float packDuration,
            float packPulseScale,
            float packPulseCycles,
            float revealDuration,
            float revealStartScale,
            float rarePulseScale)
        {
            return new ProductOpeningTheme(
                id,
                styleClass,
                packAudioKey,
                rareAudioKey,
                packDuration,
                packPulseScale,
                packPulseCycles,
                revealDuration,
                revealStartScale,
                rarePulseScale,
                RareFragments);
        }
    }
}
