using System;
using System.Collections.Generic;
using System.Linq;
using Gacha.Domain;
using NUnit.Framework;

public class SetDefinitionSortingTests
{
    [Test]
    public void GenerationMode_UsesStableGenerationDateOrdinalCodeNameAndIdOrder()
    {
        SetDefinition[] source =
        {
            Set("unknown", "Unknown", null, null),
            Set("gen2", "Generation Two", new DateTime(2000, 1, 1), Ordering("neo1", 2, 1)),
            Set("later-ordinal", "Zulu", new DateTime(1999, 1, 1), Ordering("base10", 1, 10)),
            Set("earlier-date", "Beta", new DateTime(1998, 12, 1), Ordering("base2", 1, 2)),
            Set("earlier-ordinal", "Alpha", new DateTime(1999, 1, 1), Ordering("base2", 1, 2)),
            Set("earlier-code", "Zulu", new DateTime(1999, 1, 1), Ordering("base2", 1, 10))
        };

        string[] ordered = source
            .OrderBy(set => set, new SetDefinitionComparer(SetSortMode.Generation, "en"))
            .Select(set => set.Id)
            .ToArray();

        Assert.That(ordered, Is.EqualTo(new[]
        {
            "earlier-date",
            "earlier-ordinal",
            "earlier-code",
            "later-ordinal",
            "gen2",
            "unknown"
        }));
    }

    [Test]
    public void AlternateModes_UseNaturalCodesAndOrdinalNames()
    {
        SetDefinition codeTen = Set("code-ten", "Alpha", new DateTime(1999, 1, 1), Ordering("sv10", 1, 10));
        SetDefinition codeTwo = Set("code-two", "Zulu", new DateTime(2001, 1, 1), Ordering("sv2", 2, 2));
        SetDefinition noCode = Set("no-code", "Beta", new DateTime(1998, 1, 1), new SetOrderingMetadata());
        SetDefinition[] source = { codeTen, noCode, codeTwo };

        Assert.That(Ids(source, SetSortMode.SetCode),
            Is.EqualTo(new[] { "code-two", "code-ten", "no-code" }));
        Assert.That(Ids(source, SetSortMode.ReleaseDate),
            Is.EqualTo(new[] { "no-code", "code-ten", "code-two" }));
        Assert.That(Ids(source, SetSortMode.DisplayName),
            Is.EqualTo(new[] { "code-ten", "no-code", "code-two" }));
    }

    [Test]
    public void EqualMetadata_AlwaysFallsBackToStableOrdinalId()
    {
        SetOrderingMetadata ordering = Ordering("same1", 1, 1);
        SetDefinition[] source =
        {
            Set("set-b", "Same", new DateTime(2000, 1, 1), ordering),
            Set("set-a", "Same", new DateTime(2000, 1, 1), ordering)
        };

        Assert.That(Ids(source, SetSortMode.Generation), Is.EqualTo(new[] { "set-a", "set-b" }));
    }

    [Test]
    public void OrderingMetadata_TrimsValuesAndRejectsNegativeRanks()
    {
        var metadata = new SetOrderingMetadata(" sv01 ", " era ", " gen-1 ", 1, 2);

        Assert.That(metadata.SetCode, Is.EqualTo("sv01"));
        Assert.That(metadata.EraId, Is.EqualTo("era"));
        Assert.That(metadata.GenerationId, Is.EqualTo("gen-1"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SetOrderingMetadata(generationOrder: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SetOrderingMetadata(setOrdinal: -1));
    }

    private static string[] Ids(IEnumerable<SetDefinition> source, SetSortMode mode)
    {
        return source.OrderBy(set => set, new SetDefinitionComparer(mode, "en"))
            .Select(set => set.Id)
            .ToArray();
    }

    private static SetDefinition Set(
        string id,
        string name,
        DateTime? releaseDate,
        SetOrderingMetadata ordering)
    {
        return new SetDefinition(id, "pokemon", Names(name), releaseDate: releaseDate, ordering: ordering);
    }

    private static SetOrderingMetadata Ordering(string code, int generation, int ordinal)
    {
        return new SetOrderingMetadata(code, "era", $"generation-{generation}", generation, ordinal);
    }

    private static Dictionary<string, string> Names(string english)
    {
        return new Dictionary<string, string> { ["en"] = english };
    }
}
