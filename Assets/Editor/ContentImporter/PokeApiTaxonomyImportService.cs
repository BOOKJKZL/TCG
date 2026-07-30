using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Gacha.Pokemon.Infrastructure;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

[Serializable]
public sealed class PokeApiTaxonomyImportCheckpoint
{
    public int SchemaVersion = 1;
    public string ApiRoot;
    public string CapturedAtUtc;
    public string UpdatedAtUtc;
    public string Stage;
    public bool Complete;
    public int GenerationCount;
    public int SpeciesCount;
    public int PokemonCount;
    public int FormCount;
    public int VersionGroupCount;
    public List<string> Failures = new List<string>();
}

public sealed class PokeApiTaxonomyImportOptions
{
    public string OutputRoot;
    public string FormClassificationPath;
    public bool RefreshExistingFiles;
    public int MaxConcurrency = 4;
    public int RequestIntervalMilliseconds = 50;
    public int MaximumAttempts = 5;
    public int RetryBaseDelayMilliseconds = 750;
}

public sealed class PokeApiTaxonomyImportProgress
{
    public string Stage;
    public int Completed;
    public int Total;
    public string ItemId;
    public float Ratio => Total <= 0 ? 0f : (float)Completed / Total;
}

public sealed class PokeApiTaxonomyImportSummary
{
    public string SnapshotPath;
    public string CheckpointPath;
    public string SourceSha256;
    public int GenerationCount;
    public int SpeciesCount;
    public int PokemonCount;
    public int FormCount;
    public int VersionGroupCount;
    public int DownloadedFileCount;
    public int ReusedFileCount;
    public int WarningCount;
    public int ManualReviewCount;
}

public sealed class PokeApiTaxonomyImportException : Exception
{
    public PokeApiTaxonomyImportException(string message) : base(message) { }
    public PokeApiTaxonomyImportException(string message, Exception innerException) : base(message, innerException) { }
}

public sealed class PokeApiTaxonomyImportService : IDisposable
{
    public const string DefaultApiRoot = PokeApiTaxonomyCompiler.SourceBaseUrl;
    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;
    private readonly string apiRoot;
    private readonly SemaphoreSlim requestGate = new SemaphoreSlim(1, 1);
    private DateTime nextRequestUtc;
    private int downloadedFileCount;
    private int reusedFileCount;

    public PokeApiTaxonomyImportService()
        : this(CreateClient(), DefaultApiRoot, true)
    {
    }

    internal PokeApiTaxonomyImportService(HttpClient client, string apiRoot)
        : this(client, apiRoot, false)
    {
    }

    private PokeApiTaxonomyImportService(HttpClient client, string apiRoot, bool ownsClient)
    {
        httpClient = client ?? throw new ArgumentNullException(nameof(client));
        if (!Uri.TryCreate(apiRoot, UriKind.Absolute, out Uri root) || root.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("PokeAPI root must be an absolute HTTPS URL.", nameof(apiRoot));
        this.apiRoot = apiRoot.TrimEnd('/') + "/";
        ownsHttpClient = ownsClient;
    }

    public async Task<PokeApiTaxonomyImportSummary> ImportAsync(
        PokeApiTaxonomyImportOptions options,
        IProgress<PokeApiTaxonomyImportProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateOptions(options);
        downloadedFileCount = 0;
        reusedFileCount = 0;
        string outputRoot = Path.GetFullPath(options.OutputRoot);
        string rawRoot = Path.Combine(outputRoot, "raw");
        string checkpointPath = Path.Combine(outputRoot, "import-checkpoint.json");
        string snapshotPath = Path.Combine(outputRoot, "snapshot", "pokemon-taxonomy.json");
        Directory.CreateDirectory(rawRoot);

        PokeApiTaxonomyImportCheckpoint checkpoint = LoadCheckpoint(
            checkpointPath, options.RefreshExistingFiles);
        checkpoint.ApiRoot = apiRoot;
        checkpoint.Complete = false;
        checkpoint.Failures.Clear();

        try
        {
            IReadOnlyList<int> generationIds = await DiscoverAsync(
                "generation", Path.Combine(rawRoot, "lists", "generations.json"),
                options, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<int> speciesIds = await DiscoverAsync(
                "pokemon-species", Path.Combine(rawRoot, "lists", "species.json"),
                options, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<int> versionGroupIds = await DiscoverAsync(
                "version-group", Path.Combine(rawRoot, "lists", "version-groups.json"),
                options, cancellationToken).ConfigureAwait(false);

            await DownloadStageAsync("generations", "generation", generationIds,
                rawRoot, options, checkpoint, checkpointPath, progress, cancellationToken)
                .ConfigureAwait(false);
            await DownloadStageAsync("version-groups", "version-group", versionGroupIds,
                rawRoot, options, checkpoint, checkpointPath, progress, cancellationToken)
                .ConfigureAwait(false);
            await DownloadStageAsync("species", "pokemon-species", speciesIds,
                rawRoot, options, checkpoint, checkpointPath, progress, cancellationToken)
                .ConfigureAwait(false);

            IReadOnlyList<int> pokemonIds = DiscoverReferencedIds(
                rawRoot, "species", speciesIds, "varieties[*].pokemon.url", "Pokemon");
            await DownloadStageAsync("pokemon", "pokemon", pokemonIds,
                rawRoot, options, checkpoint, checkpointPath, progress, cancellationToken)
                .ConfigureAwait(false);

            IReadOnlyList<int> formIds = DiscoverReferencedIds(
                rawRoot, "pokemon", pokemonIds, "forms[*].url", "form");
            await DownloadStageAsync("forms", "pokemon-form", formIds,
                rawRoot, options, checkpoint, checkpointPath, progress, cancellationToken)
                .ConfigureAwait(false);

            PokeApiTaxonomyRawData raw = LoadRaw(
                rawRoot, generationIds, speciesIds, pokemonIds, formIds, versionGroupIds);
            PokemonFormClassificationCatalog classification =
                PokemonContentOverrideLoader.LoadFormClassification(options.FormClassificationPath);
            DateTimeOffset capturedAt = DateTimeOffset.Parse(
                checkpoint.CapturedAtUtc, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            PokeApiTaxonomyCompileResult result = PokeApiTaxonomyCompiler.Compile(
                raw, classification, capturedAt);
            WriteTextAtomic(snapshotPath,
                JsonConvert.SerializeObject(result.Snapshot, Formatting.Indented));
            new PokemonTaxonomySnapshotReader().LoadFile(snapshotPath);

            checkpoint.Stage = "complete";
            checkpoint.Complete = true;
            checkpoint.GenerationCount = generationIds.Count;
            checkpoint.SpeciesCount = speciesIds.Count;
            checkpoint.PokemonCount = pokemonIds.Count;
            checkpoint.FormCount = formIds.Count;
            checkpoint.VersionGroupCount = versionGroupIds.Count;
            SaveCheckpoint(checkpointPath, checkpoint);

            return new PokeApiTaxonomyImportSummary
            {
                SnapshotPath = snapshotPath,
                CheckpointPath = checkpointPath,
                SourceSha256 = result.Snapshot.SourceSha256,
                GenerationCount = generationIds.Count,
                SpeciesCount = speciesIds.Count,
                PokemonCount = pokemonIds.Count,
                FormCount = formIds.Count,
                VersionGroupCount = versionGroupIds.Count,
                DownloadedFileCount = downloadedFileCount,
                ReusedFileCount = reusedFileCount,
                WarningCount = result.Snapshot.Warnings.Count,
                ManualReviewCount = result.ManualReviewCount
            };
        }
        catch (OperationCanceledException)
        {
            checkpoint.Stage = "cancelled";
            checkpoint.Failures.Add("Import cancelled.");
            SaveCheckpoint(checkpointPath, checkpoint);
            throw;
        }
        catch (Exception exception)
        {
            checkpoint.Stage = "failed";
            checkpoint.Failures.Add(exception.Message);
            SaveCheckpoint(checkpointPath, checkpoint);
            throw new PokeApiTaxonomyImportException(
                "PokeAPI taxonomy import failed and can be resumed: " + exception.Message,
                exception);
        }
    }

    private async Task<IReadOnlyList<int>> DiscoverAsync(
        string resource, string path, PokeApiTaxonomyImportOptions options,
        CancellationToken cancellationToken)
    {
        string json = await GetStringWithRetryAsync(
            apiRoot + resource + "?limit=100000&offset=0", options, cancellationToken)
            .ConfigureAwait(false);
        JObject list = JObject.Parse(json);
        JArray results = list["results"] as JArray ?? throw new InvalidDataException(
            $"PokeAPI {resource} discovery has no results.");
        int count = list["count"]?.Value<int>() ?? -1;
        if (count != results.Count || list["next"]?.Type != JTokenType.Null)
            throw new InvalidDataException(
                $"PokeAPI {resource} discovery was paginated or incomplete ({results.Count}/{count}).");
        int[] ids = results.Select(item => ResourceId(item["url"]?.ToString(), resource))
            .Distinct().OrderBy(value => value).ToArray();
        if (ids.Length != results.Count || ids.Length == 0)
            throw new InvalidDataException($"PokeAPI {resource} discovery contains duplicate or empty ids.");
        WriteTextAtomic(path, list.ToString(Formatting.Indented));
        Interlocked.Increment(ref downloadedFileCount);
        return ids;
    }

    private async Task DownloadStageAsync(
        string directoryName,
        string resource,
        IReadOnlyList<int> ids,
        string rawRoot,
        PokeApiTaxonomyImportOptions options,
        PokeApiTaxonomyImportCheckpoint checkpoint,
        string checkpointPath,
        IProgress<PokeApiTaxonomyImportProgress> progress,
        CancellationToken cancellationToken)
    {
        string directory = Path.Combine(rawRoot, directoryName);
        Directory.CreateDirectory(directory);
        var failures = new ConcurrentQueue<string>();
        int completed = 0;
        using var concurrency = new SemaphoreSlim(options.MaxConcurrency);
        Task[] tasks = ids.Select(async id =>
        {
            await concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                string path = Path.Combine(directory, id + ".json");
                if (!options.RefreshExistingFiles && IsValidCachedResource(path, id))
                {
                    Interlocked.Increment(ref reusedFileCount);
                }
                else
                {
                    string json = await GetStringWithRetryAsync(
                        apiRoot + resource + "/" + id + "/", options, cancellationToken)
                        .ConfigureAwait(false);
                    JObject value = JObject.Parse(json);
                    if (value["id"]?.Value<int>() != id)
                        throw new InvalidDataException($"PokeAPI {resource} {id} returned a different id.");
                    WriteTextAtomic(path, value.ToString(Formatting.Indented));
                    Interlocked.Increment(ref downloadedFileCount);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                failures.Enqueue(resource + ":" + id + ": " + exception.Message);
            }
            finally
            {
                concurrency.Release();
                int current = Interlocked.Increment(ref completed);
                progress?.Report(new PokeApiTaxonomyImportProgress
                {
                    Stage = directoryName,
                    Completed = current,
                    Total = ids.Count,
                    ItemId = id.ToString(CultureInfo.InvariantCulture)
                });
            }
        }).ToArray();
        await Task.WhenAll(tasks).ConfigureAwait(false);

        checkpoint.Stage = directoryName;
        checkpoint.Failures = failures.OrderBy(value => value, StringComparer.Ordinal).ToList();
        SaveCheckpoint(checkpointPath, checkpoint);
        if (!failures.IsEmpty)
            throw new InvalidDataException(
                $"PokeAPI {directoryName} stage has {failures.Count} failed resources. " +
                "Run the same import again to resume.");
    }

    private static IReadOnlyList<int> DiscoverReferencedIds(
        string rawRoot,
        string directoryName,
        IEnumerable<int> ownerIds,
        string jsonPath,
        string label)
    {
        var result = new SortedSet<int>();
        foreach (int ownerId in ownerIds)
        {
            JObject owner = JObject.Parse(File.ReadAllText(
                Path.Combine(rawRoot, directoryName, ownerId + ".json"), Encoding.UTF8));
            foreach (JToken token in owner.SelectTokens(jsonPath))
                result.Add(ResourceId(token.ToString(), label));
        }
        if (result.Count == 0)
            throw new InvalidDataException($"PokeAPI snapshot contains no referenced {label} ids.");
        return result.ToArray();
    }

    private static PokeApiTaxonomyRawData LoadRaw(
        string rawRoot,
        IEnumerable<int> generationIds,
        IEnumerable<int> speciesIds,
        IEnumerable<int> pokemonIds,
        IEnumerable<int> formIds,
        IEnumerable<int> versionGroupIds)
    {
        var result = new PokeApiTaxonomyRawData();
        Load(result.Generations, rawRoot, "generations", generationIds);
        Load(result.Species, rawRoot, "species", speciesIds);
        Load(result.Pokemon, rawRoot, "pokemon", pokemonIds);
        Load(result.Forms, rawRoot, "forms", formIds);
        Load(result.VersionGroups, rawRoot, "version-groups", versionGroupIds);
        return result;
    }

    private static void Load(
        IDictionary<int, string> target,
        string rawRoot,
        string directoryName,
        IEnumerable<int> ids)
    {
        foreach (int id in ids.OrderBy(value => value))
            target.Add(id, File.ReadAllText(
                Path.Combine(rawRoot, directoryName, id + ".json"), Encoding.UTF8));
    }

    private async Task<string> GetStringWithRetryAsync(
        string url, PokeApiTaxonomyImportOptions options, CancellationToken cancellationToken)
    {
        Exception lastException = null;
        for (int attempt = 1; attempt <= options.MaximumAttempts; attempt++)
        {
            try
            {
                await AwaitRequestSlotAsync(options.RequestIntervalMilliseconds, cancellationToken)
                    .ConfigureAwait(false);
                using HttpResponseMessage response = await httpClient.GetAsync(url, cancellationToken)
                    .ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                byte[] bytes = await response.Content.ReadAsByteArrayAsync()
                    .ConfigureAwait(false);
                return Encoding.UTF8.GetString(bytes);
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

    private PokeApiTaxonomyImportCheckpoint LoadCheckpoint(string path, bool refresh)
    {
        if (!refresh && File.Exists(path))
        {
            PokeApiTaxonomyImportCheckpoint existing =
                JsonConvert.DeserializeObject<PokeApiTaxonomyImportCheckpoint>(File.ReadAllText(path));
            if (existing != null && existing.SchemaVersion == 1 &&
                string.Equals(existing.ApiRoot, apiRoot, StringComparison.Ordinal))
            {
                existing.Failures = existing.Failures ?? new List<string>();
                return existing;
            }
        }
        return new PokeApiTaxonomyImportCheckpoint
        {
            ApiRoot = apiRoot,
            CapturedAtUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            Stage = "starting"
        };
    }

    private static void SaveCheckpoint(string path, PokeApiTaxonomyImportCheckpoint checkpoint)
    {
        checkpoint.UpdatedAtUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        WriteTextAtomic(path, JsonConvert.SerializeObject(checkpoint, Formatting.Indented));
    }

    private static bool IsValidCachedResource(string path, int id)
    {
        if (!File.Exists(path))
            return false;
        try
        {
            return JObject.Parse(File.ReadAllText(path, Encoding.UTF8))["id"]?.Value<int>() == id;
        }
        catch (Exception exception) when (exception is IOException || exception is JsonException)
        {
            return false;
        }
    }

    private static void ValidateOptions(PokeApiTaxonomyImportOptions options)
    {
        if (options == null)
            throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.OutputRoot))
            throw new ArgumentException("OutputRoot is required.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.FormClassificationPath) ||
            !File.Exists(options.FormClassificationPath))
            throw new FileNotFoundException("Form classification policy was not found.", options.FormClassificationPath);
        if (options.MaxConcurrency < 1 || options.MaxConcurrency > 12)
            throw new ArgumentOutOfRangeException(nameof(options.MaxConcurrency));
        if (options.RequestIntervalMilliseconds < 0 || options.RequestIntervalMilliseconds > 10000)
            throw new ArgumentOutOfRangeException(nameof(options.RequestIntervalMilliseconds));
        if (options.MaximumAttempts < 1 || options.MaximumAttempts > 10)
            throw new ArgumentOutOfRangeException(nameof(options.MaximumAttempts));
        if (options.RetryBaseDelayMilliseconds < 0 || options.RetryBaseDelayMilliseconds > 60000)
            throw new ArgumentOutOfRangeException(nameof(options.RetryBaseDelayMilliseconds));
    }

    private static int ResourceId(string url, string context)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException($"PokeAPI {context} contains an invalid HTTPS URL.");
        string segment = uri.Segments.Select(value => value.Trim('/'))
            .LastOrDefault(value => int.TryParse(value, out _));
        if (!int.TryParse(segment, out int id) || id < 1)
            throw new InvalidDataException($"PokeAPI {context} contains an invalid resource id.");
        return id;
    }

    private static void WriteTextAtomic(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
        string temporaryPath = path + ".download";
        File.WriteAllText(temporaryPath, content, new UTF8Encoding(false));
        if (File.Exists(path))
            File.Delete(path);
        File.Move(temporaryPath, path);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "UniversalGachaSimulator-PrivatePokedexImporter/1.0");
        return client;
    }

    public void Dispose()
    {
        requestGate.Dispose();
        if (ownsHttpClient)
            httpClient.Dispose();
    }
}
