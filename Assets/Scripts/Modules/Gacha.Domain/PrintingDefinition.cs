using System;
using System.Collections.Generic;

namespace Gacha.Domain
{
    public readonly struct PrintingIdentity : IEquatable<PrintingIdentity>
    {
        public PrintingIdentity(string gameId, string setId, string cardNumber, string languageId, string variantId)
        {
            GameId = Definition.Required(gameId, nameof(gameId));
            SetId = Definition.Required(setId, nameof(setId));
            CardNumber = Definition.Required(cardNumber, nameof(cardNumber));
            LanguageId = Definition.Required(languageId, nameof(languageId));
            VariantId = Definition.Required(variantId, nameof(variantId));
        }

        public string GameId { get; }
        public string SetId { get; }
        public string CardNumber { get; }
        public string LanguageId { get; }
        public string VariantId { get; }

        public bool Equals(PrintingIdentity other)
        {
            return string.Equals(GameId, other.GameId, StringComparison.Ordinal) &&
                   string.Equals(SetId, other.SetId, StringComparison.Ordinal) &&
                   string.Equals(CardNumber, other.CardNumber, StringComparison.Ordinal) &&
                   string.Equals(LanguageId, other.LanguageId, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(VariantId, other.VariantId, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) => obj is PrintingIdentity other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (GameId == null ? 0 : StringComparer.Ordinal.GetHashCode(GameId));
                hash = hash * 31 + (SetId == null ? 0 : StringComparer.Ordinal.GetHashCode(SetId));
                hash = hash * 31 + (CardNumber == null ? 0 : StringComparer.Ordinal.GetHashCode(CardNumber));
                hash = hash * 31 + (LanguageId == null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(LanguageId));
                hash = hash * 31 + (VariantId == null ? 0 : StringComparer.Ordinal.GetHashCode(VariantId));
                return hash;
            }
        }

        public override string ToString() => $"{GameId}:{SetId}:{CardNumber}:{LanguageId}:{VariantId}";
    }

    public sealed class PrintingDefinition : Definition
    {
        public PrintingDefinition(
            string id,
            string itemId,
            PrintingIdentity identity,
            string rarityId,
            IReadOnlyDictionary<string, string> names,
            string imageRelativePath = null,
            string imageSha256 = null)
            : base(id, names)
        {
            ItemId = Required(itemId, nameof(itemId));
            Required(identity.GameId, nameof(identity.GameId));
            Required(identity.SetId, nameof(identity.SetId));
            Required(identity.CardNumber, nameof(identity.CardNumber));
            Required(identity.LanguageId, nameof(identity.LanguageId));
            Required(identity.VariantId, nameof(identity.VariantId));
            Identity = identity;
            RarityId = Required(rarityId, nameof(rarityId));
            ImageRelativePath = string.IsNullOrWhiteSpace(imageRelativePath) ? null : imageRelativePath.Trim();
            ImageSha256 = string.IsNullOrWhiteSpace(imageSha256) ? null : imageSha256.Trim().ToLowerInvariant();
        }

        public string ItemId { get; }
        public PrintingIdentity Identity { get; }
        public string RarityId { get; }
        public string ImageRelativePath { get; }
        public string ImageSha256 { get; }
    }
}
