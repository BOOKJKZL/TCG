using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace Gacha.Infrastructure.Content
{
    [Serializable]
    public sealed class PrintingLanguageGroupManifestDto
    {
        public int SchemaVersion = PrintingLanguageGroupManifestReader.SupportedSchemaVersion;
        public string SourceCoverageSnapshotSha256;
        public string SourceIdentitySnapshotSha256;
        public List<PrintingLanguageGroupRecordDto> Groups = new List<PrintingLanguageGroupRecordDto>();
    }

    [Serializable]
    public sealed class PrintingLanguageGroupRecordDto
    {
        public string Id;
        public string MatchMethod;
        public string ReviewStatus;
        public double Confidence;
        public List<string> Evidence = new List<string>();
        public List<PrintingLanguageGroupMemberDto> Members =
            new List<PrintingLanguageGroupMemberDto>();
    }

    [Serializable]
    public sealed class PrintingLanguageGroupMemberDto
    {
        public string Language;
        public string SetId;
        public string CardId;
        public string LocalId;
    }

    public sealed class PrintingLanguageGroupManifestReader
    {
        public const int SupportedSchemaVersion = 1;
        public const string InstallRelativeDirectory = "runtime/printing-language-groups";
        public const string FileName = "printing-language-groups.json";

        public PrintingLanguageGroupManifestDto LoadOptional(string contentRoot)
        {
            if (string.IsNullOrWhiteSpace(contentRoot))
                throw new ArgumentException("Content root cannot be empty.", nameof(contentRoot));

            string path = Path.Combine(
                Path.GetFullPath(contentRoot),
                InstallRelativeDirectory.Replace('/', Path.DirectorySeparatorChar),
                FileName);
            return File.Exists(path) ? LoadFile(path) : null;
        }

        public PrintingLanguageGroupManifestDto LoadFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Language group manifest path cannot be empty.", nameof(path));

            try
            {
                var settings = new JsonSerializerSettings
                {
                    MissingMemberHandling = MissingMemberHandling.Error
                };
                PrintingLanguageGroupManifestDto manifest =
                    JsonConvert.DeserializeObject<PrintingLanguageGroupManifestDto>(
                        File.ReadAllText(path), settings);
                if (manifest == null)
                    throw new PrivateContentManifestException(
                        "Printing language group manifest is empty: " + path);
                Validate(manifest, path);
                return manifest;
            }
            catch (PrivateContentManifestException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is JsonException)
            {
                throw new PrivateContentManifestException(
                    "Failed to read printing language group manifest: " + path,
                    exception);
            }
        }

        private static void Validate(PrintingLanguageGroupManifestDto manifest, string path)
        {
            if (manifest.SchemaVersion != SupportedSchemaVersion)
                throw Invalid(path, $"schema {manifest.SchemaVersion} is unsupported");
            if (!IsSha256(manifest.SourceCoverageSnapshotSha256))
                throw Invalid(path, "coverage snapshot SHA-256 is missing or invalid");
            if (!IsSha256(manifest.SourceIdentitySnapshotSha256))
                throw Invalid(path, "identity snapshot SHA-256 is missing or invalid");

            manifest.SourceCoverageSnapshotSha256 =
                manifest.SourceCoverageSnapshotSha256.Trim().ToLowerInvariant();
            manifest.SourceIdentitySnapshotSha256 =
                manifest.SourceIdentitySnapshotSha256.Trim().ToLowerInvariant();
            manifest.Groups ??= new List<PrintingLanguageGroupRecordDto>();
            var groupIds = new HashSet<string>(StringComparer.Ordinal);
            var claimedMembers = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (PrintingLanguageGroupRecordDto group in manifest.Groups)
            {
                if (group == null)
                    throw Invalid(path, "group collection contains null");
                group.Id = Required(group.Id, path, "group id");
                if (!groupIds.Add(group.Id))
                    throw Invalid(path, $"group id '{group.Id}' is duplicated");
                group.MatchMethod = Required(group.MatchMethod, path,
                    $"group '{group.Id}' match method").ToLowerInvariant();
                if (group.MatchMethod != "source-identity" &&
                    group.MatchMethod != "manual-override")
                    throw Invalid(path,
                        $"group '{group.Id}' has unsupported match method '{group.MatchMethod}'");
                group.ReviewStatus = Required(group.ReviewStatus, path,
                    $"group '{group.Id}' review status").ToLowerInvariant();
                if (group.ReviewStatus != "auto-accepted" && group.ReviewStatus != "reviewed")
                    throw Invalid(path,
                        $"group '{group.Id}' has unsupported review status '{group.ReviewStatus}'");
                if (group.MatchMethod == "manual-override" && group.ReviewStatus != "reviewed")
                    throw Invalid(path,
                        $"manual group '{group.Id}' must have reviewed status");
                if (double.IsNaN(group.Confidence) || double.IsInfinity(group.Confidence) ||
                    group.Confidence < 0d || group.Confidence > 1d)
                    throw Invalid(path, $"group '{group.Id}' confidence must be between zero and one");

                group.Evidence = (group.Evidence ?? new List<string>())
                    .Select(value => Required(value, path, $"group '{group.Id}' evidence"))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToList();
                group.Members ??= new List<PrintingLanguageGroupMemberDto>();
                if (group.Members.Count < 2)
                    throw Invalid(path, $"group '{group.Id}' requires at least two members");
                var languages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (PrintingLanguageGroupMemberDto member in group.Members)
                {
                    if (member == null)
                        throw Invalid(path, $"group '{group.Id}' contains a null member");
                    member.Language = NormalizeLanguage(Required(
                        member.Language, path, $"group '{group.Id}' member language"));
                    member.SetId = Required(member.SetId, path,
                        $"group '{group.Id}' member set id");
                    member.CardId = Required(member.CardId, path,
                        $"group '{group.Id}' member card id");
                    member.LocalId = Required(member.LocalId, path,
                        $"group '{group.Id}' member local id");
                    if (!languages.Add(member.Language))
                        throw Invalid(path,
                            $"group '{group.Id}' repeats language '{member.Language}'");
                    string key = SourceKey(member.Language, member.SetId, member.CardId, member.LocalId);
                    if (claimedMembers.TryGetValue(key, out string previous))
                        throw Invalid(path,
                            $"source card '{key}' belongs to both '{previous}' and '{group.Id}'");
                    claimedMembers.Add(key, group.Id);
                }
                group.Members = group.Members
                    .OrderBy(value => value.Language, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(value => value.SetId, StringComparer.Ordinal)
                    .ThenBy(value => value.CardId, StringComparer.Ordinal)
                    .ThenBy(value => value.LocalId, StringComparer.Ordinal)
                    .ToList();
            }
            manifest.Groups = manifest.Groups.OrderBy(value => value.Id, StringComparer.Ordinal).ToList();
        }

        internal static string SourceKey(string language, string setId, string cardId, string localId) =>
            string.Join("|", new[]
            {
                NormalizeLanguage(language),
                setId?.Trim() ?? string.Empty,
                cardId?.Trim() ?? string.Empty,
                localId?.Trim() ?? string.Empty
            });

        private static string Required(string value, string path, string field)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw Invalid(path, field + " is required");
            return value.Trim();
        }

        private static string NormalizeLanguage(string value) =>
            value.Trim().Replace('_', '-').ToLowerInvariant();

        private static bool IsSha256(string value) =>
            !string.IsNullOrWhiteSpace(value) && value.Trim().Length == 64 &&
            value.Trim().All(Uri.IsHexDigit);

        private static PrivateContentManifestException Invalid(string path, string message) =>
            new PrivateContentManifestException(
                $"Printing language group manifest '{path}' is invalid: {message}.");
    }
}
