using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json;

[Serializable]
public sealed class ContentImportCheckpoint
{
    public int SchemaVersion = 1;
    public string Language;
    public string Configuration;
    public string UpdatedAtUtc;
    public List<ContentImportSetCheckpoint> Sets = new List<ContentImportSetCheckpoint>();
    public List<ContentImportFailureRecord> Failures = new List<ContentImportFailureRecord>();
}

[Serializable]
public sealed class ContentImportSetCheckpoint
{
    public string SetId;
    public string State;
    public int ExpectedCards;
    public int ProcessedCards;
    public int FailedCards;
    public string UpdatedAtUtc;
}

[Serializable]
public sealed class ContentImportFailureRecord
{
    public string Scope;
    public string SetId;
    public string ItemId;
    public string Message;
}

[Serializable]
public sealed class ContentImportFailureReport
{
    public int SchemaVersion = 1;
    public string Language;
    public string Configuration;
    public string GeneratedAtUtc;
    public List<ContentImportFailureRecord> Failures = new List<ContentImportFailureRecord>();
}

public sealed class ContentImportCheckpointStore
{
    public const int SupportedSchemaVersion = 1;
    private readonly object gate = new object();
    private readonly string checkpointPath;
    private readonly string failureReportPath;
    private readonly ContentImportCheckpoint checkpoint;

    public ContentImportCheckpointStore(
        string outputRoot, string language, string imageQuality, string imageExtension)
    {
        if (string.IsNullOrWhiteSpace(outputRoot))
            throw new ArgumentException("Output root is required.", nameof(outputRoot));
        if (string.IsNullOrWhiteSpace(language))
            throw new ArgumentException("Language is required.", nameof(language));
        string configuration = ConfigurationName(imageQuality, imageExtension);
        string languageDirectory = Path.Combine(outputRoot, language);
        Directory.CreateDirectory(languageDirectory);
        checkpointPath = Path.Combine(languageDirectory, $"bulk-import-checkpoint-{configuration}.json");
        failureReportPath = Path.Combine(languageDirectory, $"bulk-import-failures-{configuration}.json");
        checkpoint = LoadOrCreate(checkpointPath, language, configuration);
    }

    public string CheckpointPath => checkpointPath;
    public string FailureReportPath => failureReportPath;

    public void Begin(IEnumerable<string> setIds)
    {
        lock (gate)
        {
            foreach (string setId in setIds.Where(value => !string.IsNullOrWhiteSpace(value))
                         .Select(value => value.Trim()).Distinct(StringComparer.Ordinal))
                FindOrAdd(setId);
            SaveCheckpoint();
        }
    }

    public void StartSet(string setId, int expectedCards)
    {
        lock (gate)
        {
            ContentImportSetCheckpoint set = FindOrAdd(setId);
            set.State = "running";
            set.ExpectedCards = Math.Max(0, expectedCards);
            set.ProcessedCards = 0;
            set.FailedCards = 0;
            Touch(set);
            checkpoint.Failures.RemoveAll(item =>
                string.Equals(item.SetId, setId, StringComparison.Ordinal));
            SaveCheckpoint();
        }
    }

    public void RecordCard(string setId, string cardId, string errorMessage)
    {
        lock (gate)
        {
            ContentImportSetCheckpoint set = FindOrAdd(setId);
            set.ProcessedCards++;
            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                set.FailedCards++;
                checkpoint.Failures.Add(new ContentImportFailureRecord
                {
                    Scope = "card",
                    SetId = setId,
                    ItemId = cardId,
                    Message = errorMessage
                });
            }
            Touch(set);
            SaveCheckpoint();
        }
    }

    public void CompleteSet(string setId)
    {
        lock (gate)
        {
            ContentImportSetCheckpoint set = FindOrAdd(setId);
            set.State = set.FailedCards == 0 ? "completed" : "completed-with-errors";
            Touch(set);
            SaveCheckpoint();
        }
    }

    public void FailSet(string setId, string message)
    {
        lock (gate)
        {
            ContentImportSetCheckpoint set = FindOrAdd(setId);
            set.State = "failed";
            set.FailedCards++;
            checkpoint.Failures.Add(new ContentImportFailureRecord
            {
                Scope = "set",
                SetId = setId,
                ItemId = setId,
                Message = message
            });
            Touch(set);
            SaveCheckpoint();
        }
    }

    public ContentImportCheckpoint Snapshot()
    {
        lock (gate)
        {
            return JsonConvert.DeserializeObject<ContentImportCheckpoint>(
                JsonConvert.SerializeObject(checkpoint));
        }
    }

    public void WriteFailureReport()
    {
        lock (gate)
        {
            var report = new ContentImportFailureReport
            {
                Language = checkpoint.Language,
                Configuration = checkpoint.Configuration,
                GeneratedAtUtc = DateTime.UtcNow.ToString("O"),
                Failures = checkpoint.Failures
                    .OrderBy(item => item.Scope, StringComparer.Ordinal)
                    .ThenBy(item => item.SetId, StringComparer.Ordinal)
                    .ThenBy(item => item.ItemId, StringComparer.Ordinal)
                    .Select(CloneFailure)
                    .ToList()
            };
            WriteAtomic(failureReportPath, JsonConvert.SerializeObject(report, Formatting.Indented));
        }
    }

    private ContentImportSetCheckpoint FindOrAdd(string setId)
    {
        ContentImportSetCheckpoint set = checkpoint.Sets.FirstOrDefault(item =>
            string.Equals(item.SetId, setId, StringComparison.Ordinal));
        if (set != null)
            return set;
        set = new ContentImportSetCheckpoint { SetId = setId, State = "pending" };
        checkpoint.Sets.Add(set);
        checkpoint.Sets = checkpoint.Sets
            .OrderBy(item => item.SetId, StringComparer.Ordinal)
            .ToList();
        return set;
    }

    private void Touch(ContentImportSetCheckpoint set)
    {
        string now = DateTime.UtcNow.ToString("O");
        set.UpdatedAtUtc = now;
        checkpoint.UpdatedAtUtc = now;
    }

    private void SaveCheckpoint()
    {
        checkpoint.UpdatedAtUtc = DateTime.UtcNow.ToString("O");
        WriteAtomic(checkpointPath, JsonConvert.SerializeObject(checkpoint, Formatting.Indented));
    }

    private static ContentImportCheckpoint LoadOrCreate(
        string path, string language, string configuration)
    {
        if (!File.Exists(path))
            return new ContentImportCheckpoint
            {
                Language = language,
                Configuration = configuration
            };
        ContentImportCheckpoint result = JsonConvert.DeserializeObject<ContentImportCheckpoint>(
            File.ReadAllText(path));
        if (result == null || result.SchemaVersion != SupportedSchemaVersion)
            throw new InvalidDataException($"Unsupported bulk import checkpoint: {path}");
        if (!string.Equals(result.Language, language, StringComparison.Ordinal) ||
            !string.Equals(result.Configuration, configuration, StringComparison.Ordinal))
            throw new InvalidDataException($"Bulk import checkpoint configuration mismatch: {path}");
        result.Sets = result.Sets ?? new List<ContentImportSetCheckpoint>();
        result.Failures = result.Failures ?? new List<ContentImportFailureRecord>();
        return result;
    }

    private static ContentImportFailureRecord CloneFailure(ContentImportFailureRecord source)
    {
        return new ContentImportFailureRecord
        {
            Scope = source.Scope,
            SetId = source.SetId,
            ItemId = source.ItemId,
            Message = source.Message
        };
    }

    private static string ConfigurationName(string quality, string extension)
    {
        string normalizedQuality = (quality ?? string.Empty).Trim().ToLowerInvariant();
        string normalizedExtension = (extension ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedQuality.Length == 0 || normalizedExtension.Length == 0 ||
            normalizedQuality.Any(character => !char.IsLetterOrDigit(character) && character != '-') ||
            normalizedExtension.Any(character => !char.IsLetterOrDigit(character) && character != '-'))
            throw new ArgumentException("Image quality and extension must be safe configuration names.");
        return normalizedQuality + "-" + normalizedExtension;
    }

    private static void WriteAtomic(string path, string text)
    {
        string temporaryPath = path + ".download";
        File.WriteAllText(temporaryPath, text, new UTF8Encoding(false));
        Exception lastException = null;
        for (int attempt = 1; attempt <= 8; attempt++)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
                File.Move(temporaryPath, path);
                return;
            }
            catch (Exception exception) when (
                exception is IOException || exception is UnauthorizedAccessException)
            {
                lastException = exception;
                if (attempt < 8)
                    Thread.Sleep(attempt * 25);
            }
        }
        throw new IOException($"Failed to atomically replace checkpoint '{path}'.", lastException);
    }
}
