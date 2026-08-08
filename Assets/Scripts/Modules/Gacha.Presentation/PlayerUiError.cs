using System;
using System.Collections.Generic;
using System.IO;
using Gacha.Application;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gacha.Presentation
{
    public enum PlayerUiErrorCode
    {
        NotInstalled,
        Offline,
        CatalogCorrupt,
        VerificationFailed,
        InsufficientSpace,
        ServiceUnavailable,
        Unexpected
    }

    public enum PlayerUiErrorAction
    {
        Retry,
        ManageContent,
        Home,
        Close
    }

    public sealed class PlayerUiError
    {
        public PlayerUiError(PlayerUiErrorCode code, params PlayerUiErrorAction[] actions)
        {
            Code = code;
            Actions = Array.AsReadOnly(actions ?? Array.Empty<PlayerUiErrorAction>());
        }

        public PlayerUiErrorCode Code { get; }
        public IReadOnlyList<PlayerUiErrorAction> Actions { get; }

        public bool Supports(PlayerUiErrorAction action)
        {
            for (int index = 0; index < Actions.Count; index++)
                if (Actions[index] == action)
                    return true;
            return false;
        }
    }

    public static class PlayerUiErrorMapper
    {
        public static PlayerUiError FromCatalog(CatalogLoadResult result, bool isOffline = false)
        {
            if (result == null)
                return FromDetail(null, isOffline);
            if (result.State == CatalogLoadState.Ready)
                return null;
            if (result.State == CatalogLoadState.NoInstalledContent)
                return Create(PlayerUiErrorCode.NotInstalled);
            if (isOffline)
                return Create(PlayerUiErrorCode.Offline);
            return result.FailureReason switch
            {
                CatalogFailureReason.CatalogCorrupt => Create(PlayerUiErrorCode.CatalogCorrupt),
                CatalogFailureReason.VerificationFailed => Create(PlayerUiErrorCode.VerificationFailed),
                CatalogFailureReason.ServiceUnavailable => Create(PlayerUiErrorCode.ServiceUnavailable),
                _ => Create(PlayerUiErrorCode.Unexpected)
            };
        }

        public static PlayerUiError FromException(
            Exception exception,
            bool isOffline = false,
            bool missingContentContext = false)
        {
            if (missingContentContext &&
                (exception is FileNotFoundException || exception is DirectoryNotFoundException))
                return Create(PlayerUiErrorCode.NotInstalled);
            return isOffline
                ? Create(PlayerUiErrorCode.Offline)
                : Create(PlayerUiErrorCode.Unexpected);
        }

        public static PlayerUiError FromDetail(string developerDetail, bool isOffline = false)
        {
            return isOffline ? Create(PlayerUiErrorCode.Offline) : Create(PlayerUiErrorCode.Unexpected);
        }

        public static PlayerUiError FromPreflight(ContentDownloadPreflightStatus status)
        {
            return status switch
            {
                ContentDownloadPreflightStatus.NoSelection => null,
                ContentDownloadPreflightStatus.AlreadyCurrent => null,
                ContentDownloadPreflightStatus.Ready => null,
                ContentDownloadPreflightStatus.WaitingForWifi => null,
                ContentDownloadPreflightStatus.CellularConfirmationRequired => null,
                ContentDownloadPreflightStatus.Offline => Create(PlayerUiErrorCode.Offline),
                ContentDownloadPreflightStatus.InsufficientSpace => Create(PlayerUiErrorCode.InsufficientSpace),
                ContentDownloadPreflightStatus.StorageUnavailable => Create(PlayerUiErrorCode.ServiceUnavailable),
                ContentDownloadPreflightStatus.NetworkUnavailable => Create(PlayerUiErrorCode.ServiceUnavailable),
                _ => Create(PlayerUiErrorCode.Unexpected)
            };
        }

        public static PlayerUiError FromInstallPlan(ContentInstallPlanStatus status)
        {
            return status switch
            {
                ContentInstallPlanStatus.Ready => null,
                ContentInstallPlanStatus.AlreadyCurrent => null,
                ContentInstallPlanStatus.InsufficientSpace => Create(PlayerUiErrorCode.InsufficientSpace),
                ContentInstallPlanStatus.InvalidPackage => Create(PlayerUiErrorCode.CatalogCorrupt),
                ContentInstallPlanStatus.StorageUnavailable => Create(PlayerUiErrorCode.ServiceUnavailable),
                _ => Create(PlayerUiErrorCode.Unexpected)
            };
        }

        public static PlayerUiError FromInstall(ContentPackageInstallStatus status)
        {
            return status switch
            {
                ContentPackageInstallStatus.Succeeded => null,
                ContentPackageInstallStatus.Cancelled => null,
                ContentPackageInstallStatus.ArchiveNotFound => Create(PlayerUiErrorCode.VerificationFailed),
                ContentPackageInstallStatus.IntegrityMismatch => Create(PlayerUiErrorCode.VerificationFailed),
                ContentPackageInstallStatus.InvalidArchive => Create(PlayerUiErrorCode.VerificationFailed),
                _ => Create(PlayerUiErrorCode.Unexpected)
            };
        }

        public static PlayerUiError FromImage(ContentImageLoadStatus status)
        {
            return status switch
            {
                ContentImageLoadStatus.Succeeded => null,
                ContentImageLoadStatus.NotFound => Create(PlayerUiErrorCode.NotInstalled),
                ContentImageLoadStatus.IntegrityMismatch => Create(PlayerUiErrorCode.VerificationFailed),
                ContentImageLoadStatus.InvalidPath => Create(PlayerUiErrorCode.CatalogCorrupt),
                _ => Create(PlayerUiErrorCode.Unexpected)
            };
        }

        public static PlayerUiError FromRemoval(ContentPackageRemovalStatus status)
        {
            return status switch
            {
                ContentPackageRemovalStatus.Removed => null,
                ContentPackageRemovalStatus.NotInstalled => null,
                ContentPackageRemovalStatus.Cancelled => null,
                _ => Create(PlayerUiErrorCode.Unexpected)
            };
        }

        public static PlayerUiError Create(PlayerUiErrorCode code)
        {
            return code switch
            {
                PlayerUiErrorCode.NotInstalled => new PlayerUiError(
                    code, PlayerUiErrorAction.ManageContent, PlayerUiErrorAction.Home, PlayerUiErrorAction.Close),
                PlayerUiErrorCode.Offline => new PlayerUiError(
                    code, PlayerUiErrorAction.Retry, PlayerUiErrorAction.ManageContent,
                    PlayerUiErrorAction.Home, PlayerUiErrorAction.Close),
                PlayerUiErrorCode.InsufficientSpace => new PlayerUiError(
                    code, PlayerUiErrorAction.ManageContent, PlayerUiErrorAction.Retry,
                    PlayerUiErrorAction.Home, PlayerUiErrorAction.Close),
                _ => new PlayerUiError(
                    code, PlayerUiErrorAction.Retry, PlayerUiErrorAction.ManageContent,
                    PlayerUiErrorAction.Home, PlayerUiErrorAction.Close)
            };
        }

    }

    public static class PlayerUiErrorText
    {
        public static string Title(PlayerUiError error, string languageId = null) =>
            CardUiText.Get(Key(error, "title"));

        public static string Body(PlayerUiError error, string languageId = null) =>
            CardUiText.Get(Key(error, "body"));

        public static string Key(PlayerUiError error, string part)
        {
            PlayerUiErrorCode code = error?.Code ?? PlayerUiErrorCode.Unexpected;
            return "player_error." + ToKey(code) + "." + part;
        }

        private static string ToKey(PlayerUiErrorCode code) => code switch
        {
            PlayerUiErrorCode.NotInstalled => "not_installed",
            PlayerUiErrorCode.Offline => "offline",
            PlayerUiErrorCode.CatalogCorrupt => "catalog_corrupt",
            PlayerUiErrorCode.VerificationFailed => "verification_failed",
            PlayerUiErrorCode.InsufficientSpace => "insufficient_space",
            PlayerUiErrorCode.ServiceUnavailable => "service_unavailable",
            _ => "unexpected"
        };
    }

    public sealed class PlayerUiErrorPresenter : IDisposable
    {
        private readonly VisualElement panel;
        private readonly Label title;
        private readonly Label body;
        private readonly VisualElement retry;
        private readonly VisualElement manageContent;
        private readonly VisualElement home;
        private readonly VisualElement close;
        private IVisualElementScheduledItem animation;
        private bool visible;

        public PlayerUiErrorPresenter(
            VisualElement panel,
            Label title,
            Label body,
            VisualElement retry = null,
            VisualElement manageContent = null,
            VisualElement home = null,
            VisualElement close = null)
        {
            this.panel = panel ?? throw new ArgumentNullException(nameof(panel));
            this.title = title ?? throw new ArgumentNullException(nameof(title));
            this.body = body ?? throw new ArgumentNullException(nameof(body));
            this.retry = retry;
            this.manageContent = manageContent;
            this.home = home;
            this.close = close;
        }

        public PlayerUiError Current { get; private set; }
        public bool IsVisible => visible;
        public bool IsAnimating => animation != null;

        public void Show(PlayerUiError error)
        {
            PlayerUiError next = error ?? PlayerUiErrorMapper.Create(PlayerUiErrorCode.Unexpected);
            bool announce = !visible || Current == null || Current.Code != next.Code;
            Current = next;
            visible = true;
            panel.style.display = DisplayStyle.Flex;
            RefreshLanguage();
            SetVisible(retry, next.Supports(PlayerUiErrorAction.Retry));
            SetVisible(manageContent, next.Supports(PlayerUiErrorAction.ManageContent));
            SetVisible(home, next.Supports(PlayerUiErrorAction.Home));
            SetVisible(close, next.Supports(PlayerUiErrorAction.Close));
            if (announce)
            {
                AnimateIn();
                UIFeedbackService.Play(FeedbackCue.Error);
            }
        }

        public void RefreshLanguage(string languageId = null)
        {
            if (Current == null)
                return;
            title.text = PlayerUiErrorText.Title(Current, languageId);
            body.text = PlayerUiErrorText.Body(Current, languageId);
            SetLabel(retry, CardUiText.Get("common.action.retry"));
            SetLabel(manageContent, CardUiText.Get("common.action.manage_content"));
            SetLabel(home, CardUiText.Get("common.action.main_menu"));
            SetLabel(close, CardUiText.Get("common.action.close"));
        }

        public void Hide()
        {
            bool wasVisible = visible;
            Current = null;
            visible = false;
            animation?.Pause();
            animation = null;
            panel.RemoveFromClassList("is-entering");
            if (!wasVisible || UIFeedbackService.ReduceMotion)
            {
                ResetHiddenStyle();
                return;
            }
            panel.AddToClassList("is-leaving");
            panel.style.opacity = 0f;
            animation = panel.schedule.Execute(ResetHiddenStyle);
            animation.ExecuteLater(Mathf.RoundToInt(90f / UIFeedbackService.AnimationSpeed));
        }

        public void HideImmediately()
        {
            Current = null;
            visible = false;
            animation?.Pause();
            animation = null;
            panel.RemoveFromClassList("is-entering");
            ResetHiddenStyle();
        }

        public void Dispose()
        {
            animation?.Pause();
            animation = null;
        }

        private void AnimateIn()
        {
            animation?.Pause();
            animation = null;
            panel.RemoveFromClassList("is-leaving");
            if (UIFeedbackService.ReduceMotion)
            {
                panel.RemoveFromClassList("is-entering");
                panel.style.opacity = 1f;
                panel.style.translate = new Translate(0f, 0f, 0f);
                return;
            }
            panel.AddToClassList("is-entering");
            panel.style.opacity = 0.35f;
            panel.style.translate = new Translate(0f, 8f, 0f);
            animation = panel.schedule.Execute(() =>
            {
                panel.RemoveFromClassList("is-entering");
                panel.style.opacity = 1f;
                panel.style.translate = new Translate(0f, 0f, 0f);
                animation = null;
            });
            animation.ExecuteLater(Mathf.RoundToInt(120f / UIFeedbackService.AnimationSpeed));
        }

        private void ResetHiddenStyle()
        {
            animation = null;
            panel.RemoveFromClassList("is-leaving");
            panel.style.opacity = 1f;
            panel.style.translate = new Translate(0f, 0f, 0f);
            panel.style.display = DisplayStyle.None;
        }

        private static void SetVisible(VisualElement element, bool show)
        {
            if (element != null)
                element.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static void SetLabel(VisualElement element, string value)
        {
            if (element is Button button)
                button.text = value;
            else if (element != null)
            {
                Label label = element.Q<Label>();
                if (label != null)
                    label.text = value;
            }
        }
    }
}
