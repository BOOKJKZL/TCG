using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace Gacha.Infrastructure.Content
{
    public sealed class PrivateContentManifestException : Exception
    {
        public PrivateContentManifestException(string message) : base(message) { }
        public PrivateContentManifestException(string message, Exception innerException) : base(message, innerException) { }
    }

    public sealed class PrivateContentManifestReader
    {
        public const int MinimumSupportedSchemaVersion = 1;
        public const int SupportedSchemaVersion = 2;

        public IReadOnlyList<PrivateContentManifestDocument> LoadDirectory(string contentRoot)
        {
            return LoadDirectory(contentRoot, _ => true);
        }

        public IReadOnlyList<PrivateContentManifestDocument> LoadCardSetDirectory(string contentRoot)
        {
            return LoadDirectory(contentRoot, path => IsCardSetManifestPath(contentRoot, path));
        }

        private IReadOnlyList<PrivateContentManifestDocument> LoadDirectory(
            string contentRoot,
            Func<string, bool> includePath)
        {
            if (string.IsNullOrWhiteSpace(contentRoot))
                throw new ArgumentException("Content root cannot be empty.", nameof(contentRoot));
            Directory.CreateDirectory(contentRoot);

            string[] manifestPaths = Directory
                .GetFiles(contentRoot, "manifest.json", SearchOption.AllDirectories)
                .Where(includePath)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (manifestPaths.Length == 0)
                return Array.Empty<PrivateContentManifestDocument>();

            List<PrivateContentManifestDocument> documents = new List<PrivateContentManifestDocument>(manifestPaths.Length);
            foreach (string path in manifestPaths)
                documents.Add(LoadFile(path));
            return documents;
        }

        private static bool IsCardSetManifestPath(string contentRoot, string manifestPath)
        {
            string relativePath = Path.GetRelativePath(contentRoot, manifestPath)
                .Replace('\\', '/');
            string[] segments = relativePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            return segments.Length == 3 &&
                   string.Equals(segments[2], "manifest.json", StringComparison.OrdinalIgnoreCase);
        }

        public PrivateContentManifestDocument LoadFile(string manifestPath)
        {
            if (string.IsNullOrWhiteSpace(manifestPath))
                throw new ArgumentException("Manifest path cannot be empty.", nameof(manifestPath));

            try
            {
                string json = File.ReadAllText(manifestPath);
                PrivateContentManifestDto manifest = JsonConvert.DeserializeObject<PrivateContentManifestDto>(json);
                if (manifest == null)
                    throw new PrivateContentManifestException($"Manifest is empty: {manifestPath}");
                int sourceSchemaVersion = manifest.SchemaVersion;
                if (sourceSchemaVersion < MinimumSupportedSchemaVersion ||
                    sourceSchemaVersion > SupportedSchemaVersion)
                    throw new PrivateContentManifestException(
                        $"Manifest '{manifestPath}' uses schema {sourceSchemaVersion}; supported schemas are " +
                        $"{MinimumSupportedSchemaVersion}-{SupportedSchemaVersion}.");
                if (manifest.Set == null || string.IsNullOrWhiteSpace(manifest.Set.Id))
                    throw new PrivateContentManifestException($"Manifest has no valid set: {manifestPath}");
                if (string.IsNullOrWhiteSpace(manifest.Language))
                    throw new PrivateContentManifestException($"Manifest has no language: {manifestPath}");

                if (sourceSchemaVersion == 1)
                    MigrateV1ToV2(manifest);
                ValidateV2(manifest, manifestPath);

                manifest.Cards ??= new List<ImportedCardDto>();
                manifest.Errors ??= new List<ContentImportErrorDto>();
                return new PrivateContentManifestDocument(
                    Path.GetFullPath(manifestPath), manifest, sourceSchemaVersion);
            }
            catch (PrivateContentManifestException)
            {
                throw;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                throw new PrivateContentManifestException($"Failed to read manifest: {manifestPath}", exception);
            }
        }

        private static void MigrateV1ToV2(PrivateContentManifestDto manifest)
        {
            ImportedSetDto set = manifest.Set;
            set.SetCode = ValueOrFallback(set.SetCode, set.Id);
            set.EraId = ValueOrFallback(set.EraId, ValueOrFallback(set.SeriesId, set.Id));
            set.GenerationId = ValueOrFallback(set.GenerationId, "unmapped");
            manifest.SchemaVersion = SupportedSchemaVersion;
        }

        private static void ValidateV2(PrivateContentManifestDto manifest, string manifestPath)
        {
            if (manifest.SchemaVersion != SupportedSchemaVersion)
                throw new PrivateContentManifestException($"Manifest migration failed: {manifestPath}");
            if (string.IsNullOrWhiteSpace(manifest.Set.SetCode))
                throw new PrivateContentManifestException($"Manifest set has no stable SetCode: {manifestPath}");
            if (string.IsNullOrWhiteSpace(manifest.Set.EraId))
                throw new PrivateContentManifestException($"Manifest set has no stable EraId: {manifestPath}");
            if (string.IsNullOrWhiteSpace(manifest.Set.GenerationId))
                throw new PrivateContentManifestException($"Manifest set has no GenerationId state: {manifestPath}");
            if (manifest.Set.GenerationOrder < 0)
                throw new PrivateContentManifestException($"Manifest set has a negative GenerationOrder: {manifestPath}");
            if (manifest.Set.SetOrdinal < 0)
                throw new PrivateContentManifestException($"Manifest set has a negative SetOrdinal: {manifestPath}");
        }

        private static string ValueOrFallback(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }
    }
}
