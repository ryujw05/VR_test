using UnityEngine;

public class TorchPuzzleManager : MonoBehaviour
{
    public static TorchPuzzleManager Instance { get; private set; }

    public TorchHole[] torchHoles;
    public GameObject secondClearObject;

    private bool cleared;

    private void Awake()
    {
        Instance = this;
        if (secondClearObject != null)
            secondClearObject.SetActive(true);
    }

    public void CheckClear()
    {
        if (cleared) return;
        if (torchHoles == null || torchHoles.Length == 0) return;

        foreach (var h in torchHoles)
        {
            if (!h.isLit)
                return;
        }

        cleared = true;

        if (secondClearObject != null)
            secondClearObject.SetActive(false);
    }
}
