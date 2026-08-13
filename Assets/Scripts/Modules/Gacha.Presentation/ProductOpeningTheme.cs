using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Gacha.Domain;
using UnityEngine;

namespace Gacha.Presentation
{
    public interface IProductOpeningThemeProvider
    {
        ProductOpeningTheme Resolve(ProductDefinition product);
    }

    public static class ProductOpeningThemeAudioKeys
    {
        public const string VintagePackOpen = "pack.open.vintage";
        public const string VintageRareReveal = "card.rare.vintage";
        public const string ForestPackOpen = "pack.open.forest";
        public const string ForestRareReveal = "card.rare.forest";
        public const string RubyPackOpen = "pack.open.ruby";
        public const string RubyRareReveal = "card.rare.ruby";
        public const string ElectricPackOpen = "pack.open.electric";
        public const string ElectricRareReveal = "card.rare.electric";
        public const string GalleryPackOpen = "pack.open.gallery";
        public const string GalleryRareReveal = "card.rare.gallery";
    }

    public sealed class ProductOpeningParticleTheme
    {
        public ProductOpeningParticleTheme(
            int ambientParticleCount,
            int burstParticleCount,
            float ambientCycleSeconds,
            float ambientDriftPercent,
            float burstDurationSeconds,
            float burstRadiusPercent,
            float burstPulseScale)
        {
            AmbientParticleCount = InRange(
                ambientParticleCount, 1, 12, nameof(ambientParticleCount));
            BurstParticleCount = InRange(
                burstParticleCount, 1, 12, nameof(burstParticleCount));
            AmbientCycleSeconds = InRange(
                ambientCycleSeconds, 0.8f, 8f, nameof(ambientCycleSeconds));
            AmbientDriftPercent = InRange(
                ambientDriftPercent, 0f, 30f, nameof(ambientDriftPercent));
            BurstDurationSeconds = InRange(
                burstDurationSeconds, 0.15f, 1.5f, nameof(burstDurationSeconds));
            BurstRadiusPercent = InRange(
                burstRadiusPercent, 10f, 55f, nameof(burstRadiusPercent));
            BurstPulseScale = InRange(
                burstPulseScale, 1f, 2f, nameof(burstPulseScale));
        }

        public static ProductOpeningParticleTheme Default { get; } =
            new ProductOpeningParticleTheme(6, 10, 3.8f, 8f, 0.68f, 36f, 1.28f);

        public int AmbientParticleCount { get; }
        public int BurstParticleCount { get; }
        public float AmbientCycleSeconds { get; }
        public float AmbientDriftPercent { get; }
        public float BurstDurationSeconds { get; }
        public float BurstRadiusPercent { get; }
        public float BurstPulseScale { get; }

        private static int InRange(int value, int minimum, int maximum, string parameterName)
        {
            if (value < minimum || value > maximum)
                throw new ArgumentOutOfRangeException(parameterName);
            return value;
        }

        private static float InRange(float value, float minimum, float maximum, string parameterName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < minimum || value > maximum)
                throw new ArgumentOutOfRangeException(parameterName);
            return value;
        }
    }

    public sealed class ProductOpeningTheme
    {
        public ProductOpeningTheme(
            string id,
            string styleClass,
            string packOpenAudioKey,
            string rareRevealAudioKey,
            float packTearDurationSeconds,
            float packPulseScale,
            float packPulseCycles,
            float revealDurationSeconds,
            float revealStartScale,
            float rarePulseScale,
            IEnumerable<string> highlightedRarityIdFragments = null,
            string packArtworkResourcePath = null,
            ProductOpeningParticleTheme particleTheme = null,
            ProductOpeningPackPresentation packPresentation = null)
        {
            Id = Required(id, nameof(id));
            StyleClass = Required(styleClass, nameof(styleClass));
            PackOpenAudioKey = Required(packOpenAudioKey, nameof(packOpenAudioKey));
            RareRevealAudioKey = Required(rareRevealAudioKey, nameof(rareRevealAudioKey));
            PackTearDurationSeconds = InRange(
                packTearDurationSeconds, 0.08f, 2f, nameof(packTearDurationSeconds));
            PackPulseScale = InRange(packPulseScale, 1f, 1.2f, nameof(packPulseScale));
            PackPulseCycles = InRange(packPulseCycles, 0.5f, 6f, nameof(packPulseCycles));
            RevealDurationSeconds = InRange(
                revealDurationSeconds, 0.08f, 1f, nameof(revealDurationSeconds));
            RevealStartScale = InRange(revealStartScale, 0.4f, 1f, nameof(revealStartScale));
            RarePulseScale = InRange(rarePulseScale, 1f, 1.4f, nameof(rarePulseScale));
            string legacyArtworkPath = string.IsNullOrWhiteSpace(packArtworkResourcePath)
                ? null
                : packArtworkResourcePath.Trim();
            PackPresentation = packPresentation ??
                new ProductOpeningPackPresentation(legacyArtworkPath, legacyArtworkPath);
            PackArtworkResourcePath = PackPresentation.FrontArtworkResourcePath;
            ParticleTheme = particleTheme ?? ProductOpeningParticleTheme.Default;
            HighlightedRarityIdFragments = new ReadOnlyCollection<string>(
                (highlightedRarityIdFragments ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray());
        }

        public string Id { get; }
        public string StyleClass { get; }
        public string PackOpenAudioKey { get; }
        public string RareRevealAudioKey { get; }
        public float PackTearDurationSeconds { get; }
        public float PackPulseScale { get; }
        public float PackPulseCycles { get; }
        public float RevealDurationSeconds { get; }
        public float RevealStartScale { get; }
        public float RarePulseScale { get; }
        public string PackArtworkResourcePath { get; }
        public ProductOpeningPackPresentation PackPresentation { get; }
        public ProductOpeningParticleTheme ParticleTheme { get; }
        public IReadOnlyList<string> HighlightedRarityIdFragments { get; }

        public bool Highlights(RarityDefinition rarity)
        {
            if (rarity == null)
                return false;
            if (!string.IsNullOrWhiteSpace(rarity.PresentationKey))
                return true;
            return HighlightedRarityIdFragments.Any(fragment =>
                rarity.Id.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string Required(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Theme values cannot be empty.", parameterName);
            return value.Trim();
        }

        private static float InRange(float value, float minimum, float maximum, string parameterName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < minimum || value > maximum)
                throw new ArgumentOutOfRangeException(parameterName);
            return value;
        }
    }

    public sealed class ProductOpeningPackPresentation
    {
        public ProductOpeningPackPresentation(
            string frontArtworkResourcePath,
            string backArtworkResourcePath = null,
            float widthToHeightRatio = 0.70f,
            float topSealHeightRatio = 0.115f,
            float backSeamWidthRatio = 0.045f,
            float acceptanceThreshold = 0.72f,
            float singlePointerPullRatio = 0.34f,
            float dualPointerPullRatio = 0.28f)
        {
            FrontArtworkResourcePath = OptionalPath(frontArtworkResourcePath);
            BackArtworkResourcePath = OptionalPath(backArtworkResourcePath) ?? FrontArtworkResourcePath;
            WidthToHeightRatio = InRange(
                widthToHeightRatio, 0.45f, 1f, nameof(widthToHeightRatio));
            TopSealHeightRatio = InRange(
                topSealHeightRatio, 0.05f, 0.25f, nameof(topSealHeightRatio));
            BackSeamWidthRatio = InRange(
                backSeamWidthRatio, 0.015f, 0.15f, nameof(backSeamWidthRatio));
            AcceptanceThreshold = InRange(
                acceptanceThreshold, 0.5f, 1f, nameof(acceptanceThreshold));
            SinglePointerPullRatio = InRange(
                singlePointerPullRatio, 0.1f, 1f, nameof(singlePointerPullRatio));
            DualPointerPullRatio = InRange(
                dualPointerPullRatio, 0.1f, 1f, nameof(dualPointerPullRatio));
        }

        public string FrontArtworkResourcePath { get; }
        public string BackArtworkResourcePath { get; }
        public float WidthToHeightRatio { get; }
        public float TopSealHeightRatio { get; }
        public float BackSeamWidthRatio { get; }
        public float AcceptanceThreshold { get; }
        public float SinglePointerPullRatio { get; }
        public float DualPointerPullRatio { get; }

        private static string OptionalPath(string value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static float InRange(float value, float minimum, float maximum, string parameterName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < minimum || value > maximum)
                throw new ArgumentOutOfRangeException(parameterName);
            return value;
        }
    }

    public static class ProductOpeningThemeService
    {
        private static IProductOpeningThemeProvider provider;

        public static ProductOpeningTheme DefaultTheme { get; } = new ProductOpeningTheme(
            "universal-default",
            "gacha-theme--default",
            FeedbackCueKeys.PackOpen,
            FeedbackCueKeys.RareReveal,
            0.46f,
            1.035f,
            1.5f,
            0.22f,
            0.72f,
            1.08f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset()
        {
            provider = null;
        }

        public static void Configure(IProductOpeningThemeProvider themeProvider)
        {
            provider = themeProvider;
        }

        public static ProductOpeningTheme Resolve(ProductDefinition product)
        {
            if (product == null)
                throw new ArgumentNullException(nameof(product));
            return provider?.Resolve(product) ?? DefaultTheme;
        }
    }
}
