using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Gacha.Application;
using Gacha.Infrastructure.Content;
using Gacha.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Gacha.Tests.PlayMode
{
    public class RemoteContentCatalogPlayModeTests
    {
        private const string EnvironmentKey = "GACHA_CONTENT_CATALOG_URL";
        private const string CacheEnvironmentKey = "GACHA_CONTENT_CATALOG_CACHE_PATH";
        private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        private sealed class LoopbackCatalogServer : IDisposable
        {
            private readonly TcpListener listener;
            private readonly byte[] response;

            public LoopbackCatalogServer(string json)
            {
                listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                CatalogUrl = "http://127.0.0.1:" + port + "/catalog.json";
                byte[] body = Encoding.UTF8.GetBytes(json);
                string headers =
                    "HTTP/1.1 200 OK\r\n" +
                    "Content-Type: application/json; charset=utf-8\r\n" +
                    "Content-Length: " + body.Length + "\r\n" +
                    "Connection: close\r\n\r\n";
                byte[] prefix = Encoding.ASCII.GetBytes(headers);
                response = new byte[prefix.Length + body.Length];
                Buffer.BlockCopy(prefix, 0, response, 0, prefix.Length);
                Buffer.BlockCopy(body, 0, response, prefix.Length, body.Length);
                Completion = ServeOnceAsync();
            }

            public string CatalogUrl { get; }
            public Task Completion { get; }

            public void Dispose()
            {
                listener.Stop();
            }

            private async Task ServeOnceAsync()
            {
                using (TcpClient client = await listener.AcceptTcpClientAsync())
                using (NetworkStream stream = client.GetStream())
                {
                    var buffer = new byte[1024];
                    var request = new StringBuilder();
                    while (request.ToString().IndexOf("\r\n\r\n", StringComparison.Ordinal) < 0)
                    {
                        int read = await stream.ReadAsync(buffer, 0, buffer.Length);
                        if (read <= 0)
                            break;
                        request.Append(Encoding.ASCII.GetString(buffer, 0, read));
                        if (request.Length > 16 * 1024)
                            throw new InvalidOperationException("Loopback fixture request headers were too large.");
                    }
                    if (!request.ToString().StartsWith("GET /catalog.json HTTP/1.1", StringComparison.Ordinal))
                        throw new InvalidOperationException("Loopback fixture received an unexpected request.");
                    await stream.WriteAsync(response, 0, response.Length);
                }
            }
        }

        [UnityTest]
        public IEnumerator PrivateEnvironmentConfig_LoadsCatalogIntoContentScene()
        {
            string previous = Environment.GetEnvironmentVariable(EnvironmentKey);
            string previousCache = Environment.GetEnvironmentVariable(CacheEnvironmentKey);
            string cacheRoot = Path.Combine(
                Path.GetTempPath(),
                "gacha-remote-playmode-" + Guid.NewGuid().ToString("N"));
            string cachePath = Path.Combine(cacheRoot, "catalog-cache-v1.json");
            var server = new LoopbackCatalogServer(CatalogJson());
            try
            {
                ContentManagementController.CatalogProviderOverride = null;
                ContentManagementController.OperationFactoryOverride = null;
                ContentManagementController.DispatcherOverride = null;
                ApplicationServices.Reset();
                Environment.SetEnvironmentVariable(EnvironmentKey, server.CatalogUrl);
                Environment.SetEnvironmentVariable(CacheEnvironmentKey, cachePath);
                LogAssert.Expect(LogType.Log,
                    "Remote content catalog and its verified offline cache are configured from runtime settings.");
                InvokeBootstrap();

                Assert.That(ApplicationServices.ContentPackageCatalogs,
                    Is.TypeOf<CachedContentPackageCatalogProvider>());
                AsyncOperation load = SceneManager.LoadSceneAsync("006_ContentScene", LoadSceneMode.Single);
                yield return load;
                yield return null;

                ContentManagementController controller = UnityEngine.Object.FindFirstObjectByType<ContentManagementController>();
                Assert.That(controller, Is.Not.Null);
                float deadline = Time.realtimeSinceStartup + 5f;
                while (!controller.IsReady && Time.realtimeSinceStartup < deadline)
                    yield return null;

                Assert.That(controller.IsReady, Is.True, controller.InitializationError);
                Assert.That(controller.PackageCount, Is.EqualTo(1));
                Assert.That(controller.GetPackageState("en.remote"), Is.EqualTo(ContentPackageOperationState.Idle));
                while (!server.Completion.IsCompleted && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That(server.Completion.IsCompletedSuccessfully, Is.True,
                    server.Completion.Exception?.ToString());
                Assert.That(File.Exists(cachePath), Is.True);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                server.Dispose();
                ApplicationServices.Reset();
                Environment.SetEnvironmentVariable(EnvironmentKey, previous);
                Environment.SetEnvironmentVariable(CacheEnvironmentKey, previousCache);
                InvokeBootstrap();
                ContentManagementController.CatalogProviderOverride = null;
                ContentManagementController.OperationFactoryOverride = null;
                ContentManagementController.DispatcherOverride = null;
                if (Directory.Exists(cacheRoot))
                    Directory.Delete(cacheRoot, true);
            }
        }

        private static void InvokeBootstrap()
        {
            Type bootstrap = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("GameApplicationBootstrap"))
                .First(type => type != null);
            MethodInfo ensure = bootstrap.GetMethod("EnsureConfigured", BindingFlags.Public | BindingFlags.Static);
            ensure.Invoke(null, null);
        }

        private static string CatalogJson()
        {
            return "{\"schemaVersion\":1,\"revision\":1,\"packages\":[{" +
                   "\"packageId\":\"en.remote\",\"installRelativePath\":\"en/remote\"," +
                   "\"revision\":1,\"version\":\"1.0.0\",\"downloadBytes\":100," +
                   "\"installedBytes\":200,\"sha256\":\"" + Hash + "\"," +
                   "\"archiveUrl\":\"packages/" + Hash + ".zip\"}]}";
        }
    }
}
