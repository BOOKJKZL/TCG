using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

public class MissingCardImageSourceAuditorTests
{
    private string root;

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), "gacha-missing-image-source-tests",
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
    public void Audit_ProbesTcgdexOncePerSetAndSeparatesAvailableFromUnavailable()
    {
        WriteManifest("en", "set-a", "tcgdex",
            Card("set-a-1", "1"), Card("set-a-2", "2"));
        string setUrl = "https://api.tcgdex.net/v2/en/sets/set-a";
        var client = new FakeClient();
        client.GetResponses[setUrl] = new MissingImageHttpResponse
        {
            StatusCode = 200,
            Body = @"{'cards':[
              {'id':'set-a-1','image':'https://assets.tcgdex.net/en/series/set-a/1'},
              {'id':'set-a-2'}
            ]}"
        };

        MissingCardImageSourceAuditReport first = MissingCardImageSourceAuditor.Audit(
            root,
            new[] { "en" },
            new[] { new MissingCardImageExpectation("en", 2) },
            client,
            "2026-07-31T01:00:00Z");
        MissingCardImageSourceAuditReport second = MissingCardImageSourceAuditor.Audit(
            root,
            new[] { "en" },
            new[] { new MissingCardImageExpectation("en", 2) },
            client,
            "2026-07-31T02:00:00Z");

        Assert.That(first.IsValid, Is.True, string.Join("\n", first.Failures));
        Assert.That(first.RemoteRequestCount, Is.EqualTo(1));
        Assert.That(first.AvailableAtSourceCount, Is.EqualTo(1));
        Assert.That(first.SourceUnavailableCount, Is.EqualTo(1));
        Assert.That(first.Entries.Single(value => value.Status == "available-at-source").DownloadUrl,
            Is.EqualTo("https://assets.tcgdex.net/en/series/set-a/1/low.webp"));
        Assert.That(first.SnapshotSha256, Is.EqualTo(second.SnapshotSha256),
            "The observation time must not change the stable source result hash.");
        Assert.That(client.GetCalls.Count(value => value == setUrl), Is.EqualTo(2),
            "Each audit run should issue one Set request, not one request per card.");
    }

    [Test]
    public void Audit_ValidatesDirectHostsAndWritesOnlyUsableSourcesToQueue()
    {
        ImportedCardRecord available = Card("direct-1", "1");
        available.ImageSourceUrl = "https://tcg.mik.moe/cards/1.png";
        ImportedCardRecord missing = Card("direct-2", "2");
        missing.ImageSourceUrl = "https://tcg.mik.moe/cards/2.png";
        ImportedCardRecord invalid = Card("direct-3", "3");
        invalid.ImageSourceUrl = "https://example.com/cards/3.png";
        WriteManifest("zh-cn", "direct", "fixture", available, missing, invalid);
        var client = new FakeClient();
        client.HeadResponses[available.ImageSourceUrl] = new MissingImageHttpResponse
        {
            StatusCode = 200,
            ContentType = "image/png",
            ContentLength = 123
        };
        client.HeadResponses[missing.ImageSourceUrl] = new MissingImageHttpResponse
        {
            StatusCode = 404,
            ContentType = "text/html"
        };
        string queuePath = Path.Combine(root, "reports", "queue.json");

        MissingCardImageSourceAuditReport report = MissingCardImageSourceAuditor.Audit(
            root,
            new[] { "zh-cn" },
            new[] { new MissingCardImageExpectation("zh-cn", 3) },
            client,
            "2026-07-31T01:00:00Z",
            queueOutputPath: queuePath);
        JObject queue = JObject.Parse(File.ReadAllText(queuePath));

        Assert.That(report.IsValid, Is.True, string.Join("\n", report.Failures));
        Assert.That(report.AvailableAtSourceCount, Is.EqualTo(1));
        Assert.That(report.SourceNotFoundCount, Is.EqualTo(1));
        Assert.That(report.InvalidSourceCount, Is.EqualTo(1));
        Assert.That(report.RemoteRequestCount, Is.EqualTo(2));
        Assert.That(client.HeadCalls, Does.Not.Contain(invalid.ImageSourceUrl));
        Assert.That(queue.Value<int>("Count"), Is.EqualTo(1));
        Assert.That(queue["Entries"]?[0]?.Value<string>("RecordId"), Does.Contain("direct-1"));
    }

    [Test]
    public void Audit_FailsClosedForRemoteFailureAndExpectedCountDrift()
    {
        WriteManifest("ja", "set-a", "tcgdex", Card("set-a-1", "1"));
        var client = new FakeClient { ThrowOnGet = true };

        MissingCardImageSourceAuditReport report = MissingCardImageSourceAuditor.Audit(
            root,
            new[] { "ja" },
            new[] { new MissingCardImageExpectation("ja", 2) },
            client,
            "2026-07-31T01:00:00Z");

        Assert.That(report.IsValid, Is.False);
        Assert.That(report.ProbeFailedCount, Is.EqualTo(1));
        Assert.That(report.Failures.Any(value => value.Contains("Set probe failed")), Is.True);
        Assert.That(report.Failures.Any(value => value.Contains("expected 2 missing images")), Is.True);
    }

    [Test]
    public void Audit_RecordsCurrentSourceCardRemovalWithoutGuessingAnImageUrl()
    {
        WriteManifest("en", "set-a", "tcgdex", Card("old-card", "1"));
        string setUrl = "https://api.tcgdex.net/v2/en/sets/set-a";
        var client = new FakeClient();
        client.GetResponses[setUrl] = new MissingImageHttpResponse
        {
            StatusCode = 200,
            Body = "{'cards':[]}"
        };

        MissingCardImageSourceAuditReport report = MissingCardImageSourceAuditor.Audit(
            root,
            new[] { "en" },
            new[] { new MissingCardImageExpectation("en", 1) },
            client,
            "2026-07-31T01:00:00Z");

        Assert.That(report.IsValid, Is.True, string.Join("\n", report.Failures));
        Assert.That(report.SourceCardMissingCount, Is.EqualTo(1));
        Assert.That(report.Entries.Single().DownloadUrl, Is.Null);
    }

    private void WriteManifest(
        string language,
        string setId,
        string source,
        params ImportedCardRecord[] cards)
    {
        string setRoot = Path.Combine(root, language, setId);
        Directory.CreateDirectory(setRoot);
        var manifest = new PrivateContentManifest
        {
            Language = language,
            Source = source,
            Set = new ImportedSetRecord { Id = setId, Name = setId }
        };
        manifest.Cards.AddRange(cards);
        File.WriteAllText(Path.Combine(setRoot, "manifest.json"),
            JsonConvert.SerializeObject(manifest, Formatting.Indented));
    }

    private static ImportedCardRecord Card(string id, string localId) =>
        new ImportedCardRecord
        {
            Id = id,
            LocalId = localId,
            Name = id,
            SourceUrl = "https://api.tcgdex.net/v2/en/cards/" + id
        };

    private sealed class FakeClient : IMissingImageSourceClient
    {
        public readonly Dictionary<string, MissingImageHttpResponse> GetResponses =
            new Dictionary<string, MissingImageHttpResponse>(StringComparer.Ordinal);
        public readonly Dictionary<string, MissingImageHttpResponse> HeadResponses =
            new Dictionary<string, MissingImageHttpResponse>(StringComparer.Ordinal);
        public readonly List<string> GetCalls = new List<string>();
        public readonly List<string> HeadCalls = new List<string>();
        public bool ThrowOnGet;

        public MissingImageHttpResponse Get(string url)
        {
            GetCalls.Add(url);
            if (ThrowOnGet) throw new IOException("fixture network failure");
            return GetResponses[url];
        }

        public MissingImageHttpResponse Head(string url)
        {
            HeadCalls.Add(url);
            return HeadResponses[url];
        }
    }
}
