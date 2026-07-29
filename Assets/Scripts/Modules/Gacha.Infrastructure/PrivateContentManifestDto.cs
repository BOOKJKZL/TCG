using System;
using System.Collections.Generic;

namespace Gacha.Infrastructure.Content
{
    [Serializable]
    public sealed class PrivateContentManifestDto
    {
        public int SchemaVersion = 2;
        public string Source;
        public string Language;
        public string GeneratedAtUtc;
        public ImportedSetDto Set;
        public List<ImportedCardDto> Cards = new List<ImportedCardDto>();
        public List<ContentImportErrorDto> Errors = new List<ContentImportErrorDto>();
    }

    [Serializable]
    public sealed class ImportedSetDto
    {
        public string Id;
        public string Name;
        public string SetCode;
        public string SeriesId;
        public string SeriesName;
        public string EraId;
        public string GenerationId;
        public int? GenerationOrder;
        public int? SetOrdinal;
        public string ReleaseDate;
        public int OfficialCardCount;
        public int TotalCardCount;
        public string SourceUrl;
        public string RawDataRelativePath;
    }

    [Serializable]
    public sealed class ImportedCardDto
    {
        public string Id;
        public string LocalId;
        public string Name;
        public string Category;
        public string Rarity;
        public string Illustrator;
        public string Updated;
        public string SourceUrl;
        public string RawDataRelativePath;
        public string ImageSourceUrl;
        public string ImageRelativePath;
        public string ImageSha256;
        public long ImageBytes;
        public ImportedCardVariantsDto Variants = new ImportedCardVariantsDto();
        public List<string> Types = new List<string>();
        public List<string> BoosterIds = new List<string>();
    }

    [Serializable]
    public sealed class ImportedCardVariantsDto
    {
        public bool Normal;
        public bool Reverse;
        public bool Holo;
        public bool FirstEdition;
        public bool WPromo;
    }

    [Serializable]
    public sealed class ContentImportErrorDto
    {
        public string ItemId;
        public string Message;
    }

    public sealed class PrivateContentManifestDocument
    {
        public PrivateContentManifestDocument(
            string manifestPath,
            PrivateContentManifestDto manifest,
            int? sourceSchemaVersion = null)
        {
            ManifestPath = manifestPath;
            Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            SourceSchemaVersion = sourceSchemaVersion ?? manifest.SchemaVersion;
        }

        public string ManifestPath { get; }
        public PrivateContentManifestDto Manifest { get; }
        public int SourceSchemaVersion { get; }
        public bool WasMigrated => SourceSchemaVersion != Manifest.SchemaVersion;
    }
}
