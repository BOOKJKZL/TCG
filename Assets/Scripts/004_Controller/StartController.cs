using System;
using Gacha.Application;
using Gacha.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;

public class StartController : MonoBehaviour
{
    private MobileStartPresenter presenter;
    private bool navigationRequested;
    private bool destroyed;
    private SystemBackNavigation systemBack;
    private Action<string> sceneLoaderOverrideForTests;
    private Action quitOverrideForTests;

    public MobileStartPresenter Presenter => presenter;

    private void Start()
    {
        if (!ApplicationServices.IsConfigured)
            GameApplicationBootstrap.EnsureConfigured();
        presenter = new MobileStartPresenter(gameObject, StartBtnClick);
        systemBack = gameObject.AddComponent<SystemBackNavigation>();
        systemBack.Bind(presenter.Document.rootVisualElement, TryHandleBack);
        HideLegacyPresentation();
    }

    private void OnDestroy()
    {
        destroyed = true;
        systemBack?.DisposeBinding();
        presenter?.Dispose();
        presenter = null;
    }

    public void StartBtnClick()
    {
        if (destroyed || navigationRequested || presenter?.ExitConfirmation.IsVisible == true)
            return;
        navigationRequested = true;
        presenter?.SetPending(true);
        string sceneName = MobilePrimaryNavigation.SceneName(MobileDestination.Home);
        try
        {
            if (sceneLoaderOverrideForTests != null)
                sceneLoaderOverrideForTests(sceneName);
            else if (GameManager.Instance != null && GameManager.Instance.loadManager != null)
                GameManager.Instance.loadManager.LoadScene(sceneName, null, HandleNavigationFailure);
            else
                SceneManager.LoadScene(sceneName);
        }
        catch (Exception exception)
        {
            HandleNavigationFailure(exception);
        }
    }

    public bool TryHandleBack()
    {
        if (destroyed || presenter == null)
            return false;
        if (presenter.ExitConfirmation.TryCancel() || navigationRequested)
            return true;
        presenter.ExitConfirmation.Show(
            CardUiText.Get("home.exit.title"), CardUiText.Get("home.exit.body"),
            CardUiText.Get("home.exit.confirm"), CardUiText.Get("common.action.cancel"),
            () =>
            {
                if (quitOverrideForTests != null)
                    quitOverrideForTests();
                else
                    Application.Quit();
            }, destructive: false);
        return true;
    }

    private void HandleNavigationFailure(Exception exception)
    {
        if (destroyed)
            return;
        navigationRequested = false;
        presenter?.SetFailed();
        Debug.LogWarning("Start navigation failed: " + exception);
    }

    private void HideLegacyPresentation()
    {
        // The old edge artwork has a higher sorting order than the runtime UI and
        // persists between scenes. Retire only that Canvas, keeping its managers,
        // loading overlay and scene-transition Canvas active.
        Transform persistentRoot = GameManager.Instance != null
            ? GameManager.Instance.transform.root
            : null;
        Canvas sideCanvas = persistentRoot?.Find("Side/Canvas")?.GetComponent<Canvas>();
        if (sideCanvas != null)
            sideCanvas.enabled = false;

        foreach (GameObject sceneRoot in gameObject.scene.GetRootGameObjects())
        {
            foreach (Canvas canvas in sceneRoot.GetComponentsInChildren<Canvas>(true))
            {
                if (canvas.transform.Find("StartBtn") != null)
                    canvas.enabled = false;
            }
        }
    }
}
