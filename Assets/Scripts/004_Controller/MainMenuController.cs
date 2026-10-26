using System;
using Gacha.Application;
using Gacha.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuController : MonoBehaviour
{
    private MobileHomePresenter homePresenter;
    private bool navigationRequested;
    private Action<string> sceneLoaderOverrideForTests;
    private Action quitOverrideForTests;
    private SystemBackNavigation systemBack;

    public MobileHomePresenter HomePresenter => homePresenter;

    private void Start()
    {
        if (!ApplicationServices.IsConfigured)
            GameApplicationBootstrap.EnsureConfigured();
        HideLegacyCanvas();
        homePresenter = new MobileHomePresenter(
            gameObject,
            GachaBtnClick,
            CollectionBtnClick,
            ContentBtnClick,
            SettingBtnClick);
        systemBack = gameObject.AddComponent<SystemBackNavigation>();
        systemBack.Bind(homePresenter.Document.rootVisualElement, TryHandleBack);

        if (GetComponentInChildren<Gacha.Presentation.FirstRunContentSetupController>() == null)
        {
            var host = new GameObject("First Run Content Setup");
            host.transform.SetParent(transform, false);
            host.AddComponent<Gacha.Presentation.FirstRunContentSetupController>();
        }
    }

    private void OnDestroy()
    {
        systemBack?.DisposeBinding();
        homePresenter?.Dispose();
        homePresenter = null;
    }

    public bool TryHandleBack()
    {
        if (homePresenter == null)
            return false;
        if (homePresenter.ExitConfirmation.TryCancel() || navigationRequested)
            return true;
        homePresenter.ExitConfirmation.Show(
            CardUiText.Get("home.exit.title"),
            CardUiText.Get("home.exit.body"),
            CardUiText.Get("home.exit.confirm"),
            CardUiText.Get("common.action.cancel"),
            () =>
            {
                if (quitOverrideForTests != null)
                    quitOverrideForTests();
                else
                    Application.Quit();
            }, destructive: false);
        return true;
    }

    public void GachaBtnClick()
    {
        LoadScene(MobileDestination.Gacha);
    }

    public void CollectionBtnClick()
    {
        LoadScene(MobileDestination.Collection);
    }

    public void SettingBtnClick()
    {
        LoadScene(MobileDestination.Settings);
    }

    public void ContentBtnClick()
    {
        LoadScene(MobileDestination.Content);
    }

    private void HideLegacyCanvas()
    {
        foreach (GameObject sceneRoot in gameObject.scene.GetRootGameObjects())
        {
            foreach (Canvas canvas in sceneRoot.GetComponentsInChildren<Canvas>(true))
            {
                if (canvas != null && canvas.gameObject.scene == gameObject.scene)
                    canvas.gameObject.SetActive(false);
            }
        }
    }

    private void LoadScene(MobileDestination destination)
    {
        if (navigationRequested)
            return;
        navigationRequested = true;
        homePresenter?.SetNavigationPending(destination);
        string sceneName = MobilePrimaryNavigation.SceneName(destination);
        if (destination == MobileDestination.Settings)
            SettingsReturnNavigation.Remember(MobileDestination.Home);
        if (destination == MobileDestination.Content)
            ContentReturnNavigation.RememberCurrentScene(MobilePrimaryNavigation.SceneName(MobileDestination.Home));
        void Completed()
        {
            if (destination != MobileDestination.Content)
                ContentReturnNavigation.Clear();
        }
        void Failed(Exception exception)
        {
            if (this == null)
                return;
            navigationRequested = false;
            homePresenter?.ShowNavigationFailure();
            Debug.LogWarning("Home navigation failed: " + exception);
        }
        try
        {
            if (sceneLoaderOverrideForTests != null)
            {
                sceneLoaderOverrideForTests(sceneName);
                Completed();
            }
            else if (GameManager.Instance != null && GameManager.Instance.loadManager != null)
                GameManager.Instance.loadManager.LoadScene(sceneName, Completed, Failed);
            else
            {
                SceneManager.LoadScene(sceneName);
                Completed();
            }
        }
        catch (Exception exception)
        {
            Failed(exception);
        }
    }
}
