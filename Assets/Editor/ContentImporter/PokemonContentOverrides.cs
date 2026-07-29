using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

public sealed class PokemonContentOverrideException : Exception
{
    public PokemonContentOverrideException(string message) : base(message) { }
    public PokemonContentOverrideException(string message, Exception innerException) : base(message, innerException) { }
}

[Serializable]
public sealed class PokemonSetGenerationOverrideFile
{
    public int SchemaVersion = 1;
    public List<PokemonSetGenerationOverride> Sets = new List<PokemonSetGenerationOverride>();
}

[Serializable]
public sealed class PokemonSetGenerationOverride
{
    public string SetId;
    public string SetCode;
    public string EraId;
    public string GenerationId;
    public int GenerationOrder;
    public int SetOrdinal;
}

[Serializable]
public sealed class PokemonFormClassificationFile
{
    public int SchemaVersion = 1;
    public List<PokemonFormKindPolicy> Policies = new List<PokemonFormKindPolicy>();
    public List<PokemonFormClassificationOverride> Overrides = new List<PokemonFormClassificationOverride>();
}

[Serializable]
public sealed class PokemonFormKindPolicy
{
    public string FormKind;
    public string DefaultDisposition;
    public string Reason;
}

[Serializable]
public sealed class PokemonFormClassificationOverride
{
    public string FormId;
    public string SpeciesId;
    public string Disposition;
    public List<string> RelatedFormIds = new List<string>();
    public string Reason;
}

public sealed class PokemonSetGenerationOverrideCatalog
{
    private readonly IReadOnlyDictionary<string, PokemonSetGenerationOverride> entries;

    internal PokemonSetGenerationOverrideCatalog(
        IReadOnlyDictionary<string, PokemonSetGenerationOverride> entries)
    {
        this.entries = entries;
    }

    public int Count => entries.Count;

    public bool Apply(ImportedSetRecord set)
    {
        if (set == null)
            throw new ArgumentNullException(nameof(set));
        if (string.IsNullOrWhiteSpace(set.Id) || !entries.TryGetValue(set.Id.Trim(), out PokemonSetGenerationOverride entry))
            return false;

        set.SetCode = entry.SetCode;
        set.EraId = entry.EraId;
        set.GenerationId = entry.GenerationId;
        set.GenerationOrder = entry.GenerationOrder;
        set.SetOrdinal = entry.SetOrdinal;
        return true;
    }
}

public sealed class PokemonFormClassificationCatalog
{
    private readonly IReadOnlyDictionary<string, PokemonFormKindPolicy> policies;
    private readonly IReadOnlyDictionary<string, PokemonFormClassificationOverride> overrides;

    internal PokemonFormClassificationCatalog(
        IReadOnlyDictionary<string, PokemonFormKindPolicy> policies,
        IReadOnlyDictionary<string, PokemonFormClassificationOverride> overrides)
    {
        this.policies = policies;
        this.overrides = overrides;
    }

    public int PolicyCount => policies.Count;
    public int OverrideCount => overrides.Count;

    public PokemonFormKindPolicy GetPolicy(string formKind)
    {
        if (string.IsNullOrWhiteSpace(formKind) || !policies.TryGetValue(formKind.Trim(), out PokemonFormKindPolicy policy))
            throw new KeyNotFoundException($"No form classification policy exists for '{formKind}'.");
        return policy;
    }

    public bool TryGetOverride(string formId, out PokemonFormClassificationOverride entry)
    {
        entry = null;
        return !string.IsNullOrWhiteSpace(formId) && overrides.TryGetValue(formId.Trim(), out entry);
    }
}

public static class PokemonContentOverrideLoader
{
    public const int SupportedSchemaVersion = 1;
    private static readonly string[] RequiredFormKinds =
    {
        "regional",
        "mega",
        "gigantamax",
        "battle-only",
        "gender-difference",
        "cosmetic"
    };
    private static readonly HashSet<string> AllowedDispositions = new HashSet<string>(
        new[] { "separate-entry", "related-variant", "exclude", "manual-review" },
        StringComparer.Ordinal);

    public static PokemonSetGenerationOverrideCatalog LoadOptionalSetGeneration(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return EmptySetCatalog();
        return LoadSetGeneration(path);
    }

    public static PokemonSetGenerationOverrideCatalog LoadSetGeneration(string path)
    {
        PokemonSetGenerationOverrideFile file = Read<PokemonSetGenerationOverrideFile>(path);
        if (file.SchemaVersion != SupportedSchemaVersion)
            throw new PokemonContentOverrideException(
                $"Set generation override schema {file.SchemaVersion} is not supported.");

        var entries = new Dictionary<string, PokemonSetGenerationOverride>(StringComparer.Ordinal);
        foreach (PokemonSetGenerationOverride entry in file.Sets ?? Enumerable.Empty<PokemonSetGenerationOverride>())
        {
            ValidateSetEntry(entry);
            string setId = entry.SetId.Trim();
            if (entries.ContainsKey(setId))
                throw new PokemonContentOverrideException($"Duplicate SetId in generation overrides: {setId}.");

            entry.SetId = setId;
            entry.SetCode = entry.SetCode.Trim();
            entry.EraId = entry.EraId.Trim();
            entry.GenerationId = entry.GenerationId.Trim();
            entries.Add(setId, entry);
        }

        return new PokemonSetGenerationOverrideCatalog(
            new ReadOnlyDictionary<string, PokemonSetGenerationOverride>(entries));
    }

    public static PokemonFormClassificationCatalog LoadFormClassification(string path)
    {
        PokemonFormClassificationFile file = Read<PokemonFormClassificationFile>(path);
        if (file.SchemaVersion != SupportedSchemaVersion)
            throw new PokemonContentOverrideException(
                $"Form classification schema {file.SchemaVersion} is not supported.");

        var policies = new Dictionary<string, PokemonFormKindPolicy>(StringComparer.Ordinal);
        foreach (PokemonFormKindPolicy policy in file.Policies ?? Enumerable.Empty<PokemonFormKindPolicy>())
        {
            if (policy == null || string.IsNullOrWhiteSpace(policy.FormKind))
                throw new PokemonContentOverrideException("Form policy requires FormKind.");
            string formKind = policy.FormKind.Trim();
            ValidateDisposition(policy.DefaultDisposition, $"form policy '{formKind}'");
            if (string.IsNullOrWhiteSpace(policy.Reason))
                throw new PokemonContentOverrideException($"Form policy '{formKind}' requires a reason.");
            if (policies.ContainsKey(formKind))
                throw new PokemonContentOverrideException($"Duplicate form policy: {formKind}.");

            policy.FormKind = formKind;
            policy.DefaultDisposition = policy.DefaultDisposition.Trim();
            policy.Reason = policy.Reason.Trim();
            policies.Add(formKind, policy);
        }

        foreach (string required in RequiredFormKinds)
            if (!policies.ContainsKey(required))
                throw new PokemonContentOverrideException($"Missing required form policy: {required}.");

        var overrides = new Dictionary<string, PokemonFormClassificationOverride>(StringComparer.Ordinal);
        foreach (PokemonFormClassificationOverride entry in
                 file.Overrides ?? Enumerable.Empty<PokemonFormClassificationOverride>())
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.FormId) || string.IsNullOrWhiteSpace(entry.SpeciesId))
                throw new PokemonContentOverrideException("Form override requires FormId and SpeciesId.");
            string formId = entry.FormId.Trim();
            ValidateDisposition(entry.Disposition, $"form override '{formId}'");
            if (string.IsNullOrWhiteSpace(entry.Reason))
                throw new PokemonContentOverrideException($"Form override '{formId}' requires a reason.");
            if (overrides.ContainsKey(formId))
                throw new PokemonContentOverrideException($"Duplicate form override: {formId}.");

            entry.FormId = formId;
            entry.SpeciesId = entry.SpeciesId.Trim();
            entry.Disposition = entry.Disposition.Trim();
            entry.Reason = entry.Reason.Trim();
            entry.RelatedFormIds = (entry.RelatedFormIds ?? new List<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToList();
            overrides.Add(formId, entry);
        }

        return new PokemonFormClassificationCatalog(
            new ReadOnlyDictionary<string, PokemonFormKindPolicy>(policies),
            new ReadOnlyDictionary<string, PokemonFormClassificationOverride>(overrides));
    }

    private static PokemonSetGenerationOverrideCatalog EmptySetCatalog()
    {
        return new PokemonSetGenerationOverrideCatalog(
            new ReadOnlyDictionary<string, PokemonSetGenerationOverride>(
                new Dictionary<string, PokemonSetGenerationOverride>(StringComparer.Ordinal)));
    }

    private static void ValidateSetEntry(PokemonSetGenerationOverride entry)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.SetId))
            throw new PokemonContentOverrideException("Set generation override requires SetId.");
        if (string.IsNullOrWhiteSpace(entry.SetCode) || string.IsNullOrWhiteSpace(entry.EraId) ||
            string.IsNullOrWhiteSpace(entry.GenerationId))
            throw new PokemonContentOverrideException(
                $"Set generation override '{entry.SetId}' requires SetCode, EraId, and GenerationId.");
        if (entry.GenerationOrder < 1)
            throw new PokemonContentOverrideException(
                $"Set generation override '{entry.SetId}' requires GenerationOrder >= 1.");
        if (entry.SetOrdinal < 1)
            throw new PokemonContentOverrideException(
                $"Set generation override '{entry.SetId}' requires SetOrdinal >= 1.");
    }

    private static void ValidateDisposition(string disposition, string context)
    {
        string normalized = disposition?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || !AllowedDispositions.Contains(normalized))
            throw new PokemonContentOverrideException(
                $"Unsupported disposition '{disposition}' in {context}.");
    }

    private static T Read<T>(string path) where T : class
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Override path cannot be empty.", nameof(path));
        try
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("Override file was not found.", path);
            T result = JsonConvert.DeserializeObject<T>(File.ReadAllText(path));
            return result ?? throw new PokemonContentOverrideException($"Override file is empty: {path}");
        }
        catch (PokemonContentOverrideException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
        {
            throw new PokemonContentOverrideException($"Failed to read override file: {path}", exception);
        }
    }
}
