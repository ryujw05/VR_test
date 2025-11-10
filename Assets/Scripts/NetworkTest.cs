// 아무 새 스크립트(예: CloudAnchorAutoHost.cs)를 빈 오브젝트에 붙여서 사용
using UnityEngine;
public class CloudAnchorAutoHost : MonoBehaviour
{
    void Start()
    {
        var som = FindObjectOfType<SharedOriginManager>();
        var cam = Camera.main;
        if (som && cam)
            som.HostAtPose(cam.transform.position, cam.transform.rotation, 1); // TTL=1일
    }
}
