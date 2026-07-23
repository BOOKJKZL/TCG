using System;
using System.Threading;
using System.Threading.Tasks;

namespace Gacha.Application
{
    public enum ContentImageLoadStatus
    {
        Succeeded,
        InvalidPath,
        NotFound,
        IntegrityMismatch,
        Failed
    }

    public sealed class ContentImageLoadResult
    {
        private ContentImageLoadResult(
            ContentImageLoadStatus status,
            string relativePath,
            byte[] data,
            string sha256,
            string errorMessage)
        {
            Status = status;
            RelativePath = relativePath;
            Data = data;
            Sha256 = sha256;
            ErrorMessage = errorMessage;
        }

        public ContentImageLoadStatus Status { get; }
        public string RelativePath { get; }
        public byte[] Data { get; }
        public string Sha256 { get; }
        public string ErrorMessage { get; }
        public bool Succeeded => Status == ContentImageLoadStatus.Succeeded && Data != null;

        public static ContentImageLoadResult Success(string relativePath, byte[] data, string sha256)
        {
            if (data == null || data.Length == 0)
                throw new ArgumentException("Successful image data cannot be empty.", nameof(data));

            return new ContentImageLoadResult(
                ContentImageLoadStatus.Succeeded,
                relativePath,
                data,
                sha256,
                null);
        }

        public static ContentImageLoadResult Failure(
            ContentImageLoadStatus status,
            string relativePath,
            string errorMessage)
        {
            if (status == ContentImageLoadStatus.Succeeded)
                throw new ArgumentException("Use Success for a successful image result.", nameof(status));

            return new ContentImageLoadResult(
                status,
                relativePath,
                null,
                null,
                string.IsNullOrWhiteSpace(errorMessage) ? "Image loading failed." : errorMessage.Trim());
        }
    }

    public interface IContentImageSource
    {
        Task<ContentImageLoadResult> LoadAsync(
            string relativePath,
            string expectedSha256 = null,
            CancellationToken cancellationToken = default);
    }
}
