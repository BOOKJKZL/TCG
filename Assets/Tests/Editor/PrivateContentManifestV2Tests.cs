using System;
using System.IO;
using System.Linq;
using Gacha.Domain;
using Gacha.Infrastructure.Content;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

public class PrivateContentManifestV2Tests
{
    private string temporaryDirectory;

    [SetUp]
    public void SetUp()
    {
        temporaryDirectory = Path.Combine(
            Path.GetTempPath(), "gacha-manifest-v2-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(temporaryDirectory))
            Directory.Delete(temporaryDirectory, true);
    }

    [Test]
    public void Reader_MigratesV1InMemoryWithoutChangingPrintingIdentityOrSourceFile()
    {
        string originalJson = V1ManifestJson();
        string path = WriteManifest(originalJson);
        var reader = new PrivateContentManifestReader();

        PrivateContentManifestDocument document = reader.LoadFile(path);
        PrivateCatalogImportResult result = new PrivateManifestCatalogAdapter().Build(
            new[] { document }, "sample-game", "Sample Game");
        PrintingDefinition printing = result.Catalog.Printings.Values.Single();

        Assert.That(document.SourceSchemaVersion, Is.EqualTo(1));
        Assert.That(document.WasMigrated, Is.True);
        Assert.That(document.Manifest.SchemaVersion, Is.EqualTo(2));
        Assert.That(document.Manifest.Set.SetCode, Is.EqualTo("sample1"));
        Assert.That(document.Manifest.Set.EraId, Is.EqualTo("sample"));
        Assert.That(document.Manifest.Set.GenerationId, Is.EqualTo("unmapped"));
        Assert.That(printing.Id, Is.EqualTo("sample-game:printing:sample1:1:en:normal"));
        Assert.That(printing.Identity.SetId, Is.EqualTo("sample-game:set:sample1"));
        Assert.That(printing.Identity.CardNumber, Is.EqualTo("1"));
        Assert.That(File.ReadAllText(path), Is.EqualTo(originalJson));
    }

    [Test]
    public void ReaderAndAdapter_PreserveV2SetOrderingMetadata()
    {
        string path = WriteManifest(V2ManifestJson());

        PrivateContentManifestDocument document = new PrivateContentManifestReader().LoadFile(path);
        PrivateCatalogImportResult result = new PrivateManifestCatalogAdapter().Build(
            new[] { document }, "sample-game", "Sample Game");
        SetDefinition set = result.Catalog.Sets.Values.Single();

        Assert.That(document.SourceSchemaVersion, Is.EqualTo(2));
        Assert.That(document.WasMigrated, Is.False);
        Assert.That(set.Ordering.SetCode, Is.EqualTo("S2"));
        Assert.That(set.Ordering.EraId, Is.EqualTo("sample-era"));
        Assert.That(set.Ordering.GenerationId, Is.EqualTo("generation-2"));
        Assert.That(set.Ordering.GenerationOrder, Is.EqualTo(2));
        Assert.That(set.Ordering.SetOrdinal, Is.EqualTo(7));
    }

    [Test]
    public void Reader_RejectsV2WithoutStableOrderingState()
    {
        JObject manifest = JObject.Parse(V2ManifestJson());
        manifest["Set"]["GenerationId"] = "";
        string path = WriteManifest(manifest.ToString());

        PrivateContentManifestException exception = Assert.Throws<PrivateContentManifestException>(() =>
            new PrivateContentManifestReader().LoadFile(path));

        Assert.That(exception.Message, Does.Contain("GenerationId"));
    }

    [Test]
    public void TcgdexMapper_EmitsV2StableDefaultsWithoutGuessingGeneration()
    {
        JObject source = JObject.Parse(@"{
          'id':'base1',
          'name':'Base Set',
          'releaseDate':'1999-01-09',
          'tcgOnline':'BS',
          'serie':{'id':'base','name':'Base'},
          'cardCount':{'official':102,'total':102}
        }");

        ImportedSetRecord mapped = TcgdexImportService.MapSet(source, "https://example.test/base1");

        Assert.That(new PrivateContentManifest().SchemaVersion, Is.EqualTo(2));
        Assert.That(mapped.SetCode, Is.EqualTo("BS"));
        Assert.That(mapped.EraId, Is.EqualTo("base"));
        Assert.That(mapped.GenerationId, Is.EqualTo("unmapped"));
        Assert.That(mapped.GenerationOrder, Is.Null);
        Assert.That(mapped.SetOrdinal, Is.Null);
    }

    private string WriteManifest(string json)
    {
        string path = Path.Combine(temporaryDirectory, "manifest.json");
        File.WriteAllText(path, json);
        return path;
    }

    private static string V1ManifestJson()
    {
        return @"{
          'SchemaVersion': 1,
          'Source': 'test',
          'Language': 'en',
          'Set': {
            'Id': 'sample1',
            'Name': 'Sample Set',
            'SeriesId': 'sample',
            'ReleaseDate': '2000-01-01'
          },
          'Cards': [{
            'Id': 'sample1-1',
            'LocalId': '1',
            'Name': 'Sample Card',
            'Category': 'Pokemon',
            'Rarity': 'Common'
          }]
        }";
    }

    private static string V2ManifestJson()
    {
        return @"{
          'SchemaVersion': 2,
          'Source': 'test',
          'Language': 'en',
          'Set': {
            'Id': 'sample2',
            'Name': 'Sample Set Two',
            'SetCode': 'S2',
            'SeriesId': 'sample',
            'EraId': 'sample-era',
            'GenerationId': 'generation-2',
            'GenerationOrder': 2,
            'SetOrdinal': 7,
            'ReleaseDate': '2001-01-01'
          },
          'Cards': []
        }";
    }
}
