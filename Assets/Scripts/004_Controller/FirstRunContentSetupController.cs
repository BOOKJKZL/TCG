using System;
using System.Threading;
using System.Threading.Tasks;
using Gacha.Application;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Gacha.Presentation
{
    public sealed class FirstRunContentSetupController : MonoBehaviour
    {
        private enum CatalogStatusMode
        {
            Loading,
            Unavailable,
            Ready
        }

        private static bool dismissedForSession;
        private UIDocument document;
        private VisualElement root;
        private Label kicker;
        private Label title;
        private Label body;
        private Label languageLabel;
        private Label contentLanguageLabel;
        private Label contentLanguageDetail;
        private Label storage;
        private Label catalogStatus;
        private Button manage;
        private Button retry;
        private Button later;
        private UiToolkitSafeAreaBinding safeArea;
        private CancellationTokenSource refreshCancellation;
        private int refreshGeneration;
        private bool destroyed;
        private CatalogStatusMode catalogStatusMode = CatalogStatusMode.Loading;
        private int catalogPackageCount;
        private bool catalogUsedCached;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            dismissedForSession = false;
        }

        private void Start()
        {
            if (!ApplicationServices.IsConfigured)
                GameApplicationBootstrap.EnsureConfigured();
            CatalogLoadResult installed = ApplicationServices.Catalog.EnsureLoaded();
            if (installed.HasInstalledContent || dismissedForSession)
                return;

            VisualTreeAsset view = Resources.Load<VisualTreeAsset>("UI/FirstRunContentSetup");
            PanelSettings panel = Resources.Load<PanelSettings>("UI/Collection Panel Settings");
            if (view == null || panel == null)
            {
                Debug.LogError("First-run content setup UI assets are missing.");
                return;
            }

            document = gameObject.AddComponent<UIDocument>();
            document.panelSettings = panel;
            document.sortingOrder = 100;
            document.visualTreeAsset = view;
            root = document.rootVisualElement.Q<VisualElement>("first-run-content");
            kicker = Required<Label>("setup-kicker");
            title = Required<Label>("setup-title");
            body = Required<Label>("setup-body");
            languageLabel = Required<Label>("setup-language-label");
            contentLanguageLabel = Required<Label>("setup-content-language-label");
            contentLanguageDetail = Required<Label>("setup-content-language-detail");
            storage = Required<Label>("setup-storage");
            catalogStatus = Required<Label>("setup-catalog");
            manage = Required<Button>("setup-manage");
            retry = Required<Button>("setup-retry");
            later = Required<Button>("setup-later");
            Required<Button>("setup-language-en").clicked += () => SelectLanguage("en");
            Required<Button>("setup-language-zh").clicked += () => SelectLanguage("zh");
            Required<Button>("setup-language-ja").clicked += () => SelectLanguage("ja");
            Required<Button>("setup-content-language-en").clicked += () => SelectContentLanguage("en");
            Required<Button>("setup-content-language-zh").clicked += () => SelectContentLanguage("zh-cn");
            Required<Button>("setup-content-language-ja").clicked += () => SelectContentLanguage("ja");
            manage.clicked += OpenContentManagement;
            retry.clicked += RefreshRemoteCatalog;
            later.clicked += DismissForSession;
            safeArea = UiToolkitSafeArea.Attach(root);
            ApplicationServices.Languages.UiLanguageChanged += OnUiLanguageChanged;
            ApplicationServices.Languages.ContentLanguageChanged += OnContentLanguageChanged;
            root.style.display = DisplayStyle.Flex;
            RefreshText();
            RefreshRemoteCatalog();
        }

        private void OnDestroy()
        {
            destroyed = true;
            refreshGeneration++;
            if (ApplicationServices.IsConfigured)
            {
                ApplicationServices.Languages.UiLanguageChanged -= OnUiLanguageChanged;
                ApplicationServices.Languages.ContentLanguageChanged -= OnContentLanguageChanged;
            }
            refreshCancellation?.Cancel();
            refreshCancellation?.Dispose();
            refreshCancellation = null;
            safeArea?.Dispose();
            safeArea = null;
        }

        private void SelectLanguage(string languageId)
        {
            ApplicationServices.Languages.SelectUiLanguage(languageId);
            RefreshText();
        }

        private void SelectContentLanguage(string languageId)
        {
            ApplicationServices.Languages.SelectContentLanguage(languageId, ApplicationServices.Catalog.Catalog);
            RefreshText();
        }

        private void RefreshText()
        {
            string language = ApplicationServices.Languages.UiLanguageId;
            bool zh = language.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
            bool ja = language.StartsWith("ja", StringComparison.OrdinalIgnoreCase);
            kicker.text = zh ? "首次内容设置" : ja ? "初回コンテンツ設定" : "FIRST CONTENT SETUP";
            title.text = zh ? "选择界面语言，再下载卡牌内容" :
                ja ? "表示言語を選び、必要なカードだけをダウンロード" :
                "Choose your language, then download only the card content you want";
            body.text = zh ? "当前没有已安装卡牌。刷新只会读取目录；ZIP 需要在内容库中确认后下载。" :
                ja ? "カードはまだインストールされていません。更新ではカタログだけを取得し、ZIP はコンテンツライブラリで確認するまでダウンロードしません。" :
                "No card packs are installed yet. Refreshing fetches only the small catalog; no ZIP downloads until you explicitly confirm one in the Content Library.";
            languageLabel.text = zh ? "界面语言" : ja ? "表示言語" : "Interface language";
            contentLanguageLabel.text = zh ? "卡牌语言" : ja ? "カード言語" : "Card language";
            contentLanguageDetail.text = zh ? "界面语言与卡牌语言可不同。" :
                ja ? "表示言語とカード言語を設定できます。" :
                "Interface and card languages are independent and can be changed later.";
            storage.text = StorageText(zh, ja);
            manage.text = zh ? "进入内容库" : ja ? "コンテンツライブラリを開く" : "Open Content Library";
            retry.text = zh ? "刷新目录" : ja ? "カタログを更新" : "Refresh catalog";
            later.text = zh ? "暂不设置" : ja ? "後で" : "Not now";
            foreach (string id in new[] { "en", "zh", "ja" })
                Required<Button>("setup-language-" + id).EnableInClassList(
                    "is-selected",
                    string.Equals(language, id, StringComparison.OrdinalIgnoreCase));
            string contentLanguage = ApplicationServices.Languages.RequestedContentLanguageId;
            Required<Button>("setup-content-language-en").EnableInClassList(
                "is-selected", string.Equals(contentLanguage, "en", StringComparison.OrdinalIgnoreCase));
            Required<Button>("setup-content-language-zh").EnableInClassList(
                "is-selected", string.Equals(contentLanguage, "zh-cn", StringComparison.OrdinalIgnoreCase));
            Required<Button>("setup-content-language-ja").EnableInClassList(
                "is-selected", string.Equals(contentLanguage, "ja", StringComparison.OrdinalIgnoreCase));
            RefreshCatalogStatusText();
        }

        private string StorageText(bool zh, bool ja)
        {
            try
            {
                long bytes = GameApplicationBootstrap.GetAvailableManagedContentBytes();
                string size = FormatBytes(bytes);
                return zh ? $"存储位置：内容存储 · 可用 {size}" :
                    ja ? $"保存先：アプリ管理ストレージ · 空き {size}" :
                    $"Storage: app-managed content folder · {size} available";
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Managed content capacity could not be read: " + exception.Message);
                return zh ? "存储位置：内容存储" :
                    ja ? "保存先：アプリ管理ストレージ" :
                    "Storage: app-managed content folder";
            }
        }

        private async void RefreshRemoteCatalog()
        {
            int generation = ++refreshGeneration;
            refreshCancellation?.Cancel();
            refreshCancellation?.Dispose();
            refreshCancellation = new CancellationTokenSource();
            CancellationToken token = refreshCancellation.Token;
            retry.SetEnabled(false);
            SetCatalogStatus(CatalogStatusMode.Loading);
            try
            {
                IContentPackageCatalogProvider provider = ApplicationServices.ContentPackageCatalogs;
                if (provider == null)
                {
                    SetCatalogStatus(CatalogStatusMode.Unavailable);
                    return;
                }

                ContentPackageCatalogLoadResult result = await provider.LoadAsync(token);
                if (destroyed || generation != refreshGeneration || token.IsCancellationRequested)
                    return;
                if (result.Succeeded)
                {
                    catalogPackageCount = result.Catalog.Packages.Count;
                    catalogUsedCached = result.UsedCachedCatalog;
                    SetCatalogStatus(CatalogStatusMode.Ready);
                }
                else
                {
                    SetCatalogStatus(CatalogStatusMode.Unavailable);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                Debug.LogWarning("First-run catalog refresh failed: " + exception.Message);
                if (!destroyed && generation == refreshGeneration && !token.IsCancellationRequested)
                    SetCatalogStatus(CatalogStatusMode.Unavailable);
            }
            finally
            {
                if (!destroyed && generation == refreshGeneration && !token.IsCancellationRequested && retry != null)
                    retry.SetEnabled(true);
            }
        }

        private void OpenContentManagement()
        {
            ContentReturnNavigation.RememberCurrentScene();
            SceneManager.LoadScene("006_ContentScene");
        }

        private void DismissForSession()
        {
            dismissedForSession = true;
            root.style.display = DisplayStyle.None;
            safeArea?.Suspend();
        }

        private void SetCatalogStatus(CatalogStatusMode mode)
        {
            catalogStatusMode = mode;
            RefreshCatalogStatusText();
        }

        private void RefreshCatalogStatusText()
        {
            if (catalogStatus == null)
                return;

            switch (catalogStatusMode)
            {
                case CatalogStatusMode.Ready:
                    catalogStatus.text = ReadyText(catalogPackageCount, catalogUsedCached);
                    break;
                case CatalogStatusMode.Unavailable:
                    catalogStatus.text = UnavailableText();
                    break;
                default:
                    catalogStatus.text = LoadingText();
                    break;
            }
        }

        private string LoadingText() => Localized(
            "正在刷新可下载目录…不会下载卡牌 ZIP。",
            "ダウンロード可能なカタログを更新中…カード ZIP は取得しません。",
            "Refreshing the available catalog… no card ZIPs are being downloaded.");

        private string UnavailableText() => Localized(
            "当前无法读取目录。可以离线进入主菜单或重试。",
            "カタログを取得できません。オフラインのまま続行し、後で再試行できます。",
            "The catalog is unavailable. You can continue offline and retry later.");

        private string ReadyText(int count, bool cached) => Localized(
            $"目录更新完成：{count} 个内容包{(cached ? "（离线缓存）" : "")}。请选择后再确认下载。",
            $"カタログ準備完了：{count} パック{(cached ? "（オフラインキャッシュ）" : "")}。選択後にダウンロードを確認してください。",
            $"Catalog ready: {count} packs{(cached ? " (offline cache)" : "")}. Choose a pack before confirming a download.");

        private void OnUiLanguageChanged(string _) => RefreshText();

        private void OnContentLanguageChanged(ContentLanguageSelection _) => RefreshText();

        private string Localized(string zh, string ja, string en)
        {
            string language = ApplicationServices.Languages.UiLanguageId;
            return language.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? zh :
                language.StartsWith("ja", StringComparison.OrdinalIgnoreCase) ? ja : en;
        }

        private T Required<T>(string name) where T : VisualElement
        {
            T value = document.rootVisualElement.Q<T>(name);
            return value ?? throw new InvalidOperationException("First-run setup is missing '" + name + "'.");
        }

        private static string FormatBytes(long bytes)
        {
            double value = Math.Max(0, bytes);
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            int unit = 0;
            while (value >= 1024d && unit < units.Length - 1)
            {
                value /= 1024d;
                unit++;
            }
            return value.ToString(value >= 100d || unit == 0 ? "0" : "0.0") + " " + units[unit];
        }
    }
}
