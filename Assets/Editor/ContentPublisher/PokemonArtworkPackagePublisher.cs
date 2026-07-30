using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gacha.Pokemon.Infrastructure;

namespace Gacha.EditorTools.Content
{
    public static class PokemonArtworkPackagePublisher
    {
        public static ContentPackagePublishResult PublishAll(
            string artworkRoot,
            string outputRoot,
            long revision = 1,
            string version = "1.0.0")
        {
            if (string.IsNullOrWhiteSpace(artworkRoot) || !Directory.Exists(artworkRoot))
                throw new DirectoryNotFoundException("Pokédex artwork root was not found: " + artworkRoot);
            var definitions = new List<ContentPackagePublishDefinition>();
            foreach (string directory in Directory.GetDirectories(artworkRoot, "generation-*", SearchOption.TopDirectoryOnly)
                         .OrderBy(value => GenerationOrder(Path.GetFileName(value))))
            {
                string generationId = Path.GetFileName(directory);
                PokemonArtworkCatalog manifest = new PokemonArtworkManifestReader().LoadFile(
                    Path.Combine(directory, "manifest.json"));
                if (!string.Equals(manifest.GenerationId, generationId, StringComparison.Ordinal))
                    throw new InvalidDataException("Artwork directory and manifest generation identities differ.");
                definitions.Add(new ContentPackagePublishDefinition(
                    "pokemon.pokedex.artwork." + generationId,
                    directory,
                    "pokedex/artwork/" + generationId,
                    revision,
                    version));
            }
            if (definitions.Count != 9)
                throw new InvalidDataException($"Expected nine Pokédex artwork generations, found {definitions.Count}.");
            return new DeterministicContentPackagePublisher().Publish(
                new ContentPackagePublishRequest(outputRoot, revision, definitions));
        }

        private static int GenerationOrder(string id)
        {
            string suffix = (id ?? string.Empty).Replace("generation-", string.Empty);
            return int.TryParse(suffix, out int order) ? order : int.MaxValue;
        }
    }
}
