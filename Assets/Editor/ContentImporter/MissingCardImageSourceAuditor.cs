using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

[Serializable]
public sealed class MissingCardImageExpectation
{
    public string Language;
    public int MissingImageCount;

    public MissingCardImageExpectation(string language, int missingImageCount)
    {
        Language = language;
        MissingImageCount = missingImageCount;
    }
}

[Serializable]
public sealed class MissingCardImageSourceEntry
{
    public string RecordId;
    public string Language;
    public string ManifestSource;
    public string SetId;
    public string CardId;
    public string LocalId;
    public string CardName;
    public string MetadataUrl;
    public string DeclaredImageUrl;
    public string ProbeUrl;
    public string DownloadUrl;
    public string Status;
    public int HttpStatus;
    public string Reason;
}

[Serializable]
public sealed class MissingCardImageLanguageSummary
{
    public string Language;
    public int MissingImageCount;
    public int AvailableAtSourceCount;
    public int SourceUnavailableCount;
    public int SourceNotFoundCount;
    public int SourceNotDeclaredCount;
    public int SourceCardMissingCount;
    public int InvalidSourceCount;
    public int ProbeFailedCount;
}

[Serializable]
public sealed class MissingCardImageSourceAuditReport
{
    public int SchemaVersion = 1;
    public bool IsValid;
    public string CheckedAtUtc;
    public string SnapshotSha256;
    public int MissingImageCount;
    public int AvailableAtSourceCount;
    public int SourceUnavailableCount;
    public int SourceNotFoundCount;
    public int SourceNotDeclaredCount;
    public int SourceCardMissingCount;
    public int InvalidSourceCount;
    public int ProbeFailedCount;
    public int RemoteRequestCount;
    public List<MissingCardImageLanguageSummary> Languages =
        new List<MissingCardImageLanguageSummary>();
    public List<MissingCardImageSourceEntry> Entries =
        new List<MissingCardImageSourceEntry>();
    public List<string> Failures = new List<string>();
}

public sealed class MissingImageHttpResponse
{
    public int StatusCode;
    public string Body;
    public string ContentType;
    public long? ContentLength;
}

public interface IMissingImageSourceClient
{
    MissingImageHttpResponse Get(string url);
    MissingImageHttpResponse Head(string url);
}

public sealed class HttpMissingImageSourceClient : IMissingImageSourceClient, IDisposable
{
    private readonly HttpClient client;
    private readonly int attempts;

    public HttpMissingImageSourceClient(TimeSpan? timeout = null, int attempts = 3)
    {
        if (attempts < 1) throw new ArgumentOutOfRangeException(nameof(attempts));
        this.attempts = attempts;
        client = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("UniversalGachaPrivateImporter/1.0");
    }

    public MissingImageHttpResponse Get(string url) => Send(HttpMethod.Get, url);
    public MissingImageHttpResponse Head(string url) => Send(HttpMethod.Head, url);

    public void Dispose() => client.Dispose();

    private MissingImageHttpResponse Send(HttpMethod method, string url)
    {
        Exception last = null;
        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(method, url);
                using HttpResponseMessage response = client.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
                string body = method == HttpMethod.Head
                    ? null
                    : response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                return new MissingImageHttpResponse
                {
                    StatusCode = (int)response.StatusCode,
                    Body = body,
                    ContentType = response.Content.Headers.ContentType?.MediaType,
                    ContentLength = response.Content.Headers.ContentLength
                };
            }
            catch (Exception exception) when (exception is HttpRequestException ||
                                               exception is TaskCanceledException)
            {
                last = exception;
                if (attempt < attempts) Thread.Sleep(attempt * 250);
            }
        }
        throw new HttpRequestException($"Remote image source request failed: {url}", last);
    }
}

public static class MissingCardImageSourceAuditor
{
    private const string TcgdexSource = "tcgdex";
    private const string TcgdexApiHost = "api.tcgdex.net";
    private const string TcgdexAssetHost = "assets.tcgdex.net";
    private static readonly HashSet<string> DirectImageHosts =
        new HashSet<string>(new[] { "tcg.mik.moe" }, StringComparer.OrdinalIgnoreCase);

    public static MissingCardImageSourceAuditReport Audit(
        string importRoot,
        IEnumerable<string> languages,
        IEnumerable<MissingCardImageExpectation> expectations,
        IMissingImageSourceClient client,
        string checkedAtUtc = null,
        string jsonOutputPath = null,
        string markdownOutputPath = null,
        string queueOutputPath = null)
    {
        if (string.IsNullOrWhiteSpace(importRoot))
            throw new ArgumentException("Import root is required.", nameof(importRoot));
        if (client == null) throw new ArgumentNullException(nameof(client));
        string[] requestedLanguages = (languages ?? Enumerable.Empty<string>())
            .Select(NormalizeLanguage).Where(value => value != null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.Ordinal).ToArray();
        if (requestedLanguages.Length == 0)
            throw new ArgumentException("At least one language is required.", nameof(languages));
        Dictionary<string, MissingCardImageExpectation> expected =
            (expectations ?? Enumerable.Empty<MissingCardImageExpectation>())
            .Where(value => value != null && NormalizeLanguage(value.Language) != null)
            .ToDictionary(value => NormalizeLanguage(value.Language), value => value,
                StringComparer.OrdinalIgnoreCase);
        var report = new MissingCardImageSourceAuditReport
        {
            CheckedAtUtc = NormalizeCheckedAt(checkedAtUtc)
        };
        List<PendingCard> pending = ReadMissingCards(
            Path.GetFullPath(importRoot), requestedLanguages, report);

        foreach (IGrouping<string, PendingCard> sourceSet in pending
                     .GroupBy(value => Key(value.ManifestSource, value.Language, value.SetId),
                         StringComparer.Ordinal)
                     .OrderBy(value => value.Key, StringComparer.Ordinal))
        {
            PendingCard[] cards = sourceSet.OrderBy(value => value.CardId, StringComparer.Ordinal)
                .ThenBy(value => value.LocalId, StringComparer.Ordinal).ToArray();
            if (string.Equals(cards[0].ManifestSource, TcgdexSource,
                    StringComparison.OrdinalIgnoreCase))
                ProbeTcgdexSet(cards, client, report);
            else
                foreach (PendingCard card in cards)
                    ProbeDirectSource(card, client, report);
        }

        report.Entries = report.Entries.OrderBy(value => value.Language, StringComparer.Ordinal)
            .ThenBy(value => value.SetId, StringComparer.Ordinal)
            .ThenBy(value => value.LocalId, StringComparer.Ordinal)
            .ThenBy(value => value.CardId, StringComparer.Ordinal)
            .ThenBy(value => value.RecordId, StringComparer.Ordinal).ToList();
        foreach (string language in requestedLanguages)
        {
            MissingCardImageSourceEntry[] entries = report.Entries.Where(value =>
                string.Equals(value.Language, language, StringComparison.OrdinalIgnoreCase)).ToArray();
            var summary = new MissingCardImageLanguageSummary
            {
                Language = language,
                MissingImageCount = entries.Length,
                AvailableAtSourceCount = Count(entries, "available-at-source"),
                SourceUnavailableCount = Count(entries, "source-unavailable"),
                SourceNotFoundCount = Count(entries, "source-not-found"),
                SourceNotDeclaredCount = Count(entries, "source-not-declared"),
                SourceCardMissingCount = Count(entries, "source-card-missing"),
                InvalidSourceCount = Count(entries, "invalid-source"),
                ProbeFailedCount = Count(entries, "probe-failed")
            };
            report.Languages.Add(summary);
            if (expected.TryGetValue(language, out MissingCardImageExpectation expectation) &&
                summary.MissingImageCount != expectation.MissingImageCount)
                report.Failures.Add($"Language '{language}' expected {expectation.MissingImageCount} " +
                                    $"missing images, found {summary.MissingImageCount}.");
        }
        report.MissingImageCount = report.Entries.Count;
        report.AvailableAtSourceCount = Count(report.Entries, "available-at-source");
        report.SourceUnavailableCount = Count(report.Entries, "source-unavailable");
        report.SourceNotFoundCount = Count(report.Entries, "source-not-found");
        report.SourceNotDeclaredCount = Count(report.Entries, "source-not-declared");
        report.SourceCardMissingCount = Count(report.Entries, "source-card-missing");
        report.InvalidSourceCount = Count(report.Entries, "invalid-source");
        report.ProbeFailedCount = Count(report.Entries, "probe-failed");
        if (report.Entries.Select(value => value.RecordId).Distinct(StringComparer.Ordinal).Count() !=
            report.MissingImageCount)
            report.Failures.Add("Missing image source report contains duplicate record IDs.");
        report.Failures = report.Failures.Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal).ToList();
        report.IsValid = report.Failures.Count == 0;
        report.SnapshotSha256 = Snapshot(report);

        if (!string.IsNullOrWhiteSpace(jsonOutputPath))
            WriteAtomic(jsonOutputPath, JsonConvert.SerializeObject(report, Formatting.Indented));
        if (!string.IsNullOrWhiteSpace(markdownOutputPath))
            WriteAtomic(markdownOutputPath, Markdown(report));
        if (!string.IsNullOrWhiteSpace(queueOutputPath))
            WriteAtomic(queueOutputPath, JsonConvert.SerializeObject(new
            {
                SchemaVersion = 1,
                SourceSnapshotSha256 = report.SnapshotSha256,
                Count = report.AvailableAtSourceCount,
                Entries = report.Entries.Where(value => value.Status == "available-at-source")
                    .OrderBy(value => value.RecordId, StringComparer.Ordinal).ToArray()
            }, Formatting.Indented));
        return report;
    }

    private static List<PendingCard> ReadMissingCards(
        string root,
        IReadOnlyList<string> languages,
        MissingCardImageSourceAuditReport report)
    {
        var result = new List<PendingCard>();
        foreach (string language in languages)
        {
            string languageRoot = Path.GetFullPath(Path.Combine(root, language));
            if (!Directory.Exists(languageRoot))
            {
                report.Failures.Add($"Language import directory is missing: {language}.");
                continue;
            }
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
                    report.Failures.Add($"Failed to read manifest '{manifestPath}': {exception.Message}");
                    continue;
                }
                if (manifest?.Set == null)
                {
                    report.Failures.Add($"Manifest or Set is missing: {manifestPath}");
                    continue;
                }
                foreach (ImportedCardRecord card in manifest.Cards ?? new List<ImportedCardRecord>())
                {
                    if (card == null || !string.IsNullOrWhiteSpace(card.ImageRelativePath)) continue;
                    if (string.IsNullOrWhiteSpace(card.Id) || string.IsNullOrWhiteSpace(card.LocalId))
                    {
                        report.Failures.Add($"Missing-image card has no ID/local ID: {manifestPath}");
                        continue;
                    }
                    string normalizedLanguage = NormalizeLanguage(manifest.Language) ?? language;
                    string relativeManifest = Relative(languageRoot, manifestPath);
                    result.Add(new PendingCard
                    {
                        RecordId = string.Join("|", normalizedLanguage, relativeManifest,
                            card.Id.Trim(), card.LocalId.Trim()),
                        Language = normalizedLanguage,
                        ManifestSource = RequiredOrFallback(manifest.Source, "unknown"),
                        SetId = RequiredOrFallback(manifest.Set.Id,
                            Path.GetFileName(Path.GetDirectoryName(manifestPath))),
                        CardId = card.Id.Trim(),
                        LocalId = card.LocalId.Trim(),
                        CardName = RequiredOrFallback(card.Name, card.Id.Trim()),
                        MetadataUrl = card.SourceUrl?.Trim(),
                        DeclaredImageUrl = card.ImageSourceUrl?.Trim()
                    });
                }
            }
        }
        return result;
    }

    private static void ProbeTcgdexSet(
        IReadOnlyList<PendingCard> cards,
        IMissingImageSourceClient client,
        MissingCardImageSourceAuditReport report)
    {
        string language = cards[0].Language;
        string setId = cards[0].SetId;
        string url = $"https://{TcgdexApiHost}/v2/{Uri.EscapeDataString(language)}/sets/" +
                     Uri.EscapeDataString(setId);
        MissingImageHttpResponse response;
        try
        {
            report.RemoteRequestCount++;
            response = client.Get(url);
        }
        catch (Exception exception)
        {
            report.Failures.Add($"TCGdex Set probe failed for {language}/{setId}: {exception.Message}");
            foreach (PendingCard card in cards)
                Add(report, card, url, null, "probe-failed", 0,
                    "The current TCGdex Set endpoint could not be read.");
            return;
        }
        if (response == null || response.StatusCode != (int)HttpStatusCode.OK)
        {
            int status = response?.StatusCode ?? 0;
            report.Failures.Add($"TCGdex Set probe returned HTTP {status} for {language}/{setId}.");
            foreach (PendingCard card in cards)
                Add(report, card, url, null, "probe-failed", status,
                    "The current TCGdex Set endpoint did not return HTTP 200.");
            return;
        }

        Dictionary<string, string> images;
        try
        {
            JObject set = JObject.Parse(response.Body ?? string.Empty);
            images = (set["cards"] as JArray ?? new JArray())
                .OfType<JObject>()
                .Where(value => !string.IsNullOrWhiteSpace(value.Value<string>("id")))
                .GroupBy(value => value.Value<string>("id"), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(value => value.Key,
                    value => value.First().Value<string>("image"),
                    StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is JsonException || exception is ArgumentException)
        {
            report.Failures.Add($"TCGdex Set response is invalid for {language}/{setId}: {exception.Message}");
            foreach (PendingCard card in cards)
                Add(report, card, url, null, "probe-failed", response.StatusCode,
                    "The current TCGdex Set response was not valid card JSON.");
            return;
        }

        foreach (PendingCard card in cards)
        {
            if (!images.TryGetValue(card.CardId, out string imageBase))
            {
                Add(report, card, url, null, "source-card-missing", response.StatusCode,
                    "The card no longer appears in the current TCGdex Set response.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(imageBase))
            {
                Add(report, card, url, null, "source-unavailable", response.StatusCode,
                    "TCGdex currently has no image field for this card.");
                continue;
            }
            if (!TryHttps(imageBase, new[] { TcgdexAssetHost }, out Uri imageUri))
            {
                Add(report, card, url, null, "invalid-source", response.StatusCode,
                    "TCGdex returned an image URL outside the approved HTTPS asset host.");
                continue;
            }
            string download = imageUri.AbsoluteUri.TrimEnd('/') + "/low.webp";
            Add(report, card, url, download, "available-at-source", response.StatusCode,
                "TCGdex now exposes an approved low WebP image source.");
        }
    }

    private static void ProbeDirectSource(
        PendingCard card,
        IMissingImageSourceClient client,
        MissingCardImageSourceAuditReport report)
    {
        if (string.IsNullOrWhiteSpace(card.DeclaredImageUrl))
        {
            Add(report, card, null, null, "source-not-declared", 0,
                "The manifest has no image source URL.");
            return;
        }
        if (!TryHttps(card.DeclaredImageUrl, DirectImageHosts, out Uri uri))
        {
            Add(report, card, card.DeclaredImageUrl, null, "invalid-source", 0,
                "The declared image source is not on an approved HTTPS host.");
            return;
        }
        MissingImageHttpResponse response;
        try
        {
            report.RemoteRequestCount++;
            response = client.Head(uri.AbsoluteUri);
        }
        catch (Exception exception)
        {
            report.Failures.Add($"Direct image probe failed for {card.RecordId}: {exception.Message}");
            Add(report, card, uri.AbsoluteUri, null, "probe-failed", 0,
                "The declared image source could not be reached.");
            return;
        }
        int status = response?.StatusCode ?? 0;
        if (status == (int)HttpStatusCode.OK &&
            !string.IsNullOrWhiteSpace(response.ContentType) &&
            response.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            Add(report, card, uri.AbsoluteUri, uri.AbsoluteUri, "available-at-source", status,
                "The approved image source returned an image response.");
        }
        else if (status == (int)HttpStatusCode.NotFound || status == (int)HttpStatusCode.Gone)
        {
            Add(report, card, uri.AbsoluteUri, null, "source-not-found", status,
                "The declared image source returned not found/gone.");
        }
        else
        {
            report.Failures.Add($"Direct image probe returned HTTP {status} for {card.RecordId}.");
            Add(report, card, uri.AbsoluteUri, null, "probe-failed", status,
                "The declared image source did not return a usable image response.");
        }
    }

    private static void Add(
        MissingCardImageSourceAuditReport report,
        PendingCard card,
        string probeUrl,
        string downloadUrl,
        string status,
        int httpStatus,
        string reason)
    {
        report.Entries.Add(new MissingCardImageSourceEntry
        {
            RecordId = card.RecordId,
            Language = card.Language,
            ManifestSource = card.ManifestSource,
            SetId = card.SetId,
            CardId = card.CardId,
            LocalId = card.LocalId,
            CardName = card.CardName,
            MetadataUrl = card.MetadataUrl,
            DeclaredImageUrl = card.DeclaredImageUrl,
            ProbeUrl = probeUrl,
            DownloadUrl = downloadUrl,
            Status = status,
            HttpStatus = httpStatus,
            Reason = reason
        });
    }

    private static bool TryHttps(string value, IEnumerable<string> hosts, out Uri uri)
    {
        uri = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri candidate) ||
            candidate.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(candidate.UserInfo))
            return false;
        if (!(hosts ?? Enumerable.Empty<string>()).Contains(candidate.IdnHost,
                StringComparer.OrdinalIgnoreCase))
            return false;
        uri = candidate;
        return true;
    }

    private static string Snapshot(MissingCardImageSourceAuditReport report)
    {
        string previousSnapshot = report.SnapshotSha256;
        string previousTime = report.CheckedAtUtc;
        report.SnapshotSha256 = null;
        report.CheckedAtUtc = null;
        string canonical = JsonConvert.SerializeObject(report, Formatting.None);
        report.SnapshotSha256 = previousSnapshot;
        report.CheckedAtUtc = previousTime;
        using SHA256 sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(canonical))
            .Select(value => value.ToString("x2")));
    }

    private static string Markdown(MissingCardImageSourceAuditReport report)
    {
        var text = new StringBuilder();
        text.AppendLine("# Missing card image source audit");
        text.AppendLine();
        text.AppendLine($"Valid: `{report.IsValid}`");
        text.AppendLine($"Checked at UTC: `{report.CheckedAtUtc}`");
        text.AppendLine($"Snapshot SHA-256: `{report.SnapshotSha256}`");
        text.AppendLine($"Remote requests: `{report.RemoteRequestCount}`");
        text.AppendLine();
        text.AppendLine("| Language | Missing | Available now | Unavailable | Not found | Not declared | Card missing | Invalid | Probe failed |");
        text.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (MissingCardImageLanguageSummary language in report.Languages)
            text.AppendLine($"| {language.Language} | {language.MissingImageCount} | " +
                            $"{language.AvailableAtSourceCount} | {language.SourceUnavailableCount} | " +
                            $"{language.SourceNotFoundCount} | {language.SourceNotDeclaredCount} | " +
                            $"{language.SourceCardMissingCount} | {language.InvalidSourceCount} | " +
                            $"{language.ProbeFailedCount} |");
        text.AppendLine();
        text.AppendLine("## Failures");
        text.AppendLine();
        if (report.Failures.Count == 0) text.AppendLine("- None.");
        else foreach (string failure in report.Failures) text.AppendLine("- " + failure);
        return text.ToString().Replace("\r\n", "\n");
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

    private static int Count(IEnumerable<MissingCardImageSourceEntry> entries, string status) =>
        entries.Count(value => value.Status == status);

    private static string NormalizeCheckedAt(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out DateTimeOffset parsed))
            throw new ArgumentException("Checked-at time must be ISO-8601.", nameof(value));
        return parsed.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }

    private static string NormalizeLanguage(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().Replace('_', '-').ToLowerInvariant();

    private static string RequiredOrFallback(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string Relative(string root, string path)
    {
        Uri rootUri = new Uri(Path.GetFullPath(root).TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar);
        return Uri.UnescapeDataString(rootUri.MakeRelativeUri(new Uri(Path.GetFullPath(path))).ToString())
            .Replace('\\', '/');
    }

    private static string Key(params string[] values) => string.Join("|",
        values.Select(value => value?.Trim().ToLowerInvariant() ?? string.Empty));

    private sealed class PendingCard
    {
        public string RecordId;
        public string Language;
        public string ManifestSource;
        public string SetId;
        public string CardId;
        public string LocalId;
        public string CardName;
        public string MetadataUrl;
        public string DeclaredImageUrl;
    }
}
