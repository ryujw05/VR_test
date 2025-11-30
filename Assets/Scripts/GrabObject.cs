using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class GrabObject : MonoBehaviour
{
    [Header("Hand Tips")]
    public GameObject F1; // 엄지 (Landmark 4)
    public GameObject F2; // 검지 (Landmark 8)

    [Header("Settings")]
    public float grab_threshold = 0.05f;
    public float grab_radius = 0.1f;
    public LayerMask grabbableLayer;

    private GameObject currentHeldObject = null;
    private NetworkPlayerSync currentNps = null;
    
    // [수정] Item 대신 SimpleGrabbedItem 사용 (오류 해결)
    private SimpleGrabbedItem currentItem = null; 
    
    private Vector3 grabOffset;

    void Start()
    {
        if (F1 == null || F2 == null)
        {
            if (Manager.instance != null && Manager.instance.HandOnSpace != null)
            {
                Transform handRoot = Manager.instance.HandOnSpace.transform;
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

        // 잡고 있는 물체가 없을 때 -> 잡기 시도
        if (currentHeldObject == null)
        {
            if (dist < grab_threshold)
            {
                TryGrab(center);
            }
        }
        // 잡고 있는 중 -> 위치 동기화 및 놓기 체크
        else
        {
            if (dist < grab_threshold)
            {
                currentHeldObject.transform.position = center + grabOffset;
            }
            else
            {
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
            NetworkPlayerSync nps = nearest.GetComponent<NetworkPlayerSync>();
            // [수정] SimpleGrabbedItem 컴포넌트 가져오기
            SimpleGrabbedItem item = nearest.GetComponent<SimpleGrabbedItem>();

            if (nps != null && item != null)
            {
                // [체크] 남이 잡고 있는지 확인 (isRemotelyGrabbed는 item 안에 있음)
                if (item.isRemotelyGrabbed) return;

                currentHeldObject = nearest.gameObject;
                currentNps = nps;
                currentItem = item;

                grabOffset = currentHeldObject.transform.position - center;

                // ★ 요청하신 기능 1: 잡기 시작 함수 호출
                currentItem.OnLocalGrabStart();

                // ★ 요청하신 기능 2: ID 전달 (string ID)
                currentNps.currentGrabbedItemId = currentItem.itemId;
            }
        }
    }

    void ReleaseObject()
    {
        if (currentItem != null)
        {
            // ★ 요청하신 기능 3: 놓기 함수 호출 (Vector3.zero 전달)
            currentItem.OnLocalGrabEnd(Vector3.zero);
        }

        currentHeldObject = null;
        currentNps = null;
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