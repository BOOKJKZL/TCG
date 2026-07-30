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
                ["title"] = Text("Pokédex", "宝可梦图鉴"),
                ["subtitle"] = Text("Browse by debut generation and National Pokédex number", "依照首次登场世代与全国图鉴编号浏览"),
                ["open"] = Text("Pokédex", "图鉴"),
                ["close"] = Text("Back to collection", "返回收藏"),
                ["back"] = Text("Back", "返回"),
                ["generation"] = Text("Generation", "世代"),
                ["search"] = Text("Search name or National No.", "搜索名称或全国编号"),
                ["empty"] = Text("No Pokémon match this search.", "没有符合搜索条件的宝可梦。"),
                ["count"] = Text("{0} Pokémon · National No. order", "{0} 只宝可梦 · 全国编号顺序"),
                ["new_forms"] = Text("New forms introduced in this generation ({0})", "本世代新增形态（{0}）"),
                ["number"] = Text("National No. #{0:000}", "全国图鉴 #{0:000}"),
                ["debut"] = Text("First appeared in {0}", "首次登场于{0}"),
                ["forms"] = Text("Related forms", "相关形态"),
                ["cards"] = Text("Related cards", "关联卡牌"),
                ["card_count"] = Text("{0} cards for this species · {1} confirmed for this form", "同种宝可梦 {0} 张卡 · 当前形态已确认 {1} 张"),
                ["card_scope_form"] = Text("Current form", "当前形态"),
                ["card_scope_species"] = Text("All same species", "全部同种"),
                ["card_search"] = Text("Search related cards", "搜索关联卡牌"),
                ["card_sort"] = Text("Sort", "排序"),
                ["card_sort_set"] = Text("Set / card number", "卡包 / 卡号"),
                ["card_sort_name"] = Text("Card name", "卡牌名称"),
                ["card_empty_form"] = Text("No cards are confirmed for this exact form. Try all same-species cards.", "尚无卡牌确认属于这个具体形态，可切换到全部同种卡牌。"),
                ["card_empty_species"] = Text("No related cards match this search.", "没有符合搜索条件的关联卡牌。"),
                ["card_installed"] = Text("Installed · tap for details", "已安装 · 点击查看详情"),
                ["card_not_installed"] = Text("Card image package not installed", "卡图资源包尚未安装"),
                ["manage_downloads"] = Text("Manage downloads", "管理下载"),
                ["art_pending"] = Text("Artwork is not installed", "图鉴图片尚未安装"),
                ["art_hint"] = Text("Download the generation image package to view it offline", "下载该世代图片包后即可离线查看"),
                ["unavailable"] = Text("Pokédex data is unavailable: {0}", "图鉴资料无法使用：{0}"),
                ["types"] = Text("Types: {0}", "属性：{0}"),
                ["region"] = Text("Region: {0}", "地区：{0}")
            };

        public static string Get(string key, string languageId)
        {
            if (!Values.TryGetValue(key ?? string.Empty, out IReadOnlyDictionary<string, string> localized))
                return key ?? string.Empty;
            string language = string.Equals(languageId, "zh", StringComparison.OrdinalIgnoreCase) ? "zh" : "en";
            return localized[language];
        }

        public static string Format(string key, string languageId, params object[] arguments) =>
            string.Format(CultureInfo.CurrentCulture, Get(key, languageId), arguments ?? Array.Empty<object>());

        private static IReadOnlyDictionary<string, string> Text(string english, string chinese) =>
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = english,
                ["zh"] = chinese
            };
    }
}
