using System;
using System.IO;
using System.Linq;
using System.Text;
using Gacha.Application;
using Newtonsoft.Json;

namespace Gacha.Infrastructure.Content
{
    public sealed class FileSystemContentPackageQueueStateStore : IContentPackageQueueStateStore
    {
        public const int MaximumPackageCount = 4096;
        public const int MaximumStateBytes = 512 * 1024;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        private readonly object gate = new object();
        private readonly string statePath;

        public FileSystemContentPackageQueueStateStore(string statePath)
        {
            if (string.IsNullOrWhiteSpace(statePath))
                throw new ArgumentException("Queue state path cannot be empty.", nameof(statePath));
            this.statePath = Path.GetFullPath(statePath);
        }

        public ContentPackageQueueResumeState Load()
        {
            lock (gate)
            {
                RecoverBackupIfNeeded();
                if (!File.Exists(statePath))
                    return null;
                RejectLink(statePath);
                long length = new FileInfo(statePath).Length;
                if (length <= 0 || length > MaximumStateBytes)
                    throw new InvalidDataException("Saved content queue size is invalid: " + length);
                QueueStateDto dto = JsonConvert.DeserializeObject<QueueStateDto>(
                    StrictUtf8.GetString(File.ReadAllBytes(statePath)));
                if (dto == null)
                    throw new InvalidDataException("Saved content queue is empty.");
                if (dto.PackageIds == null || dto.PackageIds.Length == 0 ||
                    dto.PackageIds.Length > MaximumPackageCount)
                    throw new InvalidDataException("Saved content queue package count is invalid.");
                if (dto.PackageIds.Any(value => !IsSafePackageId(value)))
                    throw new InvalidDataException("Saved content queue contains an invalid package id.");
                return new ContentPackageQueueResumeState(
                    dto.SchemaVersion,
                    dto.CatalogRevision,
                    dto.PackageIds);
            }
        }

        public void Save(ContentPackageQueueResumeState state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            if (state.PackageIds.Count == 0 || state.PackageIds.Count > MaximumPackageCount)
                throw new InvalidDataException("Content queue package count is invalid.");
            if (state.PackageIds.Any(value => !IsSafePackageId(value)))
                throw new InvalidDataException("Content queue contains an invalid package id.");

            lock (gate)
            {
                string directory = Path.GetDirectoryName(statePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                if (File.Exists(statePath))
                    RejectLink(statePath);
                var dto = new QueueStateDto
                {
                    SchemaVersion = state.SchemaVersion,
                    CatalogRevision = state.CatalogRevision,
                    PackageIds = state.PackageIds.ToArray()
                };
                byte[] bytes = StrictUtf8.GetBytes(JsonConvert.SerializeObject(dto));
                if (bytes.Length > MaximumStateBytes)
                    throw new InvalidDataException("Content queue state exceeds the size limit.");
                string temporaryPath = statePath + ".tmp";
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
                File.WriteAllBytes(temporaryPath, bytes);
                try
                {
                    CommitTemporary(temporaryPath);
                }
                finally
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
            }
        }

        public void Clear()
        {
            lock (gate)
            {
                foreach (string path in new[] { statePath, statePath + ".tmp", statePath + ".backup" })
                {
                    if (File.Exists(path))
                        File.Delete(path);
                }
            }
        }

        private void CommitTemporary(string temporaryPath)
        {
            if (!File.Exists(statePath))
            {
                File.Move(temporaryPath, statePath);
                return;
            }
            try
            {
                File.Replace(temporaryPath, statePath, null);
                return;
            }
            catch (PlatformNotSupportedException) { }
            catch (IOException) { }

            string backupPath = statePath + ".backup";
            if (File.Exists(backupPath))
                File.Delete(backupPath);
            File.Move(statePath, backupPath);
            try
            {
                File.Move(temporaryPath, statePath);
                File.Delete(backupPath);
            }
            catch
            {
                if (!File.Exists(statePath) && File.Exists(backupPath))
                    File.Move(backupPath, statePath);
                throw;
            }
        }

        private void RecoverBackupIfNeeded()
        {
            string backupPath = statePath + ".backup";
            if (!File.Exists(backupPath))
                return;
            if (File.Exists(statePath))
                File.Delete(backupPath);
            else
                File.Move(backupPath, statePath);
        }

        private static void RejectLink(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Content queue state cannot be a file link.");
        }

        private static bool IsSafePackageId(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
                return false;
            foreach (char character in value)
            {
                if (!char.IsLetterOrDigit(character) && character != '.' &&
                    character != '-' && character != '_')
                    return false;
            }
            return true;
        }

        private sealed class QueueStateDto
        {
            public int SchemaVersion;
            public long CatalogRevision;
            public string[] PackageIds;
        }
    }
}
