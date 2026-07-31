using System;
using System.Collections;
using System.IO;
using System.Linq;
using Gacha.Application;
using Gacha.Presentation;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class SaveRecoverySettingsPanel : MonoBehaviour
{
    private const string SettingsSceneName = "005_SettingScene";
    private CanvasGroup canvasGroup;
    private TMP_Text titleText;
    private TMP_Text descriptionText;
    private TMP_Text previewText;
    private TMP_Text statusText;
    private Button exportButton;
    private Button chooseImportButton;
    private Button confirmImportButton;
    private Button cloudConflictButton;
    private InventoryRecoveryService recovery;
    private RecoveryDocumentPicker picker;
    private string pendingImportPath;
    private InventoryRecoveryPreview pendingPreview;
    private Coroutine transitionRoutine;

    public bool HasPendingImport => pendingPreview != null;
    public string LastBackupPath { get; private set; }
    public InventoryRecoveryPreview PendingPreview => pendingPreview;

    public static SaveRecoverySettingsPanel Create(Transform parent)
    {
        if (parent == null) throw new ArgumentNullException(nameof(parent));
        GameObject root = CreateUiObject("SaveRecoverySettingsPanel", parent);
        root.SetActive(false);
        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.anchorMin = rootRect.anchorMax = rootRect.pivot = new Vector2(0.5f, 0.5f);
        rootRect.anchoredPosition = new Vector2(0f, -820f);
        rootRect.sizeDelta = new Vector2(840f, 320f);

        Image background = root.AddComponent<Image>();
        background.color = new Color(0.045f, 0.13f, 0.20f, 0.96f);
        Outline outline = root.AddComponent<Outline>();
        outline.effectColor = new Color(0.32f, 0.82f, 0.67f, 0.78f);
        outline.effectDistance = new Vector2(3f, -3f);

        var panel = root.AddComponent<SaveRecoverySettingsPanel>();
        panel.canvasGroup = root.AddComponent<CanvasGroup>();
        panel.titleText = CreateText(root.transform, "RecoveryTitle", new Vector2(0f, 130f), new Vector2(740f, 42f), 32f, FontStyles.Bold);
        panel.descriptionText = CreateText(root.transform, "RecoveryDescription", new Vector2(0f, 91f), new Vector2(740f, 38f), 18f, FontStyles.Normal);
        panel.exportButton = CreateButton(root.transform, "ExportSaveButton", new Vector2(-285f, 33f), out _);
        panel.chooseImportButton = CreateButton(root.transform, "ChooseImportButton", new Vector2(-95f, 33f), out _);
        panel.confirmImportButton = CreateButton(root.transform, "ConfirmImportButton", new Vector2(95f, 33f), out _);
        panel.cloudConflictButton = CreateButton(root.transform, "CloudConflictButton", new Vector2(285f, 33f), out _);
        panel.previewText = CreateText(root.transform, "RecoveryPreview", new Vector2(0f, -48f), new Vector2(740f, 58f), 17f, FontStyles.Normal);
        panel.statusText = CreateText(root.transform, "RecoveryStatus", new Vector2(0f, -124f), new Vector2(740f, 42f), 16f, FontStyles.Italic);
        panel.previewText.color = new Color(0.82f, 0.92f, 0.98f, 1f);
        panel.statusText.color = new Color(0.72f, 0.87f, 0.82f, 1f);
        panel.exportButton.onClick.AddListener(panel.ExportSave);
        panel.chooseImportButton.onClick.AddListener(panel.ChooseImport);
        panel.confirmImportButton.onClick.AddListener(() => panel.ConfirmImport());
        CloudConflictSettingsDialog conflictDialog = CloudConflictSettingsDialog.Create(parent);
        panel.cloudConflictButton.onClick.AddListener(conflictDialog.Open);
        root.SetActive(true);
        return panel;
    }

    private void OnEnable()
    {
        recovery = new InventoryRecoveryService();
        picker = RecoveryDocumentPicker.GetOrCreate();
        pendingImportPath = null;
        pendingPreview = null;
        LastBackupPath = null;
        confirmImportButton.interactable = false;
        UnityEngine.Localization.Settings.LocalizationSettings.SelectedLocaleChanged += OnSelectedLocaleChanged;
        RefreshText();
        SetStatus(CardUiText.Get("settings.recovery.status.ready"), false);
        SetInteractable(CanUseRecovery());
        PlayEntrance();
    }

    private void OnDisable()
    {
        UnityEngine.Localization.Settings.LocalizationSettings.SelectedLocaleChanged -= OnSelectedLocaleChanged;
        if (transitionRoutine != null) StopCoroutine(transitionRoutine);
        transitionRoutine = null;
    }

    private void OnSelectedLocaleChanged(Locale locale)
    {
        RefreshText();
        SetStatus(CardUiText.Get("settings.recovery.status.ready"), false);
    }

    public void ExportSave()
    {
        if (!TryCreateTarget(out UnityPlayerRecoveryTarget target, out string error))
        {
            SetStatus(error, true);
            return;
        }
        try
        {
            SetBusy(true);
            string directory = StagingDirectory();
            Directory.CreateDirectory(directory);
            string fileName = "universal-gacha-save-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".gachasave";
            string stagingPath = Path.Combine(directory, fileName);
            recovery.Export(stagingPath, target.Capture(), RecoveryInstallIdentity.GetOrCreate());
            picker.CreateDocument(stagingPath, fileName, result =>
            {
                SetBusy(false);
                if (result.Succeeded)
                {
                    SetStatus(CardUiText.Format("settings.recovery.status.exported", result.Path), false);
                    UIFeedbackService.Play(FeedbackCue.Confirm);
                }
                else if (result.Cancelled)
                {
                    SetStatus(CardUiText.Get("settings.recovery.status.cancelled"), false);
                    UIFeedbackService.Play(FeedbackCue.Back);
                }
                else
                {
                    SetStatus(CardUiText.Format("settings.recovery.status.error", result.Error), true);
                    UIFeedbackService.Play(FeedbackCue.Error);
                }
            });
        }
        catch (Exception exception)
        {
            SetBusy(false);
            SetStatus(CardUiText.Format("settings.recovery.status.error", exception.Message), true);
            UIFeedbackService.Play(FeedbackCue.Error);
        }
    }

    public void ChooseImport()
    {
        try
        {
            SetBusy(true);
            string directory = StagingDirectory();
            Directory.CreateDirectory(directory);
            string destination = Path.Combine(directory, "incoming-preview.gachasave");
            picker.OpenDocument(destination, result =>
            {
                SetBusy(false);
                if (result.Succeeded)
                    PreviewImport(result.Path);
                else if (result.Cancelled)
                {
                    SetStatus(CardUiText.Get("settings.recovery.status.cancelled"), false);
                    UIFeedbackService.Play(FeedbackCue.Back);
                }
                else
                {
                    SetStatus(CardUiText.Format("settings.recovery.status.error", result.Error), true);
                    UIFeedbackService.Play(FeedbackCue.Error);
                }
            });
        }
        catch (Exception exception)
        {
            SetBusy(false);
            SetStatus(CardUiText.Format("settings.recovery.status.error", exception.Message), true);
            UIFeedbackService.Play(FeedbackCue.Error);
        }
    }

    public bool PreviewImport(string path)
    {
        try
        {
            pendingPreview = recovery.Preview(path);
            pendingImportPath = path;
            confirmImportButton.interactable = true;
            previewText.text = FormatPreview(pendingPreview);
            SetStatus(CardUiText.Get("settings.recovery.status.preview_ready"), false);
            UIFeedbackService.Play(FeedbackCue.Confirm);
            return true;
        }
        catch (Exception exception)
        {
            pendingPreview = null;
            pendingImportPath = null;
            confirmImportButton.interactable = false;
            previewText.text = string.Empty;
            SetStatus(CardUiText.Format("settings.recovery.status.error", exception.Message), true);
            UIFeedbackService.Play(FeedbackCue.Error);
            return false;
        }
    }

    public bool ConfirmImport()
    {
        if (pendingPreview == null || string.IsNullOrWhiteSpace(pendingImportPath))
            return false;
        if (!TryCreateTarget(out UnityPlayerRecoveryTarget target, out string error))
        {
            SetStatus(error, true);
            return false;
        }
        try
        {
            SetBusy(true);
            string backupDirectory = Path.Combine(Application.persistentDataPath, "Recovery", "Backups");
            Directory.CreateDirectory(backupDirectory);
            string backupPath = Path.Combine(
                backupDirectory,
                "pre-import-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".gachasave");
            InventoryRecoveryImportResult result = recovery.Restore(
                pendingImportPath,
                backupPath,
                target,
                RecoveryInstallIdentity.GetOrCreate());
            LastBackupPath = result.BackupPath;
            pendingPreview = null;
            pendingImportPath = null;
            confirmImportButton.interactable = false;
            previewText.text = string.Empty;
            SetBusy(false);
            SetStatus(CardUiText.Format("settings.recovery.status.imported", LastBackupPath), false);
            UIFeedbackService.Play(FeedbackCue.CollectionNew, true);
            if (CloudSaveServiceWrapper.IsReady)
                _ = CloudSaveServiceWrapper.SaveInventoryAsync(Inventory.Instance.Data);
            return true;
        }
        catch (Exception exception)
        {
            SetBusy(false);
            SetStatus(CardUiText.Format("settings.recovery.status.error", exception.Message), true);
            UIFeedbackService.Play(FeedbackCue.Error);
            return false;
        }
    }

    private bool TryCreateTarget(out UnityPlayerRecoveryTarget target, out string error)
    {
        target = null;
        error = null;
        if (Inventory.Instance == null || !ApplicationServices.IsConfigured ||
            ApplicationServices.Languages == null || ApplicationServices.ExperienceSettings == null)
        {
            error = CardUiText.Get("settings.recovery.status.unavailable");
            return false;
        }
        CatalogLoadResult load = ApplicationServices.Catalog.EnsureLoaded();
        target = new UnityPlayerRecoveryTarget(
            Inventory.Instance,
            LocalSaveService.Save,
            ApplicationServices.Languages,
            ApplicationServices.ExperienceSettings,
            load.Succeeded ? load.Catalog : null);
        return true;
    }

    private void RefreshText()
    {
        titleText.text = CardUiText.Get("settings.recovery.title");
        descriptionText.text = CardUiText.Get("settings.recovery.description");
        exportButton.GetComponentInChildren<TMP_Text>().text = CardUiText.Get("settings.recovery.action.export");
        chooseImportButton.GetComponentInChildren<TMP_Text>().text = CardUiText.Get("settings.recovery.action.preview");
        confirmImportButton.GetComponentInChildren<TMP_Text>().text = CardUiText.Get("settings.recovery.action.confirm");
        cloudConflictButton.GetComponentInChildren<TMP_Text>().text = CardUiText.Get("settings.recovery.action.cloud");
        previewText.text = pendingPreview == null ? string.Empty : FormatPreview(pendingPreview);
    }

    private static string FormatPreview(InventoryRecoveryPreview preview)
    {
        return CardUiText.Format(
            "settings.recovery.preview",
            preview.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
            preview.DistinctPrintingCount,
            preview.TotalCardCount,
            preview.TotalProductsOpened,
            preview.HistoryCount,
            preview.UiLanguageId,
            preview.ContentLanguageId);
    }

    private void SetBusy(bool busy)
    {
        exportButton.interactable = !busy && CanUseRecovery();
        chooseImportButton.interactable = !busy && CanUseRecovery();
        confirmImportButton.interactable = !busy && pendingPreview != null;
        cloudConflictButton.interactable = !busy;
    }

    private void SetInteractable(bool interactable)
    {
        exportButton.interactable = interactable;
        chooseImportButton.interactable = interactable;
        confirmImportButton.interactable = interactable && pendingPreview != null;
        cloudConflictButton.interactable = true;
    }

    private static bool CanUseRecovery() =>
        Inventory.Instance != null && ApplicationServices.IsConfigured;

    private void SetStatus(string message, bool error)
    {
        statusText.text = message ?? string.Empty;
        statusText.color = error
            ? new Color(1f, 0.62f, 0.64f, 1f)
            : new Color(0.72f, 0.87f, 0.82f, 1f);
    }

    private void PlayEntrance()
    {
        if (UIFeedbackService.ReduceMotion)
        {
            canvasGroup.alpha = 1f;
            transform.localScale = Vector3.one;
            return;
        }
        transitionRoutine = StartCoroutine(AnimateEntrance());
    }

    private IEnumerator AnimateEntrance()
    {
        canvasGroup.alpha = 0f;
        transform.localScale = Vector3.one * 0.97f;
        float elapsed = 0f;
        float duration = 0.24f / UIFeedbackService.AnimationSpeed;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            canvasGroup.alpha = progress;
            transform.localScale = Vector3.LerpUnclamped(Vector3.one * 0.97f, Vector3.one, progress);
            yield return null;
        }
        canvasGroup.alpha = 1f;
        transform.localScale = Vector3.one;
        transitionRoutine = null;
    }

    private static string StagingDirectory() =>
        Path.Combine(Application.persistentDataPath, "Recovery", "Staging");

    private static Button CreateButton(
        Transform parent,
        string name,
        Vector2 position,
        out TMP_Text label)
    {
        GameObject buttonObject = CreateUiObject(name, parent);
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(175f, 58f);
        Image image = buttonObject.AddComponent<Image>();
        image.color = new Color(0.10f, 0.42f, 0.38f, 0.98f);
        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        buttonObject.AddComponent<GameFeedbackButton>().Configure(FeedbackCue.Confirm);
        label = CreateText(buttonObject.transform, "Label", Vector2.zero, new Vector2(165f, 46f), 17f, FontStyles.Bold);
        return button;
    }

    private static TMP_Text CreateText(
        Transform parent,
        string name,
        Vector2 position,
        Vector2 size,
        float fontSize,
        FontStyles style)
    {
        GameObject textObject = CreateUiObject(name, parent);
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.enableWordWrapping = true;
        text.raycastTarget = false;
        return text;
    }

    private static GameObject CreateUiObject(string name, Transform parent)
    {
        var result = new GameObject(name, typeof(RectTransform));
        result.layer = 5;
        result.transform.SetParent(parent, false);
        return result;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InitializeInstaller()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallInitialScene()
    {
        Install(SceneManager.GetActiveScene());
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Install(scene);
    }

    private static void Install(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded || scene.name != SettingsSceneName)
            return;
        if (scene.GetRootGameObjects().Any(root =>
            root.GetComponentInChildren<SaveRecoverySettingsPanel>(true) != null))
        {
            return;
        }
        Canvas canvas = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Canvas>(true))
            .FirstOrDefault(candidate => candidate.isRootCanvas);
        if (canvas != null) Create(canvas.transform);
    }
}
