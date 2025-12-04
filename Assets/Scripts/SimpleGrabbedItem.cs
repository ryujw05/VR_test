using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class SimpleGrabbedItem : MonoBehaviour
{
    public static readonly Dictionary<string, SimpleGrabbedItem> Registry = new();
    public static bool TryGet(string id, out SimpleGrabbedItem item) => Registry.TryGetValue(id, out item);

    [Header("ID")]
    public string itemId;

    [Header("Refs")]
    public Rigidbody rb;

    [Header("State")]
    public bool isLocallyGrabbed = false;
    public bool isRemotelyGrabbed = false; // 다른 플레이어가 손에 쥔 상태

    // ★ 추가: 네트워크로 위치만 동기화 중인지 여부 (바닥에 놓여있거나 던져진 상태)
    private bool isNetworkMoving = false;
    private float lastNetworkUpdateResponceTime = 0f;

    private void Awake()
    {
        if (!rb) rb = GetComponent<Rigidbody>();
        if (!string.IsNullOrEmpty(itemId)) Registry[itemId] = this;

        // 시작할 때는 물리 켜두기 (싱글 테스트 호환)
        rb.isKinematic = false;
        rb.useGravity = true;
    }

    private void OnDestroy()
    {
        if (!string.IsNullOrEmpty(itemId) && Registry.ContainsKey(itemId)) Registry.Remove(itemId);
    }

    private void Update()
    {
        // ★ 추가: 네트워크 업데이트가 끊기면(1초 이상) 다시 물리 적용 (안전장치)
        if (isNetworkMoving && !isLocallyGrabbed && !isRemotelyGrabbed)
        {
            if (Time.time - lastNetworkUpdateResponceTime > 1.0f)
            {
                isNetworkMoving = false;
                rb.isKinematic = false; // 다시 물리 켜기
                // Debug.Log($"[Item] {itemId} Network idle -> Physics On");
            }
        }
    }

    // 1. 내가 잡았을 때
    public void OnLocalGrabStart()
    {
        isLocallyGrabbed = true;
        isNetworkMoving = false;
        rb.isKinematic = true; // 물리 끄기 (손따라가야 함)
        rb.useGravity = false;
    }

    // 2. 내가 놓았을 때 (던지기)
    public void OnLocalGrabEnd(Vector3 throwVelocity)
    {
        isLocallyGrabbed = false;
        isNetworkMoving = false;

        // 내가 던졌으므로 내 컴퓨터에서 물리를 계산해야 함 -> 물리 켜기
        rb.isKinematic = false;
        rb.useGravity = true;
        rb.velocity = throwVelocity;
    }

    // 3. 남이 잡았을 때 (NetworkPlayerSync에서 호출)
    public void OnRemoteGrabStart()
    {
        if (isLocallyGrabbed) return;
        isRemotelyGrabbed = true;
        rb.isKinematic = true; // 물리 끄기
        rb.useGravity = false;
    }

    public void OnRemoteGrabEnd()
    {
        if (isLocallyGrabbed) return;
        isRemotelyGrabbed = false;
        // 여기서 바로 물리를 켜면 안됨! (공중에 있을 수 있음)
        // 자연스럽게 UpdateRemotePose가 끊기면 Update()에서 켜지도록 유도하거나,
        // 혹은 바로 켤 수도 있음. 상황에 따라 다름. 일단은 유지.
    }

    // ★ 핵심 추가 함수: 서버에서 좌표를 받아 갱신할 때 호출
    public void UpdateRemotePose(Vector3 targetPos, float lerpRate)
    {
        if (isLocallyGrabbed) return; // 내가 잡고 있으면 서버 무시

        // 네트워크 데이터를 받는 동안은 물리를 무조건 끕니다.
        isNetworkMoving = true;
        lastNetworkUpdateResponceTime = Time.time;

        rb.isKinematic = true;
        rb.useGravity = false;

        // 부드러운 이동
        float dist = Vector3.Distance(transform.position, targetPos);
        if (dist > 2.0f) // 거리가 너무 멀면 텔레포트 (초기화 등)
        {
            transform.position = targetPos;
        }
        else
        {
            transform.position = Vector3.Lerp(transform.position, targetPos, Time.deltaTime * lerpRate);
        }

        // 회전도 있다면 여기서 Lerp
    }

    public void SummonToRoomOrigin()
    {
        Transform roomAnchor = null;
        if (NetworkPlayerSync.Instance != null && NetworkPlayerSync.Instance.poseProvider != null)
        {
            roomAnchor = NetworkPlayerSync.Instance.poseProvider.GetRoomAnchor();
        }

        if (roomAnchor != null)
        {
            // 1. 위치 이동 (기존 코드)
            transform.position = roomAnchor.position + Vector3.up * 0.5f; // 바닥보다 0.5m 위에서 떨어지게
            transform.rotation = roomAnchor.rotation;

            // [수정 2] ★핵심★ 앵커를 부모로 설정 (AR 좌표 보정 시 같이 따라가도록)
            transform.SetParent(roomAnchor, true);

            NetworkPlayerSync.Instance.CustomLog($"[SUMMON] {itemId} to Anchor!");
        }
        else
        {
            transform.position = Vector3.up * 0.5f; // 월드 기준
            // 앵커가 없으면 부모 해제
            transform.SetParent(null);
        }

        // 2. 물리 초기화
        if (rb)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        // 3. ★ [핵심 추가] 강제 소유권 주장
        // 이걸 설정해야 NetworkPlayerSync가 "아, 내가 이 아이템의 좌표를 서버에 보내야 하는구나"라고 인식함
        if (NetworkPlayerSync.Instance != null)
        {
            NetworkPlayerSync.Instance.currentGrabbedItemId = this.itemId;
        }
    }
}

