using System;
using System.Collections.Generic;
using System.Globalization;

namespace Gacha.Pokemon.Presentation
{
    public static class PokemonPokedexText
    {
        private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Values =
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["title"] = Text("Pokédex", "宝可梦图鉴", "ポケモン図鑑"),
                ["subtitle"] = Text("Browse by debut generation and National Pokédex number", "依照首次登场世代与全国图鉴编号浏览", "初登場の世代と全国図鑑番号で閲覧"),
                ["open"] = Text("Pokédex", "图鉴", "図鑑"),
                ["close"] = Text("Back to collection", "返回收藏", "コレクションに戻る"),
                ["back"] = Text("Back", "返回", "戻る"),
                ["generation"] = Text("Generation", "世代", "世代"),
                ["search"] = Text("Search name or National No.", "搜索名称或全国编号", "名前または全国図鑑番号を検索"),
                ["empty"] = Text("No Pokémon match this search.", "没有符合搜索条件的宝可梦。", "条件に一致するポケモンがいません。"),
                ["count"] = Text("{0} Pokémon · National No. order", "{0} 只宝可梦 · 全国编号顺序", "{0} 匹 · 全国図鑑番号順"),
                ["new_forms"] = Text("New forms introduced in this generation ({0})", "本世代新增形态（{0}）", "この世代で登場した新しいすがた（{0}）"),
                ["number"] = Text("National No. #{0:000}", "全国图鉴 #{0:000}", "全国図鑑 No. {0:000}"),
                ["debut"] = Text("First appeared in {0}", "首次登场于{0}", "初登場: {0}"),
                ["forms"] = Text("Related forms", "相关形态", "関連するすがた"),
                ["cards"] = Text("Related cards", "关联卡牌", "関連カード"),
                ["card_count"] = Text("{0} cards for this species · {1} confirmed for this form", "同种宝可梦 {0} 张卡 · 当前形态已确认 {1} 张", "同じポケモンのカード {0} 枚 · このすがたと確認済み {1} 枚"),
                ["card_scope_form"] = Text("Current form", "当前形态", "現在のすがた"),
                ["card_scope_species"] = Text("All same species", "全部同种", "同じポケモンすべて"),
                ["card_search"] = Text("Search related cards", "搜索关联卡牌", "関連カードを検索"),
                ["card_sort"] = Text("Sort", "排序", "並び順"),
                ["card_sort_set"] = Text("Set / card number", "卡包 / 卡号", "セット / カード番号"),
                ["card_sort_name"] = Text("Card name", "卡牌名称", "カード名"),
                ["card_empty_form"] = Text("No cards are confirmed for this exact form. Try all same-species cards.", "尚无卡牌确认属于这个具体形态，可切换到全部同种卡牌。", "このすがたと確認済みのカードはありません。同じポケモンすべてをお試しください。"),
                ["card_empty_species"] = Text("No related cards match this search.", "没有符合搜索条件的关联卡牌。", "条件に一致する関連カードがありません。"),
                ["card_installed"] = Text("Installed · tap for details", "已安装 · 点击查看详情", "インストール済み · タップで詳細"),
                ["card_not_installed"] = Text("Card image package not installed", "卡图资源包尚未安装", "カード画像パッケージが未インストールです"),
                ["manage_downloads"] = Text("Manage downloads", "管理下载", "ダウンロード管理"),
                ["content_missing"] = Text("Pokédex content is not installed yet.", "图鉴资料尚未安装。", "図鑑コンテンツがインストールされていません。"),
                ["art_pending"] = Text("Artwork is not installed", "图鉴图片尚未安装", "図鑑画像が未インストールです"),
                ["art_hint"] = Text("Download the generation image package to view it offline", "下载该世代图片包后即可离线查看", "世代別画像パッケージをダウンロードするとオフラインで表示できます"),
                ["unavailable"] = Text("Pokédex data is unavailable: {0}", "图鉴资料无法使用：{0}", "図鑑データを利用できません: {0}"),
                ["types"] = Text("Types: {0}", "属性：{0}", "タイプ: {0}"),
                ["region"] = Text("Region: {0}", "地区：{0}", "地方: {0}")
            };

        public static string Get(string key, string languageId)
        {
            if (!Values.TryGetValue(key ?? string.Empty, out IReadOnlyDictionary<string, string> localized))
                return key ?? string.Empty;
            string language = NormalizeLanguage(languageId);
            return localized[language];
        }

        public static string Format(string key, string languageId, params object[] arguments) =>
            string.Format(CultureInfo.CurrentCulture, Get(key, languageId), arguments ?? Array.Empty<object>());

        private static string NormalizeLanguage(string languageId)
        {
            string normalized = languageId?.Trim().Replace('_', '-').ToLowerInvariant() ?? string.Empty;
            if (normalized == "zh" || normalized.StartsWith("zh-", StringComparison.Ordinal))
                return "zh";
            if (normalized == "ja" || normalized.StartsWith("ja-", StringComparison.Ordinal))
                return "ja";
            return "en";
        }

        private static IReadOnlyDictionary<string, string> Text(
            string english,
            string chinese,
            string japanese) =>
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = english,
                ["zh"] = chinese,
                ["ja"] = japanese
            };
    }
}
