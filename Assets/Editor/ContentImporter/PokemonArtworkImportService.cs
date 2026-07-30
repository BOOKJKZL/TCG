using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Gacha.Pokemon.Domain;
using Gacha.Pokemon.Infrastructure;
using Newtonsoft.Json;

[Serializable]
public sealed class PokemonArtworkImportCheckpoint
{
    public int SchemaVersion = 1;
    public string UpdatedAtUtc;
    public bool Complete;
    public int TotalForms;
    public int CompletedImages;
    public int MissingSourceImages;
    public List<string> Failures = new List<string>();
}

public sealed class PokemonArtworkImportOptions
{
    public string TaxonomySnapshotPath;
    public string OutputRoot;
    public bool RefreshExistingFiles;
    public int MaxConcurrency = 6;
    public int RequestIntervalMilliseconds = 15;
    public int MaximumAttempts = 5;
    public int RetryBaseDelayMilliseconds = 500;
    public int MaximumImageBytes = 12 * 1024 * 1024;
}

public sealed class PokemonArtworkImportProgress
{
    public int Completed;
    public int Total;
    public string FormId;
    public float Ratio => Total <= 0 ? 0f : (float)Completed / Total;
}

public sealed class PokemonArtworkImportSummary
{
    public int FormCount;
    public int ImageCount;
    public int MissingSourceCount;
    public int DownloadedCount;
    public int ReusedCount;
    public long ImageBytes;
    public string OutputRoot;
    public string CheckpointPath;
}

public sealed class PokemonArtworkImportService : IDisposable
{
    private readonly HttpClient client;
    private readonly bool ownsClient;
    private readonly SemaphoreSlim requestGate = new SemaphoreSlim(1, 1);
    private DateTime nextRequestUtc;
    private int downloaded;
    private int reused;

    public PokemonArtworkImportService() : this(CreateClient(), true) { }

    internal PokemonArtworkImportService(HttpClient client) : this(client, false) { }

    private PokemonArtworkImportService(HttpClient client, bool ownsClient)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.ownsClient = ownsClient;
    }

    public async Task<PokemonArtworkImportSummary> ImportAsync(
        PokemonArtworkImportOptions options,
        IProgress<PokemonArtworkImportProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateOptions(options);
        downloaded = 0;
        reused = 0;
        string root = Path.GetFullPath(options.OutputRoot);
        string checkpointPath = Path.Combine(root, "artwork-import-checkpoint.json");
        Directory.CreateDirectory(root);
        PokemonTaxonomySnapshotLoadResult taxonomy =
            new PokemonTaxonomySnapshotReader().LoadFile(options.TaxonomySnapshotPath);
        PokemonFormDefinition[] forms = taxonomy.Catalog.Forms.Values
            .OrderBy(value => taxonomy.Catalog.Generations[value.IntroducedGenerationId].Order)
            .ThenBy(value => value.PokemonId)
            .ThenBy(value => value.Id, StringComparer.Ordinal)
            .ToArray();
        var checkpoint = new PokemonArtworkImportCheckpoint { TotalForms = forms.Length };
        var failures = new ConcurrentQueue<string>();
        var records = new ConcurrentBag<ArtworkRecord>();
        var missing = new ConcurrentBag<PokemonFormDefinition>();
        int completed = 0;

        try
        {
            using var concurrency = new SemaphoreSlim(options.MaxConcurrency);
            Task[] tasks = forms.Select(async form =>
            {
                await concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (string.IsNullOrWhiteSpace(form.ImageSourceUrl))
                    {
                        missing.Add(form);
                        return;
                    }
                    Uri source = ValidateSourceUri(form.ImageSourceUrl);
                    string generationRoot = Path.Combine(root, form.IntroducedGenerationId);
                    string relativePath = "images/" + PortableFormName(form.Id) + ".png";
                    string imagePath = Path.Combine(
                        generationRoot,
                        relativePath.Replace('/', Path.DirectorySeparatorChar));
                    byte[] bytes;
                    if (!options.RefreshExistingFiles && TryReadValidPng(imagePath, out bytes))
                        Interlocked.Increment(ref reused);
                    else
                    {
                        bytes = await DownloadWithRetryAsync(source, options, cancellationToken)
                            .ConfigureAwait(false);
                        if (!IsPng(bytes))
                            throw new InvalidDataException("Artwork response is not a PNG image.");
                        WriteBytesAtomic(imagePath, bytes);
                        Interlocked.Increment(ref downloaded);
                    }
                    records.Add(new ArtworkRecord(
                        form,
                        relativePath,
                        Sha256(bytes),
                        bytes.LongLength,
                        source.AbsoluteUri));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    failures.Enqueue(form.Id + ": " + exception.Message);
                }
                finally
                {
                    concurrency.Release();
                    int current = Interlocked.Increment(ref completed);
                    progress?.Report(new PokemonArtworkImportProgress
                    {
                        Completed = current,
                        Total = forms.Length,
                        FormId = form.Id
                    });
                }
            }).ToArray();
            await Task.WhenAll(tasks).ConfigureAwait(false);

            checkpoint.CompletedImages = records.Count;
            checkpoint.MissingSourceImages = missing.Count;
            checkpoint.Failures = failures.OrderBy(value => value, StringComparer.Ordinal).ToList();
            if (checkpoint.Failures.Count > 0)
                throw new InvalidDataException(
                    $"Artwork import has {checkpoint.Failures.Count} failed images and can be resumed.");

            foreach (PokemonGenerationDefinition generation in taxonomy.Catalog.Generations.Values
                         .OrderBy(value => value.Order))
            {
                string generationRoot = Path.Combine(root, generation.Id);
                var manifest = new PokemonArtworkManifestDto
                {
                    GenerationId = generation.Id,
                    GeneratedAtUtc = taxonomy.CapturedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                    TaxonomySourceSha256 = taxonomy.SourceSha256,
                    Entries = records.Where(value => value.Form.IntroducedGenerationId == generation.Id)
                        .OrderBy(value => value.Form.PokemonId)
                        .ThenBy(value => value.Form.Id, StringComparer.Ordinal)
                        .Select(value => new PokemonArtworkEntryDto
                        {
                            FormId = value.Form.Id,
                            RelativePath = value.RelativePath,
                            Sha256 = value.Sha256,
                            Bytes = value.Bytes,
                            SourceUrl = value.SourceUrl
                        }).ToList(),
                    MissingFormIds = missing.Where(value => value.IntroducedGenerationId == generation.Id)
                        .Select(value => value.Id)
                        .OrderBy(value => value, StringComparer.Ordinal)
                        .ToList()
                };
                string manifestPath = Path.Combine(generationRoot, "manifest.json");
                WriteTextAtomic(manifestPath, JsonConvert.SerializeObject(manifest, Formatting.Indented));
                PokemonArtworkCatalog loaded = new PokemonArtworkManifestReader().LoadFile(manifestPath);
                VerifyGenerationFiles(generationRoot, loaded);
            }

            checkpoint.Complete = true;
            SaveCheckpoint(checkpointPath, checkpoint);
            return new PokemonArtworkImportSummary
            {
                FormCount = forms.Length,
                ImageCount = records.Count,
                MissingSourceCount = missing.Count,
                DownloadedCount = downloaded,
                ReusedCount = reused,
                ImageBytes = records.Sum(value => value.Bytes),
                OutputRoot = root,
                CheckpointPath = checkpointPath
            };
        }
        catch
        {
            checkpoint.Failures = failures.OrderBy(value => value, StringComparer.Ordinal).ToList();
            checkpoint.CompletedImages = records.Count;
            checkpoint.MissingSourceImages = missing.Count;
            checkpoint.Complete = false;
            SaveCheckpoint(checkpointPath, checkpoint);
            throw;
        }
    }

    private async Task<byte[]> DownloadWithRetryAsync(
        Uri source,
        PokemonArtworkImportOptions options,
        CancellationToken cancellationToken)
    {
        Exception last = null;
        for (int attempt = 1; attempt <= options.MaximumAttempts; attempt++)
        {
            try
            {
                await AwaitRequestSlotAsync(options.RequestIntervalMilliseconds, cancellationToken)
                    .ConfigureAwait(false);
                using HttpResponseMessage response = await client.GetAsync(source, cancellationToken)
                    .ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > options.MaximumImageBytes)
                    throw new InvalidDataException("Artwork exceeds the maximum image byte limit.");
                byte[] bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                if (bytes.Length <= 0 || bytes.Length > options.MaximumImageBytes)
                    throw new InvalidDataException("Artwork has an invalid byte length.");
                return bytes;
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
            $"Failed to download artwork after {options.MaximumAttempts} attempts: {source}", last);
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

    private static void VerifyGenerationFiles(string generationRoot, PokemonArtworkCatalog catalog)
    {
        foreach (PokemonArtworkEntry entry in catalog.Entries.Values)
        {
            string path = Path.GetFullPath(Path.Combine(
                generationRoot,
                entry.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(
                    generationRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase) ||
                !TryReadValidPng(path, out byte[] bytes) ||
                bytes.LongLength != entry.Bytes ||
                !string.Equals(Sha256(bytes), entry.Sha256, StringComparison.Ordinal))
                throw new InvalidDataException("Artwork manifest verification failed: " + entry.FormId);
        }
    }

    private static Uri ValidateSourceUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, "raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Artwork source is outside the allowed HTTPS PokeAPI sprite host.");
        return uri;
    }

    internal static string PortableFormName(string formId) =>
        (formId ?? string.Empty).Replace(':', '-');

    internal static bool IsPng(byte[] bytes) =>
        bytes != null && bytes.Length >= 8 &&
        bytes[0] == 137 && bytes[1] == 80 && bytes[2] == 78 && bytes[3] == 71 &&
        bytes[4] == 13 && bytes[5] == 10 && bytes[6] == 26 && bytes[7] == 10;

    private static bool TryReadValidPng(string path, out byte[] bytes)
    {
        bytes = null;
        try
        {
            if (!File.Exists(path))
                return false;
            bytes = File.ReadAllBytes(path);
            return IsPng(bytes);
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static string Sha256(byte[] bytes)
    {
        using SHA256 sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
    }

    private static void WriteBytesAtomic(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
        string temporary = path + ".download";
        File.WriteAllBytes(temporary, bytes);
        if (File.Exists(path))
            File.Delete(path);
        File.Move(temporary, path);
    }

    private static void WriteTextAtomic(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
        string temporary = path + ".download";
        File.WriteAllText(temporary, text, new UTF8Encoding(false));
        if (File.Exists(path))
            File.Delete(path);
        File.Move(temporary, path);
    }

    private static void SaveCheckpoint(string path, PokemonArtworkImportCheckpoint checkpoint)
    {
        checkpoint.UpdatedAtUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        WriteTextAtomic(path, JsonConvert.SerializeObject(checkpoint, Formatting.Indented));
    }

    private static void ValidateOptions(PokemonArtworkImportOptions options)
    {
        if (options == null)
            throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.TaxonomySnapshotPath) ||
            !File.Exists(options.TaxonomySnapshotPath))
            throw new FileNotFoundException("Taxonomy snapshot was not found.", options.TaxonomySnapshotPath);
        if (string.IsNullOrWhiteSpace(options.OutputRoot))
            throw new ArgumentException("Artwork output root is required.", nameof(options));
        if (options.MaxConcurrency < 1 || options.MaxConcurrency > 12)
            throw new ArgumentOutOfRangeException(nameof(options.MaxConcurrency));
        if (options.MaximumAttempts < 1 || options.MaximumAttempts > 10)
            throw new ArgumentOutOfRangeException(nameof(options.MaximumAttempts));
        if (options.MaximumImageBytes < 1024)
            throw new ArgumentOutOfRangeException(nameof(options.MaximumImageBytes));
    }

    private static HttpClient CreateClient()
    {
        var result = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        result.DefaultRequestHeaders.UserAgent.ParseAdd("UniversalGachaPrivateImporter/1.0");
        return result;
    }

    public void Dispose()
    {
        requestGate.Dispose();
        if (ownsClient)
            client.Dispose();
    }

    private sealed class ArtworkRecord
    {
        public ArtworkRecord(
            PokemonFormDefinition form,
            string relativePath,
            string sha256,
            long bytes,
            string sourceUrl)
        {
            Form = form;
            RelativePath = relativePath;
            Sha256 = sha256;
            Bytes = bytes;
            SourceUrl = sourceUrl;
        }

        public PokemonFormDefinition Form { get; }
        public string RelativePath { get; }
        public string Sha256 { get; }
        public long Bytes { get; }
        public string SourceUrl { get; }
    }
}
