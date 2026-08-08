using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public sealed class ResolutionManager : MonoBehaviour
{
    private static readonly Rect FullScreenViewport = new Rect(0f, 0f, 1f, 1f);

    private void Awake()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        Camera.onPreCull += NormalizeCamera;
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        ApplyFullScreenViewports();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Camera.onPreCull -= NormalizeCamera;
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ApplyFullScreenViewports();
    }

    public void ApplyFullScreenViewports()
    {
        foreach (Camera camera in Camera.allCameras)
            NormalizeCamera(camera);
    }

    public static void NormalizeCamera(Camera camera)
    {
        if (camera != null && !IsFullScreen(camera.rect))
            camera.rect = FullScreenViewport;
    }

    private static void OnBeginCameraRendering(ScriptableRenderContext _, Camera camera)
    {
        NormalizeCamera(camera);
    }

    public static bool IsFullScreen(Rect viewport)
    {
        return Mathf.Approximately(viewport.x, 0f) &&
               Mathf.Approximately(viewport.y, 0f) &&
               Mathf.Approximately(viewport.width, 1f) &&
               Mathf.Approximately(viewport.height, 1f);
    }
}
