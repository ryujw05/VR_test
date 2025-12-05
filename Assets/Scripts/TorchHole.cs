using UnityEngine;

public class TorchHole : MonoBehaviour
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
        if (TorchPuzzleManager.Instance != null)
            TorchPuzzleManager.Instance.CheckClear();
    }

    private void OnTriggerEnter(Collider other)
    {
        Torch torch = other.GetComponent<Torch>();
        if (torch == null) return;
        if (!torch.isLit) return;
        SetLit();
    }
}
