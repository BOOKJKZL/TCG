using Gacha.Domain;

namespace Gacha.Presentation
{
    public static class CardVariantDisplayName
    {
        public static string Resolve(UniversalCatalog catalog, string variantId, string languageId)
        {
            if (catalog != null && !string.IsNullOrWhiteSpace(variantId) &&
                catalog.Variants.TryGetValue(variantId, out VariantDefinition definition))
            {
                string name = definition.GetDisplayName(languageId);
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }
            // Unknown definitions remain intact in the catalog/save; their IDs are not player copy.
            return CardUiText.Get("card.variant.unknown");
        }
    }
}
