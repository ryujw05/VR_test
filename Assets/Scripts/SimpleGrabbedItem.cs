using System.Collections;
using System.Collections.Generic;
using UnityEngine;

///   1) itemId로 아이템을 식별할 수 있게 해주고
///   2) 로컬/원격 Grab 상태에 따라 Rigidbody 세팅만 관리함.
/// 실제 Grab/Release 트리거(버튼 입력)는 다른 스크립트에서 호출.

[RequireComponent(typeof(Rigidbody))]
public class SimpleGrabbedItem : MonoBehaviour
{
    // === static 레지스트리: itemId -> SimpleGrabbedItem ===
    public static readonly Dictionary<string, SimpleGrabbedItem> Registry = new();
    public static bool TryGet(string id, out SimpleGrabbedItem item) => Registry.TryGetValue(id, out item);

    [Header("ID")]
    [Tooltip("서버/클라이언트 전체에서 유일한 아이템 ID (예: arrow1, torchA 등)")]
    public string itemId;

    [Header("Refs")]
    public Rigidbody rb;

    [Header("State (Debug)")]
    [Tooltip("이 클라이언트에서 직접 잡고 있는지 여부")]
    public bool isLocallyGrabbed = false;

    [Tooltip("다른 플레이어가 잡아서, 이 클라에서는 원격으로 따라가는 상태인지 여부")]
    public bool isRemotelyGrabbed = false;

    private void Awake()
    {
        if (!rb)
            rb = GetComponent<Rigidbody>();

        // 레지스트리에 등록
        if (!string.IsNullOrEmpty(itemId))
        {
            Registry[itemId] = this;
        }

        // 기본은 자유 상태: 물리 on
        rb.isKinematic = false;
        rb.useGravity = true;
    }

    private void OnDestroy()
    {
        if (!string.IsNullOrEmpty(itemId) && Registry.TryGetValue(itemId, out var me) && me == this)
        {
            Registry.Remove(itemId);
        }
    }

    // ===== 로컬 플레이어가 이 아이템을 잡기 시작 =====
    public void OnLocalGrabStart()
    {
        isLocallyGrabbed = true;
        isRemotelyGrabbed = false;

        // 손이 직접 위치를 움직일 거라 물리는 잠깐 끔
        rb.isKinematic = true;
        rb.useGravity = false;
    }

    // ===== 로컬 플레이어가 이 아이템을 놓을 때 =====
    // throwVelocity에는 손의 속도를 넣으면 "던지는" 느낌 가능 (지금은 0 넣어도 됨)
    public void OnLocalGrabEnd(Vector3 throwVelocity)
    {
        isLocallyGrabbed = false;

        rb.isKinematic = false;
        rb.useGravity = true;
        rb.velocity = throwVelocity;
    }

    // ===== 다른 플레이어가 (원격에서) 잡기 시작했다고 서버가 알려줬을 때 =====
    public void OnRemoteGrabStart()
    {
        // 내가 직접 잡고 있는 중이면 원격 상태로 덮어쓰지 않음
        if (isLocallyGrabbed) return;

        isRemotelyGrabbed = true;

        rb.isKinematic = true;
        rb.useGravity = false;
    }

    // ===== 다른 플레이어가 (원격에서) 놓았을 때 =====
    public void OnRemoteGrabEnd()
    {
        // 내가 잡고 있는 중이면 free로 돌리지 않음
        if (isLocallyGrabbed) return;

        if (!isRemotelyGrabbed) return;

        isRemotelyGrabbed = false;

        rb.isKinematic = false;
        rb.useGravity = true;
        // 속도는 일단 0으로 두고, 각 클라 물리에 맡김
    }
}