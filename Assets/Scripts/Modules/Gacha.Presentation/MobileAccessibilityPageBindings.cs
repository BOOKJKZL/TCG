using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Gacha.Presentation
{
    /// <summary>
    /// Explicit semantics for the first supported flow. No reflection, field discovery,
    /// virtual-row guessing or synthetic input. Unsupported pages are not claimed here.
    /// </summary>
    public sealed class MobileAccessibilityPageBindings : IDisposable
    {
        private readonly List<IDisposable> registrations = new List<IDisposable>();
        private bool disposed;

        public MobileAccessibilityPageBindings(VisualElement root, string screenId, int priority = 0)
        {
            registrations.Add(MobileAccessibility.RegisterScreen(root, screenId, priority));
        }

        public void Text(string id, Label label, bool heading = false)
        {
            if (label == null) throw new ArgumentNullException(nameof(label));
            registrations.Add(MobileAccessibility.RegisterText(label, id, () => label.text, heading));
        }

        public void Action(string id, MobileActionControl action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            registrations.Add(MobileAccessibility.RegisterButton(action.Root, id,
                () => action.Label.text, () => action.IsAvailable, () => action.IsSelected,
                () => action.IsLoading, action.TryActivate));
        }

        public void Confirmation(string id, MobileConfirmationPresenter confirmation)
        {
            registrations.Add(MobileAccessibility.RegisterModal(confirmation.Root, id,
                () => confirmation.IsVisible, confirmation.TryCancel));
            Text("title", confirmation.Sheet.Title, true);
            Text("body", confirmation.Sheet.Body);
            Action("confirm", confirmation.Confirm);
            Action("confirm-danger", confirmation.DangerConfirm);
            Action("cancel", confirmation.Cancel);
        }

        private void Navigation(MobilePrimaryNavigation navigation)
        {
            Action("navigation/home", navigation.GetAction(MobileDestination.Home));
            Action("navigation/gacha", navigation.GetAction(MobileDestination.Gacha));
            Action("navigation/collection", navigation.GetAction(MobileDestination.Collection));
            Action("navigation/content", navigation.GetAction(MobileDestination.Content));
            Action("navigation/settings", navigation.GetAction(MobileDestination.Settings));
        }

        public static MobileAccessibilityPageBindings ForStart(MobileStartPresenter page)
        {
            var binding = new MobileAccessibilityPageBindings(page.Shell.Root, "start");
            binding.Text("title", page.Title, true);
            binding.Text("body", page.Body);
            binding.Action("start", page.StartAction);
            binding.Text("status", page.Status);
            binding.Confirmation("exit", page.ExitConfirmation);
            return binding;
        }

        public static MobileAccessibilityPageBindings ForHome(MobileHomePresenter page)
        {
            var binding = new MobileAccessibilityPageBindings(page.Shell.Root, "home");
            binding.Text("top-title", page.TopBar.Title, true);
            binding.Text("top-subtitle", page.TopBar.Subtitle);
            binding.Action("settings-utility", page.SettingsUtilityAction);
            binding.Text("kicker", page.Kicker);
            binding.Text("title", page.Title, true);
            binding.Text("body", page.Body);
            binding.Text("features", page.SectionTitle, true);
            binding.Action("feature/gacha", page.GachaFeatureAction);
            binding.Action("feature/collection", page.CollectionFeatureAction);
            binding.Action("feature/content", page.ContentFeatureAction);
            binding.Action("feature/settings", page.SettingsFeatureAction);
            binding.Navigation(page.PrimaryNavigation);
            binding.Confirmation("exit", page.ExitConfirmation);
            return binding;
        }

        public static MobileAccessibilityPageBindings ForSettings(MobileSettingsPresenter page)
        {
            var binding = new MobileAccessibilityPageBindings(page.Shell.Root, "settings");
            binding.Text("title", page.TopBar.Title, true);
            binding.Text("subtitle", page.TopBar.Subtitle);
            binding.Action("back", page.BackAction);
            binding.Text("language/title", page.LanguageTitle, true);
            binding.Text("language/description", page.LanguageDescription);
            binding.Text("language/ui", page.UiLanguageLabel);
            binding.Action("language/ui-cycle", page.UiLanguageAction);
            binding.Text("language/card", page.CardLanguageLabel);
            binding.Action("language/card-cycle", page.CardLanguageAction);
            binding.Text("language/status", page.LanguageStatus);
            binding.Text("experience/title", page.ExperienceTitle, true);
            binding.Text("experience/description", page.ExperienceDescription);
            binding.Action("experience/sound", page.SoundAction);
            binding.Action("experience/motion", page.MotionAction);
            binding.Action("experience/haptics", page.HapticsAction);
            binding.Action("experience/speed", page.SpeedAction);
            binding.Text("experience/status", page.ExperienceStatus);
            binding.Text("download/title", page.DownloadTitle, true);
            binding.Text("download/description", page.DownloadDescription);
            binding.Action("download/wifi", page.WifiOnlyAction);
            binding.Text("download/status", page.DownloadStatus);
            binding.Text("recovery/title", page.RecoveryTitle, true);
            binding.Text("recovery/description", page.RecoveryDescription);
            binding.Action("recovery/export", page.ExportAction);
            binding.Action("recovery/import", page.ImportAction);
            binding.Text("recovery/preview", page.RecoveryPreview);
            binding.Action("recovery/confirm", page.ConfirmImportAction);
            binding.Text("recovery/status", page.RecoveryStatus);
            binding.Text("diagnostics/title", page.DiagnosticsTitle, true);
            binding.Text("diagnostics/description", page.DiagnosticsDescription);
            binding.Text("diagnostics/summary", page.DiagnosticsSummary);
            binding.Text("diagnostics/save-protection", page.SaveProtectionStatus);
            binding.Action("diagnostics/export", page.ExportDiagnosticsAction);
            binding.Action("diagnostics/clear", page.ClearDiagnosticsAction);
            binding.Text("diagnostics/status", page.DiagnosticsStatus);
            binding.Text("account/title", page.AccountTitle, true);
            binding.Text("account/description", page.AccountDescription);
            binding.Text("account/status", page.IdentityStatus);
            binding.Action("account/connect", page.IdentityAction);
            binding.Text("cloud/title", page.CloudTitle, true);
            binding.Text("cloud/description", page.CloudDescription);
            binding.Text("cloud/local-title", page.CloudLocalTitle, true);
            binding.Text("cloud/local-summary", page.CloudLocalSummary);
            binding.Text("cloud/remote-title", page.CloudRemoteTitle, true);
            binding.Text("cloud/remote-summary", page.CloudRemoteSummary);
            binding.Text("cloud/notice", page.CloudNotice);
            binding.Action("cloud/local", page.KeepLocalAction);
            binding.Action("cloud/remote", page.UseCloudAction);
            binding.Action("cloud/merge", page.MergeAction);
            binding.Text("cloud/status", page.CloudStatus);
            binding.Navigation(page.PrimaryNavigation);
            binding.Confirmation("confirmation", page.Confirmation);
            return binding;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            // Removing the screen atomically removes its entire registered subtree.
            // A closing modal must not briefly expose actions on a dying page.
            if (registrations.Count > 0) registrations[0].Dispose();
            for (int index = registrations.Count - 1; index >= 1; index--)
                registrations[index].Dispose();
            registrations.Clear();
        }
    }
}
