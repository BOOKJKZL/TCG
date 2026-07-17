using System;
using System.Collections.Generic;

namespace Gacha.Domain
{
    public sealed class LanguageDefinition : Definition
    {
        public LanguageDefinition(string id, IReadOnlyDictionary<string, string> names, string fallbackLanguageId = null)
            : base(id, names)
        {
            FallbackLanguageId = string.IsNullOrWhiteSpace(fallbackLanguageId) ? null : fallbackLanguageId.Trim();
        }

        public string FallbackLanguageId { get; }
    }

    public sealed class GameDefinition : Definition
    {
        public GameDefinition(string id, IReadOnlyDictionary<string, string> names, IEnumerable<string> supportedLanguageIds)
            : base(id, names)
        {
            SupportedLanguageIds = CopyStrings(supportedLanguageIds);
        }

        public IReadOnlyList<string> SupportedLanguageIds { get; }
    }

    public sealed class SetDefinition : Definition
    {
        public SetDefinition(
            string id,
            string gameId,
            IReadOnlyDictionary<string, string> names,
            string seriesId = null,
            DateTime? releaseDate = null)
            : base(id, names)
        {
            GameId = Required(gameId, nameof(gameId));
            SeriesId = string.IsNullOrWhiteSpace(seriesId) ? null : seriesId.Trim();
            ReleaseDate = releaseDate;
        }

        public string GameId { get; }
        public string SeriesId { get; }
        public DateTime? ReleaseDate { get; }
    }

    public sealed class CollectibleItemDefinition : Definition
    {
        public CollectibleItemDefinition(
            string id,
            string gameId,
            IReadOnlyDictionary<string, string> names,
            string category,
            IEnumerable<string> tags = null)
            : base(id, names)
        {
            GameId = Required(gameId, nameof(gameId));
            Category = Required(category, nameof(category));
            Tags = CopyStrings(tags);
        }

        public string GameId { get; }
        public string Category { get; }
        public IReadOnlyList<string> Tags { get; }
    }

    public sealed class RarityDefinition : Definition
    {
        public RarityDefinition(
            string id,
            string gameId,
            IReadOnlyDictionary<string, string> names,
            int displayRank,
            string presentationKey = null)
            : base(id, names)
        {
            GameId = Required(gameId, nameof(gameId));
            DisplayRank = displayRank;
            PresentationKey = string.IsNullOrWhiteSpace(presentationKey) ? null : presentationKey.Trim();
        }

        public string GameId { get; }
        public int DisplayRank { get; }
        public string PresentationKey { get; }
    }

    public sealed class VariantDefinition : Definition
    {
        public VariantDefinition(
            string id,
            string gameId,
            IReadOnlyDictionary<string, string> names,
            IEnumerable<string> traits = null)
            : base(id, names)
        {
            GameId = Required(gameId, nameof(gameId));
            Traits = CopyStrings(traits);
        }

        public string GameId { get; }
        public IReadOnlyList<string> Traits { get; }
    }

    public sealed class ProductDefinition : Definition
    {
        public ProductDefinition(
            string id,
            string gameId,
            string setId,
            IReadOnlyDictionary<string, string> names,
            string productType,
            IEnumerable<string> eligiblePrintingIds = null)
            : base(id, names)
        {
            GameId = Required(gameId, nameof(gameId));
            SetId = Required(setId, nameof(setId));
            ProductType = Required(productType, nameof(productType));
            EligiblePrintingIds = CopyStrings(eligiblePrintingIds);
        }

        public string GameId { get; }
        public string SetId { get; }
        public string ProductType { get; }
        public IReadOnlyList<string> EligiblePrintingIds { get; }
    }
}
