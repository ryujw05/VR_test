using UnityEngine;

public class HandPuzzleManager : MonoBehaviour
{
    public static HandPuzzleManager Instance { get; private set; }

    public HandHole[] handHoles;
    public GameObject clearObject;
    public GameObject hand;

    bool cleared;

    void Awake()
    {
        Instance = this;

        if (clearObject != null)
            clearObject.SetActive(false);
    }

    public void CheckClear()
    {
        if (cleared) return;
        if (handHoles == null || handHoles.Length == 0) return;

        foreach (var h in handHoles)
        {
            if (h == null || !h.isCleared)
                return;
        }

        cleared = true;

        if (clearObject != null)
            clearObject.SetActive(true);
    }
}
