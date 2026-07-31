using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Gacha.Application;
using Gacha.Infrastructure.Content;
using Gacha.Infrastructure.Rules;
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
        public int LanguageGroupPackageCount;
        public long PreviousCatalogRevision;
        public long CatalogRevision;
        public int PreviousPackageCount;
        public int UnchangedPreviousPackageCount;
        public int UpdatedPreviousPackageCount;
        public int NewPackageCount;
        public long DownloadBytes;
        public long InstalledBytes;
        public long LargestPackageBytes;
        public string CatalogSha256;
        public string PackageIdentitySha256;
        public int InstalledReceiptCount;
        public int InstalledSetCount;
        public int InstalledCardCount;
        public int InstalledPrintingCount;
        public int InstalledLanguageGroupCount;
        public int TaxonomySpeciesCount;
        public int TaxonomyFormCount;
        public int LinkedCardCount;
        public int ArtworkImageCount;
        public List<string> Failures = new List<string>();
    }

    public static class PokemonCompleteReleasePublisher
    {
        public const int ExpectedSetPackageCount = 524;
        public const int ExpectedPackageCount = 538;
        public const long PreviousCatalogRevision = 4;
        public const long CatalogRevision = 5;
        public const string Version = "4.0.0";
        private static readonly string[] CardLanguages = { "en", "ja", "zh-cn" };
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
                if (UnityEngine.Application.isBatchMode)
                    EditorApplication.Exit(report.IsValid ? 0 : 2);
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
                GeneratedAtUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                CatalogRevision = CatalogRevision
            };
            string verificationRoot = Path.Combine(
                ContentPackagePublisherBatch.ProjectRoot,
                "LocalContent",
                "Pokedex",
                ".complete-release-verification");
            try
            {
                ContentPackageCatalog previousCatalog = LoadPreviousCatalog(report);
                ContentPackagePublishDefinition[] definitions = BuildDefinitions().ToArray();
                report.PackageCount = definitions.Length;
                report.SetPackageCount = definitions.Count(value => CardLanguages.Any(language =>
                    value.PackageId.StartsWith(language + ".", StringComparison.Ordinal)));
                report.ArtworkPackageCount = definitions.Count(value => value.PackageId.StartsWith(
                    "pokemon.pokedex.artwork.", StringComparison.Ordinal));
                report.TaxonomyPackageCount = definitions.Count(value => value.PackageId == "pokemon.pokedex.taxonomy");
                report.LinkPackageCount = definitions.Count(value => value.PackageId.StartsWith(
                    "pokemon.card-subject-links.", StringComparison.Ordinal));
                report.LanguageGroupPackageCount = definitions.Count(value => value.PackageId ==
                    PrintingLanguageGroupPackagePublisher.PackageId);
                if (report.PackageCount != ExpectedPackageCount ||
                    report.SetPackageCount != ExpectedSetPackageCount ||
                    report.ArtworkPackageCount != 9 || report.TaxonomyPackageCount != 1 ||
                    report.LinkPackageCount != CardLanguages.Length ||
                    report.LanguageGroupPackageCount != 1)
                    report.Failures.Add("Complete release package category counts are incorrect.");

                CatalogLoadResult expectedCards = new PrivateContentCatalogProvider(
                    ContentPackagePublisherBatch.DefaultImportRoot,
                    variantPolicy: new PokemonImportedCardVariantPolicy()).Load();
                if (!expectedCards.Succeeded)
                    throw new InvalidDataException("Source card catalog failed: " + expectedCards.ErrorMessage);

                var request = new ContentPackagePublishRequest(
                    DefaultOutputRoot, CatalogRevision, definitions);
                ContentPackagePublishResult first = new DeterministicContentPackagePublisher().Publish(request);
                byte[] firstCatalog = File.ReadAllBytes(first.CatalogPath);
                string firstIdentity = IdentityHash(first.Packages);
                ContentPackagePublishResult second = new DeterministicContentPackagePublisher().Publish(request);
                if (!firstCatalog.SequenceEqual(File.ReadAllBytes(second.CatalogPath)) ||
                    !string.Equals(firstIdentity, IdentityHash(second.Packages), StringComparison.Ordinal))
                    report.Failures.Add("Complete release changed across consecutive deterministic builds.");
                VerifyPreviousPackages(previousCatalog, first.Packages, report);

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

                CatalogLoadResult cards = new PrivateContentCatalogProvider(
                    contentRoot,
                    variantPolicy: new PokemonImportedCardVariantPolicy()).Load();
                if (!cards.Succeeded)
                    throw new InvalidDataException("Installed card catalog failed: " + cards.ErrorMessage);
                report.InstalledSetCount = cards.SourceSetCount;
                report.InstalledCardCount = cards.SourceItemCount;
                report.InstalledPrintingCount = cards.PrintingCount;
                report.InstalledLanguageGroupCount = cards.Catalog.PrintingLanguageGroups.Count;
                string taxonomyPath = Path.Combine(
                    contentRoot, "pokedex", "taxonomy", "pokemon-taxonomy.json");
                PokemonTaxonomySnapshotLoadResult taxonomy =
                    new PokemonTaxonomySnapshotReader().LoadFile(taxonomyPath);
                report.TaxonomySpeciesCount = taxonomy.Catalog.Species.Count;
                report.TaxonomyFormCount = taxonomy.Catalog.Forms.Count;
                foreach (string language in CardLanguages)
                {
                    PokemonPokedexSnapshotBundle pokedex = new PokemonPokedexSnapshotRepository().Load(
                        taxonomyPath,
                        Path.Combine(contentRoot, "pokedex", "links", language,
                            $"pokemon-card-subject-links.{language}.json"));
                    report.LinkedCardCount += pokedex.SubjectCatalog.Cards.Count;
                    int missingItems = pokedex.SubjectCatalog.Cards.Values.Count(value =>
                        !cards.Catalog.Items.ContainsKey(value.ItemId));
                    int missingPrintings = pokedex.SubjectCatalog.Cards.Values.Sum(value =>
                        value.PrintingIds.Count(id => !cards.Catalog.Printings.ContainsKey(id)));
                    if (missingItems > 0 || missingPrintings > 0)
                        report.Failures.Add(
                            $"Installed '{language}' subject links miss {missingItems} Items and " +
                            $"{missingPrintings} Printings in the runtime catalog.");
                }
                for (int generation = 1; generation <= 9; generation++)
                {
                    PokemonArtworkCatalog artwork = new PokemonArtworkManifestReader().LoadFile(Path.Combine(
                        contentRoot,
                        "pokedex",
                        "artwork",
                        "generation-" + generation,
                        "manifest.json"));
                    if (!string.Equals(
                            artwork.TaxonomySourceSha256, taxonomy.SourceSha256, StringComparison.Ordinal))
                        report.Failures.Add("Installed artwork taxonomy hash differs for generation-" + generation + ".");
                    report.ArtworkImageCount += artwork.Entries.Count;
                }

                if (report.InstalledReceiptCount != ExpectedPackageCount ||
                    report.InstalledSetCount != expectedCards.SourceSetCount ||
                    report.InstalledCardCount != expectedCards.SourceItemCount ||
                    report.InstalledPrintingCount != expectedCards.PrintingCount ||
                    report.InstalledLanguageGroupCount != 147 ||
                    report.TaxonomySpeciesCount != 1025 || report.TaxonomyFormCount != 1579 ||
                    report.LinkedCardCount != 44076 || report.ArtworkImageCount != 1571)
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
                    .Where(value => CardLanguages.Contains(
                        value.Language, StringComparer.OrdinalIgnoreCase))
                    .OrderBy(value => value.Language, StringComparer.Ordinal)
                    .ThenBy(value => value.SetId, StringComparer.Ordinal)
                    .ToArray();
            IReadOnlyDictionary<string, int> expected = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = 218,
                ["ja"] = 177,
                ["zh-cn"] = 129
            };
            foreach (KeyValuePair<string, int> language in expected)
            {
                int actual = imports.Count(value => string.Equals(
                    value.Language, language.Key, StringComparison.OrdinalIgnoreCase));
                if (actual != language.Value)
                    throw new InvalidDataException(
                        $"Complete release requires {language.Value} '{language.Key}' Sets; found {actual}.");
            }
            foreach (ContentPackagePublisherBatch.ImportedSet imported in imports)
                yield return new ContentPackagePublishDefinition(
                    imported.Language.ToLowerInvariant() + "." + EncodePackageIdSegment(imported.SetId),
                    imported.SourceDirectory,
                    imported.Language + "/" + imported.SetId,
                    4,
                    Version,
                    ContentPackagePublisherBatch.RuntimePackagePaths(imported));

            yield return PrintingLanguageGroupPackagePublisher.BuildDefinition(
                ContentPackagePublisherBatch.DefaultImportRoot);

            string pokedexRoot = Path.Combine(ContentPackagePublisherBatch.ProjectRoot, "LocalContent", "Pokedex");
            yield return new ContentPackagePublishDefinition(
                "pokemon.pokedex.taxonomy",
                Path.Combine(pokedexRoot, "snapshot"),
                "pokedex/taxonomy",
                4,
                Version,
                new[] { "pokemon-taxonomy.json" });
            foreach (string language in CardLanguages)
                yield return new ContentPackagePublishDefinition(
                    PokemonCardSubjectPackagePublisher.PackageId(language),
                    Path.Combine(pokedexRoot, "links"),
                    PokemonCardSubjectPackagePublisher.InstallRelativePath(language),
                    CatalogRevision,
                    "4.1.0",
                    new[] { $"pokemon-card-subject-links.{language}.json" });
            for (int generation = 1; generation <= 9; generation++)
            {
                string generationId = "generation-" + generation;
                yield return new ContentPackagePublishDefinition(
                    "pokemon.pokedex.artwork." + generationId,
                    Path.Combine(pokedexRoot, "artwork", generationId),
                    "pokedex/artwork/" + generationId,
                    4,
                    Version);
            }
        }

        private static ContentPackageCatalog LoadPreviousCatalog(
            PokemonCompleteReleaseReport report)
        {
            string backupPath = Path.Combine(
                DefaultOutputRoot, $"catalog.revision-{PreviousCatalogRevision}.json");
            string currentPath = Path.Combine(DefaultOutputRoot, "catalog.json");
            string sourcePath = File.Exists(backupPath) ? backupPath : currentPath;
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException(
                    "Previous complete release catalog is required for non-destructive migration.",
                    sourcePath);
            string json = File.ReadAllText(sourcePath);
            ContentPackageCatalogLoadResult loaded = new JsonContentPackageCatalogReader().Read(
                json, new Uri("https://publisher.invalid/releases/catalog.json"));
            if (!loaded.Succeeded)
                throw new InvalidDataException(
                    "Previous complete release catalog failed validation: " + loaded.ErrorMessage);
            if (loaded.Catalog.Revision != PreviousCatalogRevision ||
                loaded.Catalog.Packages.Count != ExpectedPackageCount - 1)
                throw new InvalidDataException(
                    $"Expected revision {PreviousCatalogRevision} with {ExpectedPackageCount - 1} packages; " +
                    $"found revision {loaded.Catalog.Revision} with {loaded.Catalog.Packages.Count} packages.");
            if (!File.Exists(backupPath))
                File.Copy(currentPath, backupPath, false);
            report.PreviousCatalogRevision = loaded.Catalog.Revision;
            report.PreviousPackageCount = loaded.Catalog.Packages.Count;
            return loaded.Catalog;
        }

        private static void VerifyPreviousPackages(
            ContentPackageCatalog previous,
            IReadOnlyCollection<PublishedContentPackage> current,
            PokemonCompleteReleaseReport report)
        {
            Dictionary<string, ContentPackageDescriptor> currentById = current
                .ToDictionary(value => value.Package.PackageId, value => value.Package,
                    StringComparer.Ordinal);
            foreach (ContentPackageCatalogEntry entry in previous.Packages)
            {
                if (!currentById.TryGetValue(
                        entry.Package.PackageId, out ContentPackageDescriptor package))
                {
                    report.Failures.Add(
                        "Previous package is missing: " + entry.Package.PackageId + ".");
                    continue;
                }
                if (SameDescriptor(entry.Package, package))
                    report.UnchangedPreviousPackageCount++;
                else if (entry.Package.PackageId.StartsWith(
                             "pokemon.card-subject-links.", StringComparison.Ordinal) &&
                         package.Revision == CatalogRevision && package.Version == "4.1.0")
                    report.UpdatedPreviousPackageCount++;
                else
                    report.Failures.Add(
                        "Previous package descriptor changed: " + entry.Package.PackageId + ".");
            }
            var previousIds = new HashSet<string>(
                previous.Packages.Select(value => value.Package.PackageId), StringComparer.Ordinal);
            report.NewPackageCount = current.Count(value =>
                !previousIds.Contains(value.Package.PackageId));
            if (report.UnchangedPreviousPackageCount != previous.Packages.Count - CardLanguages.Length ||
                report.UpdatedPreviousPackageCount != CardLanguages.Length ||
                report.NewPackageCount != 1 ||
                !currentById.ContainsKey(PrintingLanguageGroupPackagePublisher.PackageId))
                report.Failures.Add(
                    "Revision 5 must reuse 534 descriptors, update 3 language link packages, " +
                    "and add exactly the language-group package.");
        }

        private static bool SameDescriptor(
            ContentPackageDescriptor left,
            ContentPackageDescriptor right) =>
            left.Revision == right.Revision &&
            left.DownloadBytes == right.DownloadBytes &&
            left.InstalledBytes == right.InstalledBytes &&
            string.Equals(left.PackageId, right.PackageId, StringComparison.Ordinal) &&
            string.Equals(left.InstallRelativePath, right.InstallRelativePath, StringComparison.Ordinal) &&
            string.Equals(left.Version, right.Version, StringComparison.Ordinal) &&
            string.Equals(left.Sha256, right.Sha256, StringComparison.OrdinalIgnoreCase);

        public static string EncodePackageIdSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Package id segment cannot be empty.", nameof(value));

            byte[] bytes = Encoding.UTF8.GetBytes(value.Trim().ToLowerInvariant());
            StringBuilder encoded = new(bytes.Length);
            foreach (byte current in bytes)
            {
                bool safe = current >= (byte)'a' && current <= (byte)'z' ||
                            current >= (byte)'0' && current <= (byte)'9' ||
                            current == (byte)'.' || current == (byte)'-';
                if (safe)
                    encoded.Append((char)current);
                else
                    encoded.Append('_').Append(current.ToString("x2", CultureInfo.InvariantCulture));
            }
            return encoded.ToString();
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
