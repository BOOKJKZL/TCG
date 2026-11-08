using System;
using System.Collections;
using Gacha.Application;
using Gacha.Domain;
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
    private CatalogSession catalogSession;
    private LanguageSelectionService languages;
    private CardTextureCache textureCache;
    private Coroutine contentLoad;
    private bool refreshingContent;

    public MobileHomePresenter HomePresenter => homePresenter;

    private void Start()
    {
        HomeSceneRequest.Clear();
        if (!ApplicationServices.IsConfigured)
            GameApplicationBootstrap.EnsureConfigured();
        HideLegacyCanvas();
        homePresenter = new MobileHomePresenter(
            gameObject,
            GachaBtnClick,
            CollectionBtnClick,
            ContentBtnClick,
            SettingBtnClick,
            PokedexBtnClick,
            RetryContent,
            OpenProduct,
            OpenPrinting);
        systemBack = gameObject.AddComponent<SystemBackNavigation>();
        systemBack.Bind(homePresenter.Document.rootVisualElement, TryHandleBack);
        catalogSession = ApplicationServices.Catalog;
        languages = ApplicationServices.Languages;
        catalogSession.Changed += OnCatalogChanged;
        languages.ContentLanguageChanged += OnContentLanguageChanged;
        GameBootloader.Ready += OnInventoryReady;
        BeginContentLoad(false);
    }

    private void OnDestroy()
    {
        if (contentLoad != null)
            StopCoroutine(contentLoad);
        contentLoad = null;
        if (catalogSession != null)
            catalogSession.Changed -= OnCatalogChanged;
        if (languages != null)
            languages.ContentLanguageChanged -= OnContentLanguageChanged;
        GameBootloader.Ready -= OnInventoryReady;
        systemBack?.DisposeBinding();
        homePresenter?.Dispose();
        homePresenter = null;
        textureCache?.Dispose();
        textureCache = null;
    }

    public void RetryContent() => BeginContentLoad(true);

    private void BeginContentLoad(bool forceReload)
    {
        if (homePresenter == null || contentLoad != null || navigationRequested)
            return;
        homePresenter.ShowLoading();
        contentLoad = StartCoroutine(LoadContentNextFrame(forceReload));
    }

    private IEnumerator LoadContentNextFrame(bool forceReload)
    {
        // The shell must reach the panel before synchronous catalog notifications update it.
        yield return null;
        refreshingContent = true;
        CatalogLoadResult result;
        try
        {
            result = catalogSession.EnsureLoaded(forceReload);
        }
        catch (Exception exception)
        {
            result = CatalogLoadResult.Failure("Home catalog refresh failed.");
            Debug.LogWarning("Home catalog refresh failed: " + exception);
        }
        finally
        {
            refreshingContent = false;
            contentLoad = null;
        }
        PresentContent(result);
        if (GetComponentInChildren<FirstRunContentSetupController>() == null)
        {
            var host = new GameObject("First Run Content Setup");
            host.transform.SetParent(transform, false);
            host.AddComponent<FirstRunContentSetupController>();
        }
    }

    private void OnCatalogChanged(CatalogLoadResult result)
    {
        if (!refreshingContent)
            PresentContent(result);
    }

    private void OnContentLanguageChanged(ContentLanguageSelection selection)
    {
        if (!refreshingContent)
            PresentContent(catalogSession.Current);
    }

    private void OnInventoryReady()
    {
        if (!refreshingContent && contentLoad == null)
            PresentContent(catalogSession.Current);
    }

    private void PresentContent(CatalogLoadResult result)
    {
        if (homePresenter == null || result == null)
            return;
        refreshingContent = true;
        try
        {
            if (result.State == CatalogLoadState.NoInstalledContent)
            {
                languages.RefreshContentLanguage(result.Catalog);
                homePresenter.BindContent(new HomeShowcaseData(
                    Array.Empty<HomePackPreview>(),
                    Array.Empty<PrintingDefinition>(),
                    languages.ContentLanguage.ResolvedLanguageId), null);
                return;
            }
            PlayerUiError error = PlayerUiErrorMapper.FromCatalog(result);
            if (error != null)
            {
                homePresenter.ShowContentError(error);
                return;
            }
            if (!ApplicationServices.HasContentImages || Inventory.Instance == null)
            {
                homePresenter.ShowContentError(PlayerUiErrorMapper.Create(PlayerUiErrorCode.ServiceUnavailable));
                return;
            }

            languages.RefreshContentLanguage(result.Catalog);
            textureCache ??= new CardTextureCache(ApplicationServices.Images, 12);
            homePresenter.BindContent(HomeShowcaseData.FromCatalog(
                result.Catalog,
                Inventory.Instance.Data.Cards,
                languages.ContentLanguage.ResolvedLanguageId), textureCache);
        }
        catch (Exception exception)
        {
            homePresenter.ShowContentError(PlayerUiErrorMapper.FromException(exception));
            Debug.LogWarning("Home content could not be initialized: " + exception);
        }
        finally
        {
            refreshingContent = false;
        }
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

    public void PokedexBtnClick()
    {
        if (navigationRequested)
            return;
        HomeSceneRequest.RequestPokedex();
        LoadScene(MobileDestination.Pokedex, true);
    }

    public void OpenProduct(string selectedProductId, bool prepare)
    {
        if (navigationRequested)
            return;
        HomeSceneRequest.RequestProduct(selectedProductId, prepare);
        LoadScene(MobileDestination.Gacha, true);
    }

    public void OpenPrinting(string printingId)
    {
        if (navigationRequested)
            return;
        HomeSceneRequest.RequestPrinting(printingId);
        LoadScene(MobileDestination.Collection, true);
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

    private void LoadScene(MobileDestination destination, bool hasHomeRequest = false)
    {
        if (navigationRequested)
            return;
        if (!hasHomeRequest)
            HomeSceneRequest.Clear();
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
            HomeSceneRequest.Clear();
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
