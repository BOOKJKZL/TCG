using System;
using System.Collections.Generic;
using Gacha.Pokemon.Domain;
using NUnit.Framework;

public class PokemonCardSubjectDomainTests
{
    [Test]
    public void Catalog_IndexesSpeciesFormsAndEveryPrinting()
    {
        PokemonTaxonomyCatalog taxonomy = Taxonomy();
        var speciesLink = Link("card-1", new[] { "printing-1", "printing-2" },
            new[] { "pokemon-species:19" }, Array.Empty<string>(),
            PokemonCardMatchStatus.MatchedSpecies, PokemonCardMatchMethod.SourceDexId);
        var formLink = Link("card-2", new[] { "printing-3" },
            new[] { "pokemon-species:19" }, new[] { "pokemon-form:10193" },
            PokemonCardMatchStatus.MatchedForm, PokemonCardMatchMethod.SourceDexIdAndFormName);

        var catalog = new PokemonCardSubjectCatalog(new[] { formLink, speciesLink }, taxonomy);

        Assert.That(catalog.Cards.Count, Is.EqualTo(2));
        Assert.That(catalog.Printings.Count, Is.EqualTo(3));
        Assert.That(catalog.Printings["printing-2"], Is.SameAs(speciesLink));
        Assert.That(catalog.GetBySpecies("pokemon-species:19").Count, Is.EqualTo(2));
        Assert.That(catalog.GetByForm("pokemon-form:10193").Count, Is.EqualTo(1));
    }

    [Test]
    public void Link_RejectsContradictoryStatusesAndManualOverrideWithoutId()
    {
        Assert.Throws<ArgumentException>(() => Link(
            "bad", new[] { "printing" }, new[] { "pokemon-species:19" }, Array.Empty<string>(),
            PokemonCardMatchStatus.NotApplicable, PokemonCardMatchMethod.Category));
        Assert.Throws<ArgumentException>(() => new PokemonCardSubjectLink(
            "bad-override", "set", "1", "item", new[] { "printing" }, "Pokemon", "Rattata",
            new[] { "pokemon-species:19" }, Array.Empty<string>(),
            PokemonCardMatchStatus.MatchedSpecies, PokemonCardMatchMethod.ManualOverride, 1d));
    }

    [Test]
    public void Catalog_RejectsUnknownOrForeignForms()
    {
        PokemonCardSubjectLink bad = Link(
            "bad", new[] { "printing" }, new[] { "pokemon-species:20" },
            new[] { "pokemon-form:10193" }, PokemonCardMatchStatus.MatchedForm,
            PokemonCardMatchMethod.SourceDexIdAndFormName);

        Assert.That(
            Assert.Throws<ArgumentException>(() =>
                new PokemonCardSubjectCatalog(new[] { bad }, Taxonomy())).Message,
            Does.Contain("outside its species"));
    }

    private static PokemonCardSubjectLink Link(
        string id,
        IEnumerable<string> printings,
        IEnumerable<string> species,
        IEnumerable<string> forms,
        PokemonCardMatchStatus status,
        PokemonCardMatchMethod method)
    {
        return new PokemonCardSubjectLink(
            id, "set", id, "item:" + id, printings, "Pokemon", id,
            species, forms, status, method, 1d);
    }

    private static PokemonTaxonomyCatalog Taxonomy()
    {
        var generationOne = new PokemonGenerationDefinition(
            "generation-1", 1, Name("Generation I"), 19, 20);
        var generationSeven = new PokemonGenerationDefinition(
            "generation-7", 7, Name("Generation VII"), 722, 809);
        var defaultForm = new PokemonFormDefinition(
            "pokemon-form:19", "pokemon-species:19", 19, "default",
            PokemonFormDisposition.SeparateEntry, Name("Rattata"), "generation-1",
            new[] { "pokemon-form:10193" }, new[] { "normal" }, true, false, false, false);
        var alola = new PokemonFormDefinition(
            "pokemon-form:10193", "pokemon-species:19", 10091, "regional",
            PokemonFormDisposition.SeparateEntry, Name("Alolan Rattata"), "generation-7",
            new[] { "pokemon-form:19" }, new[] { "dark", "normal" }, false, false, false, false,
            regionId: "alola");
        var other = new PokemonFormDefinition(
            "pokemon-form:20", "pokemon-species:20", 20, "default",
            PokemonFormDisposition.SeparateEntry, Name("Raticate"), "generation-1",
            Array.Empty<string>(), new[] { "normal" }, true, false, false, false);
        return new PokemonTaxonomyCatalog(
            new[] { generationOne, generationSeven },
            new[]
            {
                new PokemonSpeciesDefinition(
                    "pokemon-species:19", 19, "generation-1", Name("Rattata"), null, null,
                    defaultForm.Id, new[] { defaultForm.Id, alola.Id }, false, false, false),
                new PokemonSpeciesDefinition(
                    "pokemon-species:20", 20, "generation-1", Name("Raticate"), null, null,
                    other.Id, new[] { other.Id }, false, false, false)
            },
            new[] { defaultForm, alola, other });
    }

    private static IReadOnlyDictionary<string, string> Name(string value) =>
        new Dictionary<string, string> { ["en"] = value };
}
