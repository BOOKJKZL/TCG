using System;
using System.Collections;
using System.Collections.Generic;
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
                ["content.action.remove"] = "Remove",
                ["content.action.confirm_remove"] = "Confirm remove",
                ["content.catalog.loading"] = "Checking available content...",
                ["content.catalog.loaded"] = "{0} content packs available.",
                ["content.catalog.empty"] = "No downloadable content is listed in this catalog.",
                ["content.catalog.unavailable"] = "The content catalog is unavailable: {0}",
                ["content.catalog.not_configured"] = "Remote content is not configured yet.",
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
                ["content.progress"] = "{0}% · {1} / {2}"
            };

        private sealed class PackageRow
        {
            public PackageRow(
                ContentPackageCatalogEntry entry,
                Action<string> primary,
                Action<string> pause,
                Action<string> cancel,
                Action<string> remove)
            {
                Entry = entry;
                Root = new VisualElement { name = "package-" + entry.Package.PackageId };
                Root.AddToClassList("content-package-row");

                var copy = new VisualElement();
                copy.AddToClassList("content-package-row__copy");
                Name = new Label(entry.Package.PackageId);
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
                Primary = Button("primary", () => primary(entry.Package.PackageId));
                Pause = Button("pause", () => pause(entry.Package.PackageId));
                Cancel = Button("cancel", () => cancel(entry.Package.PackageId));
                Remove = Button("remove", () => remove(entry.Package.PackageId));
                Primary.AddToClassList("content-button--primary");
                Pause.AddToClassList("content-button--quiet");
                Cancel.AddToClassList("content-button--danger");
                Remove.AddToClassList("content-button--danger");
                actions.Add(Primary);
                actions.Add(Pause);
                actions.Add(Cancel);
                actions.Add(Remove);
                controls.Add(Progress);
                controls.Add(actions);
                Root.Add(copy);
                Root.Add(controls);
            }

            public ContentPackageCatalogEntry Entry { get; }
            public VisualElement Root { get; }
            public Label Name { get; }
            public Label Metadata { get; }
            public Label Status { get; }
            public ProgressBar Progress { get; }
            public Button Primary { get; }
            public Button Pause { get; }
            public Button Cancel { get; }
            public Button Remove { get; }
            public InstalledContentPackage Installed { get; set; }
            public string LifecycleError { get; set; }
            public bool AwaitingRemovalConfirmation { get; set; }
            public bool Removing { get; set; }
            public IVisualElementScheduledItem RemovalConfirmationTimeout { get; set; }
            public ContentPackageOperationState? LastState { get; set; }
            public IVisualElementScheduledItem Animation { get; set; }

            private static Button Button(string name, Action clicked)
            {
                var button = new Button(clicked) { name = name + "-button" };
                button.AddToClassList("content-button");
                return button;
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
        private ScrollView packageList;
        private Label title;
        private Label subtitle;
        private Label catalogStatus;
        private Label emptyState;
        private Button backButton;
        private Button refreshButton;
        private ContentPackageCatalog catalog;
        private CancellationTokenSource loadCancellation;
        private Coroutine localizationRoutine;
        private IVisualElementScheduledItem entranceAnimation;
        private int loadGeneration;
        private bool destroyed;

        public static IContentPackageCatalogProvider CatalogProviderOverride { private get; set; }
        public static IContentPackageInstallCoordinatorFactory OperationFactoryOverride { private get; set; }
        public static IContentPackageLifecycleService LifecycleOverride { private get; set; }
        public static IUiThreadDispatcher DispatcherOverride { private get; set; }

        public bool IsReady { get; private set; }
        public string InitializationError { get; private set; }
        public int PackageCount => rows.Count;
        public int LastAppliedThreadId { get; private set; }

        public ContentPackageOperationState? GetPackageState(string packageId)
        {
            return packageId != null && operations.TryGetValue(packageId, out ContentPackageInstallCoordinator operation)
                ? operation.Current.State
                : (ContentPackageOperationState?)null;
        }

        public bool StartOrRetryPackage(string packageId)
        {
            if (packageId == null || !operations.ContainsKey(packageId))
                return false;
            PrimaryClicked(packageId);
            return true;
        }

        public bool PausePackage(string packageId)
        {
            if (packageId == null || !operations.ContainsKey(packageId))
                return false;
            PauseClicked(packageId);
            return true;
        }

        public bool CancelPackage(string packageId)
        {
            if (packageId == null || !operations.ContainsKey(packageId))
                return false;
            CancelClicked(packageId);
            return true;
        }

        public bool RequestRemovePackage(string packageId)
        {
            if (packageId == null || !rows.ContainsKey(packageId))
                return false;
            RemoveClicked(packageId);
            return true;
        }

        public bool IsPackageInstalled(string packageId)
        {
            return packageId != null && rows.TryGetValue(packageId, out PackageRow row) && row.Installed != null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOverrides()
        {
            CatalogProviderOverride = null;
            OperationFactoryOverride = null;
            LifecycleOverride = null;
            DispatcherOverride = null;
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
                experienceSettings = ApplicationServices.ExperienceSettings;
                if (experienceSettings != null)
                    experienceSettings.Changed += OnExperienceSettingsChanged;
                LocalizationSettings.SelectedLocaleChanged += OnSelectedLocaleChanged;
                RefreshLocalization();
                ApplyMotionPreference();
                PlayEntrance();
                _ = ReloadCatalogAsync();
            }
            catch (Exception exception)
            {
                ShowInitializationFailure(exception.Message);
            }
        }

        private void OnDestroy()
        {
            destroyed = true;
            loadGeneration++;
            loadCancellation?.Cancel();
            loadCancellation?.Dispose();
            loadCancellation = null;
            if (localizationRoutine != null)
                StopCoroutine(localizationRoutine);
            localizationRoutine = null;
            entranceAnimation?.Pause();
            entranceAnimation = null;
            LocalizationSettings.SelectedLocaleChanged -= OnSelectedLocaleChanged;
            if (experienceSettings != null)
                experienceSettings.Changed -= OnExperienceSettingsChanged;
            experienceSettings = null;
            ClearOperations();
        }

        private void BuildView()
        {
            if (uiDocument == null)
                throw new InvalidOperationException("Content management scene has no UIDocument.");
            pageRoot = uiDocument.rootVisualElement.Q<VisualElement>("content-management");
            if (pageRoot == null)
                throw new InvalidOperationException("ContentManagementView.uxml is not attached to the UIDocument.");
            shell = pageRoot.Q<VisualElement>("content-shell");
            packageList = pageRoot.Q<ScrollView>("package-list");
            title = pageRoot.Q<Label>("content-title");
            subtitle = pageRoot.Q<Label>("content-subtitle");
            catalogStatus = pageRoot.Q<Label>("catalog-status");
            emptyState = pageRoot.Q<Label>("content-empty");
            backButton = pageRoot.Q<Button>("back-button");
            refreshButton = pageRoot.Q<Button>("refresh-button");
            if (shell == null || packageList == null || title == null || subtitle == null ||
                catalogStatus == null || emptyState == null || backButton == null || refreshButton == null)
                throw new InvalidOperationException("Content management view is missing required named elements.");

            backButton.clicked += BackToMenu;
            refreshButton.clicked += RefreshClicked;
            ApplyLocalizedChrome();
        }

        private async Task ReloadCatalogAsync()
        {
            int generation = ++loadGeneration;
            loadCancellation?.Cancel();
            loadCancellation?.Dispose();
            loadCancellation = new CancellationTokenSource();
            CancellationToken token = loadCancellation.Token;
            IsReady = false;
            InitializationError = null;
            SetCatalogStatus(L("content.catalog.loading"), false);
            refreshButton?.SetEnabled(false);

            if (catalogProvider == null || operationFactory == null)
            {
                ApplyCatalogFailure(generation, L("content.catalog.not_configured"));
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
                        ApplyCatalog(generation, result.Catalog);
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

        private void ApplyCatalog(int generation, ContentPackageCatalog value)
        {
            if (destroyed || generation != loadGeneration)
                return;
            LastAppliedThreadId = Environment.CurrentManagedThreadId;
            catalog = value;
            ClearOperations();
            packageList.Clear();
            rows.Clear();
            notifiedFailures.Clear();

            try
            {
                foreach (ContentPackageCatalogEntry entry in catalog.Packages)
                {
                    ContentPackageInstallCoordinator operation = operationFactory.Create(
                        catalog,
                        entry.Package.PackageId);
                    var bridge = new ContentPackageOperationUiBridge(operation, dispatcher);
                    string packageId = entry.Package.PackageId;
                    bridge.Changed += snapshot => ApplyOperation(packageId, snapshot);
                    bridge.FailureReported += ReportFailure;
                    operations.Add(packageId, operation);
                    bridges.Add(packageId, bridge);

                    var row = new PackageRow(entry, PrimaryClicked, PauseClicked, CancelClicked, RemoveClicked);
                    RefreshInstalledState(row);
                    rows.Add(packageId, row);
                    packageList.Add(row.Root);
                    ApplyOperation(packageId, operation.Current);
                }

                bool empty = rows.Count == 0;
                emptyState.style.display = empty ? DisplayStyle.Flex : DisplayStyle.None;
                packageList.style.display = empty ? DisplayStyle.None : DisplayStyle.Flex;
                SetCatalogStatus(empty
                    ? L("content.catalog.empty")
                    : string.Format(L("content.catalog.loaded"), rows.Count), false);
                IsReady = true;
                InitializationError = null;
                refreshButton.SetEnabled(true);
            }
            catch (Exception exception)
            {
                ApplyCatalogFailure(generation, exception.Message);
            }
        }

        private void ApplyCatalogFailure(int generation, string message)
        {
            if (destroyed || generation != loadGeneration)
                return;
            LastAppliedThreadId = Environment.CurrentManagedThreadId;
            InitializationError = string.IsNullOrWhiteSpace(message)
                ? "Content catalog is unavailable."
                : message.Trim();
            IsReady = false;
            emptyState.style.display = DisplayStyle.Flex;
            emptyState.text = string.Format(L("content.catalog.unavailable"), InitializationError);
            packageList.style.display = DisplayStyle.None;
            SetCatalogStatus(emptyState.text, true);
            refreshButton?.SetEnabled(true);
        }

        private void ApplyOperation(string packageId, ContentPackageOperationSnapshot snapshot)
        {
            if (destroyed || !rows.TryGetValue(packageId, out PackageRow row))
                return;
            LastAppliedThreadId = Environment.CurrentManagedThreadId;
            ContentPackageItemPresentation item = ContentPackageItemPresentation.Create(row.Entry, snapshot);
            ContentPackageOperationState? previous = row.LastState;
            row.LastState = snapshot.State;

            row.Metadata.text = string.Format(
                L("content.package.metadata"),
                item.Version,
                FormatBytes(item.DownloadBytes));
            if (snapshot.State == ContentPackageOperationState.Succeeded ||
                snapshot.State == ContentPackageOperationState.AlreadyCurrent)
                RefreshInstalledState(row);

            bool installedCurrent = Matches(row.Installed, row.Entry.Package);
            string status = L(item.UiState.StatusKey);
            if (row.Removing)
                status = L("content.status.removing");
            else if (row.AwaitingRemovalConfirmation)
                status = L("content.status.remove_confirm");
            else if (!string.IsNullOrWhiteSpace(row.LifecycleError))
                status = string.Format(L("content.status.remove_failed"), row.LifecycleError);
            else if (snapshot.State == ContentPackageOperationState.Idle && row.Installed != null)
                status = L(installedCurrent ? "content.status.current" : "content.status.update_available");
            if (!string.IsNullOrWhiteSpace(item.ErrorMessage))
                status += " · " + item.ErrorMessage;
            else if (!string.IsNullOrWhiteSpace(item.WarningMessage))
                status = string.Format(L("content.status.warning"), item.WarningMessage);
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
            row.Primary.style.display = hasPrimary ? DisplayStyle.Flex : DisplayStyle.None;
            string primaryKey = snapshot.State == ContentPackageOperationState.Idle && row.Installed != null
                ? "content.action.update"
                : item.UiState.PrimaryActionKey;
            row.Primary.text = hasPrimary ? L(primaryKey) : string.Empty;
            row.Primary.SetEnabled(hasPrimary && !item.UiState.IsBusy && !row.Removing && !row.AwaitingRemovalConfirmation);
            row.Pause.style.display = item.CanPause ? DisplayStyle.Flex : DisplayStyle.None;
            row.Pause.text = L("content.action.pause");
            bool canCancel = CanShowCancel(snapshot.State);
            row.Cancel.style.display = canCancel ? DisplayStyle.Flex : DisplayStyle.None;
            row.Cancel.text = L("content.action.cancel");
            bool canRemove = row.Installed != null && !item.UiState.IsBusy && !canCancel;
            row.Remove.style.display = canRemove ? DisplayStyle.Flex : DisplayStyle.None;
            row.Remove.text = L(row.AwaitingRemovalConfirmation
                ? "content.action.confirm_remove"
                : "content.action.remove");
            row.Remove.SetEnabled(canRemove && !row.Removing);
            if (row.Removing)
            {
                row.Primary.style.display = DisplayStyle.None;
                row.Pause.style.display = DisplayStyle.None;
                row.Cancel.style.display = DisplayStyle.None;
            }

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

        private async void PrimaryClicked(string packageId)
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

        private async void RemoveClicked(string packageId)
        {
            if (lifecycleService == null ||
                !rows.TryGetValue(packageId, out PackageRow row) ||
                row.Installed == null || row.Removing ||
                !operations.TryGetValue(packageId, out ContentPackageInstallCoordinator operation))
                return;
            ContentPackageOperationState state = operation.Current.State;
            if (state == ContentPackageOperationState.Planning ||
                state == ContentPackageOperationState.Downloading ||
                state == ContentPackageOperationState.Installing)
                return;

            if (!row.AwaitingRemovalConfirmation)
            {
                row.AwaitingRemovalConfirmation = true;
                row.RemovalConfirmationTimeout?.Pause();
                row.RemovalConfirmationTimeout = row.Root.schedule.Execute(() =>
                {
                    row.AwaitingRemovalConfirmation = false;
                    row.RemovalConfirmationTimeout = null;
                    ApplyOperation(packageId, operation.Current);
                }).StartingIn(4000);
                UIFeedbackService.Play(FeedbackCue.Confirm);
                ApplyOperation(packageId, operation.Current);
                AnimateRow(row, false);
                return;
            }

            row.AwaitingRemovalConfirmation = false;
            row.RemovalConfirmationTimeout?.Pause();
            row.RemovalConfirmationTimeout = null;
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
                    ApplyOperation(packageId, operation.Current);
                    row.Status.text = string.Format(L("content.status.remove_failed"), error);
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
                row.Status.text = string.IsNullOrWhiteSpace(warning)
                    ? L("content.status.removed")
                    : string.Format(L("content.status.remove_warning"), warning);
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
                row.Status.text = L("content.status.failed") + " · " + exception.Message;
                row.Status.EnableInClassList("is-error", true);
                AnimateRow(row, true);
            }
        }

        private void RefreshClicked()
        {
            UIFeedbackService.Play(FeedbackCue.ButtonClick);
            _ = ReloadCatalogAsync();
        }

        private void BackToMenu()
        {
            UIFeedbackService.Play(FeedbackCue.Back);
            SceneManager.LoadScene("002_MainMenuScene");
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

            entranceAnimation?.Pause();
            entranceAnimation = null;
            if (shell != null)
            {
                shell.style.opacity = 1f;
                shell.style.scale = new Scale(Vector3.one);
            }
            foreach (PackageRow row in rows.Values)
            {
                row.Animation?.Pause();
                row.RemovalConfirmationTimeout?.Pause();
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
            foreach (KeyValuePair<string, ContentPackageInstallCoordinator> pair in operations)
                ApplyOperation(pair.Key, pair.Value.Current);
            localizationRoutine = null;
        }

        private void ApplyLocalizedChrome()
        {
            if (title == null)
                return;
            title.text = L("content.title");
            subtitle.text = L("content.subtitle");
            backButton.text = L("content.action.back");
            refreshButton.text = L("content.action.refresh");
            emptyState.text = L("content.catalog.empty");
        }

        private string L(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return string.Empty;
            if (localized.TryGetValue(key, out string value))
                return value;
            return EnglishFallbacks.TryGetValue(key, out string fallback) ? fallback : key;
        }

        private void SetCatalogStatus(string value, bool error)
        {
            if (catalogStatus == null)
                return;
            catalogStatus.text = value;
            catalogStatus.EnableInClassList("is-error", error);
        }

        private void PlayEntrance()
        {
            entranceAnimation?.Pause();
            if (shell == null || UIFeedbackService.ReduceMotion)
            {
                if (shell != null)
                {
                    shell.style.opacity = 1f;
                    shell.style.scale = new Scale(Vector3.one);
                }
                return;
            }

            float startedAt = Time.realtimeSinceStartup;
            float duration = 0.28f / UIFeedbackService.AnimationSpeed;
            shell.style.opacity = 0f;
            shell.style.scale = new Scale(new Vector3(0.97f, 0.97f, 1f));
            entranceAnimation = shell.schedule.Execute(() =>
            {
                float progress = Mathf.SmoothStep(0f, 1f,
                    Mathf.Clamp01((Time.realtimeSinceStartup - startedAt) / duration));
                shell.style.opacity = progress;
                float scale = Mathf.Lerp(0.97f, 1f, progress);
                shell.style.scale = new Scale(new Vector3(scale, scale, 1f));
                if (progress >= 1f)
                {
                    entranceAnimation?.Pause();
                    entranceAnimation = null;
                }
            }).Every(16);
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
                row.RemovalConfirmationTimeout?.Pause();
            }
            bridges.Clear();
            operations.Clear();
        }

        private void ShowInitializationFailure(string message)
        {
            InitializationError = message;
            IsReady = false;
            Debug.LogWarning("Content management UI could not initialize: " + message);
            UIFeedbackService.Play(FeedbackCue.Error);
            if (catalogStatus != null)
                SetCatalogStatus(string.Format(L("content.catalog.unavailable"), message), true);
        }

        private static bool CanShowCancel(ContentPackageOperationState state)
        {
            return state == ContentPackageOperationState.Downloading ||
                   state == ContentPackageOperationState.Paused ||
                   state == ContentPackageOperationState.Installing ||
                   state == ContentPackageOperationState.Failed;
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
            }
        }

        private static bool Matches(InstalledContentPackage installed, ContentPackageDescriptor package)
        {
            return installed != null && package != null &&
                   installed.Revision >= package.Revision &&
                   string.Equals(installed.InstallRelativePath, package.InstallRelativePath, StringComparison.Ordinal) &&
                   string.Equals(installed.Sha256, package.Sha256, StringComparison.OrdinalIgnoreCase);
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
