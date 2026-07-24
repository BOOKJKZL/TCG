using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Gacha.EditorTools.Content
{
    public sealed class CloudflareR2Credentials
    {
        public CloudflareR2Credentials(
            Uri s3Endpoint,
            string bucketName,
            string accessKeyId,
            string secretAccessKey)
        {
            S3Endpoint = ValidateEndpoint(s3Endpoint);
            BucketName = RequireToken(bucketName, nameof(bucketName));
            AccessKeyId = RequireToken(accessKeyId, nameof(accessKeyId));
            SecretAccessKey = RequireToken(secretAccessKey, nameof(secretAccessKey));
            ValidatePortableSegment(BucketName, nameof(bucketName));
        }

        public Uri S3Endpoint { get; }
        public string BucketName { get; }
        public string AccessKeyId { get; }
        public string SecretAccessKey { get; }

        private static string RequireToken(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("R2 credential value cannot be empty.", name);
            string trimmed = value.Trim();
            if (trimmed.Any(char.IsWhiteSpace))
                throw new ArgumentException("R2 credential value cannot contain whitespace.", name);
            return trimmed;
        }

        private static void ValidatePortableSegment(string value, string name)
        {
            if (value == "." || value == ".." || value.Any(character =>
                    !(char.IsLetterOrDigit(character) || character == '-' || character == '_' || character == '.')))
                throw new ArgumentException("R2 bucket name is not portable.", name);
        }

        private static Uri ValidateEndpoint(Uri value)
        {
            if (value == null || !value.IsAbsoluteUri ||
                !string.Equals(value.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !value.Host.EndsWith(".r2.cloudflarestorage.com", StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrEmpty(value.UserInfo) || !string.IsNullOrEmpty(value.Query) ||
                !string.IsNullOrEmpty(value.Fragment) || value.AbsolutePath.Trim('/').Length != 0)
            {
                throw new ArgumentException(
                    "R2 S3 endpoint must be a credential-free Cloudflare HTTPS endpoint with no path.",
                    nameof(value));
            }
            return new Uri(value.GetLeftPart(UriPartial.Authority));
        }
    }

    /// <summary>
    /// Minimal Cloudflare R2 S3 client. Long-lived credentials stay in the
    /// desktop Editor process and are never serialized into project assets.
    /// </summary>
    public sealed class CloudflareR2ObjectStore : IR2ReleaseObjectStore, IDisposable
    {
        private static readonly byte[] EmptyBytes = Array.Empty<byte>();
        private readonly CloudflareR2Credentials credentials;
        private readonly HttpClient originClient;
        private readonly HttpClient publicClient;
        private readonly Func<DateTimeOffset> utcNow;
        private readonly bool ownsOriginClient;
        private readonly bool ownsPublicClient;

        public CloudflareR2ObjectStore(
            CloudflareR2Credentials credentials,
            TimeSpan timeout,
            HttpClient originClient = null,
            HttpClient publicClient = null,
            Func<DateTimeOffset> utcNow = null)
        {
            this.credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
            if (timeout < TimeSpan.FromSeconds(1) || timeout > TimeSpan.FromMinutes(10))
                throw new ArgumentOutOfRangeException(nameof(timeout), "R2 timeout must be from 1 second through 10 minutes.");
            this.originClient = originClient ?? CreateClient(timeout);
            this.publicClient = publicClient ?? CreateClient(timeout);
            this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
            ownsOriginClient = originClient == null;
            ownsPublicClient = publicClient == null;
        }

        public async Task<R2RemoteObjectState> InspectAsync(
            string objectKey,
            CancellationToken cancellationToken)
        {
            using (HttpResponseMessage response = await SendOriginAsync(
                       HttpMethod.Head,
                       objectKey,
                       null,
                       R2ReleasePublisher.ComputeSha256(EmptyBytes),
                       null,
                       null,
                       null,
                       cancellationToken))
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                    return R2RemoteObjectState.Missing();
                EnsureSuccess(response, "inspect", objectKey);
                long bytes = response.Content.Headers.ContentLength ?? -1;
                string sha256 = ReadMetadataHash(response);
                return R2RemoteObjectState.Present(bytes, sha256);
            }
        }

        public async Task UploadFileAsync(
            string objectKey,
            string localPath,
            string sha256,
            string contentType,
            string cacheControl,
            CancellationToken cancellationToken)
        {
            using (FileStream stream = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var content = new StreamContent(stream))
            using (HttpResponseMessage response = await SendOriginAsync(
                       HttpMethod.Put,
                       objectKey,
                       content,
                       sha256,
                       sha256,
                       contentType,
                       cacheControl,
                       cancellationToken))
            {
                EnsureSuccess(response, "upload", objectKey);
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
            using (var content = new ByteArrayContent(bytes ?? throw new ArgumentNullException(nameof(bytes))))
            using (HttpResponseMessage response = await SendOriginAsync(
                       HttpMethod.Put,
                       objectKey,
                       content,
                       sha256,
                       sha256,
                       contentType,
                       cacheControl,
                       cancellationToken))
            {
                EnsureSuccess(response, "upload", objectKey);
            }
        }

        public async Task<R2RemoteObjectState> VerifyOriginAsync(
            string objectKey,
            long expectedBytes,
            CancellationToken cancellationToken)
        {
            using (HttpResponseMessage response = await SendOriginAsync(
                       HttpMethod.Get,
                       objectKey,
                       null,
                       R2ReleasePublisher.ComputeSha256(EmptyBytes),
                       null,
                       null,
                       null,
                       cancellationToken))
            {
                EnsureSuccess(response, "download", objectKey);
                return await HashResponseAsync(response, expectedBytes, cancellationToken);
            }
        }

        public async Task<R2RemoteObjectState> VerifyPublicAsync(
            Uri publicUri,
            long expectedBytes,
            string expectedSha256,
            CancellationToken cancellationToken)
        {
            ValidatePublicUri(publicUri);
            var builder = new UriBuilder(publicUri)
            {
                Query = "gacha_verify=" + Uri.EscapeDataString(expectedSha256)
            };
            using (var request = new HttpRequestMessage(HttpMethod.Get, builder.Uri))
            using (HttpResponseMessage response = await publicClient.SendAsync(
                       request,
                       HttpCompletionOption.ResponseHeadersRead,
                       cancellationToken))
            {
                EnsureSuccess(response, "publicly download", publicUri.AbsoluteUri);
                Uri finalUri = response.RequestMessage?.RequestUri;
                ValidatePublicUri(finalUri);
                return await HashResponseAsync(response, expectedBytes, cancellationToken);
            }
        }

        public void Dispose()
        {
            if (ownsOriginClient)
                originClient.Dispose();
            if (ownsPublicClient)
                publicClient.Dispose();
        }

        internal static string CreateAuthorization(
            HttpMethod method,
            Uri uri,
            string payloadSha256,
            string metadataSha256,
            string accessKeyId,
            string secretAccessKey,
            DateTimeOffset timestamp)
        {
            string amzDate = timestamp.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
            string date = timestamp.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            string canonicalHeaders = "host:" + uri.Authority.ToLowerInvariant() + "\n" +
                                      "x-amz-content-sha256:" + payloadSha256 + "\n" +
                                      "x-amz-date:" + amzDate + "\n";
            string signedHeaders = "host;x-amz-content-sha256;x-amz-date";
            if (!string.IsNullOrEmpty(metadataSha256))
            {
                canonicalHeaders += "x-amz-meta-sha256:" + metadataSha256 + "\n";
                signedHeaders += ";x-amz-meta-sha256";
            }

            string canonicalRequest = method.Method + "\n" +
                                      uri.AbsolutePath + "\n\n" +
                                      canonicalHeaders + "\n" +
                                      signedHeaders + "\n" +
                                      payloadSha256;
            string scope = date + "/auto/s3/aws4_request";
            string stringToSign = "AWS4-HMAC-SHA256\n" + amzDate + "\n" + scope + "\n" +
                                  Sha256Hex(Encoding.UTF8.GetBytes(canonicalRequest));
            byte[] dateKey = Hmac(Encoding.UTF8.GetBytes("AWS4" + secretAccessKey), date);
            byte[] regionKey = Hmac(dateKey, "auto");
            byte[] serviceKey = Hmac(regionKey, "s3");
            byte[] signingKey = Hmac(serviceKey, "aws4_request");
            string signature = R2ReleasePublisher.ToHex(Hmac(signingKey, stringToSign));
            return "AWS4-HMAC-SHA256 Credential=" + accessKeyId + "/" + scope +
                   ", SignedHeaders=" + signedHeaders + ", Signature=" + signature;
        }

        private async Task<HttpResponseMessage> SendOriginAsync(
            HttpMethod method,
            string objectKey,
            HttpContent content,
            string payloadSha256,
            string metadataSha256,
            string contentType,
            string cacheControl,
            CancellationToken cancellationToken)
        {
            Uri uri = BuildOriginUri(objectKey);
            DateTimeOffset timestamp = utcNow();
            string amzDate = timestamp.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
            var request = new HttpRequestMessage(method, uri) { Content = content };
            request.Headers.Host = uri.Authority;
            request.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadSha256);
            request.Headers.TryAddWithoutValidation("x-amz-date", amzDate);
            if (!string.IsNullOrEmpty(metadataSha256))
                request.Headers.TryAddWithoutValidation("x-amz-meta-sha256", metadataSha256);
            request.Headers.TryAddWithoutValidation(
                "Authorization",
                CreateAuthorization(
                    method,
                    uri,
                    payloadSha256,
                    metadataSha256,
                    credentials.AccessKeyId,
                    credentials.SecretAccessKey,
                    timestamp));
            if (content != null)
            {
                if (!string.IsNullOrEmpty(contentType))
                    content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
                if (!string.IsNullOrEmpty(cacheControl))
                    content.Headers.TryAddWithoutValidation("Cache-Control", cacheControl);
            }

            try
            {
                return await originClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
            }
            finally
            {
                request.Dispose();
            }
        }

        private Uri BuildOriginUri(string objectKey)
        {
            ValidateObjectKey(objectKey);
            string escapedBucket = Uri.EscapeDataString(credentials.BucketName);
            string escapedKey = string.Join("/", objectKey.Split('/').Select(Uri.EscapeDataString));
            return new Uri(
                credentials.S3Endpoint.AbsoluteUri.TrimEnd('/') + "/" + escapedBucket + "/" + escapedKey);
        }

        private static async Task<R2RemoteObjectState> HashResponseAsync(
            HttpResponseMessage response,
            long expectedBytes,
            CancellationToken cancellationToken)
        {
            if (expectedBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(expectedBytes));
            long? declared = response.Content.Headers.ContentLength;
            if (declared.HasValue && declared.Value != expectedBytes)
                throw new IOException($"Remote object length is {declared.Value}, expected {expectedBytes}.");

            using (Stream stream = await response.Content.ReadAsStreamAsync())
            using (SHA256 sha = SHA256.Create())
            {
                long total = 0;
                var buffer = new byte[81920];
                while (true)
                {
                    int read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                    if (read == 0)
                        break;
                    checked { total += read; }
                    if (total > expectedBytes)
                        throw new IOException("Remote object exceeds its expected length.");
                    sha.TransformBlock(buffer, 0, read, buffer, 0);
                }
                sha.TransformFinalBlock(EmptyBytes, 0, 0);
                if (total != expectedBytes)
                    throw new IOException($"Remote object length is {total}, expected {expectedBytes}.");
                return R2RemoteObjectState.Present(total, R2ReleasePublisher.ToHex(sha.Hash));
            }
        }

        private static string ReadMetadataHash(HttpResponseMessage response)
        {
            if (!response.Headers.TryGetValues("x-amz-meta-sha256", out var values))
                return null;
            return values.FirstOrDefault()?.Trim().ToLowerInvariant();
        }

        private static void ValidateObjectKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.StartsWith("/", StringComparison.Ordinal) ||
                value.Contains("\\") || value.Split('/').Any(segment =>
                    string.IsNullOrEmpty(segment) || segment == "." || segment == ".."))
                throw new ArgumentException("R2 object key is invalid.", nameof(value));
        }

        private static void ValidatePublicUri(Uri uri)
        {
            if (uri == null || !uri.IsAbsoluteUri ||
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
                throw new InvalidDataException("Public R2 verification URL must remain credential-free HTTPS.");
        }

        private static void EnsureSuccess(HttpResponseMessage response, string operation, string target)
        {
            if (response.IsSuccessStatusCode)
                return;
            throw new HttpRequestException(
                $"Could not {operation} R2 object '{target}': HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");
        }

        private static HttpClient CreateClient(TimeSpan timeout)
        {
            return new HttpClient(new HttpClientHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.None
            })
            {
                Timeout = timeout
            };
        }

        private static string Sha256Hex(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
                return R2ReleasePublisher.ToHex(sha.ComputeHash(bytes));
        }

        private static byte[] Hmac(byte[] key, string value)
        {
            using (var hmac = new HMACSHA256(key))
                return hmac.ComputeHash(Encoding.UTF8.GetBytes(value));
        }
    }
}
