using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public sealed class LocalDiagnosticLogSummary
{
    public LocalDiagnosticLogSummary(
        int fileCount,
        long totalBytes,
        int maximumAgeDays,
        long maximumBytes)
    {
        FileCount = fileCount;
        TotalBytes = totalBytes;
        MaximumAgeDays = maximumAgeDays;
        MaximumBytes = maximumBytes;
    }

    public int FileCount { get; }
    public long TotalBytes { get; }
    public int MaximumAgeDays { get; }
    public long MaximumBytes { get; }
}

public sealed class LocalDiagnosticLogStore
{
    public const int DefaultMaximumAgeDays = 7;
    public const long DefaultMaximumBytes = 20L * 1024L * 1024L;
    public const long DefaultSegmentBytes = 1024L * 1024L;
    public const string ExportNamespace = "universal-gacha-simulator/local-diagnostics";

    private const int MaximumSummaryCharacters = 1024;
    private static readonly Regex ControlledFileName = new Regex(
        @"^diagnostic-(?<date>\d{8})-(?<segment>\d{3})\.jsonl$",
        RegexOptions.CultureInvariant);
    private static readonly Regex UnsafeField = new Regex(
        @"(?i)\b(token|password|secret|api[_-]?key|access[_-]?key|authorization)\b\s*[:=]\s*[^\s,;]+",
        RegexOptions.CultureInvariant);
    private static readonly Regex BearerValue = new Regex(
        @"(?i)\bbearer\s+[A-Za-z0-9._~+\-/]+=*",
        RegexOptions.CultureInvariant);
    private static readonly Regex EmailAddress = new Regex(
        @"(?i)\b[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}\b",
        RegexOptions.CultureInvariant);
    private static readonly Regex AbsolutePath = new Regex(
        "(?i)(?<![A-Z0-9])(?:[A-Z]:[\\\\/]|\\\\\\\\)[^\\r\\n\\t\\\"'<>|]+",
        RegexOptions.CultureInvariant);
    private static readonly Regex SafeIdentifier = new Regex(
        @"[^A-Za-z0-9_.\-]",
        RegexOptions.CultureInvariant);

    private readonly string rootDirectory;
    private readonly Func<DateTime> utcNow;
    private readonly int maximumAgeDays;
    private readonly long maximumBytes;
    private readonly long segmentBytes;
    private readonly object gate = new object();

    public LocalDiagnosticLogStore(
        string rootDirectory,
        Func<DateTime> utcNow = null,
        int maximumAgeDays = DefaultMaximumAgeDays,
        long maximumBytes = DefaultMaximumBytes,
        long segmentBytes = DefaultSegmentBytes)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
            throw new ArgumentException("A diagnostic root directory is required.", nameof(rootDirectory));
        if (maximumAgeDays <= 0) throw new ArgumentOutOfRangeException(nameof(maximumAgeDays));
        if (maximumBytes < 512) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        if (segmentBytes < 256 || segmentBytes > maximumBytes)
            throw new ArgumentOutOfRangeException(nameof(segmentBytes));

        this.rootDirectory = Path.GetFullPath(rootDirectory);
        this.utcNow = utcNow ?? (() => DateTime.UtcNow);
        this.maximumAgeDays = maximumAgeDays;
        this.maximumBytes = maximumBytes;
        this.segmentBytes = segmentBytes;
    }

    public string RootDirectory => rootDirectory;

    public void Append(string category, string code, string summary)
    {
        lock (gate)
        {
            Directory.CreateDirectory(rootDirectory);
            DateTime now = utcNow().ToUniversalTime();
            PruneCore(now);

            var entry = new DiagnosticEntry
            {
                UtcTicks = now.Ticks,
                Category = NormalizeIdentifier(category, "general").ToLowerInvariant(),
                Code = NormalizeIdentifier(code, "UNKNOWN").ToUpperInvariant(),
                Summary = Sanitize(summary)
            };
            string line = FitLine(entry);
            string path = SelectSegment(now, Encoding.UTF8.GetByteCount(line));
            AppendDurable(path, line);
            PruneCore(now);
        }
    }

    public int Clear()
    {
        lock (gate)
        {
            int deleted = 0;
            foreach (FileInfo file in GetControlledFiles())
            {
                file.Delete();
                deleted++;
            }
            return deleted;
        }
    }

    public LocalDiagnosticLogSummary GetSummary()
    {
        lock (gate)
        {
            PruneCore(utcNow().ToUniversalTime());
            FileInfo[] files = GetControlledFiles().ToArray();
            return new LocalDiagnosticLogSummary(
                files.Length,
                files.Sum(file => file.Length),
                maximumAgeDays,
                maximumBytes);
        }
    }

    public int Export(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A diagnostic export path is required.", nameof(path));
        string fullPath = Path.GetFullPath(path);
        if (IsInsideRoot(fullPath))
            throw new InvalidOperationException("The diagnostic export must be outside the managed log directory.");

        lock (gate)
        {
            DateTime now = utcNow().ToUniversalTime();
            PruneCore(now);
            var entries = new List<DiagnosticEntry>();
            foreach (FileInfo file in GetControlledFiles().OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                foreach (string line in File.ReadLines(file.FullName, Encoding.UTF8))
                {
                    DiagnosticEntry entry = ReadSafeEntry(line);
                    if (entry != null) entries.Add(entry);
                }
            }

            var envelope = new DiagnosticExportEnvelope
            {
                SchemaVersion = 1,
                Namespace = ExportNamespace,
                ExportedAtUtcTicks = now.Ticks,
                Entries = entries
            };
            string json = JsonConvert.SerializeObject(envelope, Formatting.Indented);
            LocalSaveService.WriteAtomic(fullPath, json);
            return entries.Count;
        }
    }

    public void Prune()
    {
        lock (gate)
            PruneCore(utcNow().ToUniversalTime());
    }

    public static string Sanitize(string summary)
    {
        string safe = summary ?? string.Empty;
        safe = safe.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        safe = BearerValue.Replace(safe, "Bearer [redacted]");
        safe = UnsafeField.Replace(safe, match => match.Groups[1].Value + "=[redacted]");
        safe = EmailAddress.Replace(safe, "[redacted-email]");
        safe = AbsolutePath.Replace(safe, "[redacted-path]");
        safe = Regex.Replace(safe, @"\s{2,}", " ").Trim();
        if (safe.Length > MaximumSummaryCharacters)
            safe = safe.Substring(0, MaximumSummaryCharacters);
        return safe;
    }

    public static void TryAppendDefault(string category, string code, string summary)
    {
        try
        {
            new LocalDiagnosticLogStore(
                Path.Combine(Application.persistentDataPath, "diagnostics"))
                .Append(category, code, summary);
        }
        catch
        {
            // Diagnostics must never break gameplay or recursively log their own failure.
        }
    }

    private string FitLine(DiagnosticEntry entry)
    {
        long limit = Math.Min(segmentBytes, maximumBytes);
        string line = JsonConvert.SerializeObject(entry, Formatting.None) + Environment.NewLine;
        while (Encoding.UTF8.GetByteCount(line) > limit && entry.Summary.Length > 0)
        {
            entry.Summary = entry.Summary.Substring(0, Math.Max(0, entry.Summary.Length / 2));
            line = JsonConvert.SerializeObject(entry, Formatting.None) + Environment.NewLine;
        }
        if (Encoding.UTF8.GetByteCount(line) > limit)
            throw new InvalidOperationException("The diagnostic entry exceeds the configured safe limit.");
        return line;
    }

    private string SelectSegment(DateTime now, int incomingBytes)
    {
        string date = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        for (int index = 0; index <= 999; index++)
        {
            string candidate = Path.Combine(rootDirectory, $"diagnostic-{date}-{index:D3}.jsonl");
            if (!File.Exists(candidate) || new FileInfo(candidate).Length + incomingBytes <= segmentBytes)
                return candidate;
        }
        throw new InvalidOperationException("No diagnostic log segment is available for the current day.");
    }

    private void PruneCore(DateTime now)
    {
        DateTime oldestAllowedDate = now.Date.AddDays(-(maximumAgeDays - 1));
        foreach (FileInfo file in GetControlledFiles())
        {
            if (!TryGetFileDate(file.Name, out DateTime fileDate) ||
                fileDate < oldestAllowedDate || fileDate > now.Date)
                file.Delete();
        }

        List<FileInfo> remaining = GetControlledFiles()
            .OrderBy(item => item.Name, StringComparer.Ordinal)
            .ToList();
        long total = remaining.Sum(item => item.Length);
        foreach (FileInfo file in remaining)
        {
            if (total <= maximumBytes) break;
            long length = file.Length;
            file.Delete();
            total -= length;
        }
    }

    private IEnumerable<FileInfo> GetControlledFiles()
    {
        if (!Directory.Exists(rootDirectory)) return Array.Empty<FileInfo>();
        return new DirectoryInfo(rootDirectory)
            .EnumerateFiles("diagnostic-*.jsonl", SearchOption.TopDirectoryOnly)
            .Where(file => ControlledFileName.IsMatch(file.Name))
            .ToArray();
    }

    private static DiagnosticEntry ReadSafeEntry(string line)
    {
        try
        {
            JObject value = JObject.Parse(line);
            long ticks = value.Value<long?>(nameof(DiagnosticEntry.UtcTicks)) ?? 0;
            if (ticks <= 0 || ticks > DateTime.MaxValue.Ticks) return null;
            return new DiagnosticEntry
            {
                UtcTicks = ticks,
                Category = NormalizeIdentifier(value.Value<string>(nameof(DiagnosticEntry.Category)), "general")
                    .ToLowerInvariant(),
                Code = NormalizeIdentifier(value.Value<string>(nameof(DiagnosticEntry.Code)), "UNKNOWN")
                    .ToUpperInvariant(),
                Summary = Sanitize(value.Value<string>(nameof(DiagnosticEntry.Summary)))
            };
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeIdentifier(string value, string fallback)
    {
        string normalized = SafeIdentifier.Replace(value ?? string.Empty, string.Empty);
        if (normalized.Length > 64) normalized = normalized.Substring(0, 64);
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    private static bool TryGetFileDate(string fileName, out DateTime date)
    {
        Match match = ControlledFileName.Match(fileName);
        return DateTime.TryParseExact(
            match.Success ? match.Groups["date"].Value : string.Empty,
            "yyyyMMdd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out date);
    }

    private bool IsInsideRoot(string path)
    {
        string normalizedRoot = rootDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                                Path.DirectorySeparatorChar;
        return path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static void AppendDurable(string path, string line)
    {
        byte[] bytes = new UTF8Encoding(false).GetBytes(line);
        using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
        {
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(true);
        }
    }

    [Serializable]
    private sealed class DiagnosticEntry
    {
        public long UtcTicks;
        public string Category;
        public string Code;
        public string Summary;
    }

    [Serializable]
    private sealed class DiagnosticExportEnvelope
    {
        public int SchemaVersion;
        public string Namespace;
        public long ExportedAtUtcTicks;
        public List<DiagnosticEntry> Entries;
    }
}

public static class LocalDiagnosticLogCapture
{
    private static LocalDiagnosticLogStore store;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        if (Application.isEditor)
            return;
        store = new LocalDiagnosticLogStore(
            Path.Combine(Application.persistentDataPath, "diagnostics"));
        Application.logMessageReceivedThreaded -= OnLogMessage;
        Application.logMessageReceivedThreaded += OnLogMessage;
    }

    private static void OnLogMessage(string condition, string stackTrace, LogType type)
    {
        if (store == null || type == LogType.Log)
            return;

        string code;
        switch (type)
        {
            case LogType.Warning:
                code = "UNITY_WARNING";
                break;
            case LogType.Assert:
                code = "UNITY_ASSERT";
                break;
            case LogType.Exception:
                code = "UNITY_EXCEPTION";
                break;
            default:
                code = "UNITY_ERROR";
                break;
        }

        try
        {
            // Deliberately omit stackTrace. Append applies the same redaction and
            // retention contract used by explicit game diagnostics.
            store.Append("runtime", code, condition);
        }
        catch
        {
            // Runtime diagnostics are best effort and must not recurse through Unity logging.
        }
    }
}
