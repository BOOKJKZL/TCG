using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;
using WebP;

public class WebpQualityExperimentTests
{
    private string root;

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), "gacha-webp-quality-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }

    [Test]
    public void Run_IsNonDestructiveAndDeterministicAcrossObservationTimes()
    {
        string firstManifest = WriteSet("set-a", 1,
            Card("set-a-1", "1", "Pokemon", "Common", Image(32, 32, 1)));
        string secondManifest = WriteSet("set-b", 2,
            Card("set-b-1", "1", "Trainer", "Rare", Image(48, 48, 9)));
        string firstManifestHash = Hash(File.ReadAllBytes(firstManifest));
        string secondManifestHash = Hash(File.ReadAllBytes(secondManifest));
        string firstJson = Path.Combine(root, "reports", "first.json");
        string secondJson = Path.Combine(root, "reports", "second.json");
        string firstReview = Path.Combine(root, "review-first");
        string secondReview = Path.Combine(root, "review-second");

        WebpQualityExperimentReport first = WebpQualityExperiment.Run(
            root, "zh-cn", 2, new[] { 95, 85 }, firstJson, null, firstReview, 2,
            "2026-07-31T01:00:00Z");
        WebpQualityExperimentReport second = WebpQualityExperiment.Run(
            root, "zh-cn", 2, new[] { 85, 95 }, secondJson, null, secondReview, 2,
            "2026-07-31T02:00:00Z");

        Assert.That(first.IsValid, Is.True, FailureText(first));
        Assert.That(first.AvailableImageCount, Is.EqualTo(2));
        Assert.That(first.SampleCount, Is.EqualTo(2));
        Assert.That(first.Summaries.Select(value => value.Quality), Is.EqualTo(new[] { 95, 85 }));
        Assert.That(first.Samples.Select(value => value.Stratum).Distinct().Count(), Is.EqualTo(2));
        Assert.That(first.Samples.SelectMany(value => value.Qualities)
            .All(value => !double.IsNaN(value.PsnrDb) && !double.IsInfinity(value.PsnrDb) &&
                          value.PsnrDb > 20d && value.MeanAbsoluteRgbError < 20d &&
                          value.Bytes > 0), Is.True,
            "Re-encoded images must retain source orientation and meaningful visual fidelity.");
        Assert.That(first.SnapshotSha256, Is.EqualTo(second.SnapshotSha256));
        Assert.That(File.ReadAllBytes(firstJson), Is.Not.EqualTo(File.ReadAllBytes(secondJson)),
            "Only the explicit observation time should make the receipts differ.");
        Assert.That(Hash(File.ReadAllBytes(firstManifest)), Is.EqualTo(firstManifestHash));
        Assert.That(Hash(File.ReadAllBytes(secondManifest)), Is.EqualTo(secondManifestHash));
        Assert.That(Directory.GetFiles(firstReview, "*.webp", SearchOption.AllDirectories),
            Has.Length.EqualTo(6));
    }

    [Test]
    public void Run_SelectsRequestedCountAcrossDeterministicStrata()
    {
        for (int index = 0; index < 8; index++)
            WriteSet("set-" + index, index % 4 + 1,
                Card("card-" + index, index.ToString(),
                    index % 2 == 0 ? "Pokemon" : "Trainer",
                    index % 3 == 0 ? "Rare" : "Common",
                    Image(24 + index, 24 + index, (byte)(index + 1))));

        WebpQualityExperimentReport first = WebpQualityExperiment.Run(
            root, "zh-cn", 5, new[] { 90 }, generatedAtUtc: "2026-07-31T01:00:00Z");
        WebpQualityExperimentReport second = WebpQualityExperiment.Run(
            root, "zh-cn", 5, new[] { 90 }, generatedAtUtc: "2026-07-31T01:00:00Z");

        Assert.That(first.IsValid, Is.True, FailureText(first));
        Assert.That(first.SampleCount, Is.EqualTo(5));
        Assert.That(first.Samples.Select(value => value.RecordId),
            Is.EqualTo(second.Samples.Select(value => value.RecordId)));
        Assert.That(first.SnapshotSha256, Is.EqualTo(second.SnapshotSha256));
        Assert.That(first.Summaries.Single().ProjectedTotalBytes, Is.GreaterThan(0));
    }

    [Test]
    public void Run_FailsClosedForManifestImageByteMismatch()
    {
        string manifestPath = WriteSet("bad", 1,
            Card("bad-1", "1", "Pokemon", "Common", Image(16, 16, 3)));
        PrivateContentManifest manifest = JsonConvert.DeserializeObject<PrivateContentManifest>(
            File.ReadAllText(manifestPath));
        manifest.Cards[0].ImageBytes++;
        File.WriteAllText(manifestPath, JsonConvert.SerializeObject(manifest, Formatting.Indented));

        WebpQualityExperimentReport report = WebpQualityExperiment.Run(
            root, "zh-cn", 1, new[] { 90 }, generatedAtUtc: "2026-07-31T01:00:00Z");

        Assert.That(report.IsValid, Is.False);
        Assert.That(report.Failures.Any(value => value.Message.Contains("byte mismatch")), Is.True);
        Assert.That(report.SampleCount, Is.Zero);
    }

    private string WriteSet(string setId, int generationOrder, params FixtureCard[] cards)
    {
        string setRoot = Path.Combine(root, "zh-cn", setId);
        string imageRoot = Path.Combine(setRoot, "images");
        Directory.CreateDirectory(imageRoot);
        var manifest = new PrivateContentManifest
        {
            Language = "zh-cn",
            Source = "fixture",
            Set = new ImportedSetRecord
            {
                Id = setId,
                Name = setId,
                GenerationOrder = generationOrder
            }
        };
        foreach (FixtureCard fixture in cards)
        {
            string imagePath = Path.Combine(imageRoot, fixture.Id + ".webp");
            File.WriteAllBytes(imagePath, fixture.Bytes);
            manifest.Cards.Add(new ImportedCardRecord
            {
                Id = fixture.Id,
                LocalId = fixture.LocalId,
                Name = fixture.Id,
                Category = fixture.Category,
                Rarity = fixture.Rarity,
                ImageRelativePath = Path.Combine("images", fixture.Id + ".webp"),
                ImageBytes = fixture.Bytes.LongLength,
                ImageSha256 = Hash(fixture.Bytes)
            });
        }
        string path = Path.Combine(setRoot, "manifest.json");
        File.WriteAllText(path, JsonConvert.SerializeObject(manifest, Formatting.Indented));
        return path;
    }

    private static FixtureCard Card(
        string id,
        string localId,
        string category,
        string rarity,
        byte[] bytes) =>
        new FixtureCard
        {
            Id = id,
            LocalId = localId,
            Category = category,
            Rarity = rarity,
            Bytes = bytes
        };

    private static byte[] Image(int width, int height, byte seed)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
        try
        {
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                pixels[y * width + x] = new Color32(
                    (byte)(seed + x * 7),
                    (byte)(seed + y * 11),
                    (byte)(seed + (x + y) * 5),
                    255);
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            byte[] bytes = texture.EncodeToWebP(95f, out Error error);
            Assert.That(error, Is.EqualTo(Error.Success));
            return bytes;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    private static string Hash(byte[] bytes)
    {
        using SHA256 sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(bytes).Select(value => value.ToString("x2")));
    }

    private static string FailureText(WebpQualityExperimentReport report) =>
        string.Join("\n", report.Failures.Select(value => value.RecordId + ": " + value.Message));

    private sealed class FixtureCard
    {
        public string Id;
        public string LocalId;
        public string Category;
        public string Rarity;
        public byte[] Bytes;
    }
}
