using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

[Serializable]
public sealed class CrossRegionSetMappingFile
{
    public int SchemaVersion = 1;
    public string SourceCoverageSnapshotSha256;
    public List<CrossRegionSetMapping> Groups = new List<CrossRegionSetMapping>();
}

[Serializable]
public sealed class CrossRegionSetMapping
{
    public string Id;
    public string Relationship;
    public string ReviewStatus;
    public List<CrossRegionSetMember> Members = new List<CrossRegionSetMember>();
    public List<string> Evidence = new List<string>();
    public string Reason;
    public string ReviewedDate;
}

[Serializable]
public sealed class CrossRegionSetMember
{
    public string Language;
    public string SetId;
}

[Serializable]
public sealed class MultilingualCardIdentityOverrideFile
{
    public int SchemaVersion = 1;
    public string SourceCoverageSnapshotSha256;
    public List<MultilingualCardIdentityOverride> Decisions =
        new List<MultilingualCardIdentityOverride>();
}

[Serializable]
public sealed class MultilingualCardIdentityOverride
{
    public string Id;
    public string Disposition;
    public List<string> RecordIds = new List<string>();
    public List<string> Evidence = new List<string>();
    public string Reason;
    public string ReviewedDate;
}

[Serializable]
public sealed class MultilingualIdentityGroupResult
{
    public string Id;
    public string Classification;
    public double Confidence;
    public List<string> RecordIds = new List<string>();
    public List<string> Languages = new List<string>();
    public List<string> Signals = new List<string>();
    public string OverrideId;
    public string Reason;
}

[Serializable]
public sealed class MultilingualIdentityCardResult
{
    public string RecordId;
    public string Language;
    public string SetId;
    public string CardId;
    public string LocalId;
    public string SemanticFingerprintSha256;
    public string Classification;
    public List<string> GroupIds = new List<string>();
}

[Serializable]
public sealed class MultilingualIdentityCompilationReport
{
    public int SchemaVersion = 1;
    public bool IsValid;
    public string SourceCoverageSnapshotSha256;
    public string SnapshotSha256;
    public int TotalCardCount;
    public int CandidateGroupCount;
    public int AutoAcceptedGroupCount;
    public int ReviewedAcceptedGroupCount;
    public int PendingReviewGroupCount;
    public int ReviewedRejectedGroupCount;
    public int AutoAcceptedCardCount;
    public int ReviewedAcceptedCardCount;
    public int PendingReviewCardCount;
    public int ReviewedRejectedCardCount;
    public int UnmatchedCardCount;
    public List<MultilingualIdentityGroupResult> Groups =
        new List<MultilingualIdentityGroupResult>();
    public List<MultilingualIdentityCardResult> Cards =
        new List<MultilingualIdentityCardResult>();
    public List<string> Failures = new List<string>();
}

[Serializable]
public sealed class MultilingualIdentityReviewQueue
{
    public int SchemaVersion = 1;
    public string SourceCoverageSnapshotSha256;
    public string SourceIdentitySnapshotSha256;
    public int PendingGroupCount;
    public List<MultilingualIdentityReviewQueueEntry> Groups =
        new List<MultilingualIdentityReviewQueueEntry>();
}

[Serializable]
public sealed class MultilingualIdentityReviewQueueEntry
{
    public string Id;
    public double Confidence;
    public List<string> Signals = new List<string>();
    public string Reason;
    public List<MultilingualIdentityReviewQueueMember> Members =
        new List<MultilingualIdentityReviewQueueMember>();
}

[Serializable]
public sealed class MultilingualIdentityReviewQueueMember
{
    public string RecordId;
    public string Language;
    public string SetId;
    public string SetName;
    public string CardId;
    public string LocalId;
    public string CardName;
    public string Category;
    public string Rarity;
    public string Illustrator;
    public string VariantKey;
    public string ImageStatus;
    public string ImageSha256;
}

public static class MultilingualIdentityCompiler
{
    public const int SupportedSchemaVersion = 1;
    public const string SetLocalSignal = "same-set-and-local-id";
    public const string SourceCardSignal = "same-source-card-id";
    public const string ImageHashSignal = "same-image-sha256";
    public const string ReviewedSetSignal = "reviewed-set-and-local-id";

    private static readonly HashSet<string> AllowedSetRelationships =
        new HashSet<string>(new[] { "equivalent", "partial-overlap", "unrelated" },
            StringComparer.Ordinal);
    private static readonly HashSet<string> AllowedSetReviewStatuses =
        new HashSet<string>(new[] { "pending", "reviewed" }, StringComparer.Ordinal);
    private static readonly HashSet<string> AllowedOverrideDispositions =
        new HashSet<string>(new[] { "accept", "reject" }, StringComparer.Ordinal);

    public static MultilingualIdentityCompilationReport Compile(
        MultilingualProductionCoverageReport coverage,
        string setMappingPath,
        string cardOverridePath,
        string jsonOutputPath = null,
        string markdownOutputPath = null,
        string reviewQueueOutputPath = null)
    {
        if (coverage == null)
            throw new ArgumentNullException(nameof(coverage));
        if (!coverage.IsValid || string.IsNullOrWhiteSpace(coverage.SnapshotSha256))
            throw new InvalidOperationException("A valid coverage report with a snapshot is required.");

        CrossRegionSetMappingFile mappings = LoadSetMappings(
            setMappingPath, coverage.SnapshotSha256);
        MultilingualCardIdentityOverrideFile overrides = LoadCardOverrides(
            cardOverridePath, coverage.SnapshotSha256);
        var report = new MultilingualIdentityCompilationReport
        {
            SourceCoverageSnapshotSha256 = coverage.SnapshotSha256
        };
        Dictionary<string, MultilingualCoverageCardRecord> cardsById = coverage.Cards
            .ToDictionary(value => value.RecordId, StringComparer.Ordinal);
        Dictionary<string, Candidate> candidates = BuildCandidates(coverage, mappings, cardsById);
        Dictionary<string, MultilingualCardIdentityOverride> decisions =
            IndexDecisions(overrides, cardsById);

        foreach (MultilingualCardIdentityOverride decision in decisions.Values)
        {
            string signature = Signature(decision.RecordIds);
            if (!candidates.TryGetValue(signature, out Candidate candidate))
            {
                candidate = new Candidate(decision.RecordIds);
                candidates.Add(signature, candidate);
            }
            candidate.Override = decision;
        }

        HashSet<string> claimedAcceptedCards = new HashSet<string>(StringComparer.Ordinal);
        foreach (Candidate candidate in candidates.Values
                     .OrderBy(value => Signature(value.RecordIds), StringComparer.Ordinal))
        {
            MultilingualIdentityGroupResult group = Classify(
                candidate, cardsById, mappings, claimedAcceptedCards);
            report.Groups.Add(group);
        }

        Dictionary<string, List<MultilingualIdentityGroupResult>> groupsByCard =
            report.Groups.SelectMany(group => group.RecordIds.Select(recordId => new { recordId, group }))
                .GroupBy(value => value.recordId, StringComparer.Ordinal)
                .ToDictionary(
                    value => value.Key,
                    value => value.Select(item => item.group)
                        .OrderBy(item => item.Id, StringComparer.Ordinal).ToList(),
                    StringComparer.Ordinal);
        foreach (MultilingualCoverageCardRecord source in coverage.Cards
                     .OrderBy(value => value.RecordId, StringComparer.Ordinal))
        {
            List<MultilingualIdentityGroupResult> groups = groupsByCard.TryGetValue(source.RecordId,
                    out List<MultilingualIdentityGroupResult> values)
                ? values
                : new List<MultilingualIdentityGroupResult>();
            string classification = ClassifyCard(groups, report.Failures, source.RecordId);
            report.Cards.Add(new MultilingualIdentityCardResult
            {
                RecordId = source.RecordId,
                Language = source.Language,
                SetId = source.SetId,
                CardId = source.CardId,
                LocalId = source.LocalId,
                SemanticFingerprintSha256 = Fingerprint(source),
                Classification = classification,
                GroupIds = groups.Select(value => value.Id).ToList()
            });
        }

        report.Groups = report.Groups.OrderBy(value => value.Id, StringComparer.Ordinal).ToList();
        report.TotalCardCount = report.Cards.Count;
        report.CandidateGroupCount = report.Groups.Count;
        report.AutoAcceptedGroupCount = CountGroups(report, "auto-accepted");
        report.ReviewedAcceptedGroupCount = CountGroups(report, "reviewed-accepted");
        report.PendingReviewGroupCount = CountGroups(report, "pending-review");
        report.ReviewedRejectedGroupCount = CountGroups(report, "reviewed-rejected");
        report.AutoAcceptedCardCount = CountCards(report, "auto-accepted");
        report.ReviewedAcceptedCardCount = CountCards(report, "reviewed-accepted");
        report.PendingReviewCardCount = CountCards(report, "pending-review");
        report.ReviewedRejectedCardCount = CountCards(report, "reviewed-rejected");
        report.UnmatchedCardCount = CountCards(report, "unmatched");
        int classified = report.AutoAcceptedCardCount + report.ReviewedAcceptedCardCount +
                         report.PendingReviewCardCount + report.ReviewedRejectedCardCount +
                         report.UnmatchedCardCount;
        if (classified != report.TotalCardCount)
            report.Failures.Add(
                $"Card classifications cover {classified} of {report.TotalCardCount} records.");
        report.Failures = report.Failures.Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal).ToList();
        report.IsValid = report.Failures.Count == 0;
        report.SnapshotSha256 = Snapshot(report);

        if (!string.IsNullOrWhiteSpace(jsonOutputPath))
            WriteReport(jsonOutputPath, JsonConvert.SerializeObject(report, Formatting.Indented));
        if (!string.IsNullOrWhiteSpace(markdownOutputPath))
            WriteReport(markdownOutputPath, Markdown(report));
        if (!string.IsNullOrWhiteSpace(reviewQueueOutputPath))
            WriteReport(reviewQueueOutputPath, JsonConvert.SerializeObject(
                BuildReviewQueue(report, cardsById), Formatting.Indented));
        return report;
    }

    public static CrossRegionSetMappingFile LoadSetMappings(
        string path, string expectedCoverageSnapshotSha256)
    {
        CrossRegionSetMappingFile file = Read<CrossRegionSetMappingFile>(path);
        ValidateHeader(file.SchemaVersion, file.SourceCoverageSnapshotSha256,
            expectedCoverageSnapshotSha256, "Set mapping");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (CrossRegionSetMapping group in file.Groups ?? new List<CrossRegionSetMapping>())
        {
            if (group == null || string.IsNullOrWhiteSpace(group.Id))
                throw new InvalidDataException("Set mapping requires an id.");
            group.Id = group.Id.Trim();
            if (!ids.Add(group.Id))
                throw new InvalidDataException($"Duplicate Set mapping id: {group.Id}.");
            group.Relationship = NormalizedAllowed(group.Relationship,
                AllowedSetRelationships, $"Set mapping '{group.Id}' relationship");
            group.ReviewStatus = NormalizedAllowed(group.ReviewStatus,
                AllowedSetReviewStatuses, $"Set mapping '{group.Id}' review status");
            group.Members = NormalizeSetMembers(group);
            group.Evidence = NormalizeList(group.Evidence);
            group.Reason = Required(group.Reason, $"Set mapping '{group.Id}' reason");
            if (group.ReviewStatus == "reviewed")
            {
                if (group.Evidence.Count == 0)
                    throw new InvalidDataException(
                        $"Reviewed Set mapping '{group.Id}' requires evidence.");
                group.ReviewedDate = ValidDate(group.ReviewedDate,
                    $"Set mapping '{group.Id}' reviewed date");
            }
            else if (!string.IsNullOrWhiteSpace(group.ReviewedDate))
            {
                throw new InvalidDataException(
                    $"Pending Set mapping '{group.Id}' cannot have a reviewed date.");
            }
        }
        file.Groups = (file.Groups ?? new List<CrossRegionSetMapping>())
            .OrderBy(value => value.Id, StringComparer.Ordinal).ToList();
        return file;
    }

    public static MultilingualCardIdentityOverrideFile LoadCardOverrides(
        string path, string expectedCoverageSnapshotSha256)
    {
        MultilingualCardIdentityOverrideFile file = Read<MultilingualCardIdentityOverrideFile>(path);
        ValidateHeader(file.SchemaVersion, file.SourceCoverageSnapshotSha256,
            expectedCoverageSnapshotSha256, "Card identity override");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var records = new HashSet<string>(StringComparer.Ordinal);
        foreach (MultilingualCardIdentityOverride decision in
                 file.Decisions ?? new List<MultilingualCardIdentityOverride>())
        {
            if (decision == null || string.IsNullOrWhiteSpace(decision.Id))
                throw new InvalidDataException("Card identity override requires an id.");
            decision.Id = decision.Id.Trim();
            if (!ids.Add(decision.Id))
                throw new InvalidDataException($"Duplicate card identity override id: {decision.Id}.");
            decision.Disposition = NormalizedAllowed(decision.Disposition,
                AllowedOverrideDispositions, $"Card identity override '{decision.Id}' disposition");
            decision.RecordIds = NormalizeList(decision.RecordIds);
            if (decision.RecordIds.Count < 2)
                throw new InvalidDataException(
                    $"Card identity override '{decision.Id}' requires at least two records.");
            foreach (string recordId in decision.RecordIds)
                if (!records.Add(recordId))
                    throw new InvalidDataException(
                        $"Record '{recordId}' occurs in more than one card identity override.");
            decision.Evidence = NormalizeList(decision.Evidence);
            if (decision.Evidence.Count == 0)
                throw new InvalidDataException(
                    $"Card identity override '{decision.Id}' requires evidence.");
            decision.Reason = Required(decision.Reason,
                $"Card identity override '{decision.Id}' reason");
            decision.ReviewedDate = ValidDate(decision.ReviewedDate,
                $"Card identity override '{decision.Id}' reviewed date");
        }
        file.Decisions = (file.Decisions ?? new List<MultilingualCardIdentityOverride>())
            .OrderBy(value => value.Id, StringComparer.Ordinal).ToList();
        return file;
    }

    private static Dictionary<string, Candidate> BuildCandidates(
        MultilingualProductionCoverageReport coverage,
        CrossRegionSetMappingFile mappings,
        IReadOnlyDictionary<string, MultilingualCoverageCardRecord> cardsById)
    {
        var result = new Dictionary<string, Candidate>(StringComparer.Ordinal);
        foreach (MultilingualCoverageCandidateGroup source in coverage.CandidateGroups)
            AddSignal(result, source.Members.Select(value => value.RecordId), source.Strategy);

        foreach (CrossRegionSetMapping mapping in mappings.Groups
                     .Where(value => value.ReviewStatus == "reviewed" &&
                                     value.Relationship == "equivalent"))
        {
            HashSet<string> memberSets = new HashSet<string>(mapping.Members.Select(SetMemberKey),
                StringComparer.Ordinal);
            foreach (IGrouping<string, MultilingualCoverageCardRecord> sameNumber in cardsById.Values
                         .Where(card => memberSets.Contains(SetMemberKey(card.Language, card.SetId)))
                         .GroupBy(card => Key(card.LocalId, card.VariantKey), StringComparer.Ordinal))
            {
                MultilingualCoverageCardRecord[] members = sameNumber.ToArray();
                if (members.Select(value => value.Language).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
                    AddSignal(result, members.Select(value => value.RecordId), ReviewedSetSignal);
            }
        }
        return result;
    }

    private static Dictionary<string, MultilingualCardIdentityOverride> IndexDecisions(
        MultilingualCardIdentityOverrideFile file,
        IReadOnlyDictionary<string, MultilingualCoverageCardRecord> cardsById)
    {
        var result = new Dictionary<string, MultilingualCardIdentityOverride>(StringComparer.Ordinal);
        foreach (MultilingualCardIdentityOverride decision in file.Decisions)
        {
            foreach (string recordId in decision.RecordIds)
                if (!cardsById.ContainsKey(recordId))
                    throw new InvalidDataException(
                        $"Card identity override '{decision.Id}' references missing record '{recordId}'.");
            string signature = Signature(decision.RecordIds);
            if (result.ContainsKey(signature))
                throw new InvalidDataException(
                    $"More than one card identity override uses record group '{signature}'.");
            result.Add(signature, decision);
        }
        return result;
    }

    private static MultilingualIdentityGroupResult Classify(
        Candidate candidate,
        IReadOnlyDictionary<string, MultilingualCoverageCardRecord> cardsById,
        CrossRegionSetMappingFile mappings,
        ISet<string> claimedAcceptedCards)
    {
        MultilingualCoverageCardRecord[] cards = candidate.RecordIds
            .Select(recordId => cardsById[recordId])
            .OrderBy(value => value.RecordId, StringComparer.Ordinal).ToArray();
        string[] languages = cards.Select(value => value.Language)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.Ordinal).ToArray();
        bool onePerLanguage = languages.Length == cards.Length;
        bool reviewedSetConflict = HasReviewedUnrelatedSetConflict(cards, mappings);
        string classification;
        double confidence;
        string reason;

        if (candidate.Override != null)
        {
            classification = candidate.Override.Disposition == "accept"
                ? "reviewed-accepted"
                : "reviewed-rejected";
            confidence = 1d;
            reason = candidate.Override.Reason;
            if (classification == "reviewed-accepted" && !onePerLanguage)
                throw new InvalidDataException(
                    $"Accepted override '{candidate.Override.Id}' contains more than one card for a language.");
        }
        else if (reviewedSetConflict)
        {
            classification = "reviewed-rejected";
            confidence = 1d;
            reason = "A reviewed Set relationship marks at least one member pair as unrelated.";
        }
        else if (onePerLanguage && HasIndependentStrongSignals(candidate.Signals))
        {
            classification = "auto-accepted";
            confidence = 0.99d;
            reason = "Stable source identity agrees with an independent structural or image signal.";
        }
        else
        {
            classification = "pending-review";
            confidence = Confidence(candidate.Signals);
            reason = onePerLanguage
                ? "Available evidence is insufficient for automatic identity acceptance."
                : "Candidate contains more than one card for the same language.";
        }

        if ((classification == "auto-accepted" || classification == "reviewed-accepted") &&
            candidate.RecordIds.Any(claimedAcceptedCards.Contains))
        {
            classification = "pending-review";
            confidence = Math.Min(confidence, 0.5d);
            reason = "Candidate overlaps another accepted identity group.";
        }
        if (classification == "auto-accepted" || classification == "reviewed-accepted")
            foreach (string recordId in candidate.RecordIds)
                claimedAcceptedCards.Add(recordId);

        string signature = Signature(candidate.RecordIds);
        return new MultilingualIdentityGroupResult
        {
            Id = "identity|" + Hash(signature).Substring(0, 20),
            Classification = classification,
            Confidence = confidence,
            RecordIds = candidate.RecordIds.OrderBy(value => value, StringComparer.Ordinal).ToList(),
            Languages = languages.ToList(),
            Signals = candidate.Signals.OrderBy(value => value, StringComparer.Ordinal).ToList(),
            OverrideId = candidate.Override?.Id,
            Reason = reason
        };
    }

    private static string ClassifyCard(
        IReadOnlyList<MultilingualIdentityGroupResult> groups,
        ICollection<string> failures,
        string recordId)
    {
        string[] accepted = groups.Where(value => value.Classification == "auto-accepted" ||
                                                   value.Classification == "reviewed-accepted")
            .Select(value => value.Classification).Distinct(StringComparer.Ordinal).ToArray();
        if (accepted.Length > 1)
        {
            failures.Add($"Record '{recordId}' has conflicting accepted classifications.");
            return "pending-review";
        }
        if (accepted.Length == 1)
            return accepted[0];
        if (groups.Any(value => value.Classification == "pending-review"))
            return "pending-review";
        if (groups.Any(value => value.Classification == "reviewed-rejected"))
            return "reviewed-rejected";
        return "unmatched";
    }

    private static bool HasIndependentStrongSignals(ISet<string> signals) =>
        signals.Contains(SourceCardSignal) &&
        (signals.Contains(SetLocalSignal) || signals.Contains(ImageHashSignal) ||
         signals.Contains(ReviewedSetSignal));

    private static double Confidence(ISet<string> signals)
    {
        if (signals.Contains(SourceCardSignal)) return 0.9d;
        if (signals.Contains(ReviewedSetSignal)) return 0.8d;
        if (signals.Contains(ImageHashSignal)) return 0.75d;
        if (signals.Contains(SetLocalSignal)) return 0.6d;
        return 0.25d;
    }

    private static bool HasReviewedUnrelatedSetConflict(
        IReadOnlyList<MultilingualCoverageCardRecord> cards,
        CrossRegionSetMappingFile mappings)
    {
        foreach (CrossRegionSetMapping mapping in mappings.Groups.Where(value =>
                     value.ReviewStatus == "reviewed" && value.Relationship == "unrelated"))
        {
            var members = new HashSet<string>(mapping.Members.Select(SetMemberKey), StringComparer.Ordinal);
            if (cards.Count(value => members.Contains(SetMemberKey(value.Language, value.SetId))) >= 2)
                return true;
        }
        return false;
    }

    private static string Fingerprint(MultilingualCoverageCardRecord card)
    {
        string canonical = string.Join("\n",
            Key(card.Category),
            Key(card.Rarity),
            Key(card.Illustrator),
            Key(card.VariantKey),
            string.Join("|", card.Types.Select(value => Key(value))
                .OrderBy(value => value, StringComparer.Ordinal)));
        return Hash(canonical);
    }

    private static void AddSignal(
        IDictionary<string, Candidate> candidates,
        IEnumerable<string> recordIds,
        string signal)
    {
        string[] ids = recordIds.Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        if (ids.Length < 2) return;
        string signature = Signature(ids);
        if (!candidates.TryGetValue(signature, out Candidate candidate))
        {
            candidate = new Candidate(ids);
            candidates.Add(signature, candidate);
        }
        candidate.Signals.Add(signal);
    }

    private static List<CrossRegionSetMember> NormalizeSetMembers(CrossRegionSetMapping group)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var members = new List<CrossRegionSetMember>();
        foreach (CrossRegionSetMember member in group.Members ?? new List<CrossRegionSetMember>())
        {
            if (member == null)
                throw new InvalidDataException($"Set mapping '{group.Id}' contains a null member.");
            member.Language = Required(member.Language,
                $"Set mapping '{group.Id}' member language").ToLowerInvariant();
            member.SetId = Required(member.SetId, $"Set mapping '{group.Id}' member Set id");
            if (!keys.Add(SetMemberKey(member)))
                throw new InvalidDataException(
                    $"Set mapping '{group.Id}' repeats member '{member.Language}/{member.SetId}'.");
            members.Add(member);
        }
        if (members.Count < 2 || members.Select(value => value.Language)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() < 2)
            throw new InvalidDataException(
                $"Set mapping '{group.Id}' requires members from at least two languages.");
        return members.OrderBy(SetMemberKey, StringComparer.Ordinal).ToList();
    }

    private static void ValidateHeader(
        int schemaVersion,
        string sourceSnapshot,
        string expectedSnapshot,
        string label)
    {
        if (schemaVersion != SupportedSchemaVersion)
            throw new InvalidDataException(
                $"{label} schema {schemaVersion} is unsupported; expected {SupportedSchemaVersion}.");
        string actual = NormalizeSha(sourceSnapshot);
        string expected = NormalizeSha(expectedSnapshot);
        if (actual == null || expected == null || !string.Equals(actual, expected, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"{label} source snapshot '{sourceSnapshot}' does not match coverage '{expectedSnapshot}'.");
    }

    private static string NormalizedAllowed(string value, ISet<string> allowed, string label)
    {
        string normalized = value?.Trim().ToLowerInvariant();
        if (normalized == null || !allowed.Contains(normalized))
            throw new InvalidDataException($"{label} '{value}' is unsupported.");
        return normalized;
    }

    private static string ValidDate(string value, string label)
    {
        string normalized = value?.Trim();
        if (!DateTime.TryParseExact(normalized, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _))
            throw new InvalidDataException($"{label} must use yyyy-MM-dd.");
        return normalized;
    }

    private static T Read<T>(string path) where T : class
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A versioned data path is required.", nameof(path));
        try
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("Versioned identity data was not found.", path);
            T value = JsonConvert.DeserializeObject<T>(File.ReadAllText(path, Encoding.UTF8));
            return value ?? throw new InvalidDataException($"Versioned identity data is empty: {path}");
        }
        catch (Exception exception) when (exception is IOException || exception is JsonException)
        {
            throw new InvalidDataException($"Failed to read versioned identity data: {path}", exception);
        }
    }

    private static string Snapshot(MultilingualIdentityCompilationReport report)
    {
        string previous = report.SnapshotSha256;
        report.SnapshotSha256 = null;
        string json = JsonConvert.SerializeObject(report, Formatting.None);
        report.SnapshotSha256 = previous;
        return Hash(json);
    }

    private static string Markdown(MultilingualIdentityCompilationReport report)
    {
        var text = new StringBuilder();
        text.AppendLine("# Multilingual identity compilation");
        text.AppendLine();
        text.AppendLine($"Valid: `{report.IsValid}`");
        text.AppendLine($"Coverage snapshot: `{report.SourceCoverageSnapshotSha256}`");
        text.AppendLine($"Identity snapshot: `{report.SnapshotSha256}`");
        text.AppendLine();
        text.AppendLine("| Classification | Groups | Cards |");
        text.AppendLine("|---|---:|---:|");
        text.AppendLine($"| auto-accepted | {report.AutoAcceptedGroupCount} | {report.AutoAcceptedCardCount} |");
        text.AppendLine($"| reviewed-accepted | {report.ReviewedAcceptedGroupCount} | {report.ReviewedAcceptedCardCount} |");
        text.AppendLine($"| pending-review | {report.PendingReviewGroupCount} | {report.PendingReviewCardCount} |");
        text.AppendLine($"| reviewed-rejected | {report.ReviewedRejectedGroupCount} | {report.ReviewedRejectedCardCount} |");
        text.AppendLine($"| unmatched | 0 | {report.UnmatchedCardCount} |");
        text.AppendLine();
        text.AppendLine("## Language combinations");
        text.AppendLine();
        text.AppendLine("| Classification | Languages | Groups |");
        text.AppendLine("|---|---|---:|");
        foreach (IGrouping<string, MultilingualIdentityGroupResult> group in report.Groups
                     .GroupBy(value => value.Classification + "\n" + string.Join("+", value.Languages),
                         StringComparer.Ordinal).OrderBy(value => value.Key, StringComparer.Ordinal))
        {
            string[] key = group.Key.Split('\n');
            text.AppendLine($"| {key[0]} | {key[1]} | {group.Count()} |");
        }
        text.AppendLine();
        text.AppendLine("## Failures");
        text.AppendLine();
        if (report.Failures.Count == 0) text.AppendLine("- None.");
        else foreach (string failure in report.Failures) text.AppendLine("- " + failure);
        return text.ToString().Replace("\r\n", "\n");
    }

    private static MultilingualIdentityReviewQueue BuildReviewQueue(
        MultilingualIdentityCompilationReport report,
        IReadOnlyDictionary<string, MultilingualCoverageCardRecord> cardsById)
    {
        var queue = new MultilingualIdentityReviewQueue
        {
            SourceCoverageSnapshotSha256 = report.SourceCoverageSnapshotSha256,
            SourceIdentitySnapshotSha256 = report.SnapshotSha256
        };
        foreach (MultilingualIdentityGroupResult group in report.Groups
                     .Where(value => value.Classification == "pending-review")
                     .OrderBy(value => value.Id, StringComparer.Ordinal))
        {
            queue.Groups.Add(new MultilingualIdentityReviewQueueEntry
            {
                Id = group.Id,
                Confidence = group.Confidence,
                Signals = group.Signals.ToList(),
                Reason = group.Reason,
                Members = group.RecordIds.Select(recordId => cardsById[recordId])
                    .OrderBy(value => value.Language, StringComparer.Ordinal)
                    .ThenBy(value => value.SetId, StringComparer.Ordinal)
                    .ThenBy(value => value.LocalId, StringComparer.Ordinal)
                    .Select(value => new MultilingualIdentityReviewQueueMember
                    {
                        RecordId = value.RecordId,
                        Language = value.Language,
                        SetId = value.SetId,
                        SetName = value.SetName,
                        CardId = value.CardId,
                        LocalId = value.LocalId,
                        CardName = value.CardName,
                        Category = value.Category,
                        Rarity = value.Rarity,
                        Illustrator = value.Illustrator,
                        VariantKey = value.VariantKey,
                        ImageStatus = value.ImageStatus,
                        ImageSha256 = value.ImageSha256
                    }).ToList()
            });
        }
        queue.PendingGroupCount = queue.Groups.Count;
        return queue;
    }

    private static void WriteReport(string path, string content)
    {
        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("Report path has no directory.");
        Directory.CreateDirectory(directory);
        string temporary = fullPath + ".download";
        File.WriteAllText(temporary, content.Replace("\r\n", "\n"), new UTF8Encoding(false));
        if (File.Exists(fullPath)) File.Replace(temporary, fullPath, null);
        else File.Move(temporary, fullPath);
    }

    private static int CountGroups(MultilingualIdentityCompilationReport report, string status) =>
        report.Groups.Count(value => value.Classification == status);

    private static int CountCards(MultilingualIdentityCompilationReport report, string status) =>
        report.Cards.Count(value => value.Classification == status);

    private static List<string> NormalizeList(IEnumerable<string> values) =>
        (values ?? Enumerable.Empty<string>()).Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value.Trim()).Distinct(StringComparer.Ordinal)
        .OrderBy(value => value, StringComparer.Ordinal).ToList();

    private static string Required(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException(label + " is required.");
        return value.Trim();
    }

    private static string NormalizeSha(string value)
    {
        string normalized = value?.Trim().ToLowerInvariant();
        return normalized != null && normalized.Length == 64 && normalized.All(character =>
            character >= '0' && character <= '9' || character >= 'a' && character <= 'f')
            ? normalized
            : null;
    }

    private static string Signature(IEnumerable<string> recordIds) =>
        string.Join("\n", recordIds.OrderBy(value => value, StringComparer.Ordinal));

    private static string SetMemberKey(CrossRegionSetMember member) =>
        SetMemberKey(member.Language, member.SetId);

    private static string SetMemberKey(string language, string setId) =>
        Key(language) + "|" + Key(setId);

    private static string Key(params string[] values) =>
        string.Join("|", values.Select(value => value?.Trim().ToLowerInvariant() ?? string.Empty));

    private static string Hash(string value)
    {
        using SHA256 sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty))
            .Select(valueByte => valueByte.ToString("x2")));
    }

    private sealed class Candidate
    {
        public Candidate(IEnumerable<string> recordIds)
        {
            RecordIds = recordIds.Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal).ToList();
        }

        public List<string> RecordIds { get; }
        public HashSet<string> Signals { get; } = new HashSet<string>(StringComparer.Ordinal);
        public MultilingualCardIdentityOverride Override { get; set; }
    }
}
