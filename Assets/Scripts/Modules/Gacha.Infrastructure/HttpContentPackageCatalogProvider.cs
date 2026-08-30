using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Gacha.Application;

namespace Gacha.Infrastructure.Content
{
    /// <summary>
    /// Downloads the small versioned package catalog. Package archives are read
    /// by HttpContentPackageByteSource after this provider validates the catalog.
    /// </summary>
    public sealed class HttpContentPackageCatalogProvider : IContentPackageCatalogProvider, IDisposable
    {
        public const int DefaultMaximumCatalogBytes = 1024 * 1024;
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

        private readonly Uri catalogUri;
        private readonly HttpClient client;
        private readonly bool ownsClient;
        private readonly int maximumCatalogBytes;
        private readonly TimeSpan timeout;
        private readonly JsonContentPackageCatalogReader reader;
        private bool disposed;

        public HttpContentPackageCatalogProvider(
            Uri catalogUri,
            HttpClient client = null,
            int maximumCatalogBytes = DefaultMaximumCatalogBytes,
            TimeSpan? timeout = null,
            JsonContentPackageCatalogReader reader = null)
        {
            ValidateUri(catalogUri);
            if (maximumCatalogBytes < 1024 || maximumCatalogBytes > 4 * 1024 * 1024)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumCatalogBytes),
                    "Catalog size limit must be between 1 KiB and 4 MiB.");
            }

            TimeSpan resolvedTimeout = timeout ?? DefaultTimeout;
            if (resolvedTimeout < TimeSpan.FromSeconds(1) || resolvedTimeout > TimeSpan.FromMinutes(2))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timeout),
                    "Catalog timeout must be between 1 second and 2 minutes.");
            }

            this.catalogUri = catalogUri;
            this.client = client ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            ownsClient = client == null;
            this.maximumCatalogBytes = maximumCatalogBytes;
            this.timeout = resolvedTimeout;
            this.reader = reader ?? new JsonContentPackageCatalogReader();
        }

        public async Task<ContentPackageCatalogLoadResult> LoadAsync(CancellationToken cancellationToken)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(HttpContentPackageCatalogProvider));

            using (var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            using (var request = new HttpRequestMessage(HttpMethod.Get, catalogUri))
            {
                timeoutCancellation.CancelAfter(timeout);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("identity"));

                try
                {
                    using (HttpResponseMessage response = await client.SendAsync(
                               request,
                               HttpCompletionOption.ResponseHeadersRead,
                               timeoutCancellation.Token))
                    {
                        Uri responseUri = response.RequestMessage?.RequestUri ?? catalogUri;
                        ValidateUri(responseUri);
                        ValidateResponse(response);
                        string json = await ReadJsonAsync(response.Content, timeoutCancellation.Token);
                        ContentPackageCatalogLoadResult result = reader.Read(json, responseUri);
                        if (result.Succeeded && result.Catalog.IsProtected &&
                            !string.Equals(
                                responseUri.AbsoluteUri,
                                catalogUri.AbsoluteUri,
                                StringComparison.Ordinal))
                        {
                            return ContentPackageCatalogLoadResult.Failure(
                                "Protected content package catalogs cannot be consumed through redirects; " +
                                "configure the final HTTPS catalog URL directly.");
                        }
                        return result;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException)
                {
                    return ContentPackageCatalogLoadResult.Failure(
                        $"Content package catalog request timed out after {timeout.TotalSeconds:0.#} seconds.");
                }
                catch (Exception exception) when (!(exception is OutOfMemoryException))
                {
                    return ContentPackageCatalogLoadResult.Failure(
                        "Content package catalog could not be downloaded: " + exception.Message);
                }
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

        private async Task<string> ReadJsonAsync(HttpContent content, CancellationToken cancellationToken)
        {
            long? declaredLength = content.Headers.ContentLength;
            if (declaredLength.HasValue && declaredLength.Value > maximumCatalogBytes)
            {
                throw new InvalidDataException(
                    $"Content package catalog declares {declaredLength.Value} bytes; limit is {maximumCatalogBytes} bytes.");
            }

            using (Stream source = await content.ReadAsStreamAsync())
            using (var destination = new MemoryStream(declaredLength.HasValue
                       ? (int)declaredLength.Value
                       : Math.Min(maximumCatalogBytes, 16 * 1024)))
            {
                var buffer = new byte[8192];
                while (true)
                {
                    int read = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                    if (read <= 0)
                        break;
                    if (destination.Length + read > maximumCatalogBytes)
                    {
                        throw new InvalidDataException(
                            $"Content package catalog exceeds the {maximumCatalogBytes} byte limit.");
                    }
                    destination.Write(buffer, 0, read);
                }

                byte[] bytes = destination.ToArray();
                int offset = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf
                    ? 3
                    : 0;
                return new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset);
            }
        }

        private static void ValidateResponse(HttpResponseMessage response)
        {
            if (response == null)
                throw new IOException("HTTP catalog provider returned no response.");
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new IOException(
                    $"Content package catalog requires HTTP 200 OK; server returned {(int)response.StatusCode} {response.ReasonPhrase}.".Trim());
            }
            if (response.Content == null)
                throw new IOException("HTTP catalog provider returned no response body.");

            MediaTypeHeaderValue contentType = response.Content.Headers.ContentType;
            if (contentType != null && !IsJsonMediaType(contentType.MediaType))
                throw new InvalidDataException("Content package catalog response is not JSON.");
            foreach (string encoding in response.Content.Headers.ContentEncoding)
            {
                if (!string.Equals(encoding, "identity", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Content package catalog response must use identity encoding.");
            }
        }

        private static bool IsJsonMediaType(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;
            return string.Equals(value, "application/json", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "text/json", StringComparison.OrdinalIgnoreCase) ||
                   value.EndsWith("+json", StringComparison.OrdinalIgnoreCase);
        }

        private static void ValidateUri(Uri uri)
        {
            if (uri == null || !uri.IsAbsoluteUri)
                throw new ArgumentException("Content package catalog URI must be absolute.", nameof(uri));
            bool isHttps = string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
            bool isLoopbackHttp = string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                                  uri.IsLoopback;
            if (!isHttps && !isLoopbackHttp)
                throw new ArgumentException("Content package catalog URI must use HTTPS; HTTP is allowed only for loopback fixtures.", nameof(uri));
            if (!string.IsNullOrEmpty(uri.UserInfo))
                throw new ArgumentException("Content package catalog URI cannot contain embedded credentials.", nameof(uri));
            if (!string.IsNullOrEmpty(uri.Fragment))
                throw new ArgumentException("Content package catalog URI cannot contain a fragment.", nameof(uri));
        }
    }
}
