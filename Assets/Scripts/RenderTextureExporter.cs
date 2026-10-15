using UnityEngine;
using System.IO;
using System.Collections;

public class RenderTextureExporter : MonoBehaviour
{
    public RenderTexture renderTexture; // Assign in Inspector
    public string fileName = "MapExport.png";

    public IEnumerator Start()
    {
        yield return new WaitForSeconds(10f);
        ExportRenderTexture();
    }

    public void ExportRenderTexture()
    {
        // Set the RenderTexture as active
        RenderTexture.active = renderTexture;

        // Create a new Texture2D the same size as the RenderTexture
        Texture2D tex = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGBA32, false);

        // Read the pixels into the Texture2D
        tex.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
        tex.Apply();

        // Encode texture to PNG
        byte[] pngData = tex.EncodeToPNG();

        // Save PNG to disk (e.g., project root or persistent data path)
        string path = Path.Combine(Application.dataPath, fileName);
        File.WriteAllBytes(path, pngData);

        Debug.Log("Exported to " + path);

        // Cleanup
        RenderTexture.active = null;
        Destroy(tex);
    }
}
