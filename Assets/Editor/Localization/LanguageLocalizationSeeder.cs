using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Platform.Android;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

public static class LanguageLocalizationSeeder
{
    private const string CollectionName = "Card_UI";
    private const string AppDisplayNameKey = "app.display_name";
    private const string SettingsAssetPath = "Assets/Resources/Data/Localization/Localization Settings.asset";
    private const string JapaneseLocaleAssetPath =
        "Assets/Resources/Data/Localization/Japanese (ja).asset";
    private const string JapaneseTableAssetPath =
        "Assets/Resources/Data/Localization/Card_UI_ja.asset";

    [MenuItem("Tools/Universal Gacha/Seed Language Configuration")]
    public static void Seed()
    {
        EnsureLocalizationSettings();

        StringTableCollection collection = LocalizationEditorSettings.GetStringTableCollection(CollectionName);
        if (collection == null)
            throw new InvalidOperationException($"Localization collection '{CollectionName}' was not found.");

        StringTable english = collection.GetTable("en") as StringTable;
        StringTable chinese = collection.GetTable("zh") as StringTable;
        if (english == null || chinese == null)
            throw new InvalidOperationException("English and Simplified Chinese string tables are required.");

        Locale japaneseLocale = EnsureJapaneseLocale();
        StringTable japanese = collection.GetTable("ja") as StringTable ??
                               collection.AddNewTable(
                                   japaneseLocale.Identifier,
                                   JapaneseTableAssetPath) as StringTable;
        if (japanese == null)
            throw new InvalidOperationException("The Japanese string table could not be created.");

        Apply(english, new Dictionary<string, string>
        {
            [AppDisplayNameKey] = "Universal Gacha Simulator",
            ["settings.language.title"] = "Language",
            ["settings.language.ui"] = "Interface language",
            ["settings.language.content"] = "Card content language",
            ["settings.language.fallback"] = "Requested {0}; using {1}.",
            ["settings.language.only_installed"] = "Only one card language is installed.",
            ["language.en"] = "English",
            ["language.zh"] = "Simplified Chinese",
            ["language.ja"] = "Japanese",
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
            ["content.catalog.loading"] = "Checking available content...",
            ["content.catalog.loaded"] = "{0} content packs available.",
            ["content.catalog.empty"] = "No downloadable content is listed in this catalog.",
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
            ["content.status.queued"] = "Queued for download",
            ["common.action.retry"] = "Retry",
            ["common.action.manage_content"] = "Manage content",
            ["common.status.loading"] = "Loading…",
            ["common.action.main_menu"] = "Main menu",
            ["common.action.close"] = "Close",
            ["common.action.cancel"] = "Cancel",
            ["player_error.not_installed.title"] = "Content not installed",
            ["player_error.not_installed.body"] = "Choose a content pack to continue.",
            ["player_error.offline.title"] = "You're offline",
            ["player_error.offline.body"] = "Connect and retry, or continue with downloaded content.",
            ["player_error.catalog_corrupt.title"] = "Content list needs attention",
            ["player_error.catalog_corrupt.body"] = "Retry. If this continues, manage your downloaded content.",
            ["player_error.verification_failed.title"] = "Content verification failed",
            ["player_error.verification_failed.body"] = "This content was not installed. Download it again.",
            ["player_error.insufficient_space.title"] = "Not enough storage",
            ["player_error.insufficient_space.body"] = "Free some space or remove downloaded content, then retry.",
            ["player_error.service_unavailable.title"] = "Service unavailable",
            ["player_error.service_unavailable.body"] = "Please retry in a moment.",
            ["player_error.unexpected.title"] = "Couldn't continue",
            ["player_error.unexpected.body"] = "Retry or return to the main menu.",
            ["collection.status.no_content"] = "No card sets are installed yet. Open Content Library to choose what to download.",
            ["content.filter.empty"] = "No content packs match the current filters.",
            ["common.action.clear"] = "Clear",
            ["common.badge.new"] = "NEW",
            ["card_image.error.invalid_path"] = "Invalid image path",
            ["card_image.error.not_installed"] = "Image not installed",
            ["card_image.error.verification_failed"] = "Image verification failed",
            ["card_image.error.loading_failed"] = "Image loading failed",
            ["collection.title"] = "Card Collection",
            ["collection.subtitle"] = "Browse installed sets, search your cards, and track new pulls",
            ["collection.action.all_sets"] = "All sets",
            ["collection.status.unavailable"] = "Collection unavailable: {0}",
            ["collection.set.metadata"] = "{0} · {1}/{2} collected · {3} new · {4}",
            ["collection.filter.empty"] = "No cards match these filters.",
            ["collection.filter.all_rarities"] = "All rarities",
            ["collection.filter.search"] = "Search name or number",
            ["collection.filter.rarity"] = "Rarity",
            ["collection.filter.owned_on"] = "Owned: ON",
            ["collection.filter.owned_off"] = "Owned: OFF",
            ["collection.filter.new_on"] = "New: ON",
            ["collection.filter.new_off"] = "New: OFF",
            ["collection.status.seen_save_failed"] = "Couldn't save the viewed-card status. The NEW badge was kept.",
            ["collection.summary.all"] = "{0} installed sets · {1}/{2} collected · {3} new",
            ["collection.summary.filtered"] = "{0} shown · {1}/{2} collected · {3} new",
            ["collection.owned"] = "Owned ×{0}",
            ["collection.unowned"] = "Not owned",
            ["gacha.status.unavailable"] = "Pack opening unavailable: {0}",
            ["gacha.pack.hint"] = "Tap to tear this simulated pack",
            ["gacha.status.open_failed"] = "Could not open this pack: {0}",
            ["gacha.badge.owned"] = "OWNED",
            ["gacha.reveal.progress"] = "Card {0} of {1}",
            ["gacha.action.view_results"] = "View results",
            ["gacha.action.reveal_next"] = "Reveal next",
            ["gacha.status.no_products"] = "No products are installed for this content language.",
            ["gacha.rule.verified"] = "VERIFIED RULES",
            ["gacha.rule.sourced_simulation"] = "SOURCED SIMULATION",
            ["gacha.rule.simulation"] = "SIMULATION",
            ["gacha.rule.simulation_notice"] = "Equal odds per installed printing. This is not historical pack collation.",
            ["gacha.rule.evidence.checked"] = "{0} · Confidence: {1} · checked {2}",
            ["gacha.rule.evidence.unverified"] = "Region and historical collation are unverified.",
            ["gacha.rule.confidence.unverified"] = "Unverified",
            ["gacha.rule.confidence.corroborated"] = "Corroborated",
            ["gacha.rule.confidence.authoritative"] = "Authoritative",
            ["gacha.action.rule_source_number"] = "Source {0}: {1}",
            ["gacha.reveal.ready"] = "Cards are ready",
            ["gacha.reveal.one_at_time"] = "Reveal them one at a time",
            ["gacha.reveal.pending_progress"] = "0 of {0} cards",
            ["gacha.action.reveal_first"] = "Reveal first card",
            ["gacha.action.reveal_all"] = "Reveal all",
            ["gacha.summary.title"] = "Pack complete",
            ["gacha.summary.metadata"] = "{0} cards · {1} new · Pack #{2}",
            ["gacha.title"] = "Open a Pack",
            ["gacha.subtitle"] = "Choose installed content, inspect the rule, then reveal every card",
            ["gacha.action.prepare"] = "Prepare pack",
            ["gacha.confirm.title"] = "Confirm pack opening",
            ["gacha.confirm.body"] = "{0} × {1}\nCard language: {2}\nRule: {3}\nYour collection updates when you tear the pack.",
            ["gacha.action.confirm_open"] = "Open now",
            ["gacha.action.rule_source"] = "Rule source",
            ["gacha.action.tear"] = "Tear pack",
            ["gacha.action.all_products"] = "All products",
            ["gacha.action.open_another"] = "Open another",
            ["gacha.action.choose_another"] = "Choose another",
            ["gacha.odds.heading"] = "Average chance per card slot",
            ["gacha.product.metadata"] = "{0} · {1} printings · {2}",
            ["gacha.reveal.metadata"] = "#{0} · {1} · {2} · Owned {3}",
            ["main_menu.action.gacha"] = "Gacha",
            ["main_menu.action.collection"] = "Collection",
            ["main_menu.action.content"] = "Content",
            ["main_menu.action.settings"] = "Settings",
            ["home.top.title"] = "Trainer Home",
            ["home.top.subtitle"] = "Offline-first card archive",
            ["home.kicker"] = "TRAINER HUB",
            ["home.title"] = "Your card archive, one pack at a time",
            ["home.body"] = "Open simulated packs, review your collection, and install only the card languages you want.",
            ["home.section.destinations"] = "Choose your next stop",
            ["home.feature.gacha"] = "Choose an installed product and reveal every card.",
            ["home.feature.collection"] = "Browse owned cards, new pulls, and set completion.",
            ["home.feature.content"] = "Install, update, repair, or remove downloadable card data.",
            ["home.feature.settings"] = "Change language, feedback, save recovery, and account options.",
            ["home.nav.home"] = "Home",
            ["settings.title"] = "Settings"
        });

        Apply(chinese, new Dictionary<string, string>
        {
            [AppDisplayNameKey] = "万能抽卡模拟器",
            ["settings.language.title"] = "语言设置",
            ["settings.language.ui"] = "界面语言",
            ["settings.language.content"] = "卡牌内容语言",
            ["settings.language.fallback"] = "未安装 {0} 内容，当前使用 {1}。",
            ["settings.language.only_installed"] = "目前只安装了一种卡牌内容语言。",
            ["language.en"] = "英语",
            ["language.zh"] = "简体中文",
            ["language.ja"] = "日语",
            ["content.title"] = "内容库",
            ["content.subtitle"] = "安装、更新、修复或删除内容包，不会丢失收藏进度",
            ["content.action.back"] = "主菜单",
            ["content.action.refresh"] = "刷新目录",
            ["content.action.install"] = "安装",
            ["content.action.update"] = "更新",
            ["content.action.repair"] = "修复",
            ["content.action.resume"] = "继续",
            ["content.action.retry"] = "重试",
            ["content.action.pause"] = "暂停",
            ["content.action.cancel"] = "取消",
            ["content.action.download"] = "下载",
            ["content.action.remove"] = "删除内容",
            ["content.action.confirm_remove"] = "确认删除",
            ["content.catalog.loading"] = "正在检查可用内容……",
            ["content.catalog.loaded"] = "可下载 {0} 个内容包。",
            ["content.catalog.empty"] = "此目录没有可下载内容。",
            ["content.catalog.unavailable"] = "内容目录暂不可用：{0}",
            ["content.catalog.not_configured"] = "尚未配置远程内容。",
            ["content.catalog.cached"] = "离线模式 · 正在显示上次已验证 catalog 中的 {0} 个内容包。",
            ["content.catalog.cache_warning"] = "可用内容包：{0}，但无法更新离线 catalog 缓存。",
            ["content.package.metadata"] = "版本 {0} · {1}",
            ["content.status.ready"] = "可以安装",
            ["content.status.checking"] = "正在检查储存空间与已安装版本……",
            ["content.status.blocked"] = "暂时无法开始",
            ["content.status.insufficient_space"] = "储存空间不足",
            ["content.status.invalid_package"] = "内容包资料无效",
            ["content.status.storage_unavailable"] = "无法读取储存空间",
            ["content.status.downloading"] = "下载中",
            ["content.status.paused"] = "下载已暂停",
            ["content.status.installing"] = "正在验证并安装……",
            ["content.status.installed"] = "已安装",
            ["content.status.current"] = "已是最新版本",
            ["content.status.cancelled"] = "已取消",
            ["content.status.failed"] = "操作失败",
            ["content.status.warning"] = "已安装，但清理下载文件失败：{0}",
            ["content.status.update_available"] = "有可用更新",
            ["content.status.remove_confirm"] = "删除已下载卡牌？收藏进度会继续保留。",
            ["content.status.removing"] = "正在删除已下载内容……",
            ["content.status.removed"] = "内容已删除，收藏进度仍然保留。",
            ["content.status.remove_failed"] = "内容删除失败：{0}",
            ["content.status.remove_warning"] = "内容已删除，但清理有警告：{0}",
            ["content.progress"] = "{0}% · {1} / {2}",
            ["content.recommended.title"] = "已选择首个内容包",
            ["content.recommended.body"] = "已选中 {0}。请检查存储空间、网络状态，然后确认下载。",
            ["content.action.choose_another"] = "选择其他内容",
            ["content.confirm.download.title"] = "确认下载内容",
            ["content.confirm.download.body"] = "已选 {0} 个 · 需要 {1} 个内容包\n下载 {2} · 安装后 {3}\n{4}",
            ["content.confirm.download.package_body"] = "{0}\n需要 {1} 个内容包 · 下载 {2} · 安装后 {3}\n{4}",
            ["content.confirm.remove.title"] = "删除已下载内容？",
            ["content.confirm.remove.body"] = "删除此设备上的 {0}？收藏进度仍会保留。",
            ["content.action.confirm_download"] = "下载",
            ["content.action.cancel_confirmation"] = "取消",
            ["content.status.queued"] = "已加入下载队列",
            ["common.action.retry"] = "重试",
            ["common.action.manage_content"] = "管理内容",
            ["common.status.loading"] = "加载中……",
            ["common.action.main_menu"] = "主菜单",
            ["common.action.close"] = "关闭",
            ["common.action.cancel"] = "取消",
            ["player_error.not_installed.title"] = "内容尚未安装",
            ["player_error.not_installed.body"] = "请进入内容库选择下载内容。",
            ["player_error.offline.title"] = "当前离线",
            ["player_error.offline.body"] = "请连接网络后重试，或使用已下载内容。",
            ["player_error.catalog_corrupt.title"] = "内容目录无法使用",
            ["player_error.catalog_corrupt.body"] = "请重试；请管理已下载内容。",
            ["player_error.verification_failed.title"] = "内容验证失败",
            ["player_error.verification_failed.body"] = "此内容未安装。请重新下载。",
            ["player_error.insufficient_space.title"] = "存储空间不足",
            ["player_error.insufficient_space.body"] = "请删除已下载内容后重试。",
            ["player_error.service_unavailable.title"] = "当前无法使用",
            ["player_error.service_unavailable.body"] = "请重试。",
            ["player_error.unexpected.title"] = "暂时无法继续",
            ["player_error.unexpected.body"] = "请重试或返回主菜单。",
            ["collection.status.no_content"] = "尚未安装卡牌系列。请打开内容库选择要下载的内容。",
            ["content.filter.empty"] = "没有符合当前筛选条件的内容包。",
            ["common.action.clear"] = "清除筛选",
            ["common.badge.new"] = "新卡",
            ["card_image.error.invalid_path"] = "图片路径无效",
            ["card_image.error.not_installed"] = "卡图尚未安装",
            ["card_image.error.verification_failed"] = "卡图校验失败",
            ["card_image.error.loading_failed"] = "卡图加载失败",
            ["collection.title"] = "卡牌收藏",
            ["collection.subtitle"] = "浏览已安装系列、搜索收藏并查看新获得卡牌",
            ["collection.action.all_sets"] = "全部系列",
            ["collection.status.unavailable"] = "收藏浏览暂不可用：{0}",
            ["collection.set.metadata"] = "{0} 年 · 已收藏 {1}/{2} · {3} 张新卡 · {4}",
            ["collection.filter.empty"] = "没有符合当前筛选条件的卡牌。",
            ["collection.filter.all_rarities"] = "全部稀有度",
            ["collection.filter.search"] = "搜索名称或卡号",
            ["collection.filter.rarity"] = "稀有度",
            ["collection.filter.owned_on"] = "仅拥有：开",
            ["collection.filter.owned_off"] = "仅拥有：关",
            ["collection.filter.new_on"] = "仅新卡：开",
            ["collection.filter.new_off"] = "仅新卡：关",
            ["collection.status.seen_save_failed"] = "无法保存已查看状态，NEW 标记已保留。",
            ["collection.summary.all"] = "已安装 {0} 个系列 · 已收藏 {1}/{2} · {3} 张新卡",
            ["collection.summary.filtered"] = "显示 {0} 张 · 已收藏 {1}/{2} · {3} 张新卡",
            ["collection.owned"] = "已拥有 ×{0}",
            ["collection.unowned"] = "尚未拥有",
            ["gacha.status.unavailable"] = "开包功能暂不可用：{0}",
            ["gacha.pack.hint"] = "点击撕开这个模拟卡包",
            ["gacha.status.open_failed"] = "无法开启这个卡包：{0}",
            ["gacha.badge.owned"] = "已拥有",
            ["gacha.reveal.progress"] = "第 {0} / {1} 张",
            ["gacha.action.view_results"] = "查看结果",
            ["gacha.action.reveal_next"] = "翻开下一张",
            ["gacha.status.no_products"] = "当前内容语言没有已安装卡包。",
            ["gacha.rule.verified"] = "已验证规则",
            ["gacha.rule.sourced_simulation"] = "来源模拟规则",
            ["gacha.rule.simulation"] = "模拟规则",
            ["gacha.rule.simulation_notice"] = "每个已安装印刷版本等概率；这不代表历史真实卡包配列。",
            ["gacha.rule.evidence.checked"] = "{0} · 可信度：{1} · 核验于 {2}",
            ["gacha.rule.evidence.unverified"] = "地区与历史配列均未核验。",
            ["gacha.rule.confidence.unverified"] = "未核验",
            ["gacha.rule.confidence.corroborated"] = "已佐证",
            ["gacha.rule.confidence.authoritative"] = "权威来源",
            ["gacha.action.rule_source_number"] = "来源 {0}：{1}",
            ["gacha.reveal.ready"] = "卡牌已经准备好",
            ["gacha.reveal.one_at_time"] = "逐张翻开查看结果",
            ["gacha.reveal.pending_progress"] = "第 0 / {0} 张",
            ["gacha.action.reveal_first"] = "翻开第一张",
            ["gacha.action.reveal_all"] = "查看全部",
            ["gacha.summary.title"] = "开包完成",
            ["gacha.summary.metadata"] = "{0} 张卡牌 · {1} 张新卡 · 第 {2} 包",
            ["gacha.title"] = "开启卡包",
            ["gacha.subtitle"] = "选择已安装内容、确认规则，然后逐张翻开卡牌",
            ["gacha.action.prepare"] = "准备卡包",
            ["gacha.confirm.title"] = "确认开包",
            ["gacha.confirm.body"] = "{0} × {1}\n卡牌语言：{2}\n规则：{3}\n撕开卡包时会更新收藏进度。",
            ["gacha.action.confirm_open"] = "确认开包",
            ["gacha.action.rule_source"] = "规则来源",
            ["gacha.action.tear"] = "撕开卡包",
            ["gacha.action.all_products"] = "全部卡包",
            ["gacha.action.open_another"] = "再开一包",
            ["gacha.action.choose_another"] = "选择其他卡包",
            ["gacha.odds.heading"] = "每个卡位的平均概率",
            ["gacha.product.metadata"] = "{0} 年 · {1} 个印刷版本 · {2}",
            ["gacha.reveal.metadata"] = "#{0} · {1} · {2} · 已拥有 {3}",
            ["main_menu.action.gacha"] = "抽卡",
            ["main_menu.action.collection"] = "收藏",
            ["main_menu.action.content"] = "内容",
            ["main_menu.action.settings"] = "设置",
            ["home.top.title"] = "主菜单",
            ["home.top.subtitle"] = "离线卡牌收藏",
            ["home.kicker"] = "卡牌收藏",
            ["home.title"] = "一包一包，查看卡牌收藏",
            ["home.body"] = "开启模拟卡包、查看收藏，并只安装需要的卡牌语言。",
            ["home.section.destinations"] = "选择内容",
            ["home.feature.gacha"] = "选择已安装的卡包，并逐张翻开所有卡牌。",
            ["home.feature.collection"] = "浏览已拥有卡牌、新抽到的卡牌、系列完成度。",
            ["home.feature.content"] = "安装、更新、修复或移除可下载的卡牌资料。",
            ["home.feature.settings"] = "更改语言、设置。",
            ["home.nav.home"] = "主菜单",
            ["settings.title"] = "设置"
        });

        ValidateExactCoverage(collection.SharedData, JapaneseCardUiLocalization.Values);
        Apply(japanese, JapaneseCardUiLocalization.Values);

        EditorUtility.SetDirty(collection.SharedData);
        EditorUtility.SetDirty(english);
        EditorUtility.SetDirty(chinese);
        EditorUtility.SetDirty(japanese);
        EnsureAndroidAppInfo(collection);
        AssetDatabase.SaveAssets();
        Debug.Log("Language settings and localized entries are up to date.");
    }

    private static void EnsureLocalizationSettings()
    {
        LocalizationSettings settings = AssetDatabase.LoadAssetAtPath<LocalizationSettings>(SettingsAssetPath);
        if (settings == null)
        {
            settings = ScriptableObject.CreateInstance<LocalizationSettings>();
            settings.name = "Universal Gacha Localization Settings";
            AssetDatabase.CreateAsset(settings, SettingsAssetPath);
        }

        if (LocalizationEditorSettings.ActiveLocalizationSettings != settings)
            LocalizationEditorSettings.ActiveLocalizationSettings = settings;

        EditorUtility.SetDirty(settings);
    }

    private static Locale EnsureJapaneseLocale()
    {
        Locale locale = LocalizationEditorSettings.GetLocale("ja");
        if (locale != null)
            return locale;

        locale = AssetDatabase.LoadAssetAtPath<Locale>(JapaneseLocaleAssetPath);
        if (locale == null)
        {
            locale = Locale.CreateLocale("ja");
            AssetDatabase.CreateAsset(locale, JapaneseLocaleAssetPath);
        }

        LocalizationEditorSettings.AddLocale(locale);
        EditorUtility.SetDirty(locale);
        return locale;
    }

    private static void EnsureAndroidAppInfo(StringTableCollection collection)
    {
        LocalizationSettings settings = LocalizationEditorSettings.ActiveLocalizationSettings;
        SharedTableData.SharedTableEntry appNameEntry = collection.SharedData.GetEntry(AppDisplayNameKey);
        if (settings == null || appNameEntry == null)
            throw new InvalidOperationException("Localization settings and the app display-name entry are required.");

        AppInfo appInfo = LocalizationSettings.Metadata.GetMetadata<AppInfo>();
        if (appInfo == null)
        {
            appInfo = new AppInfo();
            LocalizationSettings.Metadata.AddMetadata(appInfo);
        }

        appInfo.DisplayName = new LocalizedString(
            collection.SharedData.TableCollectionNameGuid,
            appNameEntry.Id);
        EditorUtility.SetDirty(settings);
    }

    private static void Apply(StringTable table, IReadOnlyDictionary<string, string> values)
    {
        foreach (KeyValuePair<string, string> pair in values)
        {
            StringTableEntry entry = table.GetEntry(pair.Key) ?? table.AddEntry(pair.Key, pair.Value);
            entry.Value = pair.Value;
        }
    }

    private static void ValidateExactCoverage(
        SharedTableData sharedData,
        IReadOnlyDictionary<string, string> values)
    {
        if (values.Count != sharedData.Entries.Count)
            throw new InvalidOperationException(
                $"Japanese Card_UI coverage is {values.Count}/{sharedData.Entries.Count}. " +
                "Add every new shared key before reseeding localization.");

        foreach (SharedTableData.SharedTableEntry entry in sharedData.Entries)
        {
            if (!values.TryGetValue(entry.Key, out string value) || string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException(
                    $"Japanese Card_UI translation is missing for '{entry.Key}'.");
        }
    }
}
