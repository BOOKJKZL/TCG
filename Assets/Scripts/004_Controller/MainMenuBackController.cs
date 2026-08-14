using System;
using System.IO;
using System.Threading.Tasks;
using Gacha.Application;
using Gacha.Presentation;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.SceneManagement;

public sealed class MainMenuBackController : MonoBehaviour
{
    private static readonly string[] CardLanguageIds = { "en", "zh-cn", "ja" };
    private static readonly float[] AnimationSpeeds = { 0.5f, 1f, 1.5f, 2f };

    private MobileSettingsPresenter presenter;
    private LanguageSelectionService languages;
    private ExperienceSettingsService experience;
    private ContentDownloadPolicyService downloadPolicy;
    private InventoryRecoveryService recovery;
    private RecoveryDocumentPicker picker;
    private InventoryRecoveryPreview pendingPreview;
    private MobileSettingsRecoveryPreviewData pendingPreviewOverride;
    private string pendingImportPath;
    private string languageStatusKey = "settings.language.status.ready";
    private string recoveryStatusKey = "settings.recovery.status.ready";
    private string experienceStatusKey = "settings.experience.auto_save";
    private string downloadStatusKey = "settings.download.status.ready";
    private string identityStatusKey;
    private string cloudStatusKey = "settings.cloud.status.none";
    private bool recoveryStatusError;
    private bool languageStatusError;
    private bool experienceStatusError;
    private bool downloadStatusError;
    private bool identityStatusError;
    private bool cloudStatusError;
    private bool navigationRequested;
    private bool busy;
    private bool pickerRequestActive;
    private bool destroyed;
    private int operationGeneration;
    private Action<string> sceneLoaderOverrideForTests;
    private MobileSettingsOperationOverrides operationOverridesForTests;

    public MobileSettingsPresenter SettingsPresenter => presenter;
    public bool IsBusy => busy;
    public bool HasPendingImport => pendingPreview != null || pendingPreviewOverride != null;

    private void Awake()
    {
        if (!ApplicationServices.IsConfigured)
            GameApplicationBootstrap.EnsureConfigured();
        HideLegacyCanvas();
    }

    private void Start()
    {
        languages = ApplicationServices.Languages;
        experience = ApplicationServices.ExperienceSettings;
        downloadPolicy = ApplicationServices.ContentDownloadPolicy;
        recovery = new InventoryRecoveryService();
        picker = RecoveryDocumentPicker.GetOrCreate();

        presenter = new MobileSettingsPresenter(gameObject, new MobileSettingsCallbacks
        {
            CycleUiLanguage = CycleUiLanguage,
            CycleCardLanguage = CycleCardLanguage,
            ToggleSound = ToggleSound,
            ToggleReduceMotion = ToggleReduceMotion,
            ToggleHaptics = ToggleHaptics,
            CycleAnimationSpeed = CycleAnimationSpeed,
            ToggleWifiOnly = ToggleWifiOnly,
            ExportSave = ExportSave,
            ChooseImport = ChooseImport,
            ConfirmImport = RequestImportConfirmation,
            ConnectIdentity = ConnectIdentity,
            KeepLocal = () => RequestCloudChoice(InventoryConflictChoice.KeepLocal),
            UseCloud = () => RequestCloudChoice(InventoryConflictChoice.UseCloud),
            SafeMerge = () => RequestCloudChoice(InventoryConflictChoice.SafeMerge),
            Navigate = Navigate
        });

        if (languages != null)
        {
            languages.UiLanguageChanged += OnUiLanguageChanged;
            languages.ContentLanguageChanged += OnContentLanguageChanged;
        }
        if (experience != null)
            experience.Changed += OnExperienceChanged;
        if (downloadPolicy != null)
            downloadPolicy.Changed += OnDownloadPreferencesChanged;
        if (picker != null)
            picker.BusyChanged += OnPickerBusyChanged;
        LocalizationSettings.SelectedLocaleChanged += OnSelectedLocaleChanged;
        GameIdentityService.Changed += OnIdentityChanged;
        GameCloudConflictSession.Current.Changed += OnConflictChanged;
        if (Inventory.Instance != null)
            GameCloudConflictSession.Current.RefreshLocal(Inventory.Instance.Data);
        RefreshAll();
    }

    private void OnDestroy()
    {
        destroyed = true;
        operationGeneration++;
        if (languages != null)
        {
            languages.UiLanguageChanged -= OnUiLanguageChanged;
            languages.ContentLanguageChanged -= OnContentLanguageChanged;
        }
        if (experience != null)
            experience.Changed -= OnExperienceChanged;
        if (downloadPolicy != null)
            downloadPolicy.Changed -= OnDownloadPreferencesChanged;
        if (picker != null)
            picker.BusyChanged -= OnPickerBusyChanged;
        LocalizationSettings.SelectedLocaleChanged -= OnSelectedLocaleChanged;
        GameIdentityService.Changed -= OnIdentityChanged;
        GameCloudConflictSession.Current.Changed -= OnConflictChanged;
        if (pickerRequestActive)
        {
            if (operationOverridesForTests?.CancelPendingPicker != null)
                operationOverridesForTests.CancelPendingPicker();
            else
                picker?.CancelPending();
            pickerRequestActive = false;
        }
        presenter?.Dispose();
        presenter = null;
    }

    public void MenuBtnClick()
    {
        Navigate(MobileDestination.Home);
    }

    private void CycleUiLanguage()
    {
        if (languages == null || languages.AvailableUiLanguageIds.Count == 0)
            return;
        int current = IndexOf(languages.AvailableUiLanguageIds, languages.UiLanguageId);
        try
        {
            languages.SelectUiLanguage(languages.AvailableUiLanguageIds[(current + 1) % languages.AvailableUiLanguageIds.Count]);
            languageStatusKey = "settings.language.status.saved";
            languageStatusError = false;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("UI language preference was not saved: " + exception);
            languageStatusKey = "settings.language.status.failed";
            languageStatusError = true;
        }
        RefreshAll();
    }

    private void CycleCardLanguage()
    {
        if (languages == null)
            return;
        int current = IndexOf(CardLanguageIds, languages.RequestedContentLanguageId);
        try
        {
            languages.SelectContentLanguage(CardLanguageIds[(current + 1) % CardLanguageIds.Length], null);
            languageStatusKey = "settings.language.status.saved";
            languageStatusError = false;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Card language preference was not saved: " + exception);
            languageStatusKey = "settings.language.status.failed";
            languageStatusError = true;
        }
        RefreshAll();
    }

    private void ToggleSound() => UpdateExperience(
        () => experience?.SetSoundEnabled(!experience.Current.SoundEnabled),
        presenter?.SoundAction);

    private void ToggleReduceMotion() => UpdateExperience(
        () => experience?.SetReduceMotion(!experience.Current.ReduceMotion),
        presenter?.MotionAction);

    private void ToggleHaptics()
    {
        bool enabling = experience != null && !experience.Current.HapticsEnabled;
        UpdateExperience(
            () => experience?.SetHapticsEnabled(enabling),
            presenter?.HapticsAction);
        if (enabling && experience?.Current.HapticsEnabled == true)
            UIFeedbackService.Play(FeedbackCue.Confirm, true);
    }

    private void CycleAnimationSpeed()
    {
        if (experience == null)
            return;
        int current = Array.FindIndex(
            AnimationSpeeds,
            value => Math.Abs(value - experience.Current.AnimationSpeed) < 0.01f);
        float next = AnimationSpeeds[(Math.Max(0, current) + 1) % AnimationSpeeds.Length];
        UpdateExperience(() => experience.SetAnimationSpeed(next), presenter?.SpeedAction);
    }

    private void UpdateExperience(
        Func<ExperienceSettingsUpdateResult> update,
        MobileActionControl source)
    {
        if (busy || update == null)
            return;
        ExperienceSettingsUpdateResult result = update();
        if (result == null || !result.Succeeded)
        {
            Debug.LogWarning("Experience preference was not saved: " + result?.ErrorMessage);
            experienceStatusKey = "settings.experience.save_failed_safe";
            experienceStatusError = true;
            UIFeedbackService.Play(FeedbackCue.Error);
        }
        else
        {
            experienceStatusKey = "settings.experience.saved";
            experienceStatusError = false;
        }
        RefreshAll();
    }

    private void ToggleWifiOnly()
    {
        if (busy || downloadPolicy == null)
            return;
        try
        {
            downloadPolicy.SetWifiOnlyForLargeDownloads(!downloadPolicy.Current.WifiOnlyForLargeDownloads);
            downloadStatusKey = "settings.download.status.saved";
            downloadStatusError = false;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Download preference was not saved: " + exception);
            downloadStatusKey = "settings.download.status.failed";
            downloadStatusError = true;
            UIFeedbackService.Play(FeedbackCue.Error);
        }
        RefreshAll();
    }

    private void ExportSave()
    {
        if (busy)
            return;
        UnityPlayerRecoveryTarget target = null;
        if (operationOverridesForTests == null && !TryCreateRecoveryTarget(out target))
            return;
        if (operationOverridesForTests != null && !RecoveryAvailable())
        {
            SetRecoveryUnavailable();
            return;
        }
        int generation = BeginBusy(presenter.ExportAction);
        try
        {
            pickerRequestActive = true;
            Action<MobileSettingsOperationResult> completed = result =>
            {
                pickerRequestActive = false;
                if (!CanComplete(generation))
                    return;
                EndBusy(presenter.ExportAction);
                if (result != null && result.Succeeded)
                {
                    Debug.Log("Recovery export completed through the document picker.");
                    recoveryStatusKey = "settings.recovery.status.exported_safe";
                    recoveryStatusError = false;
                    UIFeedbackService.Play(FeedbackCue.Confirm);
                }
                else if (result != null && result.Cancelled)
                {
                    recoveryStatusKey = "settings.recovery.status.cancelled";
                    recoveryStatusError = false;
                    UIFeedbackService.Play(FeedbackCue.Back);
                }
                else
                {
                    Debug.LogWarning("Recovery export picker failed: " + result?.DeveloperDetail);
                    SetRecoveryFailure();
                }
                RefreshAll();
            };
            if (operationOverridesForTests?.ExportRecovery != null)
            {
                operationOverridesForTests.ExportRecovery(completed);
            }
            else
            {
                string directory = StagingDirectory();
                Directory.CreateDirectory(directory);
                string fileName = "universal-gacha-save-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".gachasave";
                string stagingPath = Path.Combine(directory, fileName);
                recovery.Export(stagingPath, target.Capture(), RecoveryInstallIdentity.GetOrCreate());
                picker.CreateDocument(stagingPath, fileName, result => completed(ToMobileResult(result)));
            }
        }
        catch (Exception exception)
        {
            pickerRequestActive = false;
            Debug.LogWarning("Recovery export failed: " + exception);
            EndBusy(presenter.ExportAction);
            SetRecoveryFailure();
            RefreshAll();
        }
    }

    private void ChooseImport()
    {
        if (busy || (operationOverridesForTests == null && (recovery == null || picker == null)))
            return;
        if (!RecoveryAvailable())
        {
            SetRecoveryUnavailable();
            return;
        }
        int generation = BeginBusy(presenter.ImportAction);
        try
        {
            pickerRequestActive = true;
            Action<MobileSettingsOperationResult> completed = result =>
            {
                pickerRequestActive = false;
                if (!CanComplete(generation))
                    return;
                EndBusy(presenter.ImportAction);
                if (result != null && result.Succeeded)
                    PreviewImport(result.Path);
                else if (result != null && result.Cancelled)
                {
                    recoveryStatusKey = "settings.recovery.status.cancelled";
                    recoveryStatusError = false;
                    UIFeedbackService.Play(FeedbackCue.Back);
                }
                else
                {
                    Debug.LogWarning("Recovery import picker failed: " + result?.DeveloperDetail);
                    SetRecoveryFailure();
                }
                RefreshAll();
            };
            if (operationOverridesForTests?.ChooseImport != null)
            {
                operationOverridesForTests.ChooseImport(completed);
            }
            else
            {
                string directory = StagingDirectory();
                Directory.CreateDirectory(directory);
                string destination = Path.Combine(directory, "incoming-preview.gachasave");
                picker.OpenDocument(destination, result => completed(ToMobileResult(result)));
            }
        }
        catch (Exception exception)
        {
            pickerRequestActive = false;
            Debug.LogWarning("Recovery import picker failed: " + exception);
            EndBusy(presenter.ImportAction);
            SetRecoveryFailure();
            RefreshAll();
        }
    }

    public bool PreviewImport(string path)
    {
        if (busy || string.IsNullOrWhiteSpace(path))
            return false;
        try
        {
            if (operationOverridesForTests?.PreviewRecovery != null)
            {
                pendingPreviewOverride = operationOverridesForTests.PreviewRecovery(path);
                pendingPreview = null;
                if (pendingPreviewOverride == null)
                    throw new InvalidOperationException("The recovery preview was unavailable.");
            }
            else
            {
                pendingPreview = recovery.Preview(path);
                pendingPreviewOverride = null;
            }
            pendingImportPath = path;
            recoveryStatusKey = "settings.recovery.status.preview_ready";
            recoveryStatusError = false;
            UIFeedbackService.Play(FeedbackCue.Confirm);
            RefreshAll();
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Recovery preview failed: " + exception);
            pendingPreview = null;
            pendingPreviewOverride = null;
            pendingImportPath = null;
            SetRecoveryFailure();
            RefreshAll();
            return false;
        }
    }

    private void RequestImportConfirmation()
    {
        if (busy || !HasPendingImport || string.IsNullOrWhiteSpace(pendingImportPath))
            return;
        presenter.Confirmation.Show(
            CardUiText.Get("settings.recovery.confirm.title"),
            CardUiText.Get("settings.recovery.confirm.body"),
            CardUiText.Get("settings.recovery.action.confirm"),
            CardUiText.Get("common.action.cancel"),
            () => ConfirmImport(),
            null,
            true);
    }

    public bool ConfirmImport()
    {
        if (busy || !HasPendingImport || string.IsNullOrWhiteSpace(pendingImportPath))
        {
            return false;
        }
        UnityPlayerRecoveryTarget target = null;
        if (operationOverridesForTests == null && !TryCreateRecoveryTarget(out target))
            return false;
        BeginBusy(presenter.ConfirmImportAction);
        try
        {
            bool restored;
            if (operationOverridesForTests?.RestoreRecovery != null)
            {
                restored = operationOverridesForTests.RestoreRecovery(pendingImportPath);
            }
            else
            {
                string backupDirectory = Path.Combine(Application.persistentDataPath, "Recovery", "Backups");
                Directory.CreateDirectory(backupDirectory);
                string backupPath = Path.Combine(
                    backupDirectory,
                    "pre-import-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff") + ".gachasave");
                restored = recovery.Restore(
                    pendingImportPath,
                    backupPath,
                    target,
                    RecoveryInstallIdentity.GetOrCreate()) != null;
            }
            if (!restored)
                throw new InvalidOperationException("The recovery import did not complete.");
            Debug.Log("Recovery import completed with a verified safety backup.");
            pendingPreview = null;
            pendingPreviewOverride = null;
            pendingImportPath = null;
            recoveryStatusKey = "settings.recovery.status.imported_safe";
            recoveryStatusError = false;
            EndBusy(presenter.ConfirmImportAction);
            UIFeedbackService.Play(FeedbackCue.CollectionNew, true);
            if (CloudSaveServiceWrapper.IsReady && Inventory.Instance != null)
                _ = CloudSaveServiceWrapper.SaveInventoryAsync(Inventory.Instance.Data);
            RefreshAll();
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Recovery import failed: " + exception);
            EndBusy(presenter.ConfirmImportAction);
            SetRecoveryFailure();
            RefreshAll();
            return false;
        }
    }

    private async void ConnectIdentity()
    {
        if (busy || GetIdentityStatus().State != MobileSettingsIdentityState.Available ||
            GetCloudState().HasPending)
        {
            return;
        }
        int generation = BeginBusy(presenter.IdentityAction);
        identityStatusKey = "settings.identity.status.connecting";
        identityStatusError = false;
        RefreshAll();
        MobileSettingsIdentityResultData result;
        try
        {
            result = operationOverridesForTests?.ConnectIdentity != null
                ? await operationOverridesForTests.ConnectIdentity()
                : ToMobileIdentityResult(await GameIdentityService.ConnectAsync());
        }
        catch (Exception exception)
        {
            if (!CanComplete(generation))
                return;
            Debug.LogWarning("Player identity connection failed: " + exception);
            EndBusy(presenter.IdentityAction);
            identityStatusKey = "settings.identity.status.failed_safe";
            identityStatusError = true;
            UIFeedbackService.Play(FeedbackCue.Error);
            RefreshAll();
            return;
        }
        if (!CanComplete(generation))
            return;
        EndBusy(presenter.IdentityAction);
        switch (result?.Outcome ?? MobileSettingsIdentityOutcome.Failed)
        {
            case MobileSettingsIdentityOutcome.Linked:
                identityStatusKey = "settings.identity.status.linked";
                identityStatusError = false;
                UIFeedbackService.Play(FeedbackCue.CollectionNew, true);
                break;
            case MobileSettingsIdentityOutcome.CloudPending:
                Debug.LogWarning("Player identity linked with pending cloud sync: " + result.DeveloperDetail);
                identityStatusKey = "settings.identity.status.cloud_pending_safe";
                identityStatusError = true;
                UIFeedbackService.Play(FeedbackCue.Error);
                break;
            case MobileSettingsIdentityOutcome.ConflictPending:
                identityStatusKey = "settings.identity.status.conflict";
                identityStatusError = false;
                break;
            case MobileSettingsIdentityOutcome.SetupRequired:
                identityStatusKey = "settings.identity.status.setup_required";
                identityStatusError = false;
                break;
            default:
                Debug.LogWarning("Player identity was not changed: " + result?.DeveloperDetail);
                identityStatusKey = "settings.identity.status.failed_safe";
                identityStatusError = true;
                UIFeedbackService.Play(FeedbackCue.Error);
                break;
        }
        RefreshAll();
    }

    private void RequestCloudChoice(InventoryConflictChoice choice)
    {
        if (busy || !GetCloudState().HasPending)
            return;
        presenter.Confirmation.Show(
            CardUiText.Get("settings.cloud.confirm.title"),
            CardUiText.Get("settings.cloud.confirm.body"),
            CardUiText.Get(CloudActionKey(choice)),
            CardUiText.Get("common.action.cancel"),
            () => ResolveCloudChoice(choice),
            null,
            choice == InventoryConflictChoice.UseCloud);
    }

    private async void ResolveCloudChoice(InventoryConflictChoice choice)
    {
        if (busy || !GetCloudState().HasPending)
            return;
        int generation = BeginBusy(CloudAction(choice));
        cloudStatusKey = "settings.cloud.status.resolving";
        cloudStatusError = false;
        RefreshAll();
        if (operationOverridesForTests?.ResolveCloud == null)
        {
            try
            {
                CreateCloudSafetyBackup();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Cloud conflict safety backup failed: " + exception);
                EndBusy(CloudAction(choice));
                cloudStatusKey = "settings.cloud.status.backup_failed_safe";
                cloudStatusError = true;
                UIFeedbackService.Play(FeedbackCue.Error);
                RefreshAll();
                return;
            }
        }

        MobileSettingsOperationResult result;
        try
        {
            if (operationOverridesForTests?.ResolveCloud != null)
            {
                result = await operationOverridesForTests.ResolveCloud(ToMobileCloudChoice(choice));
            }
            else
            {
                InventoryConflictResolutionResult resolved = await GameCloudConflictSession.Current.ResolveAsync(
                    choice,
                    new UnityInventoryConflictTarget());
                result = new MobileSettingsOperationResult(resolved.Succeeded, developerDetail: resolved.Error);
            }
        }
        catch (Exception exception)
        {
            if (!CanComplete(generation))
                return;
            Debug.LogWarning("Cloud conflict resolution failed: " + exception);
            EndBusy(CloudAction(choice));
            cloudStatusKey = "settings.cloud.status.failed_safe";
            cloudStatusError = true;
            UIFeedbackService.Play(FeedbackCue.Error);
            RefreshAll();
            return;
        }
        if (!CanComplete(generation))
            return;
        EndBusy(CloudAction(choice));
        if (result != null && result.Succeeded)
        {
            cloudStatusKey = "settings.cloud.status.resolved_safe";
            cloudStatusError = false;
            UIFeedbackService.Play(FeedbackCue.CollectionNew, true);
        }
        else
        {
            Debug.LogWarning("Cloud conflict resolution failed: " + result?.DeveloperDetail);
            cloudStatusKey = "settings.cloud.status.failed_safe";
            cloudStatusError = true;
            UIFeedbackService.Play(FeedbackCue.Error);
        }
        RefreshAll();
    }

    private void Navigate(MobileDestination destination)
    {
        if (destination == MobileDestination.Settings)
        {
            presenter?.Scroll.ScrollTo(presenter.Scroll.contentContainer[0]);
            return;
        }
        if (navigationRequested || busy)
            return;
        navigationRequested = true;
        operationGeneration++;
        presenter?.SetNavigationPending(destination);
        string sceneName = MobilePrimaryNavigation.SceneName(destination);
        try
        {
            if (sceneLoaderOverrideForTests != null)
                sceneLoaderOverrideForTests(sceneName);
            else if (GameManager.Instance != null && GameManager.Instance.loadManager != null)
                GameManager.Instance.loadManager.LoadScene(sceneName);
            else
                SceneManager.LoadScene(sceneName);
        }
        catch
        {
            navigationRequested = false;
            presenter?.ClearNavigationPending();
            RefreshAvailability();
            throw;
        }
    }

    private void RefreshAll()
    {
        if (destroyed || presenter == null)
            return;
        ExperienceSettings currentExperience = experience?.Current ?? new ExperienceSettings();
        presenter.RefreshText(
            languages?.UiLanguageId ?? "en",
            languages?.RequestedContentLanguageId ?? "en",
            currentExperience,
            downloadPolicy?.Current.WifiOnlyForLargeDownloads ?? true);
        presenter.SetStatus(presenter.LanguageStatus, CardUiText.Get(languageStatusKey), languageStatusError);
        presenter.SetStatus(presenter.ExperienceStatus, CardUiText.Get(experienceStatusKey), experienceStatusError);
        presenter.SetStatus(presenter.DownloadStatus, CardUiText.Get(downloadStatusKey), downloadStatusError);
        presenter.SetRecoveryPreview(pendingPreviewOverride != null
            ? FormatRecoveryPreview(pendingPreviewOverride)
            : pendingPreview == null ? null : FormatRecoveryPreview(pendingPreview));
        presenter.SetStatus(presenter.RecoveryStatus, CardUiText.Get(recoveryStatusKey), recoveryStatusError);
        RefreshIdentity();
        RefreshConflict();
        RefreshAvailability();
    }

    private void RefreshIdentity()
    {
        MobileSettingsIdentityStatusData status = GetIdentityStatus();
        string text;
        bool error = identityStatusError;
        if (!string.IsNullOrWhiteSpace(identityStatusKey))
            text = CardUiText.Get(identityStatusKey);
        else
        {
            switch (status.State)
            {
                case MobileSettingsIdentityState.Connected:
                    text = CardUiText.Format("settings.identity.status.connected", status.RedactedIdentity);
                    break;
                case MobileSettingsIdentityState.Available:
                    text = CardUiText.Get("settings.identity.status.available");
                    break;
                case MobileSettingsIdentityState.Busy:
                    text = CardUiText.Get("settings.identity.status.connecting");
                    break;
                default:
                    text = CardUiText.Get("settings.identity.status.setup_required");
                    break;
            }
        }
        presenter.SetStatus(presenter.IdentityStatus, text, error);
    }

    private void RefreshConflict()
    {
        MobileSettingsCloudStateData state = GetCloudState();
        presenter.SetCloudVisible(state.HasPending);
        if (!state.HasPending)
            return;
        presenter.CloudLocalSummary.text = FormatCloudSummary(state.Local);
        presenter.CloudRemoteSummary.text = FormatCloudSummary(state.Cloud);
        presenter.SetStatus(presenter.CloudStatus, CardUiText.Get(cloudStatusKey), cloudStatusError);
    }

    private void RefreshAvailability()
    {
        if (presenter == null || navigationRequested)
            return;
        bool recoveryAvailable = RecoveryAvailable() && !PickerBusy();
        MobileSettingsIdentityState identity = GetIdentityStatus().State;
        MobileSettingsCloudStateData conflict = GetCloudState();
        bool conflictAvailable = conflict.HasPending && !conflict.IsResolving;
        presenter.ApplyAvailability(
            !busy,
            recoveryAvailable,
            HasPendingImport,
            identity == MobileSettingsIdentityState.Available && !conflict.HasPending,
            conflictAvailable);
    }

    private bool TryCreateRecoveryTarget(out UnityPlayerRecoveryTarget target)
    {
        target = null;
        if (Inventory.Instance == null || languages == null || experience == null)
        {
            recoveryStatusKey = "settings.recovery.status.unavailable";
            recoveryStatusError = true;
            RefreshAll();
            return false;
        }
        target = new UnityPlayerRecoveryTarget(
            Inventory.Instance,
            LocalSaveService.Save,
            languages,
            experience,
            null);
        return true;
    }

    private bool RecoveryAvailable() => operationOverridesForTests?.RecoveryAvailable != null
        ? operationOverridesForTests.RecoveryAvailable()
        : Inventory.Instance != null && ApplicationServices.IsConfigured;

    private bool PickerBusy() => operationOverridesForTests?.PickerBusy != null
        ? operationOverridesForTests.PickerBusy()
        : picker != null && picker.IsBusy;

    private MobileSettingsIdentityStatusData GetIdentityStatus()
    {
        if (operationOverridesForTests?.IdentityStatus != null)
            return operationOverridesForTests.IdentityStatus() ??
                   new MobileSettingsIdentityStatusData(MobileSettingsIdentityState.SetupRequired);
        GameIdentityStatus status = GameIdentityService.GetStatus();
        MobileSettingsIdentityState state = status.Kind switch
        {
            GameIdentityStatusKind.Available => MobileSettingsIdentityState.Available,
            GameIdentityStatusKind.Connected => MobileSettingsIdentityState.Connected,
            GameIdentityStatusKind.Busy => MobileSettingsIdentityState.Busy,
            _ => MobileSettingsIdentityState.SetupRequired
        };
        return new MobileSettingsIdentityStatusData(state, status.RedactedIdentity);
    }

    private MobileSettingsCloudStateData GetCloudState()
    {
        if (operationOverridesForTests?.CloudState != null)
            return operationOverridesForTests.CloudState() ?? new MobileSettingsCloudStateData(false, false);
        InventoryConflictPreview preview = GameCloudConflictSession.Current.PendingPreview;
        return preview == null
            ? new MobileSettingsCloudStateData(false, GameCloudConflictSession.Current.IsResolving)
            : new MobileSettingsCloudStateData(
                true,
                GameCloudConflictSession.Current.IsResolving,
                ToMobileSummary(preview.Local),
                ToMobileSummary(preview.Cloud));
    }

    private void SetRecoveryUnavailable()
    {
        recoveryStatusKey = "settings.recovery.status.unavailable";
        recoveryStatusError = true;
        RefreshAll();
    }

    private int BeginBusy(MobileActionControl source)
    {
        busy = true;
        int generation = ++operationGeneration;
        source?.SetLoading(true);
        RefreshAvailability();
        return generation;
    }

    private void EndBusy(MobileActionControl source)
    {
        busy = false;
        source?.SetLoading(false);
        RefreshAvailability();
    }

    private bool CanComplete(int generation) =>
        !destroyed && generation == operationGeneration && presenter != null;

    private void SetRecoveryFailure()
    {
        recoveryStatusKey = "settings.recovery.status.error_safe";
        recoveryStatusError = true;
        UIFeedbackService.Play(FeedbackCue.Error);
    }

    private void OnUiLanguageChanged(string _) => RefreshAll();
    private void OnContentLanguageChanged(ContentLanguageSelection _) => RefreshAll();
    private void OnExperienceChanged(ExperienceSettings _) => RefreshAll();
    private void OnDownloadPreferencesChanged(ContentDownloadPreferences _) => RefreshAll();
    private void OnPickerBusyChanged(bool _) => RefreshAvailability();
    private void OnSelectedLocaleChanged(Locale _)
    {
        presenter?.Confirmation.Hide();
        RefreshAll();
    }
    private void OnIdentityChanged() => RefreshAll();
    private void OnConflictChanged() => RefreshAll();

    private void HideLegacyCanvas()
    {
        foreach (GameObject sceneRoot in gameObject.scene.GetRootGameObjects())
        {
            foreach (Canvas canvas in sceneRoot.GetComponentsInChildren<Canvas>(true))
            {
                if (canvas != null && canvas.gameObject.scene == gameObject.scene)
                    canvas.gameObject.SetActive(false);
            }
        }
    }

    private void CreateCloudSafetyBackup()
    {
        if (!TryCreateRecoveryTarget(out UnityPlayerRecoveryTarget target))
            throw new InvalidOperationException("Player recovery services are unavailable.");
        string directory = Path.Combine(Application.persistentDataPath, "Recovery", "Backups");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(
            directory,
            "pre-cloud-choice-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff") + ".gachasave");
        recovery.Export(path, target.Capture(), RecoveryInstallIdentity.GetOrCreate());
    }

    private static string FormatRecoveryPreview(InventoryRecoveryPreview preview) =>
        CardUiText.Format(
            "settings.recovery.preview",
            preview.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
            preview.DistinctPrintingCount,
            preview.TotalCardCount,
            preview.TotalProductsOpened,
            preview.HistoryCount,
            preview.UiLanguageId,
            preview.ContentLanguageId);

    private static string FormatRecoveryPreview(MobileSettingsRecoveryPreviewData preview) =>
        CardUiText.Format(
            "settings.recovery.preview",
            preview.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
            preview.DistinctPrintings,
            preview.TotalCards,
            preview.TotalProducts,
            preview.HistoryCount,
            preview.UiLanguageId,
            preview.ContentLanguageId);

    private static string FormatCloudSummary(InventoryProgressSummary summary)
    {
        string time = summary.LastModifiedUtc == DateTime.MinValue
            ? "—"
            : summary.LastModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        return CardUiText.Format(
            "settings.cloud.summary",
            time,
            summary.DistinctPrintingCount,
            summary.TotalCardCount,
            summary.TotalProductsOpened,
            summary.HistoryCount);
    }

    private static string FormatCloudSummary(MobileSettingsProgressSummaryData summary)
    {
        if (summary == null)
            return CardUiText.Get("settings.cloud.status.none");
        string time = summary.LastModifiedUtc == DateTime.MinValue
            ? "\u2014"
            : summary.LastModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        return CardUiText.Format(
            "settings.cloud.summary",
            time,
            summary.DistinctPrintings,
            summary.TotalCards,
            summary.TotalProducts,
            summary.HistoryCount);
    }

    private static MobileSettingsOperationResult ToMobileResult(RecoveryDocumentPickerResult result) =>
        result == null
            ? new MobileSettingsOperationResult(false, developerDetail: "Document picker returned no result.")
            : new MobileSettingsOperationResult(result.Succeeded, result.Cancelled, result.Path, result.Error);

    private static MobileSettingsIdentityResultData ToMobileIdentityResult(GameIdentityConnectResult result)
    {
        if (result == null)
            return new MobileSettingsIdentityResultData(MobileSettingsIdentityOutcome.Failed);
        MobileSettingsIdentityOutcome outcome = result.Outcome switch
        {
            GameIdentityConnectOutcome.LinkedCurrentPlayer => MobileSettingsIdentityOutcome.Linked,
            GameIdentityConnectOutcome.ExistingPlayerReady => MobileSettingsIdentityOutcome.Linked,
            GameIdentityConnectOutcome.LinkedCurrentPlayerCloudPending => MobileSettingsIdentityOutcome.CloudPending,
            GameIdentityConnectOutcome.ConflictPending => MobileSettingsIdentityOutcome.ConflictPending,
            GameIdentityConnectOutcome.ExternalSetupRequired => MobileSettingsIdentityOutcome.SetupRequired,
            _ => MobileSettingsIdentityOutcome.Failed
        };
        return new MobileSettingsIdentityResultData(outcome, result.Error);
    }

    private static MobileSettingsProgressSummaryData ToMobileSummary(InventoryProgressSummary summary) =>
        new MobileSettingsProgressSummaryData(
            summary.LastModifiedUtc,
            summary.DistinctPrintingCount,
            summary.TotalCardCount,
            summary.TotalProductsOpened,
            summary.HistoryCount);

    private static MobileSettingsCloudChoice ToMobileCloudChoice(InventoryConflictChoice choice) => choice switch
    {
        InventoryConflictChoice.KeepLocal => MobileSettingsCloudChoice.KeepLocal,
        InventoryConflictChoice.UseCloud => MobileSettingsCloudChoice.UseCloud,
        _ => MobileSettingsCloudChoice.SafeMerge
    };

    private MobileActionControl CloudAction(InventoryConflictChoice choice) => choice switch
    {
        InventoryConflictChoice.KeepLocal => presenter.KeepLocalAction,
        InventoryConflictChoice.UseCloud => presenter.UseCloudAction,
        _ => presenter.MergeAction
    };

    private static string CloudActionKey(InventoryConflictChoice choice) => choice switch
    {
        InventoryConflictChoice.KeepLocal => "settings.cloud.action.local",
        InventoryConflictChoice.UseCloud => "settings.cloud.action.remote",
        _ => "settings.cloud.action.merge"
    };

    private static int IndexOf(System.Collections.Generic.IReadOnlyList<string> values, string current)
    {
        for (int index = 0; index < values.Count; index++)
        {
            if (string.Equals(values[index], current, StringComparison.OrdinalIgnoreCase))
                return index;
        }
        return -1;
    }

    private static string StagingDirectory() =>
        Path.Combine(Application.persistentDataPath, "Recovery", "Staging");

    private sealed class UnityInventoryConflictTarget : IInventoryConflictTarget
    {
        public InventoryData CaptureLocal() =>
            CloudInventoryConflictCoordinator.Clone(Inventory.Instance?.Data);

        public void ApplyLocal(InventoryData inventory)
        {
            if (Inventory.Instance == null)
                throw new InvalidOperationException("The player inventory is unavailable.");
            Inventory.Instance.ReplaceData(CloudInventoryConflictCoordinator.Clone(inventory));
            LocalSaveService.Save(Inventory.Instance.Data);
        }

        public Task<bool> SaveCloudAsync(InventoryData inventory) =>
            CloudSaveServiceWrapper.SaveInventoryForConflictResolutionAsync(inventory);
    }
}
