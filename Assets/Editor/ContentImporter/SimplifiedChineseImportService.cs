using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

[Serializable]
public sealed class SimplifiedChineseImportOptions
{
    public string OutputRoot;
    public int MaxConcurrency = 4;
    public int MaximumCardsPerSet;
    public bool RefreshExistingFiles;
    public bool DownloadImages = true;
    public int RequestIntervalMilliseconds = 25;
    public int MaximumAttempts = 5;
    public int RetryBaseDelayMilliseconds = 750;
}

[Serializable]
public sealed class SimplifiedChineseProductRecord
{
    public string SetId;
    public string Name;
    public string SetCode;
    public string ReleaseDate;
    public string Series;
    public bool MainExpansion;
    public int CardCount;
    public string GenerationId;
    public int GenerationOrder;
    public int SetOrdinal;
}

[Serializable]
public sealed class SimplifiedChineseSourceInventory
{
    public int SchemaVersion = 1;
    public string Source = "cryst-simplified-chinese";
    public string Language = "zh-cn";
    public string GeneratedAtUtc;
    public string ContentSha256;
    public int ProductCount;
    public int CardCount;
    public List<SimplifiedChineseProductRecord> Products = new List<SimplifiedChineseProductRecord>();
}

public sealed class SimplifiedChineseImportService : IDisposable
{
    public const string Language = "zh-cn";
    private const string DefaultApiRoot = "https://tcg.mik.moe/api/v3/card";
    private const string DefaultAssetRoot = "https://tcg.mik.moe/static/img";
    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;
    private readonly string apiRoot;
    private readonly string assetRoot;
    private readonly SemaphoreSlim requestGate = new SemaphoreSlim(1, 1);
    private DateTime nextRequestUtc = DateTime.MinValue;

    public SimplifiedChineseImportService()
        : this(CreateClient(), DefaultApiRoot, DefaultAssetRoot, true)
    {
    }

    internal SimplifiedChineseImportService(
        HttpClient httpClient,
        string apiRoot = DefaultApiRoot,
        string assetRoot = DefaultAssetRoot)
        : this(httpClient, apiRoot, assetRoot, false)
    {
    }

    private SimplifiedChineseImportService(
        HttpClient httpClient,
        string apiRoot,
        string assetRoot,
        bool ownsHttpClient)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.apiRoot = RequiredRoot(apiRoot, nameof(apiRoot));
        this.assetRoot = RequiredRoot(assetRoot, nameof(assetRoot));
        this.ownsHttpClient = ownsHttpClient;
    }

    public async Task<SimplifiedChineseSourceInventory> BuildInventoryAsync(
        string outputRoot = null,
        CancellationToken cancellationToken = default)
    {
        List<SimplifiedChineseProductRecord> products = await DiscoverProductsAsync(
            DefaultRequestOptions(), cancellationToken).ConfigureAwait(false);
        var inventory = new SimplifiedChineseSourceInventory
        {
            GeneratedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            ProductCount = products.Count,
            CardCount = products.Sum(value => value.CardCount),
            Products = products
        };
        inventory.ContentSha256 = InventoryHash(inventory);
        if (!string.IsNullOrWhiteSpace(outputRoot))
        {
            Directory.CreateDirectory(outputRoot);
            WriteTextAtomic(Path.Combine(outputRoot, "simplified-chinese-inventory.json"),
                JsonConvert.SerializeObject(inventory, Formatting.Indented));
            WriteTextAtomic(Path.Combine(outputRoot, "simplified-chinese-inventory.md"),
                InventoryMarkdown(inventory));
        }
        return inventory;
    }

    public async Task<ContentImportSummary> ImportAsync(
        IEnumerable<string> setIds,
        SimplifiedChineseImportOptions options,
        IProgress<ContentImportProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateOptions(options);
        Directory.CreateDirectory(options.OutputRoot);
        List<SimplifiedChineseProductRecord> products = await DiscoverProductsAsync(
            options, cancellationToken).ConfigureAwait(false);
        string[] requested = (setIds ?? Enumerable.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (requested.Length > 0)
        {
            var requestedSet = new HashSet<string>(requested, StringComparer.OrdinalIgnoreCase);
            string[] missing = requestedSet.Where(id => products.All(product =>
                    !string.Equals(product.SetId, id, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (missing.Length > 0)
                throw new InvalidDataException("Unknown Simplified Chinese Set IDs: " + string.Join(", ", missing));
            products = products.Where(product => requestedSet.Contains(product.SetId)).ToList();
        }

        var checkpoint = new ContentImportCheckpointStore(
            options.OutputRoot, Language, "source", options.DownloadImages ? "png" : "metadata");
        checkpoint.Begin(products.Select(value => value.SetId));
        var summary = new ContentImportSummary
        {
            CheckpointPath = checkpoint.CheckpointPath,
            FailureReportPath = checkpoint.FailureReportPath
        };
        foreach (SimplifiedChineseProductRecord product in products)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!options.RefreshExistingFiles && checkpoint.IsSetComplete(product.SetId))
            {
                summary.SkippedSetCount++;
                continue;
            }

            try
            {
                ImportSetOutcome outcome = await ImportSetAsync(
                    product, options, checkpoint, progress, cancellationToken).ConfigureAwait(false);
                summary.SetCount++;
                summary.CardCount += outcome.Manifest.Cards.Count;
                summary.ErrorCount += outcome.Manifest.Errors.Count;
                summary.ImageBytes += outcome.Manifest.Cards.Sum(value => value.ImageBytes);
                summary.ReusedMetadataCount += outcome.ReusedMetadataCount;
                summary.ReusedImageCount += outcome.ReusedImageCount;
            }
            catch (OperationCanceledException)
            {
                checkpoint.WriteFailureReport();
                throw;
            }
            catch (Exception exception)
            {
                summary.FailedSetCount++;
                summary.ErrorCount++;
                checkpoint.FailSet(product.SetId, exception.Message);
            }
        }
        checkpoint.WriteFailureReport();
        return summary;
    }

    internal async Task<List<SimplifiedChineseProductRecord>> DiscoverProductsAsync(
        SimplifiedChineseImportOptions options,
        CancellationToken cancellationToken)
    {
        JObject envelope = await PostJsonAsync(
            apiRoot + "/product-list", new JObject(), options, cancellationToken).ConfigureAwait(false);
        JArray list = envelope.SelectToken("data.list") as JArray ?? new JArray();
        List<SimplifiedChineseProductRecord> products = list
            .OfType<JObject>()
            .Select(MapProduct)
            .Where(value => !string.IsNullOrWhiteSpace(value.SetId))
            .GroupBy(value => value.SetId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(value => value.Name, StringComparer.Ordinal).First())
            .ToList();
        ApplyOrdering(products);
        return products.OrderBy(value => value.GenerationOrder)
            .ThenBy(value => value.SetOrdinal)
            .ThenBy(value => value.SetCode, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<ImportSetOutcome> ImportSetAsync(
        SimplifiedChineseProductRecord product,
        SimplifiedChineseImportOptions options,
        ContentImportCheckpointStore checkpoint,
        IProgress<ContentImportProgress> progress,
        CancellationToken cancellationToken)
    {
        string setDirectory = Path.Combine(options.OutputRoot, Language, product.SetId);
        string rawDirectory = Path.Combine(setDirectory, "raw");
        string rawCardsDirectory = Path.Combine(rawDirectory, "cards");
        string imagesDirectory = Path.Combine(setDirectory, "images");
        Directory.CreateDirectory(rawCardsDirectory);
        Directory.CreateDirectory(imagesDirectory);
        string rawSetPath = Path.Combine(rawDirectory, "set.json");
        JObject detail;
        int reusedMetadata = 0;
        if (!options.RefreshExistingFiles && File.Exists(rawSetPath))
        {
            detail = JObject.Parse(File.ReadAllText(rawSetPath, Encoding.UTF8));
            reusedMetadata++;
        }
        else
        {
            progress?.Report(new ContentImportProgress
            {
                SetId = product.SetId,
                Stage = "Downloading Simplified Chinese Set metadata",
                Total = 1
            });
            JObject envelope = await PostJsonAsync(
                apiRoot + "/product-detail",
                new JObject { ["setId"] = product.SetId },
                options,
                cancellationToken).ConfigureAwait(false);
            detail = envelope["data"] as JObject ?? throw new InvalidDataException(
                "Simplified Chinese product-detail response has no data object: " + product.SetId);
            WriteTextAtomic(rawSetPath, detail.ToString(Formatting.Indented));
        }

        JArray cards = detail["cards"] as JArray ?? new JArray();
        if (options.MaximumCardsPerSet > 0)
            cards = new JArray(cards.Take(options.MaximumCardsPerSet));
        checkpoint.StartSet(product.SetId, cards.Count);
        var manifest = new PrivateContentManifest
        {
            Source = "cryst-simplified-chinese",
            Language = Language,
            GeneratedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            Set = new ImportedSetRecord
            {
                Id = product.SetId,
                Name = product.Name,
                SetCode = product.SetCode,
                SeriesId = SeriesSlug(product.Series),
                SeriesName = product.Series,
                EraId = SeriesSlug(product.Series),
                GenerationId = product.GenerationId,
                GenerationOrder = product.GenerationOrder,
                SetOrdinal = product.SetOrdinal,
                ReleaseDate = product.ReleaseDate,
                OfficialCardCount = product.CardCount,
                TotalCardCount = product.CardCount,
                SourceUrl = apiRoot + "/product-detail",
                RawDataRelativePath = "raw/set.json"
            }
        };

        var records = new ImportedCardRecord[cards.Count];
        var errors = new ContentImportError[cards.Count];
        int completed = 0;
        int reusedCards = 0;
        int reusedImages = 0;
        using var semaphore = new SemaphoreSlim(options.MaxConcurrency);
        IEnumerable<Task> tasks = cards.Select(async (token, index) =>
        {
            JObject card = token as JObject ?? new JObject();
            string localId = Value(card, "cardIndex") ?? (index + 1).ToString("D3", CultureInfo.InvariantCulture);
            string cardId = product.SetCode + "-" + localId;
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            string failure = null;
            bool attemptFinished = false;
            try
            {
                CardImportOutcome result = await ImportCardAsync(
                    cardId, localId, product, card, rawCardsDirectory, imagesDirectory,
                    setDirectory, options, cancellationToken).ConfigureAwait(false);
                records[index] = result.Record;
                if (result.ReusedMetadata)
                    Interlocked.Increment(ref reusedCards);
                if (result.ReusedImage)
                    Interlocked.Increment(ref reusedImages);
                attemptFinished = true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                failure = exception.Message;
                errors[index] = new ContentImportError { ItemId = cardId, Message = failure };
                attemptFinished = true;
            }
            finally
            {
                if (attemptFinished)
                    checkpoint.RecordCard(product.SetId, cardId, failure);
                semaphore.Release();
                int current = Interlocked.Increment(ref completed);
                progress?.Report(new ContentImportProgress
                {
                    SetId = product.SetId,
                    Stage = options.DownloadImages ? "Downloading Simplified Chinese cards" : "Writing card metadata",
                    Completed = current,
                    Total = cards.Count
                });
            }
        });
        await Task.WhenAll(tasks).ConfigureAwait(false);
        manifest.Cards.AddRange(records.Where(value => value != null)
            .OrderBy(value => value.LocalId, NaturalIdComparer.Instance));
        manifest.Errors.AddRange(errors.Where(value => value != null));
        WriteTextAtomic(Path.Combine(setDirectory, "manifest.json"),
            JsonConvert.SerializeObject(manifest, Formatting.Indented));
        checkpoint.CompleteSet(product.SetId);
        return new ImportSetOutcome(
            manifest, reusedMetadata + reusedCards, reusedImages);
    }

    private async Task<CardImportOutcome> ImportCardAsync(
        string cardId,
        string localId,
        SimplifiedChineseProductRecord product,
        JObject card,
        string rawCardsDirectory,
        string imagesDirectory,
        string setDirectory,
        SimplifiedChineseImportOptions options,
        CancellationToken cancellationToken)
    {
        string rawPath = Path.Combine(rawCardsDirectory, SafeFileName(cardId) + ".json");
        bool reusedMetadata = !options.RefreshExistingFiles && File.Exists(rawPath);
        if (!reusedMetadata)
            WriteTextAtomic(rawPath, card.ToString(Formatting.Indented));
        string imageUrl = assetRoot + "/" + Uri.EscapeDataString(product.SetCode) + "/" +
                          Uri.EscapeDataString(localId) + ".png";
        string imageRelativePath = null;
        string imageHash = null;
        long imageBytes = 0;
        bool reusedImage = false;
        if (options.DownloadImages)
        {
            string imagePath = Path.Combine(imagesDirectory, SafeFileName(cardId) + ".png");
            byte[] bytes;
            if (!options.RefreshExistingFiles && File.Exists(imagePath))
            {
                bytes = File.ReadAllBytes(imagePath);
                reusedImage = true;
            }
            else
            {
                bytes = await GetBytesAsync(imageUrl, options, cancellationToken).ConfigureAwait(false);
                WriteBytesAtomic(imagePath, bytes);
            }
            imageRelativePath = RelativePath(setDirectory, imagePath);
            imageHash = Sha256(bytes);
            imageBytes = bytes.LongLength;
        }

        var record = new ImportedCardRecord
        {
            Id = cardId,
            LocalId = localId,
            Name = Value(card, "cardName") ?? cardId,
            Category = Value(card, "cardType") ?? "collectible",
            Rarity = Value(card, "rarity") ?? "unspecified",
            SourceUrl = apiRoot + "/product-detail",
            RawDataRelativePath = RelativePath(setDirectory, rawPath),
            ImageSourceUrl = options.DownloadImages ? imageUrl : null,
            ImageRelativePath = imageRelativePath,
            ImageSha256 = imageHash,
            ImageBytes = imageBytes,
            Variants = new ImportedCardVariants { Normal = true }
        };
        JArray traits = card["is"] as JArray;
        if (traits != null)
            record.Types.AddRange(traits.Values<string>().Where(value => !string.IsNullOrWhiteSpace(value)));
        string yorenCode = Value(card, "yorenCode");
        if (!string.IsNullOrWhiteSpace(yorenCode))
            record.Types.Add("subject:" + yorenCode.Trim());
        return new CardImportOutcome(record, reusedMetadata, reusedImage);
    }

    private async Task<JObject> PostJsonAsync(
        string url,
        JObject body,
        SimplifiedChineseImportOptions options,
        CancellationToken cancellationToken)
    {
        byte[] bytes = await SendWithRetryAsync(
            url,
            () => new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json"),
            options,
            cancellationToken).ConfigureAwait(false);
        JObject envelope = JObject.Parse(Encoding.UTF8.GetString(bytes));
        if (envelope["code"]?.Value<int>() != 200)
            throw new InvalidDataException(
                $"Simplified Chinese source returned code {envelope["code"]}: {envelope["msg"]}");
        return envelope;
    }

    private Task<byte[]> GetBytesAsync(
        string url,
        SimplifiedChineseImportOptions options,
        CancellationToken cancellationToken) =>
        SendWithRetryAsync(url, null, options, cancellationToken);

    private async Task<byte[]> SendWithRetryAsync(
        string url,
        Func<HttpContent> contentFactory,
        SimplifiedChineseImportOptions options,
        CancellationToken cancellationToken)
    {
        Exception last = null;
        for (int attempt = 1; attempt <= options.MaximumAttempts; attempt++)
        {
            try
            {
                await AwaitRequestSlotAsync(options.RequestIntervalMilliseconds, cancellationToken)
                    .ConfigureAwait(false);
                using var request = new HttpRequestMessage(
                    contentFactory == null ? HttpMethod.Get : HttpMethod.Post, url);
                if (contentFactory != null)
                    request.Content = contentFactory();
                using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken)
                    .ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                last = exception;
                if (attempt < options.MaximumAttempts)
                    await Task.Delay(attempt * options.RetryBaseDelayMilliseconds, cancellationToken)
                        .ConfigureAwait(false);
            }
        }
        throw new HttpRequestException(
            $"Failed to download '{url}' after {options.MaximumAttempts} attempts.", last);
    }

    private async Task AwaitRequestSlotAsync(int milliseconds, CancellationToken cancellationToken)
    {
        if (milliseconds <= 0)
            return;
        await requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            TimeSpan delay = nextRequestUtc - DateTime.UtcNow;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            nextRequestUtc = DateTime.UtcNow.AddMilliseconds(milliseconds);
        }
        finally
        {
            requestGate.Release();
        }
    }

    private static SimplifiedChineseProductRecord MapProduct(JObject value)
    {
        return new SimplifiedChineseProductRecord
        {
            SetId = Value(value, "setId"),
            Name = Value(value, "name"),
            SetCode = Value(value, "setCode") ?? Value(value, "setId"),
            ReleaseDate = NormalizeDate(Value(value, "releaseDate")),
            Series = Value(value, "series"),
            MainExpansion = value["mainExpansion"]?.Value<bool>() ?? false,
            CardCount = value["cardsNum"]?.Value<int>() ?? 0
        };
    }

    private static void ApplyOrdering(IList<SimplifiedChineseProductRecord> products)
    {
        foreach (SimplifiedChineseProductRecord product in products)
        {
            product.GenerationOrder = GenerationOrder(product.Series);
            product.GenerationId = "generation-" + product.GenerationOrder;
        }
        foreach (IGrouping<int, SimplifiedChineseProductRecord> generation in
                 products.GroupBy(value => value.GenerationOrder))
        {
            int ordinal = 1;
            foreach (SimplifiedChineseProductRecord product in generation
                         .OrderBy(value => SortDate(value.ReleaseDate))
                         .ThenBy(value => value.SetCode, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(value => value.SetId, StringComparer.OrdinalIgnoreCase))
                product.SetOrdinal = ordinal++;
        }
    }

    private static int GenerationOrder(string series)
    {
        if (string.Equals(series, "Sun & Moon", StringComparison.OrdinalIgnoreCase))
            return 7;
        if (string.Equals(series, "Sword & Shield", StringComparison.OrdinalIgnoreCase))
            return 8;
        if (string.Equals(series, "Scarlet & Violet", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(series, "30th", StringComparison.OrdinalIgnoreCase))
            return 9;
        throw new InvalidDataException("Unknown Simplified Chinese card series: " + series);
    }

    private static DateTime SortDate(string date) =>
        DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out DateTime parsed) ? parsed : DateTime.MaxValue;

    private static string NormalizeDate(string date)
    {
        if (!DateTimeOffset.TryParse(date, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out DateTimeOffset parsed) || parsed.Year <= 1)
            return null;
        return parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static string SeriesSlug(string value)
    {
        var builder = new StringBuilder();
        bool separator = false;
        foreach (char character in (value ?? "unknown").Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                separator = false;
            }
            else if (!separator && builder.Length > 0)
            {
                builder.Append('-');
                separator = true;
            }
        }
        return builder.ToString().Trim('-');
    }

    internal static string InventoryHash(SimplifiedChineseSourceInventory inventory)
    {
        var canonical = new JObject
        {
            ["source"] = inventory.Source,
            ["language"] = inventory.Language,
            ["products"] = JArray.FromObject(inventory.Products.OrderBy(value => value.GenerationOrder)
                .ThenBy(value => value.SetOrdinal)
                .ThenBy(value => value.SetId, StringComparer.Ordinal))
        };
        return Sha256(new UTF8Encoding(false).GetBytes(canonical.ToString(Formatting.None)));
    }

    private static string InventoryMarkdown(SimplifiedChineseSourceInventory inventory)
    {
        var text = new StringBuilder();
        text.AppendLine("# Simplified Chinese private source inventory");
        text.AppendLine();
        text.AppendLine($"Generated: `{inventory.GeneratedAtUtc}`");
        text.AppendLine($"Products: **{inventory.ProductCount}**");
        text.AppendLine($"Cards: **{inventory.CardCount}**");
        text.AppendLine($"Content hash: `{inventory.ContentSha256}`");
        text.AppendLine();
        text.AppendLine("| Generation | Series | Products | Cards |");
        text.AppendLine("|---:|---|---:|---:|");
        foreach (IGrouping<string, SimplifiedChineseProductRecord> group in inventory.Products
                     .GroupBy(value => value.Series)
                     .OrderBy(value => value.Min(product => product.GenerationOrder)))
            text.AppendLine($"| {group.Min(value => value.GenerationOrder)} | {group.Key} | " +
                            $"{group.Count()} | {group.Sum(value => value.CardCount)} |");
        return text.ToString();
    }

    private static void ValidateOptions(SimplifiedChineseImportOptions options)
    {
        if (options == null)
            throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.OutputRoot))
            throw new ArgumentException("OutputRoot is required.", nameof(options));
        if (options.MaxConcurrency < 1 || options.MaxConcurrency > 32)
            throw new ArgumentOutOfRangeException(nameof(options.MaxConcurrency));
        if (options.MaximumCardsPerSet < 0)
            throw new ArgumentOutOfRangeException(nameof(options.MaximumCardsPerSet));
        if (options.RequestIntervalMilliseconds < 0 || options.RequestIntervalMilliseconds > 10000)
            throw new ArgumentOutOfRangeException(nameof(options.RequestIntervalMilliseconds));
        if (options.MaximumAttempts < 1 || options.MaximumAttempts > 10)
            throw new ArgumentOutOfRangeException(nameof(options.MaximumAttempts));
        if (options.RetryBaseDelayMilliseconds < 0 || options.RetryBaseDelayMilliseconds > 60000)
            throw new ArgumentOutOfRangeException(nameof(options.RetryBaseDelayMilliseconds));
    }

    private static SimplifiedChineseImportOptions DefaultRequestOptions() =>
        new SimplifiedChineseImportOptions { OutputRoot = Path.GetTempPath() };

    private static string RequiredRoot(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException(name + " is required.", name);
        return value.TrimEnd('/');
    }

    private static string Value(JToken token, string propertyName) =>
        token?[propertyName]?.Type == JTokenType.Null ? null : token?[propertyName]?.ToString();

    private static string SafeFileName(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
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

    private static void WriteTextAtomic(string path, string content) =>
        WriteBytesAtomic(path, new UTF8Encoding(false).GetBytes(content));

    private static void WriteBytesAtomic(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
        string temporary = path + ".download";
        File.WriteAllBytes(temporary, bytes);
        if (File.Exists(path))
            File.Delete(path);
        File.Move(temporary, path);
    }

    private static HttpClient CreateClient()
    {
        ServicePointManager.DefaultConnectionLimit = Math.Max(
            ServicePointManager.DefaultConnectionLimit, 32);
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "UniversalGachaSimulator-PrivateImporter/3.0");
        return client;
    }

    public void Dispose()
    {
        requestGate.Dispose();
        if (ownsHttpClient)
            httpClient.Dispose();
    }

    private sealed class CardImportOutcome
    {
        public CardImportOutcome(ImportedCardRecord record, bool reusedMetadata, bool reusedImage)
        {
            Record = record;
            ReusedMetadata = reusedMetadata;
            ReusedImage = reusedImage;
        }

        public ImportedCardRecord Record { get; }
        public bool ReusedMetadata { get; }
        public bool ReusedImage { get; }
    }

    private sealed class ImportSetOutcome
    {
        public ImportSetOutcome(
            PrivateContentManifest manifest,
            int reusedMetadataCount,
            int reusedImageCount)
        {
            Manifest = manifest;
            ReusedMetadataCount = reusedMetadataCount;
            ReusedImageCount = reusedImageCount;
        }

        public PrivateContentManifest Manifest { get; }
        public int ReusedMetadataCount { get; }
        public int ReusedImageCount { get; }
    }

    private sealed class NaturalIdComparer : IComparer<string>
    {
        public static readonly NaturalIdComparer Instance = new NaturalIdComparer();

        public int Compare(string left, string right)
        {
            if (int.TryParse(left, NumberStyles.Integer, CultureInfo.InvariantCulture, out int leftNumber) &&
                int.TryParse(right, NumberStyles.Integer, CultureInfo.InvariantCulture, out int rightNumber))
                return leftNumber.CompareTo(rightNumber);
            return StringComparer.Ordinal.Compare(left, right);
        }
    }
}
