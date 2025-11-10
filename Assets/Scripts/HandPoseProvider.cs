using UnityEngine;
using MediapipeHandTracking;

public class HandPoseProvider : MonoBehaviour
{
    private const int WRIST = 0;

    public bool TryGetHandWorld(out Vector3 v)
    {
        v = default;
        if (ARHandProcessor.Instance == null) return false;
        v = ARHandProcessor.Instance.CurrentHand.GetLandmark(WRIST);
        return true;
    }
}
