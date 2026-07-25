using System;
using Gacha.Infrastructure.Content;

namespace Gacha.Infrastructure.Rules
{
    public sealed class PokemonImportedCardVariantPolicy : IImportedCardVariantPolicy
    {
        private const string ScarletVioletManifestSetId = "sv01";

        public ImportedCardVariantsDto Resolve(string setId, ImportedCardDto card)
        {
            if (card == null)
                throw new ArgumentNullException(nameof(card));

            ImportedCardVariantsDto source = card.Variants ?? new ImportedCardVariantsDto();
            var resolved = new ImportedCardVariantsDto
            {
                Normal = source.Normal,
                Reverse = source.Reverse,
                Holo = source.Holo,
                FirstEdition = source.FirstEdition,
                WPromo = source.WPromo
            };
            if (!string.Equals(setId, ScarletVioletManifestSetId, StringComparison.OrdinalIgnoreCase))
                return resolved;

            string rarity = card.Rarity?.Trim();
            if (string.Equals(rarity, "Common", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(rarity, "Uncommon", StringComparison.OrdinalIgnoreCase))
            {
                resolved.Normal = true;
                resolved.Reverse = true;
                resolved.Holo = false;
            }
            else if (string.Equals(rarity, "Rare", StringComparison.OrdinalIgnoreCase))
            {
                resolved.Normal = false;
                resolved.Reverse = true;
                resolved.Holo = true;
            }
            else if (string.Equals(rarity, "Double rare", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(rarity, "Ultra Rare", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(rarity, "Illustration rare", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(rarity, "Special illustration rare", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(rarity, "Hyper rare", StringComparison.OrdinalIgnoreCase))
            {
                resolved.Normal = false;
                resolved.Reverse = false;
                resolved.Holo = true;
            }

            return resolved;
        }
    }
}
