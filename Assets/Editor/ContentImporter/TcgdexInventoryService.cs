using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public sealed class TcgdexInventoryService : IDisposable
{
    public const string DefaultApiRoot = "https://api.tcgdex.net/v2";
    public static readonly IReadOnlyList<string> SupportedLanguages = new[]
    {
        "de", "en", "es", "fr", "id", "it", "ja", "ko", "nl", "pl", "pt", "pt-br",
        "pt-pt", "ru", "th", "zh-cn", "zh-tw"
    };

    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;
    private readonly string apiRoot;

    public TcgdexInventoryService()
        : this(CreateClient(), DefaultApiRoot, true)
    {
    }

    internal TcgdexInventoryService(HttpClient httpClient, string apiRoot = DefaultApiRoot)
        : this(httpClient, apiRoot, false)
    {
    }

    private TcgdexInventoryService(HttpClient httpClient, string apiRoot, bool ownsHttpClient)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.apiRoot = (apiRoot ?? throw new ArgumentNullException(nameof(apiRoot))).TrimEnd('/');
        this.ownsHttpClient = ownsHttpClient;
    }

    public async Task<ContentInventorySnapshot> BuildAsync(
        ContentInventoryOptions options,
        IProgress<ContentInventoryProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateOptions(options);
        List<string> languages = NormalizeLanguages(options.Languages);
        HashSet<string> detailedLanguages = new HashSet<string>(
            NormalizeLanguages(options.DetailedLanguages), StringComparer.Ordinal);
        string referenceLanguage = options.ReferenceLanguage.Trim().ToLowerInvariant();
        if (!languages.Contains(referenceLanguage))
            languages.Add(referenceLanguage);
        languages.Sort(StringComparer.Ordinal);
        detailedLanguages.Add(referenceLanguage);

        PokemonSetGenerationOverrideCatalog overrides =
            PokemonContentOverrideLoader.LoadOptionalSetGeneration(options.SetGenerationOverridesPath);
        var snapshot = new ContentInventorySnapshot
        {
            ApiRoot = apiRoot,
            ReferenceLanguage = referenceLanguage,
            GeneratedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
        };
        var detailCandidates = new List<ImageCandidate>();

        for (int languageIndex = 0; languageIndex < languages.Count; languageIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string language = languages[languageIndex];
            progress?.Report(new ContentInventoryProgress
            {
                Stage = "Discovering languages",
                ItemId = language,
                Completed = languageIndex,
                Total = languages.Count
            });

            var languageRecord = new ContentInventoryLanguageRecord
            {
                Language = language,
                Detailed = detailedLanguages.Contains(language)
            };
            snapshot.Languages.Add(languageRecord);

            JArray briefs;
            try
            {
                briefs = JArray.Parse(await GetStringWithRetryAsync(
                    $"{apiRoot}/{language}/sets", cancellationToken).ConfigureAwait(false));
            }
            catch (Exception exception) when (!(exception is OperationCanceledException))
            {
                snapshot.Errors.Add(Error("language", language, exception));
                if (language == referenceLanguage)
                    throw new ContentInventoryException(
                        $"Reference language '{referenceLanguage}' could not be discovered.", exception);
                continue;
            }

            List<JObject> uniqueBriefs = NormalizeAndSortBriefs(briefs, language, snapshot.Errors);
            languageRecord.SetCount = uniqueBriefs.Count;
            languageRecord.OfficialCardCount =
                uniqueBriefs.Sum(item => IntValue(item.SelectToken("cardCount.official")));
            languageRecord.TotalCardCount =
                uniqueBriefs.Sum(item => IntValue(item.SelectToken("cardCount.total")));
            languageRecord.SetLogoCount = uniqueBriefs.Count(item => HasText(item["logo"]));
            languageRecord.SetSymbolCount = uniqueBriefs.Count(item => HasText(item["symbol"]));

            if (!languageRecord.Detailed)
                continue;

            await AddDetailedSetsAsync(
                language, uniqueBriefs, languageRecord, snapshot, detailCandidates, overrides,
                options.MaxConcurrency, progress, cancellationToken).ConfigureAwait(false);
        }

        snapshot.Languages = snapshot.Languages
            .OrderBy(item => item.Language, StringComparer.Ordinal)
            .ToList();
        snapshot.Sets = snapshot.Sets
            .OrderBy(item => item.Language, StringComparer.Ordinal)
            .ThenBy(item => item.ReleaseDate ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToList();
        snapshot.Errors = snapshot.Errors
            .OrderBy(item => item.Scope, StringComparer.Ordinal)
            .ThenBy(item => item.ItemId, StringComparer.Ordinal)
            .ThenBy(item => item.Message, StringComparer.Ordinal)
            .ToList();

        await MeasureImagesAsync(
            snapshot, detailCandidates, options.ImageSampleCount, options.MaxConcurrency,
            progress, cancellationToken).ConfigureAwait(false);

        snapshot.Errors = snapshot.Errors
            .OrderBy(item => item.Scope, StringComparer.Ordinal)
            .ThenBy(item => item.ItemId, StringComparer.Ordinal)
            .ThenBy(item => item.Message, StringComparer.Ordinal)
            .ToList();

        snapshot.ContentSha256 = ComputeContentHash(snapshot);
        WriteReports(options.OutputRoot, snapshot);
        return snapshot;
    }

    private async Task AddDetailedSetsAsync(
        string language,
        IReadOnlyList<JObject> briefs,
        ContentInventoryLanguageRecord languageRecord,
        ContentInventorySnapshot snapshot,
        List<ImageCandidate> imageCandidates,
        PokemonSetGenerationOverrideCatalog overrides,
        int maxConcurrency,
        IProgress<ContentInventoryProgress> progress,
        CancellationToken cancellationToken)
    {
        var results = new ContentInventorySetRecord[briefs.Count];
        var candidates = new List<ImageCandidate>[briefs.Count];
        var errors = new ContentInventoryError[briefs.Count];
        int completed = 0;
        using var semaphore = new SemaphoreSlim(maxConcurrency);

        IEnumerable<Task> tasks = briefs.Select(async (brief, index) =>
        {
            string setId = Value(brief, "id");
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                string sourceUrl = $"{apiRoot}/{language}/sets/{Uri.EscapeDataString(setId)}";
                JObject detail = JObject.Parse(await GetStringWithRetryAsync(
                    sourceUrl, cancellationToken).ConfigureAwait(false));
                ImportedSetRecord mapped = TcgdexImportService.MapSet(detail, sourceUrl);
                bool wasMapped = overrides.Apply(mapped);
                JArray cards = detail["cards"] as JArray ?? new JArray();
                results[index] = new ContentInventorySetRecord
                {
                    Language = language,
                    Id = mapped.Id,
                    Name = mapped.Name,
                    SetCode = mapped.SetCode,
                    SeriesId = mapped.SeriesId,
                    SeriesName = mapped.SeriesName,
                    EraId = mapped.EraId,
                    GenerationId = mapped.GenerationId,
                    GenerationOrder = mapped.GenerationOrder,
                    SetOrdinal = mapped.SetOrdinal,
                    ReleaseDate = mapped.ReleaseDate,
                    OfficialCardCount = mapped.OfficialCardCount,
                    TotalCardCount = mapped.TotalCardCount,
                    CardEntryCount = cards.Count,
                    CardImageCount = cards.Count(card => HasText(card["image"])),
                    LogoUrl = Value(brief, "logo"),
                    SymbolUrl = Value(brief, "symbol"),
                    SourceUrl = sourceUrl
                };
                candidates[index] = cards
                    .Where(card => HasText(card["image"]))
                    .Select(card => new ImageCandidate(
                        Value(card, "id"), Value(card, "image")))
                    .ToList();
                if (wasMapped)
                    Interlocked.Increment(ref languageRecord.MappedSetCount);
                else
                    Interlocked.Increment(ref languageRecord.UnmappedSetCount);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                errors[index] = Error("set-detail", $"{language}:{setId}", exception);
            }
            finally
            {
                semaphore.Release();
                int current = Interlocked.Increment(ref completed);
                progress?.Report(new ContentInventoryProgress
                {
                    Stage = "Reading set metadata",
                    ItemId = $"{language}:{setId}",
                    Completed = current,
                    Total = briefs.Count
                });
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
        foreach (ContentInventorySetRecord result in results.Where(item => item != null))
        {
            snapshot.Sets.Add(result);
            languageRecord.DetailedSetCount++;
            languageRecord.CardEntryCount += result.CardEntryCount;
            languageRecord.CardImageCount += result.CardImageCount;
        }
        foreach (List<ImageCandidate> setCandidates in candidates.Where(item => item != null))
            imageCandidates.AddRange(setCandidates);
        snapshot.Errors.AddRange(errors.Where(item => item != null));
    }

    private async Task MeasureImagesAsync(
        ContentInventorySnapshot snapshot,
        IReadOnlyList<ImageCandidate> candidates,
        int requestedCount,
        int maxConcurrency,
        IProgress<ContentInventoryProgress> progress,
        CancellationToken cancellationToken)
    {
        ContentInventoryImageEstimate estimate = snapshot.ImageEstimate;
        estimate.RequestedSampleCount = requestedCount;
        if (requestedCount == 0 || candidates.Count == 0)
            return;

        List<ImageCandidate> samples = SelectEvenSamples(candidates, requestedCount);
        var highBytes = new long[samples.Count];
        var lowBytes = new long[samples.Count];
        var errors = new ContentInventoryError[samples.Count];
        int completed = 0;
        using var semaphore = new SemaphoreSlim(maxConcurrency);
        IEnumerable<Task> tasks = samples.Select(async (candidate, index) =>
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                byte[] high = await GetBytesWithRetryAsync(
                    candidate.BaseUrl + "/high.jpg", cancellationToken).ConfigureAwait(false);
                byte[] low = await GetBytesWithRetryAsync(
                    candidate.BaseUrl + "/low.webp", cancellationToken).ConfigureAwait(false);
                highBytes[index] = high.LongLength;
                lowBytes[index] = low.LongLength;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                errors[index] = Error("image-sample", candidate.Id, exception);
            }
            finally
            {
                semaphore.Release();
                int current = Interlocked.Increment(ref completed);
                progress?.Report(new ContentInventoryProgress
                {
                    Stage = "Measuring image samples",
                    ItemId = candidate.Id,
                    Completed = current,
                    Total = samples.Count
                });
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
        estimate.CompletedSampleCount = highBytes.Count(value => value > 0);
        estimate.HighJpegBytes = highBytes.Sum();
        estimate.LowWebpBytes = lowBytes.Sum();
        if (estimate.CompletedSampleCount > 0)
        {
            estimate.AverageHighJpegBytes = estimate.HighJpegBytes / estimate.CompletedSampleCount;
            estimate.AverageLowWebpBytes = estimate.LowWebpBytes / estimate.CompletedSampleCount;
            long images = snapshot.Languages.Sum(item => (long)item.CardImageCount);
            estimate.ProjectedHighJpegBytes = SaturatingMultiply(estimate.AverageHighJpegBytes, images);
            estimate.ProjectedLowWebpBytes = SaturatingMultiply(estimate.AverageLowWebpBytes, images);
        }
        snapshot.Errors.AddRange(errors.Where(item => item != null));
    }

    internal static List<ImageCandidate> SelectEvenSamples(
        IReadOnlyList<ImageCandidate> candidates, int requestedCount)
    {
        List<ImageCandidate> sorted = candidates
            .Where(item => item != null && !string.IsNullOrWhiteSpace(item.BaseUrl))
            .GroupBy(item => item.Id ?? item.BaseUrl, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .ToList();
        int count = Math.Min(Math.Max(0, requestedCount), sorted.Count);
        if (count == 0)
            return new List<ImageCandidate>();
        if (count == 1)
            return new List<ImageCandidate> { sorted[0] };

        var result = new List<ImageCandidate>(count);
        for (int index = 0; index < count; index++)
        {
            int selected = (int)((long)index * (sorted.Count - 1) / (count - 1));
            result.Add(sorted[selected]);
        }
        return result;
    }

    internal static string ComputeContentHash(ContentInventorySnapshot snapshot)
    {
        string generatedAt = snapshot.GeneratedAtUtc;
        string hash = snapshot.ContentSha256;
        snapshot.GeneratedAtUtc = string.Empty;
        snapshot.ContentSha256 = string.Empty;
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(snapshot, Formatting.None));
            using SHA256 sha = SHA256.Create();
            return string.Concat(sha.ComputeHash(bytes).Select(value => value.ToString("x2")));
        }
        finally
        {
            snapshot.GeneratedAtUtc = generatedAt;
            snapshot.ContentSha256 = hash;
        }
    }

    internal static void WriteReports(string outputRoot, ContentInventorySnapshot snapshot)
    {
        Directory.CreateDirectory(outputRoot);
        WriteTextAtomic(Path.Combine(outputRoot, "tcgdex-inventory.json"),
            JsonConvert.SerializeObject(snapshot, Formatting.Indented));
        WriteTextAtomic(Path.Combine(outputRoot, "tcgdex-inventory.md"), BuildMarkdown(snapshot));
    }

    internal static string BuildMarkdown(ContentInventorySnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# TCGdex metadata inventory");
        builder.AppendLine();
        builder.AppendLine($"Generated: {snapshot.GeneratedAtUtc}");
        builder.AppendLine($"Content SHA-256: `{snapshot.ContentSha256}`");
        builder.AppendLine($"Reference language: `{snapshot.ReferenceLanguage}`");
        builder.AppendLine();
        builder.AppendLine("| Language | Sets | Total cards | Official cards | Detailed sets | Card image coverage | Mapped / unmapped |");
        builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
        foreach (ContentInventoryLanguageRecord language in snapshot.Languages)
        {
            string coverage = language.CardEntryCount == 0
                ? "n/a"
                : $"{language.CardImageCount}/{language.CardEntryCount} ({(100d * language.CardImageCount / language.CardEntryCount):F1}%)";
            builder.AppendLine(
                $"| `{language.Language}` | {language.SetCount} | {language.TotalCardCount} | " +
                $"{language.OfficialCardCount} | {language.DetailedSetCount} | {coverage} | " +
                $"{language.MappedSetCount} / {language.UnmappedSetCount} |");
        }

        ContentInventoryImageEstimate estimate = snapshot.ImageEstimate;
        builder.AppendLine();
        builder.AppendLine("## Image sample estimate");
        builder.AppendLine();
        builder.AppendLine($"- Samples: {estimate.CompletedSampleCount}/{estimate.RequestedSampleCount}");
        builder.AppendLine($"- Average high JPG: {FormatBytes(estimate.AverageHighJpegBytes)}");
        builder.AppendLine($"- Average low WebP: {FormatBytes(estimate.AverageLowWebpBytes)}");
        builder.AppendLine($"- Projected high JPG: {FormatBytes(estimate.ProjectedHighJpegBytes)}");
        builder.AppendLine($"- Projected low WebP: {FormatBytes(estimate.ProjectedLowWebpBytes)}");
        builder.AppendLine();
        builder.AppendLine($"Errors: {snapshot.Errors.Count}");
        return builder.ToString();
    }

    private static List<JObject> NormalizeAndSortBriefs(
        JArray briefs, string language, ICollection<ContentInventoryError> errors)
    {
        var byId = new Dictionary<string, List<JObject>>(StringComparer.Ordinal);
        foreach (JToken token in briefs)
        {
            if (!(token is JObject item) || string.IsNullOrWhiteSpace(Value(item, "id")))
                throw new ContentInventoryException($"Language '{language}' returned a Set without an ID.");
            string id = Value(item, "id").Trim();
            if (!byId.TryGetValue(id, out List<JObject> matches))
            {
                matches = new List<JObject>();
                byId.Add(id, matches);
            }
            matches.Add(item);
        }

        var result = new List<JObject>(byId.Count);
        foreach (KeyValuePair<string, List<JObject>> pair in byId.OrderBy(
                     item => item.Key, StringComparer.Ordinal))
        {
            if (pair.Value.Count > 1)
            {
                errors.Add(new ContentInventoryError
                {
                    Scope = "set-list-duplicate",
                    ItemId = $"{language}:{pair.Key}",
                    Message = $"Source returned {pair.Value.Count} entries; inventory kept one canonical entry."
                });
            }
            result.Add(pair.Value
                .OrderBy(item => item.ToString(Formatting.None), StringComparer.Ordinal)
                .First());
        }
        return result;
    }

    private static List<string> NormalizeLanguages(IEnumerable<string> languages)
    {
        var result = (languages ?? Enumerable.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        foreach (string language in result)
            if (!SupportedLanguages.Contains(language))
                throw new ArgumentException($"Unsupported TCGdex language '{language}'.");
        return result;
    }

    private static void ValidateOptions(ContentInventoryOptions options)
    {
        if (options == null)
            throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.OutputRoot))
            throw new ArgumentException("OutputRoot is required.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.ReferenceLanguage))
            throw new ArgumentException("ReferenceLanguage is required.", nameof(options));
        if (options.MaxConcurrency < 1 || options.MaxConcurrency > 12)
            throw new ArgumentOutOfRangeException(nameof(options.MaxConcurrency));
        if (options.ImageSampleCount < 0 || options.ImageSampleCount > 100)
            throw new ArgumentOutOfRangeException(nameof(options.ImageSampleCount));
    }

    private async Task<string> GetStringWithRetryAsync(string url, CancellationToken cancellationToken)
    {
        byte[] bytes = await GetBytesWithRetryAsync(url, cancellationToken).ConfigureAwait(false);
        return Encoding.UTF8.GetString(bytes);
    }

    private async Task<byte[]> GetBytesWithRetryAsync(string url, CancellationToken cancellationToken)
    {
        Exception lastException = null;
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using HttpResponseMessage response = await httpClient
                    .GetAsync(url, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                lastException = exception;
                if (attempt < 3)
                    await Task.Delay(attempt * 750, cancellationToken).ConfigureAwait(false);
            }
        }
        throw new HttpRequestException($"Failed to read '{url}' after 3 attempts.", lastException);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "UniversalGachaSimulator-InventoryAudit/1.0");
        return client;
    }

    private static ContentInventoryError Error(string scope, string itemId, Exception exception)
    {
        return new ContentInventoryError
        {
            Scope = scope,
            ItemId = itemId,
            Message = exception.Message
        };
    }

    private static string Value(JToken token, string propertyName)
    {
        JToken value = token?[propertyName];
        return value == null || value.Type == JTokenType.Null ? null : value.ToString();
    }

    private static int IntValue(JToken token)
    {
        return token == null ? 0 : token.Value<int>();
    }

    private static bool HasText(JToken token)
    {
        return token != null && token.Type != JTokenType.Null && !string.IsNullOrWhiteSpace(token.ToString());
    }

    private static long SaturatingMultiply(long left, long right)
    {
        if (left <= 0 || right <= 0)
            return 0;
        return left > long.MaxValue / right ? long.MaxValue : left * right;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
            return "0 B";
        string[] units = { "B", "KiB", "MiB", "GiB", "TiB" };
        double value = bytes;
        int unit = 0;
        while (value >= 1024d && unit < units.Length - 1)
        {
            value /= 1024d;
            unit++;
        }
        return $"{value:F2} {units[unit]}";
    }

    private static void WriteTextAtomic(string path, string text)
    {
        string temporaryPath = path + ".download";
        File.WriteAllText(temporaryPath, text, new UTF8Encoding(false));
        if (File.Exists(path))
            File.Delete(path);
        File.Move(temporaryPath, path);
    }

    public void Dispose()
    {
        if (ownsHttpClient)
            httpClient.Dispose();
    }

    internal sealed class ImageCandidate
    {
        public ImageCandidate(string id, string baseUrl)
        {
            Id = id;
            BaseUrl = baseUrl;
        }

        public string Id { get; }
        public string BaseUrl { get; }
    }
}

public sealed class ContentInventoryException : Exception
{
    public ContentInventoryException(string message) : base(message) { }
    public ContentInventoryException(string message, Exception innerException) : base(message, innerException) { }
}
