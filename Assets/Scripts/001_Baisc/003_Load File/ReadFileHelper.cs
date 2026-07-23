using System.Collections;
using System.Collections.Generic;
using Gacha.Application;
using UnityEngine;

public enum LangFileType { Image, Text, Voice, Story, Character }
public enum NlFileType { Image, Sound, Unit, Character, Item, Servant, Background }

public static class ReadFileHelper
{
    public static string getLangPath(LangFileType type)
    {
        string languageId = ApplicationServices.IsConfigured
            ? ApplicationServices.Languages.UiLanguageId
            : "en";
        return "Localization/" + type + "/" + languageId + "/";
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
