using MediapipeHandTracking;
using UnityEngine;

public class ARHandProcessor : MonoBehaviour
{
    public static ARHandProcessor Instance { get; private set; }
    private GameObject Hand = default;
    private HandRect currentHandRect = default;
    private HandRect oldHandRect = default;
    private ARHand currentHand = default;
    private bool isHandRectChange = default;

    // [추가 1] 스무딩 기능을 담당할 변수 선언
    private LandmarkSmootherLerp smoother;

    // [설정] 보정 강도 (0.1 ~ 0.9)
    // 값이 작을수록(0.1) 부드럽지만 손이 뒤늦게 따라옵니다.
    // 값이 클수록(0.9) 빠릿하지만 떨림이 그대로 보입니다.
    // 0.4 ~ 0.6 정도를 추천합니다.
    [Range(0.01f, 1.0f)]
    public float smoothFactor = 0.5f; 

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject); 
        }
    }

    void Start()
    {
        Hand = Manager.instance.HandOnSpace;
        currentHand = new ARHand();
        currentHandRect = new HandRect();
        oldHandRect = new HandRect();

        // [추가 2] 스무더 초기화 (손 랜드마크 21개)
        smoother = new LandmarkSmootherLerp(21);
    }

    void FixedUpdate()
    {
        // [중요] 실행 중에도 보정 강도를 조절할 수 있게 값 전달
        if(smoother != null) smoother.SetFactor(smoothFactor);

        if (GetComponent<ARFrameProcessor>().HandProcessor == null) return;
        
        float[] handRectData = GetComponent<ARFrameProcessor>().HandProcessor.getHandRectData();
        float[] handLandmarksData = GetComponent<ARFrameProcessor>().HandProcessor.getHandLandmarksData();

        if (null != handRectData)
        {
            currentHandRect = HandRect.ParseFrom(handRectData);
            if (!isHandStay())
            {
                oldHandRect = currentHandRect;
                isHandRectChange = true;
            }
            else
            {
                isHandRectChange = false;
            }
        }

        if (null != handLandmarksData && !float.IsNegativeInfinity(GetComponent<ARFrameProcessor>().ImageRatio))
        {
            currentHand.ParseFrom(handLandmarksData, GetComponent<ARFrameProcessor>().ImageRatio);
        }

        if (!Hand.activeInHierarchy) return;

        // [추가 3] 보정 적용 부분
        for (int i = 0; i < Hand.transform.childCount; i++)
        {
            // 1. 원래 MediaPipe에서 온 날것의 좌표
            Vector3 rawPos = currentHand.GetLandmark(i);

            // 2. 스무더를 통해 부드럽게 다듬어진 좌표 받기
            Vector3 smoothedPos = smoother.GetSmoothedPosition(i, rawPos);

            // 3. 적용
            Hand.transform.GetChild(i).transform.position = smoothedPos;
        }
    }

    private bool isHandStay()
    {
        return currentHandRect.XCenter == oldHandRect.XCenter &&
            currentHandRect.YCenter == oldHandRect.YCenter &&
            currentHandRect.Width == oldHandRect.Width &&
            currentHandRect.Height == oldHandRect.Height &&
            currentHandRect.Rotaion == oldHandRect.Rotaion;
    }

    public ARHand CurrentHand { get => currentHand; }
    public bool IsHandRectChange { get => isHandRectChange; }
    public HandRect CurrentHandRect { get => currentHandRect; }
}

// [추가 4] 스무딩 클래스 (파일 아래쪽에 같이 두시거나, 별도 파일로 만드셔도 됩니다)
public class LandmarkSmootherLerp
{
    private Vector3[] prevPositions;
    private float smoothFactor = 0.5f;

    public LandmarkSmootherLerp(int landmarkCount)
    {
        prevPositions = new Vector3[landmarkCount];
    }

    public void SetFactor(float factor)
    {
        this.smoothFactor = factor;
    }

    public Vector3 GetSmoothedPosition(int index, Vector3 rawPosition)
    {
        // 처음 들어오는 값이면(0,0,0) 바로 현재 위치로 초기화 (튐 방지)
        if (prevPositions[index] == Vector3.zero)
        {
            prevPositions[index] = rawPosition;
            return rawPosition;
        }

        // 보정 공식: 이전 위치와 새 위치 사이를 smoothFactor 만큼 이동
        Vector3 smoothed = Vector3.Lerp(prevPositions[index], rawPosition, smoothFactor);
        
        prevPositions[index] = smoothed;
        return smoothed;
    }
}