using System;
using System.Reflection;
using Gacha.Application;
using Gacha.Infrastructure.Content;
using NUnit.Framework;

public class RemoteContentBootstrapTests
{
    private const string EnvironmentKey = "GACHA_CONTENT_CATALOG_URL";

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
