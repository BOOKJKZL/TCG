using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Gacha.Application;
using Gacha.Domain;
using Gacha.Presentation;
using NUnit.Framework;
using UnityEngine;
using WebP;

public class CardTextureCacheTests
{
    private sealed class ImageSource : IContentImageSource
    {
        private readonly Dictionary<string, List<TaskCompletionSource<ContentImageLoadResult>>> pending =
            new Dictionary<string, List<TaskCompletionSource<ContentImageLoadResult>>>();

        public int Calls { get; private set; }
        public byte[] ImmediateBytes { get; set; }

        public Task<ContentImageLoadResult> LoadAsync(
            string relativePath,
            string expectedSha256 = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            if (ImmediateBytes != null)
                return Task.FromResult(ContentImageLoadResult.Success(relativePath, ImmediateBytes, "hash"));

            var completion = new TaskCompletionSource<ContentImageLoadResult>();
            if (!pending.TryGetValue(relativePath, out List<TaskCompletionSource<ContentImageLoadResult>> requests))
            {
                requests = new List<TaskCompletionSource<ContentImageLoadResult>>();
                pending.Add(relativePath, requests);
            }
            requests.Add(completion);
            return completion.Task;
        }

        public void Complete(string relativePath, byte[] bytes, int requestIndex = 0)
        {
            TakeNext(relativePath, requestIndex).SetResult(ContentImageLoadResult.Success(relativePath, bytes, "hash"));
        }

        public void Fail(string relativePath)
        {
            TakeNext(relativePath).SetException(new InvalidOperationException("source failure"));
        }

        private TaskCompletionSource<ContentImageLoadResult> TakeNext(string relativePath, int requestIndex = 0)
        {
            List<TaskCompletionSource<ContentImageLoadResult>> requests = pending[relativePath];
            TaskCompletionSource<ContentImageLoadResult> next = requests[requestIndex];
            requests.RemoveAt(requestIndex);
            if (requests.Count == 0)
                pending.Remove(relativePath);
            return next;
        }
    }

    private static byte[] pngBytes;

    [OneTimeSetUp]
    public void CreateImageBytes()
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        texture.SetPixels(new[] { Color.red, Color.green, Color.blue, Color.white });
        texture.Apply();
        pngBytes = texture.EncodeToPNG();
        UnityEngine.Object.DestroyImmediate(texture);
    }

    [Test]
    public async Task LoadAsync_DecodesWebPImages()
    {
        var sourceTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        sourceTexture.SetPixels(new[] { Color.red, Color.green, Color.blue, Color.white });
        sourceTexture.Apply();
        byte[] webpBytes = sourceTexture.EncodeToWebP(80f, out Error encodeError);
        UnityEngine.Object.DestroyImmediate(sourceTexture);
        Assert.That(encodeError, Is.EqualTo(Error.Success));

        var source = new ImageSource { ImmediateBytes = webpBytes };
        using (var cache = new CardTextureCache(source, 2))
        {
            CardTextureLoadResult result = await cache.LoadAsync(CreatePrinting("webp"));

            Assert.That(result.Succeeded, Is.True, result.ErrorMessage);
            Assert.That(result.Texture.width, Is.EqualTo(2));
            Assert.That(result.Texture.height, Is.EqualTo(2));
            Assert.That(cache.Count, Is.EqualTo(1));
            result.Dispose();
        }
    }

    [Test]
    public async Task LoadAsync_CoalescesDuplicateRequestsAndCachesTexture()
    {
        var source = new ImageSource();
        using (var cache = new CardTextureCache(source, 2))
        {
            PrintingDefinition printing = CreatePrinting("one");
            Task<CardTextureLoadResult> first = cache.LoadAsync(printing);
            Task<CardTextureLoadResult> second = cache.LoadAsync(printing);
            await Task.Yield();

            Assert.That(source.Calls, Is.EqualTo(1));
            Assert.That(cache.InFlightCount, Is.EqualTo(1));
            source.Complete(printing.ImageRelativePath, pngBytes);

            CardTextureLoadResult firstResult = await first;
            CardTextureLoadResult secondResult = await second;
            CardTextureLoadResult cachedResult = await cache.LoadAsync(printing);

            Assert.That(firstResult.Succeeded, Is.True);
            Assert.That(secondResult.Texture, Is.SameAs(firstResult.Texture));
            Assert.That(cachedResult.Texture, Is.SameAs(firstResult.Texture));
            Assert.That(cachedResult.FromCache, Is.True);
            Assert.That(source.Calls, Is.EqualTo(1));
            Assert.That(cache.Count, Is.EqualTo(1));
            firstResult.Dispose();
            secondResult.Dispose();
            cachedResult.Dispose();
        }
    }

    [Test]
    public async Task LoadAsync_EvictsLeastRecentlyUsedTexture()
    {
        var source = new ImageSource { ImmediateBytes = pngBytes };
        using (var cache = new CardTextureCache(source, 2))
        {
            CardTextureLoadResult first = await cache.LoadAsync(CreatePrinting("one"));
            CardTextureLoadResult second = await cache.LoadAsync(CreatePrinting("two"));
            Texture2D firstTexture = first.Texture;
            Texture2D secondTexture = second.Texture;
            first.Dispose();
            second.Dispose();
            CardTextureLoadResult touched = await cache.LoadAsync(CreatePrinting("one"));
            touched.Dispose();
            CardTextureLoadResult third = await cache.LoadAsync(CreatePrinting("three"));

            Assert.That(cache.Count, Is.EqualTo(2));
            Assert.That(firstTexture, Is.Not.Null);
            Assert.That(secondTexture == null, Is.True);
            Assert.That(third.Texture, Is.Not.Null);
            third.Dispose();
        }
    }

    [Test]
    public async Task LoadAsync_EvictsLeastRecentlyUsedTexturesToStayWithinDecodedByteBudget()
    {
        var source = new ImageSource { ImmediateBytes = pngBytes };
        using (var cache = new CardTextureCache(
                   source,
                   capacity: 8,
                   maximumDecodedBytes: 32L,
                   decodedSizeEstimator: _ => 16L))
        {
            CardTextureLoadResult first = await cache.LoadAsync(CreatePrinting("byte-one"));
            CardTextureLoadResult second = await cache.LoadAsync(CreatePrinting("byte-two"));
            Texture2D firstTexture = first.Texture;
            Texture2D secondTexture = second.Texture;
            first.Dispose();
            second.Dispose();
            CardTextureLoadResult third = await cache.LoadAsync(CreatePrinting("byte-three"));

            Assert.That(cache.Count, Is.EqualTo(2));
            Assert.That(cache.DecodedBytes, Is.EqualTo(32L));
            Assert.That(cache.DecodedBytes, Is.LessThanOrEqualTo(cache.MaximumDecodedBytes));
            Assert.That(firstTexture == null, Is.True);
            Assert.That(secondTexture, Is.Not.Null);
            Assert.That(third.Texture, Is.Not.Null);
            third.Dispose();
        }
    }

    [Test]
    public async Task LoadAsync_RejectsSingleTextureThatExceedsDecodedByteBudget()
    {
        var source = new ImageSource { ImmediateBytes = pngBytes };
        using (var cache = new CardTextureCache(
                   source,
                   capacity: 8,
                   maximumDecodedBytes: 63L,
                   decodedSizeEstimator: _ => 64L))
        {
            CardTextureLoadResult result = await cache.LoadAsync(CreatePrinting("oversized"));

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("exceeding"));
            Assert.That(result.Texture, Is.Null);
            Assert.That(cache.Count, Is.Zero);
            Assert.That(cache.DecodedBytes, Is.Zero);
        }
    }

    [Test]
    public async Task TrimForMemoryPressure_PreservesDisplayedTexturesUntilLeasesAreReleased()
    {
        var source = new ImageSource { ImmediateBytes = pngBytes };
        using (var cache = new CardTextureCache(
                   source,
                   capacity: 8,
                   maximumDecodedBytes: 64L,
                   decodedSizeEstimator: _ => 16L))
        {
            CardTextureLoadResult first = await cache.LoadAsync(CreatePrinting("trim-one"));
            CardTextureLoadResult second = await cache.LoadAsync(CreatePrinting("trim-two"));

            cache.TrimForMemoryPressure();

            Assert.That(cache.Count, Is.EqualTo(2));
            Assert.That(cache.DecodedBytes, Is.EqualTo(32L));
            Assert.That(first.Texture, Is.Not.Null);
            Assert.That(second.Texture, Is.Not.Null);

            Texture2D firstTexture = first.Texture;
            Texture2D secondTexture = second.Texture;
            first.Dispose();
            second.Dispose();

            Assert.That(cache.Count, Is.Zero);
            Assert.That(cache.DecodedBytes, Is.Zero);
            Assert.That(firstTexture == null, Is.True);
            Assert.That(secondTexture == null, Is.True);
        }
    }

    [Test]
    public async Task TrimForMemoryPressure_DiscardsLoadThatStartedBeforeTrim()
    {
        var source = new ImageSource();
        using (var cache = new CardTextureCache(source, 8))
        {
            PrintingDefinition printing = CreatePrinting("trim-in-flight");
            Task<CardTextureLoadResult> pending = cache.LoadAsync(printing);
            await Task.Yield();
            Assert.That(cache.InFlightCount, Is.EqualTo(1));

            cache.TrimForMemoryPressure();
            source.Complete(printing.ImageRelativePath, pngBytes);
            CardTextureLoadResult result = await pending;

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("memory-pressure"));
            Assert.That(cache.Count, Is.Zero);
            Assert.That(cache.DecodedBytes, Is.Zero);
            Assert.That(cache.InFlightCount, Is.Zero);
        }
    }

    [Test]
    public async Task LoadAsync_CancelsCallerWithoutCancellingSharedLoad()
    {
        var source = new ImageSource();
        using (var cache = new CardTextureCache(source, 2))
        using (var cancellation = new CancellationTokenSource())
        {
            PrintingDefinition printing = CreatePrinting("one");
            Task<CardTextureLoadResult> cancelled = cache.LoadAsync(printing, cancellation.Token);
            Task<CardTextureLoadResult> retained = cache.LoadAsync(printing);
            cancellation.Cancel();

            bool cancellationObserved = false;
            try
            {
                await cancelled;
            }
            catch (OperationCanceledException)
            {
                cancellationObserved = true;
            }

            Assert.That(cancellationObserved, Is.True);
            Assert.That(retained.IsCompleted, Is.False);
            Assert.That(source.Calls, Is.EqualTo(1));
            source.Complete(printing.ImageRelativePath, pngBytes);
            CardTextureLoadResult retainedResult = await retained;
            Assert.That(retainedResult.Succeeded, Is.True);
            retainedResult.Dispose();
        }
    }

    [Test]
    public async Task LoadAsync_PinsNewTextureUntilDeliveredAndReturnsToUnchangedBudgetOnRelease()
    {
        var source = new ImageSource { ImmediateBytes = pngBytes };
        using (var cache = new CardTextureCache(source, capacity: 1, maximumDecodedBytes: 16L))
        using (CardTextureLoadResult first = await cache.LoadAsync(CreatePrinting("pinned-a")))
        {
            CardTextureLoadResult second = await cache.LoadAsync(CreatePrinting("pinned-b"));
            Assert.That(second.Succeeded, Is.True, second.ErrorMessage);
            Texture2D secondTexture = second.Texture;
            Assert.That(first.Texture == null, Is.False);
            Assert.That(cache.Capacity, Is.EqualTo(1));
            Assert.That(cache.MaximumDecodedBytes, Is.EqualTo(16L));
            Assert.That(cache.Count, Is.EqualTo(2), "Only live leases may temporarily exceed the soft cache budget.");
            Assert.That(cache.DecodedBytes, Is.EqualTo(32L));

            second.Dispose();
            second.Dispose();
            Assert.That(secondTexture == null, Is.True);
            Assert.That(first.Texture == null, Is.False);
            Assert.That(cache.Count, Is.EqualTo(1));
            Assert.That(cache.DecodedBytes, Is.EqualTo(16L));
            Assert.That(cache.InFlightCount, Is.Zero);
        }
    }

    [Test]
    public async Task LoadAsync_FirstConsumerReleaseDoesNotReclaimOtherConsumersTexture()
    {
        var source = new ImageSource { ImmediateBytes = pngBytes };
        using (var cache = new CardTextureCache(source, capacity: 1, maximumDecodedBytes: 16L))
        using (CardTextureLoadResult pinned = await cache.LoadAsync(CreatePrinting("shared-a")))
        {
            source.ImmediateBytes = null;
            PrintingDefinition printing = CreatePrinting("shared-b");
            Task<CardTextureLoadResult> first = cache.LoadAsync(printing);
            Task<Texture2D> released = ReleaseWhenDelivered(first);
            Task<CardTextureLoadResult> second = cache.LoadAsync(printing);
            await Task.Yield();
            source.Complete(printing.ImageRelativePath, pngBytes);

            Texture2D firstTexture = await released;
            using (CardTextureLoadResult retained = await second)
            {
                Assert.That(retained.Succeeded, Is.True, retained.ErrorMessage);
                Assert.That(firstTexture == null, Is.False);
                Assert.That(retained.Texture, Is.SameAs(firstTexture));
                Assert.That(source.Calls, Is.EqualTo(2));
                Assert.That(cache.Count, Is.EqualTo(2));
            }
            Assert.That(firstTexture == null, Is.True);
            Assert.That(cache.DecodedBytes, Is.EqualTo(16L));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task LoadAsync_CancelledWaiterCannotReclaimOtherConsumersTexture(bool cancelDuringDecode)
    {
        var source = new ImageSource { ImmediateBytes = pngBytes };
        using (var cancellation = new CancellationTokenSource())
        using (var cache = new CardTextureCache(source, capacity: 1, maximumDecodedBytes: 16L,
                   decoder: bytes =>
                   {
                       if (cancelDuringDecode && source.Calls == 2)
                           cancellation.Cancel();
                       return new Texture2D(2, 2, TextureFormat.RGBA32, false);
                   }))
        using (CardTextureLoadResult pinned = await cache.LoadAsync(CreatePrinting("cancel-a")))
        {
            source.ImmediateBytes = null;
            PrintingDefinition printing = CreatePrinting("cancel-b");
            Task<CardTextureLoadResult> cancelled = cache.LoadAsync(printing, cancellation.Token);
            Task<CardTextureLoadResult> retained = cache.LoadAsync(printing);
            await Task.Yield();
            if (!cancelDuringDecode)
                cancellation.Cancel();
            source.Complete(printing.ImageRelativePath, pngBytes);

            await AssertCancelled(cancelled);
            using (CardTextureLoadResult result = await retained)
            {
                Assert.That(result.Succeeded, Is.True, result.ErrorMessage);
                Assert.That(pinned.Texture == null, Is.False);
                Assert.That(source.Calls, Is.EqualTo(2));
                Assert.That(cache.Count, Is.EqualTo(2));
            }
            Assert.That(cache.Count, Is.EqualTo(1));
            Assert.That(cache.DecodedBytes, Is.EqualTo(16L));
            Assert.That(cache.InFlightCount, Is.Zero);
        }
    }

    [Test]
    public async Task LoadAsync_AllWaitersCancelDuringDecodeReleasesUndeliveredTexture()
    {
        var source = new ImageSource { ImmediateBytes = pngBytes };
        Texture2D undeliveredTexture = null;
        using (var firstCancellation = new CancellationTokenSource())
        using (var secondCancellation = new CancellationTokenSource())
        using (var cache = new CardTextureCache(source, capacity: 1, maximumDecodedBytes: 16L,
                   decoder: bytes =>
                   {
                       var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                       if (source.Calls == 2)
                       {
                           undeliveredTexture = texture;
                           firstCancellation.Cancel();
                           secondCancellation.Cancel();
                       }
                       return texture;
                   }))
        using (CardTextureLoadResult pinned = await cache.LoadAsync(CreatePrinting("all-cancel-a")))
        {
            PrintingDefinition printing = CreatePrinting("all-cancel-b");
            Task<CardTextureLoadResult> first = cache.LoadAsync(printing, firstCancellation.Token);
            Task<CardTextureLoadResult> second = cache.LoadAsync(printing, secondCancellation.Token);
            await AssertCancelled(first);
            await AssertCancelled(second);

            Assert.That(ReferenceEquals(undeliveredTexture, null), Is.False, "The decoder must have run.");
            Assert.That(undeliveredTexture == null, Is.True, "The last waiter must release the delivery pin.");
            Assert.That(pinned.Texture == null, Is.False);
            Assert.That(cache.Count, Is.EqualTo(1));
            Assert.That(cache.DecodedBytes, Is.EqualTo(16L));
            Assert.That(cache.InFlightCount, Is.Zero);
        }
    }

    [Test]
    public async Task LoadAsync_AllWaitersCancelDetachesAbandonedLoadAndAllowsSameKeyRetry()
    {
        var source = new ImageSource { ImmediateBytes = pngBytes };
        int decoded = 0;
        using (var cancellation = new CancellationTokenSource())
        using (var cache = new CardTextureCache(source, capacity: 1, maximumDecodedBytes: 16L,
                   decoder: bytes =>
                   {
                       decoded++;
                       return new Texture2D(2, 2, TextureFormat.RGBA32, false);
                   }))
        using (CardTextureLoadResult pinned = await cache.LoadAsync(CreatePrinting("abandon-a")))
        {
            source.ImmediateBytes = null;
            PrintingDefinition printing = CreatePrinting("abandon-b");
            Task<CardTextureLoadResult> first = cache.LoadAsync(printing, cancellation.Token);
            Task<CardTextureLoadResult> second = cache.LoadAsync(printing, cancellation.Token);
            await Task.Yield();
            cancellation.Cancel();
            await AssertCancelled(first);
            await AssertCancelled(second);
            Assert.That(cache.InFlightCount, Is.Zero);
            Assert.That(cache.Count, Is.EqualTo(1));
            Assert.That(cache.DecodedBytes, Is.EqualTo(16L));

            Task<CardTextureLoadResult> retry = cache.LoadAsync(printing);
            await Task.Yield();
            source.Complete(printing.ImageRelativePath, pngBytes); // Abandoned generation finishes first.
            await Task.Yield();
            Assert.That(decoded, Is.EqualTo(1), "No callers remain for the abandoned bytes.");
            Assert.That(cache.InFlightCount, Is.EqualTo(1), "The old finally must not unregister the retry.");
            Assert.That(retry.IsCompleted, Is.False);
            source.Complete(printing.ImageRelativePath, pngBytes);
            using (CardTextureLoadResult result = await retry)
                Assert.That(result.Succeeded, Is.True, result.ErrorMessage);
            Assert.That(decoded, Is.EqualTo(2));
            Assert.That(source.Calls, Is.EqualTo(3));
            Assert.That(cache.InFlightCount, Is.Zero);
            Assert.That(cache.DecodedBytes, Is.EqualTo(16L));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ClearOrTrim_OldCompletionCannotAcquireOrUnregisterNewSameKeyLoad(bool trim)
    {
        var source = new ImageSource();
        using (var cache = new CardTextureCache(source, 1))
        {
            PrintingDefinition printing = CreatePrinting("new-generation");
            Task<CardTextureLoadResult> old = cache.LoadAsync(printing);
            await Task.Yield();
            if (trim)
                cache.TrimForMemoryPressure();
            else
                cache.Clear();
            Assert.That(cache.InFlightCount, Is.Zero);

            Task<CardTextureLoadResult> current = cache.LoadAsync(printing);
            await Task.Yield();
            source.Complete(printing.ImageRelativePath, pngBytes);
            CardTextureLoadResult rejected = await old;
            Assert.That(rejected.Succeeded, Is.False);
            Assert.That(rejected.Texture, Is.Null);
            Assert.That(cache.Count, Is.Zero);
            Assert.That(cache.InFlightCount, Is.EqualTo(1));
            Assert.That(current.IsCompleted, Is.False);

            Task<CardTextureLoadResult> joined = cache.LoadAsync(printing);
            source.Complete(printing.ImageRelativePath, pngBytes);
            using (CardTextureLoadResult result = await current)
            using (CardTextureLoadResult other = await joined)
            {
                Assert.That(result.Succeeded, Is.True, result.ErrorMessage);
                Assert.That(other.Texture, Is.SameAs(result.Texture));
                Assert.That(source.Calls, Is.EqualTo(2));
                Assert.That(cache.InFlightCount, Is.Zero);
            }
        }
    }

    [Test]
    public async Task Clear_OldWaiterCannotAcquireCompletedNewSameKeyTexture()
    {
        var source = new ImageSource();
        using (var cache = new CardTextureCache(source, 1))
        {
            PrintingDefinition printing = CreatePrinting("new-first");
            Task<CardTextureLoadResult> old = cache.LoadAsync(printing);
            await Task.Yield();
            cache.Clear();
            Task<CardTextureLoadResult> current = cache.LoadAsync(printing);
            await Task.Yield();
            source.Complete(printing.ImageRelativePath, pngBytes, requestIndex: 1);
            using (CardTextureLoadResult result = await current)
            {
                Assert.That(result.Succeeded, Is.True, result.ErrorMessage);
                source.Complete(printing.ImageRelativePath, pngBytes);
                CardTextureLoadResult rejected = await old;
                Assert.That(rejected.Succeeded, Is.False);
                Assert.That(rejected.Texture, Is.Null);
                Assert.That(result.Texture == null, Is.False);
                Assert.That(cache.Count, Is.EqualTo(1));
                Assert.That(cache.InFlightCount, Is.Zero);
            }
        }
    }

    [Test]
    public async Task Dispose_LateSourceCompletionCannotDecodeOrRepopulateCache()
    {
        var source = new ImageSource();
        int decoded = 0;
        using (var cache = new CardTextureCache(source, decoder: bytes =>
               {
                   decoded++;
                   return new Texture2D(2, 2, TextureFormat.RGBA32, false);
               }))
        {
            PrintingDefinition printing = CreatePrinting("dispose-pending");
            Task<CardTextureLoadResult> pending = cache.LoadAsync(printing);
            await Task.Yield();
            cache.Dispose();
            Assert.That(cache.InFlightCount, Is.Zero);
            source.Complete(printing.ImageRelativePath, pngBytes);
            CardTextureLoadResult result = await pending;
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("disposed"));
            Assert.That(decoded, Is.Zero);
            Assert.That(cache.Count, Is.Zero);
            Assert.That(cache.DecodedBytes, Is.Zero);
            Assert.Throws<ObjectDisposedException>(() => cache.LoadAsync(printing));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ClearOrDispose_DuringDecodeDestroysRejectedTexture(bool dispose)
    {
        var source = new ImageSource { ImmediateBytes = pngBytes };
        CardTextureCache cache = null;
        Texture2D rejectedTexture = null;
        cache = new CardTextureCache(source, decoder: bytes =>
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (source.Calls == 1)
            {
                rejectedTexture = texture;
                if (dispose)
                    cache.Dispose();
                else
                    cache.Clear();
            }
            return texture;
        });
        using (cache)
        {
            PrintingDefinition printing = CreatePrinting("invalidate-decode");
            CardTextureLoadResult result = await cache.LoadAsync(printing);
            Assert.That(result.Succeeded, Is.False);
            Assert.That(rejectedTexture == null, Is.True);
            Assert.That(cache.Count, Is.Zero);
            Assert.That(cache.DecodedBytes, Is.Zero);
            Assert.That(cache.InFlightCount, Is.Zero);
            if (!dispose)
            {
                using (CardTextureLoadResult retry = await cache.LoadAsync(printing))
                    Assert.That(retry.Succeeded, Is.True, retry.ErrorMessage);
            }
        }
    }

    [Test]
    public async Task LoadAsync_SourceFailureRemovesPendingLoadAndAllowsRetry()
    {
        var source = new ImageSource();
        using (var cache = new CardTextureCache(source, 1))
        {
            PrintingDefinition printing = CreatePrinting("source-failure");
            Task<CardTextureLoadResult> pending = cache.LoadAsync(printing);
            await Task.Yield();
            source.Fail(printing.ImageRelativePath);
            CardTextureLoadResult failure = await pending;
            Assert.That(failure.Succeeded, Is.False);
            Assert.That(failure.ErrorMessage, Does.Contain("source failure"));
            Assert.That(cache.InFlightCount, Is.Zero);
            Assert.That(cache.Count, Is.Zero);
            source.ImmediateBytes = pngBytes;
            using (CardTextureLoadResult retry = await cache.LoadAsync(printing))
                Assert.That(retry.Succeeded, Is.True, retry.ErrorMessage);
        }
    }

    [Test]
    [Category("Performance")]
    public async Task LoadAsync_LargeCollectionKeepsTextureWorkingSetAtCapacity()
    {
        var source = new ImageSource { ImmediateBytes = pngBytes };
        using (var cache = new CardTextureCache(source, 32))
        {
            for (int index = 0; index < 256; index++)
            {
                CardTextureLoadResult result = await cache.LoadAsync(CreatePrinting($"large-{index}"));
                Assert.That(result.Succeeded, Is.True);
                Assert.That(cache.Count, Is.LessThanOrEqualTo(32));
                Assert.That(cache.DecodedBytes, Is.LessThanOrEqualTo(cache.MaximumDecodedBytes));
                Assert.That(cache.InFlightCount, Is.Zero);
                result.Dispose();
            }

            Assert.That(source.Calls, Is.EqualTo(256));
            Assert.That(cache.Count, Is.EqualTo(32));
        }
    }

    private static async Task<Texture2D> ReleaseWhenDelivered(Task<CardTextureLoadResult> task)
    {
        using (CardTextureLoadResult result = await task)
        {
            Assert.That(result.Succeeded, Is.True, result.ErrorMessage);
            return result.Texture;
        }
    }

    private static async Task AssertCancelled(Task<CardTextureLoadResult> task)
    {
        try
        {
            using (await task)
                Assert.Fail("Expected the caller to observe cancellation.");
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static PrintingDefinition CreatePrinting(string id)
    {
        return new PrintingDefinition(
            id,
            $"item-{id}",
            new PrintingIdentity("game", "set", id, "en", "normal"),
            "common",
            new Dictionary<string, string> { ["en"] = id },
            $"en/set/images/{id}.png",
            $"hash-{id}");
    }
}
