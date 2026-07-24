using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Gacha.Application;
using Gacha.Infrastructure.Content;
using NUnit.Framework;

public class HttpContentPackageCatalogProviderTests
{
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private sealed class FixtureHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory;

        public FixtureHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory)
        {
            this.responseFactory = responseFactory;
        }

        public int Calls { get; private set; }
        public readonly List<string> Accept = new List<string>();
        public readonly List<string> Encodings = new List<string>();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            Accept.Add(string.Join(",", request.Headers.Accept));
            Encodings.Add(string.Join(",", request.Headers.AcceptEncoding));
            return responseFactory(request, cancellationToken);
        }
    }

    private sealed class UnknownLengthContent : HttpContent
    {
        private readonly byte[] bytes;

        public UnknownLengthContent(byte[] bytes)
        {
            this.bytes = bytes;
            Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext context)
        {
            return stream.WriteAsync(bytes, 0, bytes.Length);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private readonly List<IDisposable> disposables = new List<IDisposable>();

    [TearDown]
    public void TearDown()
    {
        for (int index = disposables.Count - 1; index >= 0; index--)
            disposables[index].Dispose();
        disposables.Clear();
    }

    [Test]
    public async Task Load_RequestsJsonAndParsesCatalog()
    {
        FixtureHandler handler = Handler((request, _) => Completed(JsonResponse(CatalogJson(), request.RequestUri)));
        HttpContentPackageCatalogProvider provider = Provider(handler);

        ContentPackageCatalogLoadResult result = await provider.LoadAsync(CancellationToken.None);

        Assert.That(result.Succeeded, Is.True, result.ErrorMessage);
        Assert.That(result.Catalog.Packages.Count, Is.EqualTo(1));
        Assert.That(handler.Accept, Is.EqualTo(new[] { "application/json" }));
        Assert.That(handler.Encodings, Is.EqualTo(new[] { "identity" }));
    }

    [Test]
    public async Task Load_UsesFinalResponseUriForRelativeArchives()
    {
        var redirected = new Uri("https://cdn.example.test/releases/v9/catalog.json");
        FixtureHandler handler = Handler((_, __) => Completed(JsonResponse(CatalogJson("packages/" + Hash + ".zip"), redirected)));
        HttpContentPackageCatalogProvider provider = Provider(handler);

        ContentPackageCatalogLoadResult result = await provider.LoadAsync(CancellationToken.None);

        Assert.That(result.Succeeded, Is.True, result.ErrorMessage);
        Assert.That(result.Catalog.Packages[0].ArchiveUri.AbsoluteUri, Is.EqualTo(
            "https://cdn.example.test/releases/v9/packages/" + Hash + ".zip"));
    }

    [Test]
    public void Constructor_RejectsPublicHttpAndEmbeddedCredentials()
    {
        Assert.Throws<ArgumentException>(() => new HttpContentPackageCatalogProvider(
            new Uri("http://content.example.test/catalog.json")));
        Assert.Throws<ArgumentException>(() => new HttpContentPackageCatalogProvider(
            new Uri("https://user:secret@content.example.test/catalog.json")));
    }

    [Test]
    public async Task Load_AllowsLoopbackHttpFixture()
    {
        FixtureHandler handler = Handler((request, _) => Completed(JsonResponse(CatalogJson(), request.RequestUri)));
        HttpContentPackageCatalogProvider provider = Provider(
            handler,
            "http://127.0.0.1:18473/catalog.json");

        ContentPackageCatalogLoadResult result = await provider.LoadAsync(CancellationToken.None);

        Assert.That(result.Succeeded, Is.True, result.ErrorMessage);
        Assert.That(handler.Calls, Is.EqualTo(1));
    }

    [Test]
    public async Task Load_NonSuccessStatusReturnsStructuredFailure()
    {
        FixtureHandler handler = Handler((request, _) => Completed(new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, request.RequestUri),
            Content = new ByteArrayContent(Array.Empty<byte>())
        }));
        HttpContentPackageCatalogProvider provider = Provider(handler);

        ContentPackageCatalogLoadResult result = await provider.LoadAsync(CancellationToken.None);

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("403"));
    }

    [Test]
    public async Task Load_RejectsDeclaredOversizeBeforeReadingBody()
    {
        FixtureHandler handler = Handler((request, _) =>
        {
            HttpResponseMessage response = JsonResponse(CatalogJson(), request.RequestUri);
            response.Content.Headers.ContentLength = 2049;
            return Completed(response);
        });
        HttpContentPackageCatalogProvider provider = Provider(handler, maximumBytes: 1024);

        ContentPackageCatalogLoadResult result = await provider.LoadAsync(CancellationToken.None);

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("limit is 1024"));
    }

    [Test]
    public async Task Load_RejectsStreamingBodyThatExceedsLimit()
    {
        byte[] oversized = Encoding.UTF8.GetBytes(new string(' ', 1025));
        FixtureHandler handler = Handler((request, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, request.RequestUri),
                Content = new UnknownLengthContent(oversized)
            };
            return Completed(response);
        });
        HttpContentPackageCatalogProvider provider = Provider(handler, maximumBytes: 1024);

        ContentPackageCatalogLoadResult result = await provider.LoadAsync(CancellationToken.None);

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("exceeds"));
    }

    [Test]
    public async Task Load_RejectsNonJsonAndEncodedResponses()
    {
        FixtureHandler handler = Handler((request, _) =>
        {
            HttpResponseMessage response = JsonResponse(CatalogJson(), request.RequestUri);
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/html");
            response.Content.Headers.ContentEncoding.Add("gzip");
            return Completed(response);
        });
        HttpContentPackageCatalogProvider provider = Provider(handler);

        ContentPackageCatalogLoadResult result = await provider.LoadAsync(CancellationToken.None);

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("not JSON"));
    }

    [Test]
    public void Load_ExternalCancellationPropagates()
    {
        FixtureHandler handler = Handler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return null;
        });
        HttpContentPackageCatalogProvider provider = Provider(handler);
        var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.CatchAsync<OperationCanceledException>(async () =>
            await provider.LoadAsync(cancellation.Token));
    }

    [Test]
    public async Task Load_InternalTimeoutReturnsStructuredFailure()
    {
        FixtureHandler handler = Handler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return null;
        });
        HttpContentPackageCatalogProvider provider = Provider(handler, timeout: TimeSpan.FromSeconds(1));

        ContentPackageCatalogLoadResult result = await provider.LoadAsync(CancellationToken.None);

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("timed out after 1 seconds"));
    }

    [Test]
    public async Task Load_RejectsRedirectToPublicPlainHttp()
    {
        FixtureHandler handler = Handler((_, __) => Completed(JsonResponse(
            CatalogJson(),
            new Uri("http://content.example.test/catalog.json"))));
        HttpContentPackageCatalogProvider provider = Provider(handler);

        ContentPackageCatalogLoadResult result = await provider.LoadAsync(CancellationToken.None);

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("HTTPS"));
    }

    private FixtureHandler Handler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory)
    {
        return new FixtureHandler(responseFactory);
    }

    private HttpContentPackageCatalogProvider Provider(
        FixtureHandler handler,
        string uri = "https://content.example.test/catalog.json",
        int maximumBytes = HttpContentPackageCatalogProvider.DefaultMaximumCatalogBytes,
        TimeSpan? timeout = null)
    {
        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var provider = new HttpContentPackageCatalogProvider(
            new Uri(uri),
            client,
            maximumBytes,
            timeout);
        disposables.Add(provider);
        disposables.Add(client);
        return provider;
    }

    private static HttpResponseMessage JsonResponse(string json, Uri finalUri)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, finalUri),
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        return response;
    }

    private static Task<HttpResponseMessage> Completed(HttpResponseMessage response)
    {
        return Task.FromResult(response);
    }

    private static string CatalogJson(string archiveUrl = null)
    {
        return "{\"schemaVersion\":1,\"revision\":9,\"packages\":[{" +
               "\"packageId\":\"en.base1\",\"installRelativePath\":\"en/base1\"," +
               "\"revision\":1,\"version\":\"1.0.0\",\"downloadBytes\":100," +
               "\"installedBytes\":200,\"sha256\":\"" + Hash + "\"," +
               "\"archiveUrl\":\"" + (archiveUrl ?? "https://cdn.example.test/" + Hash + ".zip") + "\"}]}";
    }
}
