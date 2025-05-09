using System.Collections;
using UnityEngine;

public class Fade : MonoBehaviour
{
    CanvasGroup canvasGroup;
    public float fadeDuration = 1f;
    private Coroutine fadeCoroutine;

    void Awake()
    {
        Init();
    }

    void Start()
    {
        Init();
    }

    private void Init()
    {
        canvasGroup = gameObject.GetComponent<CanvasGroup>();
        if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();
        canvasGroup.interactable = false;
        canvasGroup.alpha = 0; // Start hidden
    }

    // Coroutine-compatible smooth FadeIn
    public IEnumerator FadeInCoroutine()
    {
        float elapsedTime = 0f;
        float startAlpha = canvasGroup.alpha;

        while (elapsedTime < fadeDuration)
        {
            elapsedTime += Time.deltaTime;
            canvasGroup.alpha = Mathf.Lerp(startAlpha, 1, elapsedTime / fadeDuration);
            yield return null;
        }
        canvasGroup.alpha = 1; // Ensure it's fully visible
        canvasGroup.interactable = true;
    }

    // Coroutine-compatible smooth FadeOut
    public IEnumerator FadeOutCoroutine(System.Action callback = null)
    {
        float elapsedTime = 0f;
        float startAlpha = canvasGroup.alpha;

        while (elapsedTime < fadeDuration)
        {
            elapsedTime += Time.deltaTime;
            canvasGroup.alpha = Mathf.Lerp(startAlpha, 0, elapsedTime / fadeDuration);
            yield return null;
        }
        canvasGroup.alpha = 0; // Ensure it's fully hidden
        canvasGroup.interactable = false;
        callback?.Invoke();
    }

    // Direct methods if used without coroutine
    public void StartFadeIn()
    {
        if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
        fadeCoroutine = StartCoroutine(FadeInCoroutine());
    }

    public void StartFadeOut(System.Action callback)
    {
        if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
        fadeCoroutine = StartCoroutine(FadeOutCoroutine(callback));
    }
}
