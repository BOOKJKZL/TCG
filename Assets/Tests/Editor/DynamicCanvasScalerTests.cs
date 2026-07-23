using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

public class DynamicCanvasScalerTests
{
    [Test]
    public void AdjustCanvasScaler_UsesExistingReferenceWhenGameManagerIsMissing()
    {
        GameObject canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));

        try
        {
            CanvasScaler canvasScaler = canvasObject.GetComponent<CanvasScaler>();
            canvasScaler.referenceResolution = new Vector2(1000f, 2000f);
            DynamicCanvasScaler dynamicScaler = canvasObject.AddComponent<DynamicCanvasScaler>();

            Assert.DoesNotThrow(dynamicScaler.AdjustCanvasScaler);
            Assert.That(canvasScaler.referenceResolution, Is.EqualTo(new Vector2(1000f, 2000f)));
        }
        finally
        {
            Object.DestroyImmediate(canvasObject);
        }
    }
}
