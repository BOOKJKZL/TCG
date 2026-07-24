using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Gacha.Application;
using Gacha.Infrastructure.Content;
using NUnit.Framework;

public class HttpContentPackageByteSourceTests
{
    private sealed class FixedUriResolver : IContentPackageUriResolver
    {
        private readonly Uri uri;

        public FixedUriResolver(string uri)
        {
            this.uri = new Uri(uri, UriKind.Absolute);
        }

        public Uri Resolve(ContentPackageDescriptor package) => uri;
    }

    private sealed class FixtureHandler : HttpMessageHandler
    {
        private readonly Func<int, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory;

        public FixtureHandler(
            Func<int, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory)
        {
            this.responseFactory = responseFactory;
        }

        public int Calls { get; private set; }
        public readonly List<string> Ranges = new List<string>();
        public readonly List<string> Encodings = new List<string>();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            int call = Calls++;
            Ranges.Add(request.Headers.Range?.ToString());
            Encodings.Add(string.Join(",", request.Headers.AcceptEncoding));
            HttpResponseMessage response = await responseFactory(call, request, cancellationToken);
            if (response != null)
                response.RequestMessage = request;
            return response;
        }
    }

    private string temporaryRoot;
    private string downloadRoot;
    private readonly List<IDisposable> disposables = new List<IDisposable>();

    [SetUp]
    public void SetUp()
    {
        temporaryRoot = Path.Combine(Path.GetTempPath(), "gacha-http-" + Guid.NewGuid().ToString("N"));
        downloadRoot = Path.Combine(temporaryRoot, "downloads");
    }

    [TearDown]
    public void TearDown()
    {
        for (int index = disposables.Count - 1; index >= 0; index--)
            disposables[index].Dispose();
        disposables.Clear();

        if (Directory.Exists(temporaryRoot))
            Directory.Delete(temporaryRoot, true);
    }

    [Test]
    public async Task FreshDownload_UsesIdentityWithoutRangeAndPublishesArchive()
    {
        byte[] bytes = Data(100);
        var handler = Handler((_, __, ___) => Completed(Response(HttpStatusCode.OK, bytes)));
        ContentPackageDownloadTask task = CreateTask(handler, "https://content.example.test/en.base1.zip");

        ContentDownloadSnapshot result = await task.StartAsync();

        Assert.That(result.State, Is.EqualTo(ContentDownloadState.Completed));
        Assert.That(File.ReadAllBytes(result.ArchivePath), Is.EqualTo(bytes));
        Assert.That(handler.Ranges, Is.EqualTo(new string[] { null }));
        Assert.That(handler.Encodings, Is.EqualTo(new[] { "identity" }));
    }

    [Test]
    public async Task ExistingPartial_SendsExactRangeAndPreservesPrefix()
    {
        byte[] bytes = Data(100);
        WritePartial(bytes, 40);
        var handler = Handler((_, __, ___) => Completed(
            RangeResponse(Slice(bytes, 40, 60), 40, 99, 100)));
        ContentPackageDownloadTask task = CreateTask(handler, "https://content.example.test/en.base1.zip");

        ContentDownloadSnapshot result = await task.StartAsync();

        Assert.That(result.State, Is.EqualTo(ContentDownloadState.Completed));
        Assert.That(File.ReadAllBytes(result.ArchivePath), Is.EqualTo(bytes));
        Assert.That(handler.Ranges, Is.EqualTo(new[] { "bytes=40-" }));
    }

    [Test]
    public async Task Resume_WhenServerIgnoresRange_FailsWithoutAppending()
    {
        byte[] bytes = Data(100);
        WritePartial(bytes, 40);
        var handler = Handler((_, __, ___) => Completed(Response(HttpStatusCode.OK, bytes)));
        ContentPackageDownloadTask task = CreateTask(handler, "https://content.example.test/en.base1.zip");
        int failures = 0;
        task.FailureReported += _ => failures++;

        ContentDownloadSnapshot result = await task.StartAsync();

        Assert.That(result.State, Is.EqualTo(ContentDownloadState.Failed));
        Assert.That(result.ErrorMessage, Does.Contain("206 Partial Content"));
        Assert.That(new FileInfo(PartialPath).Length, Is.EqualTo(40));
        Assert.That(failures, Is.EqualTo(1));
    }

    [TestCase(39, 99, 100)]
    [TestCase(40, 98, 100)]
    [TestCase(40, 99, 101)]
    public async Task Resume_WhenContentRangeDoesNotMatch_FailsBeforeWriting(
        long from,
        long to,
        long total)
    {
        byte[] bytes = Data(100);
        WritePartial(bytes, 40);
        var handler = Handler((_, __, ___) => Completed(
            RangeResponse(Slice(bytes, 40, 60), from, to, total)));
        ContentPackageDownloadTask task = CreateTask(handler, "https://content.example.test/en.base1.zip");

        ContentDownloadSnapshot result = await task.StartAsync();

        Assert.That(result.State, Is.EqualTo(ContentDownloadState.Failed));
        Assert.That(result.ErrorMessage, Does.Contain("does not match"));
        Assert.That(new FileInfo(PartialPath).Length, Is.EqualTo(40));
    }

    [Test]
    public async Task TruncatedRange_RetryContinuesFromPersistedOffset()
    {
        byte[] bytes = Data(100);
        WritePartial(bytes, 40);
        var handler = Handler((call, _, __) =>
        {
            if (call == 0)
            {
                HttpResponseMessage truncated = RangeResponse(Slice(bytes, 40, 20), 40, 99, 100);
                truncated.Content.Headers.ContentLength = 60;
                return Completed(truncated);
            }
            return Completed(RangeResponse(Slice(bytes, 60, 40), 60, 99, 100));
        });
        ContentPackageDownloadTask task = CreateTask(handler, "https://content.example.test/en.base1.zip");

        ContentDownloadSnapshot failed = await task.StartAsync();

        Assert.That(failed.State, Is.EqualTo(ContentDownloadState.Failed));
        Assert.That(new FileInfo(PartialPath).Length, Is.EqualTo(60));

        ContentDownloadSnapshot completed = await task.RetryAsync();

        Assert.That(completed.State, Is.EqualTo(ContentDownloadState.Completed));
        Assert.That(File.ReadAllBytes(completed.ArchivePath), Is.EqualTo(bytes));
        Assert.That(handler.Ranges, Is.EqualTo(new[] { "bytes=40-", "bytes=60-" }));
    }

    [Test]
    public async Task PublicPlainHttp_IsRejectedBeforeRequest()
    {
        var handler = Handler((_, __, ___) => Completed(Response(HttpStatusCode.OK, Data(100))));
        ContentPackageDownloadTask task = CreateTask(handler, "http://content.example.test/en.base1.zip");

        ContentDownloadSnapshot result = await task.StartAsync();

        Assert.That(result.State, Is.EqualTo(ContentDownloadState.Failed));
        Assert.That(result.ErrorMessage, Does.Contain("HTTPS"));
        Assert.That(handler.Calls, Is.Zero);
    }

    [Test]
    public async Task LoopbackPlainHttp_IsAllowedForLocalFixtures()
    {
        byte[] bytes = Data(100);
        var handler = Handler((_, __, ___) => Completed(Response(HttpStatusCode.OK, bytes)));
        ContentPackageDownloadTask task = CreateTask(handler, "http://127.0.0.1:18473/en.base1.zip");

        ContentDownloadSnapshot result = await task.StartAsync();

        Assert.That(result.State, Is.EqualTo(ContentDownloadState.Completed));
        Assert.That(File.ReadAllBytes(result.ArchivePath), Is.EqualTo(bytes));
    }

    [Test]
    public async Task Pause_CancelsBlockedRequestWithoutReportingFailure()
    {
        var started = new TaskCompletionSource<bool>();
        var handler = Handler(async (_, __, cancellationToken) =>
        {
            started.TrySetResult(true);
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return Response(HttpStatusCode.OK, Data(100));
        });
        ContentPackageDownloadTask task = CreateTask(handler, "https://content.example.test/en.base1.zip");
        int failures = 0;
        task.FailureReported += _ => failures++;

        Task<ContentDownloadSnapshot> active = task.StartAsync();
        await started.Task;
        ContentDownloadSnapshot paused = await task.PauseAsync();

        Assert.That(await active, Is.SameAs(paused));
        Assert.That(paused.State, Is.EqualTo(ContentDownloadState.Paused));
        Assert.That(paused.DownloadedBytes, Is.Zero);
        Assert.That(failures, Is.Zero);
    }

    [Test]
    public async Task FreshResponseWithWrongLength_FailsBeforeWriting()
    {
        var handler = Handler((_, __, ___) =>
        {
            HttpResponseMessage response = Response(HttpStatusCode.OK, Data(80));
            response.Content.Headers.ContentLength = 80;
            return Completed(response);
        });
        ContentPackageDownloadTask task = CreateTask(handler, "https://content.example.test/en.base1.zip");

        ContentDownloadSnapshot result = await task.StartAsync();

        Assert.That(result.State, Is.EqualTo(ContentDownloadState.Failed));
        Assert.That(result.ErrorMessage, Does.Contain("expected 100"));
        Assert.That(File.Exists(PartialPath), Is.False);
    }

    [Test]
    public async Task EncodedResponse_IsRejectedBeforeWriting()
    {
        var handler = Handler((_, __, ___) =>
        {
            HttpResponseMessage response = Response(HttpStatusCode.OK, Data(100));
            response.Content.Headers.ContentEncoding.Add("gzip");
            return Completed(response);
        });
        ContentPackageDownloadTask task = CreateTask(handler, "https://content.example.test/en.base1.zip");

        ContentDownloadSnapshot result = await task.StartAsync();

        Assert.That(result.State, Is.EqualTo(ContentDownloadState.Failed));
        Assert.That(result.ErrorMessage, Does.Contain("not resumable"));
        Assert.That(File.Exists(PartialPath), Is.False);
    }

    private string PartialPath => Path.Combine(downloadRoot, "en.base1.part");

    private FixtureHandler Handler(
        Func<int, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory)
    {
        return new FixtureHandler(responseFactory);
    }

    private ContentPackageDownloadTask CreateTask(FixtureHandler handler, string uri)
    {
        var client = new HttpClient(handler);
        var source = new HttpContentPackageByteSource(new FixedUriResolver(uri), client);
        disposables.Add(source);
        disposables.Add(client);
        var transfer = new FileSystemContentPackageTransfer(downloadRoot, source);
        return new ContentPackageDownloadTask(Package(), transfer);
    }

    private void WritePartial(byte[] bytes, int count)
    {
        Directory.CreateDirectory(downloadRoot);
        File.WriteAllBytes(PartialPath, Slice(bytes, 0, count));
    }

    private static HttpResponseMessage Response(HttpStatusCode status, byte[] body)
    {
        return new HttpResponseMessage(status)
        {
            Content = new ByteArrayContent(body)
        };
    }

    private static HttpResponseMessage RangeResponse(
        byte[] body,
        long from,
        long to,
        long total)
    {
        HttpResponseMessage response = Response(HttpStatusCode.PartialContent, body);
        response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, total);
        return response;
    }

    private static Task<HttpResponseMessage> Completed(HttpResponseMessage response)
    {
        return System.Threading.Tasks.Task.FromResult(response);
    }

    private static ContentPackageDescriptor Package()
    {
        return new ContentPackageDescriptor(
            "en.base1",
            "en/base1",
            1,
            "1.0.0",
            100,
            200,
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
    }

    private static byte[] Data(int count)
    {
        var bytes = new byte[count];
        for (int index = 0; index < count; index++)
            bytes[index] = (byte)(index % 251);
        return bytes;
    }

    private static byte[] Slice(byte[] source, int offset, int count)
    {
        var result = new byte[count];
        Buffer.BlockCopy(source, offset, result, 0, count);
        return result;
    }
}
