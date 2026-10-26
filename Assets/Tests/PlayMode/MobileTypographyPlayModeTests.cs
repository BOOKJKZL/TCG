using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Gacha.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Gacha.Tests.PlayMode
{
    public sealed class TestFontScaleSource : IUiFontScaleSource
    {
        private Action<float> changed;
        public float Scale { get; private set; } = 1f;
        public int Subscribers => changed?.GetInvocationList().Length ?? 0;
        public event Action<float> Changed { add => changed += value; remove => changed -= value; }
        public void Set(float value) { Scale = value; changed?.Invoke(value); }
    }

    public sealed class MobileTypographyPlayModeTests
    {
        [UnityTest]
        public IEnumerator ScaleRoundTrip_UsesNominalSizeAndReattachesWithoutLeakingSubscriptions()
        {
            var source = new TestFontScaleSource();
            GameObject host = Host(out UIDocument document);
            var root = new VisualElement();
            var label = new Label("English 简体中文 日本語");
            label.AddToClassList("home-hero__title");
            label.style.fontSize = 31f;
            root.Add(label);
            MobileTypographyBinding binding = MobileTypography.Bind(root, MobileTypographyProfiles.Home, source);
            try
            {
                Assert.That(source.Subscribers, Is.Zero, "Detached rows must not retain source subscriptions.");
                document.rootVisualElement.Add(root);
                yield return null;
                foreach (float scale in new[] { 1f, 1.3f, 2f, 0.85f, 1f })
                {
                    source.Set(scale);
                    binding.RegisterSubtree(root);
                    yield return null;
                    Assert.That(label.resolvedStyle.fontSize, Is.EqualTo(34f * scale).Within(0.1f));
                    Assert.That(binding.TargetCount, Is.EqualTo(1));
                    Assert.That(source.Subscribers, Is.EqualTo(1));
                }
                root.AddToClassList("mobile-layout--compact");
                binding.Refresh();
                yield return null;
                Assert.That(label.resolvedStyle.fontSize, Is.EqualTo(28f).Within(0.1f));
                root.RemoveFromHierarchy();
                Assert.That(source.Subscribers, Is.Zero);
                source.Set(2f);
                document.rootVisualElement.Add(root);
                yield return null;
                Assert.That(label.resolvedStyle.fontSize, Is.EqualTo(56f).Within(0.1f));
                binding.Dispose();
                Assert.That(label.style.fontSize.value.value, Is.EqualTo(31f));
                Assert.That(source.Subscribers, Is.Zero);
            }
            finally { binding.Dispose(); Object.DestroyImmediate(host); }
        }

        [UnityTest]
        public IEnumerator VirtualizedRows_ReuseTheirBindingAndNewRowsUseCurrentScale()
        {
            var source = new TestFontScaleSource();
            GameObject host = Host(out UIDocument document);
            var list = new ListView { virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight };
            var rows = new List<VisualElement>();
            var destroyed = new HashSet<VisualElement>();
            list.style.width = 520f;
            list.style.height = 300f;
            MobileGameDesignSystem.AttachStyle(list);
            list.makeItem = () =>
            {
                var row = new VisualElement();
                row.style.minHeight = 72f;
                var label = new Label();
                label.AddToClassList("mobile-action__label");
                row.Add(label);
                MobileTypography.Bind(row, MobileTypographyProfiles.Shared, source);
                rows.Add(row);
                return row;
            };
            list.bindItem = (row, index) =>
            {
                row.Q<Label>().text = $"{index}: English / 中文 / 日本語の長い動的な行";
                Assert.That(MobileTypography.Bind(row, MobileTypographyProfiles.Shared, source).TargetCount, Is.EqualTo(1));
            };
            list.unbindItem = (row, _) => row.Q<Label>().text = string.Empty;
            list.destroyItem = row => { MobileTypography.Unbind(row, MobileTypographyProfiles.Shared); destroyed.Add(row); };
            try
            {
                document.rootVisualElement.Add(list);
                list.itemsSource = Enumerable.Range(0, 100).ToList();
                yield return Settle();
                source.Set(2f);
                list.ScrollToItem(95);
                yield return Settle();
                foreach (Label label in list.Query<Label>(className: "mobile-action__label").ToList())
                    Assert.That(label.resolvedStyle.fontSize, Is.EqualTo(34f).Within(0.1f));
                source.Set(0.85f);
                list.ScrollToItem(0);
                yield return Settle();
                foreach (Label label in list.Query<Label>(className: "mobile-action__label").ToList())
                    Assert.That(label.resolvedStyle.fontSize, Is.EqualTo(14.45f).Within(0.1f));
                list.Rebuild();
                yield return Settle();
                Assert.That(destroyed, Is.Not.Empty, "The real ListView rebuild must release row bindings.");
                Assert.That(rows.Count, Is.GreaterThan(0));
                list.RemoveFromHierarchy();
                Assert.That(source.Subscribers, Is.Zero);
            }
            finally
            {
                foreach (VisualElement row in rows) MobileTypography.Unbind(row, MobileTypographyProfiles.Shared);
                Object.DestroyImmediate(host);
            }
        }

        [UnityTest]
        public IEnumerator TypedInputs_KeepValuesAndScaleTheCorrectLiveDropdown()
        {
            var source = new TestFontScaleSource();
            GameObject host = Host(out UIDocument document);
            VisualElement root = document.rootVisualElement;
            var text = new TextField();
            text.AddToClassList("content-management__search");
            text.value = "保存する入力";
            var toggle = new Toggle("Wi-Fi only") { value = true };
            toggle.AddToClassList("content-management__wifi-only");
            var first = new DropdownField("语言", new List<string> { "English", "日本語" }, 0);
            first.AddToClassList("content-management__filter");
            var second = new DropdownField("稀有度", new List<string> { "すべて", "特別なカード" }, 0);
            second.AddToClassList("collection-filter");
            root.Add(text); root.Add(toggle); root.Add(first); root.Add(second);
            first.style.width = 400f; second.style.width = 400f;
            MobileTypographyBinding content = MobileTypography.Bind(root, MobileTypographyProfiles.Content, source);
            MobileTypographyBinding collection = MobileTypography.Bind(root, MobileTypographyProfiles.Collection, source);
            text.label = "後から翻訳される検索ラベル";
            try
            {
                yield return Settle();
                Open(first);
                yield return Settle();
                VisualElement popup = Popup(root);
                Assert.That(popup, Is.Not.Null);
                source.Set(2f);
                yield return Settle();
                Assert.That(popup.resolvedStyle.fontSize, Is.EqualTo(32f).Within(0.1f),
                    $"The live native popup must consume scale changes: parent={popup.parent?.name}, root={root.name}.");
                Label japanese = popup.Query<Label>().ToList().First(label => label.text == "日本語");
                Assert.That(japanese.resolvedStyle.fontSize, Is.EqualTo(32f).Within(0.1f));
                Assert.That(japanese.worldBound.height, Is.GreaterThanOrEqualTo(31f));
                Tap(japanese);
                yield return Settle();
                Assert.That(first.value, Is.EqualTo("日本語"));
                Open(second);
                yield return Settle();
                popup = Popup(root);
                Assert.That(popup, Is.Not.Null);
                source.Set(1.3f);
                yield return Settle();
                Assert.That(popup.resolvedStyle.fontSize, Is.EqualTo(18.2f).Within(0.1f),
                    "The previous 16px field must not overwrite the new 14px field's popup.");
                Assert.That(text.value, Is.EqualTo("保存する入力"));
                Assert.That(toggle.value, Is.True);
                Assert.That(first.value, Is.EqualTo("日本語"));
                Assert.That(text.resolvedStyle.fontSize, Is.EqualTo(20.8f).Within(0.1f));
                Assert.That(text.labelElement.resolvedStyle.fontSize, Is.EqualTo(20.8f).Within(0.1f));
                foreach (TextElement child in text.Query<TextElement>().ToList())
                    Assert.That(child.resolvedStyle.fontSize, Is.EqualTo(20.8f).Within(0.1f));
            }
            finally { content.Dispose(); collection.Dispose(); Object.DestroyImmediate(host); }
            Assert.That(source.Subscribers, Is.Zero);
        }

        [UnityTest]
        public IEnumerator LongLocalizedSheet_WithDoubleSystemFont_KeepsActionsReachable()
        {
            var source = new TestFontScaleSource();
            source.Set(2f);
            GameObject host = Host(out UIDocument document);
            MobileConfirmationPresenter confirmation = null;
            IDisposable injected = MobileTypography.OverrideSourceForTests(source);
            bool priorMotion = UIFeedbackService.ReduceMotion;
            bool priorHaptics = UIFeedbackService.HapticsEnabled;
            bool priorSound = UIFeedbackService.SoundEnabled;
            float priorSpeed = UIFeedbackService.AnimationSpeed;
            UIFeedbackService.Configure(true, false, 1f, false);
            try
            {
                var viewport = new VisualElement();
                viewport.style.position = Position.Relative;
                viewport.style.width = 1000f;
                viewport.style.height = 600f;
                document.rootVisualElement.Add(viewport);
                confirmation = new MobileConfirmationPresenter();
                viewport.Add(confirmation.Root);
                foreach (string languageText in new[] { "Review this content before continuing. ", "请检查内容，然后确认继续。", "続ける前に内容と選択した設定を確認してください。" })
                foreach (bool destructive in new[] { false, true })
                {
                    confirmation.Show(languageText, string.Concat(Enumerable.Repeat(languageText, 16)),
                        "Confirm / 确认 / 確認", "Cancel / 取消 / キャンセル", () => { }, destructive: destructive);
                    yield return Settle();
                    VisualElement action = destructive ? confirmation.DangerConfirm.Root : confirmation.Confirm.Root;
                    confirmation.Sheet.Scroll.ScrollTo(action);
                    yield return Settle();
                    Rect visible = confirmation.Sheet.Scroll.contentViewport.worldBound;
                    Assert.That(action.worldBound.yMin, Is.GreaterThanOrEqualTo(visible.yMin - 1f));
                    Assert.That(action.worldBound.yMax, Is.LessThanOrEqualTo(visible.yMax + 1f));
                    Assert.That(action.worldBound.height, Is.GreaterThanOrEqualTo(48f));
                    VisualElement picked = action.panel.Pick(action.worldBound.center);
                    Assert.That(picked == action || action.Contains(picked), Is.True);
                    Assert.That(confirmation.Sheet.Title.resolvedStyle.fontSize, Is.EqualTo(50f).Within(0.1f));
                    confirmation.Hide();
                }
            }
            finally
            {
                confirmation?.Dispose();
                Object.DestroyImmediate(host);
                injected.Dispose();
                UIFeedbackService.Configure(priorMotion, priorHaptics, priorSpeed, priorSound);
            }
            Assert.That(source.Subscribers, Is.Zero);
        }

        private static GameObject Host(out UIDocument document)
        {
            var host = new GameObject("System Font Scale Test");
            document = host.AddComponent<UIDocument>();
            document.panelSettings = Resources.Load<PanelSettings>("UI/Collection Panel Settings");
            return host;
        }
        private static IEnumerator Settle() { for (int i = 0; i < 5; i++) yield return null; }
        private static void Open(DropdownField field)
        {
            field.Focus();
            using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled()) field.SendEvent(submit);
        }
        private static VisualElement Popup(VisualElement root) =>
            root.panel.visualTree.Q<VisualElement>(className: GenericDropdownMenu.ussClassName);
        private static void Tap(VisualElement target)
        {
            using (PointerDownEvent down = PointerDownEvent.GetPooled(new Event
                   { type = EventType.MouseDown, button = 0, mousePosition = target.worldBound.center })) target.SendEvent(down);
            using (PointerUpEvent up = PointerUpEvent.GetPooled(new Event
                   { type = EventType.MouseUp, button = 0, mousePosition = target.worldBound.center })) target.SendEvent(up);
        }
    }

    /// <summary>Reuse the production scene matrix and its unchanged safe-area/picking assertions at scale 2.</summary>
    public sealed class MobileTypographyBusinessPlayModeTests
    {
        private RemainingUiStateMatrixPlayModeTests matrix;
        private IDisposable injected;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            var source = new TestFontScaleSource();
            source.Set(2f);
            injected = MobileTypography.OverrideSourceForTests(source);
            matrix = new RemainingUiStateMatrixPlayModeTests();
            yield return matrix.SetUp();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            try { if (matrix != null) yield return matrix.TearDown(); }
            finally { injected?.Dispose(); matrix = null; injected = null; }
        }

        [UnityTest]
        public IEnumerator DoubleFont_Gacha_ConfirmationAndRevealRemainReachable() =>
            matrix.Gacha_LongJapaneseConfirmationAndReveal_ActionsRemainReachable();

        [UnityTest]
        public IEnumerator DoubleFont_Collection_DetailsAndFiltersRemainReachable() =>
            matrix.Collection_LongJapaneseDetailsAndFilters_CloseWithoutLosingTheSet();

        [UnityTest]
        public IEnumerator DoubleFont_Content_ConfirmationActionsRemainReachable() =>
            matrix.Content_LongJapaneseDownloadAndRemoveConfirmations_CancelWithoutSideEffects();

        [UnityTest]
        public IEnumerator DoubleFont_Settings_RecoveryActionsRemainReachable() =>
            matrix.Settings_RecoveryConfirmation_IsReachableAndRestoresOnlyAfterExplicitConfirmation();
    }
}
