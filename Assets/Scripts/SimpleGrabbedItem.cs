using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class SimpleGrabbedItem : MonoBehaviour
{
    // [수정] int -> string으로 복구 (기존 코드 호환성 위함)
    public static readonly Dictionary<string, SimpleGrabbedItem> Registry = new();
    public static bool TryGet(string id, out SimpleGrabbedItem item) => Registry.TryGetValue(id, out item);

    [Header("ID")]
    [Tooltip("네트워크 동기화용 고유 ID (예: item_1, sword_A)")]
    public string itemId; // [수정] 다시 string으로 변경

    [Header("Refs")]
    public Rigidbody rb;

    [Header("State")]
    public bool isLocallyGrabbed = false;
    public bool isRemotelyGrabbed = false;

    private float _debugTimer;

    // ★ [추가] 네트워크 보간을 위한 변수
    private Vector3 _networkTargetPos;
    private bool _hasNetworkTarget = false;
    private float _smoothSpeed = 12f;

    private void Awake()
    {
        if (!rb) rb = GetComponent<Rigidbody>();

        // [수정] string 키로 등록
        if (!string.IsNullOrEmpty(itemId) && !Registry.ContainsKey(itemId))
        {
            Registry[itemId] = this;
        }

        rb.isKinematic = false;
        rb.useGravity = true;
    }

    private void OnDestroy()
    {
        if (!string.IsNullOrEmpty(itemId) && Registry.ContainsKey(itemId) && Registry[itemId] == this)
        {
            Registry.Remove(itemId);
        }
    }

    public void OnLocalGrabStart()
    {
        isLocallyGrabbed = true;
        isRemotelyGrabbed = false;
        _hasNetworkTarget = false; // 내가 잡으면 네트워크 목표 무시

        rb.isKinematic = true;
        rb.useGravity = false;
    }

    public void OnLocalGrabEnd(Vector3 throwVelocity)
    {
        isLocallyGrabbed = false;
        
        rb.isKinematic = false;
        rb.useGravity = true;
        rb.velocity = throwVelocity;
    }

    public void OnRemoteGrabStart()
    {
        if (isLocallyGrabbed) return;
        
        isRemotelyGrabbed = true;
        rb.isKinematic = true;
        rb.useGravity = false;
    }

    public void OnRemoteGrabEnd()
    {
        if (isLocallyGrabbed) return;
        if (!isRemotelyGrabbed) return;

        isRemotelyGrabbed = false;
        _hasNetworkTarget = false;

        rb.isKinematic = false;
        rb.useGravity = true;
    }

    // ★ [추가] 네트워크에서 목표 좌표를 입력받는 함수
    public void SetNetworkTarget(Vector3 worldPos, float lerpSpeed)
    {
        // 내가 잡고 있으면 무시
        if (isLocallyGrabbed) return;

        // 원격 잡기 상태가 아니라면 강제로 전환 (안전장치)
        if (!isRemotelyGrabbed)
        {
            OnRemoteGrabStart();
            NetworkPlayerSync.Instance.CustomLog($"[ITEM] Remote Control Started: {itemId}");
        }

        _networkTargetPos = worldPos;
        _smoothSpeed = lerpSpeed;
        _hasNetworkTarget = true;
    }

    //////////after test destroy
    private void Update()
    {

        // ★ [핵심] 원격 조종 중일 때 매 프레임 목표로 이동
        if (isRemotelyGrabbed && _hasNetworkTarget && !isLocallyGrabbed)
        {
            // 거리가 너무 멀면 텔레포트 (초기화 등)
            if (Vector3.Distance(transform.position, _networkTargetPos) > 2.0f)
            {
                transform.position = _networkTargetPos;
            }
            else
            {
                // 부드럽게 이동 (Lerp)
                transform.position = Vector3.Lerp(transform.position, _networkTargetPos, Time.deltaTime * _smoothSpeed);
            }
        }

        // 1. [생존 신고] 3초마다 로그 (디버깅용 유지)
        _debugTimer += Time.deltaTime;
        if (_debugTimer > 3.0f)
        {
            _debugTimer = 0f;
            string parentName = transform.parent ? transform.parent.name : "null";
            string msg = $"[ITEM] {itemId} / Pos: {transform.position} / LocalGrab: {isLocallyGrabbed} / RemoteGrab: {isRemotelyGrabbed} / NetTarget: {_hasNetworkTarget}";

            if (NetworkPlayerSync.Instance != null)
                NetworkPlayerSync.Instance.CustomLog(msg);
        }
    }

    // ★ [추가] UI 버튼과 연결할 소환 함수 (Public)
    // SimpleGrabbedItem.cs

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
            transform.position = roomAnchor.position + Vector3.up * 0.7f; // 바닥보다 0.5m 위에서 떨어지게
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
            OnLocalGrabStart();
        }

        // 3. ★ [핵심 추가] 강제 소유권 주장
        // 이걸 설정해야 NetworkPlayerSync가 "아, 내가 이 아이템의 좌표를 서버에 보내야 하는구나"라고 인식함
        if (NetworkPlayerSync.Instance != null)
        {
            NetworkPlayerSync.Instance.currentGrabbedItemId = this.itemId;
        }
    }
}

