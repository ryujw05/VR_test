using UnityEngine;

public class LocalGrabController : MonoBehaviour
{
    [Header("References")]
    public NetworkPlayerSync nps;      // NetworkPlayerSync
    public Transform handTransform;     // 내 손 (로컬 AR/VR 손 프리팹)

    [Header("Grab Settings")]
    public float grabRadius = 0.15f;    // 손 주변 반경 탐지
    public LayerMask itemMask;          // 아이템 레이어 (Interactable)

    private SimpleGrabbedItem grabbedItem;

    void Update()
    {
        // 이미 잡고 있다면 → 손 위치로 계속 이동
        if (grabbedItem != null)
        {
            grabbedItem.transform.position = handTransform.position;
            grabbedItem.transform.rotation = handTransform.rotation;
        }

        // 임시: 스페이스바로 Grab/Release 테스트
        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (grabbedItem == null)
                TryGrab();
            else
                Release();
        }
    }

    public void TryGrab()
    {
        // 이미 잡고 있으면 무시
        if (grabbedItem != null) return;

        Collider[] hits = Physics.OverlapSphere(handTransform.position, grabRadius, itemMask);
        if (hits.Length == 0) return;

        // 가장 가까운 SimpleGrabbedItem 찾기
        SimpleGrabbedItem candidate = null;
        float bestDist = float.MaxValue;

        foreach (var c in hits)
        {
            if (c.TryGetComponent(out SimpleGrabbedItem item))
            {
                float d = Vector3.Distance(c.transform.position, handTransform.position);
                if (d < bestDist)
                {
                    bestDist = d;
                    candidate = item;
                }
            }
        }

        if (candidate == null) return;

        // === 로컬 Grab 시작 ===
        grabbedItem = candidate;
        grabbedItem.OnLocalGrabStart();

        // 서버로 보낼 grabbedItemId 설정
        nps.currentGrabbedItemId = grabbedItem.itemId;

        Debug.Log("[Grab] Local grabbed: " + grabbedItem.itemId);
    }

    public void Release()
    {
        if (grabbedItem == null) return;

        // 손 속도 측정 → 던지기 구현 가능 (지금은 0)
        Vector3 throwVel = Vector3.zero;

        grabbedItem.OnLocalGrabEnd(throwVel);

        Debug.Log("[Grab] Local release: " + grabbedItem.itemId);

        grabbedItem = null;

        // === 서버로 '아무것도 안 잡음' 전송 ===
        nps.currentGrabbedItemId = "";
    }
}
