using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;

namespace Gacha.EditorTools.Content
{
    /// <summary>
    /// Publishes the precomputed Card-to-Pokémon index as one independently
    /// downloadable package. The phone never needs to scan every Set manifest.
    /// </summary>
    public static class PokemonCardSubjectPackagePublisher
    {
        public const string DefaultPackageId = "pokemon.card-subject-links.en";
        public const string DefaultInstallRelativePath = "pokedex/links/en";

        public static ContentPackagePublishResult Publish(
            string snapshotPath,
            string outputDirectory,
            long revision = 1,
            string version = "1.0.0")
        {
            if (string.IsNullOrWhiteSpace(snapshotPath) || !File.Exists(snapshotPath))
                throw new FileNotFoundException("Card subject snapshot was not found.", snapshotPath);

            string fullSnapshotPath = Path.GetFullPath(snapshotPath);
            string sourceDirectory = Path.GetDirectoryName(fullSnapshotPath);
            string fileName = Path.GetFileName(fullSnapshotPath);
            ContentPackagePublishResult result = new DeterministicContentPackagePublisher().Publish(
                new ContentPackagePublishRequest(
                    outputDirectory,
                    revision,
                    new[]
                    {
                        new ContentPackagePublishDefinition(
                            DefaultPackageId,
                            sourceDirectory,
                            DefaultInstallRelativePath,
                            revision,
                            version,
                            new[] { fileName })
                    }));

            PublishedContentPackage package = result.Packages.Single();
            VerifyArchive(package.ArchivePath, fileName, fullSnapshotPath);
            return result;
        }

        private static void VerifyArchive(string archivePath, string expectedEntryName, string snapshotPath)
        {
            using ZipArchive archive = ZipFile.OpenRead(archivePath);
            ZipArchiveEntry[] files = archive.Entries
                .Where(entry => !entry.FullName.EndsWith("/", StringComparison.Ordinal))
                .ToArray();
            if (files.Length != 1 || !string.Equals(files[0].FullName, expectedEntryName, StringComparison.Ordinal))
                throw new InvalidDataException("Card subject package must contain exactly its runtime snapshot.");
            using Stream archived = files[0].Open();
            using FileStream source = File.OpenRead(snapshotPath);
            if (!string.Equals(Sha256(archived), Sha256(source), StringComparison.Ordinal))
                throw new InvalidDataException("Card subject package snapshot bytes failed verification.");
        }

        private static string Sha256(Stream stream)
        {
            using SHA256 sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
