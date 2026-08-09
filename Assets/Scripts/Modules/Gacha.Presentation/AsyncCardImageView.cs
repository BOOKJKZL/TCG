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
        private readonly VisualElement retry;
        private readonly MobileActionControl retryAction;
        private CancellationTokenSource cancellation;
        private CardTextureLoadResult textureLease;
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
            retry = new VisualElement { name = "card-image-retry" };
            retry.AddToClassList("async-card-image__retry");
            var retryLabel = new Label { pickingMode = PickingMode.Ignore };
            retryLabel.AddToClassList("async-card-image__retry-label");
            retry.Add(retryLabel);
            retryAction = new MobileActionControl(
                retry,
                Retry,
                playFeedback: false,
                showPressWhenUnavailable: false,
                fallbackLabelClass: "async-card-image__retry-label");
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

            ReleaseTexture();
            Printing = printing;
            StartLoad(false);
        }

        public void Unbind()
        {
            requestVersion++;
            CancelCurrent();
            ReleaseTexture();
            Printing = null;
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
            retryAction.Dispose();
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
                result.Dispose();
                return;
            }

            StopPulse();
            if (result.Succeeded)
            {
                ReleaseTexture();
                textureLease = result;
                art.style.backgroundImage = new StyleBackground(result.Texture);
                SetState(AsyncCardImageState.Ready);
                return;
            }

            result.Dispose();
            status.text = FailureMessage(result.Status);
            SetState(AsyncCardImageState.Failed, preserveStatus: true);
            if (userInitiated)
                UIFeedbackService.Play(FeedbackCue.Error);
        }

        private void SetState(AsyncCardImageState state, bool preserveStatus = false)
        {
            State = state;
            retryAction.SetLabel(CardUiText.Get("common.action.retry"));
            Element.EnableInClassList("is-loading", state == AsyncCardImageState.Loading);
            Element.EnableInClassList("is-ready", state == AsyncCardImageState.Ready);
            Element.EnableInClassList("is-failed", state == AsyncCardImageState.Failed);
            retry.style.display = state == AsyncCardImageState.Failed
                ? DisplayStyle.Flex
                : DisplayStyle.None;

            if (!preserveStatus)
            {
                status.text = state == AsyncCardImageState.Loading
                    ? CardUiText.Get("common.status.loading")
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
            ReleaseTexture();
            Printing = null;
            StopPulse();
            CurrentLoadTask = Task.CompletedTask;
        }

        private void ReleaseTexture()
        {
            art.style.backgroundImage = new StyleBackground(StyleKeyword.None);
            textureLease?.Dispose();
            textureLease = null;
        }

        private static string FailureMessage(ContentImageLoadStatus status)
        {
            switch (status)
            {
                case ContentImageLoadStatus.InvalidPath:
                    return CardUiText.Get("card_image.error.invalid_path");
                case ContentImageLoadStatus.NotFound:
                    return CardUiText.Get("card_image.error.not_installed");
                case ContentImageLoadStatus.IntegrityMismatch:
                    return CardUiText.Get("card_image.error.verification_failed");
                default:
                    return CardUiText.Get("card_image.error.loading_failed");
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(AsyncCardImageView));
        }
    }
}
