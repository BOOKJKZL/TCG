using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using NUnit.Framework;

public class MultilingualProductionCoverageAuditorTests
{
    private string root;

    [SetUp]
    public void SetUp()
    {
        Images.Clear();
        root = Path.Combine(Path.GetTempPath(), "gacha-multilingual-coverage-tests",
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
    public void Audit_ClassifiesEveryCardAndCountsRealImageFiles()
    {
        WriteManifest("en", "shared", Card("en-card", "1", Bytes(1)));
        WriteManifest("ja", "shared", Card("ja-card", "1", null));
        WriteManifest("zh-cn", "exclusive", Card("zh-card", "7", Bytes(7)));

        MultilingualProductionCoverageReport report = Audit();

        Assert.That(report.IsValid, Is.True, string.Join("\n", report.Failures));
        Assert.That(report.TotalSetCount, Is.EqualTo(3));
        Assert.That(report.TotalCardCount, Is.EqualTo(3));
        Assert.That(report.TotalImageCount, Is.EqualTo(2));
        Assert.That(report.TotalMissingImageCount, Is.EqualTo(1));
        Assert.That(report.DirectCandidateGroupCount, Is.EqualTo(1));
        Assert.That(report.DirectCandidateCardCount, Is.EqualTo(2));
        Assert.That(report.UnmatchedCardCount, Is.EqualTo(1));
        MultilingualCoverageCandidateGroup candidate = report.CandidateGroups.Single();
        Assert.That(candidate.Strategy, Is.EqualTo("same-set-and-local-id"));
        Assert.That(candidate.LanguageCombination, Is.EqualTo("en+ja"));
        Assert.That(report.Cards.Count(value => value.Status == "direct-candidate"), Is.EqualTo(2));
        Assert.That(report.Cards.Single(value => value.Language == "zh-cn").Status,
            Is.EqualTo("unmatched"));
    }

    [Test]
    public void Audit_ReportsIndependentSourceAndImageCandidatesDeterministically()
    {
        byte[] sharedImage = Bytes(9);
        WriteManifest("en", "set-a", Card("shared-source", "1", sharedImage));
        WriteManifest("ja", "set-b", Card("shared-source", "99", sharedImage));
        string firstJson = Path.Combine(root, "reports", "first.json");
        string firstMarkdown = Path.Combine(root, "reports", "first.md");
        string secondJson = Path.Combine(root, "reports", "second.json");
        string secondMarkdown = Path.Combine(root, "reports", "second.md");

        MultilingualProductionCoverageReport first = MultilingualProductionCoverageAuditor.Audit(
            root, new[] { "en", "ja" }, null, firstJson, firstMarkdown);
        MultilingualProductionCoverageReport second = MultilingualProductionCoverageAuditor.Audit(
            root, new[] { "ja", "en" }, null, secondJson, secondMarkdown);

        Assert.That(first.IsValid, Is.True, string.Join("\n", first.Failures));
        Assert.That(first.CandidateGroups.Select(value => value.Strategy), Is.EquivalentTo(new[]
        {
            "same-image-sha256",
            "same-source-card-id"
        }));
        Assert.That(first.SnapshotSha256, Is.EqualTo(second.SnapshotSha256));
        Assert.That(File.ReadAllBytes(firstJson), Is.EqualTo(File.ReadAllBytes(secondJson)));
        Assert.That(File.ReadAllBytes(firstMarkdown), Is.EqualTo(File.ReadAllBytes(secondMarkdown)));
        Assert.That(File.ReadAllText(firstMarkdown), Does.Contain("Snapshot SHA-256"));
    }

    [Test]
    public void Audit_FailsClosedForCountMismatchUnsafeImageAndMissingLanguage()
    {
        string manifestPath = WriteManifest("en", "set-a", Card("card-a", "1", null));
        PrivateContentManifest manifest = JsonConvert.DeserializeObject<PrivateContentManifest>(
            File.ReadAllText(manifestPath));
        manifest.Cards[0].ImageRelativePath = Path.Combine("..", "outside.webp");
        manifest.Cards[0].ImageBytes = 4;
        manifest.Cards[0].ImageSha256 = Hash(Bytes(1));
        File.WriteAllText(manifestPath, JsonConvert.SerializeObject(manifest, Formatting.Indented));

        MultilingualProductionCoverageReport report =
            MultilingualProductionCoverageAuditor.Audit(
                root,
                new[] { "en", "ja" },
                new[] { new MultilingualCoverageExpectation("en", 2, 1) });

        Assert.That(report.IsValid, Is.False);
        Assert.That(report.Failures.Any(value => value.Contains("expected 2 Sets")), Is.True);
        Assert.That(report.Failures.Any(value => value.Contains("directory is missing: ja")), Is.True);
        Assert.That(report.Failures.Any(value => value.Contains("escapes its Set directory")), Is.True);
        Assert.That(report.TotalCardCount, Is.EqualTo(1));
        Assert.That(report.TotalMissingImageCount, Is.EqualTo(1));
    }

    private MultilingualProductionCoverageReport Audit() =>
        MultilingualProductionCoverageAuditor.Audit(
            root,
            new[] { "en", "ja", "zh-cn" },
            new[]
            {
                new MultilingualCoverageExpectation("en", 1, 1),
                new MultilingualCoverageExpectation("ja", 1, 1),
                new MultilingualCoverageExpectation("zh-cn", 1, 1)
            });

    private string WriteManifest(string language, string setId, ImportedCardRecord card)
    {
        string setRoot = Path.Combine(root, language, setId);
        Directory.CreateDirectory(setRoot);
        if (card.ImageRelativePath != null)
        {
            string imagePath = Path.Combine(setRoot, card.ImageRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(imagePath) ?? setRoot);
            File.WriteAllBytes(imagePath, ImageBytes(card.ImageSha256));
        }
        var manifest = new PrivateContentManifest
        {
            Language = language,
            Source = "fixture",
            Set = new ImportedSetRecord
            {
                Id = setId,
                Name = setId,
                SeriesId = "fixture-series",
                ReleaseDate = "2026-01-01"
            }
        };
        manifest.Cards.Add(card);
        string path = Path.Combine(setRoot, "manifest.json");
        File.WriteAllText(path, JsonConvert.SerializeObject(manifest, Formatting.Indented));
        return path;
    }

    private static ImportedCardRecord Card(string id, string localId, byte[] image)
    {
        var card = new ImportedCardRecord
        {
            Id = id,
            LocalId = localId,
            Name = id,
            Category = "Pokemon",
            Rarity = "Common",
            Illustrator = "Fixture"
        };
        if (image != null)
        {
            card.ImageRelativePath = Path.Combine("images", id + ".webp");
            card.ImageBytes = image.LongLength;
            card.ImageSha256 = Hash(image);
            RememberImage(card.ImageSha256, image);
        }
        return card;
    }

    private static readonly System.Collections.Generic.Dictionary<string, byte[]> Images =
        new System.Collections.Generic.Dictionary<string, byte[]>(StringComparer.Ordinal);

    private static void RememberImage(string hash, byte[] bytes)
    {
        Images[hash] = bytes;
    }

    private static byte[] ImageBytes(string hash) => Images[hash];

    private static byte[] Bytes(byte seed) => new[] { seed, (byte)(seed + 1), (byte)(seed + 2) };

    private static string Hash(byte[] bytes)
    {
        using SHA256 sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(bytes).Select(value => value.ToString("x2")));
    }
}
