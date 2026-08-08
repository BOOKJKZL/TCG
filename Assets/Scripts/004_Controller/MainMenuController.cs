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

        if (GetComponentInChildren<Gacha.Presentation.FirstRunContentSetupController>() == null)
        {
            var host = new GameObject("First Run Content Setup");
            host.transform.SetParent(transform, false);
            host.AddComponent<Gacha.Presentation.FirstRunContentSetupController>();
        }
    }

    private void OnDestroy()
    {
        homePresenter?.Dispose();
        homePresenter = null;
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
        try
        {
            if (sceneLoaderOverrideForTests != null)
                sceneLoaderOverrideForTests(sceneName);
            else if (GameManager.Instance != null && GameManager.Instance.loadManager != null)
                GameManager.Instance.loadManager.LoadScene(sceneName);
            else
                SceneManager.LoadScene(sceneName);
        }
        catch
        {
            navigationRequested = false;
            homePresenter?.ClearNavigationPending();
            throw;
        }
    }
}
