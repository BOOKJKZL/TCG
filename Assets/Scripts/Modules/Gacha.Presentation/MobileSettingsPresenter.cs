using System;
using System.Threading.Tasks;
using Gacha.Application;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gacha.Presentation
{
    public enum MobileSettingsIdentityState
    {
        SetupRequired,
        Available,
        Connected,
        Busy
    }

    public enum MobileSettingsIdentityOutcome
    {
        SetupRequired,
        Linked,
        CloudPending,
        ConflictPending,
        Failed
    }

    public enum MobileSettingsCloudChoice
    {
        KeepLocal,
        UseCloud,
        SafeMerge
    }

    public sealed class MobileSettingsOperationResult
    {
        public MobileSettingsOperationResult(
            bool succeeded,
            bool cancelled = false,
            string path = null,
            string developerDetail = null)
        {
            Succeeded = succeeded;
            Cancelled = cancelled;
            Path = path;
            DeveloperDetail = developerDetail;
        }

        public bool Succeeded { get; }
        public bool Cancelled { get; }
        public string Path { get; }
        public string DeveloperDetail { get; }
    }

    public sealed class MobileSettingsRecoveryPreviewData
    {
        public MobileSettingsRecoveryPreviewData(
            DateTime createdAtUtc,
            int distinctPrintings,
            long totalCards,
            long totalProducts,
            int historyCount,
            string uiLanguageId,
            string contentLanguageId)
        {
            CreatedAtUtc = createdAtUtc;
            DistinctPrintings = distinctPrintings;
            TotalCards = totalCards;
            TotalProducts = totalProducts;
            HistoryCount = historyCount;
            UiLanguageId = uiLanguageId;
            ContentLanguageId = contentLanguageId;
        }

        public DateTime CreatedAtUtc { get; }
        public int DistinctPrintings { get; }
        public long TotalCards { get; }
        public long TotalProducts { get; }
        public int HistoryCount { get; }
        public string UiLanguageId { get; }
        public string ContentLanguageId { get; }
    }

    public sealed class MobileSettingsIdentityStatusData
    {
        public MobileSettingsIdentityStatusData(MobileSettingsIdentityState state, string redactedIdentity = null)
        {
            State = state;
            RedactedIdentity = redactedIdentity;
        }

        public MobileSettingsIdentityState State { get; }
        public string RedactedIdentity { get; }
    }

    public sealed class MobileSettingsIdentityResultData
    {
        public MobileSettingsIdentityResultData(
            MobileSettingsIdentityOutcome outcome,
            string developerDetail = null)
        {
            Outcome = outcome;
            DeveloperDetail = developerDetail;
        }

        public MobileSettingsIdentityOutcome Outcome { get; }
        public string DeveloperDetail { get; }
    }

    public sealed class MobileSettingsProgressSummaryData
    {
        public MobileSettingsProgressSummaryData(
            DateTime lastModifiedUtc,
            int distinctPrintings,
            long totalCards,
            long totalProducts,
            int historyCount)
        {
            LastModifiedUtc = lastModifiedUtc;
            DistinctPrintings = distinctPrintings;
            TotalCards = totalCards;
            TotalProducts = totalProducts;
            HistoryCount = historyCount;
        }

        public DateTime LastModifiedUtc { get; }
        public int DistinctPrintings { get; }
        public long TotalCards { get; }
        public long TotalProducts { get; }
        public int HistoryCount { get; }
    }

    public sealed class MobileSettingsCloudStateData
    {
        public MobileSettingsCloudStateData(
            bool hasPending,
            bool isResolving,
            MobileSettingsProgressSummaryData local = null,
            MobileSettingsProgressSummaryData cloud = null)
        {
            HasPending = hasPending;
            IsResolving = isResolving;
            Local = local;
            Cloud = cloud;
        }

        public bool HasPending { get; }
        public bool IsResolving { get; }
        public MobileSettingsProgressSummaryData Local { get; }
        public MobileSettingsProgressSummaryData Cloud { get; }
    }

    public sealed class MobileSettingsOperationOverrides
    {
        public Func<bool> RecoveryAvailable { get; set; }
        public Func<bool> PickerBusy { get; set; }
        public Action<Action<MobileSettingsOperationResult>> ExportRecovery { get; set; }
        public Action<Action<MobileSettingsOperationResult>> ChooseImport { get; set; }
        public Func<string, MobileSettingsRecoveryPreviewData> PreviewRecovery { get; set; }
        public Func<string, bool> RestoreRecovery { get; set; }
        public Action CancelPendingPicker { get; set; }
        public Func<MobileSettingsIdentityStatusData> IdentityStatus { get; set; }
        public Func<Task<MobileSettingsIdentityResultData>> ConnectIdentity { get; set; }
        public Func<MobileSettingsCloudStateData> CloudState { get; set; }
        public Func<MobileSettingsCloudChoice, Task<MobileSettingsOperationResult>> ResolveCloud { get; set; }
    }

    public sealed class MobileSettingsCallbacks
    {
        public Action CycleUiLanguage { get; set; }
        public Action CycleCardLanguage { get; set; }
        public Action ToggleSound { get; set; }
        public Action ToggleReduceMotion { get; set; }
        public Action ToggleHaptics { get; set; }
        public Action CycleAnimationSpeed { get; set; }
        public Action ToggleWifiOnly { get; set; }
        public Action ExportSave { get; set; }
        public Action ChooseImport { get; set; }
        public Action ConfirmImport { get; set; }
        public Action ConnectIdentity { get; set; }
        public Action KeepLocal { get; set; }
        public Action UseCloud { get; set; }
        public Action SafeMerge { get; set; }
        public Action<MobileDestination> Navigate { get; set; }
    }

    public sealed class MobileSettingsPresenter : IDisposable
    {
        private readonly MobileActionControl[] settingActions;
        private readonly MobileActionControl backAction;
        private bool disposed;

        public MobileSettingsPresenter(GameObject host, MobileSettingsCallbacks callbacks)
        {
            if (host == null)
                throw new ArgumentNullException(nameof(host));
            callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));

            PanelSettings panelSettings = Resources.Load<PanelSettings>("UI/Collection Panel Settings");
            if (panelSettings == null)
                throw new InvalidOperationException("The mobile settings Panel Settings asset is missing.");

            Document = host.GetComponent<UIDocument>() ?? host.AddComponent<UIDocument>();
            Document.panelSettings = panelSettings;
            Document.sortingOrder = 10;
            Document.rootVisualElement.Clear();
            Document.rootVisualElement.name = "mobile-settings-document";

            Shell = new MobilePageShell("mobile-settings-page");
            Document.rootVisualElement.Add(Shell.Root);
            TopBar = new MobileTopBar(string.Empty);
            backAction = Action("settings-back-action", callbacks.Navigate, MobileDestination.Home, MobileActionTone.Quiet);
            TopBar.AddAction(backAction);
            Shell.HeaderSlot.Add(TopBar.Root);

            Scroll = MobileGameDesignSystem.CloneTemplateRoot("UI/SettingsView", "settings-scroll") as ScrollView;
            if (Scroll == null)
                throw new InvalidOperationException("The mobile settings view root must be a ScrollView.");
            Shell.ContentSlot.Add(Scroll);

            UiLanguageAction = Add("settings-ui-language-slot", "settings-ui-language-action", callbacks.CycleUiLanguage);
            CardLanguageAction = Add("settings-card-language-slot", "settings-card-language-action", callbacks.CycleCardLanguage);
            SoundAction = Add("settings-sound-slot", "settings-sound-action", callbacks.ToggleSound);
            MotionAction = Add("settings-motion-slot", "settings-motion-action", callbacks.ToggleReduceMotion);
            HapticsAction = Add("settings-haptics-slot", "settings-haptics-action", callbacks.ToggleHaptics);
            SpeedAction = Add("settings-speed-slot", "settings-speed-action", callbacks.CycleAnimationSpeed);
            WifiOnlyAction = Add("settings-wifi-slot", "settings-wifi-action", callbacks.ToggleWifiOnly);
            ExportAction = Add("settings-export-slot", "settings-export-action", callbacks.ExportSave);
            ImportAction = Add("settings-import-slot", "settings-import-action", callbacks.ChooseImport);
            ConfirmImportAction = Add(
                "settings-confirm-import-slot",
                "settings-confirm-import-action",
                callbacks.ConfirmImport,
                MobileActionTone.Danger);
            IdentityAction = Add("settings-connect-slot", "settings-connect-action", callbacks.ConnectIdentity);
            KeepLocalAction = Add("settings-cloud-local-slot", "settings-cloud-local-action", callbacks.KeepLocal);
            UseCloudAction = Add(
                "settings-cloud-remote-slot",
                "settings-cloud-remote-action",
                callbacks.UseCloud,
                MobileActionTone.Danger);
            MergeAction = Add("settings-cloud-merge-slot", "settings-cloud-merge-action", callbacks.SafeMerge);
            settingActions = new[]
            {
                UiLanguageAction, CardLanguageAction, SoundAction, MotionAction, HapticsAction,
                SpeedAction, WifiOnlyAction, ExportAction, ImportAction, ConfirmImportAction,
                IdentityAction, KeepLocalAction, UseCloudAction, MergeAction
            };

            PrimaryNavigation = new MobilePrimaryNavigation(
                MobileDestination.Settings,
                callbacks.Navigate ?? throw new ArgumentNullException(nameof(callbacks.Navigate)));
            Shell.BottomNavigationSlot.Add(PrimaryNavigation.BottomNavigation.Root);

            Confirmation = new MobileConfirmationPresenter();
            Shell.ModalLayer.Add(Confirmation.Root);

            LanguageTitle = RequiredLabel("settings-language-title");
            LanguageDescription = RequiredLabel("settings-language-description");
            UiLanguageLabel = RequiredLabel("settings-ui-language-label");
            CardLanguageLabel = RequiredLabel("settings-card-language-label");
            LanguageStatus = RequiredLabel("settings-language-status");
            ExperienceTitle = RequiredLabel("settings-experience-title");
            ExperienceDescription = RequiredLabel("settings-experience-description");
            ExperienceStatus = RequiredLabel("settings-experience-status");
            DownloadTitle = RequiredLabel("settings-download-title");
            DownloadDescription = RequiredLabel("settings-download-description");
            DownloadStatus = RequiredLabel("settings-download-status");
            RecoveryTitle = RequiredLabel("settings-recovery-title");
            RecoveryDescription = RequiredLabel("settings-recovery-description");
            RecoveryPreview = RequiredLabel("settings-recovery-preview");
            RecoveryStatus = RequiredLabel("settings-recovery-status");
            AccountTitle = RequiredLabel("settings-account-title");
            AccountDescription = RequiredLabel("settings-account-description");
            IdentityStatus = RequiredLabel("settings-identity-status");
            CloudCard = RequiredElement("settings-cloud-card");
            CloudTitle = RequiredLabel("settings-cloud-title");
            CloudDescription = RequiredLabel("settings-cloud-description");
            CloudLocalTitle = RequiredLabel("settings-cloud-local-title");
            CloudRemoteTitle = RequiredLabel("settings-cloud-remote-title");
            CloudLocalSummary = RequiredLabel("settings-cloud-local");
            CloudRemoteSummary = RequiredLabel("settings-cloud-remote");
            CloudNotice = RequiredLabel("settings-cloud-notice");
            CloudStatus = RequiredLabel("settings-cloud-status");
        }

        public UIDocument Document { get; }
        public MobilePageShell Shell { get; }
        public MobileTopBar TopBar { get; }
        public ScrollView Scroll { get; }
        public MobilePrimaryNavigation PrimaryNavigation { get; }
        public MobileConfirmationPresenter Confirmation { get; }
        public MobileActionControl UiLanguageAction { get; }
        public MobileActionControl CardLanguageAction { get; }
        public MobileActionControl SoundAction { get; }
        public MobileActionControl MotionAction { get; }
        public MobileActionControl HapticsAction { get; }
        public MobileActionControl SpeedAction { get; }
        public MobileActionControl WifiOnlyAction { get; }
        public MobileActionControl ExportAction { get; }
        public MobileActionControl ImportAction { get; }
        public MobileActionControl ConfirmImportAction { get; }
        public MobileActionControl IdentityAction { get; }
        public MobileActionControl KeepLocalAction { get; }
        public MobileActionControl UseCloudAction { get; }
        public MobileActionControl MergeAction { get; }
        public Label LanguageTitle { get; }
        public Label LanguageDescription { get; }
        public Label UiLanguageLabel { get; }
        public Label CardLanguageLabel { get; }
        public Label LanguageStatus { get; }
        public Label ExperienceTitle { get; }
        public Label ExperienceDescription { get; }
        public Label ExperienceStatus { get; }
        public Label DownloadTitle { get; }
        public Label DownloadDescription { get; }
        public Label DownloadStatus { get; }
        public Label RecoveryTitle { get; }
        public Label RecoveryDescription { get; }
        public Label RecoveryPreview { get; }
        public Label RecoveryStatus { get; }
        public Label AccountTitle { get; }
        public Label AccountDescription { get; }
        public Label IdentityStatus { get; }
        public VisualElement CloudCard { get; }
        public Label CloudTitle { get; }
        public Label CloudDescription { get; }
        public Label CloudLocalTitle { get; }
        public Label CloudRemoteTitle { get; }
        public Label CloudLocalSummary { get; }
        public Label CloudRemoteSummary { get; }
        public Label CloudNotice { get; }
        public Label CloudStatus { get; }

        public void RefreshText(
            string uiLanguageId,
            string cardLanguageId,
            ExperienceSettings experience,
            bool wifiOnly)
        {
            if (disposed)
                return;
            experience = experience ?? new ExperienceSettings();
            TopBar.SetText(CardUiText.Get("settings.title"), CardUiText.Get("settings.subtitle"));
            backAction.SetLabel(CardUiText.Get("common.action.main_menu"));
            LanguageTitle.text = CardUiText.Get("settings.language.title");
            LanguageDescription.text = CardUiText.Get("settings.language.description");
            UiLanguageLabel.text = CardUiText.Get("settings.language.ui");
            CardLanguageLabel.text = CardUiText.Get("settings.language.content");
            UiLanguageAction.SetLabel(CardUiText.Get(LanguageKey(uiLanguageId)));
            CardLanguageAction.SetLabel(CardUiText.Get(LanguageKey(cardLanguageId)));
            ExperienceTitle.text = CardUiText.Get("settings.experience.title");
            ExperienceDescription.text = CardUiText.Get("settings.experience.description");
            SoundAction.SetLabel(SettingValue(
                "settings.experience.sound",
                experience.SoundEnabled ? "settings.experience.sound_on" : "settings.experience.muted"));
            MotionAction.SetLabel(SettingValue(
                "settings.experience.reduce_motion",
                experience.ReduceMotion ? "settings.experience.on" : "settings.experience.off"));
            HapticsAction.SetLabel(SettingValue(
                "settings.experience.haptics",
                experience.HapticsEnabled ? "settings.experience.on" : "settings.experience.off"));
            SpeedAction.SetLabel(SettingValue(
                "settings.experience.animation_speed",
                experience.AnimationSpeed.ToString("0.0") + "×"));
            DownloadTitle.text = CardUiText.Get("settings.download.title");
            DownloadDescription.text = CardUiText.Get("settings.download.description");
            WifiOnlyAction.SetLabel(SettingValue(
                "content.policy.wifi_only",
                wifiOnly ? "settings.experience.on" : "settings.experience.off"));
            RecoveryTitle.text = CardUiText.Get("settings.recovery.title");
            RecoveryDescription.text = CardUiText.Get("settings.recovery.description");
            ExportAction.SetLabel(CardUiText.Get("settings.recovery.action.export"));
            ImportAction.SetLabel(CardUiText.Get("settings.recovery.action.preview"));
            ConfirmImportAction.SetLabel(CardUiText.Get("settings.recovery.action.confirm"));
            AccountTitle.text = CardUiText.Get("settings.account.title");
            AccountDescription.text = CardUiText.Get("settings.account.description");
            IdentityAction.SetLabel(CardUiText.Get("settings.identity.action.connect"));
            CloudTitle.text = CardUiText.Get("settings.cloud.title");
            CloudDescription.text = CardUiText.Get("settings.cloud.description");
            CloudLocalTitle.text = CardUiText.Get("settings.cloud.local");
            CloudRemoteTitle.text = CardUiText.Get("settings.cloud.remote");
            CloudNotice.text = CardUiText.Get("settings.cloud.merge_notice");
            KeepLocalAction.SetLabel(CardUiText.Get("settings.cloud.action.local"));
            UseCloudAction.SetLabel(CardUiText.Get("settings.cloud.action.remote"));
            MergeAction.SetLabel(CardUiText.Get("settings.cloud.action.merge"));
            PrimaryNavigation.RefreshText();
        }

        public void SetStatus(Label target, string text, bool error = false)
        {
            if (disposed || target == null)
                return;
            target.text = text ?? string.Empty;
            target.EnableInClassList("is-error", error);
        }

        public void SetRecoveryPreview(string text)
        {
            RecoveryPreview.text = text ?? string.Empty;
            RecoveryPreview.EnableInClassList("is-hidden", string.IsNullOrWhiteSpace(text));
        }

        public void SetCloudVisible(bool visible)
        {
            CloudCard.EnableInClassList("is-hidden", !visible);
        }

        public void SetNavigationPending(MobileDestination destination)
        {
            if (disposed || destination == MobileDestination.Settings)
                return;
            PrimaryNavigation.SetPending(destination);
            backAction.SetLoading(destination == MobileDestination.Home);
            backAction.SetEnabled(false);
            foreach (MobileActionControl action in settingActions)
                action.SetEnabled(false);
        }

        public void ClearNavigationPending()
        {
            if (disposed)
                return;
            PrimaryNavigation.ClearPending(MobileDestination.Settings);
            backAction.SetLoading(false);
            backAction.SetEnabled(true);
        }

        public void ApplyAvailability(
            bool enabled,
            bool recoveryAvailable,
            bool hasPendingImport,
            bool identityAvailable,
            bool conflictAvailable)
        {
            if (disposed)
                return;
            UiLanguageAction.SetEnabled(enabled);
            CardLanguageAction.SetEnabled(enabled);
            SoundAction.SetEnabled(enabled);
            MotionAction.SetEnabled(enabled);
            HapticsAction.SetEnabled(enabled);
            SpeedAction.SetEnabled(enabled);
            WifiOnlyAction.SetEnabled(enabled);
            ExportAction.SetEnabled(enabled && recoveryAvailable);
            ImportAction.SetEnabled(enabled && recoveryAvailable);
            ConfirmImportAction.SetEnabled(enabled && recoveryAvailable && hasPendingImport);
            IdentityAction.SetEnabled(enabled && identityAvailable);
            KeepLocalAction.SetEnabled(enabled && conflictAvailable);
            UseCloudAction.SetEnabled(enabled && conflictAvailable);
            MergeAction.SetEnabled(enabled && conflictAvailable);
            backAction.SetEnabled(enabled);
            foreach (MobileDestination destination in Enum.GetValues(typeof(MobileDestination)))
                PrimaryNavigation.GetAction(destination).SetEnabled(enabled);
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            VisualElement documentRoot = Document != null ? Document.rootVisualElement : null;
            VisualElement focusedElement = documentRoot?.panel?.focusController?.focusedElement as VisualElement;
            if (focusedElement != null && documentRoot.Contains(focusedElement))
                focusedElement.Blur();
            foreach (MobileActionControl action in settingActions)
                action.Dispose();
            backAction.Dispose();
            PrimaryNavigation.Dispose();
            Confirmation.Dispose();
            Shell.Dispose();
            documentRoot?.Clear();
        }

        private MobileActionControl Add(
            string slotName,
            string actionName,
            Action clicked,
            MobileActionTone tone = MobileActionTone.Standard)
        {
            var action = new MobileActionControl(
                actionName,
                string.Empty,
                clicked ?? throw new ArgumentNullException(actionName),
                tone);
            RequiredElement(slotName).Add(action.Root);
            return action;
        }

        private Label RequiredLabel(string name) =>
            Scroll.Q<Label>(name) ?? throw new InvalidOperationException(
                "The mobile settings view is missing label '" + name + "'.");

        private VisualElement RequiredElement(string name) =>
            Scroll.Q<VisualElement>(name) ?? throw new InvalidOperationException(
                "The mobile settings view is missing element '" + name + "'.");

        private static MobileActionControl Action(
            string name,
            Action<MobileDestination> navigate,
            MobileDestination destination,
            MobileActionTone tone) =>
            new MobileActionControl(
                name,
                string.Empty,
                () => (navigate ?? throw new ArgumentNullException(nameof(navigate)))(destination),
                tone);

        private static string SettingValue(string labelKey, string valueKeyOrText)
        {
            string value = valueKeyOrText != null && valueKeyOrText.Contains(".")
                ? CardUiText.Get(valueKeyOrText)
                : valueKeyOrText ?? string.Empty;
            return CardUiText.Format("settings.value", CardUiText.Get(labelKey), value);
        }

        private static string LanguageKey(string languageId)
        {
            if (string.IsNullOrWhiteSpace(languageId))
                return "language.en";
            string normalized = languageId.Replace('_', '-');
            int separator = normalized.IndexOf('-');
            string root = separator > 0 ? normalized.Substring(0, separator) : normalized;
            return "language." + root.ToLowerInvariant();
        }
    }
}
