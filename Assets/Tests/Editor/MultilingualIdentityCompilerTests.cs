using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;

public class MultilingualIdentityCompilerTests
{
    private const string CoverageSnapshot =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private string root;
    private string setMappings;
    private string cardOverrides;

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), "gacha-identity-compiler-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        setMappings = Write("sets.json", new CrossRegionSetMappingFile
        {
            SourceCoverageSnapshotSha256 = CoverageSnapshot
        });
        cardOverrides = Write("cards.json", new MultilingualCardIdentityOverrideFile
        {
            SourceCoverageSnapshotSha256 = CoverageSnapshot
        });
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }

    [Test]
    public void Compile_AutoAcceptsOnlyIndependentStrongSignalsAndClassifiesEveryCard()
    {
        MultilingualCoverageCardRecord enAccepted = Card("en/accepted", "en", "neo1", "x-1", "1");
        MultilingualCoverageCardRecord jaAccepted = Card("ja/accepted", "ja", "neo1", "x-1", "1");
        MultilingualCoverageCardRecord enPending = Card("en/pending", "en", "smp", "en-p-1", "1");
        MultilingualCoverageCardRecord zhPending = Card("zh/pending", "zh-cn", "smp", "zh-p-1", "1");
        MultilingualCoverageCardRecord enSolo = Card("en/solo", "en", "solo", "solo-en", "8");
        MultilingualCoverageCardRecord jaSolo = Card("ja/solo", "ja", "solo-ja", "solo-ja", "9");
        MultilingualProductionCoverageReport coverage = Coverage(
            new[] { enAccepted, jaAccepted, enPending, zhPending, enSolo, jaSolo },
            Candidate(MultilingualIdentityCompiler.SetLocalSignal, enAccepted, jaAccepted),
            Candidate(MultilingualIdentityCompiler.SourceCardSignal, enAccepted, jaAccepted),
            Candidate(MultilingualIdentityCompiler.SetLocalSignal, enPending, zhPending));

        string queuePath = Path.Combine(root, "review-queue.json");
        MultilingualIdentityCompilationReport report = MultilingualIdentityCompiler.Compile(
            coverage, setMappings, cardOverrides, null, null, queuePath);
        MultilingualIdentityReviewQueue queue =
            JsonConvert.DeserializeObject<MultilingualIdentityReviewQueue>(
                File.ReadAllText(queuePath));

        Assert.That(report.IsValid, Is.True, string.Join("\n", report.Failures));
        Assert.That(report.CandidateGroupCount, Is.EqualTo(2));
        Assert.That(report.AutoAcceptedGroupCount, Is.EqualTo(1));
        Assert.That(report.AutoAcceptedCardCount, Is.EqualTo(2));
        Assert.That(report.PendingReviewGroupCount, Is.EqualTo(1));
        Assert.That(report.PendingReviewCardCount, Is.EqualTo(2));
        Assert.That(report.UnmatchedCardCount, Is.EqualTo(2));
        Assert.That(report.Cards, Has.Count.EqualTo(6));
        Assert.That(queue.PendingGroupCount, Is.EqualTo(1));
        Assert.That(queue.Groups.Single().Members, Has.Count.EqualTo(2));
        Assert.That(queue.Groups.Single().Members.Select(value => value.Language),
            Is.EqualTo(new[] { "en", "zh-cn" }));
        Assert.That(report.Cards.Single(value => value.RecordId == enPending.RecordId)
            .SemanticFingerprintSha256, Is.EqualTo(report.Cards
                .Single(value => value.RecordId == zhPending.RecordId).SemanticFingerprintSha256));
    }

    [Test]
    public void Compile_ReplaysReviewedAcceptAndRejectDeterministically()
    {
        MultilingualCoverageCardRecord enAccepted = Card("en/a", "en", "set-a", "a", "1");
        MultilingualCoverageCardRecord jaAccepted = Card("ja/a", "ja", "set-b", "b", "9");
        MultilingualCoverageCardRecord enRejected = Card("en/r", "en", "set-r", "r1", "2");
        MultilingualCoverageCardRecord zhRejected = Card("zh/r", "zh-cn", "set-z", "r2", "8");
        cardOverrides = Write("reviewed.json", new MultilingualCardIdentityOverrideFile
        {
            SourceCoverageSnapshotSha256 = CoverageSnapshot,
            Decisions = new List<MultilingualCardIdentityOverride>
            {
                Decision("accept-a", "accept", enAccepted, jaAccepted),
                Decision("reject-r", "reject", enRejected, zhRejected)
            }
        });
        MultilingualProductionCoverageReport firstCoverage = Coverage(
            new[] { enAccepted, jaAccepted, enRejected, zhRejected });
        MultilingualProductionCoverageReport secondCoverage = Coverage(
            new[] { zhRejected, enRejected, jaAccepted, enAccepted });
        string firstJson = Path.Combine(root, "first.json");
        string secondJson = Path.Combine(root, "second.json");
        string firstQueue = Path.Combine(root, "first-queue.json");
        string secondQueue = Path.Combine(root, "second-queue.json");

        MultilingualIdentityCompilationReport first = MultilingualIdentityCompiler.Compile(
            firstCoverage, setMappings, cardOverrides, firstJson, null, firstQueue);
        MultilingualIdentityCompilationReport second = MultilingualIdentityCompiler.Compile(
            secondCoverage, setMappings, cardOverrides, secondJson, null, secondQueue);

        Assert.That(first.ReviewedAcceptedGroupCount, Is.EqualTo(1));
        Assert.That(first.ReviewedAcceptedCardCount, Is.EqualTo(2));
        Assert.That(first.ReviewedRejectedGroupCount, Is.EqualTo(1));
        Assert.That(first.ReviewedRejectedCardCount, Is.EqualTo(2));
        Assert.That(first.SnapshotSha256, Is.EqualTo(second.SnapshotSha256));
        Assert.That(File.ReadAllBytes(firstJson), Is.EqualTo(File.ReadAllBytes(secondJson)));
        Assert.That(File.ReadAllBytes(firstQueue), Is.EqualTo(File.ReadAllBytes(secondQueue)));
    }

    [Test]
    public void Loaders_RejectStaleSnapshotAndCompilerRejectsSameLanguageAcceptance()
    {
        string stale = Write("stale.json", new CrossRegionSetMappingFile
        {
            SourceCoverageSnapshotSha256 = new string('b', 64)
        });
        InvalidDataException staleException = Assert.Throws<InvalidDataException>(() =>
            MultilingualIdentityCompiler.LoadSetMappings(stale, CoverageSnapshot));
        Assert.That(staleException.Message, Does.Contain("does not match coverage"));

        MultilingualCoverageCardRecord first = Card("en/1", "en", "set-a", "a", "1");
        MultilingualCoverageCardRecord second = Card("en/2", "en", "set-b", "b", "2");
        cardOverrides = Write("same-language.json", new MultilingualCardIdentityOverrideFile
        {
            SourceCoverageSnapshotSha256 = CoverageSnapshot,
            Decisions = new List<MultilingualCardIdentityOverride>
            {
                Decision("invalid", "accept", first, second)
            }
        });

        InvalidDataException languageException = Assert.Throws<InvalidDataException>(() =>
            MultilingualIdentityCompiler.Compile(
                Coverage(new[] { first, second }), setMappings, cardOverrides));
        Assert.That(languageException.Message, Does.Contain("more than one card for a language"));
    }

    [Test]
    public void Compile_UsesReviewedSetRelationshipsWithoutTreatingSetEvidenceAsSufficientAlone()
    {
        MultilingualCoverageCardRecord english = Card("en/set-card", "en", "set-a", "shared", "1");
        MultilingualCoverageCardRecord japanese = Card("ja/set-card", "ja", "set-b", "shared", "1");
        CrossRegionSetMapping mapping = new CrossRegionSetMapping
        {
            Id = "reviewed-sets",
            Relationship = "equivalent",
            ReviewStatus = "reviewed",
            Members = new List<CrossRegionSetMember>
            {
                new CrossRegionSetMember { Language = "en", SetId = "set-a" },
                new CrossRegionSetMember { Language = "ja", SetId = "set-b" }
            },
            Evidence = new List<string> { "fixture-source" },
            Reason = "Fixture Set relationship.",
            ReviewedDate = "2026-07-31"
        };
        setMappings = Write("equivalent.json", new CrossRegionSetMappingFile
        {
            SourceCoverageSnapshotSha256 = CoverageSnapshot,
            Groups = new List<CrossRegionSetMapping> { mapping }
        });

        MultilingualIdentityCompilationReport accepted = MultilingualIdentityCompiler.Compile(
            Coverage(new[] { english, japanese },
                Candidate(MultilingualIdentityCompiler.SourceCardSignal, english, japanese)),
            setMappings,
            cardOverrides);

        Assert.That(accepted.AutoAcceptedGroupCount, Is.EqualTo(1));
        Assert.That(accepted.Groups.Single().Signals,
            Is.EquivalentTo(new[]
            {
                MultilingualIdentityCompiler.SourceCardSignal,
                MultilingualIdentityCompiler.ReviewedSetSignal
            }));

        mapping.Relationship = "unrelated";
        setMappings = Write("unrelated.json", new CrossRegionSetMappingFile
        {
            SourceCoverageSnapshotSha256 = CoverageSnapshot,
            Groups = new List<CrossRegionSetMapping> { mapping }
        });
        MultilingualIdentityCompilationReport rejected = MultilingualIdentityCompiler.Compile(
            Coverage(new[] { english, japanese },
                Candidate(MultilingualIdentityCompiler.SetLocalSignal, english, japanese),
                Candidate(MultilingualIdentityCompiler.SourceCardSignal, english, japanese)),
            setMappings,
            cardOverrides);

        Assert.That(rejected.ReviewedRejectedGroupCount, Is.EqualTo(1));
        Assert.That(rejected.AutoAcceptedGroupCount, Is.Zero);
    }

    [Test]
    public void CheckedInReviewFiles_AreVersionedAgainstCurrentProductionCoverage()
    {
        string overrideRoot = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Editor",
            "ContentImporter", "Overrides");

        CrossRegionSetMappingFile mappings = MultilingualIdentityCompiler.LoadSetMappings(
            Path.Combine(overrideRoot, "cross-region-set-mappings.json"),
            "183195b01a22c8e36198834aa6fa39fbd4bddf036380168647c5d965c6e520ef");
        MultilingualCardIdentityOverrideFile overrides = MultilingualIdentityCompiler.LoadCardOverrides(
            Path.Combine(overrideRoot, "multilingual-card-identity-overrides.json"),
            "183195b01a22c8e36198834aa6fa39fbd4bddf036380168647c5d965c6e520ef");

        Assert.That(mappings.SchemaVersion, Is.EqualTo(1));
        Assert.That(overrides.SchemaVersion, Is.EqualTo(1));
        Assert.That(mappings.Groups, Is.Empty,
            "No Set relation is treated as reviewed until its evidence is explicitly recorded.");
        Assert.That(overrides.Decisions, Is.Empty,
            "Automatic groups remain reproducible compiler output, not fake manual reviews.");
    }

    private string Write(string fileName, object value)
    {
        string path = Path.Combine(root, fileName);
        File.WriteAllText(path, JsonConvert.SerializeObject(value, Formatting.Indented));
        return path;
    }

    private static MultilingualCardIdentityOverride Decision(
        string id,
        string disposition,
        params MultilingualCoverageCardRecord[] cards) =>
        new MultilingualCardIdentityOverride
        {
            Id = id,
            Disposition = disposition,
            RecordIds = cards.Select(value => value.RecordId).ToList(),
            Evidence = new List<string> { "fixture-review" },
            Reason = "Fixture decision with explicit evidence.",
            ReviewedDate = "2026-07-31"
        };

    private static MultilingualProductionCoverageReport Coverage(
        IEnumerable<MultilingualCoverageCardRecord> cards,
        params MultilingualCoverageCandidateGroup[] groups) =>
        new MultilingualProductionCoverageReport
        {
            IsValid = true,
            SnapshotSha256 = CoverageSnapshot,
            Cards = cards.ToList(),
            CandidateGroups = groups.ToList()
        };

    private static MultilingualCoverageCandidateGroup Candidate(
        string strategy,
        params MultilingualCoverageCardRecord[] cards) =>
        new MultilingualCoverageCandidateGroup
        {
            Strategy = strategy,
            Key = strategy,
            Members = cards.Select(card => new MultilingualCoverageCandidateMember
            {
                RecordId = card.RecordId,
                Language = card.Language,
                SetId = card.SetId,
                CardId = card.CardId,
                LocalId = card.LocalId,
                CardName = card.CardName
            }).ToList()
        };

    private static MultilingualCoverageCardRecord Card(
        string recordId,
        string language,
        string setId,
        string cardId,
        string localId) =>
        new MultilingualCoverageCardRecord
        {
            RecordId = recordId,
            Language = language,
            SetId = setId,
            SetName = setId,
            CardId = cardId,
            LocalId = localId,
            CardName = cardId,
            Category = "Pokemon",
            Rarity = "Rare",
            Illustrator = "Fixture Artist",
            VariantKey = "normal",
            Types = new List<string> { "Colorless" }
        };
}
