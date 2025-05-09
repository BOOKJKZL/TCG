using UnityEngine;
using System.IO;
using System.Text;

public static class LocalSaveService
{
    private const string FileName = "save.json";

    public static void Save(InventoryData data)
    {
        string json = JsonUtility.ToJson(data);
        string path = Path.Combine(Application.persistentDataPath, FileName);
        File.WriteAllText(path, json, Encoding.UTF8);
    }

    public static InventoryData Load()
    {
        string path = Path.Combine(Application.persistentDataPath, FileName);
        if (!File.Exists(path))
            return new InventoryData();
        string json = File.ReadAllText(path, Encoding.UTF8);
        return JsonUtility.FromJson<InventoryData>(json);
    }

}