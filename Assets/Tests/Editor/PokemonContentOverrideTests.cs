using System;
using System.IO;
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

        Assert.That(catalog.Count, Is.EqualTo(5));
        Assert.That(catalog.Apply(known), Is.True);
        Assert.That(known.SetCode, Is.EqualTo("BS"));
        Assert.That(known.EraId, Is.EqualTo("base"));
        Assert.That(known.GenerationId, Is.EqualTo("generation-1"));
        Assert.That(known.GenerationOrder, Is.EqualTo(1));
        Assert.That(known.SetOrdinal, Is.EqualTo(1));
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
    public void CheckedInFormPolicies_KeepRegionalDistinctAndAmbiguousKindsReviewable()
    {
        PokemonFormClassificationCatalog catalog = PokemonContentOverrideLoader.LoadFormClassification(
            ProjectPath("form-classification-overrides.json"));

        Assert.That(catalog.PolicyCount, Is.EqualTo(6));
        Assert.That(catalog.OverrideCount, Is.Zero);
        Assert.That(catalog.GetPolicy("regional").DefaultDisposition, Is.EqualTo("separate-entry"));
        Assert.That(catalog.GetPolicy("mega").DefaultDisposition, Is.EqualTo("manual-review"));
        Assert.That(catalog.GetPolicy("gigantamax").DefaultDisposition, Is.EqualTo("manual-review"));
        Assert.That(catalog.GetPolicy("battle-only").DefaultDisposition, Is.EqualTo("manual-review"));
        Assert.That(catalog.GetPolicy("gender-difference").DefaultDisposition, Is.EqualTo("related-variant"));
        Assert.That(catalog.GetPolicy("cosmetic").DefaultDisposition, Is.EqualTo("related-variant"));
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
