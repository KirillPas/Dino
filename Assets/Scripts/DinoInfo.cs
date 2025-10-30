using UnityEngine;

public class DinoInfo : MonoBehaviour
{
    public GameObject sphere;
    public Material newMaterial;
    public Canvas targetCanvas;

    private Material originalMaterial;
    private Renderer sphereRenderer;

    private void Start()
    {
        if (sphere != null)
        {
            sphereRenderer = sphere.GetComponent<Renderer>();
            if (sphereRenderer != null)
                originalMaterial = sphereRenderer.material;
        }

        if (targetCanvas != null)
            targetCanvas.enabled = false;
    }
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            if (sphereRenderer != null && newMaterial != null)
                sphereRenderer.material = newMaterial;

            if (targetCanvas != null)
                targetCanvas.enabled = true;
        }
    }

}
