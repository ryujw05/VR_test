using UnityEngine;

public class MapPlayerAdapter : MonoBehaviour
{
    public Transform playerRoot;
    public RoomPoseDisplay rps;

    [Header("위치 옵션")]
    public float playerHeight = 1.7f;
    public float positionScale = 1f;

    void Start()
    {
        if (rps == null)
            rps = FindObjectOfType<RoomPoseDisplay>();
    }

    void LateUpdate()
    {
        if (rps == null || playerRoot == null) return;

        // 1) 위치 연동
        Vector2 roomXZ = rps.RoomXZ;
        float mx = roomXZ.x * positionScale;
        float mz = roomXZ.y * positionScale;

        playerRoot.localPosition = new Vector3(mx, playerHeight, mz);

        // 2) 회전 연동 (yaw + pitch)
        float yaw = rps.RoomYawDeg;
        float pitch = rps.RoomPitchDeg;

        // 보기 편하게 [-180, 180] 범위로 바꿔주기 (필수는 아님)
        if (pitch > 180f) pitch -= 360f;

        playerRoot.localRotation = Quaternion.Euler(pitch, yaw, 0f);
    }
}
