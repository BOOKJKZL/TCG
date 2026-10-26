using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Gacha.Presentation
{
    public sealed class MobileTypographyRule
    {
        public MobileTypographyRule(string className, float size, string styleSheetPath, float? compactSize = null)
        {
            ClassName = className;
            BaseSize = size;
            CompactSize = compactSize ?? size;
            StyleSheetPath = styleSheetPath;
        }
        public string ClassName { get; }
        public float BaseSize { get; }
        public float CompactSize { get; }
        public string StyleSheetPath { get; }
    }

    public sealed class MobileTypographyProfile
    {
        public MobileTypographyProfile(string name, params MobileTypographyRule[] rules)
        {
            Name = name;
            Rules = rules;
        }
        public string Name { get; }
        public IReadOnlyList<MobileTypographyRule> Rules { get; }
        public MobileTypographyRule Match(VisualElement element)
        {
            // More specific modifiers are deliberately declared last, just as in USS.
            for (int index = Rules.Count - 1; index >= 0; index--)
                if (element.ClassListContains(Rules[index].ClassName)) return Rules[index];
            return null;
        }
    }

    /// <summary>
    /// Explicit nominal pixel sizes at scale 1. Editor contracts compare every rule with
    /// its USS declaration so that code and authoring styles cannot silently drift.
    /// </summary>
    public static class MobileTypographyProfiles
    {
        private const string SharedStyles = "Assets/Resources/UI/MobileGameDesignSystem.uss";
        private const string PageStyles = "Assets/UI/Styles.uss";
        private static MobileTypographyRule R(string className, float size, string path = PageStyles, float? compact = null) =>
            new MobileTypographyRule(className, size, path, compact);

        public static readonly MobileTypographyProfile Shared = new MobileTypographyProfile("Shared",
            R("mobile-top-bar__title", 28, SharedStyles), R("mobile-top-bar__subtitle", 15, SharedStyles),
            R("mobile-action__label", 17, SharedStyles), R("mobile-action__loading", 17, SharedStyles),
            R("mobile-state-card__icon", 34, SharedStyles), R("mobile-state-card__title", 24, SharedStyles),
            R("mobile-state-card__body", 17, SharedStyles), R("mobile-progress__label", 16, SharedStyles),
            R("mobile-progress__value", 16, SharedStyles), R("mobile-toast__label", 16, SharedStyles),
            R("mobile-sheet__title", 25, SharedStyles), R("mobile-sheet__body", 17, SharedStyles));

        public static readonly MobileTypographyProfile Start = new MobileTypographyProfile("Start",
            R("start-title", 38, "Assets/Resources/UI/StartView.uss", 30),
            R("start-body", 20, "Assets/Resources/UI/StartView.uss"),
            R("start-status", 17, "Assets/Resources/UI/StartView.uss"));

        public static readonly MobileTypographyProfile Home = new MobileTypographyProfile("Home",
            R("home-hero__kicker", 15, "Assets/Resources/UI/HomeView.uss"),
            R("home-hero__title", 34, "Assets/Resources/UI/HomeView.uss", 28),
            R("home-hero__body", 18, "Assets/Resources/UI/HomeView.uss"),
            R("home-section__title", 23, "Assets/Resources/UI/HomeView.uss"),
            R("home-feature-card__number", 17, "Assets/Resources/UI/HomeView.uss"),
            R("home-feature-card__title", 22, "Assets/Resources/UI/HomeView.uss"),
            R("home-feature-card__body", 16, "Assets/Resources/UI/HomeView.uss"));

        public static readonly MobileTypographyProfile Settings = new MobileTypographyProfile("Settings",
            R("settings-card__title", 23, "Assets/Resources/UI/SettingsView.uss"),
            R("settings-card__description", 16, "Assets/Resources/UI/SettingsView.uss"),
            R("settings-card__summary", 16, "Assets/Resources/UI/SettingsView.uss"),
            R("settings-card__status", 16, "Assets/Resources/UI/SettingsView.uss"),
            R("settings-row__label", 17, "Assets/Resources/UI/SettingsView.uss"),
            R("settings-cloud-summary__title", 17, "Assets/Resources/UI/SettingsView.uss"));

        public static readonly MobileTypographyProfile FirstRun = new MobileTypographyProfile("FirstRun",
            R("first-run-content__kicker", 18, "Assets/Resources/UI/FirstRunContentSetup.uss"),
            R("first-run-content__label", 18, "Assets/Resources/UI/FirstRunContentSetup.uss"),
            R("first-run-content__title", 34, "Assets/Resources/UI/FirstRunContentSetup.uss"),
            R("first-run-content__body", 19, "Assets/Resources/UI/FirstRunContentSetup.uss"),
            R("first-run-content__detail", 19, "Assets/Resources/UI/FirstRunContentSetup.uss"),
            R("first-run-content__detail--compact", 17, "Assets/Resources/UI/FirstRunContentSetup.uss"),
            R("first-run-content__button-label", 18, "Assets/Resources/UI/FirstRunContentSetup.uss"),
            R("first-run-content__recommendation-name", 23, "Assets/Resources/UI/FirstRunContentSetup.uss"));

        public static readonly MobileTypographyProfile Collection = new MobileTypographyProfile("Collection",
            R("zero-content-panel__text", 20), R("player-error-panel__title", 23), R("player-error-panel__body", 18),
            R("collection-browser__status", 17), R("card-page__title", 34), R("card-page__count", 17),
            R("collection-action__label", 17), R("collection-filter", 14), R("collection-filter__empty", 19),
            R("browser-row__title", 23), R("browser-row__metadata", 17), R("card-tile__name", 19),
            R("card-tile__number", 14), R("card-tile__rarity", 14), R("card-tile__owned", 14),
            R("card-tile__new", 13), R("collection-detail__new", 13), R("collection-detail__name", 25),
            R("collection-detail__metadata", 17), R("collection-detail__progress", 17));

        public static readonly MobileTypographyProfile Gacha = new MobileTypographyProfile("Gacha",
            R("player-error-panel__title", 23), R("player-error-panel__body", 18), R("gacha-opening__status", 16),
            R("gacha-stage__title", 34), R("gacha-selected-product__metadata", 16),
            R("gacha-product-row__metadata", 16), R("gacha-stage__hint", 16), R("gacha-reveal-progress", 16),
            R("gacha-reveal-card__metadata", 16), R("gacha-summary-row__metadata", 16),
            R("gacha-product-row__name", 21), R("gacha-selected-product__name", 21),
            R("gacha-reveal-card__name", 21), R("gacha-summary-row__name", 21),
            R("gacha-rule-evidence", 14), R("gacha-rule-notice", 16), R("gacha-rule-badge", 16),
            R("gacha-odds-heading", 16), R("gacha-odds-row__name", 16), R("gacha-odds-row__value", 16),
            R("gacha-reveal-card__badge", 16), R("gacha-summary-row__badge", 16),
            R("gacha-opening-statistics", 13), R("gacha-history-row", 12),
            R("gacha-button__label", 18), R("gacha-source-button__label", 15));

        public static readonly MobileTypographyProfile Content = new MobileTypographyProfile("Content",
            R("player-error-panel__title", 23), R("player-error-panel__body", 18),
            R("content-management__title", 34), R("content-management__subtitle", 16),
            R("content-management__empty", 19), R("content-management__catalog-status", 16),
            R("content-management__selection-summary", 16), R("content-management__wifi-only", 16),
            R("content-management__search", 16), R("content-management__filter", 16),
            R("content-package-row__name", 23), R("content-package-row__metadata", 16),
            R("content-package-row__status", 16), R("content-button__label", 16),
            R("content-package-row__progress-value", 16),
            R("content-management__launch-title", 18), R("content-management__launch-body", 17));

        private const string PokedexStyles = "Assets/Resources/UI/PokedexStyles.uss";
        public static readonly MobileTypographyProfile Pokedex = new MobileTypographyProfile("Pokedex",
            R("player-error-panel__title", 23), R("player-error-panel__body", 18),
            R("pokedex-field", 15, PokedexStyles), R("pokedex-status", 15, PokedexStyles),
            R("pokedex-card-count", 15, PokedexStyles), R("pokedex-row__genus", 15, PokedexStyles),
            R("pokedex-card-row__metadata", 15, PokedexStyles), R("pokedex-card-row__status", 15, PokedexStyles),
            R("pokedex-detail__metadata", 15, PokedexStyles), R("pokedex-empty", 18, PokedexStyles),
            R("pokedex-introduced-forms__heading", 18, PokedexStyles), R("pokedex-row__number", 17, PokedexStyles),
            R("pokedex-row__name", 20, PokedexStyles), R("pokedex-card-row__name", 20, PokedexStyles),
            R("pokedex-action__label", 16, PokedexStyles), R("pokedex-artwork__status", 15, PokedexStyles),
            R("pokedex-detail__number", 17, PokedexStyles), R("pokedex-detail__name", 36, PokedexStyles),
            R("pokedex-detail__genus", 18, PokedexStyles), R("pokedex-detail__description", 17, PokedexStyles),
            R("pokedex-section__heading", 20, PokedexStyles));

        public static readonly MobileTypographyProfile CardImage = new MobileTypographyProfile("CardImage",
            R("async-card-image__status", 11), R("async-card-image__retry-label", 12));
        public static readonly MobileTypographyProfile Pack = new MobileTypographyProfile("Pack",
            R("interactive-pack-carousel__position", 15));

        public static IReadOnlyList<MobileTypographyProfile> All { get; } = new[]
            { Shared, Start, Home, Settings, FirstRun, Collection, Gacha, Content, Pokedex, CardImage, Pack };
    }
}
