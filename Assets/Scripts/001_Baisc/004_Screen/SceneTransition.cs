using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class SceneTransition : MonoBehaviour
{
    public Image fadeOverlay; // Reference to the UI Image for fade effect
    public float fadeDuration = 0.5f; // Duration for fade in/out
    public float delayBeforeFadeIn = 1f; // Delay before fade-out starts

    private void Start()
    {
        // Start with fade in effect
        StartCoroutine(FadeIn());
    }

    // String version
    public void ChangeScene(string sceneName)
    {
        StartCoroutine(LoadSceneWithTransition(sceneName));
    }

    private IEnumerator LoadSceneWithTransition(string sceneName)
    {
        // Start fade-out animation
        yield return StartCoroutine(FadeOut());

        // Begin loading the scene asynchronously
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName);
        asyncLoad.allowSceneActivation = false;

        // Wait until the scene is almost loaded
        while (asyncLoad.progress < 0.9f)
        {
            yield return null;
        }

        // Activate the scene
        asyncLoad.allowSceneActivation = true;

        // Wait for delay before fade-out
        yield return new WaitForSeconds(delayBeforeFadeIn);

        GameManager.Instance.loadManager.EndLoad();

        // Scene is ready; fade in after activation
        yield return StartCoroutine(FadeIn());
    }

    // Integer version
    public void ChangeScene(int sceneID)
    {
        StartCoroutine(LoadSceneWithTransition(sceneID));
    }

    private IEnumerator LoadSceneWithTransition(int sceneID)
    {
        // Start fade-out animation
        yield return StartCoroutine(FadeOut());

        // Begin loading the scene asynchronously
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneID);
        asyncLoad.allowSceneActivation = false;

        // Wait until the scene is almost loaded
        while (asyncLoad.progress < 0.9f)
        {
            yield return null;
        }

        // Activate the scene
        asyncLoad.allowSceneActivation = true;

        // Wait for delay before fade-out
        yield return new WaitForSeconds(delayBeforeFadeIn);


        GameManager.Instance.loadManager.EndLoad();

        // Scene is ready; fade in after activation
        yield return StartCoroutine(FadeIn());
    }

    private IEnumerator FadeIn()
    {
        fadeOverlay.gameObject.SetActive(true);
        float timer = 0f;
        Color overlayColor = fadeOverlay.color;
        overlayColor.a = 1f;

        while (timer < fadeDuration)
        {
            overlayColor.a = Mathf.Lerp(1f, 0f, timer / fadeDuration);
            fadeOverlay.color = overlayColor;
            timer += Time.deltaTime;
            yield return null;
        }

        overlayColor.a = 0f;
        fadeOverlay.color = overlayColor;
        fadeOverlay.gameObject.SetActive(false); // Hide overlay after fade-in
    }

    private IEnumerator FadeOut()
    {
        fadeOverlay.gameObject.SetActive(true);
        float timer = 0f;
        Color overlayColor = fadeOverlay.color;
        overlayColor.a = 0f;

        while (timer < fadeDuration)
        {
            overlayColor.a = Mathf.Lerp(0f, 1f, timer / fadeDuration);
            fadeOverlay.color = overlayColor;
            timer += Time.deltaTime;
            yield return null;
        }

        overlayColor.a = 1f;
        fadeOverlay.color = overlayColor;
    }
}
