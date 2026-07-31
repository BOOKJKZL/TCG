using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Gacha.Application;

namespace Gacha.Infrastructure.Content
{
    public sealed class PrivateContentImageSource : IContentImageSource
    {
        public const long DefaultMaximumImageBytes = 32L * 1024L * 1024L;

        private readonly string contentRoot;
        private readonly string contentRootPrefix;
        private readonly long maximumImageBytes;

        public PrivateContentImageSource(
            string contentRoot,
            long maximumImageBytes = DefaultMaximumImageBytes)
        {
            if (string.IsNullOrWhiteSpace(contentRoot))
                throw new ArgumentException("Content root cannot be empty.", nameof(contentRoot));
            if (maximumImageBytes < 1)
                throw new ArgumentOutOfRangeException(nameof(maximumImageBytes), "Maximum image size must be at least one byte.");

            this.contentRoot = Path.GetFullPath(contentRoot);
            contentRootPrefix = this.contentRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                                Path.DirectorySeparatorChar;
            this.maximumImageBytes = maximumImageBytes;
        }

        public long MaximumImageBytes => maximumImageBytes;

        public async Task<ContentImageLoadResult> LoadAsync(
            string relativePath,
            string expectedSha256 = null,
            CancellationToken cancellationToken = default)
        {
            if (!TryResolvePath(relativePath, out string fullPath))
            {
                return ContentImageLoadResult.Failure(
                    ContentImageLoadStatus.InvalidPath,
                    relativePath,
                    "The image path is empty, rooted, or outside the installed content directory.");
            }

            if (!File.Exists(fullPath))
            {
                return ContentImageLoadResult.Failure(
                    ContentImageLoadStatus.NotFound,
                    relativePath,
                    $"The installed image does not exist: {relativePath}");
            }

            try
            {
                long fileLength = new FileInfo(fullPath).Length;
                if (fileLength > maximumImageBytes)
                    return ImageTooLarge(relativePath, fileLength);

                byte[] data = await Task.Run(() => File.ReadAllBytes(fullPath), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (data.LongLength > maximumImageBytes)
                    return ImageTooLarge(relativePath, data.LongLength);

                string actualSha256 = ComputeSha256(data);
                if (!string.IsNullOrWhiteSpace(expectedSha256) &&
                    !string.Equals(actualSha256, NormalizeHash(expectedSha256), StringComparison.OrdinalIgnoreCase))
                {
                    return ContentImageLoadResult.Failure(
                        ContentImageLoadStatus.IntegrityMismatch,
                        relativePath,
                        $"The installed image failed its SHA-256 check: {relativePath}");
                }

                return ContentImageLoadResult.Success(relativePath, data, actualSha256);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is CryptographicException)
            {
                return ContentImageLoadResult.Failure(
                    ContentImageLoadStatus.Failed,
                    relativePath,
                    exception.Message);
            }
        }

        private bool TryResolvePath(string relativePath, out string fullPath)
        {
            fullPath = null;
            if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
                return false;

            try
            {
                string normalized = relativePath.Trim()
                    .Replace('/', Path.DirectorySeparatorChar)
                    .Replace('\\', Path.DirectorySeparatorChar);
                string candidate = Path.GetFullPath(Path.Combine(contentRoot, normalized));
                StringComparison pathComparison = Path.DirectorySeparatorChar == '\\'
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
                if (!candidate.StartsWith(contentRootPrefix, pathComparison))
                    return false;

                fullPath = candidate;
                return true;
            }
            catch (Exception exception) when (
                exception is ArgumentException ||
                exception is NotSupportedException ||
                exception is PathTooLongException)
            {
                return false;
            }
        }

        private static string ComputeSha256(byte[] data)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                return string.Concat(sha256.ComputeHash(data).Select(value => value.ToString("x2")));
            }
        }

        private static string NormalizeHash(string hash)
        {
            return hash.Trim().Replace("-", string.Empty);
        }

        private ContentImageLoadResult ImageTooLarge(string relativePath, long imageBytes)
        {
            return ContentImageLoadResult.Failure(
                ContentImageLoadStatus.Failed,
                relativePath,
                $"The installed image is {imageBytes} bytes, exceeding the {maximumImageBytes}-byte safety limit: {relativePath}");
        }
    }
}
