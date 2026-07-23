using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Gacha.Application;
using Gacha.Domain;
using UnityEngine;

namespace Gacha.Presentation
{
    public interface ICardTextureCache
    {
        Task<CardTextureLoadResult> LoadAsync(
            PrintingDefinition printing,
            CancellationToken cancellationToken = default);
    }

    public sealed class CardTextureLoadResult
    {
        private CardTextureLoadResult(
            ContentImageLoadStatus status,
            Texture2D texture,
            string errorMessage,
            bool fromCache)
        {
            Status = status;
            Texture = texture;
            ErrorMessage = errorMessage;
            FromCache = fromCache;
        }

        public ContentImageLoadStatus Status { get; }
        public Texture2D Texture { get; }
        public string ErrorMessage { get; }
        public bool FromCache { get; }
        public bool Succeeded => Status == ContentImageLoadStatus.Succeeded && Texture != null;

        public static CardTextureLoadResult Success(Texture2D texture, bool fromCache)
        {
            return new CardTextureLoadResult(
                ContentImageLoadStatus.Succeeded,
                texture,
                null,
                fromCache);
        }

        public static CardTextureLoadResult Failure(ContentImageLoadStatus status, string errorMessage)
        {
            return new CardTextureLoadResult(status, null, errorMessage, false);
        }
    }

    public sealed class CardTextureCache : ICardTextureCache, IDisposable
    {
        private sealed class CacheEntry
        {
            public Texture2D Texture;
            public LinkedListNode<string> Node;
        }

        private readonly object gate = new object();
        private readonly IContentImageSource source;
        private readonly int capacity;
        private readonly Func<byte[], Texture2D> decode;
        private readonly Dictionary<string, CacheEntry> entries = new Dictionary<string, CacheEntry>(StringComparer.Ordinal);
        private readonly Dictionary<string, Task<CardTextureLoadResult>> inFlight = new Dictionary<string, Task<CardTextureLoadResult>>(StringComparer.Ordinal);
        private readonly LinkedList<string> lru = new LinkedList<string>();
        private volatile bool disposed;

        public CardTextureCache(IContentImageSource source, int capacity = 32, Func<byte[], Texture2D> decoder = null)
        {
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            if (capacity < 1)
                throw new ArgumentOutOfRangeException(nameof(capacity), "Texture cache capacity must be at least one.");

            this.capacity = capacity;
            decode = decoder ?? DecodeTexture;
        }

        public int Capacity => capacity;

        public int Count
        {
            get
            {
                lock (gate)
                    return entries.Count;
            }
        }

        public int InFlightCount
        {
            get
            {
                lock (gate)
                    return inFlight.Count;
            }
        }

        public Task<CardTextureLoadResult> LoadAsync(
            PrintingDefinition printing,
            CancellationToken cancellationToken = default)
        {
            if (printing == null)
                throw new ArgumentNullException(nameof(printing));
            if (string.IsNullOrWhiteSpace(printing.ImageRelativePath))
            {
                return Task.FromResult(CardTextureLoadResult.Failure(
                    ContentImageLoadStatus.NotFound,
                    "This printing has no installed image path."));
            }

            cancellationToken.ThrowIfCancellationRequested();
            string key = CacheKey(printing);
            Task<CardTextureLoadResult> sharedTask;
            lock (gate)
            {
                ThrowIfDisposed();
                if (entries.TryGetValue(key, out CacheEntry cached))
                {
                    Touch(cached);
                    return Task.FromResult(CardTextureLoadResult.Success(cached.Texture, true));
                }

                if (!inFlight.TryGetValue(key, out sharedTask))
                {
                    sharedTask = LoadAndCacheAsync(key, printing);
                    inFlight.Add(key, sharedTask);
                }
            }

            return AwaitWithCancellation(sharedTask, cancellationToken);
        }

        public void Clear()
        {
            List<Texture2D> textures;
            lock (gate)
            {
                textures = new List<Texture2D>(entries.Count);
                foreach (CacheEntry entry in entries.Values)
                    textures.Add(entry.Texture);
                entries.Clear();
                lru.Clear();
            }

            foreach (Texture2D texture in textures)
                DestroyTexture(texture);
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed)
                    return;
                disposed = true;
            }

            Clear();
        }

        private async Task<CardTextureLoadResult> LoadAndCacheAsync(string key, PrintingDefinition printing)
        {
            // Ensure the shared task is registered before a synchronously-completing source can finish it.
            await Task.Yield();
            try
            {
                ContentImageLoadResult image = await source.LoadAsync(
                    printing.ImageRelativePath,
                    printing.ImageSha256,
                    CancellationToken.None);
                if (!image.Succeeded)
                    return CardTextureLoadResult.Failure(image.Status, image.ErrorMessage);

                Texture2D texture;
                try
                {
                    texture = decode(image.Data);
                }
                catch (Exception exception)
                {
                    return CardTextureLoadResult.Failure(ContentImageLoadStatus.Failed, exception.Message);
                }

                if (texture == null)
                {
                    return CardTextureLoadResult.Failure(
                        ContentImageLoadStatus.Failed,
                        $"The installed image could not be decoded: {printing.ImageRelativePath}");
                }

                texture.name = $"Card_{printing.Id}";
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;

                Texture2D evicted = null;
                lock (gate)
                {
                    if (disposed)
                    {
                        evicted = texture;
                    }
                    else
                    {
                        var node = lru.AddFirst(key);
                        entries.Add(key, new CacheEntry { Texture = texture, Node = node });
                        if (entries.Count > capacity)
                            evicted = RemoveLeastRecentlyUsed();
                    }
                }

                DestroyTexture(evicted);
                return disposed
                    ? CardTextureLoadResult.Failure(ContentImageLoadStatus.Failed, "The texture cache was disposed during loading.")
                    : CardTextureLoadResult.Success(texture, false);
            }
            finally
            {
                lock (gate)
                    inFlight.Remove(key);
            }
        }

        private Texture2D RemoveLeastRecentlyUsed()
        {
            LinkedListNode<string> last = lru.Last;
            if (last == null || !entries.TryGetValue(last.Value, out CacheEntry entry))
                return null;

            lru.Remove(last);
            entries.Remove(last.Value);
            return entry.Texture;
        }

        private void Touch(CacheEntry entry)
        {
            lru.Remove(entry.Node);
            lru.AddFirst(entry.Node);
        }

        private static string CacheKey(PrintingDefinition printing)
        {
            return $"{printing.ImageRelativePath}|{printing.ImageSha256}";
        }

        private static Texture2D DecodeTexture(byte[] data)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            if (ImageConversion.LoadImage(texture, data, true))
                return texture;

            DestroyTexture(texture);
            return null;
        }

        private static void DestroyTexture(Texture2D texture)
        {
            if (texture == null)
                return;

            if (UnityEngine.Application.isPlaying)
                UnityEngine.Object.Destroy(texture);
            else
                UnityEngine.Object.DestroyImmediate(texture);
        }

        private static async Task<CardTextureLoadResult> AwaitWithCancellation(
            Task<CardTextureLoadResult> task,
            CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled)
                return await task;

            var cancelled = new TaskCompletionSource<bool>();
            using (cancellationToken.Register(() => cancelled.TrySetResult(true)))
            {
                if (task != await Task.WhenAny(task, cancelled.Task))
                    throw new OperationCanceledException(cancellationToken);
            }

            return await task;
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(CardTextureCache));
        }
    }
}
