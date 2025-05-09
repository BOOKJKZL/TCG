using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum LangFileType { Image, Text, Voice, Story, Character }
public enum NlFileType { Image, Sound, Unit, Character, Item, Servant, Background }

public static class ReadFileHelper
{
    public static string getLangPath(LangFileType type)
    {
        return "Localization/" + type.ToString() + "/" + SettingManager.Instance.settingData.langMode.ToString() + "/";
    }

    public static string getNlPath(NlFileType type)
    {
        return "Normal/" + type.ToString() + "/";
    }

    public static T ReadJsonFile<T>(string path) where T : class
    {
        TextAsset jsonTextAsset = Resources.Load<TextAsset>(path); 

        T fileData = JsonUtility.FromJson<T>(jsonTextAsset.text);

        return fileData;
    }
}
