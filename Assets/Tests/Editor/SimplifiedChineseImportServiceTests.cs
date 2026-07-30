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

public class SimplifiedChineseImportServiceTests
{
    private string temporaryDirectory;

    [SetUp]
    public void SetUp()
    {
        temporaryDirectory = Path.Combine(
            Path.GetTempPath(), "gacha-zh-cn-import-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(temporaryDirectory))
            Directory.Delete(temporaryDirectory, true);
    }

    [Test]
    public async Task Discover_AssignsGenerationAndChronologicalOrdinal()
    {
        using var client = Client(ProductListFixture());
        using var service = new SimplifiedChineseImportService(
            client, "https://cards.test/api", "https://cards.test/images");

        List<SimplifiedChineseProductRecord> products = await service.DiscoverProductsAsync(
            Options(), CancellationToken.None);

        Assert.That(products.Select(value => value.SetId),
            Is.EqualTo(new[] { "CSM1aC", "SMP", "CSV1C" }));
        Assert.That(products.Select(value => value.GenerationId),
            Is.EqualTo(new[] { "generation-7", "generation-7", "generation-9" }));
        Assert.That(products.Select(value => value.SetOrdinal), Is.EqualTo(new[] { 1, 2, 1 }));
        Assert.That(products.Single(value => value.SetId == "SMP").ReleaseDate, Is.Null);
    }

    [Test]
    public void DefaultConstructor_UsesTransportSupportedByUnityMono()
    {
        Assert.That(() =>
        {
            using var service = new SimplifiedChineseImportService();
        }, Throws.Nothing);
    }

    [Test]
    public async Task Import_WritesRuntimeManifestPngHashesAndResumableCheckpoint()
    {
        var responses = ProductListFixture();
        responses["https://cards.test/api/product-detail"] = Json(@"{
          'code':200,'msg':'OK.','data':{
            'setId':'CSM1aC','setCode':'CSM1aC','name':'横空出世 赫',
            'releaseDate':'2022-10-28T00:00:00+08:00','series':'Sun & Moon',
            'cardsNum':2,'cards':[
              {'setCode':'CSM1aC','cardIndex':'001','cardName':'飞天螳螂','rarity':'C',
               'cardType':'Pokemon','yorenCode':'P123','is':['Basic']},
              {'setCode':'CSM1aC','cardIndex':'002','cardName':'小火龙','rarity':'C',
               'cardType':'Pokemon','yorenCode':'P004','is':['Basic']}
            ]
          }
        }");
        responses["https://cards.test/images/CSM1aC/001.png"] = new byte[] { 1, 2, 3 };
        responses["https://cards.test/images/CSM1aC/002.png"] = new byte[] { 4, 5, 6, 7 };
        using var client = Client(responses);
        using var service = new SimplifiedChineseImportService(
            client, "https://cards.test/api", "https://cards.test/images");

        ContentImportSummary first = await service.ImportAsync(
            new[] { "CSM1aC" }, Options(), cancellationToken: CancellationToken.None);

        Assert.That(first.SetCount, Is.EqualTo(1));
        Assert.That(first.CardCount, Is.EqualTo(2));
        Assert.That(first.ImageBytes, Is.EqualTo(7));
        Assert.That(first.ErrorCount, Is.Zero);
        string manifestPath = Path.Combine(
            temporaryDirectory, "zh-cn", "CSM1aC", "manifest.json");
        PrivateContentManifest manifest = JsonConvert.DeserializeObject<PrivateContentManifest>(
            File.ReadAllText(manifestPath));
        Assert.That(manifest.Source, Is.EqualTo("cryst-simplified-chinese"));
        Assert.That(manifest.Language, Is.EqualTo("zh-cn"));
        Assert.That(manifest.Set.GenerationId, Is.EqualTo("generation-7"));
        Assert.That(manifest.Set.SetOrdinal, Is.EqualTo(1));
        Assert.That(manifest.Cards.Select(value => value.LocalId), Is.EqualTo(new[] { "001", "002" }));
        Assert.That(manifest.Cards.All(value => value.ImageRelativePath.EndsWith(".png")), Is.True);
        Assert.That(manifest.Cards[1].Types, Does.Contain("subject:P004"));
        Assert.That(manifest.Cards.All(value => value.ImageSha256.Length == 64), Is.True);

        ContentImportIntegrityReport audit = ContentImportIntegrityAuditor.Audit(
            temporaryDirectory, "zh-cn", 1);
        Assert.That(audit.IsValid, Is.True);
        Assert.That(audit.ImageFileCount, Is.EqualTo(2));

        ContentImportSummary resumed = await service.ImportAsync(
            new[] { "CSM1aC" }, Options(), cancellationToken: CancellationToken.None);
        Assert.That(resumed.SkippedSetCount, Is.EqualTo(1));
        Assert.That(resumed.SetCount, Is.Zero);
    }

    [Test]
    public async Task Import_MissingSourceImageKeepsCardMetadataAndCompletesSet()
    {
        var responses = ProductListFixture();
        responses["https://cards.test/api/product-detail"] = Json(@"{
          'code':200,'msg':'OK.','data':{
            'setId':'CSM1aC','setCode':'CSM1aC','name':'横空出世 赫',
            'releaseDate':'2022-10-28T00:00:00+08:00','series':'Sun & Moon',
            'cardsNum':1,'cards':[
              {'setCode':'CSM1aC','cardIndex':'GRA','cardName':'基本草能量','rarity':'',
               'cardType':'Energy','yorenCode':'','is':[]}
            ]
          }
        }");
        using var client = Client(responses);
        using var service = new SimplifiedChineseImportService(
            client, "https://cards.test/api", "https://cards.test/images");

        ContentImportSummary summary = await service.ImportAsync(
            new[] { "CSM1aC" }, Options(), cancellationToken: CancellationToken.None);

        Assert.That(summary.SetCount, Is.EqualTo(1));
        Assert.That(summary.CardCount, Is.EqualTo(1));
        Assert.That(summary.ErrorCount, Is.Zero);
        PrivateContentManifest manifest = JsonConvert.DeserializeObject<PrivateContentManifest>(
            File.ReadAllText(Path.Combine(temporaryDirectory, "zh-cn", "CSM1aC", "manifest.json")));
        Assert.That(manifest.Cards.Single().Name, Is.EqualTo("基本草能量"));
        Assert.That(manifest.Cards.Single().ImageRelativePath, Is.Null);
        Assert.That(manifest.Errors, Is.Empty);
        ContentImportIntegrityReport audit = ContentImportIntegrityAuditor.Audit(
            temporaryDirectory, "zh-cn", 1);
        Assert.That(audit.IsValid, Is.True);
        Assert.That(audit.MissingImageReferenceCount, Is.EqualTo(1));
    }

    [Test]
    public async Task Import_PromoUsesUniqueSetIdButCardSetCodeForArtworkUrl()
    {
        var responses = ProductListFixture();
        responses["https://cards.test/api/product-detail"] = Json(@"{
          'code':200,'msg':'OK.','data':{
            'setId':'SMP','setCode':'PROMO','name':'特典卡',
            'releaseDate':'0001-01-01T00:00:00Z','series':'Sun & Moon',
            'cardsNum':1,'cards':[
              {'setCode':'SMP','cardIndex':'001','cardName':'特典卡一','rarity':'PROMO',
               'cardType':'Pokemon','yorenCode':'P001','is':['Basic']}
            ]
          }
        }");
        responses["https://cards.test/images/SMP/001.png"] = new byte[] { 9, 8, 7 };
        using var client = Client(responses);
        using var service = new SimplifiedChineseImportService(
            client, "https://cards.test/api", "https://cards.test/images");

        ContentImportSummary summary = await service.ImportAsync(
            new[] { "SMP" }, Options(), cancellationToken: CancellationToken.None);

        Assert.That(summary.ErrorCount, Is.Zero);
        PrivateContentManifest manifest = JsonConvert.DeserializeObject<PrivateContentManifest>(
            File.ReadAllText(Path.Combine(temporaryDirectory, "zh-cn", "SMP", "manifest.json")));
        Assert.That(manifest.Cards.Single().Id, Is.EqualTo("SMP-001"));
        Assert.That(manifest.Cards.Single().ImageSourceUrl,
            Is.EqualTo("https://cards.test/images/SMP/001.png"));
    }

    [Test]
    public async Task Inventory_IsDeterministicExceptForGeneratedTimestamp()
    {
        using var firstClient = Client(ProductListFixture());
        using var firstService = new SimplifiedChineseImportService(
            firstClient, "https://cards.test/api", "https://cards.test/images");
        SimplifiedChineseSourceInventory first = await firstService.BuildInventoryAsync(temporaryDirectory);
        first.GeneratedAtUtc = "2000-01-01T00:00:00Z";

        Assert.That(first.ProductCount, Is.EqualTo(3));
        Assert.That(first.CardCount, Is.EqualTo(15));
        Assert.That(SimplifiedChineseImportService.InventoryHash(first), Is.EqualTo(first.ContentSha256));
        Assert.That(File.Exists(Path.Combine(temporaryDirectory, "simplified-chinese-inventory.json")), Is.True);
        Assert.That(File.Exists(Path.Combine(temporaryDirectory, "simplified-chinese-inventory.md")), Is.True);
    }

    private SimplifiedChineseImportOptions Options()
    {
        return new SimplifiedChineseImportOptions
        {
            OutputRoot = temporaryDirectory,
            MaxConcurrency = 2,
            RequestIntervalMilliseconds = 0,
            MaximumAttempts = 1,
            RetryBaseDelayMilliseconds = 0
        };
    }

    private static Dictionary<string, byte[]> ProductListFixture()
    {
        return new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["https://cards.test/api/product-list"] = Json(@"{
              'code':200,'msg':'OK.','data':{'list':[
                {'setId':'SMP','name':'特典卡','setCode':'PROMO',
                 'releaseDate':'0001-01-01T00:00:00Z','series':'Sun & Moon','cardsNum':3},
                {'setId':'CSV1C','name':'朱与紫','setCode':'CSV1C',
                 'releaseDate':'2024-01-26T00:00:00+08:00','series':'Scarlet & Violet','cardsNum':10},
                {'setId':'CSM1aC','name':'横空出世 赫','setCode':'CSM1aC',
                 'releaseDate':'2022-10-28T00:00:00+08:00','series':'Sun & Moon','cardsNum':2}
              ]}}
            ")
        };
    }

    private static HttpClient Client(IReadOnlyDictionary<string, byte[]> responses) =>
        new HttpClient(new FixtureHandler(responses));

    private static byte[] Json(string value) =>
        Encoding.UTF8.GetBytes(value.Replace('\'', '"'));

    private sealed class FixtureHandler : HttpMessageHandler
    {
        private readonly IReadOnlyDictionary<string, byte[]> responses;

        public FixtureHandler(IReadOnlyDictionary<string, byte[]> responses)
        {
            this.responses = responses;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
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
