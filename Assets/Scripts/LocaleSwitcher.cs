using UnityEngine;
using UnityEngine.Localization.Settings;

public static class LocaleSwitcher { 
    public static void SetLocale(string code) { 
        var locale = LocalizationSettings.AvailableLocales.GetLocale(code); 
        if (locale != null) 
            LocalizationSettings.SelectedLocale = locale; 
    } 
}
