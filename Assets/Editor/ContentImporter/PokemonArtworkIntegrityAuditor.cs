using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Gacha.EditorTools.Content;
using Gacha.Pokemon.Domain;
using Gacha.Pokemon.Infrastructure;
using Newtonsoft.Json;

[Serializable]
public sealed class PokemonArtworkIntegrityReport
{
    public int SchemaVersion = 1;
    public string GeneratedAtUtc;
    public bool IsValid;
    public int GenerationCount;
    public int FormCount;
    public int ImageCount;
    public int MissingSourceCount;
    public long ImageBytes;
    public int TemporaryFileCount;
    public int PackageCount;
    public long PackageDownloadBytes;
    public long LargestPackageBytes;
    public string CatalogPath;
    public List<string> PackageSha256 = new List<string>();
    public List<string> Failures = new List<string>();
}

public static class PokemonArtworkIntegrityAuditor
{
    public const long SiteSingleObjectLimit = 100L * 1024L * 1024L;

    public static PokemonArtworkIntegrityReport AuditAndPublish(
        string taxonomyPath,
        string artworkRoot,
        string releaseRoot,
        string reportPath)
    {
        var report = new PokemonArtworkIntegrityReport
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
        };
        try
        {
            PokemonTaxonomySnapshotLoadResult taxonomy =
                new PokemonTaxonomySnapshotReader().LoadFile(taxonomyPath);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (PokemonGenerationDefinition generation in taxonomy.Catalog.Generations.Values
                         .OrderBy(value => value.Order))
            {
                string generationRoot = Path.Combine(artworkRoot, generation.Id);
                PokemonArtworkCatalog manifest = new PokemonArtworkManifestReader().LoadFile(
                    Path.Combine(generationRoot, "manifest.json"));
                report.GenerationCount++;
                if (!string.Equals(manifest.TaxonomySourceSha256, taxonomy.SourceSha256, StringComparison.Ordinal))
                    report.Failures.Add("Artwork taxonomy hash differs for " + generation.Id + ".");
                foreach (PokemonArtworkEntry entry in manifest.Entries.Values)
                {
                    if (!seen.Add(entry.FormId))
                        report.Failures.Add("Duplicate artwork form: " + entry.FormId);
                    if (!taxonomy.Catalog.Forms.TryGetValue(entry.FormId, out PokemonFormDefinition form) ||
                        form.IntroducedGenerationId != generation.Id)
                        report.Failures.Add("Artwork form is missing or assigned to the wrong generation: " + entry.FormId);
                    string path = Path.GetFullPath(Path.Combine(
                        generationRoot,
                        entry.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
                    if (!File.Exists(path) || new FileInfo(path).Length != entry.Bytes ||
                        !string.Equals(Sha256(path), entry.Sha256, StringComparison.Ordinal))
                        report.Failures.Add("Artwork file hash or size failed: " + entry.FormId);
                    else
                    {
                        report.ImageCount++;
                        report.ImageBytes += entry.Bytes;
                    }
                }
                foreach (string missingId in manifest.MissingFormIds)
                {
                    if (!seen.Add(missingId))
                        report.Failures.Add("Duplicate missing artwork form: " + missingId);
                    if (!taxonomy.Catalog.Forms.TryGetValue(missingId, out PokemonFormDefinition form) ||
                        form.IntroducedGenerationId != generation.Id || !string.IsNullOrWhiteSpace(form.ImageSourceUrl))
                        report.Failures.Add("Missing artwork form is not an expected source omission: " + missingId);
                    else
                        report.MissingSourceCount++;
                }
                string[] referenced = manifest.Entries.Values
                    .Select(value => Path.GetFullPath(Path.Combine(
                        generationRoot,
                        value.RelativePath.Replace('/', Path.DirectorySeparatorChar))))
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                string[] actual = Directory.GetFiles(generationRoot, "*.png", SearchOption.AllDirectories)
                    .Select(Path.GetFullPath)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (!actual.SequenceEqual(referenced, StringComparer.OrdinalIgnoreCase))
                    report.Failures.Add("Artwork directory has missing or orphan PNG files: " + generation.Id);
            }
            report.FormCount = seen.Count;
            if (report.FormCount != taxonomy.Catalog.Forms.Count)
                report.Failures.Add($"Artwork dispositions cover {report.FormCount}/{taxonomy.Catalog.Forms.Count} forms.");
            report.TemporaryFileCount = Directory.GetFiles(artworkRoot, "*.download", SearchOption.AllDirectories).Length;
            if (report.TemporaryFileCount != 0)
                report.Failures.Add("Artwork root still contains temporary download files.");

            if (report.Failures.Count == 0)
            {
                ContentPackagePublishResult publication = PokemonArtworkPackagePublisher.PublishAll(
                    artworkRoot, releaseRoot);
                report.PackageCount = publication.Packages.Count;
                report.PackageDownloadBytes = publication.Packages.Sum(value => value.Package.DownloadBytes);
                report.LargestPackageBytes = publication.Packages.Max(value => value.Package.DownloadBytes);
                report.CatalogPath = publication.CatalogPath;
                report.PackageSha256 = publication.Packages
                    .Select(value => value.Package.Sha256)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToList();
                if (report.PackageCount != 9)
                    report.Failures.Add("Artwork release does not contain nine generation packages.");
                if (report.LargestPackageBytes > SiteSingleObjectLimit)
                    report.Failures.Add("An artwork generation package exceeds the Site 100 MiB object limit.");
            }
        }
        catch (Exception exception)
        {
            report.Failures.Add(exception.Message);
        }
        report.Failures = report.Failures.Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal).ToList();
        report.IsValid = report.Failures.Count == 0;
        Write(reportPath, report);
        return report;
    }

    private static string Sha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using SHA256 sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
    }

    private static void Write(string path, PokemonArtworkIntegrityReport report)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
        string temporary = path + ".download";
        File.WriteAllText(temporary, JsonConvert.SerializeObject(report, Formatting.Indented));
        if (File.Exists(path))
            File.Delete(path);
        File.Move(temporary, path);
    }
}
