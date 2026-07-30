using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Gacha.EditorTools.Content;
using NUnit.Framework;

public class SitesContentApiObjectStoreTests
{
    private sealed class RecordedRequest
    {
        public HttpMethod Method;
        public Uri Uri;
        public string Authorization;
        public byte[] Body;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, byte[], HttpResponseMessage> responder;

        public RecordingHandler(Func<HttpRequestMessage, byte[], HttpResponseMessage> responder)
        {
            this.responder = responder;
        }

        public List<RecordedRequest> Requests { get; } = new List<RecordedRequest>();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            byte[] body = request.Content == null
                ? new byte[0]
                : await request.Content.ReadAsByteArrayAsync();
            Requests.Add(new RecordedRequest
            {
                Method = request.Method,
                Uri = request.RequestUri,
                Authorization = request.Headers.Authorization?.ToString(),
                Body = body
            });
            return responder(request, body);
        }
    }

    private string root;

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), "gacha-sites-publisher-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }

    [Test]
    public async Task Adapter_UsesBearerOnlyForProtectedRoutesAndVerifiesPublicBytes()
    {
        byte[] archive = { 1, 2, 3, 4, 5 };
        string sha256 = Sha256(archive);
        string objectKey = "packages/en.A1/" + sha256 + ".zip";
        string archivePath = Path.Combine(root, sha256 + ".zip");
        File.WriteAllBytes(archivePath, archive);
        var handler = new RecordingHandler((request, body) =>
        {
            if (request.Method == HttpMethod.Head)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(new byte[archive.Length])
                };
                response.Headers.Add("X-Content-Sha256", sha256);
                return response;
            }
            if (request.Method == HttpMethod.Post)
            {
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent("{\"ok\":true}")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(archive)
            };
        });
        var credentials = new SitesContentApiCredentials(
            new Uri("https://cards.chatgpt.site"),
            new string('A', 43));

        using (var store = new SitesContentApiObjectStore(credentials, handler, TimeSpan.FromSeconds(5)))
        {
            R2RemoteObjectState inspected = await store.InspectAsync(objectKey, CancellationToken.None);
            Assert.That(inspected.Exists, Is.True);
            Assert.That(inspected.Bytes, Is.EqualTo(archive.Length));
            Assert.That(inspected.Sha256, Is.EqualTo(sha256));

            await store.UploadFileAsync(
                objectKey,
                archivePath,
                sha256,
                "application/zip",
                "public, max-age=31536000, immutable",
                CancellationToken.None);

            R2RemoteObjectState publicState = await store.VerifyPublicAsync(
                new Uri("https://cards.chatgpt.site/api/content/packages/en.A1/" + sha256 + ".zip"),
                archive.Length,
                sha256,
                CancellationToken.None);
            Assert.That(publicState.Bytes, Is.EqualTo(archive.Length));
            Assert.That(publicState.Sha256, Is.EqualTo(sha256));
        }

        Assert.That(handler.Requests.Count, Is.EqualTo(3));
        Assert.That(handler.Requests[0].Uri.AbsolutePath, Is.EqualTo("/api/admin/content/packages"));
        Assert.That(handler.Requests[0].Authorization, Is.EqualTo("Bearer " + new string('A', 43)));
        Assert.That(handler.Requests[1].Body, Is.EqualTo(archive));
        Assert.That(handler.Requests[2].Uri.AbsolutePath, Does.StartWith("/api/content/packages/"));
        Assert.That(handler.Requests[2].Authorization, Is.Null);
    }

    [Test]
    public async Task Adapter_UploadsCatalogLastThroughDedicatedEndpoint()
    {
        byte[] catalog = System.Text.Encoding.UTF8.GetBytes("{\"schemaVersion\":1}");
        var handler = new RecordingHandler((request, body) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"revision\":1}")
        });
        var credentials = new SitesContentApiCredentials(
            new Uri("https://cards.chatgpt.site"),
            new string('B', 43));

        using (var store = new SitesContentApiObjectStore(credentials, handler, TimeSpan.FromSeconds(5)))
        {
            await store.UploadBytesAsync(
                "catalog.json",
                catalog,
                Sha256(catalog),
                "application/json; charset=utf-8",
                "no-cache, no-store",
                CancellationToken.None);
        }

        Assert.That(handler.Requests.Count, Is.EqualTo(1));
        Assert.That(handler.Requests[0].Method, Is.EqualTo(HttpMethod.Post));
        Assert.That(handler.Requests[0].Uri.AbsolutePath, Is.EqualTo("/api/admin/content/catalog"));
        Assert.That(handler.Requests[0].Body, Is.EqualTo(catalog));
    }

    [Test]
    public void Credentials_RejectTokenExfiltrationDestinationsAndMalformedTokens()
    {
        Assert.Throws<ArgumentException>(() => new SitesContentApiCredentials(
            new Uri("https://attacker.example"),
            new string('A', 43)));
        Assert.Throws<ArgumentException>(() => new SitesContentApiCredentials(
            new Uri("https://cards.chatgpt.site/redirect"),
            new string('A', 43)));
        Assert.Throws<ArgumentException>(() => new SitesContentApiCredentials(
            new Uri("https://cards.chatgpt.site"),
            "short"));
        Assert.DoesNotThrow(() => new SitesContentApiCredentials(
            new Uri("http://127.0.0.1:3000"),
            new string('A', 43)));
    }

    [Test]
    public void CredentialStore_GeneratesIgnoredLocalSecretAndStableBindingHash()
    {
        string path = Path.Combine(root, "site-publisher-credential.json");
        SitesPublisherCredential generated = SitesPublisherCredentialStore.GenerateAndSave(
            path,
            new Uri("https://cards.chatgpt.site"));
        SitesPublisherCredential loaded = SitesPublisherCredentialStore.Load(path);

        Assert.That(generated.PublisherToken, Has.Length.EqualTo(43));
        Assert.That(loaded.PublisherToken, Is.EqualTo(generated.PublisherToken));
        Assert.That(loaded.TokenSha256, Is.EqualTo(generated.TokenSha256));
        Assert.That(loaded.TokenSha256, Does.Match("^[a-f0-9]{64}$"));
        Assert.That(File.ReadAllText(path), Does.Contain(generated.PublisherToken));
        Assert.That(File.ReadAllText(path), Does.Not.Contain(generated.TokenSha256));
    }

    private static string Sha256(byte[] bytes)
    {
        using (SHA256 sha = SHA256.Create())
            return R2ReleasePublisher.ToHex(sha.ComputeHash(bytes));
    }
}
