using System;
using System.IO;
using Gacha.Pokemon.Infrastructure;
using NUnit.Framework;

public sealed class PokemonArtworkManifestTests
{
    [Test]
    public void Reader_IndexesPortableHashedArtworkAndMissingForms()
    {
        string root = Path.Combine(Path.GetTempPath(), "pokemon-artwork-manifest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "manifest.json");
        try
        {
            File.WriteAllText(path,
                "{\"SchemaVersion\":1,\"GenerationId\":\"generation-1\"," +
                "\"GeneratedAtUtc\":\"2026-07-30T00:00:00Z\"," +
                "\"TaxonomySourceSha256\":\"" + new string('a', 64) + "\"," +
                "\"Entries\":[{\"FormId\":\"pokemon-form:1\",\"RelativePath\":\"images/pokemon-form-1.png\"," +
                "\"Sha256\":\"" + new string('b', 64) + "\",\"Bytes\":123," +
                "\"SourceUrl\":\"https://raw.githubusercontent.com/example/1.png\"}]," +
                "\"MissingFormIds\":[\"pokemon-form:2\"]}");

            PokemonArtworkCatalog catalog = new PokemonArtworkManifestReader().LoadFile(path);

            Assert.That(catalog.Entries.Count, Is.EqualTo(1));
            Assert.That(catalog.Find("pokemon-form:1").RelativePath, Is.EqualTo("images/pokemon-form-1.png"));
            Assert.That(catalog.MissingFormIds, Is.EqualTo(new[] { "pokemon-form:2" }));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Test]
    public void Reader_RejectsTraversalPath()
    {
        Assert.Throws<ArgumentException>(() => new PokemonArtworkEntry(
            "form:1", "../escape.png", new string('a', 64), 1, "https://example.test/1.png"));
    }
}
