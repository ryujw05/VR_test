using UnityEngine;

public class TestItemGrabController : MonoBehaviour
{
    [Header("Target Item (���� �̸� �ִ� ������)")]
    public SimpleGrabbedItem targetItem;        // �׽�Ʈ�� ������

    [Header("Hand Provider")]
    public HandPoseProvider handProvider;       // NPS���� ���� �� HandPoseProvider�� �״�� ����

    [Header("Double Tap Settings")]
    public float doubleTapMaxDelay = 0.25f;     // �� �� �� �� �ִ� �ð�(��)

    [Header("Distance Test")]
    public bool useDistanceCheck = false;       // true�� �Ÿ� ���� ���
    public float maxGrabDistance = 5.0f;        // ��-������ �ִ� �Ÿ� (�׽�Ʈ��, ũ�� ������ �ָ����� ����)

    private float lastTapTime = -999f;
    private bool held = false;                  // ���� �� �������� ���� ��� �ִ���

    private NetworkPlayerSync nps;

    private void Awake()
    {
        nps = FindObjectOfType<NetworkPlayerSync>();
        if (!nps)
        {
            Debug.LogWarning("[TestItemGrab] NetworkPlayerSync�� ������ ã�� ����.");
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
        // ������/PC������ ���콺 ����Ŭ���� ���
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

        // === ��� �ִ� ���ȿ��� �� ��ġ�� ���󰡰� �� ===
        if (held && targetItem != null && handProvider != null)
        {
            if (handProvider.TryGetHandWorld(out var handWorld))
            {
                // HandPoseProvider�� �ִ� ������ǥ �״�� ���
                targetItem.transform.position = handWorld;
                // ȸ������ ���߰� ������ �ʿ��� �� �Ʒ� �Ѹ� ��
                // targetItem.transform.rotation = Quaternion.LookRotation(handWorldDir ...);
            }
        }
    }

    private void ToggleGrab()
    {
        if (!targetItem)
        {
            Debug.LogWarning("[TestItemGrab] targetItem�� �����Ǿ� ���� ����.");
            return;
        }

        if (handProvider == null)
        {
            Debug.LogWarning("[TestItemGrab] handProvider�� �����Ǿ� ���� ����.");
            return;
        }

        if (!held)
        {
            // ===== Grab ���� =====
            // 1) �� ������ǥ ��������
            if (!handProvider.TryGetHandWorld(out var handWorld))
            {
                Debug.LogWarning("[TestItemGrab] HandPoseProvider���� �� ��ǥ�� �������� ����.");
                return;
            }

            // 2) (�ɼ�) �Ÿ� ���� üũ
            if (useDistanceCheck)
            {
                float dist = Vector3.Distance(handWorld, targetItem.transform.position);
                if (dist > maxGrabDistance)
                {
                    Debug.Log($"[TestItemGrab] �ʹ� �־ Grab �Ұ�. dist={dist:F2} > max={maxGrabDistance:F2}");
                    return;
                }
            }

            held = true;

            // ���ÿ��� ��� ����
            targetItem.OnLocalGrabStart();

            // NPS�� "�� �������� ���ʴ� ��"��� �˷���
            if (nps != null)
            {
                nps.currentGrabbedItemId = targetItem.itemId;
            }

            // ��� ���� �� ��ġ�� �� �� ����
            targetItem.transform.position = handWorld;

            Debug.Log($"[TestItemGrab] GRAB start: {targetItem.itemId}");
        }
        else
        {
            // ===== Release (Drop) =====
            held = false;

            // ���ÿ��� ���� �� Rigidbody�� �߷� ����
            targetItem.OnLocalGrabEnd(Vector3.zero);

            // �� ���⼭�� currentGrabbedItemId�� ����� ����
            //    �������� ���ȿ��� owner-streaming�� ��� ������ �ؼ�
            //    NetworkPlayerSync.SendMyPose() �ȿ���
            //    �ӵ��� ����� �۾����� �� currentGrabbedItemId = ""�� ������

            Debug.Log($"[TestItemGrab] GRAB end(drop): {targetItem.itemId}");
        }
    }
}
