using UnityEngine;

public class TestItemGrabController : MonoBehaviour
{
    [Header("Target Item (씬에 미리 있는 아이템)")]
    public SimpleGrabbedItem targetItem;        // 테스트할 아이템

    [Header("Hand Provider")]
    public HandPoseProvider handProvider;       // NPS에서 쓰던 그 HandPoseProvider를 그대로 참조

    [Header("Double Tap Settings")]
    public float doubleTapMaxDelay = 0.25f;     // 두 번 탭 간 최대 시간(초)

    [Header("Distance Test")]
    public bool useDistanceCheck = false;       // true면 거리 제한 사용
    public float maxGrabDistance = 5.0f;        // 손-아이템 최대 거리 (테스트용, 크게 잡으면 멀리서도 잡힘)

    private float lastTapTime = -999f;
    private bool held = false;                  // 현재 이 아이템을 내가 잡고 있는지

    private NetworkPlayerSync nps;

    private void Awake()
    {
        nps = FindObjectOfType<NetworkPlayerSync>();
        if (!nps)
        {
            Debug.LogWarning("[TestItemGrab] NetworkPlayerSync를 씬에서 찾지 못함.");
        }
    }

    private void Update()
    {
        bool doubleTap = false;

#if UNITY_ANDROID
        if (Input.touchCount > 0)
        {
            Touch t = Input.GetTouch(0);
            if (t.phase == TouchPhase.Began)
            {
                if (Time.time - lastTapTime <= doubleTapMaxDelay)
                    doubleTap = true;
                lastTapTime = Time.time;
            }
        }
#else
        // 에디터/PC용으로 마우스 더블클릭도 허용
        if (Input.GetMouseButtonDown(0))
        {
            if (Time.time - lastTapTime <= doubleTapMaxDelay)
                doubleTap = true;
            lastTapTime = Time.time;
        }
#endif

        if (doubleTap)
        {
            ToggleGrab();
        }

        // === 잡고 있는 동안에는 손 위치를 따라가게 함 ===
        if (held && targetItem != null && handProvider != null)
        {
            if (handProvider.TryGetHandWorld(out var handWorld))
            {
                // HandPoseProvider가 주는 월드좌표 그대로 사용
                targetItem.transform.position = handWorld;
                // 회전까지 맞추고 싶으면 필요할 때 아래 켜면 됨
                // targetItem.transform.rotation = Quaternion.LookRotation(handWorldDir ...);
            }
        }
    }

    private void ToggleGrab()
    {
        if (!targetItem)
        {
            Debug.LogWarning("[TestItemGrab] targetItem이 설정되어 있지 않음.");
            return;
        }

        if (handProvider == null)
        {
            Debug.LogWarning("[TestItemGrab] handProvider가 설정되어 있지 않음.");
            return;
        }

        if (!held)
        {
            // ===== Grab 시작 =====
            // 1) 손 월드좌표 가져오기
            if (!handProvider.TryGetHandWorld(out var handWorld))
            {
                Debug.LogWarning("[TestItemGrab] HandPoseProvider에서 손 좌표를 가져오지 못함.");
                return;
            }

            // 2) (옵션) 거리 제한 체크
            if (useDistanceCheck)
            {
                float dist = Vector3.Distance(handWorld, targetItem.transform.position);
                if (dist > maxGrabDistance)
                {
                    Debug.Log($"[TestItemGrab] 너무 멀어서 Grab 불가. dist={dist:F2} > max={maxGrabDistance:F2}");
                    return;
                }
            }

            held = true;

            // 로컬에서 잡기 시작
            targetItem.OnLocalGrabStart();

            // NPS에 "이 아이템의 오너는 나"라고 알려줌
            if (nps != null)
            {
                nps.currentGrabbedItemId = targetItem.itemId;
            }

            // 잡는 순간 손 위치로 한 번 스냅
            targetItem.transform.position = handWorld;

            Debug.Log($"[TestItemGrab] GRAB start: {targetItem.itemId}");
        }
        else
        {
            // ===== Release (Drop) =====
            held = false;

            // 로컬에서 놓기 → Rigidbody에 중력 적용
            targetItem.OnLocalGrabEnd(Vector3.zero);

            // ※ 여기서는 currentGrabbedItemId를 비우지 않음
            //    떨어지는 동안에도 owner-streaming을 계속 보내야 해서
            //    NetworkPlayerSync.SendMyPose() 안에서
            //    속도가 충분히 작아졌을 때 currentGrabbedItemId = ""로 해제됨

            Debug.Log($"[TestItemGrab] GRAB end(drop): {targetItem.itemId}");
        }
    }
}
