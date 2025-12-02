using UnityEngine;

public class HandPoseProvider : MonoBehaviour
{
    // 랜드마크 인덱스 상수 (MediaPipe 기준)
    private const int THUMB_TIP = 4; // 엄지 끝
    private const int INDEX_TIP = 8; // 검지 끝

    public bool TryGetHandWorld(out Vector3 v)
    {
        v = default;

        // 1. ARHandProcessor 인스턴스 확인
        if (ARHandProcessor.Instance == null) return false;

        // 2. 손 오브젝트(HandOnSpace)를 가져옵니다.
        // ARHandProcessor 코드에서 Manager.instance.HandOnSpace를 쓰고 있었으므로 여기서도 접근 가능합니다.
        GameObject handRoot = Manager.instance.HandOnSpace;

        // 손이 꺼져있거나(인식 안됨), 아직 초기화가 안 됐다면 실패 반환
        if (handRoot == null || !handRoot.activeInHierarchy)
        {
            return false;
        }

        try
        {
            // 3. 스무딩(보정)이 적용된 실제 랜드마크 오브젝트의 위치를 가져옵니다.
            // ARHandProcessor의 FixedUpdate에서 이미 이 transform들의 위치를 부드럽게 옮겨놨습니다.
            Transform thumbTransform = handRoot.transform.GetChild(THUMB_TIP);
            Transform indexTransform = handRoot.transform.GetChild(INDEX_TIP);

            // 4. 엄지와 검지의 '딱 중간 위치'를 반환합니다. (집게 손가락 사이)
            // 이렇게 하면 핀치(집기) 동작을 할 때 물체가 손가락 사이에 예쁘게 위치합니다.
            v = (thumbTransform.position + indexTransform.position) * 0.5f;
            
            return true;
        }
        catch
        {
            // 혹시라도 자식 오브젝트가 아직 생성되지 않았을 경우 예외 처리
            return false;
        }
    }
}