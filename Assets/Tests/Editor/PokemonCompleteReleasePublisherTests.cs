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

        Assert.That(definitions.Length, Is.EqualTo(229));
        Assert.That(definitions.Select(value => value.PackageId).Distinct().Count(), Is.EqualTo(229));
        Assert.That(definitions.All(value => value.PackageId == value.PackageId.ToLowerInvariant()), Is.True);
        Assert.That(definitions.Select(value => value.InstallRelativePath)
            .Distinct(System.StringComparer.OrdinalIgnoreCase).Count(), Is.EqualTo(229));
        Assert.That(definitions.Count(value => value.PackageId.StartsWith("en.")), Is.EqualTo(218));
        Assert.That(definitions.Count(value => value.PackageId.StartsWith("pokemon.pokedex.artwork.")), Is.EqualTo(9));
        Assert.That(definitions.Single(value => value.PackageId == "pokemon.pokedex.taxonomy")
            .IncludedRelativePaths, Is.EqualTo(new[] { "pokemon-taxonomy.json" }));
    }
}
