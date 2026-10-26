using System.Collections;
using Gacha.Application;
using Gacha.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.TestTools;

namespace Gacha.Tests.PlayMode
{
    public sealed class CardUiTextPlayModeTests
    {
        [UnityTest]
        public IEnumerator CompletedLocaleTablesRefreshHomeWithoutClearingPendingNavigation()
        {
            yield return LocalizationSettings.InitializationOperation;
            var previous = LocalizationSettings.SelectedLocaleAsync.Result;
            var host = new GameObject("Delayed localized home fixture");
            var settingsHost = new GameObject("Delayed localized settings fixture");
            MobileHomePresenter page = null;
            MobileSettingsPresenter settingsPage = null;
            try
            {
                page = new MobileHomePresenter(host, () => { }, () => { }, () => { }, () => { });
                page.SetNavigationPending(MobileDestination.Content);
                settingsPage = new MobileSettingsPresenter(settingsHost, new MobileSettingsCallbacks
                {
                    CycleUiLanguage = () => { }, CycleCardLanguage = () => { }, ToggleSound = () => { },
                    ToggleReduceMotion = () => { }, ToggleHaptics = () => { }, CycleAnimationSpeed = () => { },
                    ToggleWifiOnly = () => { }, ExportSave = () => { }, ChooseImport = () => { },
                    ConfirmImport = () => { }, ExportDiagnostics = () => { }, ClearDiagnostics = () => { },
                    ConnectIdentity = () => { }, KeepLocal = () => { }, UseCloud = () => { },
                    SafeMerge = () => { }, Back = () => { }, Navigate = _ => { }
                });
                settingsPage.RefreshText("en", "ja", new ExperienceSettings(), true);
                settingsPage.SetStatus(settingsPage.RecoveryStatus, "Retained recovery result", true);
                foreach (string code in new[] { "zh", "ja", "en" })
                {
                    var locale = LocalizationSettings.AvailableLocales.GetLocale(code);
                    Assert.That(locale, Is.Not.Null);
                    LocalizationSettings.SelectedLocale = locale;
                    string immediate = CardUiText.Get("home.title");
                    Assert.That(immediate, Is.Not.Empty, "A page always has usable text while the table loads.");
                    var table = LocalizationSettings.StringDatabase.GetTableAsync(CardUiText.TableName, locale);
                    yield return table;
                    string expectedTitle = table.Result.GetEntry("home.title").GetLocalizedString(locale, null, null);
                    string expectedSettingsTitle = table.Result.GetEntry("settings.title").GetLocalizedString(locale, null, null);
                    float deadline = Time.realtimeSinceStartup + 3f;
                    while ((page.Title.text != expectedTitle || settingsPage.TopBar.Title.text != expectedSettingsTitle) &&
                           Time.realtimeSinceStartup < deadline)
                        yield return null;
                    Assert.That(CardUiText.Get("home.title"), Is.EqualTo(expectedTitle));
                    Assert.That(page.Title.text, Is.EqualTo(expectedTitle));
                    Assert.That(page.Body.text, Is.EqualTo(CardUiText.Get("common.navigation.loading")));
                    Assert.That(page.ContentNavigation.IsLoading, Is.True);
                    Assert.That(page.GachaFeatureAction.TryActivate(), Is.False);
                    Assert.That(settingsPage.TopBar.Title.text, Is.EqualTo(expectedSettingsTitle));
                    Assert.That(settingsPage.RecoveryStatus.text, Is.EqualTo("Retained recovery result"));
                }
            }
            finally
            {
                page?.Dispose();
                settingsPage?.Dispose();
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(settingsHost);
                LocalizationSettings.SelectedLocale = previous;
            }
        }
    }
}
