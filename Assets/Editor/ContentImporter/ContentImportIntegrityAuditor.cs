using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

[Serializable]
public sealed class ContentImportIntegrityReport
{
    public int SchemaVersion = 1;
    public string Language;
    public string GeneratedAtUtc;
    public bool IsValid;
    public int SetCount;
    public int CardCount;
    public int RawCardFileCount;
    public int ImageFileCount;
    public int MissingImageReferenceCount;
    public int OrphanImageFileCount;
    public int DownloadTempFileCount;
    public long ImageBytes;
    public List<ContentImportIntegrityFailure> Failures = new List<ContentImportIntegrityFailure>();
}

[Serializable]
public sealed class ContentImportIntegrityFailure
{
    public string Scope;
    public string ItemId;
    public string Message;
}

public static class ContentImportIntegrityAuditor
{
    private static readonly HashSet<string> SupportedImageExtensions = new HashSet<string>(
        new[] { ".jpg", ".jpeg", ".png", ".webp" },
        StringComparer.OrdinalIgnoreCase);

    public static ContentImportIntegrityReport Audit(
        string importRoot, string language, int expectedSetCount, string outputPath = null)
    {
        if (string.IsNullOrWhiteSpace(importRoot))
            throw new ArgumentException("Import root is required.", nameof(importRoot));
        if (string.IsNullOrWhiteSpace(language))
            throw new ArgumentException("Language is required.", nameof(language));
        if (expectedSetCount < 1)
            throw new ArgumentOutOfRangeException(nameof(expectedSetCount));

        string normalizedLanguage = language.Trim().ToLowerInvariant();
        string languageRoot = Path.GetFullPath(Path.Combine(importRoot, normalizedLanguage));
        var report = new ContentImportIntegrityReport
        {
            Language = normalizedLanguage,
            GeneratedAtUtc = DateTime.UtcNow.ToString("O")
        };
        if (!Directory.Exists(languageRoot))
        {
            Failure(report, "language", normalizedLanguage, "Language import directory does not exist.");
            Finish(report, outputPath);
            return report;
        }

        string[] manifestPaths = Directory.GetFiles(
                languageRoot, "manifest.json", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        report.SetCount = manifestPaths.Length;
        if (report.SetCount != expectedSetCount)
            Failure(report, "set-count", normalizedLanguage,
                $"Expected {expectedSetCount} manifests, found {report.SetCount}.");

        var cardIds = new HashSet<string>(StringComparer.Ordinal);
        var referencedRawFiles = new HashSet<string>(PathComparer());
        var referencedImageFiles = new HashSet<string>(PathComparer());
        foreach (string manifestPath in manifestPaths)
            AuditManifest(manifestPath, languageRoot, normalizedLanguage, report,
                cardIds, referencedRawFiles, referencedImageFiles);

        string[] rawFiles = Directory.GetFiles(languageRoot, "*.json", SearchOption.AllDirectories)
            .Where(path => path.IndexOf(
                $"{Path.DirectorySeparatorChar}raw{Path.DirectorySeparatorChar}cards{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase) >= 0)
            .Select(Path.GetFullPath)
            .ToArray();
        report.RawCardFileCount = rawFiles.Length;
        foreach (string orphan in rawFiles.Where(path => !referencedRawFiles.Contains(path)))
            Failure(report, "orphan-raw", Relative(languageRoot, orphan),
                "Raw card JSON is not referenced by any manifest.");

        string[] imageFiles = Directory.GetFiles(languageRoot, "*", SearchOption.AllDirectories)
            .Where(path => SupportedImageExtensions.Contains(Path.GetExtension(path)))
            .Select(Path.GetFullPath)
            .ToArray();
        report.ImageFileCount = imageFiles.Length;
        report.OrphanImageFileCount = imageFiles.Count(path => !referencedImageFiles.Contains(path));
        foreach (string orphan in imageFiles.Where(path => !referencedImageFiles.Contains(path)))
            Failure(report, "orphan-image", Relative(languageRoot, orphan),
                "Image is not referenced by any manifest.");

        report.DownloadTempFileCount = Directory.GetFiles(
            languageRoot, "*.download", SearchOption.AllDirectories).Length;
        if (report.DownloadTempFileCount > 0)
            Failure(report, "temporary-files", normalizedLanguage,
                $"Found {report.DownloadTempFileCount} unfinished .download files.");

        Finish(report, outputPath);
        return report;
    }

    private static void AuditManifest(
        string manifestPath,
        string languageRoot,
        string language,
        ContentImportIntegrityReport report,
        ISet<string> cardIds,
        ISet<string> referencedRawFiles,
        ISet<string> referencedImageFiles)
    {
        string setDirectory = Path.GetDirectoryName(manifestPath) ?? string.Empty;
        string fallbackSetId = Path.GetFileName(setDirectory);
        PrivateContentManifest manifest;
        try
        {
            manifest = JsonConvert.DeserializeObject<PrivateContentManifest>(File.ReadAllText(manifestPath));
        }
        catch (Exception exception) when (exception is IOException || exception is JsonException)
        {
            Failure(report, "manifest", fallbackSetId, exception.Message);
            return;
        }
        if (manifest == null || manifest.Set == null)
        {
            Failure(report, "manifest", fallbackSetId, "Manifest or Set record is missing.");
            return;
        }

        string setId = manifest.Set.Id ?? fallbackSetId;
        if (manifest.SchemaVersion != 2)
            Failure(report, "manifest-schema", setId, $"Expected schema 2, got {manifest.SchemaVersion}.");
        if (!string.Equals(manifest.Language, language, StringComparison.Ordinal))
            Failure(report, "manifest-language", setId,
                $"Expected language '{language}', got '{manifest.Language}'.");
        if (string.IsNullOrWhiteSpace(manifest.Set.GenerationId) ||
            string.Equals(manifest.Set.GenerationId, "unmapped", StringComparison.Ordinal) ||
            !manifest.Set.GenerationOrder.HasValue || !manifest.Set.SetOrdinal.HasValue)
            Failure(report, "set-ordering", setId, "Set generation or ordinal metadata is incomplete.");
        if (!File.Exists(Path.Combine(setDirectory, "raw", "set.json")))
            Failure(report, "set-raw", setId, "raw/set.json is missing.");
        foreach (ContentImportError sourceError in manifest.Errors ?? new List<ContentImportError>())
            Failure(report, "source-error", $"{setId}:{sourceError.ItemId}", sourceError.Message);

        foreach (ImportedCardRecord card in manifest.Cards ?? new List<ImportedCardRecord>())
        {
            report.CardCount++;
            string cardId = card?.Id;
            if (card == null || string.IsNullOrWhiteSpace(cardId))
            {
                Failure(report, "card", setId, "Card record or ID is missing.");
                continue;
            }
            if (!cardIds.Add(cardId))
                Failure(report, "duplicate-card", cardId, "Card ID occurs in more than one manifest.");

            string rawPath = ResolveWithin(setDirectory, card.RawDataRelativePath);
            if (rawPath == null)
                Failure(report, "raw-path", cardId, "RawDataRelativePath escapes the Set directory.");
            else if (!File.Exists(rawPath))
                Failure(report, "raw-missing", cardId, card.RawDataRelativePath);
            else
                referencedRawFiles.Add(rawPath);

            if (string.IsNullOrWhiteSpace(card.ImageRelativePath))
            {
                report.MissingImageReferenceCount++;
                continue;
            }
            string imagePath = ResolveWithin(setDirectory, card.ImageRelativePath);
            if (imagePath == null)
            {
                Failure(report, "image-path", cardId, "ImageRelativePath escapes the Set directory.");
                continue;
            }
            if (!File.Exists(imagePath))
            {
                Failure(report, "image-missing", cardId, card.ImageRelativePath);
                continue;
            }

            if (!SupportedImageExtensions.Contains(Path.GetExtension(imagePath)))
            {
                Failure(report, "image-format", cardId,
                    $"Unsupported image extension: {Path.GetExtension(imagePath)}");
                continue;
            }

            referencedImageFiles.Add(imagePath);
            var info = new FileInfo(imagePath);
            report.ImageBytes += info.Length;
            if (info.Length != card.ImageBytes)
                Failure(report, "image-size", cardId,
                    $"Manifest {card.ImageBytes} bytes, file {info.Length} bytes.");
            string hash = ComputeSha256(imagePath);
            if (!string.Equals(hash, card.ImageSha256, StringComparison.OrdinalIgnoreCase))
                Failure(report, "image-hash", cardId,
                    $"Expected {card.ImageSha256}, got {hash}.");
        }
    }

    private static string ResolveWithin(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            return null;
        string normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        string candidate;
        try
        {
            candidate = Path.GetFullPath(Path.Combine(root, relativePath));
        }
        catch (Exception exception) when (
            exception is ArgumentException || exception is NotSupportedException ||
            exception is PathTooLongException)
        {
            return null;
        }
        return candidate.StartsWith(normalizedRoot, PathComparison()) ? candidate : null;
    }

    private static string ComputeSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using SHA256 sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(stream).Select(value => value.ToString("x2")));
    }

    private static void Finish(ContentImportIntegrityReport report, string outputPath)
    {
        report.Failures = report.Failures
            .OrderBy(item => item.Scope, StringComparer.Ordinal)
            .ThenBy(item => item.ItemId, StringComparer.Ordinal)
            .ThenBy(item => item.Message, StringComparer.Ordinal)
            .ToList();
        report.IsValid = report.Failures.Count == 0;
        if (string.IsNullOrWhiteSpace(outputPath))
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? string.Empty);
        string temporaryPath = outputPath + ".download";
        File.WriteAllText(temporaryPath, JsonConvert.SerializeObject(report, Formatting.Indented),
            new UTF8Encoding(false));
        if (File.Exists(outputPath))
            File.Delete(outputPath);
        File.Move(temporaryPath, outputPath);
    }

    private static void Failure(
        ContentImportIntegrityReport report, string scope, string itemId, string message)
    {
        report.Failures.Add(new ContentImportIntegrityFailure
        {
            Scope = scope,
            ItemId = itemId,
            Message = message
        });
    }

    private static string Relative(string root, string path)
    {
        Uri rootUri = new Uri(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) +
                              Path.DirectorySeparatorChar);
        return Uri.UnescapeDataString(rootUri.MakeRelativeUri(new Uri(path)).ToString());
    }

    private static StringComparer PathComparer()
    {
        return Path.DirectorySeparatorChar == '\\'
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
    }

    private static StringComparison PathComparison()
    {
        return Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }
}
