using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;

public class PokemonSetGenerationCompilerTests
{
    [Test]
    public void Compile_CheckedInPoliciesClassifySplitSeriesAndPocketWithoutNameGuessing()
    {
        PokemonSetGenerationPolicyFile policies = JsonConvert.DeserializeObject<PokemonSetGenerationPolicyFile>(
            File.ReadAllText(ProjectPath("set-generation-policies.json")));
        ContentInventorySnapshot inventory = Inventory(
            Set("en", "base1", "BS", "base", "1999-01-09"),
            Set("en", "pop4", "P4", "pop", "2006-08-01"),
            Set("en", "pop6", "P6", "pop", "2007-09-01"),
            Set("en", "A1", "A1", "tcgp", "2024-10-30"));

        PokemonSetGenerationCompileResult result =
            PokemonSetGenerationCompiler.Compile(inventory, policies);

        Assert.That(result.SourceSetCount, Is.EqualTo(4));
        Assert.That(result.PolicyCount, Is.EqualTo(30));
        AssertEntry(result.File, "base1", "base", "generation-1", 1, 1);
        AssertEntry(result.File, "pop4", "pop", "generation-3", 3, 1);
        AssertEntry(result.File, "pop6", "pop", "generation-4", 4, 1);
        AssertEntry(result.File, "A1", "pokemon-tcg-pocket", "pokemon-tcg-pocket", 100, 1);
        Assert.That(result.File.SourceInventorySha256, Is.EqualTo("inventory-hash"));
        Assert.That(result.File.SourceLanguage, Is.EqualTo("en"));
    }

    [Test]
    public void Compile_AssignsStableOrdinalByReleaseDateThenSetIdWithinGeneration()
    {
        var policies = new PokemonSetGenerationPolicyFile();
        policies.Policies.Add(Policy("sample", "sample-era", "generation-1", 1));
        ContentInventorySnapshot inventory = Inventory(
            Set("en", "z-set", "Z", "sample", "2000-02-01"),
            Set("en", "b-set", "B", "sample", "2000-01-01"),
            Set("en", "a-set", "A", "sample", "2000-01-01"));

        PokemonSetGenerationOverrideFile file =
            PokemonSetGenerationCompiler.Compile(inventory, policies).File;

        Assert.That(file.Sets.Select(item => item.SetId),
            Is.EqualTo(new[] { "a-set", "b-set", "z-set" }));
        Assert.That(file.Sets.Select(item => item.SetOrdinal), Is.EqualTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public void Compile_RejectsUnmappedSetAndOverlappingPolicies()
    {
        var unmatched = new PokemonSetGenerationPolicyFile();
        unmatched.Policies.Add(Policy("other", "other", "generation-1", 1));
        PokemonContentOverrideException unmatchedError =
            Assert.Throws<PokemonContentOverrideException>(() =>
                PokemonSetGenerationCompiler.Compile(
                    Inventory(Set("en", "set1", "S1", "sample", "2000-01-01")), unmatched));

        var overlap = new PokemonSetGenerationPolicyFile();
        PokemonSetGenerationPolicy first = Policy("sample", "one", "generation-1", 1);
        first.ReleaseDateTo = "2000-12-31";
        PokemonSetGenerationPolicy second = Policy("sample", "two", "generation-2", 2);
        second.ReleaseDateFrom = "2000-06-01";
        overlap.Policies.Add(first);
        overlap.Policies.Add(second);
        PokemonContentOverrideException overlapError =
            Assert.Throws<PokemonContentOverrideException>(() =>
                PokemonSetGenerationCompiler.Compile(
                    Inventory(Set("en", "set1", "S1", "sample", "2000-07-01")), overlap));

        Assert.That(unmatchedError.Message, Does.Contain("matched 0"));
        Assert.That(overlapError.Message, Does.Contain("overlap"));
    }

    private static ContentInventorySnapshot Inventory(params ContentInventorySetRecord[] sets)
    {
        var inventory = new ContentInventorySnapshot
        {
            SchemaVersion = 1,
            ReferenceLanguage = "en",
            ContentSha256 = "inventory-hash"
        };
        inventory.Sets.AddRange(sets);
        return inventory;
    }

    private static ContentInventorySetRecord Set(
        string language, string id, string code, string seriesId, string releaseDate)
    {
        return new ContentInventorySetRecord
        {
            Language = language,
            Id = id,
            SetCode = code,
            SeriesId = seriesId,
            ReleaseDate = releaseDate
        };
    }

    private static PokemonSetGenerationPolicy Policy(
        string seriesId, string eraId, string generationId, int generationOrder)
    {
        return new PokemonSetGenerationPolicy
        {
            SeriesId = seriesId,
            EraId = eraId,
            GenerationId = generationId,
            GenerationOrder = generationOrder
        };
    }

    private static void AssertEntry(
        PokemonSetGenerationOverrideFile file, string setId, string eraId,
        string generationId, int generationOrder, int setOrdinal)
    {
        PokemonSetGenerationOverride entry = file.Sets.Single(item => item.SetId == setId);
        Assert.That(entry.EraId, Is.EqualTo(eraId));
        Assert.That(entry.GenerationId, Is.EqualTo(generationId));
        Assert.That(entry.GenerationOrder, Is.EqualTo(generationOrder));
        Assert.That(entry.SetOrdinal, Is.EqualTo(setOrdinal));
    }

    private static string ProjectPath(string fileName)
    {
        return Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Editor", "ContentImporter",
            "Overrides", fileName);
    }
}
