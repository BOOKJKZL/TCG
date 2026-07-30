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
                ["number"] = Text("National No. #{0:000}", "全国图鉴 #{0:000}"),
                ["debut"] = Text("First appeared in {0}", "首次登场于{0}"),
                ["forms"] = Text("Related forms", "相关形态"),
                ["cards"] = Text("Related cards", "关联卡牌"),
                ["card_count"] = Text("{0} cards for this species · {1} confirmed for this form", "同种宝可梦 {0} 张卡 · 当前形态已确认 {1} 张"),
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
