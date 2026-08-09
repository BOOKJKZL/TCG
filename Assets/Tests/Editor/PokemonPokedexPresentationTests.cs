using System.IO;
using Gacha.Pokemon.Presentation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

public sealed class PokemonPokedexPresentationTests
{
    private static readonly string[] LocalizedKeys =
    {
        "title", "subtitle", "close", "back", "generation", "search", "empty", "count",
        "new_forms", "number", "debut", "forms", "cards", "card_count", "card_scope_form",
        "card_scope_species", "card_search", "card_sort", "card_sort_set", "card_sort_name",
        "card_empty_form", "card_empty_species", "card_installed", "card_not_installed",
        "manage_downloads", "content_missing", "art_pending", "art_hint", "types", "region"
    };

    [Test]
    public void PokedexView_UsesStableMobileToolkitContract()
    {
        const string viewPath = "Assets/Resources/UI/PokedexView.uxml";
        const string stylesPath = "Assets/Resources/UI/PokedexStyles.uss";
        string view = File.ReadAllText(viewPath);
        string styles = File.ReadAllText(stylesPath);

        Assert.That(AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(viewPath), Is.Not.Null);
        Assert.That(AssetDatabase.LoadAssetAtPath<StyleSheet>(stylesPath), Is.Not.Null);
        Assert.That(view, Does.Not.Contain("<ui:Button"));
        Assert.That(view, Does.Not.Contain("style="));
        Assert.That(view, Does.Contain("virtualization-method=\"DynamicHeight\""));
        Assert.That(view, Does.Contain("pokedex-species-list"));
        Assert.That(view, Does.Contain("pokedex-card-list"));
        Assert.That(styles, Does.Contain(".pokedex-card-grid-row"));
        Assert.That(styles, Does.Contain(".pokedex-card-tile"));
        Assert.That(styles, Does.Not.Contain(":active"));
        Assert.That(styles, Does.Not.Contain(".pokedex-card-tile:focus {") );
        Assert.That(styles, Does.Not.Contain(".pokedex-species-tile:focus,"));
        Assert.That(styles, Does.Not.Contain("box-shadow"));
        Assert.That(styles, Does.Not.Contain("z-index"));
        Assert.That(styles, Does.Not.Contain("gap:"));
    }

    [Test]
    public void PokedexController_ComposesSharedShellAndStableActions()
    {
        string source = File.ReadAllText(
            "Assets/Scripts/Modules/Gacha.Pokemon.Presentation/PokemonPokedexController.cs");

        Assert.That(source, Does.Contain("new MobilePageShell"));
        Assert.That(source, Does.Contain("new MobileTopBar"));
        Assert.That(source, Does.Contain("new MobilePrimaryNavigation"));
        Assert.That(source, Does.Contain("new MobileActionControl"));
        Assert.That(source, Does.Contain("CollectionVirtualizationMethod.DynamicHeight"));
        Assert.That(source, Does.Not.Contain("new Button"));
        Assert.That(source, Does.Not.Contain("UiToolkitSafeArea.Attach(root)"));
        Assert.That(source, Does.Not.Contain("tooltip = form.Id"));
    }

    [Test]
    public void PokedexChrome_HasCompleteEnglishChineseJapaneseCoverage()
    {
        foreach (string key in LocalizedKeys)
        {
            foreach (string language in new[] { "en", "zh", "ja" })
            {
                string value = PokemonPokedexText.Get(key, language);
                Assert.That(value, Is.Not.Null.And.Not.Empty, $"{key}/{language}");
                Assert.That(value, Is.Not.EqualTo(key), $"{key}/{language}");
            }
        }
    }

    [Test]
    public void PokedexTaxonomy_HasSemanticNamesForEverySnapshotIdentifierInThreeLanguages()
    {
        string[] typeIds =
        {
            "bug", "dark", "dragon", "electric", "fairy", "fighting", "fire", "flying",
            "ghost", "grass", "ground", "ice", "normal", "poison", "psychic", "rock", "steel", "water"
        };
        string[] regionIds = { "alola", "galar", "hisui", "paldea" };
        string[] formKinds =
        {
            "alternate", "battle-only", "cosmetic", "default", "gender-difference",
            "gigantamax", "mega", "regional"
        };

        foreach (string language in new[] { "en", "zh", "ja" })
        {
            foreach (string id in typeIds)
                AssertSemantic(PokemonPokedexText.TypeName(id, language), id, language);
            foreach (string id in regionIds)
                AssertSemantic(PokemonPokedexText.RegionName(id, language), id, language);
            foreach (string id in formKinds)
                AssertSemantic(PokemonPokedexText.FormKindName(id, language), id, language);
        }

        Assert.That(PokemonPokedexText.TypeName("electric", "zh"), Is.EqualTo("电"));
        Assert.That(PokemonPokedexText.TypeName("electric", "ja"), Is.EqualTo("でんき"));
        Assert.That(PokemonPokedexText.RegionName("alola", "zh"), Is.EqualTo("阿罗拉"));
        Assert.That(PokemonPokedexText.RegionName("alola", "ja"), Is.EqualTo("アローラ"));
    }

    private static void AssertSemantic(string value, string rawId, string language)
    {
        Assert.That(value, Is.Not.Null.And.Not.Empty, $"{rawId}/{language}");
        Assert.That(value, Is.Not.EqualTo(rawId), $"{rawId}/{language}");
    }
}
