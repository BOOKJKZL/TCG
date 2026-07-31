using System.Linq;
using Gacha.EditorTools.Content;
using NUnit.Framework;

public sealed class PokemonCompleteReleasePublisherTests
{
    [Test]
    public void BuildDefinitions_CoversEveryRuntimePackageWithUniqueIdentityAndInstallPath()
    {
        ContentPackagePublishDefinition[] definitions = PokemonCompleteReleasePublisher
            .BuildDefinitions().ToArray();

        Assert.That(definitions.Length, Is.EqualTo(538));
        Assert.That(definitions.Select(value => value.PackageId).Distinct().Count(), Is.EqualTo(538));
        Assert.That(definitions.All(value => value.PackageId == value.PackageId.ToLowerInvariant()), Is.True);
        Assert.That(definitions.All(value => value.PackageId.All(character =>
            character >= 'a' && character <= 'z' || character >= '0' && character <= '9' ||
            character == '.' || character == '-' || character == '_')),
            Is.True);
        Assert.That(definitions.Select(value => value.InstallRelativePath)
            .Distinct(System.StringComparer.OrdinalIgnoreCase).Count(), Is.EqualTo(538));
        Assert.That(definitions.Count(value => value.PackageId.StartsWith("en.")), Is.EqualTo(218));
        Assert.That(definitions.Count(value => value.PackageId.StartsWith("ja.")), Is.EqualTo(177));
        Assert.That(definitions.Count(value => value.PackageId.StartsWith("zh-cn.")), Is.EqualTo(129));
        Assert.That(definitions.Count(value => value.PackageId.StartsWith("pokemon.card-subject-links.")),
            Is.EqualTo(3));
        Assert.That(definitions.Count(value => value.PackageId.StartsWith("pokemon.pokedex.artwork.")), Is.EqualTo(9));
        Assert.That(definitions.All(value => value.Metadata != null && !value.Metadata.IsLegacy), Is.True);
        ContentPackagePublishDefinition[] cardSets = definitions.Where(value =>
            value.Metadata.Kind == "card-set").ToArray();
        Assert.That(cardSets, Has.Length.EqualTo(524));
        Assert.That(cardSets.All(value => value.Metadata.GameId == "pokemon-tcg" &&
                                          value.Metadata.GenerationOrder.HasValue &&
                                          value.Metadata.SortOrdinal.HasValue &&
                                          !string.IsNullOrWhiteSpace(value.Metadata.SetId) &&
                                          !string.IsNullOrWhiteSpace(value.Metadata.SetCode)), Is.True);
        Assert.That(cardSets.Count(value => value.Metadata.ReleaseDate.HasValue), Is.EqualTo(421));
        Assert.That(cardSets.Count(value => !value.Metadata.ReleaseDate.HasValue), Is.EqualTo(103));
        ContentPackagePublishDefinition languageGroups = definitions.Single(value =>
            value.PackageId == PrintingLanguageGroupPackagePublisher.PackageId);
        Assert.That(languageGroups.InstallRelativePath,
            Is.EqualTo("runtime/printing-language-groups"));
        Assert.That(languageGroups.IncludedRelativePaths,
            Is.EqualTo(new[] { "printing-language-groups.json" }));
        Assert.That(languageGroups.Revision, Is.EqualTo(1));
        ContentPackagePublishDefinition[] updatedLinks = definitions.Where(value =>
            value.PackageId.StartsWith("pokemon.card-subject-links.")).ToArray();
        Assert.That(updatedLinks, Has.Length.EqualTo(3));
        Assert.That(updatedLinks.All(value => value.Revision == 5 && value.Version == "4.1.0" &&
                                                  value.Metadata.Dependencies.SequenceEqual(
                                                      new[] { "pokemon.pokedex.taxonomy" })),
            Is.True);
        Assert.That(definitions.Where(value => value != languageGroups &&
                                              !updatedLinks.Contains(value))
            .All(value => value.Revision == 4 && value.Version == "4.0.0"), Is.True,
            "The other 534 descriptors must remain unchanged so phones do not redownload card images.");
        Assert.That(PokemonCompleteReleasePublisher.CatalogRevision, Is.EqualTo(6));
        Assert.That(PokemonCompleteReleasePublisher.PreviousCatalogRevision, Is.EqualTo(5));
        Assert.That(definitions.Single(value => value.PackageId == "pokemon.pokedex.taxonomy")
            .IncludedRelativePaths, Is.EqualTo(new[] { "pokemon-taxonomy.json" }));
        Assert.That(definitions.Single(value => string.Equals(
                value.InstallRelativePath, "ja/sm1+", System.StringComparison.OrdinalIgnoreCase)).PackageId,
            Is.EqualTo("ja.sm1_2b"));
    }

    [TestCase("sm1+", "sm1_2b")]
    [TestCase("A_B", "a_5fb")]
    [TestCase("包", "_e5_8c_85")]
    public void EncodePackageIdSegment_UsesStableUtf8Escapes(string source, string expected)
    {
        Assert.That(PokemonCompleteReleasePublisher.EncodePackageIdSegment(source), Is.EqualTo(expected));
    }
}
