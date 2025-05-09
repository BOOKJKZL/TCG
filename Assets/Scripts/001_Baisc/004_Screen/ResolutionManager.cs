using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class ResolutionManager : MonoBehaviour
{
    public int resolutionWidth = 1000;
    public int resolutionHeight = 2000 ;

    // Dictionary to store original rect values for each camera
    private Dictionary<Camera, Rect> originalCameraRects = new Dictionary<Camera, Rect>();

    void Awake()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;

        // Store the original rect for each camera at startup
        StoreOriginalCameraRects();
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Reset all cameras to original rects and then apply the fixed resolution
        StoreOriginalCameraRects();
        SetFixedResolution(resolutionWidth, resolutionHeight);
    }

    void StoreOriginalCameraRects()
    {
        originalCameraRects.Clear();
        Camera[] cameras = Camera.allCameras;
        foreach (Camera camera in cameras)
        {
            if (camera != null)
            {
                originalCameraRects[camera] = camera.rect;
            }
        }
    }

    void SetFixedResolution(int targetWidth, int targetHeight)
    {
        // Calculate the target aspect ratio
        float targetAspect = (float)targetWidth / targetHeight;
        // Calculate the current screen aspect ratio
        float screenAspect = (float)Screen.width / Screen.height;

        foreach (KeyValuePair<Camera, Rect> entry in originalCameraRects)
        {
            Camera camera = entry.Key;
            Rect originalRect = entry.Value;

            if (screenAspect >= targetAspect)
            {
                // Screen is wider than the target aspect ratio, add pillarboxing (black bars on the sides)
                float scaleWidth = targetAspect / screenAspect;
                float insetX = (1.0f - scaleWidth) / 2.0f;
                camera.rect = new Rect(
                    originalRect.x * scaleWidth + insetX,
                    originalRect.y,
                    originalRect.width * scaleWidth,
                    originalRect.height
                );
            }
            else
            {
                // Screen is taller than the target aspect ratio, add letterboxing (black bars on top and bottom)
                float scaleHeight = screenAspect / targetAspect;
                float insetY = (1.0f - scaleHeight) / 2.0f;
                camera.rect = new Rect(
                    originalRect.x,
                    originalRect.y * scaleHeight + insetY,
                    originalRect.width,
                    originalRect.height * scaleHeight
                );
            }
        }
    }
}
