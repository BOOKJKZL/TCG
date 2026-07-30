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
    public sealed class PokemonTaxonomySnapshotDto
    {
        public int SchemaVersion = 1;
        public string Source;
        public string SourceBaseUrl;
        public string CapturedAtUtc;
        public string SourceSha256;
        public List<string> Languages = new List<string>();
        public List<PokemonGenerationSnapshotDto> Generations = new List<PokemonGenerationSnapshotDto>();
        public List<PokemonSpeciesSnapshotDto> Species = new List<PokemonSpeciesSnapshotDto>();
        public List<PokemonFormSnapshotDto> Forms = new List<PokemonFormSnapshotDto>();
        public List<string> Warnings = new List<string>();
    }

    [Serializable]
    public sealed class PokemonGenerationSnapshotDto
    {
        public string Id;
        public int Order;
        public Dictionary<string, string> Names = new Dictionary<string, string>();
        public int SpeciesStartNumber;
        public int SpeciesEndNumber;
        public string SourceUrl;
    }

    [Serializable]
    public sealed class PokemonSpeciesSnapshotDto
    {
        public string Id;
        public int NationalDexNumber;
        public string DebutGenerationId;
        public Dictionary<string, string> Names = new Dictionary<string, string>();
        public Dictionary<string, string> Genera = new Dictionary<string, string>();
        public Dictionary<string, string> Descriptions = new Dictionary<string, string>();
        public string DefaultFormId;
        public List<string> FormIds = new List<string>();
        public bool IsBaby;
        public bool IsLegendary;
        public bool IsMythical;
        public string ColorId;
        public string HabitatId;
        public string SourceUrl;
    }

    [Serializable]
    public sealed class PokemonFormSnapshotDto
    {
        public string Id;
        public string SpeciesId;
        public int PokemonId;
        public string FormKind;
        public string Disposition;
        public Dictionary<string, string> Names = new Dictionary<string, string>();
        public string IntroducedGenerationId;
        public List<string> RelatedFormIds = new List<string>();
        public List<string> TypeIds = new List<string>();
        public bool IsDefault;
        public bool IsBattleOnly;
        public bool IsMega;
        public bool IsGigantamax;
        public string RegionId;
        public string ImageRelativePath;
        public string ImageSourceUrl;
        public string ImageSha256;
        public string SourceUrl;
    }

    public sealed class PokemonTaxonomySnapshotLoadResult
    {
        public PokemonTaxonomySnapshotLoadResult(
            PokemonTaxonomyCatalog catalog,
            string source,
            Uri sourceBaseUri,
            DateTimeOffset capturedAtUtc,
            string sourceSha256,
            IReadOnlyList<string> languages,
            IReadOnlyList<string> warnings)
        {
            Catalog = catalog;
            Source = source;
            SourceBaseUri = sourceBaseUri;
            CapturedAtUtc = capturedAtUtc;
            SourceSha256 = sourceSha256;
            Languages = languages;
            Warnings = warnings;
        }

        public PokemonTaxonomyCatalog Catalog { get; }
        public string Source { get; }
        public Uri SourceBaseUri { get; }
        public DateTimeOffset CapturedAtUtc { get; }
        public string SourceSha256 { get; }
        public IReadOnlyList<string> Languages { get; }
        public IReadOnlyList<string> Warnings { get; }
    }

    public sealed class PokemonTaxonomySnapshotException : Exception
    {
        public PokemonTaxonomySnapshotException(string message) : base(message) { }
        public PokemonTaxonomySnapshotException(string message, Exception innerException) : base(message, innerException) { }
    }

    public sealed class PokemonTaxonomySnapshotReader
    {
        public const int SupportedSchemaVersion = 1;

        public PokemonTaxonomySnapshotLoadResult LoadFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Taxonomy snapshot path cannot be empty.", nameof(path));
            try
            {
                return Read(File.ReadAllText(path));
            }
            catch (PokemonTaxonomySnapshotException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                throw new PokemonTaxonomySnapshotException("Failed to read Pokémon taxonomy snapshot: " + path, exception);
            }
        }

        public PokemonTaxonomySnapshotLoadResult Read(string json)
        {
            PokemonTaxonomySnapshotDto snapshot;
            try
            {
                snapshot = JsonConvert.DeserializeObject<PokemonTaxonomySnapshotDto>(json);
            }
            catch (JsonException exception)
            {
                throw new PokemonTaxonomySnapshotException("Pokémon taxonomy snapshot is not valid JSON.", exception);
            }
            if (snapshot == null)
                throw new PokemonTaxonomySnapshotException("Pokémon taxonomy snapshot is empty.");
            ValidateHeader(snapshot);

            try
            {
                PokemonGenerationDefinition[] generations = (snapshot.Generations ??
                        new List<PokemonGenerationSnapshotDto>())
                    .Select(item => new PokemonGenerationDefinition(
                        item.Id,
                        item.Order,
                        item.Names,
                        item.SpeciesStartNumber,
                        item.SpeciesEndNumber,
                        item.SourceUrl))
                    .ToArray();
                PokemonSpeciesDefinition[] species = (snapshot.Species ??
                        new List<PokemonSpeciesSnapshotDto>())
                    .Select(item => new PokemonSpeciesDefinition(
                        item.Id,
                        item.NationalDexNumber,
                        item.DebutGenerationId,
                        item.Names,
                        item.Genera,
                        item.Descriptions,
                        item.DefaultFormId,
                        item.FormIds,
                        item.IsBaby,
                        item.IsLegendary,
                        item.IsMythical,
                        item.ColorId,
                        item.HabitatId,
                        item.SourceUrl))
                    .ToArray();
                PokemonFormDefinition[] forms = (snapshot.Forms ?? new List<PokemonFormSnapshotDto>())
                    .Select(BuildForm)
                    .ToArray();

                var catalog = new PokemonTaxonomyCatalog(generations, species, forms);
                return new PokemonTaxonomySnapshotLoadResult(
                    catalog,
                    snapshot.Source.Trim(),
                    new Uri(snapshot.SourceBaseUrl, UriKind.Absolute),
                    DateTimeOffset.Parse(
                        snapshot.CapturedAtUtc,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
                    snapshot.SourceSha256.ToLowerInvariant(),
                    NormalizeStrings(snapshot.Languages),
                    NormalizeStrings(snapshot.Warnings));
            }
            catch (PokemonTaxonomySnapshotException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is ArgumentException || exception is InvalidOperationException ||
                exception is KeyNotFoundException || exception is FormatException)
            {
                throw new PokemonTaxonomySnapshotException(
                    "Pokémon taxonomy snapshot failed domain validation: " + exception.Message,
                    exception);
            }
        }

        private static PokemonFormDefinition BuildForm(PokemonFormSnapshotDto item)
        {
            if (item == null)
                throw new PokemonTaxonomySnapshotException("Pokémon taxonomy contains a null form.");
            ValidateImageReference(item);
            return new PokemonFormDefinition(
                item.Id,
                item.SpeciesId,
                item.PokemonId,
                item.FormKind,
                ParseDisposition(item.Disposition),
                item.Names,
                item.IntroducedGenerationId,
                item.RelatedFormIds,
                item.TypeIds,
                item.IsDefault,
                item.IsBattleOnly,
                item.IsMega,
                item.IsGigantamax,
                item.RegionId,
                item.ImageRelativePath,
                item.ImageSourceUrl,
                item.ImageSha256,
                item.SourceUrl);
        }

        private static void ValidateHeader(PokemonTaxonomySnapshotDto snapshot)
        {
            if (snapshot.SchemaVersion != SupportedSchemaVersion)
                throw new PokemonTaxonomySnapshotException(
                    $"Pokémon taxonomy schema {snapshot.SchemaVersion} is not supported.");
            if (string.IsNullOrWhiteSpace(snapshot.Source))
                throw new PokemonTaxonomySnapshotException("Pokémon taxonomy source cannot be empty.");
            if (!Uri.TryCreate(snapshot.SourceBaseUrl, UriKind.Absolute, out Uri sourceUri) ||
                !string.Equals(sourceUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                throw new PokemonTaxonomySnapshotException("Pokémon taxonomy source base URL must use HTTPS.");
            if (!DateTimeOffset.TryParse(
                    snapshot.CapturedAtUtc,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out _))
                throw new PokemonTaxonomySnapshotException("Pokémon taxonomy capture timestamp is invalid.");
            if (!IsSha256(snapshot.SourceSha256))
                throw new PokemonTaxonomySnapshotException("Pokémon taxonomy source SHA-256 is invalid.");
            if ((snapshot.Languages ?? new List<string>()).Count == 0)
                throw new PokemonTaxonomySnapshotException("Pokémon taxonomy requires at least one language.");
        }

        private static void ValidateImageReference(PokemonFormSnapshotDto form)
        {
            bool hasPath = !string.IsNullOrWhiteSpace(form.ImageRelativePath);
            bool hasHash = !string.IsNullOrWhiteSpace(form.ImageSha256);
            if (hasPath != hasHash)
                throw new PokemonTaxonomySnapshotException(
                    $"Form '{form.Id}' image path and SHA-256 must be provided together.");
            if (!hasPath)
                return;

            string path = form.ImageRelativePath.Replace('\\', '/');
            if (!path.StartsWith("images/", StringComparison.Ordinal) ||
                path.StartsWith("/", StringComparison.Ordinal) ||
                path.Split('/').Any(segment => string.IsNullOrWhiteSpace(segment) || segment == "." || segment == "..") ||
                path.Contains(":"))
                throw new PokemonTaxonomySnapshotException($"Form '{form.Id}' has an unsafe image path.");
            if (!IsSha256(form.ImageSha256))
                throw new PokemonTaxonomySnapshotException($"Form '{form.Id}' image SHA-256 is invalid.");
        }

        private static PokemonFormDisposition ParseDisposition(string value)
        {
            switch (value?.Trim())
            {
                case "separate-entry": return PokemonFormDisposition.SeparateEntry;
                case "related-variant": return PokemonFormDisposition.RelatedVariant;
                case "exclude": return PokemonFormDisposition.Excluded;
                case "manual-review": return PokemonFormDisposition.ManualReview;
                default:
                    throw new PokemonTaxonomySnapshotException(
                        "Unsupported Pokémon form disposition: " + value);
            }
        }

        private static bool IsSha256(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && value.Length == 64 &&
                   value.All(character =>
                       (character >= '0' && character <= '9') ||
                       (character >= 'a' && character <= 'f') ||
                       (character >= 'A' && character <= 'F'));
        }

        private static IReadOnlyList<string> NormalizeStrings(IEnumerable<string> values)
        {
            return (values ?? Enumerable.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
        }
    }
}
