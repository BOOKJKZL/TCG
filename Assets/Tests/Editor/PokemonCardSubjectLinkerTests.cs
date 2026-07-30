using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gacha.Pokemon.Infrastructure;
using Newtonsoft.Json;
using NUnit.Framework;

public class PokemonCardSubjectLinkerTests
{
    private string root;

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), "pokemon-card-linker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }

    [Test]
    public void Linker_CoversCategoryDexMultiFormAndReviewPaths()
    {
        string imports = CreateCardImports();
        string taxonomy = CreateTaxonomy();
        string overrides = Write("overrides.json", "{'schemaVersion':1,'overrides':[]}");
        string output = Path.Combine(root, "links.json");

        PokemonCardSubjectLinkResult result = PokemonCardSubjectLinker.LinkFiles(
            imports, "en", taxonomy, overrides, output);

        Assert.That(result.CardCount, Is.EqualTo(5));
        Assert.That(result.PrintingCount, Is.EqualTo(5));
        Assert.That(result.MatchedSpeciesCount, Is.EqualTo(1));
        Assert.That(result.MatchedFormCount, Is.EqualTo(1));
        Assert.That(result.MultiSpeciesCount, Is.EqualTo(1));
        Assert.That(result.NotApplicableCount, Is.EqualTo(1));
        Assert.That(result.NeedsReviewCount, Is.EqualTo(1));
        Assert.That(result.Snapshot.Links.Single(value => value.CardId == "alola-19").FormIds,
            Is.EqualTo(new[] { "pokemon-form:10193" }));
        Assert.That(result.Snapshot.Links.Single(value => value.CardId == "rocket-19").Reason,
            Is.EqualTo("trainer-owned-name-requires-review"));
        Assert.That(File.Exists(output), Is.True);
    }

    [Test]
    public void Linker_AppliesVersionedManualOverrideToMissingSourceDexId()
    {
        string imports = CreateCardImports();
        string taxonomy = CreateTaxonomy();
        string overrides = Write("overrides.json", @"{
          'schemaVersion':1,'overrides':[
            {'id':'verified-rocket-rattata','cardId':'rocket-19',
             'speciesIds':['pokemon-species:19'],'formIds':[],
             'status':'matched-species','reason':'Verified trainer-owned Rattata.'}
          ]
        }");

        PokemonCardSubjectLinkResult result = PokemonCardSubjectLinker.LinkFiles(
            imports, "en", taxonomy, overrides, Path.Combine(root, "links.json"));

        PokemonCardSubjectLinkDto link = result.Snapshot.Links.Single(value => value.CardId == "rocket-19");
        Assert.That(link.Status, Is.EqualTo("matched-species"));
        Assert.That(link.Method, Is.EqualTo("manual-override"));
        Assert.That(link.OverrideId, Is.EqualTo("verified-rocket-rattata"));
        Assert.That(result.NeedsReviewCount, Is.Zero);
    }

    private string CreateCardImports()
    {
        string set = Path.Combine(root, "imports", "en", "test");
        string raw = Path.Combine(set, "raw", "cards");
        Directory.CreateDirectory(raw);
        Card(raw, "card-19", "Rattata", "Pokemon", "19", 19);
        Card(raw, "alola-19", "Alolan Rattata", "Pokemon", "20", 19);
        Card(raw, "tag-243-245", "Raikou & Suicune LEGEND", "Pokemon", "21", 243, 245);
        Card(raw, "trainer-1", "Potion", "Trainer", "22");
        Card(raw, "rocket-19", "Team Rocket's Rattata", "Pokemon", "23");
        var manifest = new
        {
            SchemaVersion = 2,
            Source = "tcgdex",
            Language = "en",
            GeneratedAtUtc = "2026-07-30T00:00:00Z",
            Set = new
            {
                Id = "test", Name = "Test", SetCode = "TST", EraId = "test",
                GenerationId = "generation-1", GenerationOrder = 1, SetOrdinal = 1,
                ReleaseDate = "2026-01-01", SeriesId = "test", SeriesName = "Test"
            },
            Cards = new[]
            {
                ManifestCard("card-19", "Rattata", "Pokemon", "19"),
                ManifestCard("alola-19", "Alolan Rattata", "Pokemon", "20"),
                ManifestCard("tag-243-245", "Raikou & Suicune LEGEND", "Pokemon", "21"),
                ManifestCard("trainer-1", "Potion", "Trainer", "22"),
                ManifestCard("rocket-19", "Team Rocket's Rattata", "Pokemon", "23")
            },
            Errors = Array.Empty<object>()
        };
        File.WriteAllText(Path.Combine(set, "manifest.json"),
            JsonConvert.SerializeObject(manifest, Formatting.Indented));
        return Path.Combine(root, "imports");
    }

    private static object ManifestCard(string id, string name, string category, string localId) => new
    {
        Id = id,
        LocalId = localId,
        Name = name,
        Category = category,
        Rarity = "Common",
        RawDataRelativePath = "raw/cards/" + id + ".json",
        Variants = new { Normal = true, Reverse = false, Holo = false, FirstEdition = false, WPromo = false },
        Types = Array.Empty<string>(),
        BoosterIds = Array.Empty<string>()
    };

    private static void Card(
        string rawDirectory, string id, string name, string category, string localId, params int[] dexIds)
    {
        File.WriteAllText(Path.Combine(rawDirectory, id + ".json"), JsonConvert.SerializeObject(new
        {
            id,
            name,
            category,
            localId,
            dexId = dexIds,
            suffix = ""
        }, Formatting.Indented));
    }

    private string CreateTaxonomy()
    {
        var snapshot = new PokemonTaxonomySnapshotDto
        {
            Source = "pokeapi",
            SourceBaseUrl = "https://pokeapi.co/api/v2/",
            CapturedAtUtc = "2026-07-30T00:00:00Z",
            SourceSha256 = new string('a', 64),
            Languages = new List<string> { "en", "zh" },
            Generations = new List<PokemonGenerationSnapshotDto>
            {
                Generation("generation-1", 1, 19, 19),
                Generation("generation-2", 2, 243, 245),
                Generation("generation-7", 7, 722, 809)
            },
            Species = new List<PokemonSpeciesSnapshotDto>
            {
                Species("pokemon-species:19", 19, "generation-1", "Rattata", "pokemon-form:19",
                    "pokemon-form:19", "pokemon-form:10193"),
                Species("pokemon-species:243", 243, "generation-2", "Raikou", "pokemon-form:243", "pokemon-form:243"),
                Species("pokemon-species:245", 245, "generation-2", "Suicune", "pokemon-form:245", "pokemon-form:245")
            },
            Forms = new List<PokemonFormSnapshotDto>
            {
                Form("pokemon-form:19", "pokemon-species:19", 19, "Rattata", "generation-1", true,
                    "pokemon-form:10193"),
                Form("pokemon-form:10193", "pokemon-species:19", 10091, "Alolan Rattata", "generation-7", false,
                    "pokemon-form:19", "regional", "alola"),
                Form("pokemon-form:243", "pokemon-species:243", 243, "Raikou", "generation-2", true),
                Form("pokemon-form:245", "pokemon-species:245", 245, "Suicune", "generation-2", true)
            }
        };
        return Write("taxonomy.json", JsonConvert.SerializeObject(snapshot, Formatting.Indented), false);
    }

    private static PokemonGenerationSnapshotDto Generation(string id, int order, int start, int end) => new PokemonGenerationSnapshotDto
    {
        Id = id, Order = order, Names = Names(id), SpeciesStartNumber = start, SpeciesEndNumber = end
    };

    private static PokemonSpeciesSnapshotDto Species(
        string id, int number, string generation, string name, string defaultForm, params string[] forms) =>
        new PokemonSpeciesSnapshotDto
        {
            Id = id, NationalDexNumber = number, DebutGenerationId = generation, Names = Names(name),
            Genera = Names("Pokemon"), Descriptions = Names("Description"), DefaultFormId = defaultForm,
            FormIds = forms.ToList()
        };

    private static PokemonFormSnapshotDto Form(
        string id, string species, int pokemonId, string name, string generation, bool isDefault,
        string related = null, string kind = "default", string region = null) => new PokemonFormSnapshotDto
        {
            Id = id, SpeciesId = species, PokemonId = pokemonId, FormKind = kind,
            Disposition = "separate-entry", Names = Names(name), IntroducedGenerationId = generation,
            RelatedFormIds = related == null ? new List<string>() : new List<string> { related },
            TypeIds = new List<string> { "normal" }, IsDefault = isDefault, RegionId = region
        };

    private static Dictionary<string, string> Names(string value) =>
        new Dictionary<string, string> { ["en"] = value, ["zh"] = value };

    private string Write(string fileName, string json, bool replaceQuotes = true)
    {
        string path = Path.Combine(root, fileName);
        File.WriteAllText(path, replaceQuotes ? json.Replace('\'', '"') : json);
        return path;
    }
}
