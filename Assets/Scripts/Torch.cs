using UnityEngine;

public class Torch : MonoBehaviour
{
    public bool isLit;
    public Renderer rend;
    public Material normalMaterial;
    public Material fireMaterial;

    public void SetLit()
    {
        if (isLit) return;
        isLit = true;
        if (rend != null && fireMaterial != null)
            rend.material = fireMaterial;
    }
}

