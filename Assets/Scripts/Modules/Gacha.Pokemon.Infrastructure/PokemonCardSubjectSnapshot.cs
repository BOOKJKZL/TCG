using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Gacha.Pokemon.Domain;
using Newtonsoft.Json;

namespace Gacha.Pokemon.Infrastructure
{
    [Serializable]
    public sealed class PokemonCardSubjectSnapshotDto
    {
        public int SchemaVersion = 1;
        public string Source;
        public string Language;
        public string GeneratedAtUtc;
        public string TaxonomySourceSha256;
        public string CardContentSha256;
        public List<PokemonCardSubjectLinkDto> Links = new List<PokemonCardSubjectLinkDto>();
        public List<string> Warnings = new List<string>();
    }

    [Serializable]
    public sealed class PokemonCardSubjectLinkDto
    {
        public string CardId;
        public string SetId;
        public string LocalId;
        public string ItemId;
        public List<string> PrintingIds = new List<string>();
        public string Category;
        public string CardName;
        public List<string> SpeciesIds = new List<string>();
        public List<string> FormIds = new List<string>();
        public string Status;
        public string Method;
        public double Confidence;
        public string Reason;
        public string OverrideId;
    }

    public sealed class PokemonCardSubjectSnapshotLoadResult
    {
        public PokemonCardSubjectSnapshotLoadResult(
            PokemonCardSubjectCatalog catalog,
            string source,
            string language,
            DateTimeOffset generatedAtUtc,
            string taxonomySourceSha256,
            string cardContentSha256,
            IReadOnlyList<string> warnings)
        {
            Catalog = catalog;
            Source = source;
            Language = language;
            GeneratedAtUtc = generatedAtUtc;
            TaxonomySourceSha256 = taxonomySourceSha256;
            CardContentSha256 = cardContentSha256;
            Warnings = warnings;
        }

        public PokemonCardSubjectCatalog Catalog { get; }
        public string Source { get; }
        public string Language { get; }
        public DateTimeOffset GeneratedAtUtc { get; }
        public string TaxonomySourceSha256 { get; }
        public string CardContentSha256 { get; }
        public IReadOnlyList<string> Warnings { get; }
    }

    public sealed class PokemonCardSubjectSnapshotException : Exception
    {
        public PokemonCardSubjectSnapshotException(string message) : base(message) { }
        public PokemonCardSubjectSnapshotException(string message, Exception innerException)
            : base(message, innerException) { }
    }

    public sealed class PokemonCardSubjectSnapshotReader
    {
        public const int SupportedSchemaVersion = 1;

        public PokemonCardSubjectSnapshotLoadResult LoadFile(
            string path, PokemonTaxonomyCatalog taxonomy)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Card subject snapshot path cannot be empty.", nameof(path));
            try
            {
                return Read(File.ReadAllText(path), taxonomy);
            }
            catch (PokemonCardSubjectSnapshotException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                throw new PokemonCardSubjectSnapshotException(
                    "Failed to read card subject snapshot: " + path, exception);
            }
        }

        public PokemonCardSubjectSnapshotLoadResult Read(
            string json, PokemonTaxonomyCatalog taxonomy)
        {
            if (taxonomy == null)
                throw new ArgumentNullException(nameof(taxonomy));
            PokemonCardSubjectSnapshotDto snapshot;
            try
            {
                snapshot = JsonConvert.DeserializeObject<PokemonCardSubjectSnapshotDto>(json);
            }
            catch (JsonException exception)
            {
                throw new PokemonCardSubjectSnapshotException(
                    "Card subject snapshot is not valid JSON.", exception);
            }
            if (snapshot == null)
                throw new PokemonCardSubjectSnapshotException("Card subject snapshot is empty.");
            ValidateHeader(snapshot);

            try
            {
                PokemonCardSubjectLink[] links = (snapshot.Links ?? new List<PokemonCardSubjectLinkDto>())
                    .Select(item => new PokemonCardSubjectLink(
                        item.CardId,
                        item.SetId,
                        item.LocalId,
                        item.ItemId,
                        item.PrintingIds,
                        item.Category,
                        item.CardName,
                        item.SpeciesIds,
                        item.FormIds,
                        ParseStatus(item.Status),
                        ParseMethod(item.Method),
                        item.Confidence,
                        item.Reason,
                        item.OverrideId))
                    .ToArray();
                var catalog = new PokemonCardSubjectCatalog(links, taxonomy);
                return new PokemonCardSubjectSnapshotLoadResult(
                    catalog,
                    snapshot.Source.Trim(),
                    snapshot.Language.Trim().ToLowerInvariant(),
                    DateTimeOffset.Parse(snapshot.GeneratedAtUtc, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
                    snapshot.TaxonomySourceSha256.ToLowerInvariant(),
                    snapshot.CardContentSha256.ToLowerInvariant(),
                    Normalize(snapshot.Warnings));
            }
            catch (PokemonCardSubjectSnapshotException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is ArgumentException || exception is InvalidOperationException ||
                exception is KeyNotFoundException || exception is FormatException)
            {
                throw new PokemonCardSubjectSnapshotException(
                    "Card subject snapshot failed domain validation: " + exception.Message,
                    exception);
            }
        }

        private static void ValidateHeader(PokemonCardSubjectSnapshotDto snapshot)
        {
            if (snapshot.SchemaVersion != SupportedSchemaVersion)
                throw new PokemonCardSubjectSnapshotException(
                    $"Card subject schema {snapshot.SchemaVersion} is not supported.");
            if (string.IsNullOrWhiteSpace(snapshot.Source) || string.IsNullOrWhiteSpace(snapshot.Language))
                throw new PokemonCardSubjectSnapshotException("Card subject source and language are required.");
            if (!DateTimeOffset.TryParse(snapshot.GeneratedAtUtc, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out _))
                throw new PokemonCardSubjectSnapshotException("Card subject generation timestamp is invalid.");
            if (!IsSha256(snapshot.TaxonomySourceSha256) || !IsSha256(snapshot.CardContentSha256))
                throw new PokemonCardSubjectSnapshotException("Card subject source hashes are invalid.");
        }

        private static PokemonCardMatchStatus ParseStatus(string value)
        {
            return value?.Trim() switch
            {
                "matched-form" => PokemonCardMatchStatus.MatchedForm,
                "matched-species" => PokemonCardMatchStatus.MatchedSpecies,
                "multi-species" => PokemonCardMatchStatus.MultiSpecies,
                "not-applicable" => PokemonCardMatchStatus.NotApplicable,
                "needs-review" => PokemonCardMatchStatus.NeedsReview,
                _ => throw new PokemonCardSubjectSnapshotException(
                    "Unsupported card subject status: " + value)
            };
        }

        private static PokemonCardMatchMethod ParseMethod(string value)
        {
            return value?.Trim() switch
            {
                "source-dex-id" => PokemonCardMatchMethod.SourceDexId,
                "source-dex-id-and-form-name" => PokemonCardMatchMethod.SourceDexIdAndFormName,
                "canonical-english-name" => PokemonCardMatchMethod.CanonicalEnglishName,
                "manual-override" => PokemonCardMatchMethod.ManualOverride,
                "category" => PokemonCardMatchMethod.Category,
                _ => throw new PokemonCardSubjectSnapshotException(
                    "Unsupported card subject method: " + value)
            };
        }

        private static bool IsSha256(string value) =>
            !string.IsNullOrWhiteSpace(value) && value.Length == 64 &&
            value.All(character =>
                (character >= '0' && character <= '9') ||
                (character >= 'a' && character <= 'f') ||
                (character >= 'A' && character <= 'F'));

        private static IReadOnlyList<string> Normalize(IEnumerable<string> values) =>
            (values ?? Enumerable.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
    }
}
