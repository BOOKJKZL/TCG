using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class SceneTransition : MonoBehaviour
{
    public Image fadeOverlay;
    public float fadeDuration = 0.5f;
    public float delayBeforeFadeIn = 1f;
    private Coroutine transition;
    private Coroutine initialFade;
    private Action<Exception> pendingFailure;
    private Func<string, AsyncOperation> sceneLoaderOverrideForTests;

    private void Start()
    {
        if (transition == null)
            initialFade = StartCoroutine(FadeTo(0f));
    }

    public void ChangeScene(string sceneName) => ChangeScene(sceneName, null,
        exception => Debug.LogWarning("Scene navigation failed: " + exception));

    public void ChangeScene(int sceneID) => ChangeScene(
        SceneUtility.GetScenePathByBuildIndex(sceneID));

    public void ChangeScene(string sceneName, Action completed, Action<Exception> failed)
    {
        if (!isActiveAndEnabled)
        {
            failed?.Invoke(new InvalidOperationException("The scene transition owner is inactive."));
            return;
        }
        if (transition != null)
        {
            failed?.Invoke(new InvalidOperationException("A scene transition is already pending."));
            return;
        }
        if (initialFade != null)
        {
            StopCoroutine(initialFade);
            initialFade = null;
        }
        pendingFailure = failed;
        transition = StartCoroutine(LoadSceneWithTransition(sceneName, completed, failed));
    }

    private IEnumerator LoadSceneWithTransition(string sceneName, Action completed, Action<Exception> failed)
    {
        // Ensure even a missing overlay has an asynchronous boundary owned by this coroutine.
        yield return null;
        yield return FadeTo(1f);
        AsyncOperation operation = null;
        Exception failure = null;
        try
        {
            operation = sceneLoaderOverrideForTests != null
                ? sceneLoaderOverrideForTests(sceneName)
                : SceneManager.LoadSceneAsync(sceneName);
            if (operation == null)
                throw new InvalidOperationException("The requested scene did not start loading.");
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        if (failure != null)
        {
            yield return FadeTo(0f);
            transition = null;
            pendingFailure = null;
            failed?.Invoke(failure);
            yield break;
        }
        while (!operation.isDone)
            yield return null;
        // Commit return-source state as soon as the destination is active, before further input.
        pendingFailure = null;
        completed?.Invoke();
        if (delayBeforeFadeIn > 0f)
            yield return new WaitForSecondsRealtime(delayBeforeFadeIn);
        yield return FadeTo(0f);
        transition = null;
    }

    private IEnumerator FadeTo(float target)
    {
        if (fadeOverlay == null)
            yield break;
        fadeOverlay.gameObject.SetActive(true);
        Color color = fadeOverlay.color;
        float start = color.a;
        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            color.a = Mathf.Lerp(start, target, Mathf.Clamp01(elapsed / fadeDuration));
            fadeOverlay.color = color;
            yield return null;
        }
        color.a = target;
        fadeOverlay.color = color;
        if (target == 0f)
            fadeOverlay.gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        if (transition != null)
            StopCoroutine(transition);
        if (initialFade != null)
            StopCoroutine(initialFade);
        transition = null;
        initialFade = null;
        if (fadeOverlay != null)
        {
            Color color = fadeOverlay.color;
            color.a = 0f;
            fadeOverlay.color = color;
            fadeOverlay.gameObject.SetActive(false);
        }
        Action<Exception> failed = pendingFailure;
        pendingFailure = null;
        failed?.Invoke(new OperationCanceledException("The scene transition owner was disabled."));
    }
}
