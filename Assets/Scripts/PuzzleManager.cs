using UnityEngine;

public class PuzzleManager : MonoBehaviour
{
    public static PuzzleManager Instance { get; private set; }

    public PuzzleHole[] holes;
    public GameObject clearObject;
    public GameObject key;

    public float fadeDuration = 1.5f;

    bool cleared = false;

    void Awake()
    {
        Instance = this;

        if (clearObject != null)
            clearObject.SetActive(false);

        if (key != null)
            key.SetActive(false);
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

        if (clearObject != null)
            clearObject.SetActive(true);

        if (key != null)
        {
            key.SetActive(true);
            StartCoroutine(FadeInKey());
        }
    }

    System.Collections.IEnumerator FadeInKey()
    {
        Renderer[] rends = key.GetComponentsInChildren<Renderer>();
        float t = 0f;

        foreach (var r in rends)
        {
            foreach (var mat in r.materials)
            {
                Color c = mat.color;
                c.a = 0f;
                mat.color = c;
            }
        }

        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            float a = t / fadeDuration;

            foreach (var r in rends)
            {
                foreach (var mat in r.materials)
                {
                    Color c = mat.color;
                    c.a = a;
                    mat.color = c;
                }
            }

            yield return null;
        }

        foreach (var r in rends)
        {
            foreach (var mat in r.materials)
            {
                Color c = mat.color;
                c.a = 1f;
                mat.color = c;
            }
        }
    }
}
