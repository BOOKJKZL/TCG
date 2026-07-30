using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Gacha.Pokemon.Infrastructure;
using NUnit.Framework;

public class PokeApiTaxonomyImportServiceTests
{
    private string temporaryDirectory;

    [SetUp]
    public void SetUp()
    {
        temporaryDirectory = Path.Combine(Path.GetTempPath(), "pokeapi-import-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(temporaryDirectory))
            Directory.Delete(temporaryDirectory, true);
    }

    [Test]
    public async Task Importer_ResumesAtomicResourcesAfterNetworkFailure()
    {
        const string root = "https://poke.test/api/v2/";
        var handler = new FakePokeApiHandler(root, Responses(root));
        handler.FailFirst("/api/v2/pokemon-form/10193/");
        using var client = new HttpClient(handler);
        using var service = new PokeApiTaxonomyImportService(client, root);
        PokeApiTaxonomyImportOptions options = Options();

        Assert.ThrowsAsync<PokeApiTaxonomyImportException>(async () =>
            await service.ImportAsync(options));
        Assert.That(File.Exists(Path.Combine(temporaryDirectory, "raw", "forms", "19.json")), Is.True);
        Assert.That(File.Exists(Path.Combine(temporaryDirectory, "raw", "forms", "10193.json")), Is.False);

        PokeApiTaxonomyImportSummary summary = await service.ImportAsync(options);

        Assert.That(summary.GenerationCount, Is.EqualTo(2));
        Assert.That(summary.SpeciesCount, Is.EqualTo(1));
        Assert.That(summary.PokemonCount, Is.EqualTo(2));
        Assert.That(summary.FormCount, Is.EqualTo(2));
        Assert.That(summary.VersionGroupCount, Is.EqualTo(2));
        Assert.That(summary.DownloadedFileCount, Is.EqualTo(4));
        Assert.That(summary.ReusedFileCount, Is.EqualTo(8));
        Assert.That(summary.SourceSha256, Has.Length.EqualTo(64));
        Assert.That(handler.RequestCount("/api/v2/pokemon-form/19/"), Is.EqualTo(1));
        Assert.That(handler.RequestCount("/api/v2/pokemon-form/10193/"), Is.EqualTo(2));
        Assert.That(Directory.GetFiles(temporaryDirectory, "*.download", SearchOption.AllDirectories), Is.Empty);

        PokemonTaxonomySnapshotLoadResult loaded =
            new PokemonTaxonomySnapshotReader().LoadFile(summary.SnapshotPath);
        Assert.That(loaded.Catalog.Species.Count, Is.EqualTo(1));
        Assert.That(loaded.Catalog.Forms.Count, Is.EqualTo(2));
        PokeApiTaxonomyIntegrityReport audit = PokeApiTaxonomyIntegrityAuditor.Audit(
            temporaryDirectory, options.FormClassificationPath);
        Assert.That(audit.IsValid, Is.False);
        Assert.That(audit.Failures, Has.Some.Contains("Generation 1"));
        PokeApiTaxonomyImportCheckpoint checkpoint = Newtonsoft.Json.JsonConvert
            .DeserializeObject<PokeApiTaxonomyImportCheckpoint>(File.ReadAllText(summary.CheckpointPath));
        Assert.That(checkpoint.Complete, Is.True);
        Assert.That(checkpoint.Failures, Is.Empty);
    }

    private PokeApiTaxonomyImportOptions Options()
    {
        return new PokeApiTaxonomyImportOptions
        {
            OutputRoot = temporaryDirectory,
            FormClassificationPath = Path.Combine(
                Directory.GetCurrentDirectory(), "Assets", "Editor", "ContentImporter", "Overrides",
                "form-classification-overrides.json"),
            MaxConcurrency = 2,
            RequestIntervalMilliseconds = 0,
            MaximumAttempts = 1,
            RetryBaseDelayMilliseconds = 0
        };
    }

    private static Dictionary<string, string> Responses(string root)
    {
        string Url(string resource, int id) => root + resource + "/" + id + "/";
        string List(string resource, params int[] ids) =>
            "{\"count\":" + ids.Length + ",\"next\":null,\"previous\":null,\"results\":[" +
            string.Join(",", ids.Select(id => "{\"name\":\"item\",\"url\":\"" + Url(resource, id) + "\"}")) + "]}";

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/api/v2/generation?limit=100000&offset=0"] = List("generation", 1, 7),
            ["/api/v2/pokemon-species?limit=100000&offset=0"] = List("pokemon-species", 19),
            ["/api/v2/version-group?limit=100000&offset=0"] = List("version-group", 1, 17),
            ["/api/v2/generation/1/"] = "{'id':1,'name':'generation-i','names':[{'name':'Generation I','language':{'name':'en'}},{'name':'第一世代','language':{'name':'zh-hans'}}],'pokemon_species':[{'url':'" + Url("pokemon-species", 19) + "'}]}",
            ["/api/v2/generation/7/"] = "{'id':7,'name':'generation-vii','names':[{'name':'Generation VII','language':{'name':'en'}},{'name':'第七世代','language':{'name':'zh-hans'}}],'pokemon_species':[{'url':'" + Url("pokemon-species", 722) + "'},{'url':'" + Url("pokemon-species", 809) + "'}]}",
            ["/api/v2/version-group/1/"] = "{'id':1,'name':'red-blue','generation':{'name':'generation-i'}}",
            ["/api/v2/version-group/17/"] = "{'id':17,'name':'sun-moon','generation':{'name':'generation-vii'}}",
            ["/api/v2/pokemon-species/19/"] = "{'id':19,'name':'rattata','generation':{'name':'generation-i'},'names':[{'name':'Rattata','language':{'name':'en'}},{'name':'小拉达','language':{'name':'zh-hans'}}],'genera':[{'genus':'Mouse Pokemon','language':{'name':'en'}}],'flavor_text_entries':[{'flavor_text':'Mouse.','language':{'name':'en'}}],'varieties':[{'is_default':true,'pokemon':{'url':'" + Url("pokemon", 19) + "'}},{'is_default':false,'pokemon':{'url':'" + Url("pokemon", 10091) + "'}}],'is_baby':false,'is_legendary':false,'is_mythical':false,'has_gender_differences':false}",
            ["/api/v2/pokemon/19/"] = "{'id':19,'name':'rattata','types':[{'slot':1,'type':{'name':'normal'}}],'forms':[{'url':'" + Url("pokemon-form", 19) + "'}],'sprites':{'other':{'official-artwork':{'front_default':'https://raw.example/19.png'}}}}",
            ["/api/v2/pokemon/10091/"] = "{'id':10091,'name':'rattata-alola','types':[{'slot':1,'type':{'name':'dark'}}],'forms':[{'url':'" + Url("pokemon-form", 10193) + "'}],'sprites':{'other':{'official-artwork':{'front_default':'https://raw.example/10091.png'}}}}",
            ["/api/v2/pokemon-form/19/"] = "{'id':19,'name':'rattata','form_name':'','is_default':true,'is_battle_only':false,'is_mega':false,'version_group':{'name':'red-blue'},'names':[],'sprites':{}}",
            ["/api/v2/pokemon-form/10193/"] = "{'id':10193,'name':'rattata-alola','form_name':'alola','is_default':true,'is_battle_only':false,'is_mega':false,'version_group':{'name':'sun-moon'},'names':[{'name':'Alolan Rattata','language':{'name':'en'}}],'sprites':{}}"
        }.ToDictionary(item => item.Key, item => item.Value.Replace('\'', '"'), StringComparer.Ordinal);
    }

    private sealed class FakePokeApiHandler : HttpMessageHandler
    {
        private readonly string root;
        private readonly IReadOnlyDictionary<string, string> responses;
        private readonly ConcurrentDictionary<string, int> counts = new ConcurrentDictionary<string, int>();
        private readonly ConcurrentDictionary<string, byte> failFirst = new ConcurrentDictionary<string, byte>();

        public FakePokeApiHandler(string root, IReadOnlyDictionary<string, string> responses)
        {
            this.root = root;
            this.responses = responses;
        }

        public void FailFirst(string path) => failFirst[path] = 0;
        public int RequestCount(string path) => counts.TryGetValue(path, out int count) ? count : 0;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.That(request.RequestUri.ToString(), Does.StartWith(root));
            string key = request.RequestUri.PathAndQuery;
            int count = counts.AddOrUpdate(key, 1, (_, value) => value + 1);
            if (failFirst.ContainsKey(key) && count == 1)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            if (!responses.TryGetValue(key, out string content))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            });
        }
    }
}
