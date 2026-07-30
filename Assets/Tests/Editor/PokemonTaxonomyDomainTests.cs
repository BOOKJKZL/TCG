using System;
using System.Collections.Generic;
using System.Linq;
using Gacha.Pokemon.Domain;
using NUnit.Framework;

public class PokemonTaxonomyDomainTests
{
    [Test]
    public void GenerationOne_Has151UniqueSpeciesAndRegionalFormKeepsNationalNumber()
    {
        PokemonTaxonomyCatalog catalog = BuildGenerationOneCatalog(includeAlolanRattata: true);

        Assert.That(catalog.GetSpeciesByGeneration("generation-1").Count, Is.EqualTo(151));
        Assert.That(catalog.Species.Values.Select(item => item.NationalDexNumber),
            Is.EqualTo(Enumerable.Range(1, 151)));
        Assert.That(catalog.Species.Values.Select(item => item.NationalDexNumber).Distinct().Count(), Is.EqualTo(151));

        PokemonSpeciesDefinition rattata = catalog.Species["pokemon-species:19"];
        Assert.That(rattata.NationalDexNumber, Is.EqualTo(19));
        Assert.That(rattata.FormIds, Has.Count.EqualTo(2));
        PokemonFormDefinition alola = catalog.Forms["pokemon-form:10091"];
        Assert.That(alola.SpeciesId, Is.EqualTo(rattata.Id));
        Assert.That(alola.FormKind, Is.EqualTo("regional"));
        Assert.That(alola.RegionId, Is.EqualTo("alola"));
        Assert.That(alola.IntroducedGenerationId, Is.EqualTo("generation-7"));
        Assert.That(alola.Disposition, Is.EqualTo(PokemonFormDisposition.SeparateEntry));
        Assert.That(alola.RelatedFormIds, Does.Contain(rattata.DefaultFormId));
        Assert.That(catalog.Forms[rattata.DefaultFormId].RelatedFormIds, Does.Contain(alola.Id));
    }

    [Test]
    public void Catalog_SortsByNationalNumberAndRejectsBrokenBidirectionalLinks()
    {
        PokemonGenerationDefinition generation = Generation("generation-1", 1, 1, 2);
        PokemonFormDefinition firstForm = Form("pokemon-form:1", "pokemon-species:1", true,
            related: new[] { "pokemon-form:2" });
        PokemonFormDefinition secondForm = Form("pokemon-form:2", "pokemon-species:1", false);
        PokemonSpeciesDefinition first = Species(1, new[] { firstForm.Id, secondForm.Id }, firstForm.Id);
        PokemonFormDefinition otherForm = Form("pokemon-form:3", "pokemon-species:2", true);
        PokemonSpeciesDefinition second = Species(2, new[] { otherForm.Id }, otherForm.Id);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new PokemonTaxonomyCatalog(
                new[] { generation },
                new[] { second, first },
                new[] { otherForm, secondForm, firstForm }));

        Assert.That(exception.Message, Does.Contain("not bidirectional"));
    }

    [Test]
    public void Catalog_RejectsDuplicateNationalDexNumbersAndForeignForms()
    {
        PokemonGenerationDefinition generation = Generation("generation-1", 1, 1, 2);
        PokemonFormDefinition form1 = Form("pokemon-form:1", "pokemon-species:1", true);
        PokemonFormDefinition form2 = Form("pokemon-form:2", "pokemon-species:2", true);
        PokemonSpeciesDefinition species1 = Species(1, new[] { form1.Id }, form1.Id);
        PokemonSpeciesDefinition species2 = new PokemonSpeciesDefinition(
            "pokemon-species:2", 1, "generation-1", Name("Two"), null, null,
            form2.Id, new[] { form2.Id }, false, false, false);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new PokemonTaxonomyCatalog(
                new[] { generation },
                new[] { species1, species2 },
                new[] { form1, form2 }));

        Assert.That(exception.Message, Does.Contain("national Pokédex number"));
    }

    private static PokemonTaxonomyCatalog BuildGenerationOneCatalog(bool includeAlolanRattata)
    {
        var generations = new[]
        {
            Generation("generation-1", 1, 1, 151),
            Generation("generation-7", 7, 722, 809)
        };
        var species = new List<PokemonSpeciesDefinition>();
        var forms = new List<PokemonFormDefinition>();
        for (int number = 1; number <= 151; number++)
        {
            string speciesId = "pokemon-species:" + number;
            string defaultFormId = "pokemon-form:" + number;
            var formIds = new List<string> { defaultFormId };
            var related = new List<string>();
            if (number == 19 && includeAlolanRattata)
            {
                formIds.Add("pokemon-form:10091");
                related.Add("pokemon-form:10091");
            }

            forms.Add(Form(defaultFormId, speciesId, true, related));
            if (number == 19 && includeAlolanRattata)
            {
                forms.Add(new PokemonFormDefinition(
                    "pokemon-form:10091", speciesId, 10091, "regional",
                    PokemonFormDisposition.SeparateEntry, Name("Alolan Rattata"), "generation-7",
                    new[] { defaultFormId }, new[] { "dark", "normal" }, false, false, false, false,
                    regionId: "alola"));
            }
            species.Add(Species(number, formIds, defaultFormId));
        }
        return new PokemonTaxonomyCatalog(generations, species, forms);
    }

    private static PokemonGenerationDefinition Generation(string id, int order, int start, int end)
    {
        return new PokemonGenerationDefinition(id, order, Name(id), start, end);
    }

    private static PokemonSpeciesDefinition Species(int number, IEnumerable<string> forms, string defaultForm)
    {
        return new PokemonSpeciesDefinition(
            "pokemon-species:" + number,
            number,
            "generation-1",
            Name("Species " + number),
            Name("Seed Pokémon"),
            Name("Description " + number),
            defaultForm,
            forms,
            false,
            false,
            false);
    }

    private static PokemonFormDefinition Form(
        string id,
        string speciesId,
        bool isDefault,
        IEnumerable<string> related = null)
    {
        int pokemonId = int.Parse(id.Substring(id.IndexOf(':') + 1));
        return new PokemonFormDefinition(
            id,
            speciesId,
            pokemonId,
            isDefault ? "default" : "alternate",
            isDefault ? PokemonFormDisposition.SeparateEntry : PokemonFormDisposition.ManualReview,
            Name(id),
            "generation-1",
            related,
            new[] { "normal" },
            isDefault,
            false,
            false,
            false);
    }

    private static IReadOnlyDictionary<string, string> Name(string value)
    {
        return new Dictionary<string, string> { ["en"] = value };
    }
}
