using UnityEngine;
using System.IO;
using System.Text;

public static class LocalSaveService
{
    private const string FileName = "save.json";
    private const string TemporarySuffix = ".tmp";
    private const string BackupSuffix = ".backup";

    public static void Save(InventoryData data)
    {
        if (data == null)
            return;

        string json = JsonUtility.ToJson(data.ToSnapshot());
        string path = Path.Combine(Application.persistentDataPath, FileName);
        WriteAtomic(path, json);
    }

    public static InventoryData Load()
    {
        string path = Path.Combine(Application.persistentDataPath, FileName);
        RecoverInterruptedWrite(path);
        if (!File.Exists(path))
            return new InventoryData();
        try
        {
            string json = File.ReadAllText(path, Encoding.UTF8);
            // Version 1 used Dictionary fields, which JsonUtility could not persist.
            // Its scalar Gold field can still be recovered during migration.
            if (!json.Contains("\"Version\""))
            {
                LegacyInventoryData legacy = JsonUtility.FromJson<LegacyInventoryData>(json);
                return new InventoryData { Gold = legacy != null ? legacy.Gold : 0 };
            }

            InventorySnapshot snapshot = JsonUtility.FromJson<InventorySnapshot>(json);
            return InventoryData.FromSnapshot(snapshot);
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"Local save could not be read. A new save will be used. {exception.Message}");
            return new InventoryData();
        }
    }

    internal static void WriteAtomic(string path, string text)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new System.ArgumentException("A save path is required.", nameof(path));
        string directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        string temporaryPath = path + TemporarySuffix;
        string backupPath = path + BackupSuffix;
        File.WriteAllText(temporaryPath, text ?? string.Empty, new UTF8Encoding(false));
        if (!File.Exists(path))
        {
            File.Move(temporaryPath, path);
            return;
        }

        if (File.Exists(backupPath)) File.Delete(backupPath);
        File.Move(path, backupPath);
        try
        {
            File.Move(temporaryPath, path);
            File.Delete(backupPath);
        }
        catch
        {
            if (!File.Exists(path) && File.Exists(backupPath))
                File.Move(backupPath, path);
            throw;
        }
    }

    private static void RecoverInterruptedWrite(string path)
    {
        string temporaryPath = path + TemporarySuffix;
        string backupPath = path + BackupSuffix;
        if (!File.Exists(path) && File.Exists(backupPath))
            File.Move(backupPath, path);
        else if (File.Exists(path) && File.Exists(backupPath))
            File.Delete(backupPath);
        if (File.Exists(temporaryPath))
            File.Delete(temporaryPath);
    }

    [System.Serializable]
    private sealed class LegacyInventoryData
    {
        public int Gold = 0;
    }

}
