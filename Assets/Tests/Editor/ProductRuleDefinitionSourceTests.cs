using Gacha.Application;
using Gacha.Infrastructure.Rules;
using NUnit.Framework;

public class ProductRuleDefinitionSourceTests
{
    [Test]
    public void JsonSource_ReadsStringEnumsAndVersionedRevision()
    {
        const string json = @"{
          'SchemaVersion': 1,
          'Revision': 'fixture-v1',
          'Rules': [{
            'ProfileId': 'profile-v1',
            'SetId': 'game:set:one',
            'LanguageId': 'en',
            'Trust': 'HistoricallyVerified',
            'Confidence': 'Authoritative'
          }]
        }";

        ProductRuleCatalogDefinition document =
            new JsonProductRuleDefinitionSource(json).Load();

        Assert.That(document.SchemaVersion, Is.EqualTo(1));
        Assert.That(document.Revision, Is.EqualTo("fixture-v1"));
        Assert.That(document.Rules[0].Trust, Is.EqualTo(ProductRuleTrust.HistoricallyVerified));
        Assert.That(document.Rules[0].Confidence, Is.EqualTo(ProductRuleConfidence.Authoritative));
    }

    [Test]
    public void Provider_RejectsUnsupportedSchemaBeforeGameplay()
    {
        var source = new StaticSource(new ProductRuleCatalogDefinition
        {
            SchemaVersion = 99,
            Revision = "future",
            Rules = new System.Collections.Generic.List<ProductRuleDefinition>
            {
                new ProductRuleDefinition { SetId = "set", LanguageId = "en" }
            }
        });

        Assert.Throws<System.InvalidOperationException>(() =>
            new DataDrivenProductRuleProvider(source));
    }

    private sealed class StaticSource : IProductRuleDefinitionSource
    {
        private readonly ProductRuleCatalogDefinition document;

        public StaticSource(ProductRuleCatalogDefinition document) => this.document = document;
        public ProductRuleCatalogDefinition Load() => document;
    }
}
