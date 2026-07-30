using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;

public class SimplifiedChineseImageOptimizerTests
{
    private string temporaryDirectory;

    [SetUp]
    public void SetUp()
    {
        temporaryDirectory = Path.Combine(
            Path.GetTempPath(), "gacha-zh-image-optimizer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(temporaryDirectory))
            Directory.Delete(temporaryDirectory, true);
    }

    [Test]
    public void Optimize_ConvertsPngUpdatesHashDeletesSourceAndIsIdempotent()
    {
        string setDirectory = WriteFixture();
        string reportPath = Path.Combine(temporaryDirectory, "report.json");

        SimplifiedChineseImageOptimizationReport first =
            SimplifiedChineseImageOptimizer.Optimize(
                temporaryDirectory, outputPath: reportPath);

        Assert.That(first.IsValid, Is.True);
        Assert.That(first.SetCount, Is.EqualTo(1));
        Assert.That(first.CardCount, Is.EqualTo(2));
        Assert.That(first.ConvertedImageCount, Is.EqualTo(1));
        Assert.That(first.MissingImageCount, Is.EqualTo(1));
        Assert.That(first.AfterBytes, Is.LessThan(first.BeforeBytes));
        Assert.That(first.SavedBytes, Is.EqualTo(first.BeforeBytes - first.AfterBytes));
        Assert.That(File.Exists(reportPath), Is.True);
        Assert.That(File.Exists(Path.Combine(setDirectory, "images", "card-1.png")), Is.False);
        string webpPath = Path.Combine(setDirectory, "images", "card-1.webp");
        byte[] webp = File.ReadAllBytes(webpPath);
        Assert.That(webp.Take(4), Is.EqualTo(new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' }));
        PrivateContentManifest manifest = JsonConvert.DeserializeObject<PrivateContentManifest>(
            File.ReadAllText(Path.Combine(setDirectory, "manifest.json")));
        ImportedCardRecord card = manifest.Cards.Single(value => value.Id == "card-1");
        Assert.That(card.ImageRelativePath.Replace('\\', '/'), Is.EqualTo("images/card-1.webp"));
        Assert.That(card.ImageBytes, Is.EqualTo(webp.LongLength));
        Assert.That(card.ImageSha256, Is.EqualTo(Hash(webp)));

        SimplifiedChineseImageOptimizationReport second =
            SimplifiedChineseImageOptimizer.Optimize(
                temporaryDirectory, outputPath: reportPath);
        Assert.That(second.IsValid, Is.True);
        Assert.That(second.ConvertedImageCount, Is.Zero);
        Assert.That(second.ExistingWebpImageCount, Is.EqualTo(1));
        Assert.That(second.BeforeBytes, Is.EqualTo(second.AfterBytes));
    }

    private string WriteFixture()
    {
        string setDirectory = Path.Combine(temporaryDirectory, "zh-cn", "set1");
        string imageDirectory = Path.Combine(setDirectory, "images");
        Directory.CreateDirectory(imageDirectory);
        var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        try
        {
            var random = new System.Random(1234);
            var pixels = Enumerable.Range(0, 64 * 64)
                .Select(_ => new Color32(
                    (byte)random.Next(256),
                    (byte)random.Next(256),
                    (byte)random.Next(256),
                    255)).ToArray();
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(Path.Combine(imageDirectory, "card-1.png"), texture.EncodeToPNG());
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }

        var manifest = new PrivateContentManifest
        {
            Language = "zh-cn",
            Set = new ImportedSetRecord
            {
                Id = "set1",
                Name = "Fixture",
                SetCode = "SET1",
                EraId = "fixture",
                GenerationId = "generation-1",
                GenerationOrder = 1,
                SetOrdinal = 1
            }
        };
        manifest.Cards.Add(new ImportedCardRecord
        {
            Id = "card-1",
            LocalId = "1",
            Name = "With image",
            ImageRelativePath = Path.Combine("images", "card-1.png")
        });
        manifest.Cards.Add(new ImportedCardRecord
        {
            Id = "card-2",
            LocalId = "2",
            Name = "Missing source image"
        });
        File.WriteAllText(Path.Combine(setDirectory, "manifest.json"),
            JsonConvert.SerializeObject(manifest, Formatting.Indented));
        return setDirectory;
    }

    private static string Hash(byte[] bytes)
    {
        using SHA256 sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(bytes).Select(value => value.ToString("x2")));
    }
}
