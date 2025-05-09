using UnityEngine;
using UnityEngine.UI;

public class DynamicCanvasScaler : MonoBehaviour
{
    void Start()
    {
        AdjustCanvasScaler();
    }

    void AdjustCanvasScaler()
    {
        ResolutionManager manager = GameManager.Instance.resolutionManager;
        CanvasScaler scaler = GetComponent<CanvasScaler>();

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(manager.resolutionWidth, manager.resolutionHeight);

        // Get screen dimensions
        float screenWidth = Screen.width;
        float screenHeight = Screen.height;

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
