using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

enum LoadType { TEXT, IMAGE, SOUND, SCENE}

public class LoadManager : MonoBehaviour
{
    public GameObject loadingScreen;
    public SceneTransition fadeblackScreen;
    Fade fade;
    [HideInInspector] public bool load = false;

    public float sceneChangeDelay = 1f; // Delay before activating the scene (in seconds)
    private int loadCounter = 0; // Counter for active load operations

    void Awake()
    {
        if(fade == null)
        {
            fade = loadingScreen != null ? loadingScreen.GetComponent<Fade>() : null;
        }

        if (fadeblackScreen == null)
        {
            fadeblackScreen = gameObject.GetComponent<SceneTransition>();
        }
    }

    /* -------Example--------
    public string LoadText(string path)
    {
        string data = null;

        LoadText(path, (string textContent) =>
        {
            if (textContent != null)
            {
                data = textContent;
            }
        });
        return data;
    }

    public Texture2D LoadImage(string path)
    {
        Texture2D data = null;

        LoadImage(path, (Texture2D texture) => {
            if (texture != null)
            {
                data = texture;
            }
        });
        return data;
    }

    public AudioClip LoadAudio(string path)
    {
        AudioClip data = null;

        LoadAudio(path, (AudioClip audioClip) => {
            if (audioClip != null)
            {
                data = audioClip;
            }
        });
        return data;
    }
    */

    // Load text file asynchronously
    public void LoadText(string textFilePath, System.Action<string> callback)
    {
        StartLoad();
        StartCoroutine(LoadTextAsync(textFilePath, callback));
    }

    private IEnumerator LoadTextAsync(string textFilePath, System.Action<string> callback)
    {
        ResourceRequest request = Resources.LoadAsync<TextAsset>(textFilePath);
        while (!request.isDone)
        {
            yield return null;
        }
        EndLoad();

        if (request.asset != null && request.asset is TextAsset textAsset)
        {
            callback?.Invoke(textAsset.text);
        }
        else
        {
            Debug.LogError("Failed to load text file: " + textFilePath);
            callback?.Invoke(null);
        }
    }

    // Load image asynchronously
    public void LoadImage(string imagePath, System.Action<Texture2D> callback)
    {
        StartLoad();
        StartCoroutine(LoadImageAsync(imagePath, callback));
    }

    private IEnumerator LoadImageAsync(string imagePath, System.Action<Texture2D> callback)
    {
        ResourceRequest request = Resources.LoadAsync<Texture2D>(imagePath);
        while (!request.isDone)
        {
            yield return null;
        }
        EndLoad();

        if (request.asset != null && request.asset is Texture2D texture)
        {
            callback?.Invoke(texture);
        }
        else
        {
            Debug.LogError("Failed to load image: " + imagePath);
            callback?.Invoke(null);
        }
    }

    // Load audio clip asynchronously
    public void LoadAudio(string audioPath, System.Action<AudioClip> callback)
    {
        StartLoad();
        StartCoroutine(LoadAudioAsync(audioPath, callback));
    }

    private IEnumerator LoadAudioAsync(string audioPath, System.Action<AudioClip> callback)
    {
        ResourceRequest request = Resources.LoadAsync<AudioClip>(audioPath);
        while (!request.isDone)
        {
            yield return null;
        }
        EndLoad();

        if (request.asset != null && request.asset is AudioClip audioClip)
        {
            callback?.Invoke(audioClip);
        }
        else
        {
            Debug.LogError("Failed to load audio clip: " + audioPath);
            callback?.Invoke(null);
        }
    }

    // Load scene asynchronously with transition
    public void LoadScene(string sceneName)
    {
        LoadScene(sceneName, null, exception => Debug.LogWarning("Scene navigation failed: " + exception));
    }

    public void LoadScene(string sceneName, System.Action completed, System.Action<System.Exception> failed)
    {
        bool settled = false;
        StartLoad();
        void Finish(System.Exception error)
        {
            if (settled)
                return;
            settled = true;
            EndLoad();
            if (error == null)
                completed?.Invoke();
            else
                failed?.Invoke(error);
        }
        try
        {
            if (fadeblackScreen != null)
                fadeblackScreen.ChangeScene(sceneName, () => Finish(null), Finish);
            else
            {
                SceneManager.LoadScene(sceneName);
                Finish(null);
            }
        }
        catch (System.Exception exception)
        {
            Finish(exception);
        }
    }

    public void LoadScene(int sceneID)
    {
        LoadScene(SceneUtility.GetScenePathByBuildIndex(sceneID));
    }

    public void StartLoad()
    {
        loadCounter++;
        if (loadCounter == 1)
        {
            if (loadingScreen != null)
                loadingScreen.SetActive(true);
            load = true;
            fade?.StartFadeIn();
        }
    }

    public void EndLoad()
    {
        loadCounter = Mathf.Max(0, loadCounter - 1);
        if (loadCounter == 0)
        {
            load = false;
            void HideLoading()
            {
                if (loadCounter == 0 && loadingScreen != null)
                    loadingScreen.SetActive(false);
            }
            if (fade != null && fade.isActiveAndEnabled)
                fade.StartFadeOut(HideLoading);
            else
                HideLoading();
        }
    }
}
