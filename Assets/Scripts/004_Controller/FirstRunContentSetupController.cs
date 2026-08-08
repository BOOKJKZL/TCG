using System;
using System.Collections.Generic;
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
        private VisualElement recommendationRoot;
        private Label recommendationLabel;
        private Label recommendationName;
        private Label recommendationDetail;
        private readonly List<MobileActionControl> actions = new List<MobileActionControl>();
        private MobileActionControl recommended;
        private MobileActionControl manage;
        private MobileActionControl retry;
        private MobileActionControl later;
        private UiToolkitSafeAreaBinding safeArea;
        private CancellationTokenSource refreshCancellation;
        private int refreshGeneration;
        private bool destroyed;
        private bool shutdown;
        private CatalogStatusMode catalogStatusMode = CatalogStatusMode.Loading;
        private int catalogPackageCount;
        private bool catalogUsedCached;
        private ContentPackageCatalog remoteCatalog;
        private ContentPackageRecommendation recommendation;
        public static Action<string> SceneLoaderOverride { private get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            dismissedForSession = false;
            SceneLoaderOverride = null;
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
            recommendationRoot = Required<VisualElement>("setup-recommendation");
            recommendationLabel = Required<Label>("setup-recommendation-label");
            recommendationName = Required<Label>("setup-recommendation-name");
            recommendationDetail = Required<Label>("setup-recommendation-detail");
            BindAction("setup-language-en", () => SelectLanguage("en"));
            BindAction("setup-language-zh", () => SelectLanguage("zh"));
            BindAction("setup-language-ja", () => SelectLanguage("ja"));
            BindAction("setup-content-language-en", () => SelectContentLanguage("en"));
            BindAction("setup-content-language-zh", () => SelectContentLanguage("zh-cn"));
            BindAction("setup-content-language-ja", () => SelectContentLanguage("ja"));
            recommended = BindAction("setup-recommended", OpenRecommendedContent);
            manage = BindAction("setup-manage", OpenContentManagement);
            retry = BindAction("setup-retry", RefreshRemoteCatalog);
            later = BindAction("setup-later", DismissForSession);
            safeArea = UiToolkitSafeArea.Attach(root);
            ApplicationServices.Languages.UiLanguageChanged += OnUiLanguageChanged;
            ApplicationServices.Languages.ContentLanguageChanged += OnContentLanguageChanged;
            root.style.display = DisplayStyle.Flex;
            RefreshText();
            RefreshRemoteCatalog();
        }

        private void OnDestroy()
        {
            Shutdown();
        }

        private void Shutdown()
        {
            if (shutdown)
                return;
            shutdown = true;
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
            foreach (MobileActionControl action in actions)
                action.Dispose();
            actions.Clear();
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
            manage.SetLabel(zh ? "进入内容库" : ja ? "コンテンツライブラリを開く" : "Open Content Library");
            retry.SetLabel(zh ? "刷新目录" : ja ? "カタログを更新" : "Refresh catalog");
            later.SetLabel(zh ? "暂不设置" : ja ? "後で" : "Not now");
            recommendationLabel.text = zh ? "首次卡包" : ja ? "最初のカードパック" : "FIRST PACK";
            recommended.SetLabel(zh ? "查看这个卡包" : ja ? "このパックを確認" : "Review this pack");
            foreach (string id in new[] { "en", "zh", "ja" })
                Required<VisualElement>("setup-language-" + id).Q<Label>().EnableInClassList(
                    "is-selected",
                    string.Equals(language, id, StringComparison.OrdinalIgnoreCase));
            string contentLanguage = ApplicationServices.Languages.RequestedContentLanguageId;
            Required<VisualElement>("setup-content-language-en").Q<Label>().EnableInClassList(
                "is-selected", string.Equals(contentLanguage, "en", StringComparison.OrdinalIgnoreCase));
            Required<VisualElement>("setup-content-language-zh").Q<Label>().EnableInClassList(
                "is-selected", string.Equals(contentLanguage, "zh-cn", StringComparison.OrdinalIgnoreCase));
            Required<VisualElement>("setup-content-language-ja").Q<Label>().EnableInClassList(
                "is-selected", string.Equals(contentLanguage, "ja", StringComparison.OrdinalIgnoreCase));
            RefreshRecommendationText();
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
            ClearRecommendation();
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
                    remoteCatalog = result.Catalog;
                    catalogPackageCount = result.Catalog.Packages.Count;
                    catalogUsedCached = result.UsedCachedCatalog;
                    RefreshRecommendation();
                    SetCatalogStatus(CatalogStatusMode.Ready);
                }
                else
                {
                    ClearRecommendation();
                    SetCatalogStatus(CatalogStatusMode.Unavailable);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                Debug.LogWarning("First-run catalog refresh failed: " + exception.Message);
                if (!destroyed && generation == refreshGeneration && !token.IsCancellationRequested)
                {
                    ClearRecommendation();
                    SetCatalogStatus(CatalogStatusMode.Unavailable);
                }
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
            if (SceneLoaderOverride != null)
                SceneLoaderOverride("006_ContentScene");
            else
                SceneManager.LoadScene("006_ContentScene");
        }

        private void OpenRecommendedContent()
        {
            if (recommendation == null)
                return;
            ContentLaunchRequest.Recommend(recommendation.Entry.Package.PackageId);
            OpenContentManagement();
        }

        private void DismissForSession()
        {
            dismissedForSession = true;
            root.style.display = DisplayStyle.None;
            Shutdown();
            Destroy(gameObject);
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

        private string ReadyText(int count, bool cached)
        {
            if (count <= 0)
                return Localized(
                    "目录中没有可下载内容。请重试或进入内容库。",
                    "現在ダウンロード可能なパックはありません。後で再試行するか、コンテンツ一覧を開いてください。",
                    "No downloadable packs are currently available. Retry later or open the content library.");
            return Localized(
                $"目录更新完成：{count} 个内容包{(cached ? "（离线缓存）" : "")}。请选择后再确认下载。",
                $"カタログ準備完了：{count} パック{(cached ? "（オフラインキャッシュ）" : "")}。選択後にダウンロードを確認してください。",
                $"Catalog ready: {count} packs{(cached ? " (offline cache)" : "")}. Choose a pack before confirming a download.");
        }

        private void OnUiLanguageChanged(string _) => RefreshText();

        private void OnContentLanguageChanged(ContentLanguageSelection _)
        {
            RefreshRecommendation();
            RefreshText();
        }

        private void RefreshRecommendation()
        {
            recommendation = remoteCatalog == null
                ? null
                : ContentPackageRecommendations.FindSmallestPlayable(
                    remoteCatalog,
                    ApplicationServices.Languages.RequestedContentLanguageId);
            RefreshRecommendationText();
        }

        private void ClearRecommendation()
        {
            remoteCatalog = null;
            recommendation = null;
            RefreshRecommendationText();
        }

        private void RefreshRecommendationText()
        {
            if (recommendationRoot == null)
                return;
            bool visible = recommendation != null;
            recommendationRoot.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            recommended?.SetEnabled(visible);
            if (!visible)
                return;
            bool zh = ApplicationServices.Languages.UiLanguageId.StartsWith(
                "zh", StringComparison.OrdinalIgnoreCase);
            bool ja = ApplicationServices.Languages.UiLanguageId.StartsWith(
                "ja", StringComparison.OrdinalIgnoreCase);
            recommendationName.text = recommendation.Entry.Metadata.GetDisplayName(
                zh ? "zh-cn" : ja ? "ja" : "en",
                recommendation.Entry.Package.PackageId);
            recommendationDetail.text = string.Format(
                zh ? "下载 {0} · 安装 {1} · 需要 {2} 个内容包" :
                ja ? "ダウンロード {0} · インストール {1} · 必要 {2} パック" :
                "{0} download · {1} installed · {2} required pack(s)",
                FormatBytes(recommendation.Selection.DownloadBytes),
                FormatBytes(recommendation.Selection.InstalledBytes),
                recommendation.Selection.PackageIds.Count);
        }

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

        private MobileActionControl BindAction(string name, Action clicked)
        {
            var action = new MobileActionControl(
                Required<VisualElement>(name),
                clicked,
                playFeedback: true,
                showPressWhenUnavailable: false,
                fallbackLabelClass: "first-run-content__button-label");
            actions.Add(action);
            return action;
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
