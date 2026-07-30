using System;
using System.Collections.Generic;
using System.Globalization;

public static class PokemonSetOrderingInference
{
    private static readonly IReadOnlyDictionary<string, int> SeriesGenerations =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["PMCG"] = 1,
            ["base"] = 1,
            ["gym"] = 1,
            ["neo"] = 2,
            ["VS"] = 2,
            ["web"] = 2,
            ["e"] = 2,
            ["ecard"] = 2,
            ["ADV"] = 3,
            ["PCG"] = 3,
            ["ex"] = 3,
            ["DP"] = 4,
            ["DPt"] = 4,
            ["L"] = 4,
            ["dp"] = 4,
            ["pl"] = 4,
            ["hgss"] = 4,
            ["BW"] = 5,
            ["bw"] = 5,
            ["XY"] = 6,
            ["XYb"] = 6,
            ["xy"] = 6,
            ["SM"] = 7,
            ["sm"] = 7,
            ["S"] = 8,
            ["swsh"] = 8,
            ["SV"] = 9,
            ["sv"] = 9,
            ["M"] = 9
        };

    public static bool TryApply(ImportedSetRecord set)
    {
        if (set == null)
            throw new ArgumentNullException(nameof(set));
        if (!string.IsNullOrWhiteSpace(set.GenerationId) &&
            !string.Equals(set.GenerationId, "unmapped", StringComparison.OrdinalIgnoreCase) &&
            set.GenerationOrder.HasValue && set.SetOrdinal.HasValue)
            return true;

        int generation;
        if (!string.IsNullOrWhiteSpace(set.SeriesId) &&
            SeriesGenerations.TryGetValue(set.SeriesId.Trim(), out int mapped))
        {
            generation = mapped;
        }
        else if (DateTime.TryParseExact(set.ReleaseDate, "yyyy-MM-dd",
                     CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime releaseDate))
        {
            generation = GenerationAt(releaseDate);
        }
        else
        {
            return false;
        }

        set.GenerationId = "generation-" + generation;
        set.GenerationOrder = generation;
        set.SetOrdinal = set.SetOrdinal ?? 1;
        if (string.IsNullOrWhiteSpace(set.EraId))
            set.EraId = string.IsNullOrWhiteSpace(set.SeriesId)
                ? "generation-" + generation
                : set.SeriesId.Trim();
        if (string.IsNullOrWhiteSpace(set.SetCode))
            set.SetCode = set.Id;
        return true;
    }

    private static int GenerationAt(DateTime releaseDate)
    {
        if (releaseDate < new DateTime(1999, 11, 21)) return 1;
        if (releaseDate < new DateTime(2002, 11, 21)) return 2;
        if (releaseDate < new DateTime(2006, 9, 28)) return 3;
        if (releaseDate < new DateTime(2010, 9, 18)) return 4;
        if (releaseDate < new DateTime(2013, 10, 12)) return 5;
        if (releaseDate < new DateTime(2016, 11, 18)) return 6;
        if (releaseDate < new DateTime(2019, 11, 15)) return 7;
        if (releaseDate < new DateTime(2022, 11, 18)) return 8;
        return 9;
    }
}
