using System;
using System.Collections.Generic;
using System.Linq;
using Gacha.Pokemon.Domain;
using Gacha.Pokemon.Infrastructure;
using Newtonsoft.Json;
using NUnit.Framework;

public class PokemonTaxonomySnapshotTests
{
    [Test]
    public void Reader_BuildsLocalizedCatalogAndRegionalLinks()
    {
        PokemonTaxonomySnapshotLoadResult result = new PokemonTaxonomySnapshotReader().Read(ValidSnapshot());

        Assert.That(result.Source, Is.EqualTo("pokeapi"));
        Assert.That(result.SourceBaseUri, Is.EqualTo(new Uri("https://pokeapi.co/api/v2/")));
        Assert.That(result.SourceSha256, Has.Length.EqualTo(64));
        Assert.That(result.Languages, Is.EqualTo(new[] { "en", "zh" }));
        Assert.That(result.Catalog.Species.Count, Is.EqualTo(1));
        PokemonSpeciesDefinition species = result.Catalog.Species["pokemon-species:19"];
        Assert.That(species.GetDisplayName("zh"), Is.EqualTo("小拉达"));
        Assert.That(species.Descriptions["en"], Is.EqualTo("A cautious Pokémon."));
        Assert.That(result.Catalog.GetForms(species.Id).Count, Is.EqualTo(2));
        PokemonFormDefinition regional = result.Catalog.Forms["pokemon-form:10091"];
        Assert.That(regional.RegionId, Is.EqualTo("alola"));
        Assert.That(regional.Disposition, Is.EqualTo(PokemonFormDisposition.SeparateEntry));
        Assert.That(regional.ImageRelativePath, Is.EqualTo("images/10091.png"));
    }

    [Test]
    public void Reader_RejectsUnsafeImagePathsAndUnknownDispositions()
    {
        string unsafePath = ValidSnapshot().Replace("images/10091.png", "../secret.png");
        string unknownDisposition = ValidSnapshot().Replace("separate-entry", "invented");

        Assert.That(
            Assert.Throws<PokemonTaxonomySnapshotException>(() =>
                new PokemonTaxonomySnapshotReader().Read(unsafePath)).Message,
            Does.Contain("unsafe image path"));
        Assert.That(
            Assert.Throws<PokemonTaxonomySnapshotException>(() =>
                new PokemonTaxonomySnapshotReader().Read(unknownDisposition)).Message,
            Does.Contain("Unsupported Pokémon form disposition"));
    }

    [Test]
    public void Reader_RejectsBrokenRelatedFormLinksAndDuplicateDexNumbers()
    {
        string oneWay = ValidSnapshot().Replace(
            "\"RelatedFormIds\":[\"pokemon-form:10091\"]",
            "\"RelatedFormIds\":[]");
        PokemonTaxonomySnapshotDto duplicateDto =
            JsonConvert.DeserializeObject<PokemonTaxonomySnapshotDto>(ValidSnapshot());
        duplicateDto.Species.Add(new PokemonSpeciesSnapshotDto
        {
            Id = "pokemon-species:20",
            NationalDexNumber = 19,
            DebutGenerationId = "generation-1",
            Names = new Dictionary<string, string> { ["en"] = "Duplicate" },
            DefaultFormId = "pokemon-form:20",
            FormIds = new List<string> { "pokemon-form:20" }
        });
        duplicateDto.Forms.Add(new PokemonFormSnapshotDto
        {
            Id = "pokemon-form:20",
            SpeciesId = "pokemon-species:20",
            PokemonId = 20,
            FormKind = "default",
            Disposition = "separate-entry",
            Names = new Dictionary<string, string> { ["en"] = "Duplicate" },
            IntroducedGenerationId = "generation-1",
            IsDefault = true
        });
        string duplicate = JsonConvert.SerializeObject(duplicateDto);

        Assert.That(
            Assert.Throws<PokemonTaxonomySnapshotException>(() =>
                new PokemonTaxonomySnapshotReader().Read(oneWay)).Message,
            Does.Contain("not bidirectional"));
        Assert.That(
            Assert.Throws<PokemonTaxonomySnapshotException>(() =>
                new PokemonTaxonomySnapshotReader().Read(duplicate)).Message,
            Does.Contain("national Pokédex number"));
    }

    private static string ValidSnapshot()
    {
        return @"{
          'SchemaVersion':1,
          'Source':'pokeapi',
          'SourceBaseUrl':'https://pokeapi.co/api/v2/',
          'CapturedAtUtc':'2026-07-30T00:00:00Z',
          'SourceSha256':'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',
          'Languages':['zh','en'],
          'Generations':[
            {'Id':'generation-1','Order':1,'Names':{'en':'Generation I','zh':'第一世代'},'SpeciesStartNumber':1,'SpeciesEndNumber':151},
            {'Id':'generation-7','Order':7,'Names':{'en':'Generation VII','zh':'第七世代'},'SpeciesStartNumber':722,'SpeciesEndNumber':809}
          ],
          'Species':[
            {'Id':'pokemon-species:19','NationalDexNumber':19,'DebutGenerationId':'generation-1',
             'Names':{'en':'Rattata','zh':'小拉达'},'Genera':{'en':'Mouse Pokémon'},
             'Descriptions':{'en':'A cautious Pokémon.'},'DefaultFormId':'pokemon-form:19',
             'FormIds':['pokemon-form:19','pokemon-form:10091']}
          ],
          'Forms':[
            {'Id':'pokemon-form:19','SpeciesId':'pokemon-species:19','PokemonId':19,'FormKind':'default',
             'Disposition':'separate-entry','Names':{'en':'Rattata','zh':'小拉达'},
             'IntroducedGenerationId':'generation-1','RelatedFormIds':['pokemon-form:10091'],
             'TypeIds':['normal'],'IsDefault':true},
            {'Id':'pokemon-form:10091','SpeciesId':'pokemon-species:19','PokemonId':10091,'FormKind':'regional',
             'Disposition':'separate-entry','Names':{'en':'Alolan Rattata','zh':'阿罗拉小拉达'},
             'IntroducedGenerationId':'generation-7','RelatedFormIds':['pokemon-form:19'],
             'TypeIds':['dark','normal'],'RegionId':'alola','ImageRelativePath':'images/10091.png',
             'ImageSha256':'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb'}
          ],
          'Warnings':[]
        }".Replace('\'', '"');
    }
}
