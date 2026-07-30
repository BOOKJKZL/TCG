using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using NUnit.Framework;

public class ContentImportIntegrityAuditorTests
{
    private string temporaryDirectory;

    [SetUp]
    public void SetUp()
    {
        temporaryDirectory = Path.Combine(
            Path.GetTempPath(), "gacha-import-audit-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(temporaryDirectory))
            Directory.Delete(temporaryDirectory, true);
    }

    [Test]
    public void Audit_ValidManifestRawAndImagePassEveryIntegrityCheck()
    {
        WriteSet("set1", "card1", new byte[] { 1, 2, 3, 4 });
        string outputPath = Path.Combine(temporaryDirectory, "en", "audit.json");

        ContentImportIntegrityReport report = ContentImportIntegrityAuditor.Audit(
            temporaryDirectory, "en", 1, outputPath);

        Assert.That(report.IsValid, Is.True);
        Assert.That(report.SetCount, Is.EqualTo(1));
        Assert.That(report.CardCount, Is.EqualTo(1));
        Assert.That(report.RawCardFileCount, Is.EqualTo(1));
        Assert.That(report.ImageFileCount, Is.EqualTo(1));
        Assert.That(report.ImageBytes, Is.EqualTo(4));
        Assert.That(report.MissingImageReferenceCount, Is.Zero);
        Assert.That(report.OrphanImageFileCount, Is.Zero);
        Assert.That(report.DownloadTempFileCount, Is.Zero);
        Assert.That(report.Failures, Is.Empty);
        Assert.That(File.Exists(outputPath), Is.True);
    }

    [Test]
    public void Audit_CorruptImageAndTraversalPathAreRejected()
    {
        string manifestPath = WriteSet("set1", "card1", new byte[] { 1, 2, 3, 4 });
        PrivateContentManifest manifest = JsonConvert.DeserializeObject<PrivateContentManifest>(
            File.ReadAllText(manifestPath));
        manifest.Cards[0].RawDataRelativePath = Path.Combine("..", "outside.json");
        File.WriteAllBytes(Path.Combine(temporaryDirectory, "en", "set1", "images", "card1.webp"),
            new byte[] { 9, 9 });
        File.WriteAllText(manifestPath, JsonConvert.SerializeObject(manifest, Formatting.Indented));

        ContentImportIntegrityReport report = ContentImportIntegrityAuditor.Audit(
            temporaryDirectory, "en", 1);

        Assert.That(report.IsValid, Is.False);
        Assert.That(report.Failures.Select(item => item.Scope), Does.Contain("raw-path"));
        Assert.That(report.Failures.Select(item => item.Scope), Does.Contain("image-size"));
        Assert.That(report.Failures.Select(item => item.Scope), Does.Contain("image-hash"));
        Assert.That(report.Failures.Select(item => item.Scope), Does.Contain("orphan-raw"));
    }

    [Test]
    public void Audit_DuplicateCardIdsAndTemporaryFilesFail()
    {
        WriteSet("set1", "same-card", null);
        WriteSet("set2", "same-card", null);
        File.WriteAllText(Path.Combine(temporaryDirectory, "en", "unfinished.download"), "partial");

        ContentImportIntegrityReport report = ContentImportIntegrityAuditor.Audit(
            temporaryDirectory, "en", 2);

        Assert.That(report.IsValid, Is.False);
        Assert.That(report.MissingImageReferenceCount, Is.EqualTo(2));
        Assert.That(report.DownloadTempFileCount, Is.EqualTo(1));
        Assert.That(report.Failures.Select(item => item.Scope), Does.Contain("duplicate-card"));
        Assert.That(report.Failures.Select(item => item.Scope), Does.Contain("temporary-files"));
    }

    private string WriteSet(string setId, string cardId, byte[] imageBytes)
    {
        string setDirectory = Path.Combine(temporaryDirectory, "en", setId);
        string rawDirectory = Path.Combine(setDirectory, "raw");
        string rawCardsDirectory = Path.Combine(rawDirectory, "cards");
        string imagesDirectory = Path.Combine(setDirectory, "images");
        Directory.CreateDirectory(rawCardsDirectory);
        Directory.CreateDirectory(imagesDirectory);
        File.WriteAllText(Path.Combine(rawDirectory, "set.json"), "{}");
        File.WriteAllText(Path.Combine(rawCardsDirectory, cardId + ".json"), "{}");

        var card = new ImportedCardRecord
        {
            Id = cardId,
            LocalId = "1",
            Name = "Fixture",
            RawDataRelativePath = Path.Combine("raw", "cards", cardId + ".json")
        };
        if (imageBytes != null)
        {
            string imagePath = Path.Combine(imagesDirectory, cardId + ".webp");
            File.WriteAllBytes(imagePath, imageBytes);
            card.ImageRelativePath = Path.Combine("images", cardId + ".webp");
            card.ImageBytes = imageBytes.LongLength;
            card.ImageSha256 = Hash(imageBytes);
        }

        var manifest = new PrivateContentManifest
        {
            Language = "en",
            Set = new ImportedSetRecord
            {
                Id = setId,
                Name = setId,
                SetCode = setId,
                EraId = "fixture-era",
                GenerationId = "generation-1",
                GenerationOrder = 1,
                SetOrdinal = 1
            }
        };
        manifest.Cards.Add(card);
        string manifestPath = Path.Combine(setDirectory, "manifest.json");
        File.WriteAllText(manifestPath, JsonConvert.SerializeObject(manifest, Formatting.Indented));
        return manifestPath;
    }

    private static string Hash(byte[] bytes)
    {
        using SHA256 sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(bytes).Select(value => value.ToString("x2")));
    }
}
