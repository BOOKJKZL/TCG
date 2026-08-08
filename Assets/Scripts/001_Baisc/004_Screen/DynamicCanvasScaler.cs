using UnityEngine;
using UnityEngine.UI;

public sealed class DynamicCanvasScaler : MonoBehaviour
{
    private static readonly Vector2 DesignReferenceResolution = new Vector2(1000f, 2000f);

    private void Start()
    {
        AdjustCanvasScaler();
    }

    public void AdjustCanvasScaler()
    {
        CanvasScaler scaler = GetComponent<CanvasScaler>();
        if (scaler == null)
            return;

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        if (scaler.referenceResolution.x <= 0f || scaler.referenceResolution.y <= 0f)
            scaler.referenceResolution = DesignReferenceResolution;

        // The reference is a design scale, not a viewport contract. A balanced
        // match lets the Canvas occupy every supported aspect ratio without
        // reintroducing the retired 1:2 camera crop.
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
    }
}
