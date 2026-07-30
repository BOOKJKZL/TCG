using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Gacha.EditorTools.Content;
using NUnit.Framework;

public sealed class PokemonCardSubjectPackagePublisherTests
{
    private string root;

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), "pokemon-links-package-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "source"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }

    [Test]
    public void Publish_CreatesOneDeterministicRuntimeOnlyArchive()
    {
        string snapshot = Path.Combine(root, "source", "pokemon-card-subject-links.en.json");
        File.WriteAllText(snapshot, "{\"schemaVersion\":1,\"links\":[]}");
        string output = Path.Combine(root, "release");

        ContentPackagePublishResult first = PokemonCardSubjectPackagePublisher.Publish(snapshot, output);
        ContentPackagePublishResult second = PokemonCardSubjectPackagePublisher.Publish(snapshot, output);

        Assert.That(first.Packages.Single().Package.PackageId,
            Is.EqualTo(PokemonCardSubjectPackagePublisher.DefaultPackageId));
        Assert.That(second.Packages.Single().Package.Sha256,
            Is.EqualTo(first.Packages.Single().Package.Sha256));
        Assert.That(second.CatalogJson, Is.EqualTo(first.CatalogJson));
        using ZipArchive archive = ZipFile.OpenRead(first.Packages.Single().ArchivePath);
        Assert.That(archive.Entries.Select(entry => entry.FullName),
            Is.EqualTo(new[] { "pokemon-card-subject-links.en.json" }));
    }

    [TestCase("en", "pokemon.card-subject-links.en", "pokedex/links/en")]
    [TestCase("ja", "pokemon.card-subject-links.ja", "pokedex/links/ja")]
    [TestCase("zh-cn", "pokemon.card-subject-links.zh-cn", "pokedex/links/zh-cn")]
    public void LanguageIdentity_IsIndependentPerInstalledCardLanguage(
        string language, string packageId, string installPath)
    {
        Assert.That(PokemonCardSubjectPackagePublisher.PackageId(language), Is.EqualTo(packageId));
        Assert.That(PokemonCardSubjectPackagePublisher.InstallRelativePath(language), Is.EqualTo(installPath));
    }
}
