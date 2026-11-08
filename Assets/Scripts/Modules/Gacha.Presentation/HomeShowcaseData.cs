using System;
using System.Collections.Generic;
using System.Linq;
using Gacha.Domain;

namespace Gacha.Presentation
{
    public sealed class HomePackPreview
    {
        public HomePackPreview(ProductDefinition product, PrintingDefinition cover)
        {
            Product = product ?? throw new ArgumentNullException(nameof(product));
            Cover = cover;
        }

        public ProductDefinition Product { get; }
        public PrintingDefinition Cover { get; }
    }

    public sealed class HomeShowcaseData
    {
        public HomeShowcaseData(
            IReadOnlyList<HomePackPreview> packs,
            IReadOnlyList<PrintingDefinition> ownedCards,
            string contentLanguageId)
        {
            Packs = Array.AsReadOnly((packs ?? throw new ArgumentNullException(nameof(packs))).ToArray());
            OwnedCards = Array.AsReadOnly((ownedCards ?? throw new ArgumentNullException(nameof(ownedCards)))
                .Take(3).ToArray());
            ContentLanguageId = contentLanguageId ?? string.Empty;
        }

        public IReadOnlyList<HomePackPreview> Packs { get; }
        public IReadOnlyList<PrintingDefinition> OwnedCards { get; }
        public string ContentLanguageId { get; }

        public static HomeShowcaseData FromCatalog(
            UniversalCatalog catalog,
            IReadOnlyDictionary<string, int> ownedCounts,
            string contentLanguageId)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            if (ownedCounts == null)
                throw new ArgumentNullException(nameof(ownedCounts));

            var setOrder = new SetDefinitionComparer(SetSortMode.Generation, contentLanguageId);
            bool MatchesLanguage(PrintingDefinition printing) => string.Equals(
                printing.Identity.LanguageId, contentLanguageId, StringComparison.OrdinalIgnoreCase);

            HomePackPreview[] packs = catalog.Products.Values
                .OrderBy(product => catalog.Sets[product.SetId], setOrder)
                .ThenBy(product => product.Id, StringComparer.Ordinal)
                .Select(product => new
                {
                    Product = product,
                    Printings = product.EligiblePrintingIds.Select(id => catalog.Printings[id])
                        .Where(MatchesLanguage).ToArray()
                })
                .Where(item => item.Printings.Length > 0)
                .Select(item => new HomePackPreview(item.Product,
                    item.Printings.FirstOrDefault(printing => !string.IsNullOrWhiteSpace(printing.ImageRelativePath)) ??
                    item.Printings[0]))
                .ToArray();
            PrintingDefinition[] ownedCards = catalog.Printings.Values
                .Where(MatchesLanguage)
                .Where(printing => ownedCounts.TryGetValue(printing.Id, out int count) && count > 0)
                .OrderBy(printing => catalog.Sets[printing.Identity.SetId], setOrder)
                .ThenBy(printing => printing.Id, StringComparer.Ordinal)
                .Take(3)
                .ToArray();
            return new HomeShowcaseData(packs, ownedCards, contentLanguageId);
        }
    }
}
