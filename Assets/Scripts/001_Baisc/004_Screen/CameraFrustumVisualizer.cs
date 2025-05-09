using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class CameraFrustumVisualizer : MonoBehaviour
{
    public Camera targetCamera; // Target camera
    public Color lineColor = Color.green; // Line color

    private LineRenderer lineRenderer;

    void Start()
    {
        // Initialize LineRenderer
        lineRenderer = GetComponent<LineRenderer>();
        lineRenderer.positionCount = 5; // Four corners + closing line
        lineRenderer.loop = true; // Ensure it forms a closed shape
        lineRenderer.startWidth = 0.5f;
        lineRenderer.endWidth = 0.5f;
        lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        lineRenderer.startColor = lineColor;
        lineRenderer.endColor = lineColor;
    }

    void Update()
    {
        if (targetCamera == null) return;

        // Calculate the four corners of the camera frustum
        Vector3[] corners = GetCameraFrustumCorners();
        for (int i = 0; i < corners.Length; i++)
        {
            lineRenderer.SetPosition(i, corners[i]);
        }
    }

    Vector3[] GetCameraFrustumCorners()
    {
        Vector3[] corners = new Vector3[5]; // Four corners + closing point

        if (targetCamera.orthographic)
        {
            // Orthographic camera
            float size = targetCamera.orthographicSize;
            float aspect = targetCamera.aspect;

            float halfWidth = size * aspect;
            float halfHeight = size;

            // Calculate the corners in world space
            corners[0] = targetCamera.transform.position + targetCamera.transform.forward * targetCamera.nearClipPlane
                         + targetCamera.transform.up * halfHeight - targetCamera.transform.right * halfWidth;
            corners[1] = targetCamera.transform.position + targetCamera.transform.forward * targetCamera.nearClipPlane
                         + targetCamera.transform.up * halfHeight + targetCamera.transform.right * halfWidth;
            corners[2] = targetCamera.transform.position + targetCamera.transform.forward * targetCamera.nearClipPlane
                         - targetCamera.transform.up * halfHeight + targetCamera.transform.right * halfWidth;
            corners[3] = targetCamera.transform.position + targetCamera.transform.forward * targetCamera.nearClipPlane
                         - targetCamera.transform.up * halfHeight - targetCamera.transform.right * halfWidth;
            corners[4] = corners[0]; // Closing line
        }
        else
        {
            // Perspective camera
            Plane nearPlane = new Plane(targetCamera.transform.forward, targetCamera.transform.position + targetCamera.transform.forward * targetCamera.nearClipPlane);
            float distance = Mathf.Abs(nearPlane.GetDistanceToPoint(targetCamera.transform.position));

            // Use Camera.CalculateFrustumCorners to get frustum corners
            Vector3[] frustumCorners = new Vector3[4];
            targetCamera.CalculateFrustumCorners(new Rect(0, 0, 1, 1), distance, Camera.MonoOrStereoscopicEye.Mono, frustumCorners);

            for (int i = 0; i < 4; i++)
            {
                corners[i] = targetCamera.transform.position + targetCamera.transform.TransformVector(frustumCorners[i]);
            }
            corners[4] = corners[0]; // Closing line
        }

        return corners;
    }
}
