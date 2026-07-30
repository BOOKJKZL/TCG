using System;
using System.Collections.Generic;

[Serializable]
public sealed class ContentInventoryOptions
{
    public string OutputRoot;
    public string ReferenceLanguage = "en";
    public List<string> Languages = new List<string>();
    public List<string> DetailedLanguages = new List<string> { "en" };
    public string SetGenerationOverridesPath;
    public int MaxConcurrency = 4;
    public int ImageSampleCount = 12;
}

[Serializable]
public sealed class ContentInventorySnapshot
{
    public int SchemaVersion = 1;
    public string Source = "tcgdex";
    public string ApiRoot;
    public string ReferenceLanguage;
    public string GeneratedAtUtc;
    public string ContentSha256;
    public List<ContentInventoryLanguageRecord> Languages = new List<ContentInventoryLanguageRecord>();
    public List<ContentInventorySetRecord> Sets = new List<ContentInventorySetRecord>();
    public ContentInventoryImageEstimate ImageEstimate = new ContentInventoryImageEstimate();
    public List<ContentInventoryError> Errors = new List<ContentInventoryError>();
}

[Serializable]
public sealed class ContentInventoryLanguageRecord
{
    public string Language;
    public bool Detailed;
    public int SetCount;
    public int OfficialCardCount;
    public int TotalCardCount;
    public int SetLogoCount;
    public int SetSymbolCount;
    public int DetailedSetCount;
    public int CardEntryCount;
    public int CardImageCount;
    public int MappedSetCount;
    public int UnmappedSetCount;
}

[Serializable]
public sealed class ContentInventorySetRecord
{
    public string Language;
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
    public int CardEntryCount;
    public int CardImageCount;
    public string LogoUrl;
    public string SymbolUrl;
    public string SourceUrl;
}

[Serializable]
public sealed class ContentInventoryImageEstimate
{
    public int RequestedSampleCount;
    public int CompletedSampleCount;
    public long HighJpegBytes;
    public long LowWebpBytes;
    public long AverageHighJpegBytes;
    public long AverageLowWebpBytes;
    public long ProjectedHighJpegBytes;
    public long ProjectedLowWebpBytes;
}

[Serializable]
public sealed class ContentInventoryError
{
    public string Scope;
    public string ItemId;
    public string Message;
}

public sealed class ContentInventoryProgress
{
    public string Stage;
    public string ItemId;
    public int Completed;
    public int Total;

    public float Ratio => Total <= 0 ? 0f : (float)Completed / Total;
}
