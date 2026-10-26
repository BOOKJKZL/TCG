using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Gacha.Application;
using Gacha.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.TestTools;

namespace Gacha.Tests.PlayMode
{
    // Real presenters and their explicit registrations; callbacks are in-memory.
    // This verifies the program-side path, not TalkBack speech or touch exploration.
    public sealed class MobileAccessibilityPageFlowPlayModeTests
    {
        private static MobileAccessibilitySession Session =>
            (MobileAccessibilitySession)typeof(MobileAccessibility)
                .GetField("session", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);

        [UnityTest]
        public IEnumerator StartSemanticsRespectPendingAndKeepIdentityAcrossTextChanges()
        {
            yield return LocalizationSettings.InitializationOperation;
            var host = new GameObject("Accessible Start Presenter");
            MobileStartPresenter page = null;
            int starts = 0;
            try
            {
                page = new MobileStartPresenter(host, () => starts++);
                yield return null;
                yield return null;
                MobileAccessibility.Refresh();
                Assert.That(Session.Model.ActiveScreenId, Is.EqualTo("start"));
                Assert.That(Session.Model.Nodes.Any(node => node.Id == "start/start"), Is.True);
                var nativeId = Session.Native.FindNode("start/start").id;
                Session.Focus("start/start");
                Assert.That(starts, Is.Zero, "Screen-reader focus must never invoke a game action.");
                page.SetPending(true);
                MobileAccessibility.Refresh();
                var pending = Session.Model.Nodes.Single(node => node.Id == "start/start");
                Assert.That(pending.IsLoading, Is.True);
                Assert.That(pending.CanActivate, Is.False);
                Assert.That(Session.TryActivate("start/start"), Is.False);
                Assert.That(starts, Is.Zero);
                page.SetFailed();
                page.Title.text = "新しいタイトル / 新标题";
                MobileAccessibility.Refresh();
                Assert.That(Session.Model.Nodes.Single(node => node.Id == "start/title").Label,
                    Is.EqualTo(page.Title.text));
                Assert.That(Session.Native.FindNode("start/start").id, Is.EqualTo(nativeId));
                Assert.That(Session.TryActivate("start/start"), Is.True);
                Assert.That(starts, Is.EqualTo(1));
                Assert.That(Session.Model.Nodes.Select(node => node.Id).Distinct().Count(),
                    Is.EqualTo(Session.Model.Nodes.Count));
                page.Dispose();
                page = null;
                MobileAccessibility.Refresh();
                Assert.That(Session.TryActivate("start/start"), Is.False);
            }
            finally
            {
                page?.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [UnityTest]
        public IEnumerator HomeToSettingsAndConfirmationPreserveSemanticOwnershipAndSafeActions()
        {
            yield return LocalizationSettings.InitializationOperation;
            Locale originalLocale = LocalizationSettings.SelectedLocale;
            var homeHost = new GameObject("Accessible Home Presenter");
            var settingsHost = new GameObject("Accessible Settings Presenter");
            MobileHomePresenter home = null;
            MobileSettingsPresenter settings = null;
            int settingsRequests = 0;
            int languageRequests = 0;
            int destructiveRequests = 0;
            try
            {
                home = new MobileHomePresenter(homeHost, () => { }, () => { }, () => { }, () => settingsRequests++);
                yield return null;
                yield return null;
                MobileAccessibility.Refresh();
                Assert.That(Session.TryActivate("home/feature/settings"), Is.True);
                Assert.That(settingsRequests, Is.EqualTo(1));
                settings = new MobileSettingsPresenter(settingsHost, Callbacks(() => languageRequests++));
                settings.RefreshText("en", "ja", new ExperienceSettings(), true);
                yield return null;
                yield return null;
                MobileAccessibility.Refresh();
                Assert.That(Session.Model.ActiveScreenId, Is.EqualTo("settings"));
                Assert.That(Session.Model.Nodes.All(node => node.Id.StartsWith("settings/", StringComparison.Ordinal)), Is.True);
                home.Dispose();
                home = null;
                MobileAccessibility.Refresh();
                Assert.That(Session.Model.ActiveScreenId, Is.EqualTo("settings"),
                    "Late disposal of the source must not clear the new page's semantics.");

                string actionId = "settings/language/ui-cycle";
                Session.Focus(actionId);
                var nativeId = Session.Native.FindNode(actionId).id;
                foreach (string language in new[] { "en", "zh", "ja" })
                {
                    LocalizationSettings.SelectedLocale = LocalizationSettings.AvailableLocales.GetLocale(language);
                    settings.RefreshText(language, "ja", new ExperienceSettings(), true);
                    yield return null;
                    MobileAccessibility.Refresh();
                    Assert.That(Session.Model.Nodes.Single(node => node.Id == actionId).Label,
                        Is.EqualTo(settings.UiLanguageAction.Label.text));
                    Assert.That(Session.Native.FindNode(actionId).id, Is.EqualTo(nativeId));
                }
                Assert.That(languageRequests, Is.Zero);
                settings.Confirmation.Show("Confirm recovery", "Only test data", "Restore", "Cancel",
                    () => destructiveRequests++, destructive: true);
                yield return null;
                yield return null;
                MobileAccessibility.Refresh();
                Assert.That(Session.Model.ActiveScopeId, Is.EqualTo("settings/confirmation"));
                Assert.That(Session.Model.Nodes.All(node => node.Id.StartsWith("settings/confirmation/", StringComparison.Ordinal)), Is.True);
                Assert.That(Session.TryActivate(actionId), Is.False);
                Assert.That(Session.DismissModal(), Is.True);
                // RequestedFocusId is the one-shot projection notification. Observe
                // it synchronously before the next scheduled refresh consumes it.
                MobileAccessibility.Refresh();
                Assert.That(destructiveRequests, Is.Zero);
                Assert.That(Session.Model.ActiveScopeId, Is.EqualTo("settings"));
                Assert.That(Session.Model.RequestedFocusId, Is.EqualTo(actionId));
                Assert.That(Session.TryActivate(actionId), Is.True);
                Assert.That(languageRequests, Is.EqualTo(1));
            }
            finally
            {
                settings?.Dispose();
                home?.Dispose();
                LocalizationSettings.SelectedLocale = originalLocale;
                UnityEngine.Object.DestroyImmediate(settingsHost);
                UnityEngine.Object.DestroyImmediate(homeHost);
            }
        }

        private static MobileSettingsCallbacks Callbacks(Action language) => new MobileSettingsCallbacks
        {
            CycleUiLanguage = language, CycleCardLanguage = () => { }, ToggleSound = () => { },
            ToggleReduceMotion = () => { }, ToggleHaptics = () => { }, CycleAnimationSpeed = () => { },
            ToggleWifiOnly = () => { }, ExportSave = () => { }, ChooseImport = () => { },
            ConfirmImport = () => { }, ExportDiagnostics = () => { }, ClearDiagnostics = () => { },
            ConnectIdentity = () => { }, KeepLocal = () => { }, UseCloud = () => { },
            SafeMerge = () => { }, Navigate = _ => { }, Back = () => { }
        };
    }
}
