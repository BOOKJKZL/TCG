using System;
using System.IO;
using System.Linq;
using Gacha.Pokemon.Infrastructure;
using Newtonsoft.Json;
using NUnit.Framework;

public class PokeApiTaxonomyCompilerTests
{
    [Test]
    public void Compiler_BuildsRegionalFormAndLocalizedSpeciesDeterministically()
    {
        PokeApiTaxonomyRawData raw = RattataRaw();
        PokemonFormClassificationCatalog policies = Policies();

        PokeApiTaxonomyCompileResult first = PokeApiTaxonomyCompiler.Compile(
            raw, policies, DateTimeOffset.Parse("2026-07-30T00:00:00Z"));
        PokeApiTaxonomyCompileResult second = PokeApiTaxonomyCompiler.Compile(
            Reformat(raw), policies, DateTimeOffset.Parse("2026-07-30T00:00:00Z"));

        Assert.That(first.Snapshot.SourceSha256, Is.EqualTo(second.Snapshot.SourceSha256));
        Assert.That(first.Snapshot.Species.Count, Is.EqualTo(1));
        PokemonSpeciesSnapshotDto species = first.Snapshot.Species.Single();
        Assert.That(species.NationalDexNumber, Is.EqualTo(19));
        Assert.That(species.Names["zh"], Is.EqualTo("小拉达"));
        Assert.That(species.FormIds, Is.EqualTo(new[] { "pokemon-form:10193", "pokemon-form:19" }));

        PokemonFormSnapshotDto alola = first.Snapshot.Forms.Single(item => item.Id == "pokemon-form:10193");
        Assert.That(alola.SpeciesId, Is.EqualTo(species.Id));
        Assert.That(alola.FormKind, Is.EqualTo("regional"));
        Assert.That(alola.RegionId, Is.EqualTo("alola"));
        Assert.That(alola.IntroducedGenerationId, Is.EqualTo("generation-7"));
        Assert.That(alola.Disposition, Is.EqualTo("separate-entry"));
        Assert.That(alola.Names["zh"], Is.EqualTo("阿罗拉小拉达"));
        Assert.That(alola.RelatedFormIds, Is.EqualTo(new[] { "pokemon-form:19" }));
        Assert.That(first.SeparateEntryCount, Is.EqualTo(2));
        Assert.That(first.ManualReviewCount, Is.Zero);
    }

    [Test]
    public void Compiler_RejectsMissingReferencedPokemonAndUnknownVersionGroup()
    {
        PokeApiTaxonomyRawData missingPokemon = RattataRaw();
        missingPokemon.Pokemon.Remove(10091);
        PokeApiTaxonomyRawData unknownVersion = RattataRaw();
        unknownVersion.VersionGroups.Remove(17);

        Assert.That(
            Assert.Throws<InvalidOperationException>(() => PokeApiTaxonomyCompiler.Compile(
                missingPokemon, Policies(), DateTimeOffset.UtcNow)).Message,
            Does.Contain("missing Pokemon 10091"));
        Assert.That(
            Assert.Throws<InvalidOperationException>(() => PokeApiTaxonomyCompiler.Compile(
                unknownVersion, Policies(), DateTimeOffset.UtcNow)).Message,
            Does.Contain("unknown version group"));
    }

    [Test]
    public void Compiler_UsesTrackedSlugFallbackWhenFormNamesAreMissing()
    {
        PokeApiTaxonomyRawData raw = RattataRaw();
        raw.Forms[10193] = raw.Forms[10193].Replace(
            "[{'name':'Alolan Rattata','language':{'name':'en'}}]".Replace('\'', '"'),
            "[]");

        PokeApiTaxonomyCompileResult result = PokeApiTaxonomyCompiler.Compile(
            raw, Policies(), DateTimeOffset.Parse("2026-07-30T00:00:00Z"));

        PokemonFormSnapshotDto form = result.Snapshot.Forms.Single(item => item.Id == "pokemon-form:10193");
        Assert.That(form.Names["en"], Is.EqualTo("Rattata (Alola)"));
        Assert.That(form.Names["zh"], Is.EqualTo("阿罗拉小拉达"));
        Assert.That(result.Snapshot.Warnings,
            Does.Contain("fallback:pokemon-form:10193:name:en->slug"));
    }

    private static PokeApiTaxonomyRawData Reformat(PokeApiTaxonomyRawData source)
    {
        var result = new PokeApiTaxonomyRawData();
        Copy(source.Generations, result.Generations);
        Copy(source.Species, result.Species);
        Copy(source.Pokemon, result.Pokemon);
        Copy(source.Forms, result.Forms);
        Copy(source.VersionGroups, result.VersionGroups);
        return result;
    }

    private static void Copy(
        System.Collections.Generic.IDictionary<int, string> source,
        System.Collections.Generic.IDictionary<int, string> target)
    {
        foreach (var entry in source)
            target.Add(entry.Key, JsonConvert.SerializeObject(JsonConvert.DeserializeObject(entry.Value), Formatting.Indented));
    }

    private static PokemonFormClassificationCatalog Policies()
    {
        return PokemonContentOverrideLoader.LoadFormClassification(Path.Combine(
            Directory.GetCurrentDirectory(), "Assets", "Editor", "ContentImporter", "Overrides",
            "form-classification-overrides.json"));
    }

    private static PokeApiTaxonomyRawData RattataRaw()
    {
        var raw = new PokeApiTaxonomyRawData();
        raw.Generations.Add(1, @"{
          'id':1,'name':'generation-i',
          'names':[{'name':'Generation I','language':{'name':'en'}},{'name':'第一世代','language':{'name':'zh-hans'}}],
          'pokemon_species':[{'url':'https://pokeapi.co/api/v2/pokemon-species/19/'}]
        }".Replace('\'', '"'));
        raw.Generations.Add(7, @"{
          'id':7,'name':'generation-vii',
          'names':[{'name':'Generation VII','language':{'name':'en'}},{'name':'第七世代','language':{'name':'zh-hans'}}],
          'pokemon_species':[{'url':'https://pokeapi.co/api/v2/pokemon-species/722/'},{'url':'https://pokeapi.co/api/v2/pokemon-species/809/'}]
        }".Replace('\'', '"'));
        raw.Species.Add(19, @"{
          'id':19,'name':'rattata','generation':{'name':'generation-i'},
          'names':[{'name':'Rattata','language':{'name':'en'}},{'name':'小拉达','language':{'name':'zh-hans'}}],
          'genera':[{'genus':'Mouse Pokemon','language':{'name':'en'}},{'genus':'鼠宝可梦','language':{'name':'zh-hans'}}],
          'flavor_text_entries':[{'flavor_text':'A cautious Pokemon.','language':{'name':'en'}},{'flavor_text':'谨慎的宝可梦。','language':{'name':'zh-hans'}}],
          'varieties':[
            {'is_default':true,'pokemon':{'url':'https://pokeapi.co/api/v2/pokemon/19/'}},
            {'is_default':false,'pokemon':{'url':'https://pokeapi.co/api/v2/pokemon/10091/'}}
          ],
          'is_baby':false,'is_legendary':false,'is_mythical':false,'has_gender_differences':false,
          'color':{'name':'purple'},'habitat':{'name':'grassland'}
        }".Replace('\'', '"'));
        raw.Pokemon.Add(19, @"{
          'id':19,'name':'rattata','types':[{'slot':1,'type':{'name':'normal'}}],
          'forms':[{'url':'https://pokeapi.co/api/v2/pokemon-form/19/'}],
          'sprites':{'other':{'official-artwork':{'front_default':'https://raw.example/19.png'}}}
        }".Replace('\'', '"'));
        raw.Pokemon.Add(10091, @"{
          'id':10091,'name':'rattata-alola','types':[{'slot':1,'type':{'name':'dark'}},{'slot':2,'type':{'name':'normal'}}],
          'forms':[{'url':'https://pokeapi.co/api/v2/pokemon-form/10193/'}],
          'sprites':{'other':{'official-artwork':{'front_default':'https://raw.example/10091.png'}}}
        }".Replace('\'', '"'));
        raw.Forms.Add(19, @"{
          'id':19,'name':'rattata','form_name':'','is_default':true,'is_battle_only':false,'is_mega':false,
          'version_group':{'name':'red-blue'},'names':[],'sprites':{'front_default':'https://raw.example/sprite19.png'}
        }".Replace('\'', '"'));
        raw.Forms.Add(10193, @"{
          'id':10193,'name':'rattata-alola','form_name':'alola','is_default':true,'is_battle_only':false,'is_mega':false,
          'version_group':{'name':'sun-moon'},
          'names':[{'name':'Alolan Rattata','language':{'name':'en'}}],
          'sprites':{'front_default':'https://raw.example/sprite10091.png'}
        }".Replace('\'', '"'));
        raw.VersionGroups.Add(1, @"{'id':1,'name':'red-blue','generation':{'name':'generation-i'}}".Replace('\'', '"'));
        raw.VersionGroups.Add(17, @"{'id':17,'name':'sun-moon','generation':{'name':'generation-vii'}}".Replace('\'', '"'));
        return raw;
    }
}
