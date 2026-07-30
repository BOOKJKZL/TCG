using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Gacha.Application;
using Gacha.Infrastructure.Content;
using Gacha.Pokemon.Infrastructure;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace Gacha.EditorTools.Content
{
    [Serializable]
    public sealed class PokemonCompleteReleaseReport
    {
        public int SchemaVersion = 1;
        public string GeneratedAtUtc;
        public bool IsValid;
        public int PackageCount;
        public int SetPackageCount;
        public int ArtworkPackageCount;
        public int TaxonomyPackageCount;
        public int LinkPackageCount;
        public long DownloadBytes;
        public long InstalledBytes;
        public long LargestPackageBytes;
        public string CatalogSha256;
        public string PackageIdentitySha256;
        public int InstalledReceiptCount;
        public int InstalledSetCount;
        public int InstalledCardCount;
        public int InstalledPrintingCount;
        public int TaxonomySpeciesCount;
        public int TaxonomyFormCount;
        public int LinkedCardCount;
        public int ArtworkImageCount;
        public List<string> Failures = new List<string>();
    }

    public static class PokemonCompleteReleasePublisher
    {
        public const int ExpectedPackageCount = 229;
        public const string Version = "3.0.0";
        public static string DefaultOutputRoot => Path.Combine(
            ContentPackagePublisherBatch.ProjectRoot, "LocalContent", "Releases", "android-complete");

        [MenuItem("Tools/Universal Gacha/Publish Complete Pokemon Archive")]
        public static void PublishFromMenu()
        {
            PokemonCompleteReleaseReport report = PublishAndAudit();
            Debug.Log(Format(report));
        }

        public static void PublishFromCommandLine()
        {
            try
            {
                PokemonCompleteReleaseReport report = PublishAndAudit();
                Debug.Log(Format(report));
                if (!report.IsValid && UnityEngine.Application.isBatchMode)
                    EditorApplication.Exit(2);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (UnityEngine.Application.isBatchMode)
                    EditorApplication.Exit(1);
            }
        }

        public static PokemonCompleteReleaseReport PublishAndAudit()
        {
            var report = new PokemonCompleteReleaseReport
            {
                GeneratedAtUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
            };
            string verificationRoot = Path.Combine(
                ContentPackagePublisherBatch.ProjectRoot,
                "LocalContent",
                "Pokedex",
                ".complete-release-verification");
            try
            {
                ContentPackagePublishDefinition[] definitions = BuildDefinitions().ToArray();
                report.PackageCount = definitions.Length;
                report.SetPackageCount = definitions.Count(value => value.PackageId.StartsWith("en.", StringComparison.Ordinal));
                report.ArtworkPackageCount = definitions.Count(value => value.PackageId.StartsWith(
                    "pokemon.pokedex.artwork.", StringComparison.Ordinal));
                report.TaxonomyPackageCount = definitions.Count(value => value.PackageId == "pokemon.pokedex.taxonomy");
                report.LinkPackageCount = definitions.Count(value => value.PackageId == PokemonCardSubjectPackagePublisher.DefaultPackageId);
                if (report.PackageCount != ExpectedPackageCount || report.SetPackageCount != 218 ||
                    report.ArtworkPackageCount != 9 || report.TaxonomyPackageCount != 1 || report.LinkPackageCount != 1)
                    report.Failures.Add("Complete release package category counts are incorrect.");

                var request = new ContentPackagePublishRequest(DefaultOutputRoot, 3, definitions);
                ContentPackagePublishResult first = new DeterministicContentPackagePublisher().Publish(request);
                byte[] firstCatalog = File.ReadAllBytes(first.CatalogPath);
                string firstIdentity = IdentityHash(first.Packages);
                ContentPackagePublishResult second = new DeterministicContentPackagePublisher().Publish(request);
                if (!firstCatalog.SequenceEqual(File.ReadAllBytes(second.CatalogPath)) ||
                    !string.Equals(firstIdentity, IdentityHash(second.Packages), StringComparison.Ordinal))
                    report.Failures.Add("Complete release changed across consecutive deterministic builds.");

                report.CatalogSha256 = Sha256(firstCatalog);
                report.PackageIdentitySha256 = firstIdentity;
                report.DownloadBytes = first.Packages.Sum(value => value.Package.DownloadBytes);
                report.InstalledBytes = first.Packages.Sum(value => value.Package.InstalledBytes);
                report.LargestPackageBytes = first.Packages.Max(value => value.Package.DownloadBytes);
                if (report.LargestPackageBytes > PokemonArtworkIntegrityAuditor.SiteSingleObjectLimit)
                    report.Failures.Add("Complete release contains an archive larger than the Site object limit.");

                if (Directory.Exists(verificationRoot))
                    Directory.Delete(verificationRoot, true);
                string contentRoot = Path.Combine(verificationRoot, "Content");
                var registry = new FileSystemInstalledContentPackageRegistry(contentRoot);
                var planner = new ContentPackagePlanner(
                    registry,
                    new FileSystemContentStorageProbe(contentRoot),
                    0);
                var installer = new FileSystemContentPackageInstaller(contentRoot);
                foreach (PublishedContentPackage package in first.Packages)
                {
                    ContentInstallPlan plan = planner.Plan(package.Package);
                    if (!plan.CanStart)
                        throw new InvalidOperationException(
                            $"Complete release install planning failed for {package.Package.PackageId}: {plan.ErrorMessage}");
                    ContentPackageInstallResult installed = installer.InstallAsync(plan, package.ArchivePath)
                        .GetAwaiter().GetResult();
                    if (!installed.Succeeded)
                        throw new InvalidOperationException(
                            $"Complete release installation failed for {package.Package.PackageId}: {installed.ErrorMessage}");
                    if (registry.Find(package.Package.PackageId) != null)
                        report.InstalledReceiptCount++;
                }

                CatalogLoadResult cards = new PrivateContentCatalogProvider(contentRoot).Load();
                if (!cards.Succeeded)
                    throw new InvalidDataException("Installed card catalog failed: " + cards.ErrorMessage);
                report.InstalledSetCount = cards.SourceSetCount;
                report.InstalledCardCount = cards.SourceItemCount;
                report.InstalledPrintingCount = cards.PrintingCount;
                var pokedex = new PokemonPokedexSnapshotRepository().Load(
                    Path.Combine(contentRoot, "pokedex", "taxonomy", "pokemon-taxonomy.json"),
                    Path.Combine(contentRoot, "pokedex", "links", "en", "pokemon-card-subject-links.en.json"));
                report.TaxonomySpeciesCount = pokedex.Catalog.Species.Count;
                report.TaxonomyFormCount = pokedex.Catalog.Forms.Count;
                report.LinkedCardCount = pokedex.SubjectCatalog.Cards.Count;
                for (int generation = 1; generation <= 9; generation++)
                {
                    PokemonArtworkCatalog artwork = new PokemonArtworkManifestReader().LoadFile(Path.Combine(
                        contentRoot,
                        "pokedex",
                        "artwork",
                        "generation-" + generation,
                        "manifest.json"));
                    if (!string.Equals(artwork.TaxonomySourceSha256, pokedex.Taxonomy.SourceSha256, StringComparison.Ordinal))
                        report.Failures.Add("Installed artwork taxonomy hash differs for generation-" + generation + ".");
                    report.ArtworkImageCount += artwork.Entries.Count;
                }

                if (report.InstalledReceiptCount != ExpectedPackageCount ||
                    report.InstalledSetCount != 218 || report.InstalledCardCount != 23444 ||
                    report.TaxonomySpeciesCount != 1025 || report.TaxonomyFormCount != 1579 ||
                    report.LinkedCardCount != 23444 || report.ArtworkImageCount != 1571)
                    report.Failures.Add("Installed complete release counts do not match audited source counts.");
            }
            catch (Exception exception)
            {
                report.Failures.Add(exception.Message);
            }
            finally
            {
                if (Directory.Exists(verificationRoot))
                    Directory.Delete(verificationRoot, true);
            }
            report.Failures = report.Failures.Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal).ToList();
            report.IsValid = report.Failures.Count == 0;
            string reportPath = Path.Combine(DefaultOutputRoot, "complete-release-audit.json");
            Directory.CreateDirectory(DefaultOutputRoot);
            File.WriteAllText(reportPath, JsonConvert.SerializeObject(report, Formatting.Indented));
            return report;
        }

        public static IEnumerable<ContentPackagePublishDefinition> BuildDefinitions()
        {
            IReadOnlyList<ContentPackagePublisherBatch.ImportedSet> imports =
                ContentPackagePublisherBatch.DiscoverImports(ContentPackagePublisherBatch.DefaultImportRoot)
                    .Where(value => value.Language.Equals("en", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(value => value.SetId, StringComparer.Ordinal)
                    .ToArray();
            if (imports.Count != 218)
                throw new InvalidDataException($"Complete release requires 218 English Sets; found {imports.Count}.");
            foreach (ContentPackagePublisherBatch.ImportedSet imported in imports)
                yield return new ContentPackagePublishDefinition(
                    imported.Language + "." + imported.SetId,
                    imported.SourceDirectory,
                    imported.Language + "/" + imported.SetId,
                    3,
                    Version,
                    ContentPackagePublisherBatch.RuntimePackagePaths(imported));

            string pokedexRoot = Path.Combine(ContentPackagePublisherBatch.ProjectRoot, "LocalContent", "Pokedex");
            yield return new ContentPackagePublishDefinition(
                "pokemon.pokedex.taxonomy",
                Path.Combine(pokedexRoot, "snapshot"),
                "pokedex/taxonomy",
                3,
                Version,
                new[] { "pokemon-taxonomy.json" });
            yield return new ContentPackagePublishDefinition(
                PokemonCardSubjectPackagePublisher.DefaultPackageId,
                Path.Combine(pokedexRoot, "links"),
                PokemonCardSubjectPackagePublisher.DefaultInstallRelativePath,
                3,
                Version,
                new[] { "pokemon-card-subject-links.en.json" });
            for (int generation = 1; generation <= 9; generation++)
            {
                string generationId = "generation-" + generation;
                yield return new ContentPackagePublishDefinition(
                    "pokemon.pokedex.artwork." + generationId,
                    Path.Combine(pokedexRoot, "artwork", generationId),
                    "pokedex/artwork/" + generationId,
                    3,
                    Version);
            }
        }

        private static string IdentityHash(IEnumerable<PublishedContentPackage> packages)
        {
            string value = string.Join("\n", packages
                .OrderBy(item => item.Package.PackageId, StringComparer.Ordinal)
                .Select(item => item.Package.PackageId + ":" + item.Package.Sha256)) + "\n";
            return Sha256(Encoding.UTF8.GetBytes(value));
        }

        private static string Sha256(byte[] bytes)
        {
            using SHA256 sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static string Format(PokemonCompleteReleaseReport report) =>
            $"Complete Pokemon release valid={report.IsValid}: packages={report.PackageCount}, " +
            $"download={report.DownloadBytes}, installed={report.InstalledBytes}, " +
            $"sets/cards/printings={report.InstalledSetCount}/{report.InstalledCardCount}/" +
            $"{report.InstalledPrintingCount}, species/forms/artwork={report.TaxonomySpeciesCount}/" +
            $"{report.TaxonomyFormCount}/{report.ArtworkImageCount}, failures={report.Failures.Count}.";
    }
}
