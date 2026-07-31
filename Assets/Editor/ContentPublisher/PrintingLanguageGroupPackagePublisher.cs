using System;
using System.IO;
using Gacha.Application;
using Gacha.Infrastructure.Content;

namespace Gacha.EditorTools.Content
{
    public static class PrintingLanguageGroupPackagePublisher
    {
        public const string PackageId = "pokemon.printing-language-groups";
        public const string Version = "1.0.0";
        public const long Revision = 1;

        public static ContentPackagePublishDefinition BuildDefinition(
            string importRoot,
            ContentPackageMetadata metadata = null)
        {
            if (string.IsNullOrWhiteSpace(importRoot))
                throw new ArgumentException("Import root is required.", nameof(importRoot));
            string sourceDirectory = Path.Combine(
                Path.GetFullPath(importRoot),
                PrintingLanguageGroupManifestReader.InstallRelativeDirectory
                    .Replace('/', Path.DirectorySeparatorChar));
            return new ContentPackagePublishDefinition(
                PackageId,
                sourceDirectory,
                PrintingLanguageGroupManifestReader.InstallRelativeDirectory,
                Revision,
                Version,
                new[] { PrintingLanguageGroupManifestReader.FileName },
                metadata);
        }
    }
}
