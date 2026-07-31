using System;
using Gacha.Application;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using UnityEngine;

namespace Gacha.Infrastructure.Rules
{
    public sealed class JsonProductRuleDefinitionSource : IProductRuleDefinitionSource
    {
        private readonly string json;

        public JsonProductRuleDefinitionSource(string json)
        {
            this.json = string.IsNullOrWhiteSpace(json)
                ? throw new ArgumentException("Product rule JSON is required.", nameof(json))
                : json;
        }

        public ProductRuleCatalogDefinition Load()
        {
            var settings = new JsonSerializerSettings();
            settings.Converters.Add(new StringEnumConverter());
            return JsonConvert.DeserializeObject<ProductRuleCatalogDefinition>(json, settings) ??
                   throw new InvalidOperationException("Product rule JSON has no root object.");
        }
    }

    internal static class PokemonRuleDefinitionLoader
    {
        private const string ResourcePath = "Gacha/Rules/pokemon-product-rules-v1";

        public static DataDrivenProductRuleProvider CreateProvider()
        {
            TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
            {
                throw new InvalidOperationException(
                    $"Bundled product rule data '{ResourcePath}' is missing.");
            }
            return new DataDrivenProductRuleProvider(new JsonProductRuleDefinitionSource(asset.text));
        }
    }
}
