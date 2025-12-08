using UnityEngine;

public class FireSource : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        Torch torch = other.GetComponent<Torch>();
        if (torch != null)
            torch.SetLit();
    }
}
