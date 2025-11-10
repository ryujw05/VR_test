using UnityEngine;
using TMPro;
using UnityEngine.XR.ARFoundation;

public class RoomPoseDisplay : MonoBehaviour
{
    [Header("References")]
    public Camera arCamera;          // AR 카메라 (없으면 Camera.main 사용)
    public TMP_Text debugText;       // 출력용 텍스트 (선택)

    [Header("Options")]
    [Range(0f, 0.9f)]
    public float smooth = 0.2f;      // 0=즉시, 1에 가까울수록 더 부드럽게
    public bool useGyroFallback = true; // 트래킹 상실 시 자이로로 yaw 임시 보완

    // 내부 상태
    private Transform roomRef;       // 기준 좌표계(= RoomAnchor 우선, 없으면 GameRoom)
    private Vector2 lastStableXZ;    // 마지막 안정 XZ (room-space)
    private float lastStableYawDeg;  // 마지막 안정 yaw (deg, room-space)
    private bool gyroSupported;
    private float gyroYawDeg;

    // RoomPoseDisplay.cs
    public void SetRoomRef(Transform t) { roomRef = t; }  // roomRef는 RPS 내부 필드

    // RoomPoseDisplay.cs (클래스 내부 어디든)
    public bool HasRoomLock => roomRef != null;

    // 최근 계산된 room-space 포즈를 외부에 제공
    public Vector2 RoomXZ { get; private set; }
    public float RoomYawDeg { get; private set; }
    public float RoomPitchDeg { get; private set; }


    void Awake()
    {
        if (!arCamera) arCamera = Camera.main;
        gyroSupported = SystemInfo.supportsGyroscope;
        if (gyroSupported) Input.gyro.enabled = true;
    }

    void Update()
    {
        // 1) 기준 좌표계 확보 (원본 스크립트 수정 없이 자동 탐색)
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
                // 아직 [Fix] 전이거나 생성 전인 상태
                if (debugText)
                    debugText.text = "Waiting for room lock... (Press [Fix])";
                return;
            }
        }

        if (arCamera == null) return;

        // 2) 룸 기준 포즈 계산
        Vector3 localPos = roomRef.InverseTransformPoint(arCamera.transform.position);
        Quaternion localRot = Quaternion.Inverse(roomRef.rotation) * arCamera.transform.rotation;

        Vector2 xz = new Vector2(localPos.x, localPos.z);
        float yawDeg = Normalize360(localRot.eulerAngles.y);
        float pitchDeg = Normalize360(localRot.eulerAngles.x);

        bool trackingGood = IsTrackingGood();

        if (!trackingGood)
        {
            // XZ는 마지막 안정값 유지
            xz = lastStableXZ;

            // yaw는 자이로로 임시 보완(가능/옵션일 때)
            if (useGyroFallback && gyroSupported)
            {
                Quaternion att = Input.gyro.attitude;
                // 플랫폼 좌표계 간단 보정
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
            // 트래킹 정상: 현재 값을 안정값으로 갱신
            lastStableXZ = xz;
            lastStableYawDeg = yawDeg;
        }

        // 3) 스무딩
        Vector2 smXZ = Vector2.Lerp(lastStableXZ, xz, 1f - smooth);
        float smYaw = Mathf.LerpAngle(lastStableYawDeg, yawDeg, 1f - smooth);

        // 4) 출력
        if (debugText)
        {
            string trackStr = trackingGood
                ? "TRACKING"
                : (useGyroFallback && gyroSupported ? "TRACK LOST (XZ frozen, yaw by gyro)" : "TRACK LOST (holding last)");
            debugText.text =
                $"room-space x/z (m): {smXZ.x,6:0.00} / {smXZ.y,6:0.00}\n" +
                $"room-space yaw (°): {Normalize360(smYaw),6:0.0}   {trackStr}";
        }
        // Update() 말미의 출력 직전/직후 등 적절한 위치에 추가
        RoomXZ = smXZ;          // room-space XZ (meters)
        RoomYawDeg = smYaw;     // room-space yaw (degrees)
        RoomPitchDeg = pitchDeg;
    }

    // ===== 유틸 =====
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
