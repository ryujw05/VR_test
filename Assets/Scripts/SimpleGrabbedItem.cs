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
        rb.isKinematic = false;
        rb.useGravity = true;
    }
}