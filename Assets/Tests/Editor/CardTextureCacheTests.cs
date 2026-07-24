using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Gacha.Application;
using Gacha.Domain;
using Gacha.Presentation;
using NUnit.Framework;
using UnityEngine;

public class CardTextureCacheTests
{
    private sealed class ImageSource : IContentImageSource
    {
        private readonly Dictionary<string, TaskCompletionSource<ContentImageLoadResult>> pending =
            new Dictionary<string, TaskCompletionSource<ContentImageLoadResult>>();

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
            pending.Add(relativePath, completion);
            return completion.Task;
        }

        public void Complete(string relativePath, byte[] bytes)
        {
            pending[relativePath].SetResult(ContentImageLoadResult.Success(relativePath, bytes, "hash"));
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
            await cache.LoadAsync(CreatePrinting("one"));
            CardTextureLoadResult third = await cache.LoadAsync(CreatePrinting("three"));

            Assert.That(cache.Count, Is.EqualTo(2));
            Assert.That(first.Texture, Is.Not.Null);
            Assert.That(second.Texture == null, Is.True);
            Assert.That(third.Texture, Is.Not.Null);
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
            Assert.That((await retained).Succeeded, Is.True);
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
                Assert.That(cache.InFlightCount, Is.Zero);
            }

            Assert.That(source.Calls, Is.EqualTo(256));
            Assert.That(cache.Count, Is.EqualTo(32));
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
