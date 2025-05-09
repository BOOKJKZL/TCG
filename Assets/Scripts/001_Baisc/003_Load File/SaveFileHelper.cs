using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class SaveFileHelper
{
    public static void WriteFile<T>(T data) where T : ScriptableObject
    {
        string directoryPath = Path.Combine(Application.persistentDataPath, "SaveFile");

        try
        {
            // Ensure directory exists
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            string filePath = Path.Combine(directoryPath, typeof(T).Name + ".json");
            string json = JsonUtility.ToJson(data);
            File.WriteAllText(filePath, json);
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.LogError("UnauthorizedAccessException: " + ex.Message);
            Debug.LogError("Permission denied. Make sure you have appropriate permissions.");
        }
        catch (Exception ex)
        {
            Debug.LogError("Failed to write file: " + ex.Message);
        }
    }

    public static T ReadFile<T>() where T : ScriptableObject
    {
        string directoryPath = Path.Combine(Application.persistentDataPath, "SaveFile");
        string filePath = Path.Combine(directoryPath, typeof(T).Name + ".json");

        if (File.Exists(filePath))
        {
            // Read the JSON data from file
            string json = File.ReadAllText(filePath);

            // Create an instance of T (ScriptableObject)
            T loadedData = ScriptableObject.CreateInstance<T>();

            // Deserialize JSON into the existing instance of T
            JsonUtility.FromJsonOverwrite(json, loadedData);

            return loadedData;
        }
        else
        {
            Debug.LogWarning("File not found: " + filePath);
            return null; // Return default settings if no file exists
        }
    }

    public static T ReadFileFirstTime<T>(T orignal) where T : ScriptableObject
    {
        T data = ReadFile<T>();

        if (data != null)
        {
            return data;
        }
        else
        {
            WriteFile<T>(orignal);
            return orignal;
        }
    }
}