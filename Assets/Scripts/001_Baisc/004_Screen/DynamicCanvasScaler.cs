using UnityEngine;
using UnityEngine.UI;

public class DynamicCanvasScaler : MonoBehaviour
{
    void Start()
    {
        AdjustCanvasScaler();
    }

    public void AdjustCanvasScaler()
    {
        CanvasScaler scaler = GetComponent<CanvasScaler>();
        if (scaler == null)
            return;

        ResolutionManager manager = GameManager.Instance != null
            ? GameManager.Instance.resolutionManager
            : null;
        Vector2 targetResolution = manager != null && manager.resolutionWidth > 0 && manager.resolutionHeight > 0
            ? new Vector2(manager.resolutionWidth, manager.resolutionHeight)
            : scaler.referenceResolution;
        if (targetResolution.x <= 0f || targetResolution.y <= 0f)
            targetResolution = new Vector2(1000f, 2000f);

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = targetResolution;

        // Get screen dimensions
        float screenWidth = Screen.width;
        float screenHeight = Mathf.Max(1f, Screen.height);

        // Determine aspect ratio
        float screenAspectRatio = screenWidth / screenHeight;
        float referenceAspectRatio = scaler.referenceResolution.x / scaler.referenceResolution.y;

        // Choose match mode based on aspect ratio
        if (screenAspectRatio >= referenceAspectRatio)
        {
            // Match Width
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f; // Match Height
        }
        else
        {
            // Match Height
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0f; // Match Width
        }
    }
}
