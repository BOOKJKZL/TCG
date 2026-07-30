using System;
using System.Collections.Generic;
using System.Linq;
using Gacha.Pokemon.Application;
using Gacha.Pokemon.Domain;
using NUnit.Framework;

public sealed class PokemonPokedexBrowserTests
{
    [Test]
    public void GenerationOne_ContainsExactly151SpeciesInNationalDexOrder()
    {
        PokemonPokedexBrowser browser = Browser(151);

        Assert.That(browser.VisibleSpecies.Count, Is.EqualTo(151));
        Assert.That(browser.VisibleSpecies.Select(value => value.NationalDexNumber),
            Is.EqualTo(Enumerable.Range(1, 151)));
    }

    [Test]
    public void Search_FindsLocalizedNameAndPaddedNationalNumber()
    {
        PokemonPokedexBrowser browser = Browser(151);

        browser.Search("宝可梦 25");
        Assert.That(browser.VisibleSpecies.Single().NationalDexNumber, Is.EqualTo(25));
        browser.Search("#001");
        Assert.That(browser.VisibleSpecies.Single().NationalDexNumber, Is.EqualTo(1));
    }

    [Test]
    public void FormNavigation_IsBidirectionalAndPreservesHistory()
    {
        PokemonTaxonomyCatalog taxonomy = Taxonomy(1, includeRegionalForm: true);
        var browser = new PokemonPokedexBrowser(taxonomy);

        Assert.That(browser.OpenSpecies("species:1"), Is.True);
        Assert.That(browser.OpenForm("form:1-alola"), Is.True);
        Assert.That(browser.SelectedForm.Id, Is.EqualTo("form:1-alola"));
        Assert.That(browser.NavigateBack(), Is.True);
        Assert.That(browser.SelectedForm.Id, Is.EqualTo("form:1"));
    }

    [Test]
    public void ManualAndExcludedForms_DoNotBecomeSelectableEntries()
    {
        PokemonTaxonomyCatalog taxonomy = Taxonomy(1, includeRegionalForm: true, includeReviewForm: true);
        var browser = new PokemonPokedexBrowser(taxonomy);
        browser.OpenSpecies("species:1");

        Assert.That(browser.SelectableForms.Select(value => value.Id),
            Is.EqualTo(new[] { "form:1", "form:1-alola" }));
        Assert.That(browser.OpenForm("form:1-review"), Is.False);
    }

    [Test]
    public void LaterGeneration_SeparatesNewSpeciesFromOldSpeciesNewForms()
    {
        PokemonTaxonomyCatalog taxonomy = CrossGenerationTaxonomy();
        var browser = new PokemonPokedexBrowser(taxonomy);

        browser.SelectGeneration("generation-2");

        Assert.That(browser.VisibleSpecies.Select(value => value.Id), Is.EqualTo(new[] { "species:2" }));
        Assert.That(browser.VisibleIntroducedForms.Select(value => value.Id),
            Is.EqualTo(new[] { "form:1-region" }));
        Assert.That(browser.OpenSpecies("species:1", "form:1-region"), Is.True);
        Assert.That(browser.SelectedSpecies.Id, Is.EqualTo("species:1"));
        Assert.That(browser.SelectedForm.Id, Is.EqualTo("form:1-region"));
    }

    private static PokemonPokedexBrowser Browser(int count) => new PokemonPokedexBrowser(Taxonomy(count));

    private static PokemonTaxonomyCatalog Taxonomy(
        int speciesCount,
        bool includeRegionalForm = false,
        bool includeReviewForm = false)
    {
        var generation = new PokemonGenerationDefinition(
            "generation-1", 1, Names("Generation I", "第一世代"), 1, 151);
        var species = new List<PokemonSpeciesDefinition>();
        var forms = new List<PokemonFormDefinition>();
        for (int number = 1; number <= speciesCount; number++)
        {
            string speciesId = "species:" + number;
            string defaultFormId = "form:" + number;
            var formIds = new List<string> { defaultFormId };
            var related = new List<string>();
            if (number == 1 && includeRegionalForm)
            {
                formIds.Add("form:1-alola");
                related.Add("form:1-alola");
            }
            if (number == 1 && includeReviewForm)
            {
                formIds.Add("form:1-review");
                related.Add("form:1-review");
            }
            species.Add(new PokemonSpeciesDefinition(
                speciesId, number, generation.Id,
                Names("Pokemon " + number, "宝可梦 " + number),
                Names("Test Pokemon", "测试宝可梦"),
                Names("Description " + number, "介绍 " + number),
                defaultFormId, formIds, false, false, false));
            forms.Add(Form(defaultFormId, speciesId, number, true,
                PokemonFormDisposition.SeparateEntry, related));
            if (number == 1 && includeRegionalForm)
                forms.Add(Form("form:1-alola", speciesId, 10001, false,
                    PokemonFormDisposition.SeparateEntry,
                    includeReviewForm ? new[] { defaultFormId, "form:1-review" } : new[] { defaultFormId }));
            if (number == 1 && includeReviewForm)
                forms.Add(Form("form:1-review", speciesId, 10002, false,
                    PokemonFormDisposition.ManualReview, new[] { defaultFormId, "form:1-alola" }));
        }
        return new PokemonTaxonomyCatalog(new[] { generation }, species, forms);
    }

    private static PokemonFormDefinition Form(
        string id,
        string speciesId,
        int pokemonId,
        bool isDefault,
        PokemonFormDisposition disposition,
        IEnumerable<string> related) =>
        new PokemonFormDefinition(
            id, speciesId, pokemonId, isDefault ? "default" : "regional", disposition,
            Names(id, id), "generation-1", related, new[] { "grass" },
            isDefault, false, false, false, isDefault ? null : "alola");

    private static IReadOnlyDictionary<string, string> Names(string english, string chinese) =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = english,
            ["zh"] = chinese
        };

    private static PokemonTaxonomyCatalog CrossGenerationTaxonomy()
    {
        var generations = new[]
        {
            new PokemonGenerationDefinition("generation-1", 1, Names("Generation I", "第一世代"), 1, 1),
            new PokemonGenerationDefinition("generation-2", 2, Names("Generation II", "第二世代"), 2, 2)
        };
        var species = new[]
        {
            new PokemonSpeciesDefinition(
                "species:1", 1, "generation-1", Names("One", "一"), Names("One", "一"),
                Names("One", "一"), "form:1", new[] { "form:1", "form:1-region" }, false, false, false),
            new PokemonSpeciesDefinition(
                "species:2", 2, "generation-2", Names("Two", "二"), Names("Two", "二"),
                Names("Two", "二"), "form:2", new[] { "form:2" }, false, false, false)
        };
        var forms = new[]
        {
            new PokemonFormDefinition(
                "form:1", "species:1", 1, "default", PokemonFormDisposition.SeparateEntry,
                Names("One", "一"), "generation-1", new[] { "form:1-region" }, new[] { "normal" },
                true, false, false, false),
            new PokemonFormDefinition(
                "form:1-region", "species:1", 10001, "regional", PokemonFormDisposition.SeparateEntry,
                Names("Region One", "地区一"), "generation-2", new[] { "form:1" }, new[] { "dark" },
                false, false, false, false, "test-region"),
            new PokemonFormDefinition(
                "form:2", "species:2", 2, "default", PokemonFormDisposition.SeparateEntry,
                Names("Two", "二"), "generation-2", Array.Empty<string>(), new[] { "normal" },
                true, false, false, false)
        };
        return new PokemonTaxonomyCatalog(generations, species, forms);
    }
}
