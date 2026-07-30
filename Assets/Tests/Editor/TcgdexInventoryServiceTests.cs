using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

public class TcgdexInventoryServiceTests
{
    private string temporaryDirectory;

    [SetUp]
    public void SetUp()
    {
        temporaryDirectory = Path.Combine(
            Path.GetTempPath(), "gacha-inventory-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(temporaryDirectory))
            Directory.Delete(temporaryDirectory, true);
    }

    [Test]
    public async Task Build_DiscoversLanguagesReadsEnglishDetailsAndWritesReportsOnly()
    {
        var responses = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["https://api.test/v2/en/sets"] = Json(@"[
              {'id':'future1','name':'Future','cardCount':{'official':1,'total':2}},
              {'id':'base1','name':'Base Set','logo':'https://assets.test/base/logo','cardCount':{'official':2,'total':2}}
            ]"),
            ["https://api.test/v2/fr/sets"] = Json(@"[
              {'id':'base1','name':'Set de Base','cardCount':{'official':2,'total':2}}
            ]"),
            ["https://api.test/v2/en/sets/base1"] = Json(@"{
              'id':'base1','name':'Base Set','releaseDate':'1999-01-09','tcgOnline':'BS',
              'serie':{'id':'base','name':'Base'},'cardCount':{'official':2,'total':2},
              'cards':[
                {'id':'base1-1','localId':'1','name':'One','image':'https://assets.test/base1/1'},
                {'id':'base1-2','localId':'2','name':'Two'}
              ]
            }"),
            ["https://api.test/v2/en/sets/future1"] = Json(@"{
              'id':'future1','name':'Future','releaseDate':'2030-01-01','tcgOnline':'F1',
              'serie':{'id':'future','name':'Future'},'cardCount':{'official':1,'total':2},
              'cards':[{'id':'future1-1','localId':'1','name':'Future One','image':'https://assets.test/future1/1'}]
            }"),
            ["https://assets.test/base1/1/high.jpg"] = new byte[100],
            ["https://assets.test/base1/1/low.webp"] = new byte[40]
        };
        using var client = new HttpClient(new FixtureHandler(responses));
        using var service = new TcgdexInventoryService(client, "https://api.test/v2");

        ContentInventorySnapshot snapshot = await service.BuildAsync(Options("en", "fr"));

        Assert.That(snapshot.Languages.Select(item => item.Language), Is.EqualTo(new[] { "en", "fr" }));
        ContentInventoryLanguageRecord english = snapshot.Languages.Single(item => item.Language == "en");
        ContentInventoryLanguageRecord french = snapshot.Languages.Single(item => item.Language == "fr");
        Assert.That(english.SetCount, Is.EqualTo(2));
        Assert.That(english.DetailedSetCount, Is.EqualTo(2));
        Assert.That(english.CardEntryCount, Is.EqualTo(3));
        Assert.That(english.CardImageCount, Is.EqualTo(2));
        Assert.That(english.MappedSetCount, Is.EqualTo(1));
        Assert.That(english.UnmappedSetCount, Is.EqualTo(1));
        Assert.That(french.Detailed, Is.False);
        Assert.That(french.SetCount, Is.EqualTo(1));
        Assert.That(snapshot.Sets.Select(item => item.Id), Is.EqualTo(new[] { "base1", "future1" }));
        Assert.That(snapshot.ImageEstimate.CompletedSampleCount, Is.EqualTo(1));
        Assert.That(snapshot.ImageEstimate.AverageHighJpegBytes, Is.EqualTo(100));
        Assert.That(snapshot.ImageEstimate.AverageLowWebpBytes, Is.EqualTo(40));
        Assert.That(snapshot.ImageEstimate.ProjectedHighJpegBytes, Is.EqualTo(200));
        Assert.That(snapshot.ImageEstimate.ProjectedLowWebpBytes, Is.EqualTo(80));
        Assert.That(snapshot.Errors, Is.Empty);
        Assert.That(snapshot.ContentSha256, Has.Length.EqualTo(64));
        Assert.That(Directory.GetFiles(temporaryDirectory, "*", SearchOption.AllDirectories)
            .Select(Path.GetFileName).OrderBy(value => value),
            Is.EqualTo(new[] { "tcgdex-inventory.json", "tcgdex-inventory.md" }));
    }

    [Test]
    public async Task Build_HashIgnoresTimestampButChangesWithInventoryContent()
    {
        ContentInventorySnapshot first = FixtureSnapshot("First");
        ContentInventorySnapshot same = FixtureSnapshot("First");
        same.GeneratedAtUtc = "2099-01-01T00:00:00Z";
        ContentInventorySnapshot changed = FixtureSnapshot("Changed");

        string firstHash = TcgdexInventoryService.ComputeContentHash(first);

        Assert.That(TcgdexInventoryService.ComputeContentHash(same), Is.EqualTo(firstHash));
        Assert.That(TcgdexInventoryService.ComputeContentHash(changed), Is.Not.EqualTo(firstHash));
        Assert.That(first.GeneratedAtUtc, Is.EqualTo("2026-01-01T00:00:00Z"));
        await Task.CompletedTask;
    }

    [Test]
    public void SelectEvenSamples_IsStableAndIncludesBothEnds()
    {
        var candidates = Enumerable.Range(1, 10)
            .Reverse()
            .Select(value => new TcgdexInventoryService.ImageCandidate(
                $"card-{value:D2}", $"https://assets.test/{value}"))
            .ToList();

        List<TcgdexInventoryService.ImageCandidate> selected =
            TcgdexInventoryService.SelectEvenSamples(candidates, 4);

        Assert.That(selected.Select(item => item.Id),
            Is.EqualTo(new[] { "card-01", "card-04", "card-07", "card-10" }));
    }

    [Test]
    public void Build_RejectsUnsupportedLanguageBeforeNetworkAccess()
    {
        using var client = new HttpClient(new FixtureHandler(
            new Dictionary<string, byte[]>(StringComparer.Ordinal)));
        using var service = new TcgdexInventoryService(client, "https://api.test/v2");
        ContentInventoryOptions options = Options("en");
        options.Languages.Add("invented");

        Assert.That(async () => await service.BuildAsync(options),
            Throws.TypeOf<ArgumentException>().With.Message.Contains("Unsupported"));
    }

    [Test]
    public async Task Build_DuplicateSetIdsAreReportedAndDeterministicallyDeduplicated()
    {
        var responses = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["https://api.test/v2/en/sets"] = Json(@"[
              {'id':'same','name':'Zulu','cardCount':{'official':1,'total':1}},
              {'id':'same','name':'Alpha','cardCount':{'official':1,'total':1}}
            ]"),
            ["https://api.test/v2/en/sets/same"] = Json(@"{
              'id':'same','name':'Canonical','releaseDate':'2000-01-01',
              'serie':{'id':'sample','name':'Sample'},'cardCount':{'official':1,'total':1},
              'cards':[]
            }")
        };
        using var client = new HttpClient(new FixtureHandler(responses));
        using var service = new TcgdexInventoryService(client, "https://api.test/v2");
        ContentInventoryOptions options = Options("en");
        options.ImageSampleCount = 0;

        ContentInventorySnapshot snapshot = await service.BuildAsync(options);

        Assert.That(snapshot.Languages.Single().SetCount, Is.EqualTo(1));
        Assert.That(snapshot.Sets, Has.Count.EqualTo(1));
        Assert.That(snapshot.Errors, Has.Count.EqualTo(1));
        Assert.That(snapshot.Errors[0].Scope, Is.EqualTo("set-list-duplicate"));
        Assert.That(snapshot.Errors[0].ItemId, Is.EqualTo("en:same"));
    }

    private ContentInventoryOptions Options(params string[] languages)
    {
        return new ContentInventoryOptions
        {
            OutputRoot = temporaryDirectory,
            ReferenceLanguage = "en",
            Languages = languages.ToList(),
            DetailedLanguages = new List<string> { "en" },
            SetGenerationOverridesPath = Path.Combine(
                Directory.GetCurrentDirectory(), "Assets", "Editor", "ContentImporter", "Overrides",
                "set-generation-overrides.json"),
            MaxConcurrency = 2,
            ImageSampleCount = 1
        };
    }

    private static ContentInventorySnapshot FixtureSnapshot(string setName)
    {
        var snapshot = new ContentInventorySnapshot
        {
            ApiRoot = "https://api.test/v2",
            ReferenceLanguage = "en",
            GeneratedAtUtc = "2026-01-01T00:00:00Z"
        };
        snapshot.Sets.Add(new ContentInventorySetRecord
        {
            Language = "en",
            Id = "set1",
            Name = setName
        });
        return snapshot;
    }

    private static byte[] Json(string text)
    {
        return Encoding.UTF8.GetBytes(text.Replace('\'', '"'));
    }

    private sealed class FixtureHandler : HttpMessageHandler
    {
        private readonly IReadOnlyDictionary<string, byte[]> responses;

        public FixtureHandler(IReadOnlyDictionary<string, byte[]> responses)
        {
            this.responses = responses;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!responses.TryGetValue(request.RequestUri.AbsoluteUri, out byte[] body))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(body)
            });
        }
    }
}
