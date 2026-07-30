using System;
using System.Collections.Generic;
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

public sealed class TcgdexImportService : IDisposable
{
    private const string ApiRoot = "https://api.tcgdex.net/v2";
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly string _apiRoot;
    private readonly SemaphoreSlim _requestGate = new SemaphoreSlim(1, 1);
    private DateTime _nextRequestUtc = DateTime.MinValue;

    public TcgdexImportService()
        : this(CreateClient(), ApiRoot, true)
    {
    }

    internal TcgdexImportService(HttpClient httpClient, string apiRoot = ApiRoot)
        : this(httpClient, apiRoot, false)
    {
    }

    private TcgdexImportService(HttpClient httpClient, string apiRoot, bool ownsHttpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _apiRoot = (apiRoot ?? throw new ArgumentNullException(nameof(apiRoot))).TrimEnd('/');
        _ownsHttpClient = ownsHttpClient;
    }

    public async Task<ContentImportSummary> ImportSetsAsync(
        IEnumerable<string> setIds,
        ContentImportOptions options,
        IProgress<ContentImportProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateOptions(options);
        Directory.CreateDirectory(options.OutputRoot);
        PokemonSetGenerationOverrideCatalog setOverrides =
            PokemonContentOverrideLoader.LoadOptionalSetGeneration(options.SetGenerationOverridesPath);
        List<string> normalizedSetIds = setIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var checkpoint = new ContentImportCheckpointStore(
            options.OutputRoot, options.Language, options.ImageQuality, options.ImageExtension);
        checkpoint.Begin(normalizedSetIds);

        var summary = new ContentImportSummary
        {
            CheckpointPath = checkpoint.CheckpointPath,
            FailureReportPath = checkpoint.FailureReportPath
        };
        foreach (string setId in normalizedSetIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!options.RefreshExistingFiles && checkpoint.IsSetComplete(setId))
            {
                summary.SkippedSetCount++;
                continue;
            }
            try
            {
                ImportSetOutcome outcome = await ImportSetAsync(
                        setId, options, setOverrides, checkpoint, progress, cancellationToken)
                    .ConfigureAwait(false);
                summary.SetCount++;
                summary.CardCount += outcome.Manifest.Cards.Count;
                summary.ErrorCount += outcome.Manifest.Errors.Count;
                summary.ImageBytes += outcome.Manifest.Cards.Sum(card => card.ImageBytes);
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
                checkpoint.FailSet(setId, exception.Message);
            }
        }

        checkpoint.WriteFailureReport();
        return summary;
    }

    private async Task<ImportSetOutcome> ImportSetAsync(
        string setId,
        ContentImportOptions options,
        PokemonSetGenerationOverrideCatalog setOverrides,
        ContentImportCheckpointStore checkpoint,
        IProgress<ContentImportProgress> progress,
        CancellationToken cancellationToken)
    {
        string setUrl = $"{_apiRoot}/{options.Language}/sets/{Uri.EscapeDataString(setId)}";
        progress?.Report(new ContentImportProgress
        {
            SetId = setId,
            Stage = "Downloading set metadata",
            Total = 1
        });

        string setJson = await GetStringWithRetryAsync(setUrl, options, cancellationToken)
            .ConfigureAwait(false);
        JObject setObject = JObject.Parse(setJson);
        string actualSetId = Value(setObject, "id") ?? setId;
        string setDirectory = Path.Combine(options.OutputRoot, options.Language, actualSetId);
        string rawDirectory = Path.Combine(setDirectory, "raw");
        string rawCardsDirectory = Path.Combine(rawDirectory, "cards");
        string imagesDirectory = Path.Combine(setDirectory, "images");
        Directory.CreateDirectory(rawCardsDirectory);
        Directory.CreateDirectory(imagesDirectory);

        WriteTextAtomic(Path.Combine(rawDirectory, "set.json"),
            setObject.ToString(Formatting.Indented));

        JArray cardBriefs = setObject["cards"] as JArray ?? new JArray();
        if (options.MaximumCardsPerSet > 0)
            cardBriefs = new JArray(cardBriefs.Take(options.MaximumCardsPerSet));
        checkpoint.StartSet(actualSetId, cardBriefs.Count);

        ImportedSetRecord importedSet = MapSet(setObject, setUrl);
        if (!setOverrides.Apply(importedSet))
            PokemonSetOrderingInference.TryApply(importedSet);
        var manifest = new PrivateContentManifest
        {
            Language = options.Language,
            GeneratedAtUtc = DateTime.UtcNow.ToString("O"),
            Set = importedSet
        };

        var results = new CardImportOutcome[cardBriefs.Count];
        var errors = new ContentImportError[cardBriefs.Count];
        int completed = 0;
        int reusedMetadata = 0;
        int reusedImages = 0;
        using var semaphore = new SemaphoreSlim(options.MaxConcurrency);

        IEnumerable<Task> tasks = cardBriefs.Select(async (token, index) =>
        {
            string cardId = Value(token, "id") ?? $"unknown-{index}";
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            bool attemptFinished = false;
            try
            {
                results[index] = await ImportCardAsync(
                        cardId, rawCardsDirectory, imagesDirectory, setDirectory,
                        options, cancellationToken)
                    .ConfigureAwait(false);
                if (results[index].ReusedMetadata)
                    Interlocked.Increment(ref reusedMetadata);
                if (results[index].ReusedImage)
                    Interlocked.Increment(ref reusedImages);
                attemptFinished = true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                errors[index] = new ContentImportError
                {
                    ItemId = cardId,
                    Message = exception.Message
                };
                attemptFinished = true;
            }
            finally
            {
                if (attemptFinished)
                    checkpoint.RecordCard(actualSetId, cardId, errors[index]?.Message);
                semaphore.Release();
                int current = Interlocked.Increment(ref completed);
                progress?.Report(new ContentImportProgress
                {
                    SetId = actualSetId,
                    Stage = "Downloading cards",
                    Completed = current,
                    Total = cardBriefs.Count
                });
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
        manifest.Cards.AddRange(results
            .Where(result => result != null)
            .Select(result => result.Record)
            .OrderBy(card => card.LocalId, StringComparer.Ordinal));
        manifest.Errors.AddRange(errors.Where(error => error != null));

        WriteTextAtomic(Path.Combine(setDirectory, "manifest.json"),
            JsonConvert.SerializeObject(manifest, Formatting.Indented));
        checkpoint.CompleteSet(actualSetId);
        return new ImportSetOutcome(manifest, reusedMetadata, reusedImages);
    }

    private async Task<CardImportOutcome> ImportCardAsync(
        string cardId,
        string rawCardsDirectory,
        string imagesDirectory,
        string setDirectory,
        ContentImportOptions options,
        CancellationToken cancellationToken)
    {
        string cardUrl = $"{_apiRoot}/{options.Language}/cards/{Uri.EscapeDataString(cardId)}";
        string rawPath = Path.Combine(rawCardsDirectory, SafeFileName(cardId) + ".json");
        string cardJson;
        bool reusedMetadata;

        if (!options.RefreshExistingFiles && File.Exists(rawPath))
        {
            cardJson = File.ReadAllText(rawPath, Encoding.UTF8);
            reusedMetadata = true;
        }
        else
        {
            cardJson = await GetStringWithRetryAsync(cardUrl, options, cancellationToken)
                .ConfigureAwait(false);
            WriteTextAtomic(rawPath, JObject.Parse(cardJson).ToString(Formatting.Indented));
            reusedMetadata = false;
        }

        JObject cardObject = JObject.Parse(cardJson);
        string imageBaseUrl = Value(cardObject, "image");
        string imageRelativePath = null;
        string imageHash = null;
        long imageBytes = 0;
        bool reusedImage = false;

        if (!string.IsNullOrWhiteSpace(imageBaseUrl))
        {
            string imageFileName = SafeFileName(cardId) + "." + options.ImageExtension;
            string imagePath = Path.Combine(imagesDirectory, imageFileName);
            string imageUrl = $"{imageBaseUrl}/{options.ImageQuality}.{options.ImageExtension}";
            byte[] bytes;

            if (!options.RefreshExistingFiles && File.Exists(imagePath))
            {
                bytes = File.ReadAllBytes(imagePath);
                reusedImage = true;
            }
            else
            {
                bytes = await GetBytesWithRetryAsync(imageUrl, options, cancellationToken)
                    .ConfigureAwait(false);
                WriteBytesAtomic(imagePath, bytes);
            }

            imageRelativePath = RelativePath(setDirectory, imagePath);
            imageHash = ComputeSha256(bytes);
            imageBytes = bytes.LongLength;
        }

        var record = new ImportedCardRecord
        {
            Id = Value(cardObject, "id") ?? cardId,
            LocalId = Value(cardObject, "localId"),
            Name = Value(cardObject, "name"),
            Category = Value(cardObject, "category"),
            Rarity = Value(cardObject, "rarity"),
            Illustrator = Value(cardObject, "illustrator"),
            Updated = Value(cardObject, "updated"),
            SourceUrl = cardUrl,
            RawDataRelativePath = RelativePath(setDirectory, rawPath),
            ImageSourceUrl = imageBaseUrl,
            ImageRelativePath = imageRelativePath,
            ImageSha256 = imageHash,
            ImageBytes = imageBytes,
            Variants = MapVariants(cardObject["variants"] as JObject)
        };

        AddStrings(record.Types, cardObject["types"] as JArray);
        JArray boosters = cardObject["boosters"] as JArray;
        if (boosters != null)
        {
            foreach (JToken booster in boosters)
            {
                string boosterId = Value(booster, "id");
                if (!string.IsNullOrWhiteSpace(boosterId))
                    record.BoosterIds.Add(boosterId);
            }
        }

        return new CardImportOutcome(record, reusedMetadata, reusedImage);
    }

    internal static ImportedSetRecord MapSet(JObject setObject, string sourceUrl)
    {
        string setId = Value(setObject, "id");
        string seriesId = Value(setObject["serie"], "id");
        return new ImportedSetRecord
        {
            Id = setId,
            Name = Value(setObject, "name"),
            SetCode = Value(setObject, "tcgOnline") ??
                      setObject.SelectToken("abbreviation.official")?.ToString() ?? setId,
            SeriesId = seriesId,
            SeriesName = Value(setObject["serie"], "name"),
            EraId = seriesId ?? setId,
            GenerationId = "unmapped",
            ReleaseDate = Value(setObject, "releaseDate"),
            OfficialCardCount = IntValue(setObject.SelectToken("cardCount.official")),
            TotalCardCount = IntValue(setObject.SelectToken("cardCount.total")),
            SourceUrl = sourceUrl,
            RawDataRelativePath = "raw/set.json"
        };
    }

    private static ImportedCardVariants MapVariants(JObject variants)
    {
        return new ImportedCardVariants
        {
            Normal = BoolValue(variants?["normal"]),
            Reverse = BoolValue(variants?["reverse"]),
            Holo = BoolValue(variants?["holo"]),
            FirstEdition = BoolValue(variants?["firstEdition"]),
            WPromo = BoolValue(variants?["wPromo"])
        };
    }

    private async Task<string> GetStringWithRetryAsync(
        string url, ContentImportOptions options, CancellationToken cancellationToken)
    {
        byte[] bytes = await GetBytesWithRetryAsync(url, options, cancellationToken)
            .ConfigureAwait(false);
        return Encoding.UTF8.GetString(bytes);
    }

    private async Task<byte[]> GetBytesWithRetryAsync(
        string url, ContentImportOptions options, CancellationToken cancellationToken)
    {
        Exception lastException = null;
        for (int attempt = 1; attempt <= options.MaximumAttempts; attempt++)
        {
            try
            {
                await AwaitRequestSlotAsync(options.RequestIntervalMilliseconds, cancellationToken)
                    .ConfigureAwait(false);
                using HttpResponseMessage response = await _httpClient
                    .GetAsync(url, cancellationToken)
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
                lastException = exception;
                if (attempt < options.MaximumAttempts)
                    await Task.Delay(attempt * options.RetryBaseDelayMilliseconds, cancellationToken)
                        .ConfigureAwait(false);
            }
        }

        throw new HttpRequestException(
            $"Failed to download '{url}' after {options.MaximumAttempts} attempts.", lastException);
    }

    private async Task AwaitRequestSlotAsync(
        int intervalMilliseconds, CancellationToken cancellationToken)
    {
        if (intervalMilliseconds <= 0)
            return;
        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            TimeSpan delay = _nextRequestUtc - DateTime.UtcNow;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            _nextRequestUtc = DateTime.UtcNow.AddMilliseconds(intervalMilliseconds);
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private static void ValidateOptions(ContentImportOptions options)
    {
        if (options == null)
            throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.OutputRoot))
            throw new ArgumentException("OutputRoot is required.", nameof(options));
        if (options.MaxConcurrency < 1 || options.MaxConcurrency > 32)
            throw new ArgumentOutOfRangeException(nameof(options.MaxConcurrency));
        if (!new[] { "low", "high" }.Contains(options.ImageQuality))
            throw new ArgumentException("ImageQuality must be low or high.", nameof(options));
        if (!new[] { "jpg", "png", "webp" }.Contains(options.ImageExtension))
            throw new ArgumentException("ImageExtension must be jpg, png, or webp.", nameof(options));
        if (options.RequestIntervalMilliseconds < 0 || options.RequestIntervalMilliseconds > 10000)
            throw new ArgumentOutOfRangeException(nameof(options.RequestIntervalMilliseconds));
        if (options.MaximumAttempts < 1 || options.MaximumAttempts > 10)
            throw new ArgumentOutOfRangeException(nameof(options.MaximumAttempts));
        if (options.RetryBaseDelayMilliseconds < 0 || options.RetryBaseDelayMilliseconds > 60000)
            throw new ArgumentOutOfRangeException(nameof(options.RetryBaseDelayMilliseconds));
    }

    private static HttpClient CreateClient()
    {
        ServicePointManager.DefaultConnectionLimit = Math.Max(
            ServicePointManager.DefaultConnectionLimit, 32);
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "UniversalGachaSimulator-PrivateImporter/2.0");
        return client;
    }

    private static void WriteTextAtomic(string path, string content)
    {
        WriteBytesAtomic(path, new UTF8Encoding(false).GetBytes(content));
    }

    private static void WriteBytesAtomic(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
        string temporaryPath = path + ".download";
        File.WriteAllBytes(temporaryPath, bytes);
        if (File.Exists(path))
            File.Delete(path);
        File.Move(temporaryPath, path);
    }

    private static string ComputeSha256(byte[] bytes)
    {
        using SHA256 sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(bytes).Select(value => value.ToString("x2")));
    }

    private static string SafeFileName(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
    }

    private static string RelativePath(string root, string path)
    {
        Uri rootUri = new Uri(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
        Uri pathUri = new Uri(Path.GetFullPath(path));
        return Uri.UnescapeDataString(rootUri.MakeRelativeUri(pathUri).ToString()).Replace('/', Path.DirectorySeparatorChar);
    }

    private static string Value(JToken token, string propertyName)
    {
        return token?[propertyName]?.Type == JTokenType.Null
            ? null
            : token?[propertyName]?.ToString();
    }

    private static int IntValue(JToken token)
    {
        return token == null ? 0 : token.Value<int>();
    }

    private static bool BoolValue(JToken token)
    {
        return token != null && token.Value<bool>();
    }

    private static void AddStrings(List<string> target, JArray source)
    {
        if (source == null)
            return;
        target.AddRange(source.Values<string>().Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    public void Dispose()
    {
        _requestGate.Dispose();
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }

    private sealed class CardImportOutcome
    {
        public CardImportOutcome(
            ImportedCardRecord record, bool reusedMetadata, bool reusedImage)
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
            PrivateContentManifest manifest, int reusedMetadataCount, int reusedImageCount)
        {
            Manifest = manifest;
            ReusedMetadataCount = reusedMetadataCount;
            ReusedImageCount = reusedImageCount;
        }

        public PrivateContentManifest Manifest { get; }
        public int ReusedMetadataCount { get; }
        public int ReusedImageCount { get; }
    }
}
