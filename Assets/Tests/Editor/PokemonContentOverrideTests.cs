using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;

public class PokemonContentOverrideTests
{
    private string temporaryDirectory;

    [SetUp]
    public void SetUp()
    {
        temporaryDirectory = Path.Combine(
            Path.GetTempPath(), "gacha-pokemon-override-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(temporaryDirectory))
            Directory.Delete(temporaryDirectory, true);
    }

    [Test]
    public void CheckedInSetOverrides_ApplyKnownSetAndLeaveUnknownSetUnmapped()
    {
        PokemonSetGenerationOverrideCatalog catalog = PokemonContentOverrideLoader.LoadSetGeneration(
            ProjectPath("set-generation-overrides.json"));
        var known = new ImportedSetRecord
        {
            Id = "base1",
            SetCode = "source-code",
            EraId = "source-era",
            GenerationId = "unmapped"
        };
        var unknown = new ImportedSetRecord
        {
            Id = "future1",
            SetCode = "F1",
            EraId = "future",
            GenerationId = "unmapped"
        };

        Assert.That(catalog.Count, Is.EqualTo(218));
        Assert.That(catalog.Apply(known), Is.True);
        Assert.That(known.SetCode, Is.EqualTo("BS"));
        Assert.That(known.EraId, Is.EqualTo("base"));
        Assert.That(known.GenerationId, Is.EqualTo("generation-1"));
        Assert.That(known.GenerationOrder, Is.EqualTo(1));
        Assert.That(known.SetOrdinal, Is.EqualTo(2));
        Assert.That(catalog.Apply(unknown), Is.False);
        Assert.That(unknown.GenerationId, Is.EqualTo("unmapped"));
    }

    [Test]
    public void SetOverrideLoader_RejectsDuplicateOrInvalidOrdering()
    {
        string duplicatePath = Write("duplicate.json", @"{
          'schemaVersion':1,
          'sets':[
            {'setId':'same','setCode':'S1','eraId':'era','generationId':'generation-1','generationOrder':1,'setOrdinal':1},
            {'setId':'same','setCode':'S2','eraId':'era','generationId':'generation-1','generationOrder':1,'setOrdinal':2}
          ]
        }");
        string invalidPath = Write("invalid.json", @"{
          'schemaVersion':1,
          'sets':[
            {'setId':'bad','setCode':'B','eraId':'era','generationId':'generation-1','generationOrder':0,'setOrdinal':1}
          ]
        }");

        Assert.That(
            Assert.Throws<PokemonContentOverrideException>(() =>
                PokemonContentOverrideLoader.LoadSetGeneration(duplicatePath)).Message,
            Does.Contain("Duplicate SetId"));
        Assert.That(
            Assert.Throws<PokemonContentOverrideException>(() =>
                PokemonContentOverrideLoader.LoadSetGeneration(invalidPath)).Message,
            Does.Contain("GenerationOrder"));
    }

    [Test]
    public void CheckedInSetOverrides_CoverEnglishSnapshotWithDenseGenerationOrdinals()
    {
        PokemonSetGenerationOverrideFile file =
            JsonConvert.DeserializeObject<PokemonSetGenerationOverrideFile>(
                File.ReadAllText(ProjectPath("set-generation-overrides.json")));

        Assert.That(file.SourceLanguage, Is.EqualTo("en"));
        Assert.That(file.SourceInventorySha256,
            Is.EqualTo("5443dcd1e46babb041432f46b7da46114b1d244a23f18296753861513a3e21a5"));
        Assert.That(file.Sets, Has.Count.EqualTo(218));
        Assert.That(file.Sets.Select(item => item.SetId).Distinct().Count(), Is.EqualTo(218));
        Assert.That(file.Sets.Select(item => item.GenerationOrder).Distinct().OrderBy(value => value),
            Is.EqualTo(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 100 }));
        foreach (var generation in file.Sets.GroupBy(item => item.GenerationOrder))
        {
            Assert.That(generation.Select(item => item.SetOrdinal).OrderBy(value => value),
                Is.EqualTo(Enumerable.Range(1, generation.Count())),
                $"Generation order {generation.Key} must have dense, unique Set ordinals.");
        }
    }

    [Test]
    public void CheckedInFormPolicies_KeepRegionalDistinctAndAmbiguousKindsReviewable()
    {
        PokemonFormClassificationCatalog catalog = PokemonContentOverrideLoader.LoadFormClassification(
            ProjectPath("form-classification-overrides.json"));

        Assert.That(catalog.PolicyCount, Is.EqualTo(7));
        Assert.That(catalog.OverrideCount, Is.Zero);
        Assert.That(catalog.GetPolicy("regional").DefaultDisposition, Is.EqualTo("separate-entry"));
        Assert.That(catalog.GetPolicy("mega").DefaultDisposition, Is.EqualTo("manual-review"));
        Assert.That(catalog.GetPolicy("gigantamax").DefaultDisposition, Is.EqualTo("manual-review"));
        Assert.That(catalog.GetPolicy("battle-only").DefaultDisposition, Is.EqualTo("manual-review"));
        Assert.That(catalog.GetPolicy("gender-difference").DefaultDisposition, Is.EqualTo("related-variant"));
        Assert.That(catalog.GetPolicy("cosmetic").DefaultDisposition, Is.EqualTo("related-variant"));
        Assert.That(catalog.GetPolicy("alternate").DefaultDisposition, Is.EqualTo("separate-entry"));
    }

    [Test]
    public void FormPolicyLoader_RejectsMissingRequiredKindAndUnknownDisposition()
    {
        string path = Write("forms.json", @"{
          'schemaVersion':1,
          'policies':[
            {'formKind':'regional','defaultDisposition':'invented','reason':'invalid'}
          ],
          'overrides':[]
        }");

        PokemonContentOverrideException exception = Assert.Throws<PokemonContentOverrideException>(() =>
            PokemonContentOverrideLoader.LoadFormClassification(path));

        Assert.That(exception.Message, Does.Contain("Unsupported disposition"));
    }

    private string Write(string fileName, string json)
    {
        string path = Path.Combine(temporaryDirectory, fileName);
        File.WriteAllText(path, json);
        return path;
    }

    private static string ProjectPath(string fileName)
    {
        return Path.Combine(
            Directory.GetCurrentDirectory(),
            "Assets",
            "Editor",
            "ContentImporter",
            "Overrides",
            fileName);
    }
}
