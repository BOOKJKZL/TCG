using System;
using System.Linq;
using System.Reflection;
using Gacha.Application;
using Gacha.Infrastructure.Content;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

public class RemoteContentBootstrapTests
{
    private const string EnvironmentKey = "GACHA_CONTENT_CATALOG_URL";
    private const string BundledConfigurationResource = "Data/RemoteContent";
    private const string PublicCatalogUrl =
        "https://universal-gacha-content.jiejingleek.chatgpt.site/api/content/catalog.json";

    [Test]
    public void BundledConfiguration_ProvidesCredentialFreePublicCatalogDefault()
    {
        TextAsset asset = Resources.Load<TextAsset>(BundledConfigurationResource);

        Assert.That(asset, Is.Not.Null, "The production APK needs a public catalog default.");
        JObject configuration = JObject.Parse(asset.text);
        Assert.That(
            configuration.Properties().Select(property => property.Name),
            Is.EquivalentTo(new[] { "catalogUrl", "timeoutSeconds", "maxCatalogBytes" }));
        Assert.That(configuration.Value<string>("catalogUrl"), Is.EqualTo(PublicCatalogUrl));
        Assert.That(configuration.Value<int>("timeoutSeconds"), Is.EqualTo(15));
        Assert.That(configuration.Value<int>("maxCatalogBytes"), Is.EqualTo(1024 * 1024));

        var catalogUri = new Uri(configuration.Value<string>("catalogUrl"), UriKind.Absolute);
        Assert.That(catalogUri.Scheme, Is.EqualTo(Uri.UriSchemeHttps));
        Assert.That(catalogUri.UserInfo, Is.Empty);
        Assert.That(catalogUri.Fragment, Is.Empty);
        Assert.That(
            configuration.Properties().Any(property =>
                property.Name.IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0 ||
                property.Name.IndexOf("secret", StringComparison.OrdinalIgnoreCase) >= 0 ||
                property.Name.IndexOf("credential", StringComparison.OrdinalIgnoreCase) >= 0 ||
                property.Name.IndexOf("key", StringComparison.OrdinalIgnoreCase) >= 0),
            Is.False,
            "The APK may contain only the anonymous read URL and bounded client settings.");
    }

    [Test]
    public void EnvironmentCatalogUrl_CreatesRemoteProviderWithoutRepositorySecrets()
    {
        string previous = Environment.GetEnvironmentVariable(EnvironmentKey);
        Environment.SetEnvironmentVariable(
            EnvironmentKey,
            "https://content.example.test/private/catalog.json");
        IDisposable provider = null;
        try
        {
            MethodInfo create = typeof(GameApplicationBootstrap).GetMethod(
                "CreateRemoteContentCatalogProvider",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(create, Is.Not.Null);

            provider = create.Invoke(null, null) as IDisposable;

            Assert.That(provider, Is.TypeOf<CachedContentPackageCatalogProvider>());
        }
        finally
        {
            provider?.Dispose();
            Environment.SetEnvironmentVariable(EnvironmentKey, previous);
            ApplicationServices.Reset();
        }
    }
}
