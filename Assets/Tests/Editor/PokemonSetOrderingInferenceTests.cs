using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;

public class PokemonSetOrderingInferenceTests
{
    [TestCase("PMCG", null, 1)]
    [TestCase("neo", null, 2)]
    [TestCase("ADV", null, 3)]
    [TestCase("L", null, 4)]
    [TestCase("BW", null, 5)]
    [TestCase("XYb", null, 6)]
    [TestCase("SM", null, 7)]
    [TestCase("S", null, 8)]
    [TestCase("M", null, 9)]
    [TestCase("unknown", "2015-01-01", 6)]
    public void Apply_MapsJapaneseSeriesAndUsesReleaseDateFallback(
        string seriesId,
        string releaseDate,
        int expectedGeneration)
    {
        var set = new ImportedSetRecord
        {
            Id = "fixture",
            SetCode = "F",
            SeriesId = seriesId,
            EraId = seriesId,
            GenerationId = "unmapped",
            ReleaseDate = releaseDate
        };

        bool applied = PokemonSetOrderingInference.TryApply(set);

        Assert.That(applied, Is.True);
        Assert.That(set.GenerationId, Is.EqualTo("generation-" + expectedGeneration));
        Assert.That(set.GenerationOrder, Is.EqualTo(expectedGeneration));
        Assert.That(set.SetOrdinal, Is.EqualTo(1));
    }

    [Test]
    public void Apply_LeavesTrulyUnknownSetExplicitlyUnmapped()
    {
        var set = new ImportedSetRecord
        {
            Id = "fixture",
            SeriesId = "unknown",
            GenerationId = "unmapped"
        };

        Assert.That(PokemonSetOrderingInference.TryApply(set), Is.False);
        Assert.That(set.GenerationId, Is.EqualTo("unmapped"));
        Assert.That(set.GenerationOrder, Is.Null);
    }

    [Test]
    public void SequentialOrdinals_SortWithinEachGenerationByDateCodeAndId()
    {
        ContentInventorySetRecord[] records =
        {
            Record("ja", "late-b", "B", 8, "2022-01-01"),
            Record("ja", "early", "Z", 8, "2020-01-01"),
            Record("ja", "late-a", "A", 8, "2022-01-01"),
            Record("ja", "unknown-date", "C", 8, null),
            Record("ja", "next-generation", "A", 9, "2023-01-01"),
            Record("en", "ignored", "A", 1, "1999-01-01")
        };

        IReadOnlyDictionary<string, int> result =
            PokemonSetOrderingInference.BuildSequentialOrdinals(records, "ja");

        Assert.That(result.Count, Is.EqualTo(5));
        Assert.That(result["early"], Is.EqualTo(1));
        Assert.That(result["late-a"], Is.EqualTo(2));
        Assert.That(result["late-b"], Is.EqualTo(3));
        Assert.That(result["unknown-date"], Is.EqualTo(4));
        Assert.That(result["next-generation"], Is.EqualTo(1));
    }

    [Test]
    public void ImportedManifestNormalizer_UpdatesEverySetAndIsIdempotent()
    {
        string root = Path.Combine(Path.GetTempPath(), "gacha-ordering-tests", Guid.NewGuid().ToString("N"));
        try
        {
            WriteManifest(root, "set-a", 1);
            WriteManifest(root, "set-b", 1);
            var ordinals = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["set-a"] = 2,
                ["set-b"] = 1
            };

            ContentImportSetOrderingNormalizationResult first =
                ContentImportSetOrderingNormalizer.Normalize(root, "ja", ordinals);
            ContentImportSetOrderingNormalizationResult second =
                ContentImportSetOrderingNormalizer.Normalize(root, "ja", ordinals);

            Assert.That(first.SetCount, Is.EqualTo(2));
            Assert.That(first.UpdatedSetCount, Is.EqualTo(1));
            Assert.That(second.UpdatedSetCount, Is.Zero);
            PrivateContentManifest updated = JsonConvert.DeserializeObject<PrivateContentManifest>(
                File.ReadAllText(Path.Combine(root, "ja", "set-a", "manifest.json")));
            Assert.That(updated.Set.SetOrdinal, Is.EqualTo(2));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static ContentInventorySetRecord Record(
        string language, string id, string code, int generation, string date) =>
        new ContentInventorySetRecord
        {
            Language = language,
            Id = id,
            SetCode = code,
            GenerationOrder = generation,
            ReleaseDate = date
        };

    private static void WriteManifest(string root, string setId, int ordinal)
    {
        string directory = Path.Combine(root, "ja", setId);
        Directory.CreateDirectory(directory);
        var manifest = new PrivateContentManifest
        {
            Language = "ja",
            Set = new ImportedSetRecord
            {
                Id = setId,
                GenerationId = "generation-8",
                GenerationOrder = 8,
                SetOrdinal = ordinal
            }
        };
        File.WriteAllText(Path.Combine(directory, "manifest.json"),
            JsonConvert.SerializeObject(manifest, Formatting.Indented));
    }
}
