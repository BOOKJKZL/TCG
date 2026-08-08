using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Gacha.Presentation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

public class MobileDesignSystemContractTests
{
    private const string MobileUiPath = "Assets/Resources/UI/Mobile";
    private const string StylesPath = "Assets/Resources/UI/MobileGameDesignSystem.uss";

    private static readonly (string File, string Root)[] Templates =
    {
        ("MobileAction.uxml", "mobile-action"),
        ("MobilePageShell.uxml", "mobile-page"),
        ("MobileTopBar.uxml", "mobile-top-bar"),
        ("MobileBottomNavigation.uxml", "mobile-bottom-navigation"),
        ("MobileCard.uxml", "mobile-card"),
        ("MobileStateView.uxml", "mobile-state-view"),
        ("MobileProgress.uxml", "mobile-progress"),
        ("MobileToast.uxml", "mobile-toast"),
        ("MobileSheet.uxml", "mobile-sheet")
    };

    [Test]
    public void Templates_ImportAndExposeStableNamedRoots()
    {
        foreach ((string file, string rootName) in Templates)
        {
            string path = MobileUiPath + "/" + file;
            VisualTreeAsset asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
            Assert.That(asset, Is.Not.Null, path);
            VisualElement tree = asset.CloneTree();
            VisualElement root = tree.Q<VisualElement>(rootName);
            Assert.That(root, Is.Not.Null, path);
            Assert.That(root.styleSheets.count, Is.GreaterThan(0),
                path + " must link the shared stylesheet explicitly.");
        }
    }

    [Test]
    public void Templates_AvoidNativeButtonsAndInlineDurableStyles()
    {
        foreach ((string file, _) in Templates)
        {
            string path = MobileUiPath + "/" + file;
            string source = File.ReadAllText(path).Replace("\r\n", "\n");
            Assert.That(source, Does.Not.Contain("<ui:Button"), path);
            Assert.That(source, Does.Not.Match(@"\sstyle\s*="), path);
            Assert.That(source, Does.Contain("MobileGameDesignSystem.uss"), path);
        }
    }

    [Test]
    public void Styles_UseUnitySupportedMobileContracts()
    {
        string styles = File.ReadAllText(StylesPath).Replace("\r\n", "\n");
        string[] unsupported =
        {
            "gap", "z-index", "box-shadow", "filter", "outline", "gradient", ":nth-child"
        };
        foreach (string property in unsupported)
            Assert.That(styles, Does.Not.Match(@"(?m)(^|[;{]\s*)" + Regex.Escape(property) + @"\s*[:(]"), property);
        Assert.That(styles, Does.Not.Contain("url(http"));
        Assert.That(styles, Does.Not.Contain(".mobile-action:active"),
            "Android press feedback must not mutate the action root render node.");

        Match action = Regex.Match(styles, @"(?ms)^\.mobile-action\s*\{(?<body>.*?)\}");
        Match pressed = Regex.Match(styles, @"(?ms)^\.mobile-action__label\.is-pressed\s*\{(?<body>.*?)\}");
        Assert.That(action.Success, Is.True);
        Assert.That(action.Groups["body"].Value, Does.Contain("min-height: 50px;"));
        Assert.That(action.Groups["body"].Value, Does.Not.Contain("transition"));
        Assert.That(action.Groups["body"].Value, Does.Not.Contain("scale"));
        Assert.That(pressed.Success, Is.True);
        Assert.That(pressed.Groups["body"].Value, Does.Contain("color:"));
        Assert.That(pressed.Groups["body"].Value, Does.Not.Contain("background-color:"));
        Assert.That(pressed.Groups["body"].Value, Does.Not.Contain("border-color:"));
        Assert.That(MobileUiTokens.MinimumTouchTarget, Is.GreaterThanOrEqualTo(48f));
    }

    [Test]
    public void ActionStates_KeepPermanentRootRenderContract()
    {
        using var action = new MobileActionControl("test-action", "Action", () => { });
        int childCount = action.Root.childCount;
        string[] rootClasses = action.Root.GetClasses().OrderBy(value => value).ToArray();
        PickingMode pickingMode = action.Root.pickingMode;
        bool focusable = action.Root.focusable;
        bool enabledSelf = action.Root.enabledSelf;
        StyleEnum<DisplayStyle> display = action.Root.style.display;
        StyleEnum<Visibility> visibility = action.Root.style.visibility;

        action.SetEnabled(false);
        action.SetLoading(true);
        action.SetSelected(true);
        action.SetLoading(false);
        action.SetEnabled(true);
        action.SetSelected(false);

        Assert.That(action.Root, Is.Not.TypeOf<Button>());
        Assert.That(action.Root.childCount, Is.EqualTo(childCount));
        Assert.That(action.Root.GetClasses().OrderBy(value => value), Is.EqualTo(rootClasses));
        Assert.That(action.Root.pickingMode, Is.EqualTo(pickingMode));
        Assert.That(action.Root.focusable, Is.EqualTo(focusable));
        Assert.That(action.Root.enabledSelf, Is.EqualTo(enabledSelf));
        Assert.That(action.Root.style.display, Is.EqualTo(display));
        Assert.That(action.Root.style.visibility, Is.EqualTo(visibility));
        Assert.That(action.Label.pickingMode, Is.EqualTo(PickingMode.Ignore));
        Assert.That(action.LoadingIndicator.pickingMode, Is.EqualTo(PickingMode.Ignore));
        Assert.That(action.Label.ClassListContains("is-disabled"), Is.False);
        Assert.That(action.LoadingIndicator.ClassListContains("is-visible"), Is.False);
    }

    [Test]
    public void Components_ExposeFixedSlotsForPageAndStateComposition()
    {
        using var shell = new MobilePageShell("test-page");
        var topBar = new MobileTopBar("A localized title", "A localized subtitle");
        using var first = new MobileActionControl("first", "First", () => { }, MobileActionTone.Navigation);
        using var second = new MobileActionControl("second", "Second", () => { }, MobileActionTone.Navigation);
        var navigation = new MobileBottomNavigation(first, second);
        var card = new MobileCard();
        var empty = new MobileEmptyState("Nothing here", "Install content to continue.");

        shell.HeaderSlot.Add(topBar.Root);
        shell.ContentSlot.Add(card.Root);
        shell.ContentSlot.Add(empty.Root);
        shell.BottomNavigationSlot.Add(navigation.Root);
        navigation.Select(1);

        Assert.That(shell.Root.Q<VisualElement>("safe-area"), Is.SameAs(shell.SafeArea));
        Assert.That(shell.Root.Q<VisualElement>("overlay-layer"), Is.SameAs(shell.OverlayLayer));
        Assert.That(shell.Root.Q<VisualElement>("modal-layer"), Is.SameAs(shell.ModalLayer));
        Assert.That(shell.OverlayLayer.parent, Is.SameAs(shell.SafeArea),
            "Transient messages must inherit the page Safe Area.");
        Assert.That(shell.ModalLayer.parent, Is.SameAs(shell.Root),
            "Modal sheets own their edge-to-edge scrim and Safe Area binding.");
        Assert.That(topBar.Leading.childCount, Is.EqualTo(4));
        Assert.That(card.Root.childCount, Is.EqualTo(3));
        Assert.That(navigation.Count, Is.EqualTo(2));
        Assert.That(first.IsSelected, Is.False);
        Assert.That(second.IsSelected, Is.True);
        Assert.That(navigation.Root.Children().Any(child => child is Button), Is.False);
    }

    [Test]
    public void ErrorAndOverlayComponents_KeepStructuredStateAndFixedActions()
    {
        int retries = 0;
        using var error = new MobileErrorState(() => retries++, () => { }, () => { }, () => { });
        PlayerUiError playerError = PlayerUiErrorMapper.Create(PlayerUiErrorCode.Offline);
        error.Presenter.Show(playerError);

        Assert.That(error.Presenter.Current, Is.SameAs(playerError));
        Assert.That(error.Root.Q<Button>(), Is.Null);
        Assert.That(error.Actions.childCount, Is.EqualTo(4));
        Assert.That(error.Title.text, Does.Not.Contain("/storage/emulated/0"));
        Assert.That(error.RetrySlot.style.display.value, Is.EqualTo(DisplayStyle.Flex));

        using var unavailableActions = new MobileErrorState(null, null, null, null);
        unavailableActions.Presenter.Show(playerError);
        Assert.That(unavailableActions.RetrySlot.style.display.value, Is.EqualTo(DisplayStyle.None));
        Assert.That(unavailableActions.ManageContentSlot.style.display.value, Is.EqualTo(DisplayStyle.None));
        Assert.That(unavailableActions.HomeSlot.style.display.value, Is.EqualTo(DisplayStyle.None));
        Assert.That(unavailableActions.CloseSlot.style.display.value, Is.EqualTo(DisplayStyle.None));

        var progress = new MobileProgressView("Download");
        progress.Set(140f);
        Assert.That(progress.Bar.value, Is.EqualTo(100f));
        Assert.That(progress.Value.text, Is.EqualTo("100%"));
        progress.Set(-20f);
        Assert.That(progress.Bar.value, Is.EqualTo(0f));

        using var toast = new MobileToastPresenter();
        toast.Show("Saved", MobileStatusTone.Success, 0);
        Assert.That(toast.IsVisible, Is.True);
        toast.HideImmediately();
        Assert.That(toast.IsVisible, Is.False);

        using var confirmation = new MobileConfirmationPresenter();
        confirmation.Show("Remove?", "Progress stays saved.", "Remove", "Cancel", null, destructive: true);
        Assert.That(confirmation.DangerConfirmSlot.style.display.value, Is.EqualTo(DisplayStyle.Flex));
        Assert.That(confirmation.ConfirmSlot.style.display.value, Is.EqualTo(DisplayStyle.None));
        Assert.That(confirmation.Root.Q<Button>(), Is.Null);
    }
}
