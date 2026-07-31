using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using WebP;

[Serializable]
public sealed class WebpQualityMeasurement
{
    public int Quality;
    public long Bytes;
    public long SavedBytes;
    public double SavedPercent;
    public double PsnrDb;
    public double MeanAbsoluteRgbError;
    public int MaximumRgbError;
    public string Sha256;
}

[Serializable]
public sealed class WebpQualitySampleResult
{
    public string RecordId;
    public string SetId;
    public string CardId;
    public string LocalId;
    public string CardName;
    public string Category;
    public string Rarity;
    public int GenerationOrder;
    public string Stratum;
    public string ImageRelativePath;
    public long SourceBytes;
    public int Width;
    public int Height;
    public List<WebpQualityMeasurement> Qualities = new List<WebpQualityMeasurement>();
}

[Serializable]
public sealed class WebpQualitySummary
{
    public int Quality;
    public long SampleBytes;
    public long SampleSavedBytes;
    public double SampleSavedPercent;
    public long ProjectedTotalBytes;
    public long ProjectedSavedBytes;
    public double AveragePsnrDb;
    public double MinimumPsnrDb;
    public double AverageMeanAbsoluteRgbError;
    public int MaximumRgbError;
}

[Serializable]
public sealed class WebpQualityExperimentFailure
{
    public string RecordId;
    public string Message;
}

[Serializable]
public sealed class WebpQualityExperimentReport
{
    public int SchemaVersion = 1;
    public bool IsValid;
    public string GeneratedAtUtc;
    public string SnapshotSha256;
    public string Language;
    public int AvailableImageCount;
    public long AvailableImageBytes;
    public int RequestedSampleCount;
    public int SampleCount;
    public int ReviewSampleCount;
    public long SampleSourceBytes;
    public List<int> QualityLevels = new List<int>();
    public List<WebpQualitySummary> Summaries = new List<WebpQualitySummary>();
    public List<WebpQualitySampleResult> Samples = new List<WebpQualitySampleResult>();
    public List<WebpQualityExperimentFailure> Failures =
        new List<WebpQualityExperimentFailure>();
}

public static class WebpQualityExperiment
{
    public static readonly int[] DefaultQualityLevels = { 95, 90, 85 };
    public const int DefaultSampleCount = 120;
    public const int DefaultReviewSampleCount = 12;

    public static WebpQualityExperimentReport Run(
        string importRoot,
        string language = "zh-cn",
        int sampleCount = DefaultSampleCount,
        IEnumerable<int> qualityLevels = null,
        string jsonOutputPath = null,
        string markdownOutputPath = null,
        string reviewOutputRoot = null,
        int reviewSampleCount = DefaultReviewSampleCount,
        string generatedAtUtc = null)
    {
        if (string.IsNullOrWhiteSpace(importRoot))
            throw new ArgumentException("Import root is required.", nameof(importRoot));
        if (sampleCount < 1) throw new ArgumentOutOfRangeException(nameof(sampleCount));
        if (reviewSampleCount < 0) throw new ArgumentOutOfRangeException(nameof(reviewSampleCount));
        string normalizedLanguage = NormalizeLanguage(language) ??
                                    throw new ArgumentException("Language is required.", nameof(language));
        int[] qualities = (qualityLevels ?? DefaultQualityLevels)
            .Distinct().OrderByDescending(value => value).ToArray();
        if (qualities.Length == 0 || qualities.Any(value => value < 0 || value > 100))
            throw new ArgumentException("Quality levels must be unique values from 0 to 100.",
                nameof(qualityLevels));
        string languageRoot = Path.GetFullPath(Path.Combine(importRoot, normalizedLanguage));
        if (!Directory.Exists(languageRoot))
            throw new DirectoryNotFoundException("Language import directory was not found: " + languageRoot);

        var report = new WebpQualityExperimentReport
        {
            GeneratedAtUtc = NormalizeTime(generatedAtUtc),
            Language = normalizedLanguage,
            RequestedSampleCount = sampleCount,
            QualityLevels = qualities.ToList()
        };
        List<Candidate> candidates = ReadCandidates(languageRoot, report);
        report.AvailableImageCount = candidates.Count;
        report.AvailableImageBytes = candidates.Sum(value => value.SourceBytes);
        Candidate[] selected = StratifiedSample(candidates, sampleCount);
        report.SampleCount = selected.Length;
        if (selected.Length < Math.Min(sampleCount, candidates.Count))
            report.Failures.Add(new WebpQualityExperimentFailure
            {
                RecordId = "sample-selection",
                Message = $"Expected {Math.Min(sampleCount, candidates.Count)} samples, " +
                          $"selected {selected.Length}."
            });

        int reviewLimit = Math.Min(reviewSampleCount, selected.Length);
        for (int index = 0; index < selected.Length; index++)
        {
            Candidate candidate = selected[index];
            try
            {
                WebpQualitySampleResult result = Measure(candidate, qualities,
                    index < reviewLimit ? reviewOutputRoot : null);
                report.Samples.Add(result);
            }
            catch (Exception exception)
            {
                report.Failures.Add(new WebpQualityExperimentFailure
                {
                    RecordId = candidate.RecordId,
                    Message = exception.Message
                });
            }
        }
        report.Samples = report.Samples.OrderBy(value => value.RecordId, StringComparer.Ordinal).ToList();
        report.SampleCount = report.Samples.Count;
        report.ReviewSampleCount = reviewOutputRoot == null ? 0 : reviewLimit;
        report.SampleSourceBytes = report.Samples.Sum(value => value.SourceBytes);
        foreach (int quality in qualities)
        {
            WebpQualityMeasurement[] values = report.Samples
                .Select(sample => sample.Qualities.Single(value => value.Quality == quality)).ToArray();
            long sampleBytes = values.Sum(value => value.Bytes);
            long projected = report.SampleSourceBytes == 0
                ? 0
                : (long)Math.Round(report.AvailableImageBytes *
                                   (sampleBytes / (double)report.SampleSourceBytes),
                    MidpointRounding.AwayFromZero);
            report.Summaries.Add(new WebpQualitySummary
            {
                Quality = quality,
                SampleBytes = sampleBytes,
                SampleSavedBytes = report.SampleSourceBytes - sampleBytes,
                SampleSavedPercent = Percent(report.SampleSourceBytes - sampleBytes,
                    report.SampleSourceBytes),
                ProjectedTotalBytes = projected,
                ProjectedSavedBytes = report.AvailableImageBytes - projected,
                AveragePsnrDb = values.Length == 0 ? 0d : values.Average(value => value.PsnrDb),
                MinimumPsnrDb = values.Length == 0 ? 0d : values.Min(value => value.PsnrDb),
                AverageMeanAbsoluteRgbError = values.Length == 0
                    ? 0d
                    : values.Average(value => value.MeanAbsoluteRgbError),
                MaximumRgbError = values.Length == 0 ? 0 : values.Max(value => value.MaximumRgbError)
            });
        }
        report.Failures = report.Failures.OrderBy(value => value.RecordId, StringComparer.Ordinal)
            .ThenBy(value => value.Message, StringComparer.Ordinal).ToList();
        report.IsValid = report.Failures.Count == 0 && report.SampleCount == selected.Length;
        report.SnapshotSha256 = Snapshot(report);
        if (!string.IsNullOrWhiteSpace(jsonOutputPath))
            WriteAtomic(jsonOutputPath, JsonConvert.SerializeObject(report, Formatting.Indented));
        if (!string.IsNullOrWhiteSpace(markdownOutputPath))
            WriteAtomic(markdownOutputPath, Markdown(report));
        return report;
    }

    private static List<Candidate> ReadCandidates(
        string languageRoot,
        WebpQualityExperimentReport report)
    {
        var result = new List<Candidate>();
        foreach (string manifestPath in Directory.GetFiles(
                     languageRoot, "manifest.json", SearchOption.AllDirectories)
                 .OrderBy(value => value, StringComparer.Ordinal))
        {
            PrivateContentManifest manifest;
            try
            {
                manifest = JsonConvert.DeserializeObject<PrivateContentManifest>(
                    File.ReadAllText(manifestPath, Encoding.UTF8));
            }
            catch (Exception exception) when (exception is IOException || exception is JsonException)
            {
                report.Failures.Add(new WebpQualityExperimentFailure
                {
                    RecordId = manifestPath,
                    Message = exception.Message
                });
                continue;
            }
            if (manifest?.Set == null)
            {
                report.Failures.Add(new WebpQualityExperimentFailure
                {
                    RecordId = manifestPath,
                    Message = "Manifest or Set record is missing."
                });
                continue;
            }
            string setRoot = Path.GetDirectoryName(manifestPath) ?? languageRoot;
            foreach (ImportedCardRecord card in manifest.Cards ?? new List<ImportedCardRecord>())
            {
                if (card == null || string.IsNullOrWhiteSpace(card.ImageRelativePath)) continue;
                string path = ResolveWithin(setRoot, card.ImageRelativePath);
                if (path == null || !File.Exists(path))
                {
                    report.Failures.Add(new WebpQualityExperimentFailure
                    {
                        RecordId = RecordId(manifest, card),
                        Message = "Referenced image is missing or unsafe."
                    });
                    continue;
                }
                if (!string.Equals(Path.GetExtension(path), ".webp", StringComparison.OrdinalIgnoreCase))
                    continue;
                long bytes = new FileInfo(path).Length;
                if (bytes <= 0 || card.ImageBytes != bytes)
                {
                    report.Failures.Add(new WebpQualityExperimentFailure
                    {
                        RecordId = RecordId(manifest, card),
                        Message = $"Manifest/file byte mismatch ({card.ImageBytes}/{bytes})."
                    });
                    continue;
                }
                int generation = manifest.Set.GenerationOrder ?? 100;
                string category = NormalizeBucket(card.Category, "unspecified");
                string rarity = RarityBucket(card.Rarity);
                string size = SizeBucket(bytes);
                result.Add(new Candidate
                {
                    RecordId = RecordId(manifest, card),
                    SetId = manifest.Set.Id,
                    CardId = card.Id,
                    LocalId = card.LocalId,
                    CardName = card.Name,
                    Category = card.Category,
                    Rarity = card.Rarity,
                    GenerationOrder = generation,
                    Stratum = $"g{generation}|{category}|{rarity}|{size}",
                    ImagePath = path,
                    ImageRelativePath = Relative(languageRoot, path),
                    SourceBytes = bytes
                });
            }
        }
        return result.OrderBy(value => value.Stratum, StringComparer.Ordinal)
            .ThenBy(value => StableKey(value.RecordId), StringComparer.Ordinal)
            .ThenBy(value => value.RecordId, StringComparer.Ordinal).ToList();
    }

    private static Candidate[] StratifiedSample(IReadOnlyList<Candidate> candidates, int count)
    {
        List<Queue<Candidate>> strata = candidates.GroupBy(value => value.Stratum, StringComparer.Ordinal)
            .OrderBy(value => value.Key, StringComparer.Ordinal)
            .Select(group => new Queue<Candidate>(group
                .OrderBy(value => StableKey(value.RecordId), StringComparer.Ordinal)
                .ThenBy(value => value.RecordId, StringComparer.Ordinal)))
            .ToList();
        var selected = new List<Candidate>(Math.Min(count, candidates.Count));
        while (selected.Count < count)
        {
            bool added = false;
            foreach (Queue<Candidate> stratum in strata)
            {
                if (stratum.Count == 0) continue;
                selected.Add(stratum.Dequeue());
                added = true;
                if (selected.Count == count) break;
            }
            if (!added) break;
        }
        return selected.ToArray();
    }

    private static WebpQualitySampleResult Measure(
        Candidate candidate,
        IReadOnlyList<int> qualities,
        string reviewOutputRoot)
    {
        byte[] source = File.ReadAllBytes(candidate.ImagePath);
        Texture2DExt.GetWebPDimensions(source, out int width, out int height);
        byte[] reference = Texture2DExt.LoadRGBAFromWebP(
            source, ref width, ref height, false, out Error decodeError);
        if (decodeError != Error.Success || reference == null || reference.Length == 0 || width < 1 || height < 1)
            throw new InvalidDataException("Source WebP could not be decoded: " + decodeError);
        var result = new WebpQualitySampleResult
        {
            RecordId = candidate.RecordId,
            SetId = candidate.SetId,
            CardId = candidate.CardId,
            LocalId = candidate.LocalId,
            CardName = candidate.CardName,
            Category = candidate.Category,
            Rarity = candidate.Rarity,
            GenerationOrder = candidate.GenerationOrder,
            Stratum = candidate.Stratum,
            ImageRelativePath = candidate.ImageRelativePath,
            SourceBytes = source.LongLength,
            Width = width,
            Height = height
        };
        string reviewDirectory = null;
        if (!string.IsNullOrWhiteSpace(reviewOutputRoot))
        {
            reviewDirectory = Path.Combine(Path.GetFullPath(reviewOutputRoot), SafeName(candidate.RecordId));
            Directory.CreateDirectory(reviewDirectory);
            WriteBytesAtomic(Path.Combine(reviewDirectory, "source.webp"), source);
        }

        // Unity's raw texture buffer starts at the bottom row, while the WebP
        // decoder returns rows in file order. Flip only the encoder input so the
        // resulting WebP preserves the source file orientation byte-for-pixel.
        byte[] encoderPixels = FlipRows(reference, width, height);
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
        try
        {
            texture.LoadRawTextureData(encoderPixels);
            texture.Apply(false, false);
            foreach (int quality in qualities)
            {
                byte[] encoded = texture.EncodeToWebP(quality, out Error encodeError);
                if (encodeError != Error.Success || encoded == null || encoded.Length == 0)
                    throw new InvalidDataException($"WebP Q{quality} encoding failed: {encodeError}");
                Texture2DExt.GetWebPDimensions(encoded,
                    out int decodedWidth, out int decodedHeight);
                byte[] decoded = Texture2DExt.LoadRGBAFromWebP(encoded,
                    ref decodedWidth, ref decodedHeight, false, out Error resultError);
                if (resultError != Error.Success || decoded == null ||
                    decodedWidth != width || decodedHeight != height || decoded.Length != reference.Length)
                    throw new InvalidDataException($"WebP Q{quality} round-trip decode failed: {resultError}");
                ErrorMetrics metrics = Compare(reference, decoded);
                result.Qualities.Add(new WebpQualityMeasurement
                {
                    Quality = quality,
                    Bytes = encoded.LongLength,
                    SavedBytes = source.LongLength - encoded.LongLength,
                    SavedPercent = Percent(source.LongLength - encoded.LongLength, source.LongLength),
                    PsnrDb = metrics.PsnrDb,
                    MeanAbsoluteRgbError = metrics.MeanAbsoluteRgbError,
                    MaximumRgbError = metrics.MaximumRgbError,
                    Sha256 = Sha256(encoded)
                });
                if (reviewDirectory != null)
                    WriteBytesAtomic(Path.Combine(reviewDirectory, $"q{quality}.webp"), encoded);
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }
        return result;
    }

    private static byte[] FlipRows(byte[] pixels, int width, int height)
    {
        int rowBytes = checked(width * 4);
        if (pixels == null || pixels.Length != checked(rowBytes * height))
            throw new InvalidDataException("Decoded WebP dimensions do not match its RGBA data.");

        var flipped = new byte[pixels.Length];
        for (int row = 0; row < height; row++)
            Buffer.BlockCopy(pixels, row * rowBytes,
                flipped, (height - row - 1) * rowBytes, rowBytes);
        return flipped;
    }

    private static ErrorMetrics Compare(byte[] reference, byte[] candidate)
    {
        double squared = 0d;
        long absolute = 0L;
        int maximum = 0;
        long channels = 0;
        for (int index = 0; index < reference.Length; index += 4)
        for (int channel = 0; channel < 3; channel++)
        {
            int difference = Math.Abs(reference[index + channel] - candidate[index + channel]);
            squared += difference * difference;
            absolute += difference;
            if (difference > maximum) maximum = difference;
            channels++;
        }
        double mse = channels == 0 ? 0d : squared / channels;
        return new ErrorMetrics
        {
            PsnrDb = mse <= 0d ? 99d : 10d * Math.Log10(255d * 255d / mse),
            MeanAbsoluteRgbError = channels == 0 ? 0d : absolute / (double)channels,
            MaximumRgbError = maximum
        };
    }

    private static string Markdown(WebpQualityExperimentReport report)
    {
        var text = new StringBuilder();
        text.AppendLine("# Simplified Chinese WebP quality experiment");
        text.AppendLine();
        text.AppendLine($"Valid: `{report.IsValid}`");
        text.AppendLine($"Snapshot SHA-256: `{report.SnapshotSha256}`");
        text.AppendLine($"Available images/bytes: `{report.AvailableImageCount}` / `{report.AvailableImageBytes}`");
        text.AppendLine($"Stratified samples: `{report.SampleCount}` (review outputs: `{report.ReviewSampleCount}`)");
        text.AppendLine();
        text.AppendLine("| Quality | Sample bytes | Saved | Projected bytes | Projected saved | Avg PSNR | Min PSNR | Avg RGB error | Max RGB error |");
        text.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (WebpQualitySummary summary in report.Summaries)
            text.AppendLine($"| {summary.Quality} | {summary.SampleBytes} | " +
                            $"{summary.SampleSavedBytes} ({summary.SampleSavedPercent:F2}%) | " +
                            $"{summary.ProjectedTotalBytes} | {summary.ProjectedSavedBytes} | " +
                            $"{summary.AveragePsnrDb:F3} | {summary.MinimumPsnrDb:F3} | " +
                            $"{summary.AverageMeanAbsoluteRgbError:F3} | {summary.MaximumRgbError} |");
        text.AppendLine();
        text.AppendLine("Automatic metrics are screening evidence only; visual review decides whether replacement is acceptable.");
        text.AppendLine();
        text.AppendLine("## Failures");
        text.AppendLine();
        if (report.Failures.Count == 0) text.AppendLine("- None.");
        else foreach (WebpQualityExperimentFailure failure in report.Failures)
            text.AppendLine($"- {failure.RecordId}: {failure.Message}");
        return text.ToString().Replace("\r\n", "\n");
    }

    private static string Snapshot(WebpQualityExperimentReport report)
    {
        string previousHash = report.SnapshotSha256;
        string previousTime = report.GeneratedAtUtc;
        report.SnapshotSha256 = null;
        report.GeneratedAtUtc = null;
        string canonical = JsonConvert.SerializeObject(report, Formatting.None);
        report.SnapshotSha256 = previousHash;
        report.GeneratedAtUtc = previousTime;
        return Sha256(Encoding.UTF8.GetBytes(canonical));
    }

    private static string RecordId(PrivateContentManifest manifest, ImportedCardRecord card) =>
        string.Join("|", NormalizeLanguage(manifest.Language), manifest.Set.Id, card.Id, card.LocalId);

    private static string RarityBucket(string rarity)
    {
        string value = NormalizeBucket(rarity, "unspecified");
        if (value.Contains("common") && !value.Contains("uncommon")) return "common";
        if (value.Contains("uncommon")) return "uncommon";
        if (value.Contains("rare")) return "rare";
        return "other";
    }

    private static string SizeBucket(long bytes)
    {
        if (bytes < 32 * 1024) return "small";
        if (bytes < 64 * 1024) return "medium";
        if (bytes < 128 * 1024) return "large";
        return "xlarge";
    }

    private static string NormalizeBucket(string value, string fallback)
    {
        string normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToLowerInvariant();
        return new string(normalized.Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray()).Trim('-');
    }

    private static string NormalizeLanguage(string value) => string.IsNullOrWhiteSpace(value)
        ? null
        : value.Trim().Replace('_', '-').ToLowerInvariant();

    private static string NormalizeTime(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out DateTimeOffset parsed))
            throw new ArgumentException("Generated-at time must be ISO-8601.", nameof(value));
        return parsed.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }

    private static string ResolveWithin(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath)) return null;
        string boundary = Path.GetFullPath(root).TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string candidate = Path.GetFullPath(Path.Combine(root, relativePath));
        return candidate.StartsWith(boundary, Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal) ? candidate : null;
    }

    private static string Relative(string root, string path)
    {
        Uri rootUri = new Uri(Path.GetFullPath(root).TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar);
        return Uri.UnescapeDataString(rootUri.MakeRelativeUri(new Uri(Path.GetFullPath(path))).ToString());
    }

    private static string SafeName(string value)
    {
        string result = new string((value ?? string.Empty).Select(character =>
            char.IsLetterOrDigit(character) || character == '-' || character == '_'
                ? character
                : '-').ToArray()).Trim('-');
        return result.Length == 0 ? StableKey(value).Substring(0, 16) : result;
    }

    private static string StableKey(string value) => Sha256(Encoding.UTF8.GetBytes(value ?? string.Empty));

    private static double Percent(long saved, long original) =>
        original <= 0 ? 0d : saved * 100d / original;

    private static string Sha256(byte[] bytes)
    {
        using SHA256 sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(bytes).Select(value => value.ToString("x2")));
    }

    private static void WriteAtomic(string path, string content)
    {
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException(
            "Output path has no directory."));
        string temporary = fullPath + ".download";
        File.WriteAllText(temporary, content.Replace("\r\n", "\n"), new UTF8Encoding(false));
        if (File.Exists(fullPath)) File.Replace(temporary, fullPath, null);
        else File.Move(temporary, fullPath);
    }

    private static void WriteBytesAtomic(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new InvalidOperationException(
            "Output path has no directory."));
        string temporary = path + ".download";
        File.WriteAllBytes(temporary, bytes);
        if (File.Exists(path)) File.Replace(temporary, path, null);
        else File.Move(temporary, path);
    }

    private sealed class Candidate
    {
        public string RecordId;
        public string SetId;
        public string CardId;
        public string LocalId;
        public string CardName;
        public string Category;
        public string Rarity;
        public int GenerationOrder;
        public string Stratum;
        public string ImagePath;
        public string ImageRelativePath;
        public long SourceBytes;
    }

    private sealed class ErrorMetrics
    {
        public double PsnrDb;
        public double MeanAbsoluteRgbError;
        public int MaximumRgbError;
    }
}
