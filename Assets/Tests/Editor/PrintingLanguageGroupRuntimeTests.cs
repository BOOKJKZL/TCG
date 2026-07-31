using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gacha.Domain;
using Gacha.Infrastructure.Content;
using Newtonsoft.Json;
using NUnit.Framework;

public sealed class PrintingLanguageGroupRuntimeTests
{
    private string root;

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(
            Path.GetTempPath(),
            "gacha-runtime-language-groups",
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
    public void Compiler_EmitsOnlyAcceptedGroupsWithStableBytes()
    {
        string sourcePath = Path.Combine(root, "identity.json");
        string firstPath = Path.Combine(root, "first", "printing-language-groups.json");
        string secondPath = Path.Combine(root, "second", "printing-language-groups.json");
        MultilingualIdentityCompilationReport source = IdentityReport();
        File.WriteAllText(sourcePath, JsonConvert.SerializeObject(source, Formatting.Indented));

        PrintingLanguageGroupRuntimeCompilationResult first =
            PrintingLanguageGroupRuntimeCompiler.Compile(sourcePath, firstPath);
        PrintingLanguageGroupRuntimeCompilationResult second =
            PrintingLanguageGroupRuntimeCompiler.Compile(sourcePath, secondPath);

        Assert.That(first.IsValid, Is.True, string.Join("\n", first.Failures));
        Assert.That(first.GroupCount, Is.EqualTo(1));
        Assert.That(first.MemberCount, Is.EqualTo(2));
        Assert.That(first.OutputSha256, Is.EqualTo(second.OutputSha256));
        Assert.That(File.ReadAllBytes(firstPath), Is.EqualTo(File.ReadAllBytes(secondPath)));
        PrintingLanguageGroupManifestDto manifest =
            new PrintingLanguageGroupManifestReader().LoadFile(firstPath);
        Assert.That(manifest.Groups.Single().Id, Is.EqualTo("identity|accepted"));
        Assert.That(manifest.Groups.Single().Members.Select(value => value.Language),
            Is.EqualTo(new[] { "en", "ja" }));
    }

    [Test]
    public void Reader_FailsClosedWhenSourceCardIsClaimedTwice()
    {
        PrintingLanguageGroupManifestDto manifest = RuntimeManifest();
        PrintingLanguageGroupMemberDto duplicate = manifest.Groups[0].Members[0];
        manifest.Groups.Add(new PrintingLanguageGroupRecordDto
        {
            Id = "identity|duplicate",
            MatchMethod = "manual-override",
            ReviewStatus = "reviewed",
            Confidence = 1d,
            Members = new List<PrintingLanguageGroupMemberDto>
            {
                new PrintingLanguageGroupMemberDto
                {
                    Language = duplicate.Language,
                    SetId = duplicate.SetId,
                    CardId = duplicate.CardId,
                    LocalId = duplicate.LocalId
                },
                Member("zh-cn", "set-z", "card-z", "3")
            }
        });
        string path = Path.Combine(root, "duplicate.json");
        File.WriteAllText(path, JsonConvert.SerializeObject(manifest, Formatting.Indented));

        PrivateContentManifestException exception = Assert.Throws<PrivateContentManifestException>(
            () => new PrintingLanguageGroupManifestReader().LoadFile(path));

        Assert.That(exception.Message, Does.Contain("belongs to both"));
    }

    [Test]
    public void Adapter_LinksOnlyExplicitReviewedSourceCardsAcrossRegionalSets()
    {
        PrivateContentManifestDto english = Manifest(
            "en", "set-en", Card("card-en", "1", "English Card"));
        PrivateContentManifestDto japanese = Manifest(
            "ja", "set-ja", Card("card-ja", "9", "日本語カード"));
        japanese.Cards[0].Variants = new ImportedCardVariantsDto { Holo = true };
        PrivateContentManifestDto unsafeEnglish = Manifest(
            "en", "collision", Card("collision-en", "7", "Pikachu"));
        PrivateContentManifestDto unsafeChinese = Manifest(
            "zh-cn", "collision", Card("collision-zh", "7", "班基拉斯"));
        PrintingLanguageGroupManifestDto overlay = RuntimeManifest();

        PrivateCatalogImportResult result = new PrivateManifestCatalogAdapter().Build(
            new[]
            {
                Document("en/set-en/manifest.json", english),
                Document("ja/set-ja/manifest.json", japanese),
                Document("en/collision/manifest.json", unsafeEnglish),
                Document("zh-cn/collision/manifest.json", unsafeChinese)
            },
            "sample-game",
            "Sample Game",
            overlay);

        PrintingDefinition englishPrinting = result.Catalog.Printings.Values.Single(value =>
            value.Identity.LanguageId == "en" && value.Identity.SetId.EndsWith(":set-en"));
        PrintingDefinition japanesePrinting = result.Catalog.Printings.Values.Single(value =>
            value.Identity.LanguageId == "ja");
        PrintingLanguageGroup linked = result.Catalog.PrintingLanguages.GetGroup(englishPrinting.Id);
        Assert.That(linked.HasMultipleLanguages, Is.True);
        Assert.That(linked.MatchMethod, Is.EqualTo(PrintingLanguageMatchMethod.SourceIdentity));
        Assert.That(result.Catalog.PrintingLanguages.Select(englishPrinting.Id, "ja"),
            Is.SameAs(japanesePrinting));
        Assert.That(japanesePrinting.Identity.VariantId, Does.EndWith(":holo"),
            "Regional variant metadata must change with the selected printing.");
        Assert.That(japanesePrinting.Identity.SetId, Is.Not.EqualTo(englishPrinting.Identity.SetId));
        Assert.That(englishPrinting.Id,
            Is.EqualTo("sample-game:printing:set-en:1:en:normal"));

        PrintingDefinition collisionEnglish = result.Catalog.Printings.Values.Single(value =>
            value.Identity.LanguageId == "en" && value.Identity.SetId.EndsWith(":collision"));
        PrintingDefinition collisionChinese = result.Catalog.Printings.Values.Single(value =>
            value.Identity.LanguageId == "zh-cn");
        Assert.That(result.Catalog.PrintingLanguages.GetGroup(collisionEnglish.Id).HasMultipleLanguages,
            Is.False);
        Assert.That(result.Catalog.PrintingLanguages.Select(collisionEnglish.Id, "zh-cn"),
            Is.SameAs(collisionEnglish));
        Assert.That(collisionEnglish.ItemId, Is.Not.EqualTo(collisionChinese.ItemId));
        Assert.That(result.SourceCardCount, Is.EqualTo(4));
    }

    [Test]
    public void Provider_LoadsOverlayFromRuntimeInstallDirectory()
    {
        WriteManifest("en", "set-en", Manifest(
            "en", "set-en", Card("card-en", "1", "English Card")));
        WriteManifest("ja", "set-ja", Manifest(
            "ja", "set-ja", Card("card-ja", "9", "日本語カード")));
        string overlayDirectory = Path.Combine(
            root,
            PrintingLanguageGroupManifestReader.InstallRelativeDirectory
                .Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(overlayDirectory);
        File.WriteAllText(
            Path.Combine(overlayDirectory, PrintingLanguageGroupManifestReader.FileName),
            JsonConvert.SerializeObject(RuntimeManifest(), Formatting.Indented));

        Gacha.Application.CatalogLoadResult loaded =
            new PrivateContentCatalogProvider(root, "sample-game", "Sample Game").Load();

        Assert.That(loaded.Succeeded, Is.True, loaded.ErrorMessage);
        Assert.That(loaded.Catalog.PrintingLanguageGroups, Has.Count.EqualTo(1));
    }

    private static MultilingualIdentityCompilationReport IdentityReport()
    {
        var cards = new List<MultilingualIdentityCardResult>
        {
            IdentityCard("en|set-en/manifest.json|card-en|1", "en", "set-en", "card-en", "1", "auto-accepted"),
            IdentityCard("ja|set-ja/manifest.json|card-ja|9", "ja", "set-ja", "card-ja", "9", "auto-accepted"),
            IdentityCard("en|collision/manifest.json|collision-en|7", "en", "collision", "collision-en", "7", "pending-review"),
            IdentityCard("zh-cn|collision/manifest.json|collision-zh|7", "zh-cn", "collision", "collision-zh", "7", "pending-review")
        };
        return new MultilingualIdentityCompilationReport
        {
            SchemaVersion = 1,
            IsValid = true,
            SourceCoverageSnapshotSha256 = new string('a', 64),
            SnapshotSha256 = new string('b', 64),
            TotalCardCount = cards.Count,
            Cards = cards,
            Groups = new List<MultilingualIdentityGroupResult>
            {
                new MultilingualIdentityGroupResult
                {
                    Id = "identity|accepted",
                    Classification = "auto-accepted",
                    Confidence = 0.99d,
                    RecordIds = cards.Take(2).Select(value => value.RecordId).ToList(),
                    Languages = new List<string> { "en", "ja" },
                    Signals = new List<string> { "same-source-card-id", "same-image" }
                },
                new MultilingualIdentityGroupResult
                {
                    Id = "identity|pending",
                    Classification = "pending-review",
                    Confidence = 0.5d,
                    RecordIds = cards.Skip(2).Select(value => value.RecordId).ToList(),
                    Languages = new List<string> { "en", "zh-cn" },
                    Signals = new List<string> { "same-set-and-local-id" }
                }
            }
        };
    }

    private static MultilingualIdentityCardResult IdentityCard(
        string recordId,
        string language,
        string setId,
        string cardId,
        string localId,
        string classification) => new MultilingualIdentityCardResult
        {
            RecordId = recordId,
            Language = language,
            SetId = setId,
            CardId = cardId,
            LocalId = localId,
            Classification = classification,
            SemanticFingerprintSha256 = new string('c', 64)
        };

    private static PrintingLanguageGroupManifestDto RuntimeManifest() =>
        new PrintingLanguageGroupManifestDto
        {
            SourceCoverageSnapshotSha256 = new string('a', 64),
            SourceIdentitySnapshotSha256 = new string('b', 64),
            Groups = new List<PrintingLanguageGroupRecordDto>
            {
                new PrintingLanguageGroupRecordDto
                {
                    Id = "identity|accepted",
                    MatchMethod = "source-identity",
                    ReviewStatus = "auto-accepted",
                    Confidence = 0.99d,
                    Evidence = new List<string> { "same-source-card-id", "same-image" },
                    Members = new List<PrintingLanguageGroupMemberDto>
                    {
                        Member("en", "set-en", "card-en", "1"),
                        Member("ja", "set-ja", "card-ja", "9")
                    }
                }
            }
        };

    private static PrintingLanguageGroupMemberDto Member(
        string language,
        string setId,
        string cardId,
        string localId) => new PrintingLanguageGroupMemberDto
        {
            Language = language,
            SetId = setId,
            CardId = cardId,
            LocalId = localId
        };

    private PrivateContentManifestDocument Document(
        string relativePath,
        PrivateContentManifestDto manifest) =>
        new PrivateContentManifestDocument(Path.Combine(root, relativePath), manifest);

    private void WriteManifest(string language, string setId, PrivateContentManifestDto manifest)
    {
        string directory = Path.Combine(root, language, setId);
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "manifest.json"),
            JsonConvert.SerializeObject(manifest, Formatting.Indented));
    }

    private static PrivateContentManifestDto Manifest(
        string language,
        string setId,
        params ImportedCardDto[] cards) => new PrivateContentManifestDto
        {
            SchemaVersion = 2,
            Source = "fixture",
            Language = language,
            Set = new ImportedSetDto
            {
                Id = setId,
                Name = setId,
                SetCode = setId,
                EraId = "fixture",
                GenerationId = "generation-1",
                GenerationOrder = 1,
                SetOrdinal = 1
            },
            Cards = cards.ToList()
        };

    private static ImportedCardDto Card(string id, string localId, string name) =>
        new ImportedCardDto
        {
            Id = id,
            LocalId = localId,
            Name = name,
            Category = "Pokemon",
            Rarity = "Common",
            Variants = new ImportedCardVariantsDto { Normal = true }
        };
}
