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
}
