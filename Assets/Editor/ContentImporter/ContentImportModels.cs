using System;
using System.Collections.Generic;

[Serializable]
public sealed class PrivateContentManifest
{
    public int SchemaVersion = 2;
    public string Source = "tcgdex";
    public string Language;
    public string GeneratedAtUtc;
    public ImportedSetRecord Set;
    public List<ImportedCardRecord> Cards = new List<ImportedCardRecord>();
    public List<ContentImportError> Errors = new List<ContentImportError>();
}

[Serializable]
public sealed class ImportedSetRecord
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
public sealed class ImportedCardRecord
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
    public ImportedCardVariants Variants = new ImportedCardVariants();
    public List<string> Types = new List<string>();
    public List<string> BoosterIds = new List<string>();
}

[Serializable]
public sealed class ImportedCardVariants
{
    public bool Normal;
    public bool Reverse;
    public bool Holo;
    public bool FirstEdition;
    public bool WPromo;
}

[Serializable]
public sealed class ContentImportError
{
    public string ItemId;
    public string Message;
}

public sealed class ContentImportProgress
{
    public string SetId;
    public string Stage;
    public int Completed;
    public int Total;

    public float Ratio => Total <= 0 ? 0f : (float)Completed / Total;
}

public sealed class ContentImportOptions
{
    public string Language = "en";
    public string OutputRoot;
    public string SetGenerationOverridesPath;
    public string ImageQuality = "low";
    public string ImageExtension = "jpg";
    public int MaxConcurrency = 4;
    public int MaximumCardsPerSet;
    public bool RefreshExistingFiles;
}

public sealed class ContentImportSummary
{
    public int SetCount;
    public int CardCount;
    public int ErrorCount;
    public long ImageBytes;
}
