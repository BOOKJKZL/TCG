using System;
using System.Collections.Generic;
using Gacha.Pokemon.Domain;
using Gacha.Pokemon.Infrastructure;
using NUnit.Framework;

public class PokemonCardSubjectSnapshotTests
{
    [Test]
    public void Reader_BuildsPrintingAndSpeciesIndexes()
    {
        PokemonCardSubjectSnapshotLoadResult result =
            new PokemonCardSubjectSnapshotReader().Read(ValidJson(), Taxonomy());

        Assert.That(result.Language, Is.EqualTo("en"));
        Assert.That(result.Catalog.Cards.Count, Is.EqualTo(2));
        Assert.That(result.Catalog.Printings.Count, Is.EqualTo(3));
        Assert.That(result.Catalog.GetBySpecies("pokemon-species:19").Count, Is.EqualTo(1));
        Assert.That(result.Catalog.Cards["trainer-1"].Status,
            Is.EqualTo(PokemonCardMatchStatus.NotApplicable));
    }

    [Test]
    public void Reader_RejectsUnknownStatusAndTaxonomyReferences()
    {
        string badStatus = ValidJson().Replace("matched-species", "invented");
        string badSpecies = ValidJson().Replace("pokemon-species:19", "pokemon-species:999");

        Assert.That(
            Assert.Throws<PokemonCardSubjectSnapshotException>(() =>
                new PokemonCardSubjectSnapshotReader().Read(badStatus, Taxonomy())).Message,
            Does.Contain("Unsupported card subject status"));
        Assert.That(
            Assert.Throws<PokemonCardSubjectSnapshotException>(() =>
                new PokemonCardSubjectSnapshotReader().Read(badSpecies, Taxonomy())).Message,
            Does.Contain("unknown species"));
    }

    private static string ValidJson() => @"{
      'SchemaVersion':1,'Source':'tcgdex','Language':'EN','GeneratedAtUtc':'2026-07-30T00:00:00Z',
      'TaxonomySourceSha256':'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',
      'CardContentSha256':'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb',
      'Links':[
        {'CardId':'card-19','SetId':'set','LocalId':'19','ItemId':'item:19',
         'PrintingIds':['printing:19:normal','printing:19:holo'],'Category':'Pokemon','CardName':'Rattata',
         'SpeciesIds':['pokemon-species:19'],'FormIds':[],'Status':'matched-species',
         'Method':'source-dex-id','Confidence':1.0},
        {'CardId':'trainer-1','SetId':'set','LocalId':'20','ItemId':'item:20',
         'PrintingIds':['printing:20'],'Category':'Trainer','CardName':'Potion',
         'SpeciesIds':[],'FormIds':[],'Status':'not-applicable','Method':'category','Confidence':1.0}
      ],'Warnings':[]
    }".Replace('\'', '"');

    private static PokemonTaxonomyCatalog Taxonomy()
    {
        var generation = new PokemonGenerationDefinition(
            "generation-1", 1, Name("Generation I"), 19, 19);
        var form = new PokemonFormDefinition(
            "pokemon-form:19", "pokemon-species:19", 19, "default",
            PokemonFormDisposition.SeparateEntry, Name("Rattata"), "generation-1",
            Array.Empty<string>(), new[] { "normal" }, true, false, false, false);
        var species = new PokemonSpeciesDefinition(
            "pokemon-species:19", 19, "generation-1", Name("Rattata"), null, null,
            form.Id, new[] { form.Id }, false, false, false);
        return new PokemonTaxonomyCatalog(new[] { generation }, new[] { species }, new[] { form });
    }

    private static IReadOnlyDictionary<string, string> Name(string value) =>
        new Dictionary<string, string> { ["en"] = value };
}
