using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GrabObject : MonoBehaviour
{
    [Header("Hand Tips")]
    public GameObject F1; // 엄지 (Landmark 4)
    public GameObject F2; // 검지 (Landmark 8)

    [Header("Settings")]
    [Tooltip("물체를 잡기 위해 오므려야 하는 거리 (좁게 설정)")]
    public float grab_threshold = 0.05f;

    [Tooltip("물체를 놓기 위해 벌려야 하는 거리 (넓게 설정 -> 떨림 방지)")]
    public float release_threshold = 0.10f;

    public float grab_radius = 0.1f;
    public LayerMask grabbableLayer;

    // 내부 변수
    private GameObject currentHeldObject = null;
    private SimpleGrabbedItem currentItem = null;
    private Vector3 grabOffset;

    void Start()
    {
        // 손가락 랜드마크 자동 할당 (기존 로직 유지)
        if (F1 == null || F2 == null)
        {
            if (Manager.instance != null && Manager.instance.HandOnSpace != null)
            {
                Transform handRoot = Manager.instance.HandOnSpace.transform;
                // 인덱스 4: 엄지 끝, 8: 검지 끝
                if (handRoot.childCount > 8)
                {
                    F1 = handRoot.GetChild(4).gameObject;
                    F2 = handRoot.GetChild(8).gameObject;
                }
            }
        }
    }

    void Update()
    {
        if (F1 == null || F2 == null) return;

        float dist = Vector3.Distance(F1.transform.position, F2.transform.position);
        Vector3 center = (F1.transform.position + F2.transform.position) / 2;

        // 1. 현재 잡고 있는 물체가 없을 때 -> 잡기 시도 (좁은 threshold)
        if (currentHeldObject == null)
        {
            if (dist < grab_threshold)
            {
                TryGrab(center);
            }
        }
        // 2. 잡고 있을 때 -> 이동 동기화 및 놓기 체크 (넓은 threshold)
        else
        {
            // 잡는 거리보다 놓는 거리를 크게 두어(히스테리시스), 경계선에서 깜빡거리는 현상 방지
            if (dist < release_threshold)
            {
                if (currentHeldObject != null)
                {
                    // 손 위치에 따라 물체 이동 (물리 무시하고 강제 동기화)
                    currentHeldObject.transform.position = center + grabOffset;

                    // (선택사항) 회전도 손 방향에 맞추려면 아래 주석 해제
                    // Quaternion targetRot = Quaternion.LookRotation(F2.transform.position - F1.transform.position);
                    // currentHeldObject.transform.rotation = Quaternion.Slerp(currentHeldObject.transform.rotation, targetRot, Time.deltaTime * 10f);
                }
            }
            else
            {
                // 손가락이 release_threshold 이상 벌어지면 놓기
                ReleaseObject();
            }
        }
    }

    void TryGrab(Vector3 center)
    {
        Collider[] colliders = Physics.OverlapSphere(center, grab_radius, grabbableLayer);

        Collider nearest = null;
        float minDist = float.MaxValue;

        foreach (var col in colliders)
        {
            float d = Vector3.Distance(center, col.transform.position);
            if (d < minDist)
            {
                minDist = d;
                nearest = col;
            }
        }

        if (nearest != null)
        {
            // ★ 중요 1: 부모 오브젝트에 컴포넌트가 있을 수 있으므로 InParent 사용
            SimpleGrabbedItem item = nearest.GetComponentInParent<SimpleGrabbedItem>();

            // ★ 중요 2: 싱글톤 인스턴스 사용 (아이템에서 NPS를 찾지 않음)
            NetworkPlayerSync nps = NetworkPlayerSync.Instance;

            if (nps != null && item != null)
            {
                // 이미 다른 사람이 원격으로 잡고 있다면 패스
                if (item.isRemotelyGrabbed) return;

                currentHeldObject = item.gameObject;
                currentItem = item;

                // 자연스러운 잡기를 위해 현재 오프셋 저장
                grabOffset = currentHeldObject.transform.position - center;

                // 1. 로컬 상태 변경 (Kinematic 켜기)
                currentItem.OnLocalGrabStart();

                // 2. 서버 전송 시작 (NPS에 ID 등록)
                nps.currentGrabbedItemId = currentItem.itemId;

                // 로그 확인용
                // Debug.Log($"[GRAB] Success: {currentItem.itemId}");
            }
        }
    }

    void ReleaseObject()
    {
        if (currentItem != null)
        {
            // 1. 물리력 복구 (속도 0으로 얌전히 놓기)
            currentItem.OnLocalGrabEnd(Vector3.zero);

            // 로그 확인용
            // Debug.Log($"[RELEASE] Dropped: {currentItem.itemId}");
        }

        // ★ 중요 3: 여기서 nps.currentGrabbedItemId = "" 를 절대 하지 않습니다.
        // 이유: 여기서 지우면 떨어지는 동안(낙하 중) 위치 전송이 끊겨버립니다.
        // NPS가 속도를 감지해서 정지하면 알아서 지우도록 되어 있습니다.

        currentHeldObject = null;
        currentItem = null;
    }

    void OnDrawGizmos()
    {
        if (F1 != null && F2 != null)
        {
            Vector3 center = (F1.transform.position + F2.transform.position) / 2;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(center, grab_radius);
        }
    }
}