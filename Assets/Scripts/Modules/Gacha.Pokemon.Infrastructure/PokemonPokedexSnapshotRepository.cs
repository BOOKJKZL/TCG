using System;
using Gacha.Pokemon.Domain;

namespace Gacha.Pokemon.Infrastructure
{
    public sealed class PokemonPokedexSnapshotBundle
    {
        public PokemonPokedexSnapshotBundle(
            PokemonTaxonomySnapshotLoadResult taxonomy,
            PokemonCardSubjectSnapshotLoadResult cardSubjects)
        {
            Taxonomy = taxonomy ?? throw new ArgumentNullException(nameof(taxonomy));
            CardSubjects = cardSubjects ?? throw new ArgumentNullException(nameof(cardSubjects));
            if (!string.Equals(
                    Taxonomy.SourceSha256,
                    CardSubjects.TaxonomySourceSha256,
                    StringComparison.Ordinal))
                throw new PokemonTaxonomySnapshotException(
                    "Pokédex taxonomy and card links were built from different source snapshots.");
        }

        public PokemonTaxonomySnapshotLoadResult Taxonomy { get; }
        public PokemonCardSubjectSnapshotLoadResult CardSubjects { get; }
        public PokemonTaxonomyCatalog Catalog => Taxonomy.Catalog;
        public PokemonCardSubjectCatalog SubjectCatalog => CardSubjects.Catalog;
    }

    public sealed class PokemonPokedexSnapshotRepository
    {
        public PokemonPokedexSnapshotBundle Load(string taxonomyPath, string cardSubjectPath)
        {
            PokemonTaxonomySnapshotLoadResult taxonomy =
                new PokemonTaxonomySnapshotReader().LoadFile(taxonomyPath);
            PokemonCardSubjectSnapshotLoadResult cards =
                new PokemonCardSubjectSnapshotReader().LoadFile(cardSubjectPath, taxonomy.Catalog);
            return new PokemonPokedexSnapshotBundle(taxonomy, cards);
        }
    }
}
