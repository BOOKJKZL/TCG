using System;
using System.IO;
using UnityEngine;

public static class SaveFileHelper
{
    public static void WriteFile<T>(T data) where T : ScriptableObject
    {
        TryWriteFile(data);
    }

    public static bool TryWriteFile<T>(T data) where T : ScriptableObject
    {
        try
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            LocalSaveService.WriteAtomic(FilePath<T>(), JsonUtility.ToJson(data), null);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Could not save {typeof(T).Name}: {ex.Message}");
            return false;
        }
    }

    public static T ReadFile<T>() where T : ScriptableObject
    {
        string path = FilePath<T>();
        return ReadFromPath<T>(path) ?? ReadFromPath<T>(path + ".backup");
    }

    private static T ReadFromPath<T>(string path) where T : ScriptableObject
    {
        if (!File.Exists(path)) return null;
        T data = ScriptableObject.CreateInstance<T>();
        try
        {
            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) throw new InvalidDataException("The file is empty.");
            JsonUtility.FromJsonOverwrite(json, data);
            return data;
        }
        catch (Exception ex)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(data);
            else UnityEngine.Object.DestroyImmediate(data);
            Debug.LogWarning($"Could not read {path}: {ex.Message}");
            return null;
        }
    }

    private static string FilePath<T>() =>
        Path.Combine(Application.persistentDataPath, "SaveFile", typeof(T).Name + ".json");

    public static T ReadFileFirstTime<T>(T orignal) where T : ScriptableObject
    {
        T data = ReadFile<T>();

        if (data != null) return data;
        string path = FilePath<T>();
        if (!File.Exists(path) && !File.Exists(path + ".backup"))
            WriteFile(orignal);
        return orignal;
    }
}
