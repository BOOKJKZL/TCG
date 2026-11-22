using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Gacha.Pokemon.Infrastructure;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;

public sealed class PokemonArtworkImportServiceTests
{
    private const string SpriteBase = "https://raw.githubusercontent.com/PokeAPI/sprites/fixture/";
    private const string Generation = "generation-1";
    private static readonly string TaxonomyHash = new string('a', 64);

    [Test]
    public void PngValidation_RequiresFullSignature()
    {
        Assert.That(PokemonArtworkImportService.IsPng(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), Is.True);
        Assert.That(PokemonArtworkImportService.IsPng(new byte[] { 137, 80, 78, 71 }), Is.False);
        Assert.That(PokemonArtworkImportService.IsPng(new byte[] { 0, 80, 78, 71, 13, 10, 26, 10 }), Is.False);
    }

    [Test]
    public void PortableFormName_RemovesArchiveUnsafeColon()
    {
        Assert.That(PokemonArtworkImportService.PortableFormName("pokemon-form:10091"),
            Is.EqualTo("pokemon-form-10091"));
    }

    [Test]
    public async Task Import_RestartsAfterPartialDownloadAndVerifiesManifestAgainstSavedImages()
    {
        string root = Path.Combine(Path.GetTempPath(), "artwork-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string taxonomy = Path.Combine(root, "taxonomy.json");
            WriteTaxonomy(taxonomy);
            var options = new PokemonArtworkImportOptions
            {
                TaxonomySnapshotPath = taxonomy, OutputRoot = Path.Combine(root, "art"),
                MaxConcurrency = 1, MaximumAttempts = 1, RequestIntervalMilliseconds = 0
            };
            var handler = new SpriteResponses();
            handler.Images.Add(SpriteBase + "1.png", Png(Color.red));
            handler.Images.Add(SpriteBase + "2.png", Png(Color.green));
            using var client = new HttpClient(handler);
            using var cancellation = new CancellationTokenSource();
            var progress = new ImportProgress();
            using (var first = new PokemonArtworkImportService(client))
            {
                try
                {
                    await first.ImportAsync(options, progress, cancellation.Token);
                    Assert.Fail("Import should report the outstanding second image.");
                }
                catch (InvalidDataException error)
                {
                    Assert.That(error.Message, Does.Contain("1 failed images"));
                }
            }
            string generationRoot = Path.Combine(options.OutputRoot, Generation);
            string manifestPath = Path.Combine(generationRoot, "manifest.json");
            string checkpointPath = Path.Combine(options.OutputRoot, "artwork-import-checkpoint.json");
            PokemonArtworkImportCheckpoint Checkpoint() =>
                JsonConvert.DeserializeObject<PokemonArtworkImportCheckpoint>(File.ReadAllText(checkpointPath));
            var interrupted = Checkpoint();
            Assert.That(interrupted.Complete, Is.False);
            Assert.That(interrupted.TotalForms, Is.EqualTo(3));
            Assert.That(interrupted.CompletedImages, Is.EqualTo(1));
            Assert.That(interrupted.MissingSourceImages, Is.EqualTo(1));
            Assert.That(interrupted.Failures.Single(), Does.StartWith("pokemon-form:2:"));
            Assert.That(File.Exists(manifestPath), Is.False);
            Assert.That(File.ReadAllBytes(Path.Combine(generationRoot, "images", "pokemon-form-1.png")),
                Is.EqualTo(handler.Images[SpriteBase + "1.png"]));
            Assert.That(File.Exists(Path.Combine(generationRoot, "images", "pokemon-form-2.png")), Is.False);
            Assert.That(handler.Requests, Is.EqualTo(new[] { SpriteBase + "1.png", SpriteBase + "2.png" }));

            handler.FailSecond = false;
            progress = new ImportProgress();
            using var resumed = new PokemonArtworkImportService(client);
            var completed = await resumed.ImportAsync(options, progress, cancellation.Token);
            Assert.That(completed.FormCount, Is.EqualTo(3));
            Assert.That(completed.ImageCount, Is.EqualTo(2));
            Assert.That(completed.MissingSourceCount, Is.EqualTo(1));
            Assert.That(completed.DownloadedCount, Is.EqualTo(1));
            Assert.That(completed.ReusedCount, Is.EqualTo(1));
            Assert.That(completed.ImageBytes, Is.EqualTo(handler.Images.Values.Sum(bytes => bytes.LongLength)));
            Assert.That(progress.Events.Select(e => e.FormId), Is.EquivalentTo(new[] { "pokemon-form:1", "pokemon-form:2", "pokemon-form:3" }));
            Assert.That(progress.Events.Select(e => e.Completed), Is.EquivalentTo(new[] { 1, 2, 3 }));
            Assert.That(progress.Events.All(e => e.Total == 3), Is.True);
            Assert.That(handler.Requests, Is.EqualTo(new[] { SpriteBase + "1.png", SpriteBase + "2.png", SpriteBase + "2.png" }));
            var manifest = new PokemonArtworkManifestReader().LoadFile(manifestPath);
            Assert.That(manifest.TaxonomySourceSha256, Is.EqualTo(TaxonomyHash));
            Assert.That(manifest.Entries.Count, Is.EqualTo(2));
            Assert.That(manifest.MissingFormIds, Is.EqualTo(new[] { "pokemon-form:3" }));
            for (int index = 1; index <= 2; index++)
            {
                var entry = manifest.Find("pokemon-form:" + index);
                byte[] bytes = File.ReadAllBytes(Path.Combine(generationRoot, entry.RelativePath));
                Assert.That(entry.SourceUri.AbsoluteUri, Is.EqualTo(SpriteBase + index + ".png"));
                Assert.That(bytes, Is.EqualTo(handler.Images[entry.SourceUri.AbsoluteUri]));
                Assert.That(entry.Bytes, Is.EqualTo(bytes.LongLength));
                using var sha = SHA256.Create();
                Assert.That(entry.Sha256, Is.EqualTo(BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant()));
            }
            var final = Checkpoint();
            Assert.That(final.Complete, Is.True);
            Assert.That(final.Failures, Is.Empty);
            Assert.That(final.CompletedImages, Is.EqualTo(2));
            Assert.That(final.MissingSourceImages, Is.EqualTo(1));
            Assert.That(Directory.GetFiles(root, "*.download", SearchOption.AllDirectories), Is.Empty);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestCase(false, true)]
    [TestCase(true, true)]
    [TestCase(true, false)]
    public async Task Import_ReusesImagesOnlyForTheirRecordedSource(bool interruptFirstImport, bool changeSource)
    {
        string root = Path.Combine(Path.GetTempPath(), "artwork-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string taxonomy = Path.Combine(root, "taxonomy.json");
            WriteTaxonomy(taxonomy);
            var options = new PokemonArtworkImportOptions
            {
                TaxonomySnapshotPath = taxonomy, OutputRoot = Path.Combine(root, "art"),
                MaxConcurrency = 1, MaximumAttempts = 1, RequestIntervalMilliseconds = 0
            };
            var handler = new SpriteResponses { FailSecond = interruptFirstImport };
            handler.Images.Add(SpriteBase + "1.png", Png(Color.red));
            handler.Images.Add(SpriteBase + "alternate.png", Png(Color.blue));
            handler.Images.Add(SpriteBase + "2.png", Png(Color.green));
            using var client = new HttpClient(handler);
            async Task<PokemonArtworkImportSummary> Import()
            {
                using var importer = new PokemonArtworkImportService(client);
                return await importer.ImportAsync(options, null, CancellationToken.None);
            }

            string generationRoot = Path.Combine(options.OutputRoot, Generation);
            string manifestPath = Path.Combine(generationRoot, "manifest.json");
            if (interruptFirstImport)
            {
                try
                {
                    await Import();
                    Assert.Fail("The second request must interrupt the first import.");
                }
                catch (InvalidDataException error)
                {
                    Assert.That(error.Message, Does.Contain("1 failed images"));
                }
            }
            else
                await Import();
            Assert.That(File.Exists(manifestPath), Is.EqualTo(!interruptFirstImport));
            string savedImage = Path.Combine(generationRoot, "images", "pokemon-form-1.png");
            Assert.That(File.ReadAllBytes(savedImage), Is.EqualTo(handler.Images[SpriteBase + "1.png"]));

            string fileName = changeSource ? "alternate.png" : "1.png";
            string taxonomyHash = changeSource ? new string('b', 64) : TaxonomyHash;
            string url = SpriteBase + fileName;
            WriteTaxonomySource(taxonomy, fileName, taxonomyHash);
            handler.FailSecond = false;
            handler.Requests.Clear();
            await Import();
            Assert.That(handler.Requests.Count(request => request == url), Is.EqualTo(changeSource ? 1 : 0));
            if (interruptFirstImport)
                Assert.That(handler.Requests.Count(request => request == SpriteBase + "2.png"), Is.EqualTo(1));
            var manifest = new PokemonArtworkManifestReader().LoadFile(manifestPath);
            var record = manifest.Find("pokemon-form:1");
            Assert.That(manifest.TaxonomySourceSha256, Is.EqualTo(taxonomyHash));
            Assert.That(record.SourceUri.AbsoluteUri, Is.EqualTo(url));
            byte[] saved = File.ReadAllBytes(Path.Combine(generationRoot, record.RelativePath));
            Assert.That(saved, Is.EqualTo(handler.Images[url]));
            Assert.That(record.Bytes, Is.EqualTo(saved.LongLength));
            using var sha = SHA256.Create();
            Assert.That(record.Sha256, Is.EqualTo(BitConverter.ToString(sha.ComputeHash(saved)).Replace("-", "").ToLowerInvariant()));

            handler.Requests.Clear();
            var repeated = await Import();
            Assert.That(handler.Requests, Is.Empty);
            Assert.That(repeated.ReusedCount, Is.EqualTo(2));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static void WriteTaxonomy(string path) => WriteTaxonomySource(path, "1.png", TaxonomyHash);

    private static void WriteTaxonomySource(string path, string firstSprite, string sourceHash)
    {
        var names = new Dictionary<string, string> { ["en"] = "Fixture" };
        var snapshot = new PokemonTaxonomySnapshotDto
        {
            Source = "pokeapi", SourceBaseUrl = "https://pokeapi.co/api/v2/", CapturedAtUtc = "2026-01-01T00:00:00Z",
            SourceSha256 = sourceHash, Languages = new List<string> { "en" }
        };
        snapshot.Generations.Add(new PokemonGenerationSnapshotDto
        {
            Id = Generation, Order = 1, Names = names, SpeciesStartNumber = 1, SpeciesEndNumber = 3
        });
        for (int index = 1; index <= 3; index++)
        {
            string form = "pokemon-form:" + index;
            string species = "pokemon-species:" + index;
            snapshot.Species.Add(new PokemonSpeciesSnapshotDto
            {
                Id = species, NationalDexNumber = index, DebutGenerationId = Generation, Names = names,
                DefaultFormId = form, FormIds = new List<string> { form }
            });
            snapshot.Forms.Add(new PokemonFormSnapshotDto
            {
                Id = form, SpeciesId = species, PokemonId = index, Names = names, IsDefault = true,
                IntroducedGenerationId = Generation, FormKind = "default", Disposition = "separate-entry",
                ImageSourceUrl = index == 3 ? null : SpriteBase + (index == 1 ? firstSprite : "2.png")
            });
        }
        File.WriteAllText(path, JsonConvert.SerializeObject(snapshot));
    }

    private static byte[] Png(Color color)
    {
        var image = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        try
        {
            image.SetPixel(0, 0, color);
            image.Apply();
            return image.EncodeToPNG();
        }
        finally { UnityEngine.Object.DestroyImmediate(image); }
    }

    private sealed class ImportProgress : IProgress<PokemonArtworkImportProgress>
    {
        public ConcurrentQueue<PokemonArtworkImportProgress> Events { get; } = new ConcurrentQueue<PokemonArtworkImportProgress>();
        public void Report(PokemonArtworkImportProgress value) => Events.Enqueue(value);
    }

    private sealed class SpriteResponses : HttpMessageHandler
    {
        public Dictionary<string, byte[]> Images { get; } = new Dictionary<string, byte[]>();
        public List<string> Requests { get; } = new List<string>();
        public bool FailSecond { get; set; } = true;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string url = request.RequestUri.AbsoluteUri;
            Requests.Add(url);
            if (request.Method != HttpMethod.Get || !Images.TryGetValue(url, out byte[] png))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            if (FailSecond && url == SpriteBase + "2.png")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(png) });
        }
    }
}
