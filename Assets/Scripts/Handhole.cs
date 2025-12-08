using UnityEngine;

public class HandHole : MonoBehaviour
{
    public float requiredStayTime = 3f;
    public bool isCleared;

    float timer;
    bool isInside;
    Collider current;

    void OnTriggerEnter(Collider other)
    {
        if (isCleared) return;
        isInside = true;
        current = other;
        timer = 0f;
    }

    void OnTriggerExit(Collider other)
    {
        if (isCleared) return;
        if (other != current) return;
        isInside = false;
        current = null;
        timer = 0f;
    }

    void Update()
    {
        if (isCleared) return;
        if (!isInside) return;

        timer += Time.deltaTime;
        if (timer >= requiredStayTime)
        {
            isCleared = true;
            isInside = false;

            if (HandPuzzleManager.Instance != null)
                HandPuzzleManager.Instance.CheckClear();
        }
    }
}
