using UnityEngine;

public class KeyholeTrigger : MonoBehaviour
{
    public GameObject quad;
    public string keyTag = "Key";

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(keyTag))
        {
            if (quad != null)
                quad.SetActive(false);
        }
    }
}
