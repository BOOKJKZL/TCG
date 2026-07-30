using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;

public class TcgdexBulkImportTests
{
    private string temporaryDirectory;

    [SetUp]
    public void SetUp()
    {
        temporaryDirectory = Path.Combine(
            Path.GetTempPath(), "gacha-bulk-import-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(temporaryDirectory))
            Directory.Delete(temporaryDirectory, true);
    }

    [Test]
    public async Task ImportSets_RerunReusesCardMetadataAndImageWithStableHash()
    {
        var handler = SuccessfulHandler();
        using var client = new HttpClient(handler);
        ContentImportSummary first;
        ContentImportSummary second;
        using (var service = new TcgdexImportService(client, "https://api.test/v2"))
            first = await service.ImportSetsAsync(new[] { "base1" }, Options());
        using (var service = new TcgdexImportService(client, "https://api.test/v2"))
            second = await service.ImportSetsAsync(new[] { "base1" }, Options());

        Assert.That(first.SetCount, Is.EqualTo(1));
        Assert.That(first.CardCount, Is.EqualTo(1));
        Assert.That(first.ReusedMetadataCount, Is.Zero);
        Assert.That(first.ReusedImageCount, Is.Zero);
        Assert.That(first.ErrorCount, Is.Zero);
        Assert.That(second.ReusedMetadataCount, Is.EqualTo(1));
        Assert.That(second.ReusedImageCount, Is.EqualTo(1));
        Assert.That(handler.Count("https://api.test/v2/en/sets/base1"), Is.EqualTo(2));
        Assert.That(handler.Count("https://api.test/v2/en/cards/base1-1"), Is.EqualTo(1));
        Assert.That(handler.Count("https://assets.test/base1/1/low.webp"), Is.EqualTo(1));

        string setDirectory = Path.Combine(temporaryDirectory, "en", "base1");
        PrivateContentManifest manifest = JsonConvert.DeserializeObject<PrivateContentManifest>(
            File.ReadAllText(Path.Combine(setDirectory, "manifest.json")));
        Assert.That(manifest.SchemaVersion, Is.EqualTo(2));
        Assert.That(manifest.Set.GenerationId, Is.EqualTo("generation-1"));
        Assert.That(manifest.Cards.Single().ImageRelativePath, Is.EqualTo(Path.Combine("images", "base1-1.webp")));
        Assert.That(manifest.Cards.Single().ImageSha256, Has.Length.EqualTo(64));
        Assert.That(File.ReadAllBytes(Path.Combine(setDirectory, "images", "base1-1.webp")),
            Is.EqualTo(new byte[] { 1, 2, 3, 4, 5 }));

        ContentImportCheckpoint checkpoint = JsonConvert.DeserializeObject<ContentImportCheckpoint>(
            File.ReadAllText(second.CheckpointPath));
        Assert.That(checkpoint.Sets.Single().State, Is.EqualTo("completed"));
        Assert.That(checkpoint.Sets.Single().ProcessedCards, Is.EqualTo(1));
        ContentImportFailureReport failures = JsonConvert.DeserializeObject<ContentImportFailureReport>(
            File.ReadAllText(second.FailureReportPath));
        Assert.That(failures.Failures, Is.Empty);
    }

    [Test]
    public async Task ImportSets_PermanentCardFailureIsCheckpointedAndDoesNotAbortOtherSets()
    {
        var handler = new FixtureHandler();
        handler.AddJson("https://api.test/v2/en/sets/base1", SetJson());
        handler.AddStatus("https://api.test/v2/en/cards/base1-1", HttpStatusCode.ServiceUnavailable);
        using var client = new HttpClient(handler);
        ContentImportOptions options = Options();
        options.MaximumAttempts = 2;

        ContentImportSummary summary;
        using (var service = new TcgdexImportService(client, "https://api.test/v2"))
            summary = await service.ImportSetsAsync(new[] { "base1" }, options);

        Assert.That(summary.SetCount, Is.EqualTo(1));
        Assert.That(summary.CardCount, Is.Zero);
        Assert.That(summary.ErrorCount, Is.EqualTo(1));
        Assert.That(summary.FailedSetCount, Is.Zero);
        Assert.That(handler.Count("https://api.test/v2/en/cards/base1-1"), Is.EqualTo(2));
        ContentImportCheckpoint checkpoint = JsonConvert.DeserializeObject<ContentImportCheckpoint>(
            File.ReadAllText(summary.CheckpointPath));
        Assert.That(checkpoint.Sets.Single().State, Is.EqualTo("completed-with-errors"));
        Assert.That(checkpoint.Failures.Single().Scope, Is.EqualTo("card"));
        ContentImportFailureReport report = JsonConvert.DeserializeObject<ContentImportFailureReport>(
            File.ReadAllText(summary.FailureReportPath));
        Assert.That(report.Failures, Has.Count.EqualTo(1));
        Assert.That(File.Exists(Path.Combine(
            temporaryDirectory, "en", "base1", "manifest.json")), Is.True);
    }

    [Test]
    public async Task ImportSets_TransientFailureRetriesThenCompletes()
    {
        var handler = SuccessfulHandler();
        handler.PrependStatus(
            "https://api.test/v2/en/cards/base1-1", HttpStatusCode.TooManyRequests);
        using var client = new HttpClient(handler);

        ContentImportSummary summary;
        using (var service = new TcgdexImportService(client, "https://api.test/v2"))
            summary = await service.ImportSetsAsync(new[] { "base1" }, Options());

        Assert.That(summary.ErrorCount, Is.Zero);
        Assert.That(handler.Count("https://api.test/v2/en/cards/base1-1"), Is.EqualTo(2));
    }

    private ContentImportOptions Options()
    {
        return new ContentImportOptions
        {
            Language = "en",
            OutputRoot = temporaryDirectory,
            SetGenerationOverridesPath = Path.Combine(
                Directory.GetCurrentDirectory(), "Assets", "Editor", "ContentImporter", "Overrides",
                "set-generation-overrides.json"),
            ImageQuality = "low",
            ImageExtension = "webp",
            MaxConcurrency = 2,
            MaximumCardsPerSet = 0,
            RefreshExistingFiles = false,
            RequestIntervalMilliseconds = 0,
            MaximumAttempts = 3,
            RetryBaseDelayMilliseconds = 0
        };
    }

    private static FixtureHandler SuccessfulHandler()
    {
        var handler = new FixtureHandler();
        handler.AddJson("https://api.test/v2/en/sets/base1", SetJson());
        handler.AddJson("https://api.test/v2/en/cards/base1-1", @"{
          'id':'base1-1','localId':'1','name':'Alakazam','category':'Pokemon',
          'rarity':'Rare Holo','illustrator':'Ken Sugimori','updated':'2026-01-01',
          'image':'https://assets.test/base1/1','types':['Psychic'],
          'variants':{'normal':false,'reverse':false,'holo':true,'firstEdition':true,'wPromo':false}
        }");
        handler.AddBytes("https://assets.test/base1/1/low.webp", new byte[] { 1, 2, 3, 4, 5 });
        return handler;
    }

    private static string SetJson()
    {
        return @"{
          'id':'base1','name':'Base Set','releaseDate':'1999-01-09','tcgOnline':'BS',
          'serie':{'id':'base','name':'Base'},'cardCount':{'official':1,'total':1},
          'cards':[{'id':'base1-1','localId':'1','name':'Alakazam','image':'https://assets.test/base1/1'}]
        }";
    }

    private sealed class FixtureHandler : HttpMessageHandler
    {
        private readonly object gate = new object();
        private readonly Dictionary<string, Queue<Response>> responses =
            new Dictionary<string, Queue<Response>>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> counts =
            new Dictionary<string, int>(StringComparer.Ordinal);

        public void AddJson(string url, string json)
        {
            AddBytes(url, Encoding.UTF8.GetBytes(json.Replace('\'', '"')));
        }

        public void AddBytes(string url, byte[] bytes)
        {
            Add(url, new Response(HttpStatusCode.OK, bytes));
        }

        public void AddStatus(string url, HttpStatusCode status)
        {
            Add(url, new Response(status, Array.Empty<byte>()));
        }

        public void PrependStatus(string url, HttpStatusCode status)
        {
            lock (gate)
            {
                Queue<Response> existing = responses[url];
                responses[url] = new Queue<Response>(
                    new[] { new Response(status, Array.Empty<byte>()) }.Concat(existing));
            }
        }

        public int Count(string url)
        {
            lock (gate)
                return counts.TryGetValue(url, out int count) ? count : 0;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Response response;
            lock (gate)
            {
                string url = request.RequestUri.AbsoluteUri;
                counts[url] = Count(url) + 1;
                if (!responses.TryGetValue(url, out Queue<Response> queue) || queue.Count == 0)
                    response = new Response(HttpStatusCode.NotFound, Array.Empty<byte>());
                else
                {
                    response = queue.Peek();
                    if (queue.Count > 1)
                        queue.Dequeue();
                }
            }
            return Task.FromResult(new HttpResponseMessage(response.Status)
            {
                Content = new ByteArrayContent(response.Body)
            });
        }

        private void Add(string url, Response response)
        {
            lock (gate)
            {
                if (!responses.TryGetValue(url, out Queue<Response> queue))
                {
                    queue = new Queue<Response>();
                    responses.Add(url, queue);
                }
                queue.Enqueue(response);
            }
        }

        private sealed class Response
        {
            public Response(HttpStatusCode status, byte[] body)
            {
                Status = status;
                Body = body;
            }

            public HttpStatusCode Status { get; }
            public byte[] Body { get; }
        }
    }
}
