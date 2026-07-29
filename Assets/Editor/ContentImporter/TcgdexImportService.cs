using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    public TcgdexImportService()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "UniversalGachaSimulator-PrivateImporter/1.0");
    }

    public async Task<ContentImportSummary> ImportSetsAsync(
        IEnumerable<string> setIds,
        ContentImportOptions options,
        IProgress<ContentImportProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateOptions(options);
        Directory.CreateDirectory(options.OutputRoot);

        var summary = new ContentImportSummary();
        foreach (string setId in setIds.Where(id => !string.IsNullOrWhiteSpace(id)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            PrivateContentManifest manifest = await ImportSetAsync(
                    setId.Trim(), options, progress, cancellationToken)
                .ConfigureAwait(false);

            summary.SetCount++;
            summary.CardCount += manifest.Cards.Count;
            summary.ErrorCount += manifest.Errors.Count;
            summary.ImageBytes += manifest.Cards.Sum(card => card.ImageBytes);
        }

        return summary;
    }

    private async Task<PrivateContentManifest> ImportSetAsync(
        string setId,
        ContentImportOptions options,
        IProgress<ContentImportProgress> progress,
        CancellationToken cancellationToken)
    {
        string setUrl = $"{ApiRoot}/{options.Language}/sets/{Uri.EscapeDataString(setId)}";
        progress?.Report(new ContentImportProgress
        {
            SetId = setId,
            Stage = "Downloading set metadata",
            Total = 1
        });

        string setJson = await GetStringWithRetryAsync(setUrl, cancellationToken).ConfigureAwait(false);
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

        var manifest = new PrivateContentManifest
        {
            Language = options.Language,
            GeneratedAtUtc = DateTime.UtcNow.ToString("O"),
            Set = MapSet(setObject, setUrl)
        };

        var results = new ImportedCardRecord[cardBriefs.Count];
        var errors = new ContentImportError[cardBriefs.Count];
        int completed = 0;
        using var semaphore = new SemaphoreSlim(options.MaxConcurrency);

        IEnumerable<Task> tasks = cardBriefs.Select(async (token, index) =>
        {
            string cardId = Value(token, "id") ?? $"unknown-{index}";
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                results[index] = await ImportCardAsync(
                        cardId, rawCardsDirectory, imagesDirectory, setDirectory,
                        options, cancellationToken)
                    .ConfigureAwait(false);
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
            }
            finally
            {
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
        manifest.Cards.AddRange(results.Where(card => card != null).OrderBy(card => card.LocalId));
        manifest.Errors.AddRange(errors.Where(error => error != null));

        WriteTextAtomic(Path.Combine(setDirectory, "manifest.json"),
            JsonConvert.SerializeObject(manifest, Formatting.Indented));
        return manifest;
    }

    private async Task<ImportedCardRecord> ImportCardAsync(
        string cardId,
        string rawCardsDirectory,
        string imagesDirectory,
        string setDirectory,
        ContentImportOptions options,
        CancellationToken cancellationToken)
    {
        string cardUrl = $"{ApiRoot}/{options.Language}/cards/{Uri.EscapeDataString(cardId)}";
        string rawPath = Path.Combine(rawCardsDirectory, SafeFileName(cardId) + ".json");
        string cardJson;

        if (!options.RefreshExistingFiles && File.Exists(rawPath))
            cardJson = File.ReadAllText(rawPath, Encoding.UTF8);
        else
        {
            cardJson = await GetStringWithRetryAsync(cardUrl, cancellationToken).ConfigureAwait(false);
            WriteTextAtomic(rawPath, JObject.Parse(cardJson).ToString(Formatting.Indented));
        }

        JObject cardObject = JObject.Parse(cardJson);
        string imageBaseUrl = Value(cardObject, "image");
        string imageRelativePath = null;
        string imageHash = null;
        long imageBytes = 0;

        if (!string.IsNullOrWhiteSpace(imageBaseUrl))
        {
            string imageFileName = SafeFileName(cardId) + "." + options.ImageExtension;
            string imagePath = Path.Combine(imagesDirectory, imageFileName);
            string imageUrl = $"{imageBaseUrl}/{options.ImageQuality}.{options.ImageExtension}";
            byte[] bytes;

            if (!options.RefreshExistingFiles && File.Exists(imagePath))
                bytes = File.ReadAllBytes(imagePath);
            else
            {
                bytes = await GetBytesWithRetryAsync(imageUrl, cancellationToken).ConfigureAwait(false);
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

        return record;
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
                if (attempt < 3)
                    await Task.Delay(attempt * 750, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new HttpRequestException($"Failed to download '{url}' after 3 attempts.", lastException);
    }

    private static void ValidateOptions(ContentImportOptions options)
    {
        if (options == null)
            throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.OutputRoot))
            throw new ArgumentException("OutputRoot is required.", nameof(options));
        if (options.MaxConcurrency < 1 || options.MaxConcurrency > 12)
            throw new ArgumentOutOfRangeException(nameof(options.MaxConcurrency));
        if (!new[] { "low", "high" }.Contains(options.ImageQuality))
            throw new ArgumentException("ImageQuality must be low or high.", nameof(options));
        if (!new[] { "jpg", "png", "webp" }.Contains(options.ImageExtension))
            throw new ArgumentException("ImageExtension must be jpg, png, or webp.", nameof(options));
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
        _httpClient.Dispose();
    }
}
