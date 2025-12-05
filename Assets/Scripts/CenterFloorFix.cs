using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.SceneManagement;

public class FloorRoomStateMachine : MonoBehaviour
{
    [Header("References")]
    public Camera arCamera;              // Main Camera (AR/VR)
    public Button confirmButton;         // "Fix Game Room"
    public TMP_Text messageText;         // Guide message
    public TMP_Text debugText;           // Realtime debug (xyz + rot)
    public Material roomMaterial;        // Visual material (transparent recommended)
    public ARAnchorManager anchorManager;// XR Origin에 붙은 ARAnchorManager
    public string mapSceneName = "Map";

    [Header("AR Plane")]
    [SerializeField] private ARPlaneManager planeManager;

    [Header("Room Settings")]
    public float roomSizeMeters = 3f;       // Cube edge length
    public float cameraLensOffset = 0.02f;  // Lens height offset when phone is on the floor
    public UnityEvent<Transform> onRoomFixed;

    // 클래스 상단 (MonoBehaviour 내부)
    [SerializeField] private RoomPoseDisplay rps;
    [SerializeField] private NetworkPlayerSync nps;

    private enum Stage { AwaitConfirm, Locked }
    private Stage stage = Stage.AwaitConfirm;

    private GameObject roomCube;     // Fixed cube after confirm
    private ARAnchor roomAnchor;     // Anchor holding the cube
    private Vector3 lockedCenter;    // Locked floor center (world)
    private float lockedYawDeg;      // Locked yaw (deg)

    void Start()
    {
        if (!arCamera) arCamera = Camera.main;
        if (!anchorManager) anchorManager = FindObjectOfType<ARAnchorManager>();

        if (confirmButton)
        {
            confirmButton.onClick.RemoveAllListeners();
            confirmButton.onClick.AddListener(OnConfirmPressed);
        }

        if (messageText)
            messageText.text = "Place your phone on the floor (center of play area)\nand press [Fix] to confirm.";

        stage = Stage.AwaitConfirm;
    }

    static float ToSigned(float a)
    {
        a %= 360f;
        if (a > 180f) a -= 360f;
        if (a < -180f) a += 360f;
        return a;
    }

    void Update()
    {
        if (debugText && arCamera)
        {
            // Camera pose (world)
            Vector3 cp = arCamera.transform.position;
            Vector3 ce = arCamera.transform.eulerAngles;
            float cPitch = ToSigned(ce.x);
            float cYaw = ToSigned(ce.y);
            float cRoll = ToSigned(ce.z);

            if (stage == Stage.Locked)
            {
                // Locked values + cube pose (if exists)
                string lockedLine =
                    $"locked center (floor):\n {lockedCenter.x:F2}, {lockedCenter.y:F2}, {lockedCenter.z:F2}\n" +
                    $"locked yaw: {ToSigned(lockedYawDeg):F1}\n";

                string cubeLine = "";
                if (roomCube)
                {
                    Vector3 rp = roomCube.transform.position;
                    Vector3 re = roomCube.transform.eulerAngles;
                    float rPitch = ToSigned(re.x);
                    float rYaw = ToSigned(re.y);
                    float rRoll = ToSigned(re.z);

                    cubeLine =
                        $"cube center (world):\n {rp.x:F2}, {rp.y:F2}, {rp.z:F2}\n" +
                        $"cube rot (pitch/yaw/roll): {rPitch:F1}/{rYaw:F1}/{rRoll:F1}\n";
                }

                string anchorLine = "";
                if (roomAnchor)
                {
                    Vector3 ap = roomAnchor.transform.position;
                    Vector3 ae = roomAnchor.transform.eulerAngles;
                    anchorLine =
                        $"anchor pose:\n {ap.x:F2}, {ap.y:F2}, {ap.z:F2}\n rot: {ToSigned(ae.x):F1}/{ToSigned(ae.y):F1}/{ToSigned(ae.z):F1}\n";
                }

                debugText.text =
                    $"cam xyz:\n {cp.x:F2}, {cp.y:F2}, {cp.z:F2}\n" +
                    $"cam rot (pitch/yaw/roll): {cPitch:F1}/{cYaw:F1}/{cRoll:F1}\n" +
                    lockedLine + cubeLine + anchorLine;
            }
            else
            {
                debugText.text =
                    $"cam xyz:\n {cp.x:F2}, {cp.y:F2}, {cp.z:F2}\n" +
                    $"cam rot (pitch/yaw/roll): {cPitch:F1}/{cYaw:F1}/{cRoll:F1}";
            }
        }
    }

    void OnConfirmPressed()
    {
        if (stage != Stage.AwaitConfirm || !arCamera) return;

        // Lock floor center at confirm
        Vector3 camPos = arCamera.transform.position;
        //float floorY = camPos.y - Mathf.Clamp(cameraLensOffset, 0f, 0.05f);
        //lockedCenter = new Vector3(camPos.x, floorY, camPos.z);
        float floorY;
        if (!TryGetFloorY(out floorY))
        {
            // 실패하면 fallback으로 옛날 방식 쓰거나, 메시지 띄우고 return
            floorY = camPos.y - Mathf.Clamp(cameraLensOffset, 0f, 0.05f);
        }

        lockedCenter = new Vector3(camPos.x, floorY, camPos.z);

        // Lock yaw
        lockedYawDeg = arCamera.transform.eulerAngles.y;

        // Create cube under an Anchor
        CreateLockedRoom();

        stage = Stage.Locked;
        if (messageText) messageText.text = "Game room fixed.";
        if (confirmButton) confirmButton.interactable = false;

        // ▼ 방금 생성한 RoomAnchor Transform 얻기 (네 프로젝트에 맞게)
        Transform roomAnchorTr = GameObject.Find("RoomAnchor")?.transform;

        // ▼ RPS에 "바로 그" roomRef를 주입
        if (rps == null) rps = FindObjectOfType<RoomPoseDisplay>();
        if (rps != null && roomAnchorTr != null)
            rps.SetRoomRef(roomAnchorTr);

        // ▼ 1프레임 뒤에 NPS 보정→동기화 시작 (타이밍 안정화)
        StartCoroutine(_AfterLockWireUp());

        if (roomAnchorTr != null)
        {
            StartCoroutine(LoadMapAdditive(roomAnchorTr));
        }
    }

    private IEnumerator _AfterLockWireUp()
    {
        // RPS가 roomRef로 한 프레임 갱신할 시간을 줌
        yield return null;

        if (nps == null) nps = FindObjectOfType<NetworkPlayerSync>();
        if (rps == null) rps = FindObjectOfType<RoomPoseDisplay>();

        if (nps != null && rps != null)
        {
            // NPS가 "바로 그" RPS 인스턴스를 참조하도록 강제 주입
            nps.poseProvider = rps;

            // CVR 모드라면 보정 → 그 다음에 동기화 시작
            nps.CalibrateCVR();
            nps.RequestSync();
        }
    }


    IEnumerator _AfterLock()
    {
        yield return null; // 1프레임 대기(안전)
        var nps = FindObjectOfType<NetworkPlayerSync>();
        var rps = FindObjectOfType<RoomPoseDisplay>();
        if (nps && rps)
        {
            nps.poseProvider = rps; // 같은 인스턴스 주입(중요)
            nps.CalibrateCVR();
            nps.RequestSync();
        }
    }

    void CreateLockedRoom()
    {
        if (roomCube) Destroy(roomCube);
        if (roomAnchor) Destroy(roomAnchor.gameObject);

        // Create an anchor GameObject at the cube center (so cube bottom sits on floor)
        GameObject anchorGO = new GameObject("RoomAnchor");
        anchorGO.transform.position = new Vector3(
            lockedCenter.x,
            lockedCenter.y + (roomSizeMeters * 0.5f),
            lockedCenter.z
        );
        anchorGO.transform.rotation = Quaternion.Euler(0f, lockedYawDeg, 0f);

        if (anchorManager)
        {
            roomAnchor = anchorGO.AddComponent<ARAnchor>();
        }
        else
        {
            Debug.LogWarning("ARAnchorManager not found. The room may jump on tracking reset.");
        }

        // Create the cube as a child of the anchor (so it survives origin resets)
        roomCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        roomCube.name = "GameRoom";
        roomCube.transform.SetParent(anchorGO.transform, false);
        roomCube.transform.localScale = Vector3.one * roomSizeMeters;
        // ★ [추가] 방을 나타내는 큐브는 물리 충돌을 일으키면 안 되므로 콜라이더 제거
        Collider cubeCol = roomCube.GetComponent<Collider>();
        if (cubeCol) Destroy(cubeCol);

        if (roomMaterial)
        {
            var mr = roomCube.GetComponent<MeshRenderer>();
            if (mr) mr.material = roomMaterial;
        }

        //////////////after test destroy
        // 3. ★ [추가] 물리적 바닥 판(Floor Plate) 생성
        // 0,0,0 위치에 얇은 판을 깔아서 아이템이 떨어지지 않게 함
        GameObject floorPlate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floorPlate.name = "FloorPlate";
        floorPlate.transform.SetParent(anchorGO.transform, false);
        // 크기는 방보다 약간 크게, 두께는 얇게(0.1m)
        floorPlate.transform.localScale = new Vector3(roomSizeMeters, 0.1f, roomSizeMeters);
        // 바닥면(Y=0) 바로 아래에 위치하도록 설정 (아이템이 0에 딱 얹히게)
        floorPlate.transform.localPosition = new Vector3(0, -0.05f, 0);

        // ★ [중요] 레이어 설정: Grabbable이 아닌 'Default' 레이어로 설정
        // (그래야 손이 바닥을 아이템으로 인식해서 잡으려 하지 않음)
        floorPlate.layer = 0; // 0 = Default Layer

        BuildCubeEdges(roomCube.transform, roomSizeMeters, Color.red, 0.005f);
    }

    // 정육면체 모서리선 생성기
    private void BuildCubeEdges(Transform cube, float size, Color color, float width = 0.01f)
    {
        // 부모에 기존 라인들 있으면 정리
        var old = cube.Find("CubeEdges");
        if (old) Destroy(old.gameObject);

        var edgesRoot = new GameObject("CubeEdges");
        edgesRoot.transform.SetParent(cube, false);

        float h = 0.5f; // half extent

        // 8개 코너 (로컬좌표, 부모의 스케일을 따름)
        Vector3[] v = new Vector3[]
        {
        new Vector3(-h, -h, -h), // 0
        new Vector3( h, -h, -h), // 1
        new Vector3( h, -h,  h), // 2
        new Vector3(-h, -h,  h), // 3
        new Vector3(-h,  h, -h), // 4
        new Vector3( h,  h, -h), // 5
        new Vector3( h,  h,  h), // 6
        new Vector3(-h,  h,  h), // 7
        };

        // 12개 모서리(인덱스 쌍)
        int[,] e = new int[,]
        {
        {0,1},{1,2},{2,3},{3,0}, // 아래 사각
        {4,5},{5,6},{6,7},{7,4}, // 위 사각
        {0,4},{1,5},{2,6},{3,7}  // 수직 4개
        };

        // 라인 재질 준비 (지정 재질 없으면 Unlit/Color로 생성)
        Material lineMat = null;
        var shader = Shader.Find("Unlit/Color");
        if (shader != null)
        {
            lineMat = new Material(shader) { color = color };
            // 항상 위에 보이게 하고 싶으면 주석 해제:
            // lineMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
        }
        else
        {
            // 대체 셰이더 (URP 등에서 Unlit/Color 없을 경우)
            var spriteShader = Shader.Find("Sprites/Default");
            lineMat = new Material(spriteShader);
            lineMat.color = color;
        }

        // 12개의 LineRenderer 생성
        for (int i = 0; i < e.GetLength(0); i++)
        {
            var go = new GameObject($"edge_{i:D2}");
            go.transform.SetParent(edgesRoot.transform, false);

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;              // 로컬 좌표로 그려서 부모 스케일/회전에 자동 대응
            lr.positionCount = 2;
            lr.SetPosition(0, v[e[i, 0]]);
            lr.SetPosition(1, v[e[i, 1]]);
            lr.widthMultiplier = width;

            lr.material = lineMat;
            lr.numCapVertices = 0;                 // 끝 둥글게 하고 싶으면 2~4
            lr.numCornerVertices = 0;
            lr.alignment = LineAlignment.View;     // 카메라를 향하게(얇아 보이는 문제 방지)
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.textureMode = LineTextureMode.Stretch;
            lr.sortingOrder = 1000;                // 투명 머티리얼 위에 보이도록 약간 높은 정렬 순서
        }
    }

    private bool TryGetFloorY(out float floorY)
    {
        floorY = 0f;
        if (planeManager == null) return false;

        float minY = float.PositiveInfinity;
        foreach (var plane in planeManager.trackables)
        {
            // 수평 아래를 향하는 plane만 필터링
            var n = plane.transform.up;
            if (Vector3.Dot(n, Vector3.up) < 0.9f) continue;

            float y = plane.transform.position.y;
            if (y < minY) minY = y;
        }

        if (minY < float.PositiveInfinity)
        {
            floorY = minY;
            return true;
        }
        return false;
    }

    private IEnumerator LoadMapAdditive(Transform roomAnchor)
    {
        // 1) Map 씬 Additive 로드
        var op = SceneManager.LoadSceneAsync(mapSceneName, LoadSceneMode.Additive);
        yield return op;

        // 2) Map 씬 핸들 가져오기
        Scene mapScene = SceneManager.GetSceneByName(mapSceneName);
        if (!mapScene.IsValid())
        {
            Debug.LogError($"Map scene '{mapSceneName}' not found or not valid.");
            yield break;
        }

        // 3) 루트 오브젝트들 가져오기
        var roots = mapScene.GetRootGameObjects();
        if (roots == null || roots.Length == 0)
        {
            Debug.LogWarning("Map scene has no root GameObjects.");
            yield break;
        }

        // 4) 모두 RoomAnchor 밑으로 붙이기
        foreach (var go in roots)
        {
            // 카메라/이벤트시스템 같은 건 건너뛰고 싶으면 여기서 필터 가능
            // if (go.GetComponent<Camera>() != null) continue;

            go.transform.SetParent(roomAnchor, false);
        }

        Debug.Log("Map scene loaded and attached under RoomAnchor.");
    }

}
