using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace Gacha.Pokemon.Infrastructure
{
    [Serializable]
    public sealed class PokemonArtworkManifestDto
    {
        public int SchemaVersion = 1;
        public string GenerationId;
        public string GeneratedAtUtc;
        public string TaxonomySourceSha256;
        public List<PokemonArtworkEntryDto> Entries = new List<PokemonArtworkEntryDto>();
        public List<string> MissingFormIds = new List<string>();
    }

    [Serializable]
    public sealed class PokemonArtworkEntryDto
    {
        public string FormId;
        public string RelativePath;
        public string Sha256;
        public long Bytes;
        public string SourceUrl;
    }

    public sealed class PokemonArtworkEntry
    {
        public PokemonArtworkEntry(
            string formId,
            string relativePath,
            string sha256,
            long bytes,
            string sourceUrl)
        {
            if (string.IsNullOrWhiteSpace(formId))
                throw new ArgumentException("Artwork form id is required.", nameof(formId));
            if (!IsPortableRelativePath(relativePath))
                throw new ArgumentException("Artwork relative path is unsafe or non-portable.", nameof(relativePath));
            if (!IsSha256(sha256))
                throw new ArgumentException("Artwork SHA-256 is invalid.", nameof(sha256));
            if (bytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(bytes));
            if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out Uri source) || source.Scheme != Uri.UriSchemeHttps)
                throw new ArgumentException("Artwork source URL must use HTTPS.", nameof(sourceUrl));
            FormId = formId.Trim();
            RelativePath = relativePath.Replace('\\', '/');
            Sha256 = sha256.ToLowerInvariant();
            Bytes = bytes;
            SourceUri = source;
        }

        public string FormId { get; }
        public string RelativePath { get; }
        public string Sha256 { get; }
        public long Bytes { get; }
        public Uri SourceUri { get; }

        private static bool IsPortableRelativePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value))
                return false;
            string portable = value.Replace('\\', '/');
            return portable.Split('/').All(segment =>
                !string.IsNullOrWhiteSpace(segment) && segment != "." && segment != ".." &&
                segment.IndexOfAny(new[] { '<', '>', ':', '"', '|', '?', '*' }) < 0);
        }

        private static bool IsSha256(string value) =>
            !string.IsNullOrWhiteSpace(value) && value.Length == 64 && value.All(character =>
                character >= '0' && character <= '9' ||
                character >= 'a' && character <= 'f' ||
                character >= 'A' && character <= 'F');
    }

    public sealed class PokemonArtworkCatalog
    {
        private readonly IReadOnlyDictionary<string, PokemonArtworkEntry> entries;

        public PokemonArtworkCatalog(
            string generationId,
            DateTimeOffset generatedAtUtc,
            string taxonomySourceSha256,
            IEnumerable<PokemonArtworkEntry> entries,
            IEnumerable<string> missingFormIds)
        {
            if (string.IsNullOrWhiteSpace(generationId))
                throw new ArgumentException("Artwork generation id is required.", nameof(generationId));
            if (string.IsNullOrWhiteSpace(taxonomySourceSha256) || taxonomySourceSha256.Length != 64)
                throw new ArgumentException("Artwork taxonomy SHA-256 is invalid.", nameof(taxonomySourceSha256));
            var index = new Dictionary<string, PokemonArtworkEntry>(StringComparer.Ordinal);
            foreach (PokemonArtworkEntry entry in entries ?? Enumerable.Empty<PokemonArtworkEntry>())
            {
                if (entry == null || !index.TryAdd(entry.FormId, entry))
                    throw new ArgumentException("Artwork manifest contains null or duplicate form entries.", nameof(entries));
            }
            if (index.Count == 0)
                throw new ArgumentException("Artwork manifest requires at least one image.", nameof(entries));
            GenerationId = generationId.Trim();
            GeneratedAtUtc = generatedAtUtc;
            TaxonomySourceSha256 = taxonomySourceSha256.ToLowerInvariant();
            this.entries = new ReadOnlyDictionary<string, PokemonArtworkEntry>(index);
            MissingFormIds = (missingFormIds ?? Enumerable.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
        }

        public string GenerationId { get; }
        public DateTimeOffset GeneratedAtUtc { get; }
        public string TaxonomySourceSha256 { get; }
        public IReadOnlyDictionary<string, PokemonArtworkEntry> Entries => entries;
        public IReadOnlyList<string> MissingFormIds { get; }
        public PokemonArtworkEntry Find(string formId) =>
            entries.TryGetValue(formId ?? string.Empty, out PokemonArtworkEntry entry) ? entry : null;
    }

    public sealed class PokemonArtworkManifestReader
    {
        public PokemonArtworkCatalog LoadFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Artwork manifest path is required.", nameof(path));
            PokemonArtworkManifestDto dto;
            try
            {
                dto = JsonConvert.DeserializeObject<PokemonArtworkManifestDto>(File.ReadAllText(path));
            }
            catch (Exception exception) when (exception is IOException || exception is JsonException)
            {
                throw new InvalidDataException("Failed to read Pokémon artwork manifest: " + path, exception);
            }
            if (dto == null || dto.SchemaVersion != 1)
                throw new InvalidDataException("Pokémon artwork manifest is empty or unsupported.");
            if (!DateTimeOffset.TryParse(dto.GeneratedAtUtc, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset generated))
                throw new InvalidDataException("Pokémon artwork manifest timestamp is invalid.");
            return new PokemonArtworkCatalog(
                dto.GenerationId,
                generated,
                dto.TaxonomySourceSha256,
                (dto.Entries ?? new List<PokemonArtworkEntryDto>()).Select(value => new PokemonArtworkEntry(
                    value.FormId, value.RelativePath, value.Sha256, value.Bytes, value.SourceUrl)),
                dto.MissingFormIds);
        }
    }
}
