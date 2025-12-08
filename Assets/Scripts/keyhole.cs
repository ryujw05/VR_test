using UnityEngine;

public class KeyholeTrigger : MonoBehaviour
{
    public GameObject quad;
    public GameObject[] appearObjects;
    public string keyTag = "Key";

    void Start()
    {
        if (appearObjects != null)
        {
            foreach (var obj in appearObjects)
            {
                if (obj != null)
                    obj.SetActive(false);
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(keyTag)) return;

        if (quad != null)
            quad.SetActive(false);

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
