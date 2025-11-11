using UnityEngine;

public class HandPoseProvider : MonoBehaviour
{
    private float t = 0f; // 시간 누적용
    private const float speed = 1f; // 1초에 1 주기
    private const float range = 2f; // -2~2 범위

    public bool TryGetHandWorld(out Vector3 v)
    {
        v = default;

        // 1초마다 0→2→-2→2 식으로 변하는 삼각파 생성
        t += Time.deltaTime * speed;
        float phase = Mathf.PingPong(t * 2f, 4f) - 2f; // -2~2 반복

        // x축을 이 삼각파 값으로 움직임 (y,z는 0)
        v = new Vector3(phase, 0f, 0f);
        return true;
    }
}
