using System;
using System.Threading;
using System.Threading.Tasks;
using Gacha.Application;
using Gacha.Domain;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gacha.Presentation
{
    public enum AsyncCardImageState
    {
        Empty,
        Loading,
        Ready,
        Failed
    }

    public sealed class AsyncCardImageView : IDisposable
    {
        private readonly ICardTextureCache cache;
        private readonly VisualElement art;
        private readonly Label status;
        private readonly Button retry;
        private CancellationTokenSource cancellation;
        private IVisualElementScheduledItem pulse;
        private int requestVersion;
        private bool disposed;

        public AsyncCardImageView(ICardTextureCache cache)
        {
            this.cache = cache ?? throw new ArgumentNullException(nameof(cache));

            Element = new VisualElement { name = "async-card-image" };
            Element.AddToClassList("async-card-image");
            art = new VisualElement { name = "card-art" };
            art.AddToClassList("async-card-image__art");
            status = new Label { name = "card-image-status" };
            status.AddToClassList("async-card-image__status");
            retry = new Button(Retry) { name = "card-image-retry" };
            retry.AddToClassList("async-card-image__retry");
            Element.Add(art);
            Element.Add(status);
            Element.Add(retry);
            SetState(AsyncCardImageState.Empty);
        }

        public VisualElement Element { get; }
        public PrintingDefinition Printing { get; private set; }
        public AsyncCardImageState State { get; private set; }
        public Task CurrentLoadTask { get; private set; } = Task.CompletedTask;
        public bool RetryVisible => retry.style.display.value == DisplayStyle.Flex;
        public string StatusText => status.text;

        public void Bind(PrintingDefinition printing)
        {
            if (printing == null)
                throw new ArgumentNullException(nameof(printing));
            ThrowIfDisposed();

            Printing = printing;
            StartLoad(false);
        }

        public void Unbind()
        {
            requestVersion++;
            CancelCurrent();
            Printing = null;
            art.style.backgroundImage = new StyleBackground(StyleKeyword.None);
            StopPulse();
            SetState(AsyncCardImageState.Empty);
            CurrentLoadTask = Task.CompletedTask;
        }

        public void Retry()
        {
            if (disposed || Printing == null)
                return;

            UIFeedbackService.Play(FeedbackCue.Confirm);
            StartLoad(true);
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            UnbindInternal();
            retry.clicked -= Retry;
        }

        private void StartLoad(bool userInitiated)
        {
            CancelCurrent();
            cancellation = new CancellationTokenSource();
            int version = ++requestVersion;
            SetState(AsyncCardImageState.Loading);
            StartPulse();
            CurrentLoadTask = LoadAsync(Printing, version, userInitiated, cancellation.Token);
        }

        private async Task LoadAsync(
            PrintingDefinition printing,
            int version,
            bool userInitiated,
            CancellationToken cancellationToken)
        {
            CardTextureLoadResult result;
            try
            {
                result = await cache.LoadAsync(printing, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (disposed || cancellationToken.IsCancellationRequested ||
                version != requestVersion || !ReferenceEquals(Printing, printing))
            {
                return;
            }

            StopPulse();
            if (result.Succeeded)
            {
                art.style.backgroundImage = new StyleBackground(result.Texture);
                SetState(AsyncCardImageState.Ready);
                return;
            }

            status.text = FailureMessage(result.Status);
            SetState(AsyncCardImageState.Failed, preserveStatus: true);
            if (userInitiated)
                UIFeedbackService.Play(FeedbackCue.Error);
        }

        private void SetState(AsyncCardImageState state, bool preserveStatus = false)
        {
            State = state;
            retry.text = Localized("Retry", "重试");
            Element.EnableInClassList("is-loading", state == AsyncCardImageState.Loading);
            Element.EnableInClassList("is-ready", state == AsyncCardImageState.Ready);
            Element.EnableInClassList("is-failed", state == AsyncCardImageState.Failed);
            retry.style.display = state == AsyncCardImageState.Failed ? DisplayStyle.Flex : DisplayStyle.None;

            if (!preserveStatus)
            {
                status.text = state == AsyncCardImageState.Loading
                    ? Localized("Loading…", "加载中…")
                    : string.Empty;
            }
        }

        private void StartPulse()
        {
            StopPulse();
            if (UIFeedbackService.ReduceMotion)
                return;

            pulse = Element.schedule.Execute(() =>
            {
                float speed = UIFeedbackService.AnimationSpeed;
                float wave = 0.5f + 0.5f * Mathf.Sin(Time.realtimeSinceStartup * 5f * speed);
                art.style.opacity = Mathf.Lerp(0.38f, 0.82f, wave);
            }).Every(50);
        }

        private void StopPulse()
        {
            pulse?.Pause();
            pulse = null;
            art.style.opacity = 1f;
        }

        private void CancelCurrent()
        {
            if (cancellation == null)
                return;
            cancellation.Cancel();
            cancellation.Dispose();
            cancellation = null;
        }

        private void UnbindInternal()
        {
            requestVersion++;
            CancelCurrent();
            Printing = null;
            StopPulse();
            CurrentLoadTask = Task.CompletedTask;
        }

        private static string FailureMessage(ContentImageLoadStatus status)
        {
            switch (status)
            {
                case ContentImageLoadStatus.InvalidPath:
                    return Localized("Invalid image path", "图片路径无效");
                case ContentImageLoadStatus.NotFound:
                    return Localized("Image not installed", "卡图尚未安装");
                case ContentImageLoadStatus.IntegrityMismatch:
                    return Localized("Image verification failed", "卡图校验失败");
                default:
                    return Localized("Image loading failed", "卡图加载失败");
            }
        }

        private static string Localized(string english, string chinese)
        {
            return ApplicationServices.IsConfigured &&
                   ApplicationServices.Languages.UiLanguageId.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
                ? chinese
                : english;
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(AsyncCardImageView));
        }
    }
}
