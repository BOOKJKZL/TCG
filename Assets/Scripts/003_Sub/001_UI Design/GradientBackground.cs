using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Image))]
[ExecuteAlways] // Ensures the script runs in Edit mode
public class GradientBackground : MonoBehaviour
{
    public Gradient gradient;
    public int textureWidth = 256;
    public float speed = 0.1f;
    public float angle = 0f; // Angle in degrees
    public bool loop;
    public bool followAngle = false;

    private Texture2D gradientTexture;
    private Material imageMaterial;
    private Vector2 offset = Vector2.zero;

    private void Start()
    {
        if (!Application.isPlaying)
        {
            InitializeGradient(); // Update gradient preview in the editor
        }

        InitializeComponents();
    }

    private void Update()
    {
        // Only move gradient in Play mode
        if (Application.isPlaying && loop)
        {
            if (!followAngle)
            {
                // Move the gradient in a loop by adjusting the texture offset
                offset.x += Time.deltaTime * speed;
                if (offset.x > 1f) offset.x -= 1f; // Reset offset to keep it looping smoothly
                imageMaterial.mainTextureOffset = new Vector2(offset.x, 0);
            }
            else
            {
                // Convert angle to radians
                float radians = angle * Mathf.Deg2Rad;

                // Calculate the movement direction based on angle
                Vector2 direction = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));

                // Update offset based on speed and direction
                offset += direction * speed * Time.deltaTime;

                // Ensure the offset loops smoothly
                offset.x %= 1f;
                offset.y %= 1f;

                // Apply the offset to the material
                imageMaterial.mainTextureOffset = offset;
            }
        }
    }

    private void InitializeGradient()
    {
        gradientTexture = GenerateGradientTexture();

        // Get or create the Image component
        Image image = GetComponent<Image>();

        // Create or reuse the material
        if (imageMaterial == null)
        {
            imageMaterial = new Material(Shader.Find("Unlit/Transparent"));
        }

        imageMaterial.mainTexture = gradientTexture;
        image.sprite = Sprite.Create(gradientTexture, new Rect(0, 0, textureWidth, textureWidth), Vector2.zero);
        image.type = Image.Type.Tiled;
        image.material = imageMaterial;
    }

    private Texture2D GenerateGradientTexture()
    {
        Texture2D texture = new Texture2D(textureWidth, textureWidth);
        texture.wrapMode = TextureWrapMode.Repeat; // Enable repeating for seamless looping

        // Convert angle to radians
        float radians = angle * Mathf.Deg2Rad;
        float cos = Mathf.Cos(radians);
        float sin = Mathf.Sin(radians);

        for (int x = 0; x < textureWidth; x++)
        {
            for (int y = 0; y < textureWidth; y++)
            {
                // Calculate the position along the gradient based on angle
                float t = (x * cos + y * sin) / (textureWidth - 1);
                t = Mathf.Clamp01(t); // Ensure t stays within 0-1 range

                Color color = gradient.Evaluate(t);
                texture.SetPixel(x, y, color);
            }
        }

        texture.Apply();
        return texture;
    }

    private void InitializeComponents()
    {
        // Get the Image component
        Image image = GetComponent<Image>();

        // Access existing material from the Image component
        imageMaterial = image.material;
        if (imageMaterial == null)
        {
            Debug.LogError("Image does not have a material assigned.");
            return;
        }

        // Access existing main texture from the material
        gradientTexture = imageMaterial.mainTexture as Texture2D;
        if (gradientTexture == null || gradientTexture.width != textureWidth)
        {
            // Generate a new texture if it doesn't exist or the size doesn't match
            gradientTexture = GenerateGradientTexture();
            imageMaterial.mainTexture = gradientTexture;
        }
    }

    // This function runs in the editor when a property is modified
    private void OnValidate()
    {
        if (!Application.isPlaying)
        {
            InitializeGradient(); // Update gradient preview in the editor
        }
    }
}
