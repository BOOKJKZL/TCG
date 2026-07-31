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
        public int SchemaVersion = 2;
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
        public int CatalogSchemaVersion;
        public int PreviousPackageCount;
        public int UnchangedPreviousPackageCount;
        public int UpdatedPreviousPackageCount;
        public int NewPackageCount;
        public int MetadataPackageCount;
        public int DatedSetPackageCount;
        public int UndatedSetPackageCount;
        public int DependencyEdgeCount;
        public int VerifiedExistingArchiveCount;
        public bool ReusedPreviousRuntimeAudit;
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
        public const long PreviousCatalogRevision = 5;
        public const long CatalogRevision = 6;
        public const long LinkPackageRevision = 5;
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
                PokemonCompleteReleaseReport previousAudit = LoadPreviousAudit();
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
                VerifyMetadata(definitions, report);

                CatalogLoadResult expectedCards = new PrivateContentCatalogProvider(
                    ContentPackagePublisherBatch.DefaultImportRoot,
                    variantPolicy: new PokemonImportedCardVariantPolicy()).Load();
                if (!expectedCards.Succeeded)
                    throw new InvalidDataException("Source card catalog failed: " + expectedCards.ErrorMessage);

                var request = new ContentPackagePublishRequest(
                    DefaultOutputRoot, CatalogRevision, definitions);
                ContentPackagePublishResult first =
                    new DeterministicContentPackagePublisher().PublishCatalogFromExisting(
                        request, previousCatalog);
                report.VerifiedExistingArchiveCount = first.Packages.Count;
                byte[] firstCatalog = File.ReadAllBytes(first.CatalogPath);
                ContentPackageCatalogLoadResult publishedCatalog =
                    new JsonContentPackageCatalogReader().Read(
                        Encoding.UTF8.GetString(firstCatalog),
                        new Uri("https://publisher.invalid/releases/catalog.json"));
                if (!publishedCatalog.Succeeded)
                    throw new InvalidDataException(
                        "Published v2 catalog failed readback: " + publishedCatalog.ErrorMessage);
                report.CatalogSchemaVersion = publishedCatalog.Catalog.SchemaVersion;
                if (report.CatalogSchemaVersion != ContentPackageCatalog.SupportedSchemaVersion ||
                    publishedCatalog.Catalog.Packages.Any(value => value.Metadata.IsLegacy))
                    report.Failures.Add("Complete release catalog did not preserve schema v2 metadata.");
                string firstIdentity = IdentityHash(first.Packages);
                string repeatedCatalog = DeterministicContentPackagePublisher.SerializeCatalogSnapshot(
                    CatalogRevision, first.Packages);
                if (!firstCatalog.SequenceEqual(Encoding.UTF8.GetBytes(repeatedCatalog)))
                    report.Failures.Add("Complete release catalog serialization is not deterministic.");
                VerifyPreviousPackages(previousCatalog, first.Packages, report);

                report.CatalogSha256 = Sha256(firstCatalog);
                report.PackageIdentitySha256 = firstIdentity;
                report.DownloadBytes = first.Packages.Sum(value => value.Package.DownloadBytes);
                report.InstalledBytes = first.Packages.Sum(value => value.Package.InstalledBytes);
                report.LargestPackageBytes = first.Packages.Max(value => value.Package.DownloadBytes);
                if (report.LargestPackageBytes > PokemonArtworkIntegrityAuditor.SiteSingleObjectLimit)
                    report.Failures.Add("Complete release contains an archive larger than the Site object limit.");
                CopyPreviousRuntimeAudit(previousAudit, report, firstIdentity);

                if (report.VerifiedExistingArchiveCount != ExpectedPackageCount ||
                    report.InstalledReceiptCount != ExpectedPackageCount ||
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
                    ContentPackagePublisherBatch.RuntimePackagePaths(imported),
                    ContentPackagePublisherBatch.BuildCardSetMetadata(
                        imported, "pokemon-tcg", new[] { "pokemon" }));

            yield return PrintingLanguageGroupPackagePublisher.BuildDefinition(
                ContentPackagePublisherBatch.DefaultImportRoot,
                Metadata(
                    "printing-language-groups",
                    Names("Card language variants", "カード言語バリエーション", "卡牌语言版本"),
                    tags: new[] { "pokemon", "card-language" }));

            string pokedexRoot = Path.Combine(ContentPackagePublisherBatch.ProjectRoot, "LocalContent", "Pokedex");
            yield return new ContentPackagePublishDefinition(
                "pokemon.pokedex.taxonomy",
                Path.Combine(pokedexRoot, "snapshot"),
                "pokedex/taxonomy",
                4,
                Version,
                new[] { "pokemon-taxonomy.json" },
                Metadata(
                    "pokedex-taxonomy",
                    Names("Pokédex taxonomy", "ポケモン図鑑分類", "宝可梦图鉴分类"),
                    tags: new[] { "pokemon", "pokedex" }));
            foreach (string language in CardLanguages)
                yield return new ContentPackagePublishDefinition(
                    PokemonCardSubjectPackagePublisher.PackageId(language),
                    Path.Combine(pokedexRoot, "links"),
                    PokemonCardSubjectPackagePublisher.InstallRelativePath(language),
                    LinkPackageRevision,
                    "4.1.0",
                    new[] { $"pokemon-card-subject-links.{language}.json" },
                    Metadata(
                        "card-subject-links",
                        LinkNames(language),
                        language,
                        tags: new[] { "pokemon", "pokedex", "card-links" },
                        dependencies: new[] { "pokemon.pokedex.taxonomy" }));
            for (int generation = 1; generation <= 9; generation++)
            {
                string generationId = "generation-" + generation;
                yield return new ContentPackagePublishDefinition(
                    "pokemon.pokedex.artwork." + generationId,
                    Path.Combine(pokedexRoot, "artwork", generationId),
                    "pokedex/artwork/" + generationId,
                    4,
                    Version,
                    metadata: Metadata(
                        "pokedex-artwork",
                        Names(
                            $"Generation {generation} Pokédex artwork",
                            $"第{generation}世代 ポケモン図鑑イラスト",
                            $"第 {generation} 世代宝可梦图鉴图片"),
                        generationOrder: generation,
                        sortOrdinal: generation,
                        tags: new[] { "pokemon", "pokedex", "artwork" },
                        dependencies: new[] { "pokemon.pokedex.taxonomy" }));
            }
        }

        private static ContentPackageMetadata Metadata(
            string kind,
            IReadOnlyDictionary<string, string> names,
            string contentLanguageId = null,
            int? generationOrder = null,
            int? sortOrdinal = null,
            IEnumerable<string> tags = null,
            IEnumerable<string> dependencies = null) =>
            new ContentPackageMetadata(
                kind,
                names,
                "pokemon-tcg",
                contentLanguageId,
                generationOrder: generationOrder,
                sortOrdinal: sortOrdinal,
                tags: tags,
                dependencies: dependencies);

        private static IReadOnlyDictionary<string, string> Names(
            string english,
            string japanese,
            string simplifiedChinese) =>
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = english,
                ["ja"] = japanese,
                ["zh-cn"] = simplifiedChinese
            };

        private static IReadOnlyDictionary<string, string> LinkNames(string language)
        {
            switch (language)
            {
                case "en":
                    return Names(
                        "English card Pokédex index",
                        "英語カード図鑑インデックス",
                        "英文卡牌图鉴索引");
                case "ja":
                    return Names(
                        "Japanese card Pokédex index",
                        "日本語カード図鑑インデックス",
                        "日文卡牌图鉴索引");
                case "zh-cn":
                    return Names(
                        "Simplified Chinese card Pokédex index",
                        "簡体字中国語カード図鑑インデックス",
                        "简体中文卡牌图鉴索引");
                default:
                    throw new ArgumentOutOfRangeException(nameof(language), language, null);
            }
        }

        private static void VerifyMetadata(
            IReadOnlyCollection<ContentPackagePublishDefinition> definitions,
            PokemonCompleteReleaseReport report)
        {
            ContentPackagePublishDefinition[] metadataPackages = definitions
                .Where(value => value.Metadata != null && !value.Metadata.IsLegacy)
                .ToArray();
            report.MetadataPackageCount = metadataPackages.Length;
            report.DependencyEdgeCount = metadataPackages.Sum(value =>
                value.Metadata.Dependencies.Count);
            ContentPackagePublishDefinition[] sets = metadataPackages
                .Where(value => string.Equals(
                    value.Metadata.Kind, "card-set", StringComparison.Ordinal))
                .ToArray();
            report.DatedSetPackageCount = sets.Count(value => value.Metadata.ReleaseDate.HasValue);
            report.UndatedSetPackageCount = sets.Length - report.DatedSetPackageCount;

            bool setMetadataValid = sets.All(value =>
                string.Equals(value.Metadata.GameId, "pokemon-tcg", StringComparison.Ordinal) &&
                CardLanguages.Contains(value.Metadata.ContentLanguageId, StringComparer.Ordinal) &&
                value.Metadata.LocalizedNames.ContainsKey(value.Metadata.ContentLanguageId) &&
                !string.IsNullOrWhiteSpace(value.Metadata.SetId) &&
                !string.IsNullOrWhiteSpace(value.Metadata.SetCode) &&
                value.Metadata.GenerationOrder.HasValue &&
                value.Metadata.SortOrdinal.HasValue);
            if (report.MetadataPackageCount != ExpectedPackageCount ||
                sets.Length != ExpectedSetPackageCount ||
                report.DatedSetPackageCount != 421 ||
                report.UndatedSetPackageCount != 103 ||
                report.DependencyEdgeCount != 12 ||
                !setMetadataValid)
                report.Failures.Add("Complete release player metadata counts or identities are incorrect.");
        }

        private static PokemonCompleteReleaseReport LoadPreviousAudit()
        {
            string backupPath = Path.Combine(
                DefaultOutputRoot,
                $"complete-release-audit.revision-{PreviousCatalogRevision}.json");
            string currentPath = Path.Combine(DefaultOutputRoot, "complete-release-audit.json");
            string sourcePath = File.Exists(backupPath) ? backupPath : currentPath;
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException(
                    "Previous complete release runtime audit is required for metadata-only migration.",
                    sourcePath);
            PokemonCompleteReleaseReport previous = JsonConvert.DeserializeObject<PokemonCompleteReleaseReport>(
                File.ReadAllText(sourcePath));
            if (previous == null || !previous.IsValid ||
                previous.CatalogRevision != PreviousCatalogRevision ||
                previous.PackageCount != ExpectedPackageCount ||
                previous.InstalledReceiptCount != ExpectedPackageCount ||
                string.IsNullOrWhiteSpace(previous.PackageIdentitySha256))
                throw new InvalidDataException(
                    "Previous complete release runtime audit is not valid for metadata-only migration.");
            if (!File.Exists(backupPath))
                File.Copy(currentPath, backupPath, false);
            return previous;
        }

        private static void CopyPreviousRuntimeAudit(
            PokemonCompleteReleaseReport previous,
            PokemonCompleteReleaseReport current,
            string packageIdentitySha256)
        {
            if (!string.Equals(
                    previous.PackageIdentitySha256,
                    packageIdentitySha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "Metadata-only migration package identity differs from the previous runtime audit.");
            current.InstalledReceiptCount = previous.InstalledReceiptCount;
            current.InstalledSetCount = previous.InstalledSetCount;
            current.InstalledCardCount = previous.InstalledCardCount;
            current.InstalledPrintingCount = previous.InstalledPrintingCount;
            current.InstalledLanguageGroupCount = previous.InstalledLanguageGroupCount;
            current.TaxonomySpeciesCount = previous.TaxonomySpeciesCount;
            current.TaxonomyFormCount = previous.TaxonomyFormCount;
            current.LinkedCardCount = previous.LinkedCardCount;
            current.ArtworkImageCount = previous.ArtworkImageCount;
            current.ReusedPreviousRuntimeAudit = true;
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
                loaded.Catalog.Packages.Count != ExpectedPackageCount)
                throw new InvalidDataException(
                    $"Expected revision {PreviousCatalogRevision} with {ExpectedPackageCount} packages; " +
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
                else
                {
                    report.UpdatedPreviousPackageCount++;
                    report.Failures.Add(
                        "Previous package descriptor changed: " + entry.Package.PackageId + ".");
                }
            }
            var previousIds = new HashSet<string>(
                previous.Packages.Select(value => value.Package.PackageId), StringComparer.Ordinal);
            report.NewPackageCount = current.Count(value =>
                !previousIds.Contains(value.Package.PackageId));
            if (report.UnchangedPreviousPackageCount != previous.Packages.Count ||
                report.UpdatedPreviousPackageCount != 0 ||
                report.NewPackageCount != 0)
                report.Failures.Add(
                    "Revision 6 metadata migration must reuse all 538 package descriptors.");
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
