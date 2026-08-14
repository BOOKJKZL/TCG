using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Gacha.Application;
using Gacha.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using UnityEngine.Video;

namespace Gacha.Tests.PlayMode
{
    public sealed class LanguageSettingsPanelPlayModeTests
    {
        private readonly List<string> unexpectedHeadlessErrors = new List<string>();
        private bool captureHeadlessErrors;
        private Scene sceneToUnload;

        [SetUp]
        public void SetUpHeadlessLogIsolation()
        {
            unexpectedHeadlessErrors.Clear();
            captureHeadlessErrors = Environment.GetCommandLineArgs().Any(argument =>
                string.Equals(argument, "-nographics", StringComparison.OrdinalIgnoreCase));
            if (!captureHeadlessErrors)
                return;
            LogAssert.ignoreFailingMessages = true;
            UnityEngine.Application.logMessageReceived += CaptureHeadlessError;
        }

        [TearDown]
        public void TearDownHeadlessLogIsolation()
        {
            if (!captureHeadlessErrors)
                return;
            UnityEngine.Application.logMessageReceived -= CaptureHeadlessError;
            LogAssert.ignoreFailingMessages = false;
            captureHeadlessErrors = false;
            Assert.That(unexpectedHeadlessErrors, Is.Empty,
                "Unexpected errors:\n" + string.Join("\n", unexpectedHeadlessErrors));
        }

        [UnityTearDown]
        public IEnumerator UnloadSettingsSceneAfterFailure()
        {
            if (!sceneToUnload.IsValid() || !sceneToUnload.isLoaded)
                yield break;
            Scene cleanup = SceneManager.CreateScene("Settings Test Cleanup");
            Assert.That(SceneManager.SetActiveScene(cleanup), Is.True);
            yield return SceneManager.UnloadSceneAsync(sceneToUnload);
            yield return null;
            yield return null;
            sceneToUnload = default;
        }

        [UnityTest]
        public IEnumerator SettingsScene_UsesMobileShellAndKeepsPreferencesIndependent()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("005_SettingScene", LoadSceneMode.Single);
            yield return load;
            yield return null;
            yield return null;
            DisableHeadlessVideo();
            yield return LocalizationSettings.InitializationOperation;

            MonoBehaviour controller = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None)
                .SingleOrDefault(component => component.GetType().Name == "MainMenuBackController");
            Assert.That(controller, Is.Not.Null);
            MobileSettingsPresenter page = controller.GetType().GetProperty("SettingsPresenter")
                ?.GetValue(controller) as MobileSettingsPresenter;
            Assert.That(page, Is.Not.Null);
            Assert.That(page.Document.rootVisualElement.name, Is.EqualTo("mobile-settings-document"));
            Assert.That(page.Shell.SafeArea.ClassListContains("safe-area-bound"), Is.True);
            Assert.That(page.Document.rootVisualElement.Query<UnityEngine.UIElements.Button>().ToList(), Is.Empty);
            Assert.That(page.PrimaryNavigation.Count, Is.EqualTo(5));
            Assert.That(page.PrimaryNavigation.GetAction(MobileDestination.Settings).IsSelected, Is.True);
            Assert.That(UnityEngine.Object.FindFirstObjectByType<LanguageSettingsPanel>(FindObjectsInactive.Include), Is.Null);
            Assert.That(UnityEngine.Object.FindFirstObjectByType<ExperienceSettingsPanel>(FindObjectsInactive.Include), Is.Null);
            Assert.That(UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None)
                .Any(component => component.GetType().Name == "SaveRecoverySettingsPanel"), Is.False);
            Assert.That(UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None)
                .Any(component => component.GetType().Name == "CloudConflictSettingsDialog"), Is.False);
            Assert.That(controller.gameObject.scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Canvas>(true))
                .All(canvas => !canvas.gameObject.activeInHierarchy), Is.True);

            LanguageSelectionService languages = ApplicationServices.Languages;
            ExperienceSettingsService experience = ApplicationServices.ExperienceSettings;
            ContentDownloadPolicyService download = ApplicationServices.ContentDownloadPolicy;
            Scene settingsScene = controller.gameObject.scene;
            sceneToUnload = settingsScene;
            string originalUi = languages.UiLanguageId;
            string originalCard = languages.RequestedContentLanguageId;
            ExperienceSettings originalExperience = experience.Current;
            bool originalWifi = download.Current.WifiOnlyForLargeDownloads;
            try
            {
                languages.SelectUiLanguage("en");
                languages.SelectContentLanguage("en", null);
                yield return null;
                string cardBeforeUiTap = languages.RequestedContentLanguageId;
                Tap(page.UiLanguageAction);
                yield return WaitFor(() => languages.UiLanguageId == "zh");
                Assert.That(languages.RequestedContentLanguageId, Is.EqualTo(cardBeforeUiTap));
                Assert.That(page.TopBar.Title.text, Is.EqualTo(CardUiText.Get("settings.title")));

                string uiBeforeCardTap = languages.UiLanguageId;
                Tap(page.CardLanguageAction);
                yield return null;
                Assert.That(languages.RequestedContentLanguageId, Is.EqualTo("zh-cn"));
                Assert.That(languages.UiLanguageId, Is.EqualTo(uiBeforeCardTap));

                bool soundBefore = experience.Current.SoundEnabled;
                yield return ScrollToAndTap(page, page.SoundAction);
                Assert.That(experience.Current.SoundEnabled, Is.Not.EqualTo(soundBefore));
                bool motionBefore = experience.Current.ReduceMotion;
                yield return ScrollToAndTap(page, page.MotionAction);
                Assert.That(experience.Current.ReduceMotion, Is.Not.EqualTo(motionBefore));
                bool hapticsBefore = experience.Current.HapticsEnabled;
                yield return ScrollToAndTap(page, page.HapticsAction);
                Assert.That(experience.Current.HapticsEnabled, Is.Not.EqualTo(hapticsBefore));
                float speedBefore = experience.Current.AnimationSpeed;
                yield return ScrollToAndTap(page, page.SpeedAction);
                Assert.That(experience.Current.AnimationSpeed, Is.Not.EqualTo(speedBefore));

                yield return ScrollToAndTap(page, page.WifiOnlyAction);
                Assert.That(download.Current.WifiOnlyForLargeDownloads, Is.Not.EqualTo(originalWifi));
                Assert.That(page.IdentityAction.IsEnabled, Is.False,
                    "PLAYER ID must remain disabled while external setup is absent.");

                var routes = new List<string>();
                FieldInfo overrideField = controller.GetType().GetField(
                    "sceneLoaderOverrideForTests",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(overrideField, Is.Not.Null);
                overrideField.SetValue(controller, new Action<string>(routes.Add));
                Tap(page.PrimaryNavigation.GetAction(MobileDestination.Gacha));
                Tap(page.PrimaryNavigation.GetAction(MobileDestination.Collection));
                Assert.That(routes, Is.EqualTo(new[] { "003_GachaScene" }));
                Assert.That(page.PrimaryNavigation.GetAction(MobileDestination.Gacha).IsSelected, Is.True);
                Assert.That(page.PrimaryNavigation.GetAction(MobileDestination.Collection).IsEnabled, Is.False);
            }
            finally
            {
                languages.SelectUiLanguage(originalUi);
                languages.SelectContentLanguage(originalCard, null);
                experience.SetSoundEnabled(originalExperience.SoundEnabled);
                experience.SetReduceMotion(originalExperience.ReduceMotion);
                experience.SetHapticsEnabled(originalExperience.HapticsEnabled);
                experience.SetAnimationSpeed(originalExperience.AnimationSpeed);
                download.SetWifiOnlyForLargeDownloads(originalWifi);
            }

            Scene cleanup = SceneManager.CreateScene("Settings PlayMode Cleanup");
            Assert.That(SceneManager.SetActiveScene(cleanup), Is.True);
            yield return SceneManager.UnloadSceneAsync(settingsScene);
            yield return null;
            yield return null;
            sceneToUnload = default;
        }

        [UnityTest]
        public IEnumerator SettingsRecoveryIdentityAndCloud_UseOneShotSafeOperationStates()
        {
            const string secret = @"C:\Users\secret\save.gachasave|PRIVATE_EXCEPTION";
            AsyncOperation load = SceneManager.LoadSceneAsync("005_SettingScene", LoadSceneMode.Single);
            yield return load;
            yield return null;
            yield return LocalizationSettings.InitializationOperation;

            MonoBehaviour controller = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None)
                .Single(component => component.GetType().Name == "MainMenuBackController");
            MobileSettingsPresenter page = controller.GetType().GetProperty("SettingsPresenter")
                ?.GetValue(controller) as MobileSettingsPresenter;
            Assert.That(page, Is.Not.Null);
            Scene settingsScene = controller.gameObject.scene;
            sceneToUnload = settingsScene;

            int exportCalls = 0;
            int importPickerCalls = 0;
            int restoreCalls = 0;
            int cancelPendingCalls = 0;
            int identityCalls = 0;
            int cloudCalls = 0;
            Action<MobileSettingsOperationResult> exportCompletion = null;
            Action<MobileSettingsOperationResult> importCompletion = null;
            MobileSettingsIdentityState identityState = MobileSettingsIdentityState.Available;
            MobileSettingsIdentityResultData identityResult =
                new MobileSettingsIdentityResultData(MobileSettingsIdentityOutcome.Failed, secret);
            MobileSettingsCloudStateData cloudState = new MobileSettingsCloudStateData(false, false);
            var operations = new MobileSettingsOperationOverrides
            {
                RecoveryAvailable = () => true,
                PickerBusy = () => false,
                ExportRecovery = completed =>
                {
                    exportCalls++;
                    exportCompletion = completed;
                },
                ChooseImport = completed =>
                {
                    importPickerCalls++;
                    importCompletion = completed;
                },
                PreviewRecovery = _ => new MobileSettingsRecoveryPreviewData(
                    DateTime.UtcNow,
                    3,
                    9,
                    2,
                    1,
                    "ja",
                    "zh-cn"),
                RestoreRecovery = _ =>
                {
                    restoreCalls++;
                    return true;
                },
                CancelPendingPicker = () => cancelPendingCalls++,
                IdentityStatus = () => new MobileSettingsIdentityStatusData(identityState, "p***@example.com"),
                ConnectIdentity = () =>
                {
                    identityCalls++;
                    return Task.FromResult(identityResult);
                },
                CloudState = () => cloudState,
                ResolveCloud = choice =>
                {
                    cloudCalls++;
                    cloudState = new MobileSettingsCloudStateData(false, false);
                    return Task.FromResult(new MobileSettingsOperationResult(true));
                }
            };
            SetPrivateField(controller, "operationOverridesForTests", operations);
            InvokePrivate(controller, "RefreshAll");
            yield return null;

                yield return ScrollToAndTap(page, page.ExportAction);
                Tap(page.ExportAction);
                Assert.That(exportCalls, Is.EqualTo(1), "Busy export must reject duplicate taps.");
                exportCompletion(new MobileSettingsOperationResult(false, developerDetail: secret));
                yield return null;
                AssertPlayerTextDoesNotContain(page, secret);

                yield return ScrollToAndTap(page, page.ExportAction);
                exportCompletion(new MobileSettingsOperationResult(false, cancelled: true));
                yield return null;
                Assert.That(page.RecoveryStatus.text, Is.EqualTo(CardUiText.Get("settings.recovery.status.cancelled")));

                yield return ScrollToAndTap(page, page.ExportAction);
                exportCompletion(new MobileSettingsOperationResult(true, path: secret));
                yield return null;
                Assert.That(page.RecoveryStatus.text, Is.EqualTo(CardUiText.Get("settings.recovery.status.exported_safe")));
                AssertPlayerTextDoesNotContain(page, secret);

                yield return ScrollToAndTap(page, page.ImportAction);
                Tap(page.ImportAction);
                Assert.That(importPickerCalls, Is.EqualTo(1));
                importCompletion(new MobileSettingsOperationResult(true, path: secret));
                yield return null;
                Assert.That(controller.GetType().GetProperty("HasPendingImport")?.GetValue(controller), Is.True);
                AssertPlayerTextDoesNotContain(page, secret);

                yield return ScrollToAndTap(page, page.ConfirmImportAction);
                Assert.That(page.Confirmation.IsVisible, Is.True);
                Tap(page.Confirmation.Cancel);
                Tap(page.Confirmation.Cancel);
                Assert.That(restoreCalls, Is.Zero);
                Assert.That(controller.GetType().GetProperty("HasPendingImport")?.GetValue(controller), Is.True);

                yield return ScrollToAndTap(page, page.ConfirmImportAction);
                Tap(page.Confirmation.DangerConfirm);
                Tap(page.Confirmation.DangerConfirm);
                Assert.That(restoreCalls, Is.EqualTo(1), "Import confirmation must be one-shot.");
                Assert.That(controller.GetType().GetProperty("HasPendingImport")?.GetValue(controller), Is.False);

                foreach (MobileSettingsIdentityOutcome outcome in Enum.GetValues(typeof(MobileSettingsIdentityOutcome)))
                {
                    identityState = MobileSettingsIdentityState.Available;
                    identityResult = new MobileSettingsIdentityResultData(outcome, secret);
                    InvokePrivate(controller, "RefreshAll");
                    yield return ScrollToAndTap(page, page.IdentityAction);
                    yield return null;
                    AssertPlayerTextDoesNotContain(page, secret);
                }
                operations.ConnectIdentity = () =>
                {
                    identityCalls++;
                    return Task.FromException<MobileSettingsIdentityResultData>(new InvalidOperationException(secret));
                };
                identityState = MobileSettingsIdentityState.Available;
                InvokePrivate(controller, "RefreshAll");
                yield return ScrollToAndTap(page, page.IdentityAction);
                yield return null;
                AssertPlayerTextDoesNotContain(page, secret);
                Assert.That(identityCalls, Is.EqualTo(Enum.GetValues(typeof(MobileSettingsIdentityOutcome)).Length + 1));

                foreach (MobileSettingsCloudChoice choice in Enum.GetValues(typeof(MobileSettingsCloudChoice)))
                {
                    cloudState = PendingCloudState();
                    InvokePrivate(controller, "RefreshAll");
                    MobileActionControl action = CloudAction(page, choice);
                    Assert.That(action.IsEnabled, Is.True, choice + " should be available for a pending conflict.");
                    page.Scroll.ScrollTo(action.Root);
                    yield return null;
                    yield return null;
                    Assert.That(page.CloudCard.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex), choice.ToString());
                    Assert.That(action.Root.worldBound.height, Is.GreaterThan(1f), choice.ToString());
                    SendKeyboardActivate(action.Root);
                    yield return null;
                    Assert.That(page.Confirmation.IsVisible, Is.True, choice.ToString());
                    MobileActionControl confirm = choice == MobileSettingsCloudChoice.UseCloud
                        ? page.Confirmation.DangerConfirm
                        : page.Confirmation.Confirm;
                    Tap(confirm);
                    Tap(confirm);
                    yield return null;
                }
                Assert.That(cloudCalls, Is.EqualTo(3), "Each cloud choice must resolve exactly once.");

                operations.ResolveCloud = _ =>
                    Task.FromException<MobileSettingsOperationResult>(new InvalidOperationException(secret));
                cloudState = PendingCloudState();
                InvokePrivate(controller, "RefreshAll");
                page.Scroll.ScrollTo(page.KeepLocalAction.Root);
                yield return null;
                yield return null;
                SendKeyboardActivate(page.KeepLocalAction.Root);
                Assert.That(page.Confirmation.IsVisible, Is.True);
                SendKeyboardActivate(page.Confirmation.Confirm.Root);
                yield return null;
                Assert.That(page.CloudStatus.text, Is.EqualTo(CardUiText.Get("settings.cloud.status.failed_safe")));
                AssertPlayerTextDoesNotContain(page, secret);

                exportCompletion = null;
                page.Scroll.ScrollTo(page.ExportAction.Root);
                yield return null;
                yield return null;
                SendKeyboardActivate(page.ExportAction.Root);
                Assert.That(exportCompletion, Is.Not.Null);
                Assert.That(controller.GetType().GetProperty("IsBusy")?.GetValue(controller), Is.True);

                Scene cleanup = SceneManager.CreateScene("Settings Operation Cleanup");
                Assert.That(SceneManager.SetActiveScene(cleanup), Is.True);
                yield return SceneManager.UnloadSceneAsync(settingsScene);
                yield return null;
                yield return null;
                sceneToUnload = default;

                Assert.That(cancelPendingCalls, Is.EqualTo(1),
                    "Destroying a page that owns a picker request must abandon only that request.");
                Assert.DoesNotThrow(() =>
                    exportCompletion(new MobileSettingsOperationResult(false, developerDetail: secret)));
        }

        [UnityTest]
        public IEnumerator Settings_720By1600InsetsKeepScrollableJapaneseActionsAndSheetReachable()
        {
            yield return LocalizationSettings.InitializationOperation;
            Locale originalLocale = LocalizationSettings.SelectedLocale;
            GameObject host = new GameObject("Mobile Settings Contract Host");
            MobileSettingsPresenter page = null;
            try
            {
                page = new MobileSettingsPresenter(host, Callbacks());
                SelectLocale("ja");
                page.RefreshText("ja", "zh-cn", new ExperienceSettings(), true);
                page.SetCloudVisible(true);
                page.CloudLocalSummary.text = "2026-08-14 · 100 印刷版 / 999 枚 · 25 パック · 10 回";
                page.CloudRemoteSummary.text = "2026-08-13 · 98 印刷版 / 980 枚 · 24 パック · 9 回";

                var viewport = new VisualElement { name = "settings-contract-viewport" };
                viewport.style.position = Position.Relative;
                viewport.style.width = 720f;
                viewport.style.height = 1600f;
                page.Document.rootVisualElement.Clear();
                page.Document.rootVisualElement.Add(viewport);
                viewport.Add(page.Shell.Root);
                UiToolkitSafeAreaBinding binding = GetSafeAreaBinding(page.Shell);
                binding.Suspend();
                page.Shell.SafeArea.AddToClassList("mobile-layout--compact");
                page.Shell.SafeArea.style.paddingLeft = 48f;
                page.Shell.SafeArea.style.paddingTop = 60f;
                page.Shell.SafeArea.style.paddingRight = 12f;
                page.Shell.SafeArea.style.paddingBottom = 84f;
                yield return null;
                yield return null;

                Rect safe = InsetRect(page.Shell.SafeArea.worldBound, page.Shell.SafeArea.resolvedStyle);
                AssertContained(safe, page.TopBar.Root.worldBound, "top bar");
                AssertContained(safe, page.PrimaryNavigation.BottomNavigation.Root.worldBound, "bottom navigation");
                foreach (MobileDestination destination in Enum.GetValues(typeof(MobileDestination)))
                {
                    MobileActionControl action = page.PrimaryNavigation.GetAction(destination);
                    Assert.That(action.Root.resolvedStyle.height,
                        Is.GreaterThanOrEqualTo(MobileUiTokens.MinimumTouchTarget - 0.5f), destination.ToString());
                    AssertContained(safe, action.Root.worldBound, destination.ToString());
                }

                page.Scroll.ScrollTo(page.MergeAction.Root);
                yield return null;
                yield return null;
                Assert.That(page.Scroll.contentViewport.worldBound.Contains(page.MergeAction.Root.worldBound.center), Is.True);
                Assert.That(page.MergeAction.Root.resolvedStyle.height,
                    Is.GreaterThanOrEqualTo(MobileUiTokens.MinimumTouchTarget - 0.5f));
                Assert.That(page.CloudDescription.resolvedStyle.whiteSpace, Is.EqualTo(WhiteSpace.Normal));

                page.Confirmation.Show(
                    CardUiText.Get("settings.recovery.confirm.title"),
                    CardUiText.Get("settings.recovery.confirm.body"),
                    CardUiText.Get("settings.recovery.action.confirm"),
                    CardUiText.Get("common.action.cancel"),
                    () => { },
                    null,
                    true);
                yield return null;
                yield return null;
                yield return new WaitForSecondsRealtime(0.25f);
                AssertContained(page.Shell.Root.worldBound, page.Confirmation.Sheet.Panel.worldBound, "confirmation sheet");
                Assert.That(page.Confirmation.DangerConfirm.Root.resolvedStyle.height,
                    Is.GreaterThanOrEqualTo(MobileUiTokens.MinimumTouchTarget - 0.5f));
            }
            finally
            {
                if (originalLocale != null)
                    LocalizationSettings.SelectedLocale = originalLocale;
                page?.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        private static MobileSettingsCallbacks Callbacks() => new MobileSettingsCallbacks
        {
            CycleUiLanguage = () => { }, CycleCardLanguage = () => { }, ToggleSound = () => { },
            ToggleReduceMotion = () => { }, ToggleHaptics = () => { }, CycleAnimationSpeed = () => { },
            ToggleWifiOnly = () => { }, ExportSave = () => { }, ChooseImport = () => { },
            ConfirmImport = () => { }, ConnectIdentity = () => { }, KeepLocal = () => { },
            UseCloud = () => { }, SafeMerge = () => { }, Navigate = _ => { }
        };

        private static MobileSettingsCloudStateData PendingCloudState() => new MobileSettingsCloudStateData(
            true,
            false,
            new MobileSettingsProgressSummaryData(DateTime.UtcNow, 3, 9, 2, 1),
            new MobileSettingsProgressSummaryData(DateTime.UtcNow.AddMinutes(-5), 4, 12, 3, 2));

        private static MobileActionControl CloudAction(
            MobileSettingsPresenter page,
            MobileSettingsCloudChoice choice) => choice switch
        {
            MobileSettingsCloudChoice.KeepLocal => page.KeepLocalAction,
            MobileSettingsCloudChoice.UseCloud => page.UseCloudAction,
            _ => page.MergeAction
        };

        private static void SetPrivateField(MonoBehaviour target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            field.SetValue(target, value);
        }

        private static void InvokePrivate(MonoBehaviour target, string methodName)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, methodName);
            method.Invoke(target, null);
        }

        private static void AssertPlayerTextDoesNotContain(MobileSettingsPresenter page, string sentinel)
        {
            string visibleText = string.Join("\n", page.Document.rootVisualElement.Query<Label>()
                .ToList()
                .Where(label => label.resolvedStyle.display != DisplayStyle.None)
                .Select(label => label.text));
            Assert.That(visibleText, Does.Not.Contain(sentinel));
            Assert.That(visibleText, Does.Not.Contain("C:\\Users\\secret"));
            Assert.That(visibleText, Does.Not.Contain("PRIVATE_EXCEPTION"));
        }

        private static void Tap(MobileActionControl control)
        {
            Vector2 position = control.Root.worldBound.center;
            using (PointerDownEvent down = PointerDownEvent.GetPooled(new Event
                   {
                       type = EventType.MouseDown, button = 0, mousePosition = position
                   }))
            {
                control.Root.SendEvent(down);
            }

            // Focusing a control inside a ScrollView may synchronously scroll it into view.
            // Release at the control's updated position so this remains a real inside tap.
            position = control.Root.worldBound.center;
            using (PointerUpEvent up = PointerUpEvent.GetPooled(new Event
                   {
                       type = EventType.MouseUp, button = 0, mousePosition = position
                   }))
            {
                control.Root.SendEvent(up);
            }
        }

        private static void SendKeyboardActivate(VisualElement control)
        {
            control.Focus();
            using (KeyDownEvent down = KeyDownEvent.GetPooled(new Event
                   {
                       type = EventType.KeyDown,
                       keyCode = KeyCode.Return
                   }))
                control.SendEvent(down);
            using (KeyUpEvent up = KeyUpEvent.GetPooled(new Event
                   {
                       type = EventType.KeyUp,
                       keyCode = KeyCode.Return
                   }))
                control.SendEvent(up);
        }

        private static IEnumerator WaitFor(Func<bool> condition)
        {
            float deadline = Time.realtimeSinceStartup + 3f;
            while (!condition() && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(condition(), Is.True);
        }

        private static IEnumerator ScrollToAndTap(MobileSettingsPresenter page, MobileActionControl action)
        {
            page.Scroll.ScrollTo(action.Root);
            yield return null;
            yield return null;
            Tap(action);
            yield return null;
        }

        private static void SelectLocale(string localeId)
        {
            Locale locale = LocalizationSettings.AvailableLocales.Locales.FirstOrDefault(candidate =>
                candidate.Identifier.Code.StartsWith(localeId, StringComparison.OrdinalIgnoreCase));
            Assert.That(locale, Is.Not.Null);
            LocalizationSettings.SelectedLocale = locale;
        }

        private static UiToolkitSafeAreaBinding GetSafeAreaBinding(MobilePageShell shell)
        {
            FieldInfo field = typeof(MobilePageShell).GetField(
                "safeAreaBinding",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (UiToolkitSafeAreaBinding)field.GetValue(shell);
        }

        private static Rect InsetRect(Rect outer, IResolvedStyle style) => new Rect(
            outer.xMin + style.paddingLeft,
            outer.yMin + style.paddingTop,
            outer.width - style.paddingLeft - style.paddingRight,
            outer.height - style.paddingTop - style.paddingBottom);

        private static void AssertContained(Rect outer, Rect inner, string label)
        {
            Assert.That(inner.xMin, Is.GreaterThanOrEqualTo(outer.xMin - 1f), label + " left");
            Assert.That(inner.yMin, Is.GreaterThanOrEqualTo(outer.yMin - 1f), label + " top");
            Assert.That(inner.xMax, Is.LessThanOrEqualTo(outer.xMax + 1f), label + " right");
            Assert.That(inner.yMax, Is.LessThanOrEqualTo(outer.yMax + 1f), label + " bottom");
        }

        private void DisableHeadlessVideo()
        {
            if (!captureHeadlessErrors)
                return;
            foreach (VideoPlayer player in UnityEngine.Object.FindObjectsByType<VideoPlayer>(
                         FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
            {
                player.Stop();
                player.enabled = false;
            }
        }

        private void CaptureHeadlessError(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)
                return;
            if (condition == "RenderTexture.Create failed" ||
                condition == "Failed to set the active render target, ensure that it is a valid render target.")
                return;
            unexpectedHeadlessErrors.Add(condition + "\n" + stackTrace);
        }
    }
}
