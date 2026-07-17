using UnityEngine;
using System.IO;
using System.Text;

public static class LocalSaveService
{
    private const string FileName = "save.json";

    public static void Save(InventoryData data)
    {
        if (data == null)
            return;

        string json = JsonUtility.ToJson(data.ToSnapshot());
        string path = Path.Combine(Application.persistentDataPath, FileName);
        File.WriteAllText(path, json, Encoding.UTF8);
    }

    public static InventoryData Load()
    {
        string path = Path.Combine(Application.persistentDataPath, FileName);
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

    [System.Serializable]
    private sealed class LegacyInventoryData
    {
        public int Gold = 0;
    }

}
