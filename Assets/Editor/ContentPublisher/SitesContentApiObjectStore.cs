using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Gacha.EditorTools.Content
{
    public sealed class SitesContentApiCredentials
    {
        private static readonly Regex TokenPattern = new Regex(
            "^[A-Za-z0-9_-]{43,512}$",
            RegexOptions.CultureInvariant);

        public SitesContentApiCredentials(Uri siteBaseUri, string publisherToken)
        {
            SiteBaseUri = ValidateSiteBaseUri(siteBaseUri);
            if (string.IsNullOrWhiteSpace(publisherToken) || !TokenPattern.IsMatch(publisherToken))
                throw new ArgumentException("Sites publisher token is invalid.", nameof(publisherToken));
            PublisherToken = publisherToken;
        }

        public Uri SiteBaseUri { get; }
        public string PublisherToken { get; }
        public Uri PublicContentBaseUri => new Uri(SiteBaseUri, "api/content/");

        private static Uri ValidateSiteBaseUri(Uri value)
        {
            if (value == null || !value.IsAbsoluteUri)
                throw new ArgumentException("Sites base URL must be absolute HTTPS.", nameof(value));
            bool secure = string.Equals(value.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
            bool localDevelopment = value.IsLoopback &&
                                    string.Equals(value.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
            if (!secure && !localDevelopment)
                throw new ArgumentException("Sites base URL must use HTTPS outside loopback development.", nameof(value));
            if (!value.IsLoopback && !value.Host.EndsWith(".chatgpt.site", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Sites publisher credentials can only be sent to a chatgpt.site host.", nameof(value));
            if (!string.IsNullOrEmpty(value.UserInfo) ||
                !string.IsNullOrEmpty(value.Query) ||
                !string.IsNullOrEmpty(value.Fragment) ||
                (value.AbsolutePath != "/" && value.AbsolutePath.Length != 0))
            {
                throw new ArgumentException("Sites base URL cannot contain credentials, path, query, or fragment.", nameof(value));
            }
            return new Uri(value.AbsoluteUri.TrimEnd('/') + "/");
        }
    }

    /// <summary>
    /// Adapts the generic verified release publisher to the temporary Sites R2
    /// relay. The scoped bearer token is sent only to protected admin endpoints;
    /// public verification requests never contain it.
    /// </summary>
    public sealed class SitesContentApiObjectStore : IR2ReleaseObjectStore, IDisposable
    {
        private static readonly Regex ArchiveKeyPattern = new Regex(
            "^packages/([a-z0-9][a-z0-9._-]{0,79})/([a-f0-9]{64})\\.zip$",
            RegexOptions.CultureInvariant);

        private readonly SitesContentApiCredentials credentials;
        private readonly HttpClient client;
        private readonly bool ownsClient;

        public SitesContentApiObjectStore(SitesContentApiCredentials credentials, TimeSpan timeout)
            : this(credentials, CreateHandler(), timeout, true)
        {
        }

        public SitesContentApiObjectStore(
            SitesContentApiCredentials credentials,
            HttpMessageHandler handler,
            TimeSpan timeout)
            : this(credentials, handler, timeout, true)
        {
        }

        private SitesContentApiObjectStore(
            SitesContentApiCredentials credentials,
            HttpMessageHandler handler,
            TimeSpan timeout,
            bool ownsClient)
        {
            this.credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));
            if (timeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(timeout));
            client = new HttpClient(handler, true)
            {
                BaseAddress = credentials.SiteBaseUri,
                Timeout = timeout
            };
            this.ownsClient = ownsClient;
        }

        public async Task<R2RemoteObjectState> InspectAsync(
            string objectKey,
            CancellationToken cancellationToken)
        {
            return await InspectProtectedAsync(objectKey, cancellationToken);
        }

        public async Task UploadFileAsync(
            string objectKey,
            string localPath,
            string sha256,
            string contentType,
            string cacheControl,
            CancellationToken cancellationToken)
        {
            ArchiveIdentity identity = ParseArchiveKey(objectKey);
            long bytes = new FileInfo(localPath).Length;
            Uri uri = PackageAdminUri(identity, bytes);
            using (var request = CreateProtectedRequest(HttpMethod.Post, uri))
            using (var stream = File.OpenRead(localPath))
            using (var content = new StreamContent(stream))
            {
                content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
                content.Headers.ContentLength = bytes;
                request.Content = content;
                using (HttpResponseMessage response = await client.SendAsync(
                           request,
                           HttpCompletionOption.ResponseHeadersRead,
                           cancellationToken))
                {
                    await EnsureSuccessAsync(response, "ZIP upload");
                }
            }
        }

        public async Task UploadBytesAsync(
            string objectKey,
            byte[] bytes,
            string sha256,
            string contentType,
            string cacheControl,
            CancellationToken cancellationToken)
        {
            if (!string.Equals(objectKey, "catalog.json", StringComparison.Ordinal))
                throw new InvalidDataException("Sites catalog upload received an unexpected object key: " + objectKey);
            using (var request = CreateProtectedRequest(HttpMethod.Post, new Uri("api/admin/content/catalog", UriKind.Relative)))
            using (var content = new ByteArrayContent(bytes ?? throw new ArgumentNullException(nameof(bytes))))
            {
                content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                request.Content = content;
                using (HttpResponseMessage response = await client.SendAsync(
                           request,
                           HttpCompletionOption.ResponseHeadersRead,
                           cancellationToken))
                {
                    await EnsureSuccessAsync(response, "Catalog upload");
                }
            }
        }

        public async Task<R2RemoteObjectState> VerifyOriginAsync(
            string objectKey,
            long expectedBytes,
            CancellationToken cancellationToken)
        {
            return await InspectProtectedAsync(objectKey, cancellationToken);
        }

        public async Task<R2RemoteObjectState> VerifyPublicAsync(
            Uri publicUri,
            long expectedBytes,
            string expectedSha256,
            CancellationToken cancellationToken)
        {
            EnsurePublicUri(publicUri);
            using (var request = new HttpRequestMessage(HttpMethod.Get, publicUri))
            using (HttpResponseMessage response = await client.SendAsync(
                       request,
                       HttpCompletionOption.ResponseHeadersRead,
                       cancellationToken))
            {
                await EnsureSuccessAsync(response, "Public content verification");
                using (Stream stream = await response.Content.ReadAsStreamAsync())
                {
                    string sha256 = await ComputeSha256Async(stream, cancellationToken);
                    long bytes = response.Content.Headers.ContentLength ?? expectedBytes;
                    return R2RemoteObjectState.Present(bytes, sha256);
                }
            }
        }

        public void Dispose()
        {
            if (ownsClient)
                client.Dispose();
        }

        private async Task<R2RemoteObjectState> InspectProtectedAsync(
            string objectKey,
            CancellationToken cancellationToken)
        {
            Uri uri;
            if (string.Equals(objectKey, "catalog.json", StringComparison.Ordinal))
            {
                uri = new Uri("api/admin/content/catalog", UriKind.Relative);
            }
            else
            {
                ArchiveIdentity identity = ParseArchiveKey(objectKey);
                uri = PackageAdminUri(identity, 1);
            }

            using (var request = CreateProtectedRequest(HttpMethod.Head, uri))
            using (HttpResponseMessage response = await client.SendAsync(
                       request,
                       HttpCompletionOption.ResponseHeadersRead,
                       cancellationToken))
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                    return R2RemoteObjectState.Missing();
                await EnsureSuccessAsync(response, "Protected content inspection");
                long bytes = response.Content.Headers.ContentLength ?? 0;
                string sha256 = Header(response, "X-Content-Sha256");
                return R2RemoteObjectState.Present(bytes, sha256);
            }
        }

        private HttpRequestMessage CreateProtectedRequest(HttpMethod method, Uri uri)
        {
            var request = new HttpRequestMessage(method, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.PublisherToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return request;
        }

        private Uri PackageAdminUri(ArchiveIdentity identity, long downloadBytes)
        {
            string query = "api/admin/content/packages?packageId=" + Uri.EscapeDataString(identity.PackageId) +
                           "&sha256=" + Uri.EscapeDataString(identity.Sha256) +
                           "&downloadBytes=" + downloadBytes;
            return new Uri(query, UriKind.Relative);
        }

        private void EnsurePublicUri(Uri value)
        {
            if (value == null || !value.IsAbsoluteUri ||
                !string.Equals(value.Scheme, credentials.SiteBaseUri.Scheme, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(value.Host, credentials.SiteBaseUri.Host, StringComparison.OrdinalIgnoreCase) ||
                value.Port != credentials.SiteBaseUri.Port ||
                !value.AbsolutePath.StartsWith("/api/content/", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Public verification URL is outside the configured Site content API.");
            }
        }

        private static ArchiveIdentity ParseArchiveKey(string objectKey)
        {
            Match match = ArchiveKeyPattern.Match(objectKey ?? string.Empty);
            if (!match.Success)
                throw new InvalidDataException("Sites archive object key is invalid: " + objectKey);
            return new ArchiveIdentity(match.Groups[1].Value, match.Groups[2].Value);
        }

        private static async Task EnsureSuccessAsync(HttpResponseMessage response, string action)
        {
            if (response.IsSuccessStatusCode)
                return;
            string details = string.Empty;
            try
            {
                details = (await response.Content.ReadAsStringAsync()).Trim();
            }
            catch
            {
                // Preserve the HTTP status when the error body cannot be read.
            }
            if (details.Length > 300)
                details = details.Substring(0, 300);
            throw new IOException(
                action + " failed with HTTP " + (int)response.StatusCode +
                (string.IsNullOrEmpty(details) ? "." : ": " + details));
        }

        private static string Header(HttpResponseMessage response, string name)
        {
            return response.Headers.TryGetValues(name, out System.Collections.Generic.IEnumerable<string> values)
                ? values.FirstOrDefault() ?? string.Empty
                : string.Empty;
        }

        private static async Task<string> ComputeSha256Async(Stream stream, CancellationToken cancellationToken)
        {
            using (SHA256 sha = SHA256.Create())
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                    sha.TransformBlock(buffer, 0, read, buffer, 0);
                sha.TransformFinalBlock(new byte[0], 0, 0);
                return R2ReleasePublisher.ToHex(sha.Hash);
            }
        }

        private static HttpMessageHandler CreateHandler()
        {
            return new HttpClientHandler { AllowAutoRedirect = false };
        }

        private sealed class ArchiveIdentity
        {
            public ArchiveIdentity(string packageId, string sha256)
            {
                PackageId = packageId;
                Sha256 = sha256;
            }

            public string PackageId { get; }
            public string Sha256 { get; }
        }
    }
}
