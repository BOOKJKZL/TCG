using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Gacha.Application;
using Gacha.Domain;
using Gacha.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

public class AsyncCardImageViewTests
{
    private sealed class TextureCache : ICardTextureCache
    {
        public sealed class Request
        {
            public PrintingDefinition Printing;
            public CancellationToken CancellationToken;
            public TaskCompletionSource<CardTextureLoadResult> Completion;
        }

        public readonly List<Request> Requests = new List<Request>();

        public Task<CardTextureLoadResult> LoadAsync(
            PrintingDefinition printing,
            CancellationToken cancellationToken = default)
        {
            var request = new Request
            {
                Printing = printing,
                CancellationToken = cancellationToken,
                Completion = new TaskCompletionSource<CardTextureLoadResult>()
            };
            Requests.Add(request);
            return request.Completion.Task;
        }
    }

    [Test]
    public async Task Bind_IgnoresOldResultAfterVirtualizedElementIsReused()
    {
        var cache = new TextureCache();
        var view = new AsyncCardImageView(cache);
        var texture = new Texture2D(2, 2);
        int staleReleaseCount = 0;
        int currentReleaseCount = 0;

        try
        {
            view.Bind(CreatePrinting("one"));
            Task firstLoad = view.CurrentLoadTask;
            view.Bind(CreatePrinting("two"));
            Task secondLoad = view.CurrentLoadTask;

            Assert.That(cache.Requests[0].CancellationToken.IsCancellationRequested, Is.True);
            cache.Requests[0].Completion.SetResult(CardTextureLoadResult.Success(
                texture,
                false,
                () => staleReleaseCount++));
            await firstLoad;
            Assert.That(view.State, Is.EqualTo(AsyncCardImageState.Loading));
            Assert.That(view.Printing.Id, Is.EqualTo("two"));
            Assert.That(staleReleaseCount, Is.EqualTo(1));

            cache.Requests[1].Completion.SetResult(CardTextureLoadResult.Success(
                texture,
                false,
                () => currentReleaseCount++));
            await secondLoad;
            Assert.That(view.State, Is.EqualTo(AsyncCardImageState.Ready));
            Assert.That(view.RetryVisible, Is.False);
            Assert.That(currentReleaseCount, Is.Zero);
            view.Unbind();
            Assert.That(currentReleaseCount, Is.EqualTo(1));
        }
        finally
        {
            view.Dispose();
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    [Test]
    public async Task CurrentLease_IsReleasedExactlyOnceOnRebindAndDispose()
    {
        var cache = new TextureCache();
        var view = new AsyncCardImageView(cache);
        var texture = new Texture2D(2, 2);
        int firstReleaseCount = 0;
        int secondReleaseCount = 0;

        try
        {
            view.Bind(CreatePrinting("one"));
            cache.Requests[0].Completion.SetResult(CardTextureLoadResult.Success(
                texture,
                false,
                () => firstReleaseCount++));
            await view.CurrentLoadTask;

            view.Bind(CreatePrinting("two"));
            Assert.That(firstReleaseCount, Is.EqualTo(1));
            cache.Requests[1].Completion.SetResult(CardTextureLoadResult.Success(
                texture,
                false,
                () => secondReleaseCount++));
            await view.CurrentLoadTask;

            view.Dispose();
            view.Dispose();
            Assert.That(firstReleaseCount, Is.EqualTo(1));
            Assert.That(secondReleaseCount, Is.EqualTo(1));
        }
        finally
        {
            view.Dispose();
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    [Test]
    public async Task Retry_PlaysConfirmAndOnlyPlaysErrorAfterUserFailure()
    {
        var cache = new TextureCache();
        var view = new AsyncCardImageView(cache);
        var cues = new List<FeedbackCue>();
        UIFeedbackService.FeedbackPlayed += cues.Add;

        try
        {
            view.Bind(CreatePrinting("one"));
            cache.Requests[0].Completion.SetResult(CardTextureLoadResult.Failure(
                ContentImageLoadStatus.NotFound,
                "missing"));
            await view.CurrentLoadTask;

            Assert.That(view.State, Is.EqualTo(AsyncCardImageState.Failed));
            Assert.That(view.RetryVisible, Is.True);
            Assert.That(view.Element.Q<VisualElement>("card-image-retry").style.display.value,
                Is.EqualTo(DisplayStyle.Flex));
            Assert.That(cues, Is.Empty);

            view.Retry();
            cache.Requests[1].Completion.SetResult(CardTextureLoadResult.Failure(
                ContentImageLoadStatus.IntegrityMismatch,
                "bad hash"));
            await view.CurrentLoadTask;

            Assert.That(cues, Is.EqualTo(new[] { FeedbackCue.Confirm, FeedbackCue.Error }));
            Assert.That(view.StatusText, Is.Not.Empty);
            Assert.That(view.Element.Q<VisualElement>("card-image-retry").style.display.value,
                Is.EqualTo(DisplayStyle.Flex));
        }
        finally
        {
            UIFeedbackService.FeedbackPlayed -= cues.Add;
            view.Dispose();
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
