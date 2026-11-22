using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using WebP;

[Serializable]
public sealed class SimplifiedChineseImageOptimizationFailure
{
    public string SetId;
    public string CardId;
    public string Message;
}

[Serializable]
public sealed class SimplifiedChineseImageOptimizationReport
{
    public int SchemaVersion = 1;
    public string GeneratedAtUtc;
    public string Language = "zh-cn";
    public float Quality;
    public bool IsValid;
    public int SetCount;
    public int CardCount;
    public int ConvertedImageCount;
    public int ExistingWebpImageCount;
    public int RelabeledWebpImageCount;
    public int RepairedSourceImageCount;
    public int MissingImageCount;
    public long BeforeBytes;
    public long AfterBytes;
    public long SavedBytes;
    public List<SimplifiedChineseImageOptimizationFailure> Failures =
        new List<SimplifiedChineseImageOptimizationFailure>();
}

public static class SimplifiedChineseImageOptimizer
{
    public const float DefaultQuality = 95f;

    [MenuItem("Tools/Gacha/Optimize Simplified Chinese Images (WebP 95)")]
    public static void OptimizeFromMenu()
    {
        SimplifiedChineseImageOptimizationReport report = OptimizeDefault();
        Debug.Log(Format(report));
    }

    public static void OptimizeFromCommandLine()
    {
        try
        {
            SimplifiedChineseImageOptimizationReport report = OptimizeDefault();
            Debug.Log(Format(report));
            if (!report.IsValid && Application.isBatchMode)
                EditorApplication.Exit(2);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            if (Application.isBatchMode)
                EditorApplication.Exit(1);
        }
    }

    public static SimplifiedChineseImageOptimizationReport Optimize(
        string importRoot,
        string language = "zh-cn",
        float quality = DefaultQuality,
        string outputPath = null,
        Func<string, byte[]> replacementDownloader = null)
    {
        if (string.IsNullOrWhiteSpace(importRoot))
            throw new ArgumentException("Import root is required.", nameof(importRoot));
        if (string.IsNullOrWhiteSpace(language))
            throw new ArgumentException("Language is required.", nameof(language));
        if (quality < 0f || quality > 100f)
            throw new ArgumentOutOfRangeException(nameof(quality));
        string normalizedLanguage = language.Trim().ToLowerInvariant();
        string languageRoot = Path.GetFullPath(Path.Combine(importRoot, normalizedLanguage));
        if (!Directory.Exists(languageRoot))
            throw new DirectoryNotFoundException("Language import directory was not found: " + languageRoot);

        using var importLock = ContentImportFile.AcquireWriteLock(languageRoot);
        var report = new SimplifiedChineseImageOptimizationReport
        {
            GeneratedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            Language = normalizedLanguage,
            Quality = quality
        };
        string[] manifestPaths = Directory.GetFiles(
                languageRoot, "manifest.json", SearchOption.AllDirectories)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (string manifestPath in manifestPaths)
            OptimizeManifest(manifestPath, quality, report, replacementDownloader);
        report.SavedBytes = Math.Max(0, report.BeforeBytes - report.AfterBytes);
        report.Failures = report.Failures
            .OrderBy(value => value.SetId, StringComparer.Ordinal)
            .ThenBy(value => value.CardId, StringComparer.Ordinal)
            .ThenBy(value => value.Message, StringComparer.Ordinal)
            .ToList();
        report.IsValid = report.Failures.Count == 0;
        string destination = string.IsNullOrWhiteSpace(outputPath)
            ? Path.Combine(languageRoot, "image-optimization-audit.json")
            : Path.GetFullPath(outputPath);
        ContentImportFile.WriteText(destination, JsonConvert.SerializeObject(report, Formatting.Indented));
        return report;
    }

    private static void OptimizeManifest(
        string manifestPath,
        float quality,
        SimplifiedChineseImageOptimizationReport report,
        Func<string, byte[]> replacementDownloader)
    {
        PrivateContentManifest manifest = JsonConvert.DeserializeObject<PrivateContentManifest>(
            File.ReadAllText(manifestPath, Encoding.UTF8));
        if (manifest?.Set == null)
        {
            Failure(report, Path.GetFileName(Path.GetDirectoryName(manifestPath)), null,
                "Manifest or Set record is missing.");
            return;
        }

        report.SetCount++;
        string setDirectory = Path.GetDirectoryName(manifestPath) ?? string.Empty;
        var originalsToDelete = new List<string>();
        bool changed = false;
        foreach (ImportedCardRecord card in manifest.Cards ?? new List<ImportedCardRecord>())
        {
            report.CardCount++;
            if (string.IsNullOrWhiteSpace(card.ImageRelativePath))
            {
                report.MissingImageCount++;
                continue;
            }

            string sourcePath = ResolveWithin(setDirectory, card.ImageRelativePath);
            if (sourcePath == null || !File.Exists(sourcePath))
            {
                Failure(report, manifest.Set.Id, card.Id,
                    "Referenced source image is missing or unsafe: " + card.ImageRelativePath);
                continue;
            }

            string extension = Path.GetExtension(sourcePath);
            try
            {
                if (string.Equals(extension, ".webp", StringComparison.OrdinalIgnoreCase))
                {
                    byte[] existing = File.ReadAllBytes(sourcePath);
                    if (card.ImageBytes != 0 && card.ImageBytes != existing.LongLength)
                        throw new InvalidDataException("Existing WebP size does not match the manifest.");
                    if (!string.IsNullOrEmpty(card.ImageSha256) &&
                        !string.Equals(card.ImageSha256, Sha256(existing), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Existing WebP SHA-256 does not match the manifest.");
                    ValidateWebp(existing);
                    report.ExistingWebpImageCount++;
                    report.BeforeBytes += existing.LongLength;
                    report.AfterBytes += existing.LongLength;
                    continue;
                }
                if (!string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Only PNG inputs can be optimized; found " + extension + ".");

                byte[] png = File.ReadAllBytes(sourcePath);
                report.BeforeBytes += png.LongLength;
                string webpPath = Path.ChangeExtension(sourcePath, ".webp");
                byte[] webp;
                if (IsWebp(png))
                {
                    webp = ValidateWebp(png);
                    report.RelabeledWebpImageCount++;
                }
                else
                {
                    try
                    {
                        webp = EncodePng(png, quality);
                    }
                    catch (InvalidDataException) when (!string.IsNullOrWhiteSpace(card.ImageSourceUrl))
                    {
                        byte[] repaired = DownloadReplacement(
                            card.ImageSourceUrl,
                            replacementDownloader);
                        webp = IsWebp(repaired) ? ValidateWebp(repaired) : EncodePng(repaired, quality);
                        report.RepairedSourceImageCount++;
                    }
                }
                string digest = Sha256(webp);
                if (File.Exists(webpPath) && !string.Equals(Sha256(File.ReadAllBytes(webpPath)), digest, StringComparison.Ordinal) &&
                    manifest.Cards.Any(other => other != card &&
                        string.Equals(ResolveWithin(setDirectory, other.ImageRelativePath), webpPath, PathComparison())))
                {
                    webpPath = Path.Combine(Path.GetDirectoryName(webpPath),
                        Path.GetFileNameWithoutExtension(webpPath) + "-" + digest + ".webp");
                }
                ContentImportFile.WriteBytes(webpPath, webp);
                report.ConvertedImageCount++;
                card.ImageRelativePath = RelativePath(setDirectory, webpPath);
                card.ImageSha256 = digest;
                card.ImageBytes = webp.LongLength;
                report.AfterBytes += webp.LongLength;
                originalsToDelete.Add(sourcePath);
                changed = true;
            }
            catch (Exception exception)
            {
                Failure(report, manifest.Set.Id, card.Id, exception.Message);
            }
        }

        if (!changed)
            return;
        ContentImportFile.WriteText(manifestPath, JsonConvert.SerializeObject(manifest, Formatting.Indented));
        var referenced = new HashSet<string>(manifest.Cards
            .Select(card => ResolveWithin(setDirectory, card.ImageRelativePath)), PathComparer());
        foreach (string original in originalsToDelete.Distinct(PathComparer()))
        {
            if (referenced.Contains(original) || !File.Exists(original))
                continue;
            try
            {
                File.Delete(original);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Failure(report, manifest.Set.Id, null, $"Manifest saved, but PNG cleanup failed: {original}: {exception.Message}");
            }
        }
    }

    private static byte[] EncodePng(byte[] png, float quality)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
        try
        {
            if (!ImageConversion.LoadImage(texture, png, false))
                throw new InvalidDataException("Unity could not decode the PNG image.");
            byte[] webp = texture.EncodeToWebP(quality, out Error error);
            if (error != Error.Success || webp == null || webp.Length == 0)
                throw new InvalidDataException("WebP encoding failed: " + error);
            return webp;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    private static byte[] ValidateWebp(byte[] bytes)
    {
        Texture2DExt.GetWebPDimensions(bytes, out int width, out int height);
        byte[] decoded = Texture2DExt.LoadRGBAFromWebP(bytes, ref width, ref height, false, out Error error);
        if (error != Error.Success || decoded == null || decoded.Length == 0)
            throw new InvalidDataException("Source WebP could not be decoded: " + error);
        return bytes;
    }

    private static bool IsWebp(byte[] bytes) =>
        bytes != null && bytes.Length >= 12 &&
        bytes[0] == (byte)'R' && bytes[1] == (byte)'I' &&
        bytes[2] == (byte)'F' && bytes[3] == (byte)'F' &&
        bytes[8] == (byte)'W' && bytes[9] == (byte)'E' &&
        bytes[10] == (byte)'B' && bytes[11] == (byte)'P';

    private static byte[] DownloadReplacement(
        string url,
        Func<string, byte[]> replacementDownloader)
    {
        if (replacementDownloader != null)
            return replacementDownloader(url) ??
                   throw new InvalidDataException("Replacement image download returned no data.");

        Exception last = null;
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                byte[] bytes = client.GetByteArrayAsync(url).GetAwaiter().GetResult();
                if (bytes == null || bytes.Length < 12)
                    throw new InvalidDataException("Replacement image download was empty.");
                return bytes;
            }
            catch (Exception exception)
            {
                last = exception;
                if (attempt < 3)
                    Thread.Sleep(attempt * 500);
            }
        }
        throw new HttpRequestException(
            "Failed to redownload corrupt source image after 3 attempts: " + url,
            last);
    }

    private static string ResolveWithin(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            return null;
        string boundary = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        string candidate = Path.GetFullPath(Path.Combine(root, relativePath));
        return candidate.StartsWith(boundary, PathComparison()) ? candidate : null;
    }

    private static string RelativePath(string root, string path)
    {
        Uri rootUri = new Uri(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) +
                              Path.DirectorySeparatorChar);
        return Uri.UnescapeDataString(rootUri.MakeRelativeUri(new Uri(Path.GetFullPath(path))).ToString())
            .Replace('/', Path.DirectorySeparatorChar);
    }

    private static string Sha256(byte[] bytes)
    {
        using SHA256 sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(bytes).Select(value => value.ToString("x2")));
    }

    private static void Failure(
        SimplifiedChineseImageOptimizationReport report,
        string setId,
        string cardId,
        string message)
    {
        report.Failures.Add(new SimplifiedChineseImageOptimizationFailure
        {
            SetId = setId,
            CardId = cardId,
            Message = message
        });
    }

    private static StringComparer PathComparer() =>
        Path.DirectorySeparatorChar == '\\'
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private static StringComparison PathComparison() =>
        Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static SimplifiedChineseImageOptimizationReport OptimizeDefault()
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        return Optimize(Path.Combine(projectRoot, "LocalContent", "Imports"));
    }

    private static string Format(SimplifiedChineseImageOptimizationReport report) =>
        $"Simplified Chinese WebP optimization valid={report.IsValid}: " +
        $"sets/cards={report.SetCount}/{report.CardCount}, converted={report.ConvertedImageCount}, " +
        $"relabeled/repaired={report.RelabeledWebpImageCount}/{report.RepairedSourceImageCount}, " +
        $"missing={report.MissingImageCount}, before={report.BeforeBytes}, after={report.AfterBytes}, " +
        $"saved={report.SavedBytes}, failures={report.Failures.Count}.";
}
