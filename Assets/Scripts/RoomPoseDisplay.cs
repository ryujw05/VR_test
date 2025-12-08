using UnityEngine;
using TMPro;
using UnityEngine.XR.ARFoundation;

public class RoomPoseDisplay : MonoBehaviour
{
    [Header("References")]
    public Camera arCamera;          // AR ī�޶� (������ Camera.main ���)
    public TMP_Text debugText;       // ��¿� �ؽ�Ʈ (����)

    [Header("Options")]
    [Range(0f, 0.9f)]
    public float smooth = 0.2f;      // 0=���, 1�� �������� �� �ε巴��
    public bool useGyroFallback = true; // Ʈ��ŷ ��� �� ���̷η� yaw �ӽ� ����

    // ���� ����
    private Transform roomRef;       // ���� ��ǥ��(= RoomAnchor �켱, ������ GameRoom)
    public Transform GetRoomAnchor()
    {
        return roomRef;
    }
    private Vector2 lastStableXZ;    // ������ ���� XZ (room-space)
    private float lastStableYawDeg;  // ������ ���� yaw (deg, room-space)
    private bool gyroSupported;
    private float gyroYawDeg;

    // RoomPoseDisplay.cs
    public void SetRoomRef(Transform t) { roomRef = t; }  // roomRef�� RPS ���� �ʵ�

    // RoomPoseDisplay.cs (Ŭ���� ���� ����)
    public bool HasRoomLock => roomRef != null;

    // �ֱ� ���� room-space ��� �ܺο� ����
    public Vector2 RoomXZ { get; private set; }
    public float RoomYawDeg { get; private set; }
    public float RoomPitchDeg { get; private set; }

    public bool TryWorldToRoom(Vector3 worldPos, out Vector3 roomPos)
    {
        roomPos = worldPos;
        if (!HasRoomLock || roomRef == null) return false;      // ���� [Fix] ���̸� ��ȯ �Ұ�
        roomPos = roomRef.InverseTransformPoint(worldPos);
        return true;
    }

    void Awake()
    {
        if (!arCamera) arCamera = Camera.main;
        gyroSupported = SystemInfo.supportsGyroscope;
        if (gyroSupported) Input.gyro.enabled = true;
    }

    void Update()
    {
        // 1) ���� ��ǥ�� Ȯ�� (���� ��ũ��Ʈ ���� ���� �ڵ� Ž��)
        if (roomRef == null)
        {
            var anchorGO = GameObject.Find("RoomAnchor");
            if (anchorGO) roomRef = anchorGO.transform;
            else
            {
                var cube = GameObject.Find("GameRoom");
                if (cube) roomRef = cube.transform;
            }

            if (roomRef == null)
            {
                // ���� [Fix] ���̰ų� ���� ���� ����
                if (debugText)
                    debugText.text = "Waiting for room lock... (Press [Fix])";
                return;
            }
        }

        if (arCamera == null) return;

        // 2) �� ���� ���� ���
        Vector3 localPos = roomRef.InverseTransformPoint(arCamera.transform.position);
        Quaternion localRot = Quaternion.Inverse(roomRef.rotation) * arCamera.transform.rotation;

        Vector2 xz = new Vector2(localPos.x, localPos.z);
        float yawDeg = Normalize360(localRot.eulerAngles.y);
        float pitchDeg = Normalize360(localRot.eulerAngles.x);

        bool trackingGood = IsTrackingGood();

        if (!trackingGood)
        {
            // XZ�� ������ ������ ����
            xz = lastStableXZ;

            // yaw�� ���̷η� �ӽ� ����(����/�ɼ��� ��)
            if (useGyroFallback && gyroSupported)
            {
                Quaternion att = Input.gyro.attitude;
                // �÷��� ��ǥ�� ���� ����
                Quaternion q = new Quaternion(att.x, att.y, -att.z, -att.w);
                Vector3 eul = q.eulerAngles;
                gyroYawDeg = Normalize360(eul.y);
                yawDeg = gyroYawDeg;
            }
            else
            {
                yawDeg = lastStableYawDeg;
            }
        }
        else
        {
            // Ʈ��ŷ ����: ���� ���� ���������� ����
            lastStableXZ = xz;
            lastStableYawDeg = yawDeg;
        }

        // 3) ������
        Vector2 smXZ = Vector2.Lerp(lastStableXZ, xz, 1f - smooth);
        float smYaw = Mathf.LerpAngle(lastStableYawDeg, yawDeg, 1f - smooth);

        // 4) ���
        if (debugText)
        {
            string trackStr = trackingGood
                ? "TRACKING"
                : (useGyroFallback && gyroSupported ? "TRACK LOST (XZ frozen, yaw by gyro)" : "TRACK LOST (holding last)");
            debugText.text =
                $"room-space x/z (m): {smXZ.x,6:0.00} / {smXZ.y,6:0.00}\n" +
                $"room-space yaw (��): {Normalize360(smYaw),6:0.0}   {trackStr}";
        }
        // Update() ������ ��� ����/���� �� ������ ��ġ�� �߰�
        RoomXZ = smXZ;          // room-space XZ (meters)
        RoomYawDeg = smYaw;     // room-space yaw (degrees)
        RoomPitchDeg = pitchDeg;
    }

    // ===== ��ƿ =====
    static float Normalize360(float y)
    {
        y %= 360f;
        if (y < 0f) y += 360f;
        return y;
    }

    static bool IsTrackingGood()
    {
        var s = ARSession.state;
        return (s == ARSessionState.SessionTracking || s == ARSessionState.Ready);
    }
}
