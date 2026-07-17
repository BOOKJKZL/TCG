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
        public const int SupportedSchemaVersion = 1;

        public IReadOnlyList<PrivateContentManifestDocument> LoadDirectory(string contentRoot)
        {
            if (string.IsNullOrWhiteSpace(contentRoot))
                throw new ArgumentException("Content root cannot be empty.", nameof(contentRoot));
            if (!Directory.Exists(contentRoot))
                throw new DirectoryNotFoundException($"Content directory was not found: {contentRoot}");

            string[] manifestPaths = Directory
                .GetFiles(contentRoot, "manifest.json", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (manifestPaths.Length == 0)
                throw new PrivateContentManifestException($"No manifest.json files were found under: {contentRoot}");

            List<PrivateContentManifestDocument> documents = new List<PrivateContentManifestDocument>(manifestPaths.Length);
            foreach (string path in manifestPaths)
                documents.Add(LoadFile(path));
            return documents;
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
                if (manifest.SchemaVersion != SupportedSchemaVersion)
                    throw new PrivateContentManifestException(
                        $"Manifest '{manifestPath}' uses schema {manifest.SchemaVersion}; supported schema is {SupportedSchemaVersion}.");
                if (manifest.Set == null || string.IsNullOrWhiteSpace(manifest.Set.Id))
                    throw new PrivateContentManifestException($"Manifest has no valid set: {manifestPath}");
                if (string.IsNullOrWhiteSpace(manifest.Language))
                    throw new PrivateContentManifestException($"Manifest has no language: {manifestPath}");

                manifest.Cards ??= new List<ImportedCardDto>();
                manifest.Errors ??= new List<ContentImportErrorDto>();
                return new PrivateContentManifestDocument(Path.GetFullPath(manifestPath), manifest);
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
    }
}
