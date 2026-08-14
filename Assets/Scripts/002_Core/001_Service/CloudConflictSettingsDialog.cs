using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using Gacha.Application;
using Gacha.Presentation;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;

public sealed class CloudConflictSettingsDialog : MonoBehaviour
{
    private CanvasGroup canvasGroup;
    private RectTransform card;
    private TMP_Text titleText;
    private TMP_Text descriptionText;
    private TMP_Text localTitleText;
    private TMP_Text localSummaryText;
    private TMP_Text cloudTitleText;
    private TMP_Text cloudSummaryText;
    private TMP_Text mergeNoticeText;
    private TMP_Text identityStatusText;
    private TMP_Text statusText;
    private Button keepLocalButton;
    private Button useCloudButton;
    private Button mergeButton;
    private Button identityButton;
    private Button closeButton;
    private Coroutine transitionRoutine;
    private string statusKey;
    private string statusArgument;
    private string identityStatusKey;
    private string identityStatusArgument;
    private bool identityStatusError;

    public bool IsOpen { get; private set; }
    public string LastBackupPath { get; private set; }

    public static CloudConflictSettingsDialog Create(Transform parent)
    {
        if (parent == null) throw new ArgumentNullException(nameof(parent));
        CloudConflictSettingsDialog existing = parent.GetComponentInChildren<CloudConflictSettingsDialog>(true);
        if (existing != null) return existing;

        GameObject root = CreateUiObject("CloudConflictSettingsDialog", parent);
        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = rootRect.offsetMax = Vector2.zero;
        Image blocker = root.AddComponent<Image>();
        blocker.color = new Color(0.005f, 0.015f, 0.03f, 0.82f);

        var dialog = root.AddComponent<CloudConflictSettingsDialog>();
        dialog.canvasGroup = root.AddComponent<CanvasGroup>();
        dialog.canvasGroup.alpha = 0f;
        dialog.canvasGroup.interactable = false;
        dialog.canvasGroup.blocksRaycasts = false;

        GameObject cardObject = CreateUiObject("CloudConflictCard", root.transform);
        dialog.card = cardObject.GetComponent<RectTransform>();
        dialog.card.anchorMin = dialog.card.anchorMax = dialog.card.pivot = new Vector2(0.5f, 0.5f);
        dialog.card.sizeDelta = new Vector2(860f, 940f);
        Image cardImage = cardObject.AddComponent<Image>();
        cardImage.color = new Color(0.045f, 0.105f, 0.17f, 0.99f);
        Outline outline = cardObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.35f, 0.70f, 1f, 0.82f);
        outline.effectDistance = new Vector2(4f, -4f);

        dialog.titleText = CreateText(cardObject.transform, "CloudConflictTitle", new Vector2(0f, 390f), new Vector2(740f, 62f), 38f, FontStyles.Bold);
        dialog.descriptionText = CreateText(cardObject.transform, "CloudConflictDescription", new Vector2(0f, 322f), new Vector2(740f, 70f), 20f, FontStyles.Normal);
        dialog.localTitleText = CreateText(cardObject.transform, "LocalSaveTitle", new Vector2(-205f, 235f), new Vector2(330f, 45f), 25f, FontStyles.Bold);
        dialog.cloudTitleText = CreateText(cardObject.transform, "CloudSaveTitle", new Vector2(205f, 235f), new Vector2(330f, 45f), 25f, FontStyles.Bold);
        dialog.localSummaryText = CreateText(cardObject.transform, "LocalSaveSummary", new Vector2(-205f, 145f), new Vector2(350f, 125f), 19f, FontStyles.Normal);
        dialog.cloudSummaryText = CreateText(cardObject.transform, "CloudSaveSummary", new Vector2(205f, 145f), new Vector2(350f, 125f), 19f, FontStyles.Normal);
        dialog.mergeNoticeText = CreateText(cardObject.transform, "SafeMergeNotice", new Vector2(0f, 20f), new Vector2(740f, 75f), 19f, FontStyles.Italic);
        dialog.identityStatusText = CreateText(cardObject.transform, "PlayerIdentityStatus", new Vector2(-145f, -218f), new Vector2(430f, 74f), 18f, FontStyles.Normal);
        dialog.statusText = CreateText(cardObject.transform, "CloudConflictStatus", new Vector2(0f, -305f), new Vector2(740f, 70f), 18f, FontStyles.Normal);
        dialog.localSummaryText.color = new Color(0.76f, 0.90f, 1f, 1f);
        dialog.cloudSummaryText.color = new Color(0.79f, 0.95f, 0.88f, 1f);
        dialog.mergeNoticeText.color = new Color(0.86f, 0.87f, 0.72f, 1f);

        dialog.keepLocalButton = CreateButton(cardObject.transform, "KeepLocalButton", new Vector2(-250f, -135f), FeedbackCue.Confirm);
        dialog.useCloudButton = CreateButton(cardObject.transform, "UseCloudButton", new Vector2(0f, -135f), FeedbackCue.Confirm);
        dialog.mergeButton = CreateButton(cardObject.transform, "SafeMergeButton", new Vector2(250f, -135f), FeedbackCue.Confirm);
        dialog.identityButton = CreateButton(cardObject.transform, "ConnectIdentityButton", new Vector2(245f, -218f), FeedbackCue.Confirm);
        dialog.closeButton = CreateButton(cardObject.transform, "CloseConflictButton", new Vector2(0f, -395f), FeedbackCue.Back);
        dialog.keepLocalButton.onClick.AddListener(() => dialog.Resolve(InventoryConflictChoice.KeepLocal));
        dialog.useCloudButton.onClick.AddListener(() => dialog.Resolve(InventoryConflictChoice.UseCloud));
        dialog.mergeButton.onClick.AddListener(() => dialog.Resolve(InventoryConflictChoice.SafeMerge));
        dialog.identityButton.onClick.AddListener(dialog.ConnectIdentity);
        dialog.closeButton.onClick.AddListener(dialog.Close);
        dialog.RefreshLocalizedText();
        dialog.RefreshConflict();
        root.transform.SetAsLastSibling();
        return dialog;
    }

    private void OnEnable()
    {
        GameCloudConflictSession.Current.Changed += OnConflictChanged;
        GameIdentityService.Changed += OnIdentityChanged;
        LocalizationSettings.SelectedLocaleChanged += OnSelectedLocaleChanged;
    }

    private void OnDisable()
    {
        GameCloudConflictSession.Current.Changed -= OnConflictChanged;
        GameIdentityService.Changed -= OnIdentityChanged;
        LocalizationSettings.SelectedLocaleChanged -= OnSelectedLocaleChanged;
        if (transitionRoutine != null) StopCoroutine(transitionRoutine);
        transitionRoutine = null;
    }

    public void Open()
    {
        if (Inventory.Instance != null)
            GameCloudConflictSession.Current.RefreshLocal(Inventory.Instance.Data);
        IsOpen = true;
        transform.SetAsLastSibling();
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;
        statusKey = GameCloudConflictSession.Current.HasPending
            ? null
            : "settings.cloud.status.none";
        statusArgument = null;
        identityStatusKey = null;
        identityStatusArgument = null;
        identityStatusError = false;
        RefreshLocalizedText();
        RefreshConflict();
        RefreshIdentity();
        PlayVisibility(true);
        UIFeedbackService.Play(FeedbackCue.Confirm);
    }

    public void Close()
    {
        if (!IsOpen || GameCloudConflictSession.Current.IsResolving) return;
        IsOpen = false;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
        PlayVisibility(false);
    }

    private async void Resolve(InventoryConflictChoice choice)
    {
        if (!GameCloudConflictSession.Current.HasPending || GameCloudConflictSession.Current.IsResolving)
            return;

        try
        {
            LastBackupPath = CreateSafetyBackup();
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Cloud conflict safety backup failed: " + exception);
            SetStatus("settings.cloud.status.backup_failed_safe", null, true);
            UIFeedbackService.Play(FeedbackCue.Error);
            return;
        }

        SetStatus("settings.cloud.status.resolving", null, false);
        SetChoiceButtons(false);
        InventoryConflictResolutionResult result = await GameCloudConflictSession.Current.ResolveAsync(
            choice,
            new UnityInventoryConflictTarget());
        if (result.Succeeded)
        {
            Debug.Log("Cloud conflict resolved with a verified safety backup.");
            SetStatus("settings.cloud.status.resolved_safe", null, false);
            UIFeedbackService.Play(FeedbackCue.CollectionNew, true);
        }
        else
        {
            Debug.LogWarning("Cloud conflict resolution failed: " + result.Error);
            SetStatus("settings.cloud.status.failed_safe", null, true);
            UIFeedbackService.Play(FeedbackCue.Error);
        }
        RefreshConflict();
        RefreshIdentity();
    }

    private async void ConnectIdentity()
    {
        if (GameIdentityService.IsBusy || GameCloudConflictSession.Current.HasPending)
            return;

        identityStatusKey = "settings.identity.status.connecting";
        identityStatusArgument = null;
        identityStatusError = false;
        RefreshIdentity();
        GameIdentityConnectResult result = await GameIdentityService.ConnectAsync();
        switch (result.Outcome)
        {
            case GameIdentityConnectOutcome.LinkedCurrentPlayer:
            case GameIdentityConnectOutcome.ExistingPlayerReady:
                identityStatusKey = "settings.identity.status.linked";
                identityStatusArgument = null;
                identityStatusError = false;
                UIFeedbackService.Play(FeedbackCue.CollectionNew, true);
                break;
            case GameIdentityConnectOutcome.LinkedCurrentPlayerCloudPending:
                Debug.LogWarning("Player identity linked with pending cloud sync: " + result.Error);
                identityStatusKey = "settings.identity.status.cloud_pending_safe";
                identityStatusArgument = null;
                identityStatusError = true;
                UIFeedbackService.Play(FeedbackCue.Error);
                break;
            case GameIdentityConnectOutcome.ConflictPending:
                identityStatusKey = "settings.identity.status.conflict";
                identityStatusArgument = null;
                identityStatusError = false;
                UIFeedbackService.Play(FeedbackCue.Confirm);
                break;
            case GameIdentityConnectOutcome.ExternalSetupRequired:
                identityStatusKey = "settings.identity.status.setup_required";
                identityStatusArgument = null;
                identityStatusError = true;
                UIFeedbackService.Play(FeedbackCue.Error);
                break;
            default:
                Debug.LogWarning("Player identity was not changed: " + result.Error);
                identityStatusKey = "settings.identity.status.failed_safe";
                identityStatusArgument = null;
                identityStatusError = true;
                UIFeedbackService.Play(FeedbackCue.Error);
                break;
        }
        RefreshLocalizedText();
        RefreshConflict();
        RefreshIdentity();
    }

    private static string CreateSafetyBackup()
    {
        if (Inventory.Instance == null || !ApplicationServices.IsConfigured ||
            ApplicationServices.Languages == null || ApplicationServices.ExperienceSettings == null)
        {
            throw new InvalidOperationException("Player recovery services are unavailable.");
        }

        var target = new UnityPlayerRecoveryTarget(
            Inventory.Instance,
            LocalSaveService.Save,
            ApplicationServices.Languages,
            ApplicationServices.ExperienceSettings,
            null);
        string directory = Path.Combine(Application.persistentDataPath, "Recovery", "Backups");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(
            directory,
            "pre-cloud-choice-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".gachasave");
        new InventoryRecoveryService().Export(path, target.Capture(), RecoveryInstallIdentity.GetOrCreate());
        return Path.GetFullPath(path);
    }

    private void OnConflictChanged()
    {
        if (IsOpen)
        {
            RefreshConflict();
            RefreshIdentity();
        }
    }

    private void OnIdentityChanged()
    {
        if (IsOpen) RefreshIdentity();
    }

    private void OnSelectedLocaleChanged(Locale locale)
    {
        RefreshLocalizedText();
        RefreshConflict();
    }

    private void RefreshLocalizedText()
    {
        titleText.text = CardUiText.Get("settings.cloud.title");
        descriptionText.text = CardUiText.Get("settings.cloud.description");
        localTitleText.text = CardUiText.Get("settings.cloud.local");
        cloudTitleText.text = CardUiText.Get("settings.cloud.remote");
        mergeNoticeText.text = CardUiText.Get("settings.cloud.merge_notice");
        keepLocalButton.GetComponentInChildren<TMP_Text>().text = CardUiText.Get("settings.cloud.action.local");
        useCloudButton.GetComponentInChildren<TMP_Text>().text = CardUiText.Get("settings.cloud.action.remote");
        mergeButton.GetComponentInChildren<TMP_Text>().text = CardUiText.Get("settings.cloud.action.merge");
        identityButton.GetComponentInChildren<TMP_Text>().text = CardUiText.Get("settings.identity.action.connect");
        closeButton.GetComponentInChildren<TMP_Text>().text = CardUiText.Get("settings.cloud.action.close");
        if (!string.IsNullOrWhiteSpace(statusKey))
            statusText.text = statusArgument == null
                ? CardUiText.Get(statusKey)
                : CardUiText.Format(statusKey, statusArgument);
        RefreshIdentity();
    }

    private void RefreshConflict()
    {
        InventoryConflictPreview preview = GameCloudConflictSession.Current.PendingPreview;
        if (preview == null)
        {
            localSummaryText.text = "—";
            cloudSummaryText.text = "—";
            SetChoiceButtons(false);
            return;
        }

        localSummaryText.text = FormatSummary(preview.Local);
        cloudSummaryText.text = FormatSummary(preview.Cloud);
        SetChoiceButtons(!GameCloudConflictSession.Current.IsResolving);
    }

    private void RefreshIdentity()
    {
        GameIdentityStatus status = GameIdentityService.GetStatus();
        if (status.Kind == GameIdentityStatusKind.Busy)
        {
            identityStatusText.text = CardUiText.Get("settings.identity.status.connecting");
            identityStatusText.color = new Color(0.72f, 0.90f, 0.82f, 1f);
        }
        else if (!string.IsNullOrWhiteSpace(identityStatusKey))
        {
            identityStatusText.text = identityStatusArgument == null
                ? CardUiText.Get(identityStatusKey)
                : CardUiText.Format(identityStatusKey, identityStatusArgument);
            identityStatusText.color = identityStatusError
                ? new Color(1f, 0.62f, 0.64f, 1f)
                : new Color(0.72f, 0.90f, 0.82f, 1f);
        }
        else
        {
            switch (status.Kind)
            {
                case GameIdentityStatusKind.Connected:
                    identityStatusText.text = CardUiText.Format(
                        "settings.identity.status.connected",
                        status.RedactedIdentity);
                    identityStatusText.color = new Color(0.72f, 0.90f, 0.82f, 1f);
                    break;
                case GameIdentityStatusKind.Available:
                    identityStatusText.text = CardUiText.Get("settings.identity.status.available");
                    identityStatusText.color = new Color(0.76f, 0.90f, 1f, 1f);
                    break;
                default:
                    identityStatusText.text = CardUiText.Get("settings.identity.status.setup_required");
                    identityStatusText.color = new Color(0.88f, 0.76f, 0.62f, 1f);
                    break;
            }
        }

        identityButton.interactable = status.Kind == GameIdentityStatusKind.Available &&
                                      !GameCloudConflictSession.Current.HasPending;
    }

    private static string FormatSummary(InventoryProgressSummary summary)
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

    private void SetChoiceButtons(bool interactable)
    {
        keepLocalButton.interactable = interactable;
        useCloudButton.interactable = interactable;
        mergeButton.interactable = interactable;
    }

    private void SetStatus(string key, string argument, bool error)
    {
        statusKey = key;
        statusArgument = argument;
        statusText.text = argument == null ? CardUiText.Get(key) : CardUiText.Format(key, argument);
        statusText.color = error
            ? new Color(1f, 0.62f, 0.64f, 1f)
            : new Color(0.72f, 0.90f, 0.82f, 1f);
    }

    private void PlayVisibility(bool opening)
    {
        if (transitionRoutine != null) StopCoroutine(transitionRoutine);
        if (!isActiveAndEnabled || UIFeedbackService.ReduceMotion)
        {
            canvasGroup.alpha = opening ? 1f : 0f;
            card.localScale = Vector3.one;
            return;
        }
        transitionRoutine = StartCoroutine(AnimateVisibility(opening));
    }

    private IEnumerator AnimateVisibility(bool opening)
    {
        float startAlpha = canvasGroup.alpha;
        float endAlpha = opening ? 1f : 0f;
        Vector3 startScale = card.localScale;
        Vector3 endScale = opening ? Vector3.one : Vector3.one * 0.97f;
        if (opening && startAlpha <= 0.01f)
        {
            startScale = Vector3.one * 0.97f;
            card.localScale = startScale;
        }
        float elapsed = 0f;
        float duration = 0.2f / UIFeedbackService.AnimationSpeed;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            canvasGroup.alpha = Mathf.Lerp(startAlpha, endAlpha, progress);
            card.localScale = Vector3.LerpUnclamped(startScale, endScale, progress);
            yield return null;
        }
        canvasGroup.alpha = endAlpha;
        card.localScale = endScale;
        transitionRoutine = null;
    }

    private static Button CreateButton(
        Transform parent,
        string name,
        Vector2 position,
        FeedbackCue cue)
    {
        GameObject buttonObject = CreateUiObject(name, parent);
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(220f, 68f);
        Image image = buttonObject.AddComponent<Image>();
        image.color = cue == FeedbackCue.Back
            ? new Color(0.20f, 0.28f, 0.38f, 0.98f)
            : new Color(0.10f, 0.42f, 0.38f, 0.98f);
        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        buttonObject.AddComponent<GameFeedbackButton>().Configure(cue);
        CreateText(buttonObject.transform, "Label", Vector2.zero, new Vector2(205f, 55f), 20f, FontStyles.Bold);
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
        text.textWrappingMode = TextWrappingModes.Normal;
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
