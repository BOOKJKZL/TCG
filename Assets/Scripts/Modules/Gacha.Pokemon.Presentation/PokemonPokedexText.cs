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
                ["types"] = Text("Types: {0}", "属性：{0}", "タイプ: {0}"),
                ["region"] = Text("Region: {0}", "地区：{0}", "地方: {0}")
            };

        private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> TaxonomyValues =
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["type.normal"] = Text("Normal", "一般", "ノーマル"),
                ["type.fire"] = Text("Fire", "火", "ほのお"),
                ["type.water"] = Text("Water", "水", "みず"),
                ["type.electric"] = Text("Electric", "电", "でんき"),
                ["type.grass"] = Text("Grass", "草", "くさ"),
                ["type.ice"] = Text("Ice", "冰", "こおり"),
                ["type.fighting"] = Text("Fighting", "格斗", "かくとう"),
                ["type.poison"] = Text("Poison", "毒", "どく"),
                ["type.ground"] = Text("Ground", "地面", "じめん"),
                ["type.flying"] = Text("Flying", "飞行", "ひこう"),
                ["type.psychic"] = Text("Psychic", "超能力", "エスパー"),
                ["type.bug"] = Text("Bug", "虫", "むし"),
                ["type.rock"] = Text("Rock", "岩石", "いわ"),
                ["type.ghost"] = Text("Ghost", "幽灵", "ゴースト"),
                ["type.dragon"] = Text("Dragon", "龙", "ドラゴン"),
                ["type.dark"] = Text("Dark", "恶", "あく"),
                ["type.steel"] = Text("Steel", "钢", "はがね"),
                ["type.fairy"] = Text("Fairy", "妖精", "フェアリー"),
                ["region.alola"] = Text("Alola", "阿罗拉", "アローラ"),
                ["region.galar"] = Text("Galar", "伽勒尔", "ガラル"),
                ["region.hisui"] = Text("Hisui", "洗翠", "ヒスイ"),
                ["region.paldea"] = Text("Paldea", "帕底亚", "パルデア"),
                ["form.default"] = Text("Default form", "默认形态", "通常のすがた"),
                ["form.alternate"] = Text("Alternate form", "其他形态", "別のすがた"),
                ["form.battle-only"] = Text("Battle-only form", "战斗限定形态", "バトル限定"),
                ["form.cosmetic"] = Text("Cosmetic form", "外观差异", "見た目違い"),
                ["form.gender-difference"] = Text("Gender difference", "性别差异", "性別違い"),
                ["form.gigantamax"] = Text("Gigantamax", "超极巨化", "キョダイマックス"),
                ["form.mega"] = Text("Mega Evolution", "超级进化", "メガシンカ"),
                ["form.regional"] = Text("Regional form", "地区形态", "リージョンフォーム")
            };

        private static readonly IReadOnlyDictionary<string, string> UnknownTaxonomy =
            Text("Other", "其他", "その他");

        public static string Get(string key, string languageId)
        {
            if (!Values.TryGetValue(key ?? string.Empty, out IReadOnlyDictionary<string, string> localized))
                return key ?? string.Empty;
            string language = NormalizeLanguage(languageId);
            return localized[language];
        }

        public static string Format(string key, string languageId, params object[] arguments) =>
            string.Format(CultureInfo.CurrentCulture, Get(key, languageId), arguments ?? Array.Empty<object>());

        public static string TypeName(string id, string languageId) =>
            GetTaxonomy("type", id, languageId);

        public static string RegionName(string id, string languageId) =>
            GetTaxonomy("region", id, languageId);

        public static string FormKindName(string id, string languageId) =>
            GetTaxonomy("form", id, languageId);

        private static string GetTaxonomy(string category, string id, string languageId)
        {
            string language = NormalizeLanguage(languageId);
            string normalizedId = id?.Trim().ToLowerInvariant() ?? string.Empty;
            if (TaxonomyValues.TryGetValue(category + "." + normalizedId,
                    out IReadOnlyDictionary<string, string> localized))
                return localized[language];
            return UnknownTaxonomy[language];
        }

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
