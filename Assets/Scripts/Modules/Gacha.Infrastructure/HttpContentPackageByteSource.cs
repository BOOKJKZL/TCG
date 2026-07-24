using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Gacha.Application;

namespace Gacha.Infrastructure.Content
{
    /// <summary>
    /// Streams immutable package bytes over HTTPS. Resume responses are accepted
    /// only when the server proves the exact requested range and total object size.
    /// Plain HTTP is restricted to loopback fixtures.
    /// </summary>
    public sealed class HttpContentPackageByteSource : IContentPackageByteSource, IDisposable
    {
        private readonly IContentPackageUriResolver resolver;
        private readonly HttpClient client;
        private readonly bool ownsClient;
        private bool disposed;

        public HttpContentPackageByteSource(
            IContentPackageUriResolver resolver,
            HttpClient client = null)
        {
            this.resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            this.client = client ?? new HttpClient();
            ownsClient = client == null;
        }

        public async Task<Stream> OpenReadAsync(
            ContentPackageDescriptor package,
            long offset,
            CancellationToken cancellationToken)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(HttpContentPackageByteSource));
            if (package == null)
                throw new ArgumentNullException(nameof(package));
            if (offset < 0 || offset > package.DownloadBytes)
                throw new ArgumentOutOfRangeException(nameof(offset));

            Uri uri = resolver.Resolve(package);
            ValidateUri(uri);
            var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("identity"));
            if (offset > 0)
                request.Headers.Range = new RangeHeaderValue(offset, null);

            HttpResponseMessage response = null;
            try
            {
                response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                ValidateUri(response.RequestMessage?.RequestUri ?? uri);
                ValidateResponse(response, offset, package.DownloadBytes);
                Stream content = await response.Content.ReadAsStreamAsync();
                return new OwnedResponseStream(content, response, request);
            }
            catch
            {
                response?.Dispose();
                request.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            if (ownsClient)
                client.Dispose();
        }

        private static void ValidateUri(Uri uri)
        {
            if (uri == null || !uri.IsAbsoluteUri)
                throw new InvalidOperationException("Content package URI must be absolute.");
            bool isHttps = string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
            bool isLoopbackHttp = string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                                  uri.IsLoopback;
            if (!isHttps && !isLoopbackHttp)
                throw new InvalidOperationException("Content package URI must use HTTPS; HTTP is allowed only for loopback fixtures.");
            if (!string.IsNullOrEmpty(uri.UserInfo))
                throw new InvalidOperationException("Content package URI cannot contain embedded credentials.");
            if (!string.IsNullOrEmpty(uri.Fragment))
                throw new InvalidOperationException("Content package URI cannot contain a fragment.");
        }

        private static void ValidateResponse(
            HttpResponseMessage response,
            long offset,
            long totalBytes)
        {
            if (response == null)
                throw new IOException("HTTP content source returned no response.");
            if (response.Content == null)
                throw new IOException("HTTP content source returned no response body.");

            if (offset == 0)
            {
                if (response.StatusCode != HttpStatusCode.OK)
                    throw ResponseError(response, "A fresh package download requires HTTP 200 OK.");
                ValidateFreshHeaders(response.Content.Headers, totalBytes);
            }
            else
            {
                if (response.StatusCode != HttpStatusCode.PartialContent)
                    throw ResponseError(response, "A resumed package download requires HTTP 206 Partial Content.");
                ValidateRangeHeaders(response.Content.Headers, offset, totalBytes);
            }

            foreach (string encoding in response.Content.Headers.ContentEncoding)
            {
                if (!string.Equals(encoding, "identity", StringComparison.OrdinalIgnoreCase))
                    throw new IOException($"Encoded HTTP package responses are not resumable: {encoding}");
            }
        }

        private static void ValidateFreshHeaders(HttpContentHeaders headers, long totalBytes)
        {
            if (headers.ContentRange != null)
                throw new IOException("HTTP 200 package response must not include Content-Range.");
            if (headers.ContentLength.HasValue && headers.ContentLength.Value != totalBytes)
            {
                throw new IOException(
                    $"HTTP package length was {headers.ContentLength.Value} bytes; expected {totalBytes} bytes.");
            }
        }

        private static void ValidateRangeHeaders(
            HttpContentHeaders headers,
            long offset,
            long totalBytes)
        {
            ContentRangeHeaderValue range = headers.ContentRange;
            if (range == null ||
                !string.Equals(range.Unit, "bytes", StringComparison.OrdinalIgnoreCase) ||
                !range.From.HasValue ||
                !range.To.HasValue ||
                !range.Length.HasValue)
                throw new IOException("HTTP 206 package response has no complete byte Content-Range.");
            if (range.From.Value != offset ||
                range.To.Value != totalBytes - 1 ||
                range.Length.Value != totalBytes)
            {
                throw new IOException(
                    $"HTTP Content-Range '{range}' does not match bytes {offset}-{totalBytes - 1}/{totalBytes}.");
            }

            long remainingBytes = totalBytes - offset;
            if (headers.ContentLength.HasValue && headers.ContentLength.Value != remainingBytes)
            {
                throw new IOException(
                    $"HTTP range body length was {headers.ContentLength.Value} bytes; expected {remainingBytes} bytes.");
            }
        }

        private static IOException ResponseError(HttpResponseMessage response, string requirement)
        {
            return new IOException(
                $"{requirement} Server returned {(int)response.StatusCode} {response.ReasonPhrase}.".Trim());
        }

        private sealed class OwnedResponseStream : Stream
        {
            private readonly Stream inner;
            private readonly HttpResponseMessage response;
            private readonly HttpRequestMessage request;
            private bool disposed;

            public OwnedResponseStream(
                Stream inner,
                HttpResponseMessage response,
                HttpRequestMessage request)
            {
                this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
                this.response = response;
                this.request = request;
            }

            public override bool CanRead => inner.CanRead;
            public override bool CanSeek => inner.CanSeek;
            public override bool CanWrite => inner.CanWrite;
            public override long Length => inner.Length;
            public override long Position { get => inner.Position; set => inner.Position = value; }
            public override void Flush() => inner.Flush();
            public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
            public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
            public override void SetLength(long value) => inner.SetLength(value);
            public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
            public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
                inner.ReadAsync(buffer, offset, count, cancellationToken);
            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
                inner.WriteAsync(buffer, offset, count, cancellationToken);

            protected override void Dispose(bool disposing)
            {
                if (disposed)
                    return;
                disposed = true;
                if (disposing)
                {
                    inner.Dispose();
                    response.Dispose();
                    request.Dispose();
                }
                base.Dispose(disposing);
            }
        }
    }
}
