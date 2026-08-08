using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Gacha.Application;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Gacha.Presentation
{
    public sealed class ContentManagementController : MonoBehaviour
    {
        private const string StringTable = "Card_UI";

        private static readonly IReadOnlyDictionary<string, string> EnglishFallbacks =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["content.title"] = "Content Library",
                ["content.subtitle"] = "Install, update, repair, or remove content packs without losing collection progress",
                ["content.action.back"] = "Main menu",
                ["content.action.refresh"] = "Refresh catalog",
                ["content.action.install"] = "Install",
                ["content.action.update"] = "Update",
                ["content.action.repair"] = "Repair",
                ["content.action.resume"] = "Resume",
                ["content.action.retry"] = "Retry",
                ["content.action.pause"] = "Pause",
                ["content.action.cancel"] = "Cancel",
                ["content.action.download"] = "Download",
                ["content.action.remove"] = "Remove",
                ["content.action.confirm_remove"] = "Confirm remove",
                ["content.filter.search"] = "Search name or set number",
                ["content.filter.language"] = "Card language",
                ["content.filter.generation"] = "Generation",
                ["content.filter.install"] = "Status",
                ["content.filter.all"] = "All",
                ["content.filter.language.en"] = "English cards",
                ["content.filter.language.ja"] = "Japanese cards",
                ["content.filter.language.zh-cn"] = "Simplified Chinese cards",
                ["content.filter.installed"] = "Installed",
                ["content.filter.not_installed"] = "Not installed",
                ["content.filter.update"] = "Update available",
                ["content.action.select_filtered"] = "Select shown",
                ["content.action.clear_selection"] = "Clear selection",
                ["content.action.download_selected"] = "Download selected",
                ["content.selection.none"] = "Select packs to calculate download and storage size.",
                ["content.selection.summary"] = "{0} selected + {1} dependencies · {2} download · {3} installed",
                ["content.queue.summary"] = "{0} queued · {1} running · {2} complete · {3} failed",
                ["content.queue.restore_warning"] = "Queue recovery warning: {0}",
                ["content.policy.wifi_only"] = "Wi-Fi only for downloads of 100 MB or more",
                ["content.action.confirm_cellular"] = "Confirm mobile download",
                ["content.preflight.ready"] = "{0} free · {1} required · {2}",
                ["content.preflight.current"] = "Selected content is already installed.",
                ["content.preflight.offline"] = "Offline · connect to continue.",
                ["content.preflight.waiting_wifi"] = "{0} download is waiting for Wi-Fi.",
                ["content.preflight.cellular_confirmation"] = "Mobile data · confirm the {0} download.",
                ["content.preflight.insufficient_space"] = "Not enough storage · {0} free · {1} required.",
                ["content.preflight.storage_unavailable"] = "Available storage could not be read.",
                ["content.preflight.network_unavailable"] = "Network type could not be confirmed.",
                ["content.network.wifi"] = "Wi-Fi",
                ["content.network.mobile"] = "Mobile data",
                ["content.network.offline"] = "Offline",
                ["content.network.unknown"] = "Unknown network",
                ["content.catalog.loading"] = "Checking available content...",
                ["content.catalog.loaded"] = "{0} content packs available.",
                ["content.catalog.empty"] = "No downloadable content is listed in this catalog.",
                ["content.filter.empty"] = "No content packs match the current filters.",
                ["content.catalog.unavailable"] = "The content catalog is unavailable: {0}",
                ["content.catalog.not_configured"] = "Remote content is not configured yet.",
                ["content.catalog.cached"] = "Offline · showing {0} packs from the last verified catalog.",
                ["content.catalog.cache_warning"] = "{0} packs available, but the offline catalog cache could not be updated.",
                ["content.package.metadata"] = "Version {0} · {1}",
                ["content.status.ready"] = "Ready to install",
                ["content.status.checking"] = "Checking storage and installed version...",
                ["content.status.blocked"] = "Cannot start",
                ["content.status.insufficient_space"] = "Not enough storage",
                ["content.status.invalid_package"] = "Package metadata is invalid",
                ["content.status.storage_unavailable"] = "Storage is unavailable",
                ["content.status.downloading"] = "Downloading",
                ["content.status.paused"] = "Download paused",
                ["content.status.installing"] = "Verifying and installing...",
                ["content.status.installed"] = "Installed",
                ["content.status.current"] = "Already up to date",
                ["content.status.cancelled"] = "Cancelled",
                ["content.status.failed"] = "Operation failed",
                ["content.status.warning"] = "Installed with cleanup warning: {0}",
                ["content.status.update_available"] = "Update available",
                ["content.status.remove_confirm"] = "Remove downloaded cards? Collection progress stays saved.",
                ["content.status.removing"] = "Removing downloaded content...",
                ["content.status.removed"] = "Content removed. Collection progress is still saved.",
                ["content.status.remove_failed"] = "Content removal failed: {0}",
                ["content.status.remove_warning"] = "Content removed with cleanup warning: {0}",
                ["content.progress"] = "{0}% · {1} / {2}",
                ["content.recommended.title"] = "Recommended first pack selected",
                ["content.recommended.body"] = "{0} is ready for review. Check storage and network details, then confirm the download.",
                ["content.action.choose_another"] = "Choose another",
                ["content.confirm.download.title"] = "Confirm content download",
                ["content.confirm.download.body"] = "{0} selected · {1} required packages\nDownload {2} · Install {3}\n{4}",
                ["content.confirm.download.package_body"] = "{0}\n{1} required packages · Download {2} · Install {3}\n{4}",
                ["content.confirm.remove.title"] = "Remove downloaded content?",
                ["content.confirm.remove.body"] = "Remove {0} from this device? Your collection progress stays saved.",
                ["content.action.confirm_download"] = "Download",
                ["content.action.cancel_confirmation"] = "Cancel",
                ["content.status.queued"] = "Queued for download"
            };

        private sealed class PackageRow
        {
            public PackageRow(
                Action<string> primary,
                Action<string> pause,
                Action<string> cancel,
                Action<string> remove,
                Action<string, bool> selectionChanged)
            {
                Root = new VisualElement();
                Root.AddToClassList("content-package-row");

                var copy = new VisualElement();
                copy.AddToClassList("content-package-row__copy");
                Selection = new Toggle();
                Selection.AddToClassList("content-package-row__selection");
                Selection.RegisterValueChangedCallback(evt =>
                {
                    if (!bindingSelection && Entry != null)
                        selectionChanged(Entry.Package.PackageId, evt.newValue);
                });
                Name = new Label();
                Name.AddToClassList("content-package-row__name");
                Metadata = new Label();
                Metadata.AddToClassList("content-package-row__metadata");
                Status = new Label();
                Status.AddToClassList("content-package-row__status");
                copy.Add(Name);
                copy.Add(Metadata);
                copy.Add(Status);

                var controls = new VisualElement();
                controls.AddToClassList("content-package-row__controls");
                Progress = new ProgressBar { lowValue = 0f, highValue = 100f };
                Progress.AddToClassList("content-package-row__progress");
                var actions = new VisualElement();
                actions.AddToClassList("content-package-row__actions");
                Actions = actions;
                Download = ActionControl("download", () => primary(Entry?.Package.PackageId));
                Pause = ActionControl("pause", () => pause(Entry?.Package.PackageId));
                Remove = ActionControl("remove", () => remove(Entry?.Package.PackageId));
                Cancel = ActionControl("cancel", () => cancel(Entry?.Package.PackageId));
                Download.Root.AddToClassList("content-button--primary");
                Pause.Root.AddToClassList("content-button--quiet");
                Remove.Root.AddToClassList("content-button--danger");
                Cancel.Root.AddToClassList("content-button--quiet");
                Download.Root.AddToClassList("content-package-row__download-action");
                Pause.Root.AddToClassList("content-package-row__pause-action");
                Remove.Root.AddToClassList("content-package-row__remove-action");
                Cancel.Root.AddToClassList("content-package-row__cancel-action");
                actions.Add(Cancel.Root);
                actions.Add(Remove.Root);
                actions.Add(Pause.Root);
                actions.Add(Download.Root);
                controls.Add(Progress);
                controls.Add(actions);
                Root.Add(Selection);
                Root.Add(copy);
                Root.Add(controls);
            }

            public ContentPackageCatalogEntry Entry { get; private set; }
            public VisualElement Root { get; }
            public Label Name { get; }
            public Toggle Selection { get; }
            public Label Metadata { get; }
            public Label Status { get; }
            public ProgressBar Progress { get; }
            public VisualElement Actions { get; }
            public MobileActionControl Download { get; }
            public MobileActionControl Pause { get; }
            public MobileActionControl Remove { get; }
            public MobileActionControl Cancel { get; }
            public InstalledContentPackage Installed { get; set; }
            public string LifecycleError { get; set; }
            public bool Removing { get; set; }
            public ContentPackageOperationState? LastState { get; set; }
            public IVisualElementScheduledItem Animation { get; set; }
            public bool DownloadAllowed { get => Download.Allowed; set => Download.Allowed = value; }
            public bool PauseAllowed { get => Pause.Allowed; set => Pause.Allowed = value; }
            public bool RemoveAllowed { get => Remove.Allowed; set => Remove.Allowed = value; }
            public bool CancelAllowed { get => Cancel.Allowed; set => Cancel.Allowed = value; }
            private bool bindingSelection;

            public void Bind(ContentPackageCatalogEntry entry, string displayName, bool selected)
            {
                Animation?.Pause();
                Entry = entry ?? throw new ArgumentNullException(nameof(entry));
                Root.name = "package-" + entry.Package.PackageId;
                Name.text = string.IsNullOrWhiteSpace(displayName)
                    ? entry.Package.PackageId
                    : displayName;
                bindingSelection = true;
                Selection.SetValueWithoutNotify(selected);
                bindingSelection = false;
                Root.EnableInClassList("is-selected", selected);
                Metadata.text = string.Empty;
                Status.text = string.Empty;
                Installed = null;
                LifecycleError = null;
                Removing = false;
                LastState = null;
                Animation = null;
                Root.style.opacity = 1f;
                Root.style.translate = new Translate(0f, 0f, 0f);
            }

            public void Unbind()
            {
                Animation?.Pause();
                Entry = null;
                Installed = null;
                LifecycleError = null;
                Removing = false;
                LastState = null;
                Animation = null;
            }

            private static MobileActionControl ActionControl(string name, Action clicked)
            {
                var root = new VisualElement { name = name + "-button" };
                root.AddToClassList("content-button");
                var label = new Label { name = name + "-button-label" };
                label.AddToClassList("content-button__label");
                root.Add(label);
                return new MobileActionControl(root, clicked, fallbackLabelClass: "content-button__label");
            }
        }

        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private VisualTreeAsset viewAsset;

        private readonly Dictionary<string, PackageRow> rows =
            new Dictionary<string, PackageRow>(StringComparer.Ordinal);
        private readonly Dictionary<string, ContentPackageInstallCoordinator> operations =
            new Dictionary<string, ContentPackageInstallCoordinator>(StringComparer.Ordinal);
        private readonly Dictionary<string, ContentPackageOperationUiBridge> bridges =
            new Dictionary<string, ContentPackageOperationUiBridge>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> localized =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> notifiedFailures = new HashSet<string>(StringComparer.Ordinal);

        private IContentPackageCatalogProvider catalogProvider;
        private IContentPackageInstallCoordinatorFactory operationFactory;
        private IContentPackageLifecycleService lifecycleService;
        private IUiThreadDispatcher dispatcher;
        private ExperienceSettingsService experienceSettings;
        private VisualElement pageRoot;
        private VisualElement shell;
        private MobilePageShell mobilePageShell;
        private MobileTopBar mobileTopBar;
        private MobileConfirmationPresenter confirmationPresenter;
        private ListView packageList;
        private TextField searchFilter;
        private DropdownField languageFilter;
        private DropdownField generationFilter;
        private DropdownField installFilter;
        private Toggle wifiOnlyToggle;
        private Label selectionSummaryLabel;
        private Label title;
        private Label subtitle;
        private Label catalogStatus;
        private Label emptyState;
        private VisualElement launchBanner;
        private Label launchTitle;
        private Label launchBody;
        private MobileActionControl backAction;
        private MobileActionControl refreshAction;
        private MobileActionControl selectFilteredAction;
        private MobileActionControl clearSelectionAction;
        private MobileActionControl downloadSelectedAction;
        private MobileActionControl queuePauseAction;
        private MobileActionControl queueResumeAction;
        private MobileActionControl queueRetryAction;
        private MobileActionControl queueCancelAction;
        private MobileActionControl errorRetryAction;
        private MobileActionControl errorHomeAction;
        private MobileActionControl clearLaunchAction;
        private MobilePrimaryNavigation primaryNavigation;
        private PlayerUiErrorPresenter errorPresenter;
        private ContentPackageCatalog catalog;
        private ContentPackageLibrarySnapshot library;
        private readonly List<ContentPackageLibraryItem> displayedItems =
            new List<ContentPackageLibraryItem>();
        private readonly HashSet<string> selectedPackageIds =
            new HashSet<string>(StringComparer.Ordinal);
        private ContentPackageSelectionSummary selectionSummary;
        private ContentDownloadPolicyService downloadPolicy;
        private ContentDownloadPreflightResult downloadPreflight;
        private IContentPackageQueueStateStore queueStateStore;
        private ContentPackageInstallQueue installQueue;
        private ContentPackageQueueSnapshot queueSnapshot;
        private bool queueCompletionNotified;
        private bool bindingWifiOnly;
        private bool cellularConfirmationArmed;
        private bool mobileAuthorizedForBatch;
        private readonly HashSet<string> mobileAuthorizedPackages =
            new HashSet<string>(StringComparer.Ordinal);
        private string pendingCellularPackageId;
        private ContentNetworkType lastNetworkType = ContentNetworkType.Unknown;
        private float nextNetworkPollTime;
        private CancellationTokenSource loadCancellation;
        private Coroutine localizationRoutine;
        private Coroutine entranceAnimation;
        private int loadGeneration;
        private bool destroyed;
        private bool catalogUsedCache;
        private bool catalogHasCacheWarning;
        private bool catalogQueryAllowed;
        private bool initialCatalogLoadPending;
        private bool navigationRequested;
        private string launchedRecommendationId;

        public static IContentPackageCatalogProvider CatalogProviderOverride { private get; set; }
        public static IContentPackageInstallCoordinatorFactory OperationFactoryOverride { private get; set; }
        public static IContentPackageLifecycleService LifecycleOverride { private get; set; }
        public static IUiThreadDispatcher DispatcherOverride { private get; set; }
        public static ContentDownloadPolicyService DownloadPolicyOverride { private get; set; }
        public static IContentPackageQueueStateStore QueueStateStoreOverride { private get; set; }

        public bool IsReady { get; private set; }
        public string InitializationError { get; private set; }
        public int PackageCount => catalog?.Packages.Count ?? 0;
        public int FilteredPackageCount => library?.FilteredCount ?? 0;
        public int VisibleRowCount => rows.Count;
        public int SelectedPackageCount => selectedPackageIds.Count;
        public ContentPackageSelectionSummary SelectionSummary => selectionSummary;
        public ContentDownloadPreflightResult DownloadPreflight => downloadPreflight;
        public ContentPackageQueueSnapshot QueueSnapshot => queueSnapshot;
        public int LastAppliedThreadId { get; private set; }

        public ContentPackageOperationState? GetPackageState(string packageId)
        {
            if (packageId == null || catalog?.Find(packageId) == null)
                return null;
            return operations.TryGetValue(packageId, out ContentPackageInstallCoordinator operation)
                ? operation.Current.State
                : ContentPackageOperationState.Idle;
        }

        public bool StartOrRetryPackage(string packageId)
        {
            if (packageId == null || catalog?.Find(packageId) == null)
                return false;
            ContentPackageInstallCoordinator operation = EnsureOperation(packageId);
            ContentPackageOperationState state = operation.Current.State;
            if (state == ContentPackageOperationState.Planning ||
                state == ContentPackageOperationState.Downloading ||
                state == ContentPackageOperationState.Installing ||
                state == ContentPackageOperationState.Succeeded ||
                state == ContentPackageOperationState.AlreadyCurrent ||
                ContentPackageLibrary.IsCurrent(
                    LookupInstalled(packageId), catalog.Find(packageId).Package))
                return false;
            if (!PreparePackageDownload(packageId))
                return false;
            StartPreparedPackage(packageId);
            return true;
        }

        public bool PausePackage(string packageId)
        {
            if (packageId == null ||
                !operations.TryGetValue(packageId, out ContentPackageInstallCoordinator operation) ||
                !operation.Current.CanPause)
                return false;
            PauseClicked(packageId);
            return true;
        }

        public bool CancelPackage(string packageId)
        {
            if (packageId == null ||
                !operations.TryGetValue(packageId, out ContentPackageInstallCoordinator operation) ||
                !CanShowCancel(operation.Current.State))
                return false;
            CancelClicked(packageId);
            return true;
        }

        public bool RequestRemovePackage(string packageId)
        {
            if (packageId == null ||
                !rows.TryGetValue(packageId, out PackageRow row) ||
                !row.RemoveAllowed)
                return false;
            RemoveClicked(packageId);
            return true;
        }

        public bool IsPackageInstalled(string packageId)
        {
            return packageId != null && LookupInstalled(packageId) != null;
        }

        public void SelectAllFilteredPackages()
        {
            foreach (ContentPackageLibraryItem item in displayedItems)
                selectedPackageIds.Add(item.Package.PackageId);
            cellularConfirmationArmed = false;
            RefreshSelectionUi();
        }

        public void ClearPackageSelection()
        {
            selectedPackageIds.Clear();
            cellularConfirmationArmed = false;
            RefreshSelectionUi();
        }

        public bool StartSelectedPackages()
        {
            if (installQueue == null || selectedPackageIds.Count == 0 ||
                !PrepareBatchDownload())
                return false;
            if (queueSnapshot?.IsComplete == true)
            {
                installQueue.Changed -= OnQueueChanged;
                installQueue = new ContentPackageInstallQueue(
                    catalog,
                    operationFactory,
                    ContentPackageInstallQueue.DefaultMaximumConcurrentDownloads,
                    queueStateStore,
                    EnsureOperation);
                installQueue.Changed += OnQueueChanged;
                queueSnapshot = installQueue.Current;
            }
            queueCompletionNotified = false;
            mobileAuthorizedForBatch =
                downloadPreflight?.NetworkType == ContentNetworkType.MobileData;
            installQueue.EnqueueSelection(selectedPackageIds);
            UIFeedbackService.Play(FeedbackCue.DownloadStart);
            if (queueSnapshot?.Paused == true)
                _ = installQueue.ResumeAsync();
            else
                _ = installQueue.StartAsync();
            cellularConfirmationArmed = false;
            return true;
        }

        public bool PauseInstallQueue()
        {
            if (installQueue == null || queueSnapshot?.RunningCount <= 0)
                return false;
            _ = installQueue.PauseAsync();
            return true;
        }

        public bool ResumeInstallQueue()
        {
            if (installQueue == null || queueSnapshot == null || !queueSnapshot.Paused)
                return false;
            return StartSelectedPackages();
        }

        public bool RetryFailedQueueItems()
        {
            if (installQueue == null || queueSnapshot?.FailedCount <= 0 ||
                !PrepareBatchDownload())
                return false;
            queueCompletionNotified = false;
            mobileAuthorizedForBatch =
                downloadPreflight?.NetworkType == ContentNetworkType.MobileData;
            _ = installQueue.RetryFailedAsync();
            cellularConfirmationArmed = false;
            return true;
        }

        public bool RefreshDownloadNetworkState()
        {
            if (destroyed || downloadPolicy == null)
                return false;
            ContentNetworkType current = downloadPolicy.GetNetworkType();
            if (current != lastNetworkType)
            {
                if (current == ContentNetworkType.WifiOrEthernet)
                {
                    mobileAuthorizedForBatch = false;
                    mobileAuthorizedPackages.Clear();
                }
                lastNetworkType = current;
                cellularConfirmationArmed = false;
                pendingCellularPackageId = null;
                RefreshSelectionUi();
            }

            bool pauseQueue = queueSnapshot?.RunningCount > 0 && ShouldPauseBatchForNetwork(current);
            if (pauseQueue)
                _ = installQueue.PauseAsync();

            var queuedRunning = new HashSet<string>(
                queueSnapshot?.Items
                    .Where(value => value.State == ContentPackageQueueItemState.Running)
                    .Select(value => value.PackageId) ?? Array.Empty<string>(),
                StringComparer.Ordinal);
            bool pausedSingle = false;
            foreach (KeyValuePair<string, ContentPackageInstallCoordinator> pair in operations)
            {
                if (queuedRunning.Contains(pair.Key) ||
                    pair.Value.Current.State != ContentPackageOperationState.Downloading ||
                    !ShouldPausePackageForNetwork(pair.Key, current))
                    continue;
                _ = pair.Value.PauseAsync();
                pausedSingle = true;
            }
            if (pauseQueue || pausedSingle)
                UIFeedbackService.Play(FeedbackCue.Confirm);
            return pauseQueue || pausedSingle;
        }

        public bool RetryCatalog()
        {
            if (destroyed || errorPresenter == null)
                return false;
            UIFeedbackService.Play(FeedbackCue.ButtonClick);
            _ = ReloadAfterSuspendAsync();
            return true;
        }

        public bool CancelInstallQueue()
        {
            if (installQueue == null || queueSnapshot == null ||
                queueSnapshot.Items.Count == 0 || queueSnapshot.IsComplete)
                return false;
            _ = installQueue.CancelAsync();
            return true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOverrides()
        {
            CatalogProviderOverride = null;
            OperationFactoryOverride = null;
            LifecycleOverride = null;
            DispatcherOverride = null;
            DownloadPolicyOverride = null;
            QueueStateStoreOverride = null;
        }

        private void Awake()
        {
            if (uiDocument == null)
                uiDocument = GetComponent<UIDocument>();
            if (uiDocument != null && uiDocument.visualTreeAsset == null && viewAsset != null)
                uiDocument.visualTreeAsset = viewAsset;
            dispatcher = DispatcherOverride ?? new SynchronizationContextUiThreadDispatcher(
                SynchronizationContext.Current ?? throw new InvalidOperationException("Unity UI synchronization context is unavailable."));
        }

        private void Start()
        {
            try
            {
                BuildView();
                catalogProvider = CatalogProviderOverride ?? ApplicationServices.ContentPackageCatalogs;
                operationFactory = OperationFactoryOverride ?? ApplicationServices.ContentPackageOperations;
                lifecycleService = LifecycleOverride ?? ApplicationServices.ContentPackageLifecycle;
                downloadPolicy = DownloadPolicyOverride ?? ApplicationServices.ContentDownloadPolicy;
                queueStateStore = QueueStateStoreOverride ?? ApplicationServices.ContentPackageQueueState;
                if (downloadPolicy != null)
                {
                    downloadPolicy.Changed += OnDownloadPreferencesChanged;
                    lastNetworkType = downloadPolicy.GetNetworkType();
                    ApplyDownloadPreferences(downloadPolicy.Current);
                }
                experienceSettings = ApplicationServices.ExperienceSettings;
                if (experienceSettings != null)
                    experienceSettings.Changed += OnExperienceSettingsChanged;
                LocalizationSettings.SelectedLocaleChanged += OnSelectedLocaleChanged;
                initialCatalogLoadPending = true;
                RefreshLocalization();
                ApplyMotionPreference();
                PlayEntrance();
            }
            catch (Exception exception)
            {
                ShowInitializationFailure(exception.Message);
            }
        }

        private void OnDestroy()
        {
            confirmationPresenter?.Dispose();
            confirmationPresenter = null;
            mobilePageShell?.Dispose();
            mobilePageShell = null;
            errorPresenter?.Dispose();
            errorPresenter = null;
            DisposeGlobalActions();
            _ = SuspendAllOperationsAsync();
            destroyed = true;
            loadGeneration++;
            loadCancellation?.Cancel();
            loadCancellation?.Dispose();
            loadCancellation = null;
            if (localizationRoutine != null)
                StopCoroutine(localizationRoutine);
            localizationRoutine = null;
            StopEntranceAnimation();
            LocalizationSettings.SelectedLocaleChanged -= OnSelectedLocaleChanged;
            if (experienceSettings != null)
                experienceSettings.Changed -= OnExperienceSettingsChanged;
            experienceSettings = null;
            if (downloadPolicy != null)
                downloadPolicy.Changed -= OnDownloadPreferencesChanged;
            downloadPolicy = null;
            if (installQueue != null)
            {
                installQueue.Changed -= OnQueueChanged;
            }
            installQueue = null;
            queueStateStore = null;
            ClearOperations();
        }

        private void DisposeGlobalActions()
        {
            backAction?.Dispose();
            refreshAction?.Dispose();
            selectFilteredAction?.Dispose();
            clearSelectionAction?.Dispose();
            downloadSelectedAction?.Dispose();
            queuePauseAction?.Dispose();
            queueResumeAction?.Dispose();
            queueRetryAction?.Dispose();
            queueCancelAction?.Dispose();
            errorRetryAction?.Dispose();
            errorHomeAction?.Dispose();
            clearLaunchAction?.Dispose();
            primaryNavigation?.Dispose();
            backAction = null;
            refreshAction = null;
            selectFilteredAction = null;
            clearSelectionAction = null;
            downloadSelectedAction = null;
            queuePauseAction = null;
            queueResumeAction = null;
            queueRetryAction = null;
            queueCancelAction = null;
            errorRetryAction = null;
            errorHomeAction = null;
            clearLaunchAction = null;
            primaryNavigation = null;
        }

        private void BuildView()
        {
            if (uiDocument == null)
                throw new InvalidOperationException("Content management scene has no UIDocument.");
            VisualElement legacyRoot = uiDocument.rootVisualElement.Q<VisualElement>("content-management");
            if (legacyRoot == null)
                throw new InvalidOperationException("ContentManagementView.uxml is not attached to the UIDocument.");
            shell = legacyRoot.Q<VisualElement>("content-shell");
            packageList = legacyRoot.Q<ListView>("package-list");
            searchFilter = legacyRoot.Q<TextField>("content-search");
            languageFilter = legacyRoot.Q<DropdownField>("content-language-filter");
            generationFilter = legacyRoot.Q<DropdownField>("content-generation-filter");
            installFilter = legacyRoot.Q<DropdownField>("content-install-filter");
            wifiOnlyToggle = legacyRoot.Q<Toggle>("content-wifi-only");
            selectionSummaryLabel = legacyRoot.Q<Label>("content-selection-summary");
            catalogStatus = legacyRoot.Q<Label>("catalog-status");
            emptyState = legacyRoot.Q<Label>("content-empty");
            launchBanner = legacyRoot.Q<VisualElement>("content-launch-banner");
            launchTitle = legacyRoot.Q<Label>("content-launch-title");
            launchBody = legacyRoot.Q<Label>("content-launch-body");
            VisualElement legacyHeader = legacyRoot.Q<VisualElement>(className: "content-management__header");
            VisualElement backRoot = legacyRoot.Q<VisualElement>("back-button");
            VisualElement refreshRoot = legacyRoot.Q<VisualElement>("refresh-button");
            VisualElement selectFilteredRoot = legacyRoot.Q<VisualElement>("select-filtered-button");
            VisualElement clearSelectionRoot = legacyRoot.Q<VisualElement>("clear-selection-button");
            VisualElement downloadSelectedRoot = legacyRoot.Q<VisualElement>("download-selected-button");
            VisualElement queuePauseRoot = legacyRoot.Q<VisualElement>("queue-pause-button");
            VisualElement queueResumeRoot = legacyRoot.Q<VisualElement>("queue-resume-button");
            VisualElement queueRetryRoot = legacyRoot.Q<VisualElement>("queue-retry-button");
            VisualElement queueCancelRoot = legacyRoot.Q<VisualElement>("queue-cancel-button");
            VisualElement errorPanel = legacyRoot.Q<VisualElement>("content-error-panel");
            Label errorTitle = legacyRoot.Q<Label>("content-error-title");
            Label errorBody = legacyRoot.Q<Label>("content-error-body");
            VisualElement errorRetryRoot = legacyRoot.Q<VisualElement>("content-error-retry");
            VisualElement errorHomeRoot = legacyRoot.Q<VisualElement>("content-error-home");
            VisualElement clearLaunchRoot = legacyRoot.Q<VisualElement>("content-launch-clear");
            if (shell == null || packageList == null || searchFilter == null ||
                languageFilter == null || generationFilter == null || installFilter == null ||
                wifiOnlyToggle == null || selectionSummaryLabel == null || selectFilteredRoot == null ||
                clearSelectionRoot == null || downloadSelectedRoot == null ||
                queuePauseRoot == null || queueResumeRoot == null || queueRetryRoot == null ||
                queueCancelRoot == null ||
                catalogStatus == null || emptyState == null || backRoot == null || refreshRoot == null ||
                errorPanel == null || errorTitle == null || errorBody == null ||
                errorRetryRoot == null || errorHomeRoot == null ||
                launchBanner == null || launchTitle == null || launchBody == null ||
                clearLaunchRoot == null || legacyHeader == null)
                throw new InvalidOperationException("Content management view is missing required named elements.");

            backAction = new MobileActionControl(backRoot, BackToMenu);
            refreshAction = new MobileActionControl(refreshRoot, RefreshClicked);
            errorRetryAction = new MobileActionControl(errorRetryRoot, () => RetryCatalog());
            errorHomeAction = new MobileActionControl(errorHomeRoot, BackToMenu);
            clearLaunchAction = new MobileActionControl(clearLaunchRoot, ClearRecommendedLaunch);
            mobilePageShell = new MobilePageShell("content-management");
            mobilePageShell.Root.AddToClassList("content-management");
            mobileTopBar = new MobileTopBar(string.Empty, string.Empty);
            mobilePageShell.HeaderSlot.Add(mobileTopBar.Root);
            legacyHeader.RemoveFromHierarchy();
            mobilePageShell.ContentSlot.Add(shell);
            legacyRoot.Clear();
            legacyRoot.RemoveFromClassList("content-management");
            legacyRoot.RemoveFromClassList("safe-area-root");
            legacyRoot.AddToClassList("content-management-host");
            legacyRoot.name = "content-view-host";
            legacyRoot.Add(mobilePageShell.Root);
            pageRoot = mobilePageShell.Root;
            title = mobileTopBar.Title;
            subtitle = mobileTopBar.Subtitle;
            title.name = "content-title";
            subtitle.name = "content-subtitle";
            errorPresenter = new PlayerUiErrorPresenter(
                errorPanel, errorTitle, errorBody, errorRetryRoot, home: errorHomeRoot);
            selectFilteredAction = new MobileActionControl(selectFilteredRoot, () =>
            {
                UIFeedbackService.Play(FeedbackCue.ButtonClick);
                SelectAllFilteredPackages();
            });
            clearSelectionAction = new MobileActionControl(clearSelectionRoot, () =>
            {
                UIFeedbackService.Play(FeedbackCue.ButtonClick);
                ClearPackageSelection();
            });
            downloadSelectedAction = new MobileActionControl(downloadSelectedRoot, () =>
            {
                RequestSelectedDownloadConfirmation();
            });
            queuePauseAction = new MobileActionControl(queuePauseRoot, () =>
            {
                if (PauseInstallQueue()) UIFeedbackService.Play(FeedbackCue.ButtonClick);
            });
            queueResumeAction = new MobileActionControl(queueResumeRoot, () =>
            {
                if (ResumeInstallQueue()) UIFeedbackService.Play(FeedbackCue.ButtonClick);
            });
            queueRetryAction = new MobileActionControl(queueRetryRoot, () =>
            {
                if (RetryFailedQueueItems()) UIFeedbackService.Play(FeedbackCue.ButtonClick);
            });
            queueCancelAction = new MobileActionControl(queueCancelRoot, () =>
            {
                if (CancelInstallQueue()) UIFeedbackService.Play(FeedbackCue.Back);
            });
            primaryNavigation = new MobilePrimaryNavigation(
                MobileDestination.Content,
                NavigatePrimary);
            mobilePageShell.BottomNavigationSlot.Add(primaryNavigation.BottomNavigation.Root);
            mobileTopBar.AddAction(refreshAction);
            mobileTopBar.AddAction(backAction);
            confirmationPresenter = new MobileConfirmationPresenter();
            mobilePageShell.ModalLayer.Add(confirmationPresenter.Root);
            packageList.virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight;
            packageList.selectionType = SelectionType.None;
            packageList.makeItem = MakePackageRow;
            packageList.bindItem = BindPackageRow;
            packageList.unbindItem = UnbindPackageRow;
            searchFilter.RegisterValueChangedCallback(_ => ApplyLibraryQuery());
            languageFilter.RegisterValueChangedCallback(_ => ApplyLibraryQuery());
            generationFilter.RegisterValueChangedCallback(_ => ApplyLibraryQuery());
            installFilter.RegisterValueChangedCallback(_ => ApplyLibraryQuery());
            wifiOnlyToggle.RegisterValueChangedCallback(OnWifiOnlyChanged);
            ConfigureFilterChoices();
            ApplyLocalizedChrome();
        }

        private void Update()
        {
            if (destroyed || downloadPolicy == null || Time.unscaledTime < nextNetworkPollTime)
                return;
            nextNetworkPollTime = Time.unscaledTime + 1f;
            RefreshDownloadNetworkState();
        }

        private async void OnApplicationPause(bool paused)
        {
            if (!paused || destroyed)
                return;
            try
            {
                await SuspendAllOperationsAsync();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Content queue could not be suspended while the app paused: " + exception.Message);
            }
        }

        private async Task ReloadCatalogAsync()
        {
            int generation = ++loadGeneration;
            loadCancellation?.Cancel();
            loadCancellation?.Dispose();
            loadCancellation = new CancellationTokenSource();
            CancellationToken token = loadCancellation.Token;
            IsReady = false;
            catalogQueryAllowed = false;
            InitializationError = null;
            errorPresenter?.Hide();
            SetCatalogStatus(L("content.catalog.loading"), false);
            if (refreshAction != null)
                refreshAction.Allowed = false;

            if (catalogProvider == null || operationFactory == null)
            {
                ApplyCatalogFailure(
                    generation,
                    L("content.catalog.not_configured"),
                    PlayerUiErrorMapper.Create(PlayerUiErrorCode.ServiceUnavailable));
                return;
            }

            try
            {
                ContentPackageCatalogLoadResult result = await catalogProvider.LoadAsync(token);
                dispatcher.Post(() =>
                {
                    if (destroyed || generation != loadGeneration)
                        return;
                    if (result == null || !result.Succeeded)
                        ApplyCatalogFailure(generation, result?.ErrorMessage ?? "No catalog result was returned.");
                    else
                        ApplyCatalog(generation, result);
                });
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // A newer refresh or page destruction owns the UI now.
            }
            catch (Exception exception)
            {
                dispatcher.Post(() =>
                {
                    if (!destroyed && generation == loadGeneration)
                        ApplyCatalogFailure(generation, exception.Message);
                });
            }
        }

        private void ApplyCatalog(int generation, ContentPackageCatalogLoadResult result)
        {
            if (destroyed || generation != loadGeneration)
                return;
            LastAppliedThreadId = Environment.CurrentManagedThreadId;
            errorPresenter?.Hide();
            catalog = result.Catalog;
            catalogQueryAllowed = true;
            catalogUsedCache = result.UsedCachedCatalog;
            catalogHasCacheWarning = !string.IsNullOrWhiteSpace(result.WarningMessage);
            if (catalogHasCacheWarning)
                Debug.LogWarning("Content package catalog warning: " + result.WarningMessage);
            if (installQueue != null)
            {
                installQueue.Changed -= OnQueueChanged;
                _ = installQueue.SuspendAsync();
            }
            installQueue = new ContentPackageInstallQueue(
                catalog,
                operationFactory,
                ContentPackageInstallQueue.DefaultMaximumConcurrentDownloads,
                queueStateStore,
                EnsureOperation);
            installQueue.Changed += OnQueueChanged;
            queueSnapshot = installQueue.Current;
            queueCompletionNotified = false;
            cellularConfirmationArmed = false;
            mobileAuthorizedForBatch = false;
            mobileAuthorizedPackages.Clear();
            pendingCellularPackageId = null;
            ClearOperations();
            rows.Clear();
            displayedItems.Clear();
            selectedPackageIds.RemoveWhere(id => catalog.Find(id) == null);
            if (installQueue.RestoredFromState)
            {
                foreach (string packageId in installQueue.SelectedPackageIds)
                    selectedPackageIds.Add(packageId);
            }
            notifiedFailures.Clear();
            ConfigureFilterChoices();
            ApplyRecommendedLaunch(ContentLaunchRequest.ConsumeRecommendation());
            ApplyLibraryQuery();
        }

        private void ApplyRecommendedLaunch(string packageId)
        {
            launchedRecommendationId = null;
            if (string.IsNullOrWhiteSpace(packageId))
            {
                RefreshLaunchBanner();
                return;
            }
            ContentPackageCatalogEntry entry = catalog.Find(packageId);
            if (entry == null || ContentPackageLibrary.IsCurrent(LookupInstalled(packageId), entry.Package))
            {
                RefreshLaunchBanner();
                return;
            }

            launchedRecommendationId = entry.Package.PackageId;
            selectedPackageIds.Add(launchedRecommendationId);
            SetRecommendedFilters(entry.Metadata);
            RefreshLaunchBanner();
        }

        private void SetRecommendedFilters(ContentPackageMetadata metadata)
        {
            int languageIndex = 0;
            if (string.Equals(metadata.ContentLanguageId, "en", StringComparison.OrdinalIgnoreCase))
                languageIndex = 1;
            else if (string.Equals(metadata.ContentLanguageId, "ja", StringComparison.OrdinalIgnoreCase))
                languageIndex = 2;
            else if (string.Equals(metadata.ContentLanguageId, "zh-cn", StringComparison.OrdinalIgnoreCase))
                languageIndex = 3;
            SetDropdownIndex(languageFilter, languageIndex);

            int generationIndex = 0;
            if (metadata.GenerationOrder.HasValue)
            {
                generationIndex = generationFilter.choices.FindIndex(value =>
                    string.Equals(
                        value,
                        metadata.GenerationOrder.Value.ToString(),
                        StringComparison.Ordinal));
            }
            SetDropdownIndex(generationFilter, Math.Max(0, generationIndex));
            SetDropdownIndex(installFilter, 0);
        }

        private void ApplyLibraryQuery(bool rebuild = true)
        {
            if (destroyed || !catalogQueryAllowed || catalog == null || packageList == null)
                return;
            try
            {
                library = ContentPackageLibrary.Project(
                    catalog,
                    LookupInstalled,
                    new ContentPackageLibraryQuery(
                        CurrentUiLanguageId(),
                        searchFilter?.value,
                        SelectedLanguage(),
                        SelectedGeneration(),
                        installFilter: SelectedInstallFilter()));
                displayedItems.Clear();
                displayedItems.AddRange(library.Items);
                rows.Clear();
                packageList.itemsSource = displayedItems;
                if (rebuild)
                    packageList.Rebuild();
                else
                    packageList.RefreshItems();
                ScrollToLaunchedRecommendation();
                bool empty = displayedItems.Count == 0;
                emptyState.text = catalog.Packages.Count == 0
                    ? L("content.catalog.empty")
                    : L("content.filter.empty");
                emptyState.style.display = empty ? DisplayStyle.Flex : DisplayStyle.None;
                packageList.style.display = empty ? DisplayStyle.None : DisplayStyle.Flex;
                IsReady = true;
                InitializationError = null;
                ApplyReadyCatalogStatus();
                RefreshSelectionUi();
                refreshAction.Allowed = true;
            }
            catch (Exception exception)
            {
                ApplyCatalogFailure(loadGeneration, exception.Message);
            }
        }

        private VisualElement MakePackageRow()
        {
            var row = new PackageRow(
                PrimaryClicked,
                PauseClicked,
                CancelClicked,
                RemoveClicked,
                SelectionChanged);
            row.Root.userData = row;
            return row.Root;
        }

        private void BindPackageRow(VisualElement element, int index)
        {
            if (destroyed || index < 0 || index >= displayedItems.Count)
                return;
            var row = (PackageRow)element.userData;
            RemoveVisibleBinding(row);
            ContentPackageLibraryItem item = displayedItems[index];
            row.Bind(
                item.Entry,
                item.DisplayName,
                selectedPackageIds.Contains(item.Package.PackageId));
            string packageId = item.Package.PackageId;
            rows[packageId] = row;
            ContentPackageInstallCoordinator operation = EnsureOperation(packageId);
            RefreshInstalledState(row);
            ApplyOperation(packageId, operation.Current);
        }

        private void UnbindPackageRow(VisualElement element, int index)
        {
            if (!(element.userData is PackageRow row))
                return;
            RemoveVisibleBinding(row);
            row.Unbind();
        }

        private void RemoveVisibleBinding(PackageRow row)
        {
            string packageId = row?.Entry?.Package.PackageId;
            if (packageId == null)
                return;
            if (rows.TryGetValue(packageId, out PackageRow current) && ReferenceEquals(current, row))
                rows.Remove(packageId);
            ReleaseIdleOperation(packageId);
        }

        private ContentPackageInstallCoordinator EnsureOperation(string packageId)
        {
            if (operations.TryGetValue(packageId, out ContentPackageInstallCoordinator existing))
                return existing;
            if (catalog?.Find(packageId) == null)
                throw new KeyNotFoundException("Content package is not in the active catalog: " + packageId);
            ContentPackageInstallCoordinator operation = operationFactory.Create(catalog, packageId);
            var bridge = new ContentPackageOperationUiBridge(operation, dispatcher);
            bridge.Changed += snapshot => ApplyOperation(packageId, snapshot);
            bridge.FailureReported += ReportFailure;
            operations.Add(packageId, operation);
            bridges.Add(packageId, bridge);
            return operation;
        }

        private void ReleaseIdleOperation(string packageId)
        {
            if (rows.ContainsKey(packageId) ||
                !operations.TryGetValue(packageId, out ContentPackageInstallCoordinator operation))
                return;
            ContentPackageOperationState state = operation.Current.State;
            if (state == ContentPackageOperationState.Planning ||
                state == ContentPackageOperationState.Downloading ||
                state == ContentPackageOperationState.Installing ||
                state == ContentPackageOperationState.Paused ||
                state == ContentPackageOperationState.Failed)
                return;
            if (bridges.TryGetValue(packageId, out ContentPackageOperationUiBridge bridge))
                bridge.Dispose();
            bridges.Remove(packageId);
            operations.Remove(packageId);
        }

        private void ConfigureFilterChoices()
        {
            if (languageFilter == null || generationFilter == null || installFilter == null)
                return;
            int languageIndex = Math.Max(0, languageFilter.index);
            int generationIndex = Math.Max(0, generationFilter.index);
            int installIndex = Math.Max(0, installFilter.index);
            languageFilter.choices = new List<string>
            {
                L("content.filter.all"),
                L("content.filter.language.en"),
                L("content.filter.language.ja"),
                L("content.filter.language.zh-cn")
            };
            var generations = new List<string> { L("content.filter.all") };
            if (catalog != null)
                generations.AddRange(catalog.Packages
                    .Where(value => value.Metadata.GenerationOrder.HasValue)
                    .Select(value => value.Metadata.GenerationOrder.Value)
                    .Distinct()
                    .OrderBy(value => value)
                    .Select(value => value.ToString()));
            generationFilter.choices = generations;
            installFilter.choices = new List<string>
            {
                L("content.filter.all"),
                L("content.filter.installed"),
                L("content.filter.not_installed"),
                L("content.filter.update")
            };
            SetDropdownIndex(languageFilter, languageIndex);
            SetDropdownIndex(generationFilter, generationIndex);
            SetDropdownIndex(installFilter, installIndex);
        }

        private static void SetDropdownIndex(DropdownField field, int index)
        {
            int safeIndex = Mathf.Clamp(index, 0, Math.Max(0, field.choices.Count - 1));
            field.index = safeIndex;
            field.SetValueWithoutNotify(field.choices.Count == 0 ? string.Empty : field.choices[safeIndex]);
        }

        private string SelectedLanguage()
        {
            switch (languageFilter?.index ?? 0)
            {
                case 1: return "en";
                case 2: return "ja";
                case 3: return "zh-cn";
                default: return null;
            }
        }

        private int? SelectedGeneration()
        {
            if (generationFilter == null || generationFilter.index <= 0)
                return null;
            return int.TryParse(generationFilter.value, out int generation) ? generation : (int?)null;
        }

        private ContentPackageInstallFilter SelectedInstallFilter()
        {
            switch (installFilter?.index ?? 0)
            {
                case 1: return ContentPackageInstallFilter.Installed;
                case 2: return ContentPackageInstallFilter.NotInstalled;
                case 3: return ContentPackageInstallFilter.UpdateAvailable;
                default: return ContentPackageInstallFilter.All;
            }
        }

        private static string CurrentUiLanguageId()
        {
            string code = LocalizationSettings.SelectedLocale?.Identifier.Code?.ToLowerInvariant();
            if (code != null && code.StartsWith("zh", StringComparison.Ordinal))
                return "zh-cn";
            if (code != null && code.StartsWith("ja", StringComparison.Ordinal))
                return "ja";
            return "en";
        }

        private void SelectionChanged(string packageId, bool selected)
        {
            if (string.IsNullOrWhiteSpace(packageId))
                return;
            if (selected)
                selectedPackageIds.Add(packageId);
            else
                selectedPackageIds.Remove(packageId);
            cellularConfirmationArmed = false;
            UIFeedbackService.Play(FeedbackCue.ButtonClick);
            RefreshSelectionUi();
        }

        private void RefreshSelectionUi()
        {
            if (catalog == null || selectionSummaryLabel == null)
                return;
            selectedPackageIds.RemoveWhere(id => catalog.Find(id) == null);
            selectionSummary = selectedPackageIds.Count == 0
                ? null
                : ContentPackageLibrary.SummarizeSelection(
                    catalog, selectedPackageIds, LookupInstalled);
            string selectionText = selectionSummary == null
                ? L("content.selection.none")
                : string.Format(
                    L("content.selection.summary"),
                    selectionSummary.SelectedCount,
                    selectionSummary.DependencyCount,
                    FormatBytes(selectionSummary.DownloadBytes),
                    FormatBytes(selectionSummary.InstalledBytes));
            downloadPreflight = downloadPolicy?.Evaluate(selectionSummary);
            if (downloadPreflight != null && selectionSummary != null)
                selectionText += "\n" + DescribePreflight(downloadPreflight);
            if (queueSnapshot != null && queueSnapshot.Items.Count > 0)
            {
                selectionText += "\n" + string.Format(
                    L("content.queue.summary"),
                    queueSnapshot.QueuedCount,
                    queueSnapshot.RunningCount,
                    queueSnapshot.SucceededCount,
                    queueSnapshot.FailedCount);
            }
            if (!string.IsNullOrWhiteSpace(installQueue?.PersistenceWarning))
            {
                selectionText += "\n" + string.Format(
                    L("content.queue.restore_warning"),
                    installQueue.PersistenceWarning);
            }
            selectionSummaryLabel.text = selectionText;

            ConfigureGlobalAction(selectFilteredAction, displayedItems.Count > 0);
            ConfigureGlobalAction(clearSelectionAction, selectedPackageIds.Count > 0);
            bool canAttemptDownload = downloadPreflight?.CanStart == true ||
                                      downloadPreflight?.Status ==
                                      ContentDownloadPreflightStatus.CellularConfirmationRequired;
            downloadSelectedAction.SetLabel(L(cellularConfirmationArmed
                ? "content.action.confirm_cellular"
                : "content.action.download_selected"));
            ConfigureGlobalAction(downloadSelectedAction, canAttemptDownload);
            ConfigureGlobalAction(queuePauseAction, queueSnapshot?.RunningCount > 0);
            ConfigureGlobalAction(queueResumeAction,
                queueSnapshot?.Paused == true && canAttemptDownload);
            ConfigureGlobalAction(queueRetryAction,
                queueSnapshot?.FailedCount > 0 && canAttemptDownload);
            ConfigureGlobalAction(queueCancelAction,
                queueSnapshot != null && queueSnapshot.Items.Count > 0 && !queueSnapshot.IsComplete);

            foreach (VisualElement element in packageList
                         .Query<VisualElement>(className: "content-package-row").ToList())
            {
                if (!(element.userData is PackageRow row) || row.Entry == null)
                    continue;
                bool selected = selectedPackageIds.Contains(row.Entry.Package.PackageId);
                row.Selection.SetValueWithoutNotify(selected);
                row.Root.EnableInClassList("is-selected", selected);
            }
        }

        private bool PrepareBatchDownload()
        {
            if (downloadPolicy == null || selectionSummary == null)
                return false;
            ContentDownloadPreflightResult result = downloadPolicy.Evaluate(
                selectionSummary,
                cellularConfirmationArmed);
            downloadPreflight = result;
            if (result.Status == ContentDownloadPreflightStatus.CellularConfirmationRequired &&
                !cellularConfirmationArmed)
            {
                cellularConfirmationArmed = true;
                UIFeedbackService.Play(FeedbackCue.Confirm);
                RefreshSelectionUi();
                return false;
            }
            if (!result.CanStart)
            {
                UIFeedbackService.Play(result.Status == ContentDownloadPreflightStatus.WaitingForWifi
                    ? FeedbackCue.Confirm
                    : FeedbackCue.Error);
                RefreshSelectionUi();
                return false;
            }
            return true;
        }

        private void RequestSelectedDownloadConfirmation()
        {
            if (confirmationPresenter == null || confirmationPresenter.IsVisible ||
                downloadPolicy == null || selectionSummary == null)
                return;
            ContentDownloadPreflightResult result = downloadPolicy.Evaluate(selectionSummary, true);
            downloadPreflight = result;
            if (!result.CanStart)
            {
                UIFeedbackService.Play(result.Status == ContentDownloadPreflightStatus.WaitingForWifi
                    ? FeedbackCue.Confirm
                    : FeedbackCue.Error);
                RefreshSelectionUi();
                return;
            }
            string body = string.Format(
                L("content.confirm.download.body"),
                selectionSummary.SelectedCount,
                selectionSummary.DependencyCount,
                FormatBytes(selectionSummary.DownloadBytes),
                FormatBytes(selectionSummary.InstalledBytes),
                DescribePreflight(result));
            confirmationPresenter.Show(
                L("content.confirm.download.title"),
                body,
                L("content.action.confirm_download"),
                L("content.action.cancel_confirmation"),
                () =>
                {
                    cellularConfirmationArmed = result.NetworkType == ContentNetworkType.MobileData;
                    if (StartSelectedPackages())
                        RefreshSelectionUi();
                },
                destructive: false);
            UIFeedbackService.Play(FeedbackCue.Confirm);
        }

        private void RequestPackageDownloadConfirmation(string packageId)
        {
            ContentPackageCatalogEntry entry = catalog?.Find(packageId);
            if (confirmationPresenter == null || confirmationPresenter.IsVisible ||
                downloadPolicy == null || entry == null)
                return;
            ContentPackageSelectionSummary summary = ContentPackageLibrary.SummarizeSelection(
                catalog,
                new[] { packageId },
                LookupInstalled);
            ContentDownloadPreflightResult result = downloadPolicy.Evaluate(summary, true);
            if (!result.CanStart)
            {
                ShowPackagePreflight(packageId, result);
                UIFeedbackService.Play(result.Status == ContentDownloadPreflightStatus.WaitingForWifi
                    ? FeedbackCue.Confirm
                    : FeedbackCue.Error);
                return;
            }
            string body = string.Format(
                L("content.confirm.download.package_body"),
                entry.Metadata.GetDisplayName(CurrentUiLanguageId(), packageId),
                summary.DependencyCount,
                FormatBytes(summary.DownloadBytes),
                FormatBytes(summary.InstalledBytes),
                DescribePreflight(result));
            confirmationPresenter.Show(
                L("content.confirm.download.title"),
                body,
                L("content.action.confirm_download"),
                L("content.action.cancel_confirmation"),
                () =>
                {
                    StartConfirmedPackage(
                        packageId,
                        result.NetworkType == ContentNetworkType.MobileData);
                },
                destructive: false);
            UIFeedbackService.Play(FeedbackCue.Confirm);
        }

        private bool PreparePackageDownload(string packageId)
        {
            if (downloadPolicy == null || catalog?.Find(packageId) == null)
                return false;
            ContentPackageSelectionSummary summary = ContentPackageLibrary.SummarizeSelection(
                catalog,
                new[] { packageId },
                LookupInstalled);
            bool confirmed = mobileAuthorizedPackages.Contains(packageId) ||
                             string.Equals(
                                 pendingCellularPackageId,
                                 packageId,
                                 StringComparison.Ordinal);
            ContentDownloadPreflightResult result = downloadPolicy.Evaluate(summary, confirmed);
            if (result.Status == ContentDownloadPreflightStatus.CellularConfirmationRequired &&
                !confirmed)
            {
                pendingCellularPackageId = packageId;
                ShowPackagePreflight(packageId, result);
                UIFeedbackService.Play(FeedbackCue.Confirm);
                return false;
            }
            if (!result.CanStart)
            {
                ShowPackagePreflight(packageId, result);
                UIFeedbackService.Play(result.Status == ContentDownloadPreflightStatus.WaitingForWifi
                    ? FeedbackCue.Confirm
                    : FeedbackCue.Error);
                return false;
            }
            pendingCellularPackageId = null;
            if (result.NetworkType == ContentNetworkType.MobileData)
                mobileAuthorizedPackages.Add(packageId);
            return true;
        }

        private void ShowPackagePreflight(
            string packageId,
            ContentDownloadPreflightResult result)
        {
            if (!rows.TryGetValue(packageId, out PackageRow row))
                return;
            row.Status.text = DescribePreflight(result);
            bool error = result.Status == ContentDownloadPreflightStatus.InsufficientSpace ||
                         result.Status == ContentDownloadPreflightStatus.StorageUnavailable ||
                         result.Status == ContentDownloadPreflightStatus.NetworkUnavailable ||
                         result.Status == ContentDownloadPreflightStatus.Offline;
            row.Status.EnableInClassList("is-error", error);
            AnimateRow(row, error);
        }

        private bool ShouldPauseBatchForNetwork(ContentNetworkType networkType)
        {
            if (networkType == ContentNetworkType.Offline ||
                networkType == ContentNetworkType.Unknown)
                return true;
            if (networkType != ContentNetworkType.MobileData)
                return false;
            if (!mobileAuthorizedForBatch)
                return true;
            return downloadPolicy.Current.WifiOnlyForLargeDownloads &&
                   (selectionSummary?.DownloadBytes ?? 0) >=
                   downloadPolicy.LargeDownloadThresholdBytes;
        }

        private bool ShouldPausePackageForNetwork(
            string packageId,
            ContentNetworkType networkType)
        {
            if (networkType == ContentNetworkType.Offline ||
                networkType == ContentNetworkType.Unknown)
                return true;
            if (networkType != ContentNetworkType.MobileData)
                return false;
            if (!mobileAuthorizedPackages.Contains(packageId))
                return true;
            ContentPackageSelectionSummary summary = ContentPackageLibrary.SummarizeSelection(
                catalog,
                new[] { packageId },
                LookupInstalled);
            return downloadPolicy.Current.WifiOnlyForLargeDownloads &&
                   summary.DownloadBytes >= downloadPolicy.LargeDownloadThresholdBytes;
        }

        private string DescribePreflight(ContentDownloadPreflightResult result)
        {
            switch (result.Status)
            {
                case ContentDownloadPreflightStatus.Ready:
                    return string.Format(
                        L("content.preflight.ready"),
                        FormatBytes(result.AvailableBytes),
                        FormatBytes(result.RequiredBytes),
                        NetworkName(result.NetworkType));
                case ContentDownloadPreflightStatus.AlreadyCurrent:
                    return L("content.preflight.current");
                case ContentDownloadPreflightStatus.Offline:
                    return L("content.preflight.offline");
                case ContentDownloadPreflightStatus.WaitingForWifi:
                    return string.Format(
                        L("content.preflight.waiting_wifi"),
                        FormatBytes(result.DownloadBytes));
                case ContentDownloadPreflightStatus.CellularConfirmationRequired:
                    return string.Format(
                        L("content.preflight.cellular_confirmation"),
                        FormatBytes(result.DownloadBytes));
                case ContentDownloadPreflightStatus.InsufficientSpace:
                    return string.Format(
                        L("content.preflight.insufficient_space"),
                        FormatBytes(result.AvailableBytes),
                        FormatBytes(result.RequiredBytes));
                case ContentDownloadPreflightStatus.StorageUnavailable:
                    return L("content.preflight.storage_unavailable");
                case ContentDownloadPreflightStatus.NetworkUnavailable:
                    return L("content.preflight.network_unavailable");
                default:
                    return L("content.selection.none");
            }
        }

        private string NetworkName(ContentNetworkType networkType)
        {
            switch (networkType)
            {
                case ContentNetworkType.WifiOrEthernet: return L("content.network.wifi");
                case ContentNetworkType.MobileData: return L("content.network.mobile");
                case ContentNetworkType.Offline: return L("content.network.offline");
                default: return L("content.network.unknown");
            }
        }

        private void OnWifiOnlyChanged(ChangeEvent<bool> evt)
        {
            if (bindingWifiOnly || downloadPolicy == null)
                return;
            try
            {
                downloadPolicy.SetWifiOnlyForLargeDownloads(evt.newValue);
                cellularConfirmationArmed = false;
                mobileAuthorizedForBatch = false;
                mobileAuthorizedPackages.Clear();
                UIFeedbackService.Play(FeedbackCue.ButtonClick);
                RefreshSelectionUi();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Content download preference could not be saved: " + exception.Message);
                ApplyDownloadPreferences(downloadPolicy.Current);
                UIFeedbackService.Play(FeedbackCue.Error);
            }
        }

        private void OnDownloadPreferencesChanged(ContentDownloadPreferences preferences)
        {
            ApplyDownloadPreferences(preferences);
            cellularConfirmationArmed = false;
            mobileAuthorizedForBatch = false;
            mobileAuthorizedPackages.Clear();
            RefreshSelectionUi();
        }

        private void ApplyDownloadPreferences(ContentDownloadPreferences preferences)
        {
            if (wifiOnlyToggle == null || preferences == null)
                return;
            bindingWifiOnly = true;
            wifiOnlyToggle.SetValueWithoutNotify(preferences.WifiOnlyForLargeDownloads);
            bindingWifiOnly = false;
        }

        private static void ConfigureGlobalAction(MobileActionControl action, bool allowed)
        {
            if (action == null)
                return;
            action.Allowed = allowed;
            KeepRenderNodeStable(action);
        }

        private void OnQueueChanged(ContentPackageQueueSnapshot snapshot)
        {
            dispatcher.Post(() =>
            {
                if (!destroyed)
                    ApplyQueueSnapshot(snapshot);
            });
        }

        private void ApplyQueueSnapshot(ContentPackageQueueSnapshot snapshot)
        {
            queueSnapshot = snapshot;
            RefreshSelectionUi();
            foreach (KeyValuePair<string, ContentPackageInstallCoordinator> pair in operations)
            {
                if (rows.ContainsKey(pair.Key))
                    ApplyOperation(pair.Key, pair.Value.Current);
            }
            if (snapshot == null || !snapshot.IsComplete || queueCompletionNotified)
                return;
            queueCompletionNotified = true;
            cellularConfirmationArmed = false;
            mobileAuthorizedForBatch = false;
            if (snapshot.FailedCount == 0)
            {
                UIFeedbackService.Play(FeedbackCue.DownloadComplete, true);
                ReloadLocalCatalog();
                ApplyLibraryQuery();
            }
            else
            {
                UIFeedbackService.Play(FeedbackCue.Error);
            }
        }

        private void ApplyCatalogFailure(int generation, string message, PlayerUiError playerError = null)
        {
            if (destroyed || generation != loadGeneration)
                return;
            LastAppliedThreadId = Environment.CurrentManagedThreadId;
            catalogUsedCache = false;
            catalogHasCacheWarning = false;
            InitializationError = string.IsNullOrWhiteSpace(message)
                ? "Content catalog is unavailable."
                : message.Trim();
            IsReady = false;
            catalogQueryAllowed = false;
            if (installQueue != null)
            {
                installQueue.Changed -= OnQueueChanged;
                _ = installQueue.SuspendAsync();
            }
            installQueue = null;
            queueSnapshot = null;
            queueCompletionNotified = false;
            cellularConfirmationArmed = false;
            mobileAuthorizedForBatch = false;
            mobileAuthorizedPackages.Clear();
            pendingCellularPackageId = null;
            selectedPackageIds.Clear();
            selectionSummary = null;
            if (selectionSummaryLabel != null)
                selectionSummaryLabel.text = L("content.selection.none");
            ConfigureGlobalAction(selectFilteredAction, false);
            ConfigureGlobalAction(clearSelectionAction, false);
            ConfigureGlobalAction(downloadSelectedAction, false);
            ConfigureGlobalAction(queuePauseAction, false);
            ConfigureGlobalAction(queueResumeAction, false);
            ConfigureGlobalAction(queueRetryAction, false);
            ConfigureGlobalAction(queueCancelAction, false);
            emptyState.style.display = DisplayStyle.Flex;
            PlayerUiError safeError = playerError ?? PlayerUiErrorMapper.FromDetail(
                InitializationError,
                downloadPolicy?.GetNetworkType() == ContentNetworkType.Offline);
            emptyState.text = PlayerUiErrorText.Body(safeError);
            if (packageList != null)
            {
                displayedItems.Clear();
                rows.Clear();
                packageList.itemsSource = displayedItems;
                packageList.Rebuild();
                packageList.style.display = DisplayStyle.None;
            }
            errorPresenter?.Show(safeError);
            SetCatalogStatus(PlayerUiErrorText.Title(safeError), true);
            if (refreshAction != null)
                refreshAction.Allowed = true;
        }

        private void ApplyOperation(string packageId, ContentPackageOperationSnapshot snapshot)
        {
            if (destroyed)
                return;
            if (!rows.TryGetValue(packageId, out PackageRow row))
            {
                if (snapshot.State == ContentPackageOperationState.Succeeded)
                {
                    UIFeedbackService.Play(FeedbackCue.DownloadComplete, true);
                    ReloadLocalCatalog();
                }
                ReleaseIdleOperation(packageId);
                return;
            }
            LastAppliedThreadId = Environment.CurrentManagedThreadId;
            ContentPackageItemPresentation item = ContentPackageItemPresentation.Create(row.Entry, snapshot);
            ContentPackageOperationState? previous = row.LastState;
            row.LastState = snapshot.State;
            if (snapshot.State == ContentPackageOperationState.Succeeded ||
                snapshot.State == ContentPackageOperationState.AlreadyCurrent ||
                snapshot.State == ContentPackageOperationState.Cancelled ||
                snapshot.State == ContentPackageOperationState.Failed ||
                snapshot.State == ContentPackageOperationState.Blocked)
            {
                mobileAuthorizedPackages.Remove(packageId);
                if (string.Equals(pendingCellularPackageId, packageId, StringComparison.Ordinal))
                    pendingCellularPackageId = null;
            }

            string packageIdentity = string.IsNullOrWhiteSpace(row.Entry.Metadata.SetCode)
                ? row.Entry.Package.PackageId
                : row.Entry.Metadata.SetCode;
            string generation = row.Entry.Metadata.GenerationOrder?.ToString() ?? "—";
            string releaseDate = row.Entry.Metadata.ReleaseDate?.ToString("yyyy-MM-dd") ?? "—";
            row.Metadata.text = packageIdentity + " · G" + generation + " · " + releaseDate + " · " + string.Format(
                L("content.package.metadata"),
                item.Version,
                FormatBytes(item.DownloadBytes));
            if (snapshot.State == ContentPackageOperationState.Succeeded ||
                snapshot.State == ContentPackageOperationState.AlreadyCurrent)
                RefreshInstalledState(row);

            bool installedCurrent = Matches(row.Installed, row.Entry.Package);
            string status = L(item.UiState.StatusKey);
            ContentPackageQueueItemSnapshot queueItem = queueSnapshot?.Items.FirstOrDefault(value =>
                string.Equals(value.PackageId, packageId, StringComparison.Ordinal));
            bool queueOwnsOperation = queueItem != null &&
                (queueItem.State == ContentPackageQueueItemState.Queued ||
                 queueItem.State == ContentPackageQueueItemState.Running ||
                 queueItem.State == ContentPackageQueueItemState.Paused);
            if (row.Removing)
                status = L("content.status.removing");
            else if (queueItem?.State == ContentPackageQueueItemState.Queued)
                status = L("content.status.queued");
            else if (!string.IsNullOrWhiteSpace(row.LifecycleError))
                status = PlayerUiErrorText.Body(PlayerUiErrorMapper.Create(PlayerUiErrorCode.Unexpected));
            else if (snapshot.State == ContentPackageOperationState.Idle && row.Installed != null)
                status = L(installedCurrent ? "content.status.current" : "content.status.update_available");
            if (!string.IsNullOrWhiteSpace(item.ErrorMessage))
                status = PlayerUiErrorText.Body(ErrorFor(snapshot));
            else if (!string.IsNullOrWhiteSpace(item.WarningMessage))
                status = L("content.status.installed");
            row.Status.text = status;
            row.Status.EnableInClassList("is-error", item.UiState.IsError || !string.IsNullOrWhiteSpace(row.LifecycleError));
            row.Status.EnableInClassList("is-success",
                snapshot.State == ContentPackageOperationState.Succeeded ||
                snapshot.State == ContentPackageOperationState.AlreadyCurrent ||
                snapshot.State == ContentPackageOperationState.Idle && installedCurrent);

            row.Progress.value = item.Progress01 * 100f;
            long downloaded = snapshot.Download?.DownloadedBytes ?? 0;
            row.Progress.title = string.Format(
                L("content.progress"),
                Mathf.RoundToInt(item.Progress01 * 100f),
                FormatBytes(downloaded),
                FormatBytes(item.DownloadBytes));
            row.Progress.style.display = item.UiState.ShowProgress ? DisplayStyle.Flex : DisplayStyle.None;

            bool hasPrimary = item.UiState.PrimaryAction != ContentPackagePrimaryAction.None;
            if (snapshot.State == ContentPackageOperationState.Idle && installedCurrent)
                hasPrimary = false;
            if (queueOwnsOperation)
                hasPrimary = false;
            bool canCancel = CanShowCancel(snapshot.State);
            bool canRemove = row.Installed != null && !item.UiState.IsBusy && !canCancel && !queueOwnsOperation;
            if (row.Removing)
            {
                hasPrimary = false;
                canCancel = false;
                canRemove = false;
            }

            ConfigurePersistentActions(
                row,
                hasPrimary && !item.UiState.IsBusy && !row.Removing,
                item.CanPause && !row.Removing,
                canRemove && !row.Removing,
                canCancel && !row.Removing);

            if (previous.HasValue && previous.Value != snapshot.State)
                AnimateRow(row, snapshot.State == ContentPackageOperationState.Failed);
            if (previous.HasValue &&
                previous.Value != ContentPackageOperationState.Succeeded &&
                snapshot.State == ContentPackageOperationState.Succeeded)
                UIFeedbackService.Play(FeedbackCue.DownloadComplete, true);
            if (previous.HasValue &&
                previous.Value != ContentPackageOperationState.Succeeded &&
                snapshot.State == ContentPackageOperationState.Succeeded)
                ReloadLocalCatalog();
        }

        private void PrimaryClicked(string packageId)
        {
            RequestPackageDownloadConfirmation(packageId);
        }

        private void StartConfirmedPackage(string packageId, bool mobileConsent)
        {
            if (destroyed || downloadPolicy == null || catalog?.Find(packageId) == null)
                return;
            ContentPackageSelectionSummary summary = ContentPackageLibrary.SummarizeSelection(
                catalog,
                new[] { packageId },
                LookupInstalled);
            ContentDownloadPreflightResult result = downloadPolicy.Evaluate(summary, mobileConsent);
            if (!result.CanStart)
            {
                ShowPackagePreflight(packageId, result);
                UIFeedbackService.Play(result.Status == ContentDownloadPreflightStatus.WaitingForWifi ||
                                       result.Status == ContentDownloadPreflightStatus.CellularConfirmationRequired
                    ? FeedbackCue.Confirm
                    : FeedbackCue.Error);
                return;
            }
            if (result.NetworkType == ContentNetworkType.MobileData)
                mobileAuthorizedPackages.Add(packageId);
            StartPreparedPackage(packageId);
        }

        private async void StartPreparedPackage(string packageId)
        {
            if (!operations.TryGetValue(packageId, out ContentPackageInstallCoordinator operation))
                return;
            UIFeedbackService.Play(FeedbackCue.DownloadStart);
            try
            {
                ContentPackageOperationSnapshot current = operation.Current;
                if (current.CanRetry)
                    await operation.RetryAsync();
                else
                    await operation.StartAsync();
            }
            catch (Exception exception)
            {
                ShowUnexpectedError(packageId, exception);
            }
        }

        private async void PauseClicked(string packageId)
        {
            if (!operations.TryGetValue(packageId, out ContentPackageInstallCoordinator operation))
                return;
            UIFeedbackService.Play(FeedbackCue.Confirm);
            try
            {
                await operation.PauseAsync();
            }
            catch (Exception exception)
            {
                ShowUnexpectedError(packageId, exception);
            }
        }

        private async void CancelClicked(string packageId)
        {
            if (!operations.TryGetValue(packageId, out ContentPackageInstallCoordinator operation))
                return;
            UIFeedbackService.Play(FeedbackCue.Back);
            try
            {
                await operation.CancelAsync();
            }
            catch (Exception exception)
            {
                ShowUnexpectedError(packageId, exception);
            }
        }

        private void RemoveClicked(string packageId)
        {
            if (lifecycleService == null ||
                !rows.TryGetValue(packageId, out PackageRow row) ||
                row.Installed == null || row.Removing ||
                confirmationPresenter == null || confirmationPresenter.IsVisible ||
                !operations.TryGetValue(packageId, out ContentPackageInstallCoordinator operation))
                return;
            ContentPackageOperationState state = operation.Current.State;
            if (state == ContentPackageOperationState.Planning ||
                state == ContentPackageOperationState.Downloading ||
                state == ContentPackageOperationState.Installing)
                return;
            ContentPackageCatalogEntry entry = catalog?.Find(packageId);
            string name = entry?.Metadata.GetDisplayName(CurrentUiLanguageId(), packageId) ?? packageId;
            confirmationPresenter.Show(
                L("content.confirm.remove.title"),
                string.Format(L("content.confirm.remove.body"), name),
                L("content.action.confirm_remove"),
                L("content.action.cancel_confirmation"),
                () => RemoveConfirmed(packageId),
                destructive: true);
            UIFeedbackService.Play(FeedbackCue.Confirm);
        }

        private async void RemoveConfirmed(string packageId)
        {
            if (destroyed || lifecycleService == null ||
                !rows.TryGetValue(packageId, out PackageRow row) ||
                row.Installed == null || row.Removing ||
                !operations.TryGetValue(packageId, out ContentPackageInstallCoordinator operation))
                return;
            row.Removing = true;
            ApplyOperation(packageId, operation.Current);
            AnimateRow(row, false);
            UIFeedbackService.Play(FeedbackCue.Back);
            try
            {
                ContentPackageRemovalResult result = await lifecycleService.RemoveAsync(packageId);
                if (destroyed)
                    return;
                if (result == null || !result.Succeeded)
                {
                    row.Removing = false;
                    string error = result?.ErrorMessage ?? "No removal result was returned.";
                    Debug.LogWarning($"Content package removal failed for '{packageId}': {error}");
                    ApplyOperation(packageId, operation.Current);
                    PlayerUiError safeError = result == null
                        ? PlayerUiErrorMapper.Create(PlayerUiErrorCode.Unexpected)
                        : PlayerUiErrorMapper.FromRemoval(result.Status) ??
                          PlayerUiErrorMapper.Create(PlayerUiErrorCode.Unexpected);
                    row.Status.text = PlayerUiErrorText.Body(safeError);
                    row.Status.EnableInClassList("is-error", true);
                    UIFeedbackService.Play(FeedbackCue.Error);
                    AnimateRow(row, true);
                    return;
                }

                ContentPackageOperationSnapshot reset = await operation.ResetAfterRemovalAsync();
                if (destroyed)
                    return;
                row.Removing = false;
                RefreshInstalledState(row);
                ApplyOperation(packageId, operation.Current);
                string warning = CombineWarnings(result.WarningMessage, reset.WarningMessage);
                if (!string.IsNullOrWhiteSpace(warning))
                    Debug.LogWarning($"Content package removal cleanup warning for '{packageId}': {warning}");
                row.Status.text = string.IsNullOrWhiteSpace(warning)
                    ? L("content.status.removed")
                    : L("content.status.removed");
                row.Status.EnableInClassList("is-error", false);
                row.Status.EnableInClassList("is-success", true);
                ReloadLocalCatalog();
                UIFeedbackService.Play(FeedbackCue.Confirm, true);
                AnimateRow(row, false);
            }
            catch (Exception exception)
            {
                if (destroyed)
                    return;
                row.Removing = false;
                ApplyOperation(packageId, operation.Current);
                ShowUnexpectedError(packageId, exception);
            }
        }

        private void ReportFailure(ContentPackageOperationFailure failure)
        {
            string key = failure.PackageId + ":" + failure.Attempt;
            if (!notifiedFailures.Add(key))
                return;
            Debug.LogWarning($"Content package operation failed for '{failure.PackageId}' at {failure.Stage}: {failure.ErrorMessage}");
            UIFeedbackService.Play(FeedbackCue.Error);
            if (rows.TryGetValue(failure.PackageId, out PackageRow row))
                AnimateRow(row, true);
        }

        private void ShowUnexpectedError(string packageId, Exception exception)
        {
            Debug.LogWarning($"Content package UI action failed for '{packageId}': {exception.Message}");
            UIFeedbackService.Play(FeedbackCue.Error);
            if (rows.TryGetValue(packageId, out PackageRow row))
            {
                row.Status.text = PlayerUiErrorText.Body(PlayerUiErrorMapper.FromException(exception));
                row.Status.EnableInClassList("is-error", true);
                AnimateRow(row, true);
            }
        }

        private void RefreshClicked()
        {
            UIFeedbackService.Play(FeedbackCue.ButtonClick);
            _ = ReloadAfterSuspendAsync();
        }

        private void BackToMenu()
        {
            string sceneName = ContentReturnNavigation.PeekOrDefault("002_MainMenuScene");
            NavigateAfterSuspend(
                sceneName,
                DestinationForScene(sceneName),
                true);
        }

        private void ClearRecommendedLaunch()
        {
            if (!string.IsNullOrWhiteSpace(launchedRecommendationId))
                selectedPackageIds.Remove(launchedRecommendationId);
            launchedRecommendationId = null;
            RefreshLaunchBanner();
            RefreshSelectionUi();
        }

        private void NavigatePrimary(MobileDestination destination)
        {
            if (destination == MobileDestination.Content)
            {
                packageList?.ScrollToItem(0);
                return;
            }
            NavigateAfterSuspend(MobilePrimaryNavigation.SceneName(destination), destination, true);
        }

        private async Task ReloadAfterSuspendAsync()
        {
            if (destroyed)
                return;
            refreshAction?.SetEnabled(false);
            try
            {
                await SuspendAllOperationsAsync();
                if (!destroyed)
                    await ReloadCatalogAsync();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Content refresh could not suspend the active queue: " + exception.Message);
                if (!destroyed)
                    ApplyCatalogFailure(
                        loadGeneration,
                        null,
                        PlayerUiErrorMapper.Create(PlayerUiErrorCode.Unexpected));
            }
        }

        private async void NavigateAfterSuspend(
            string sceneName,
            MobileDestination destination,
            bool clearReturn)
        {
            if (destroyed || navigationRequested || string.IsNullOrWhiteSpace(sceneName))
                return;
            navigationRequested = true;
            primaryNavigation?.SetPending(destination);
            UIFeedbackService.Play(destination == MobileDestination.Home
                ? FeedbackCue.Back
                : FeedbackCue.ButtonClick);
            try
            {
                await SuspendAllOperationsAsync();
                if (destroyed)
                    return;
                if (clearReturn)
                    ContentReturnNavigation.Clear();
                SceneManager.LoadScene(sceneName);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Content navigation could not suspend the active queue: " + exception.Message);
                navigationRequested = false;
                primaryNavigation?.ClearPending(MobileDestination.Content);
                UIFeedbackService.Play(FeedbackCue.Error);
            }
        }

        private static MobileDestination DestinationForScene(string sceneName)
        {
            if (string.Equals(sceneName, MobilePrimaryNavigation.SceneName(MobileDestination.Gacha), StringComparison.Ordinal))
                return MobileDestination.Gacha;
            if (string.Equals(sceneName, MobilePrimaryNavigation.SceneName(MobileDestination.Collection), StringComparison.Ordinal))
                return MobileDestination.Collection;
            if (string.Equals(sceneName, MobilePrimaryNavigation.SceneName(MobileDestination.Settings), StringComparison.Ordinal))
                return MobileDestination.Settings;
            return MobileDestination.Home;
        }

        private async Task SuspendAllOperationsAsync()
        {
            ContentPackageInstallQueue queue = installQueue;
            Task queueSuspension = queue == null
                ? Task.CompletedTask
                : queue.SuspendAsync();
            HashSet<string> queuedOperations = queue?.Current.Items
                .Where(value => value.State == ContentPackageQueueItemState.Running)
                .Select(value => value.PackageId)
                .ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>(StringComparer.Ordinal);
            Task[] independentSuspensions = operations
                .Where(pair => pair.Value != null && !queuedOperations.Contains(pair.Key))
                .Select(pair => pair.Value)
                .Distinct()
                .Select(value => (Task)value.SuspendAsync())
                .ToArray();
            await Task.WhenAll(new[] { queueSuspension, Task.WhenAll(independentSuspensions) });
        }

        private void ScrollToLaunchedRecommendation()
        {
            if (string.IsNullOrWhiteSpace(launchedRecommendationId) || packageList == null)
                return;
            int index = displayedItems.FindIndex(item => string.Equals(
                item.Package.PackageId,
                launchedRecommendationId,
                StringComparison.Ordinal));
            if (index < 0)
                return;
            packageList.schedule.Execute(() =>
            {
                if (!destroyed && packageList != null)
                    packageList.ScrollToItem(index);
            });
        }

        private void RefreshLaunchBanner()
        {
            if (launchBanner == null)
                return;
            ContentPackageCatalogEntry entry = string.IsNullOrWhiteSpace(launchedRecommendationId)
                ? null
                : catalog?.Find(launchedRecommendationId);
            bool visible = entry != null;
            launchBanner.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            clearLaunchAction?.SetEnabled(visible);
            if (!visible)
                return;
            launchTitle.text = L("content.recommended.title");
            launchBody.text = string.Format(
                L("content.recommended.body"),
                entry.Metadata.GetDisplayName(CurrentUiLanguageId(), entry.Package.PackageId));
        }

        private void OnSelectedLocaleChanged(Locale locale)
        {
            RefreshLocalization();
        }

        private void OnExperienceSettingsChanged(ExperienceSettings settings)
        {
            ApplyMotionPreference();
        }

        private void ApplyMotionPreference()
        {
            if (pageRoot == null)
                return;
            bool reduceMotion = UIFeedbackService.ReduceMotion;
            pageRoot.EnableInClassList("reduce-motion", reduceMotion);
            if (!reduceMotion)
                return;

            StopEntranceAnimation();
            if (shell != null)
                ResetShellEntranceStyle();
            foreach (PackageRow row in rows.Values)
            {
                row.Animation?.Pause();
                row.Animation = null;
                row.Root.style.opacity = 1f;
                row.Root.style.translate = new Translate(0f, 0f, 0f);
            }
        }

        private void RefreshLocalization()
        {
            ApplyLocalizedChrome();
            if (localizationRoutine != null)
                StopCoroutine(localizationRoutine);
            localizationRoutine = StartCoroutine(LoadLocalizedText());
        }

        private IEnumerator LoadLocalizedText()
        {
            yield return LocalizationSettings.InitializationOperation;
            foreach (KeyValuePair<string, string> pair in EnglishFallbacks)
            {
                var operation = LocalizationSettings.StringDatabase.GetLocalizedStringAsync(StringTable, pair.Key);
                yield return operation;
                localized[pair.Key] = string.IsNullOrWhiteSpace(operation.Result)
                    ? pair.Value
                    : operation.Result;
            }
            ApplyLocalizedChrome();
            if (IsReady)
            {
                ConfigureFilterChoices();
                foreach (VisualElement element in packageList
                             .Query<VisualElement>(className: "content-package-row").ToList())
                {
                    if (!(element.userData is PackageRow row) || row.Entry == null)
                        continue;
                    string packageId = row.Entry.Package.PackageId;
                    row.Name.text = row.Entry.Metadata.GetDisplayName(
                        CurrentUiLanguageId(), row.Entry.Package.PackageId);
                    if (operations.TryGetValue(
                            packageId, out ContentPackageInstallCoordinator operation))
                        ApplyOperation(packageId, operation.Current);
                    else
                    {
                        row.Download.SetLabel(L("content.action.download"));
                        row.Pause.SetLabel(L("content.action.pause"));
                        row.Remove.SetLabel(L("content.action.remove"));
                        row.Cancel.SetLabel(L("content.action.cancel"));
                    }
                }
                ApplyReadyCatalogStatus();
            }
            else
            {
                foreach (KeyValuePair<string, ContentPackageInstallCoordinator> pair in operations)
                    ApplyOperation(pair.Key, pair.Value.Current);
            }
            localizationRoutine = null;
            if (initialCatalogLoadPending && !destroyed)
            {
                initialCatalogLoadPending = false;
                _ = ReloadCatalogAsync();
            }
        }

        private void ApplyLocalizedChrome()
        {
            if (title == null)
                return;
            title.text = L("content.title");
            subtitle.text = L("content.subtitle");
            backAction.SetLabel(L("content.action.back"));
            refreshAction.SetLabel(L("content.action.refresh"));
            searchFilter.label = L("content.filter.search");
            languageFilter.label = L("content.filter.language");
            generationFilter.label = L("content.filter.generation");
            installFilter.label = L("content.filter.install");
            wifiOnlyToggle.label = L("content.policy.wifi_only");
            selectFilteredAction.SetLabel(L("content.action.select_filtered"));
            clearSelectionAction.SetLabel(L("content.action.clear_selection"));
            downloadSelectedAction.SetLabel(L("content.action.download_selected"));
            queuePauseAction.SetLabel(L("content.action.pause"));
            queueResumeAction.SetLabel(L("content.action.resume"));
            queueRetryAction.SetLabel(L("content.action.retry"));
            queueCancelAction.SetLabel(L("content.action.cancel"));
            errorPresenter?.RefreshLanguage();
            clearLaunchAction.SetLabel(L("content.action.choose_another"));
            primaryNavigation?.RefreshText();
            RefreshLaunchBanner();
            if (catalog != null)
                emptyState.text = catalog.Packages.Count == 0
                    ? L("content.catalog.empty")
                    : L("content.filter.empty");
            if (catalog != null)
                RefreshSelectionUi();
        }

        private string L(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return string.Empty;
            if (localized.TryGetValue(key, out string value))
                return value;
            return EnglishFallbacks.TryGetValue(key, out string fallback) ? fallback : key;
        }

        private void ApplyReadyCatalogStatus()
        {
            if (!IsReady)
                return;
            if (catalogUsedCache)
            {
                SetCatalogStatus(
                    string.Format(L("content.catalog.cached"), PackageCount),
                    false,
                    true);
                return;
            }
            if (catalogHasCacheWarning)
            {
                SetCatalogStatus(
                    string.Format(L("content.catalog.cache_warning"), PackageCount),
                    false,
                    true);
                return;
            }
            SetCatalogStatus(catalog.Packages.Count == 0
                ? L("content.catalog.empty")
                : string.Format(L("content.catalog.loaded"), PackageCount), false);
        }

        private void SetCatalogStatus(string value, bool error, bool warning = false)
        {
            if (catalogStatus == null)
                return;
            catalogStatus.text = value;
            catalogStatus.EnableInClassList("is-error", error);
            catalogStatus.EnableInClassList("is-warning", warning && !error);
        }

        private void PlayEntrance()
        {
            StopEntranceAnimation();
            if (shell == null)
                return;

            ResetShellEntranceStyle();
            if (UIFeedbackService.ReduceMotion)
                return;

            float duration = 0.28f / UIFeedbackService.AnimationSpeed;
            entranceAnimation = StartCoroutine(AnimateEntrance(duration));
        }

        private IEnumerator AnimateEntrance(float duration)
        {
            // Android UI Toolkit can retain a stale render transform for a large parent element
            // until the native surface is resized. Never animate opacity or scale on the page
            // shell: a border glow gives entrance feedback without risking the entire interactive
            // subtree becoming visually blank while its hit-test geometry remains active.
            Color target = new Color(66f / 255f, 196f / 255f, 213f / 255f, 0.7f);
            Color start = new Color(target.r, target.g, target.b, 0.16f);
            float startedAt = Time.realtimeSinceStartup;
            while (!destroyed && shell != null && !UIFeedbackService.ReduceMotion)
            {
                float progress = Mathf.SmoothStep(0f, 1f,
                    Mathf.Clamp01((Time.realtimeSinceStartup - startedAt) / duration));
                SetShellBorderColor(Color.Lerp(start, target, progress));
                if (progress >= 1f)
                    break;
                yield return null;
            }

            if (!destroyed && shell != null)
                ResetShellEntranceStyle();
            entranceAnimation = null;
        }

        private void SetShellBorderColor(Color color)
        {
            shell.style.borderTopColor = color;
            shell.style.borderRightColor = color;
            shell.style.borderBottomColor = color;
            shell.style.borderLeftColor = color;
            shell.MarkDirtyRepaint();
        }

        private void ResetShellEntranceStyle()
        {
            if (shell == null)
                return;
            shell.style.borderTopColor = StyleKeyword.Null;
            shell.style.borderRightColor = StyleKeyword.Null;
            shell.style.borderBottomColor = StyleKeyword.Null;
            shell.style.borderLeftColor = StyleKeyword.Null;
            shell.MarkDirtyRepaint();
        }

        private void StopEntranceAnimation()
        {
            if (entranceAnimation == null)
                return;
            StopCoroutine(entranceAnimation);
            entranceAnimation = null;
        }

        private static void AnimateRow(PackageRow row, bool error)
        {
            row.Animation?.Pause();
            if (UIFeedbackService.ReduceMotion)
            {
                row.Root.style.opacity = 1f;
                row.Root.style.translate = new Translate(0f, 0f, 0f);
                return;
            }
            float startedAt = Time.realtimeSinceStartup;
            float duration = 0.18f / UIFeedbackService.AnimationSpeed;
            row.Animation = row.Root.schedule.Execute(() =>
            {
                float progress = Mathf.Clamp01((Time.realtimeSinceStartup - startedAt) / duration);
                row.Root.style.opacity = Mathf.Lerp(0.72f, 1f, progress);
                float offset = error
                    ? Mathf.Sin(progress * Mathf.PI * 4f) * (1f - progress) * 9f
                    : Mathf.Lerp(8f, 0f, Mathf.SmoothStep(0f, 1f, progress));
                row.Root.style.translate = new Translate(offset, 0f, 0f);
                if (progress >= 1f)
                {
                    row.Root.style.opacity = 1f;
                    row.Root.style.translate = new Translate(0f, 0f, 0f);
                    row.Animation?.Pause();
                    row.Animation = null;
                }
            }).Every(16);
        }

        private void ClearOperations()
        {
            foreach (ContentPackageOperationUiBridge bridge in bridges.Values)
                bridge.Dispose();
            foreach (PackageRow row in rows.Values)
            {
                row.Animation?.Pause();
            }
            bridges.Clear();
            operations.Clear();
        }

        private void ShowInitializationFailure(string message)
        {
            InitializationError = message;
            IsReady = false;
            Debug.LogWarning("Content management UI could not initialize: " + message);
            PlayerUiError error = PlayerUiErrorMapper.FromDetail(message);
            errorPresenter?.Show(error);
            if (catalogStatus != null)
                SetCatalogStatus(PlayerUiErrorText.Title(error), true);
        }

        private static bool CanShowCancel(ContentPackageOperationState state)
        {
            return state == ContentPackageOperationState.Downloading ||
                   state == ContentPackageOperationState.Paused ||
                   state == ContentPackageOperationState.Installing ||
                   state == ContentPackageOperationState.Failed;
        }

        private void ConfigurePersistentActions(
            PackageRow row,
            bool downloadEnabled,
            bool pauseEnabled,
            bool removeEnabled,
            bool cancelEnabled)
        {
            // Android UI Toolkit can retain stale TextElement geometry after a Button is hidden,
            // disabled, shown, moved, or repurposed. These four controls are therefore permanent:
            // hierarchy, enabled state, display, visibility, label, class and geometry stay fixed.
            // Controller guards enforce availability without mutating the render node.
            row.Download.SetLabel(L("content.action.download"));
            row.Pause.SetLabel(L("content.action.pause"));
            row.Remove.SetLabel(L("content.action.remove"));
            row.Cancel.SetLabel(L("content.action.cancel"));
            row.DownloadAllowed = downloadEnabled;
            row.PauseAllowed = pauseEnabled;
            row.RemoveAllowed = removeEnabled;
            row.CancelAllowed = cancelEnabled;
            KeepRenderNodeStable(row.Download);
            KeepRenderNodeStable(row.Pause);
            KeepRenderNodeStable(row.Remove);
            KeepRenderNodeStable(row.Cancel);
        }

        private static void KeepRenderNodeStable(MobileActionControl action)
        {
            action.Root.style.visibility = Visibility.Visible;
            action.Root.style.display = DisplayStyle.Flex;
            action.Root.style.opacity = 1f;
            action.Root.pickingMode = PickingMode.Position;
        }

        private void RefreshInstalledState(PackageRow row)
        {
            row.Installed = null;
            row.LifecycleError = null;
            if (lifecycleService == null)
                return;
            try
            {
                row.Installed = lifecycleService.FindInstalled(row.Entry.Package.PackageId);
            }
            catch (Exception exception)
            {
                row.LifecycleError = exception.Message;
                Debug.LogWarning($"Installed content state could not be read for '{row.Entry.Package.PackageId}': {exception.Message}");
            }
        }

        private static PlayerUiError ErrorFor(ContentPackageOperationSnapshot snapshot)
        {
            if (snapshot?.Plan != null)
            {
                PlayerUiError plan = PlayerUiErrorMapper.FromInstallPlan(snapshot.Plan.Status);
                if (plan != null && snapshot.Plan.Status != ContentInstallPlanStatus.Ready &&
                    snapshot.Plan.Status != ContentInstallPlanStatus.AlreadyCurrent)
                    return plan;
            }
            if (snapshot?.InstallResult != null)
            {
                PlayerUiError install = PlayerUiErrorMapper.FromInstall(snapshot.InstallResult.Status);
                if (install != null)
                    return install;
            }
            return PlayerUiErrorMapper.Create(PlayerUiErrorCode.Unexpected);
        }

        private InstalledContentPackage LookupInstalled(string packageId)
        {
            if (lifecycleService == null || string.IsNullOrWhiteSpace(packageId))
                return null;
            return lifecycleService.FindInstalled(packageId);
        }

        private static bool Matches(InstalledContentPackage installed, ContentPackageDescriptor package)
        {
            return ContentPackageLibrary.IsCurrent(installed, package);
        }

        private static void ReloadLocalCatalog()
        {
            CatalogSession session = ApplicationServices.Catalog;
            if (session == null)
                return;
            CatalogLoadResult result = session.EnsureLoaded(true);
            if (result.Succeeded)
                ApplicationServices.Languages?.RefreshContentLanguage(result.Catalog);
        }

        private static string CombineWarnings(string first, string second)
        {
            if (string.IsNullOrWhiteSpace(first))
                return string.IsNullOrWhiteSpace(second) ? null : second.Trim();
            if (string.IsNullOrWhiteSpace(second))
                return first.Trim();
            return first.Trim() + " | " + second.Trim();
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024)
                return bytes + " B";
            if (bytes < 1024L * 1024L)
                return (bytes / 1024d).ToString("0.0") + " KiB";
            if (bytes < 1024L * 1024L * 1024L)
                return (bytes / (1024d * 1024d)).ToString("0.0") + " MiB";
            return (bytes / (1024d * 1024d * 1024d)).ToString("0.0") + " GiB";
        }
    }
}
