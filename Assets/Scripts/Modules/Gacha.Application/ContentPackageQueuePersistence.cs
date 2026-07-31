using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Gacha.Application
{
    public sealed class ContentPackageQueueResumeState
    {
        public const int SupportedSchemaVersion = 1;

        public ContentPackageQueueResumeState(
            int schemaVersion,
            long catalogRevision,
            IEnumerable<string> packageIds)
        {
            SchemaVersion = schemaVersion;
            CatalogRevision = catalogRevision;
            PackageIds = new ReadOnlyCollection<string>((packageIds ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray());
        }

        public int SchemaVersion { get; }
        public long CatalogRevision { get; }
        public IReadOnlyList<string> PackageIds { get; }
    }

    public interface IContentPackageQueueStateStore
    {
        ContentPackageQueueResumeState Load();
        void Save(ContentPackageQueueResumeState state);
        void Clear();
    }
}
