using UnityEngine;

public class TorchPuzzleManager : MonoBehaviour
{
    public static TorchPuzzleManager Instance { get; private set; }

    public TorchHole[] torchHoles;
    public GameObject secondClearObject;

    public GameObject[] appearObjects;

    bool cleared;

    void Awake()
    {
        Instance = this;

        if (secondClearObject != null)
            secondClearObject.SetActive(true);

        if (appearObjects != null)
        {
            foreach (var obj in appearObjects)
            {
                if (obj != null)
                    obj.SetActive(false);
            }
        }
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

        if (appearObjects != null)
        {
            foreach (var obj in appearObjects)
            {
                if (obj != null)
                    obj.SetActive(true);
            }
        }
    }
}
