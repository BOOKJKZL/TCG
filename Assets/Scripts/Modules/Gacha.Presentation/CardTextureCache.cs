using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Gacha.Application;
using Gacha.Domain;
using UnityEngine;
using WebP;

namespace Gacha.Presentation
{
    public interface ICardTextureCache
    {
        Task<CardTextureLoadResult> LoadAsync(
            PrintingDefinition printing,
            CancellationToken cancellationToken = default);
    }

    public sealed class CardTextureLoadResult : IDisposable
    {
        private Action release;

        private CardTextureLoadResult(
            ContentImageLoadStatus status,
            Texture2D texture,
            string errorMessage,
            bool fromCache,
            Action release)
        {
            Status = status;
            Texture = texture;
            ErrorMessage = errorMessage;
            FromCache = fromCache;
            this.release = release;
        }

        public ContentImageLoadStatus Status { get; }
        public Texture2D Texture { get; }
        public string ErrorMessage { get; }
        public bool FromCache { get; }
        public bool Succeeded => Status == ContentImageLoadStatus.Succeeded && Texture != null;

        public static CardTextureLoadResult Success(
            Texture2D texture,
            bool fromCache,
            Action release = null)
        {
            return new CardTextureLoadResult(
                ContentImageLoadStatus.Succeeded,
                texture,
                null,
                fromCache,
                release);
        }

        public static CardTextureLoadResult Failure(ContentImageLoadStatus status, string errorMessage)
        {
            return new CardTextureLoadResult(status, null, errorMessage, false, null);
        }

        public void Dispose()
        {
            Action releaseOnce = release;
            release = null;
            releaseOnce?.Invoke();
        }
    }

    public sealed class CardTextureCache : ICardTextureCache, IDisposable
    {
        public const long DefaultMaximumDecodedBytes = 48L * 1024L * 1024L;

        private sealed class CacheEntry
        {
            public Texture2D Texture;
            public LinkedListNode<string> Node;
            public long DecodedBytes;
            public int LeaseCount;
            public int PendingLoadCount;
            public bool RemoveWhenReleased;
        }

        private sealed class PendingLoad
        {
            public Task<CardTextureLoadResult> Task;
            public int Generation;
            public int WaiterCount;
            public CacheEntry Entry;
        }

        private readonly object gate = new object();
        private readonly IContentImageSource source;
        private readonly int capacity;
        private readonly long maximumDecodedBytes;
        private readonly Func<byte[], Texture2D> decode;
        private readonly Func<Texture2D, long> estimateDecodedBytes;
        private readonly Dictionary<string, CacheEntry> entries = new Dictionary<string, CacheEntry>(StringComparer.Ordinal);
        private readonly Dictionary<string, PendingLoad> inFlight = new Dictionary<string, PendingLoad>(StringComparer.Ordinal);
        private readonly LinkedList<string> lru = new LinkedList<string>();
        private long decodedBytes;
        private int trimGeneration;
        private volatile bool disposed;

        public CardTextureCache(
            IContentImageSource source,
            int capacity = 32,
            Func<byte[], Texture2D> decoder = null,
            long maximumDecodedBytes = DefaultMaximumDecodedBytes,
            Func<Texture2D, long> decodedSizeEstimator = null)
        {
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            if (capacity < 1)
                throw new ArgumentOutOfRangeException(nameof(capacity), "Texture cache capacity must be at least one.");
            if (maximumDecodedBytes < 1)
                throw new ArgumentOutOfRangeException(nameof(maximumDecodedBytes), "Texture cache byte budget must be at least one.");

            this.capacity = capacity;
            this.maximumDecodedBytes = maximumDecodedBytes;
            decode = decoder ?? DecodeTexture;
            estimateDecodedBytes = decodedSizeEstimator ?? EstimateDecodedBytes;
            UnityEngine.Application.lowMemory += TrimForMemoryPressure;
        }

        public int Capacity => capacity;
        public long MaximumDecodedBytes => maximumDecodedBytes;

        public long DecodedBytes
        {
            get
            {
                lock (gate)
                    return decodedBytes;
            }
        }

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
            PendingLoad load;
            lock (gate)
            {
                ThrowIfDisposed();
                if (entries.TryGetValue(key, out CacheEntry cached))
                {
                    Touch(cached);
                    return Task.FromResult(AcquireLease(key, cached, true));
                }

                if (!inFlight.TryGetValue(key, out load))
                {
                    load = new PendingLoad { Generation = trimGeneration };
                    inFlight.Add(key, load);
                }

                load.WaiterCount++;
                if (load.Task == null)
                    load.Task = LoadAndCacheAsync(key, printing, load);
            }

            return AwaitWithCancellationAndAcquire(key, load, cancellationToken);
        }

        public void Clear()
        {
            DestroyTextures(ClearEntriesAndInvalidateLoads());
        }

        public void TrimForMemoryPressure()
        {
            Clear();
        }

        public void Dispose()
        {
            List<Texture2D> textures;
            lock (gate)
            {
                if (disposed)
                    return;

                disposed = true;
                trimGeneration++;
                inFlight.Clear();
                textures = RemoveAllEntries();
            }

            UnityEngine.Application.lowMemory -= TrimForMemoryPressure;
            DestroyTextures(textures);
        }

        private async Task<CardTextureLoadResult> LoadAndCacheAsync(
            string key,
            PrintingDefinition printing,
            PendingLoad load)
        {
            // Ensure the shared task is registered before a synchronously-completing source can finish it.
            await Task.Yield();
            try
            {
                lock (gate)
                {
                    string rejection = LoadRejection(load);
                    if (rejection != null)
                        return CardTextureLoadResult.Failure(ContentImageLoadStatus.Failed, rejection);
                }

                ContentImageLoadResult image = await source.LoadAsync(
                    printing.ImageRelativePath,
                    printing.ImageSha256,
                    CancellationToken.None);
                return DecodeAndCache(key, printing, load, image);
            }
            catch (Exception exception)
            {
                // A shared load may outlive every caller; do not leave an unobserved source fault.
                return CardTextureLoadResult.Failure(ContentImageLoadStatus.Failed, exception.Message);
            }
            finally
            {
                lock (gate)
                    RemoveInFlight(key, load);
            }
        }

        private CardTextureLoadResult DecodeAndCache(
            string key,
            PrintingDefinition printing,
            PendingLoad load,
            ContentImageLoadResult image)
        {
            if (!image.Succeeded)
                return CardTextureLoadResult.Failure(image.Status, image.ErrorMessage);

            lock (gate)
            {
                string rejection = LoadRejection(load);
                if (rejection != null)
                    return CardTextureLoadResult.Failure(ContentImageLoadStatus.Failed, rejection);
            }

            Texture2D texture = null;
            long textureDecodedBytes;
            try
            {
                texture = decode(image.Data);
                textureDecodedBytes = texture == null ? 0L : Math.Max(1L, estimateDecodedBytes(texture));
            }
            catch (Exception exception)
            {
                DestroyTexture(texture);
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

            var evicted = new List<Texture2D>();
            string rejectedReason = null;
            Texture2D redundant = null;
            Texture2D resultTexture = texture;
            bool fromCache = false;
            lock (gate)
            {
                rejectedReason = LoadRejection(load);
                if (rejectedReason == null && textureDecodedBytes > maximumDecodedBytes)
                {
                    rejectedReason =
                        $"The decoded texture requires {textureDecodedBytes} bytes, exceeding the {maximumDecodedBytes}-byte cache budget.";
                }
                if (rejectedReason == null && entries.TryGetValue(key, out CacheEntry existing))
                {
                    redundant = texture;
                    resultTexture = existing.Texture;
                    fromCache = true;
                    Touch(existing);
                    load.Entry = existing;
                    existing.PendingLoadCount++;
                }
                else if (rejectedReason == null)
                {
                    var node = lru.AddFirst(key);
                    var entry = new CacheEntry
                    {
                        Texture = texture,
                        Node = node,
                        DecodedBytes = textureDecodedBytes,
                        PendingLoadCount = 1
                    };
                    entries.Add(key, entry);
                    // The load owns this pin until ALL registered waiters acquire a lease or cancel.
                    // Register it before trimming: a newly decoded texture is not yet unused.
                    load.Entry = entry;
                    decodedBytes += textureDecodedBytes;
                    TrimToBudget(evicted);
                }
            }

            DestroyTexture(redundant);
            if (rejectedReason != null)
            {
                DestroyTexture(texture);
                return CardTextureLoadResult.Failure(ContentImageLoadStatus.Failed, rejectedReason);
            }

            DestroyTextures(evicted);
            return CardTextureLoadResult.Success(resultTexture, fromCache);
        }

        private Texture2D RemoveLeastRecentlyUsed()
        {
            LinkedListNode<string> candidate = lru.Last;
            while (candidate != null)
            {
                LinkedListNode<string> previous = candidate.Previous;
                if (entries.TryGetValue(candidate.Value, out CacheEntry entry) &&
                    entry.LeaseCount == 0 && entry.PendingLoadCount == 0)
                {
                    RemoveEntry(candidate.Value, entry);
                    return entry.Texture;
                }

                candidate = previous;
            }

            return null;
        }

        private List<Texture2D> ClearEntriesAndInvalidateLoads()
        {
            lock (gate)
            {
                trimGeneration++;
                // Old completions must neither accept new callers nor unregister a newer load.
                inFlight.Clear();
                var textures = new List<Texture2D>(entries.Count);
                var removableKeys = new List<string>();
                foreach (KeyValuePair<string, CacheEntry> pair in entries)
                {
                    if (pair.Value.LeaseCount == 0)
                    {
                        removableKeys.Add(pair.Key);
                        textures.Add(pair.Value.Texture);
                    }
                    else
                    {
                        pair.Value.RemoveWhenReleased = true;
                    }
                }

                foreach (string key in removableKeys)
                {
                    if (entries.TryGetValue(key, out CacheEntry entry))
                        RemoveEntry(key, entry);
                }

                return textures;
            }
        }

        private List<Texture2D> RemoveAllEntries()
        {
            var textures = new List<Texture2D>(entries.Count);
            foreach (CacheEntry entry in entries.Values)
                textures.Add(entry.Texture);
            entries.Clear();
            lru.Clear();
            decodedBytes = 0L;
            return textures;
        }

        private void Touch(CacheEntry entry)
        {
            lru.Remove(entry.Node);
            lru.AddFirst(entry.Node);
        }

        private CardTextureLoadResult AcquireLease(string key, CacheEntry entry, bool fromCache)
        {
            entry.LeaseCount++;
            Texture2D texture = entry.Texture;
            return CardTextureLoadResult.Success(
                texture,
                fromCache,
                () => ReleaseLease(key, texture));
        }

        private CardTextureLoadResult TryAcquireLoadedTexture(
            string key,
            PendingLoad load,
            CardTextureLoadResult loaded)
        {
            if (loaded.Status != ContentImageLoadStatus.Succeeded)
                return loaded;

            lock (gate)
            {
                string rejection = LoadRejection(load);
                if (rejection != null)
                    return CardTextureLoadResult.Failure(ContentImageLoadStatus.Failed, rejection);

                if (entries.TryGetValue(key, out CacheEntry entry) &&
                    ReferenceEquals(entry, load.Entry) &&
                    entry.Texture == loaded.Texture)
                {
                    return AcquireLease(key, entry, loaded.FromCache);
                }
            }

            return CardTextureLoadResult.Failure(
                ContentImageLoadStatus.Failed,
                "The decoded texture was reclaimed before it could be displayed.");
        }

        private void ReleaseLease(string key, Texture2D texture)
        {
            var evicted = new List<Texture2D>();
            lock (gate)
            {
                if (!entries.TryGetValue(key, out CacheEntry entry) || entry.Texture != texture)
                    return;

                entry.LeaseCount = Math.Max(0, entry.LeaseCount - 1);
                CollectReleasedTextures(key, entry, evicted);
            }

            DestroyTextures(evicted);
        }

        private void ReleaseWaiter(string key, PendingLoad load)
        {
            var evicted = new List<Texture2D>();
            lock (gate)
            {
                load.WaiterCount--;
                if (load.WaiterCount != 0)
                    return;

                RemoveInFlight(key, load);
                CacheEntry entry = load.Entry;
                load.Entry = null;
                if (entry != null && entries.TryGetValue(key, out CacheEntry current) &&
                    ReferenceEquals(current, entry))
                {
                    entry.PendingLoadCount--;
                    CollectReleasedTextures(key, entry, evicted);
                }
            }

            DestroyTextures(evicted);
        }

        private void CollectReleasedTextures(string key, CacheEntry entry, List<Texture2D> evicted)
        {
            if (entry.LeaseCount == 0 && entry.PendingLoadCount == 0 && entry.RemoveWhenReleased)
            {
                RemoveEntry(key, entry);
                evicted.Add(entry.Texture);
            }

            TrimToBudget(evicted);
        }

        private void TrimToBudget(List<Texture2D> evicted)
        {
            while (entries.Count > capacity || decodedBytes > maximumDecodedBytes)
            {
                Texture2D removed = RemoveLeastRecentlyUsed();
                if (removed == null)
                    break;
                evicted.Add(removed);
            }
        }

        private void RemoveInFlight(string key, PendingLoad load)
        {
            if (inFlight.TryGetValue(key, out PendingLoad current) && ReferenceEquals(current, load))
                inFlight.Remove(key);
        }

        private string LoadRejection(PendingLoad load)
        {
            if (disposed)
                return "The texture cache was disposed during loading.";
            if (load.Generation != trimGeneration)
                return "The texture load was discarded after a memory-pressure trim.";
            if (load.WaiterCount == 0)
                return "The texture load no longer has any waiting callers.";
            return null;
        }

        private void RemoveEntry(string key, CacheEntry entry)
        {
            lru.Remove(entry.Node);
            entries.Remove(key);
            decodedBytes = Math.Max(0L, decodedBytes - entry.DecodedBytes);
        }

        private static string CacheKey(PrintingDefinition printing)
        {
            return $"{printing.ImageRelativePath}|{printing.ImageSha256}";
        }

        private static Texture2D DecodeTexture(byte[] data)
        {
            if (IsWebP(data))
            {
                Texture2D webp = Texture2DExt.CreateTexture2DFromWebP(
                    data,
                    lMipmaps: false,
                    lLinear: false,
                    lError: out Error error,
                    makeNoLongerReadable: true);
                if (error == Error.Success)
                    return webp;

                DestroyTexture(webp);
                return null;
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            if (ImageConversion.LoadImage(texture, data, true))
                return texture;

            DestroyTexture(texture);
            return null;
        }

        private static long EstimateDecodedBytes(Texture2D texture)
        {
            try
            {
                return checked((long)texture.width * texture.height * 4L);
            }
            catch (OverflowException)
            {
                return long.MaxValue;
            }
        }

        private static bool IsWebP(byte[] data)
        {
            return data != null &&
                   data.Length >= 12 &&
                   data[0] == (byte)'R' && data[1] == (byte)'I' &&
                   data[2] == (byte)'F' && data[3] == (byte)'F' &&
                   data[8] == (byte)'W' && data[9] == (byte)'E' &&
                   data[10] == (byte)'B' && data[11] == (byte)'P';
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

        private static void DestroyTextures(IEnumerable<Texture2D> textures)
        {
            foreach (Texture2D texture in textures)
                DestroyTexture(texture);
        }

        private async Task<CardTextureLoadResult> AwaitWithCancellationAndAcquire(
            string key,
            PendingLoad load,
            CancellationToken cancellationToken)
        {
            try
            {
                // Keep Unity texture work on the caller's context; cancellation callbacks only signal.
                Task<CardTextureLoadResult> task = load.Task;
                if (cancellationToken.CanBeCanceled)
                {
                    var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    using (cancellationToken.Register(() => cancelled.TrySetResult(true)))
                    {
                        if (task != await Task.WhenAny(task, cancelled.Task))
                            throw new OperationCanceledException(cancellationToken);
                    }
                }

                CardTextureLoadResult loaded = await task;
                cancellationToken.ThrowIfCancellationRequested();
                return TryAcquireLoadedTexture(key, load, loaded);
            }
            finally
            {
                ReleaseWaiter(key, load);
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(CardTextureCache));
        }
    }
}
