using Gacha.Application;

public static class LocaleSwitcher
{
    public static void SetLocale(string code)
    {
        GameApplicationBootstrap.EnsureConfigured();
        ApplicationServices.Languages.SelectUiLanguage(code);
    }
}
