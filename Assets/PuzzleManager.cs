using UnityEngine;

public class PuzzleManager : MonoBehaviour
{
    public static PuzzleManager Instance { get; private set; }

    public PuzzleHole[] holes;
    public GameObject clearObject;

    private bool cleared = false;

    private void Awake()
    {
        Instance = this;

        if (clearObject != null)
            clearObject.SetActive(false);
    }

    public void CheckClear()
    {
        if (cleared) return;
        if (holes == null || holes.Length == 0) return;

        foreach (var h in holes)
        {
            if (h == null || !h.isFilled)
                return;
        }

        cleared = true;
        Debug.Log("ÆÛÁñ Å¬¸®¾î!");

        if (clearObject != null)
            clearObject.SetActive(true);
    }
}
