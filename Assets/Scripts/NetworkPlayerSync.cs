using System;
using System.Collections.Generic;
using TMPro; // TextMeshPro를 사용하려면 필요합니다.
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.XR;
using WS = WebSocketSharp;

public class NetworkPlayerSync : MonoBehaviour
{
    public static NetworkPlayerSync Instance;
    [Header("Connection")]
    public string serverWsUrl = "ws://192.168.219.101:8080/ws";
    [HideInInspector]
    public string myId = "";

    [Header("Refs")]
    public Transform localPlayer;
    public Transform sharedOrigin;
    public GameObject remoteAvatarPrefab;
    public GameObject remoteHandPrefab;

    [Header("Sync Settings")]
    [Range(5, 30)] public int sendHz = 15;
    public float moveLerp = 12f;
    public float rotLerp = 12f;
    public float minDeltaToSend = 0.01f;

    // === CVR ===
    [Header("CVR")]
    [Tooltip("로컬 좌표를 공통 가상좌표(CVR)로 변환해서 송수신합니다.")]
    public bool useCVR = true;

    [Tooltip("CVR 보정(룸픽스) 완료 전에는 RequestSync를 차단할지 여부")]
    public bool requireCalibToSync = true;

    [Tooltip("내 아바타를 서버 스냅샷으로도 렌더링할지 (false면 내 ID는 스킵)")]
    public bool renderSelfFromServer = false;

    [Header("Hand Tracking (optional)")]
    public HandPoseProvider handProvider;   // 월드 기준 손 좌표
    public bool includeHand = true;

    [Header("UI Debug")]
    [Tooltip("로그 출력을 위한 TextMeshPro 컴포넌트")]
    public TMP_Text debugLogText;

    [Header("Item Sync")]
    [Tooltip("이 클라이언트가 현재 잡고 있는 아이템 ID (없으면 빈 문자열)")]
    public string currentGrabbedItemId = "";

    // NetworkPlayerSync.cs (필드 섹션 쪽)
    public RoomPoseDisplay poseProvider;  // 인스펙터에 Drag&Drop

    private WS.WebSocket ws;
    private float sendTimer;
    private Vector3 _lastSentLocalPos;
    private readonly Dictionary<string, RemoteAgent> agents = new();

    private string myPid = "";

    private string debugHistory = "";
    private const int MAX_LOG_LINES = 10;
    private bool syncStarted = false;

    // 보정값(룸픽스 시 저장)
    private Vector2 cvrOriginXZ;    // O
    private float cvrYawRad;      // ψ (라디안)
    private float cvrScale = 1f;  // s (필요시 사용, 기본 1)
    private bool hasCVR = false; // 보정 완료 플래그

    private Transform localAvatarTr;
    private bool cLog = false;
    private bool xLog = true;

    // ★ 추가: PlayerPrefs 키
    const string PREF_PID = "nps.player";  // ex) "p1"
    const string PREF_UID = "nps.uid";     // ex) "9f3b..."

    private string _phase2Pid = "";        // 현재 세션의 player (p1/p2…)

    // 이번에 서버가 보내준 스냅샷 기준으로, 이미 점유된 아이템 ID들
    private readonly HashSet<string> _occupiedItemIds = new();

    // JSON 직렬화를 위한 클래스
    [Serializable] class Coordinate { public float x; public float z; public float r; }
    [Serializable] public class Hand { public float x, y, z; public Hand() { } public Hand(Vector3 v) { x = v.x; y = v.y; z = v.z; } }
    [Serializable] class CoordinateWrap { public Coordinate coordinate; public Hand hand; public string grabbedItemId; }

    [Serializable] public class PlayerData { public string id; public float x; public float z; public float r; public Hand hand; public string grabbedItemId; } // hand 추가
    [Serializable] public class PlayersRoot { public PlayerData[] players; }

    [Serializable] class FirstReply { public string player; public string id; }

    [Serializable]
    class ItemPoseMsg
    {
        public string type = "item_pose";
        public string id;
        public float px, py, pz;
    }

    [Serializable]
    public class ItemPayload
    {
        public string id;
        public float x, y, z;
    }

    [Serializable]
    public class ServerSnapshot
    {
        public PlayerData[] players;
        public ItemPayload[] items;
    }

    // ★ [추가 2] 인스턴스 초기화
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            // 중복 생성 방지 (혹시 모를 상황 대비)
            Destroy(gameObject);
            return;
        }
    }

    // UI에 로그를 출력하는 헬퍼 함수
    public void CustomLog(string message)
    {
        Debug.Log(message);
        string time = DateTime.Now.ToString("HH:mm:ss");
        debugHistory = $"[{time}] {message}\n" + debugHistory;
        string[] lines = debugHistory.Split('\n');
        if (lines.Length > MAX_LOG_LINES)
        {
            debugHistory = string.Join("\n", lines, 0, MAX_LOG_LINES);
        }
        if (debugLogText != null)
        {
            debugLogText.text = debugHistory;
        }
    }

    private void Start()
    {
        if (!localPlayer) localPlayer = transform;
        CustomLog("[System] NetworkPlayerSyncSingleton Ready.");

        // Start에서 연결 시작
        ConnectToServer(null);
    }

    // ===== 1) 접속 (내부 자동 호출) =====
    public void ConnectToServer(string ignoredPlayerId)
    {
        try
        {
            string baseUrl = serverWsUrl;
            if (baseUrl.EndsWith("/ws"))
                baseUrl = baseUrl.Substring(0, baseUrl.Length - 3); // "/ws" 제거

            // ① Resume: 저장된 pid가 있으면 바로 /ws/{pid} 시도
            string savedPid = PlayerPrefs.GetString(PREF_PID, "");
            string savedUid = PlayerPrefs.GetString(PREF_UID, "");

            if (!string.IsNullOrEmpty(savedPid))
            {
                _phase2Pid = savedPid;
                myId = string.IsNullOrEmpty(savedUid) ? "" : savedUid;

                string url2 = baseUrl + "/ws/" + _phase2Pid;
                if (cLog) CustomLog($"[WS] (resume) try {url2}  pid={_phase2Pid}, uid={myId}");
                if (TryConnectMain(url2)) return;   // 성공하면 끝
                if (cLog) CustomLog("[WS] (resume) failed → fallback to phase1");
            }

            // ② Phase1: /ws로 접속해서 {player,id} 발급
            Phase1_Handshake(baseUrl);
        }
        catch (Exception ex)
        {
            if (cLog) CustomLog($"[WS] ConnectToServer EXCEPTION: {ex.Message}");
        }
    }

    // ===== 2) Phase1: /ws 에서 pid/uid 발급 =====
    private void Phase1_Handshake(string baseUrl)
    {
        // 안전: 기존 소켓 닫기
        try { ws?.Close(); } catch { }
        ws = null;

        string phase1Url = baseUrl + "/ws";
        ws = new WS.WebSocket(phase1Url);

        ws.OnOpen += (s, e) =>
        {
            if (cLog) CustomLog($"[WS] (phase1) connected: {phase1Url}");
            // 연결 즉시 {"name": "..."} 전송 (선택)
            string displayName = SystemInfo.deviceName;
            string hello = "{\"name\":\"" + displayName + "\"}";
            try { ws.Send(hello); if (cLog) CustomLog("[WS] (phase1) sent hello: " + hello); }
            catch (Exception ex) { if (cLog) CustomLog("[WS] (phase1) send hello EX: " + ex.Message); }
        };

        ws.OnMessage += (s, e) =>
        {
            try
            {
                // 서버 기대: {"player":"p1","id":"<uid>"}
                FirstReply fr = JsonUtility.FromJson<FirstReply>(e.Data);
                if (fr != null && !string.IsNullOrEmpty(fr.player) && !string.IsNullOrEmpty(fr.id))
                {
                    _phase2Pid = fr.player;
                    myId = fr.id;
                    myPid = _phase2Pid; // ★ 내 pid 저장
                    if (cLog) CustomLog($"[WS] (phase1) assigned: player={_phase2Pid}, uid={myId}");

                    // 영구 저장 → 다음부터 resume 경로 사용
                    PlayerPrefs.SetString(PREF_PID, _phase2Pid);
                    PlayerPrefs.SetString(PREF_UID, myId);
                    PlayerPrefs.Save();

                    // /ws 닫고 /ws/{pid} 접속
                    try { ws.Close(); } catch { }
                    string phase2Url = baseUrl + "/ws/" + _phase2Pid;
                    ConnectToMainChannel(phase2Url);
                    return;
                }
                else
                {
                    if (cLog) CustomLog("[WS] (phase1) non-first-reply: " + e.Data);
                }
            }
            catch (Exception ex)
            {
                if (cLog) CustomLog("[WS] (phase1) parse error: " + ex.Message + " | raw=" + e.Data);
            }
        };

        ws.OnError += (s, e) => { if (cLog) CustomLog($"[WS] (phase1) ERROR: {e?.Message ?? "(null)"}"); };
        ws.OnClose += (s, e) => { if (cLog) CustomLog($"[WS] (phase1) closed. Code:{e.Code}, Reason:{e.Reason}"); };

        if (cLog) CustomLog($"[WS] (phase1) try connect: {phase1Url}");
        ws.Connect();
    }

    // ===== 3) 본 채널: /ws/{pid} =====
    private void ConnectToMainChannel(string url)
    {
        // 안전: 기존 소켓 닫기
        try { ws?.Close(); } catch { }
        ws = null;

        ws = new WS.WebSocket(url);

        ws.OnOpen += (s, e) =>
        {
            if (cLog) CustomLog($"[WS] (main) connected: {url}");
            // 자동 시작이 필요하면 한 줄 추가 (원치 않으면 주석)
            // RequestSync();
            syncStarted = true;
            _lastSentLocalPos = Vector3.positiveInfinity;
        };

        ws.OnMessage += (s, e) =>
        {
            try
            {
                try
                {
                    ServerSnapshot snap = JsonUtility.FromJson<ServerSnapshot>(e.Data);

                    // snap.players가 null이면 실패로 판단
                    if (snap != null && snap.players != null)
                    {
                        // ---- 플레이어 스냅샷 ----
                        ApplySnapshot(snap.players);

                        // ---- 아이템 스냅샷 ----
                        if (snap.items != null)
                        {
                            ApplyItemSnapshot(snap.items);
                        }
                        return;
                    }
                }
                catch { /* ServerSnapshot parse 실패 시 다음 단계 진행 */ }

                if (cLog) CustomLog("[WS] (main) msg: " + e.Data);
            }
            catch (Exception ex)
            {
                if (cLog) CustomLog("[WS] (main) parse error: " + ex.Message);
            }
        };

        ws.OnError += (s, e) => { if (cLog) CustomLog($"[WS] (main) ERROR: {e?.Message ?? "(null)"}"); };
        ws.OnClose += (s, e) =>
        {
            if (cLog) CustomLog($"[WS] (main) closed. Code:{e.Code}, Reason:{e.Reason}");
        };

        try
        {
            if (cLog) CustomLog($"[WS] (main) try connect: {url}");
            ws.Connect();
        }
        catch (Exception ex)
        {
            if (cLog) CustomLog($"[WS] (main) Connect EXCEPTION: {ex.Message}");
        }
    }

    // ===== 4) 재개(resume) 시도: /ws/{pid} 바로 붙기 =====
    private bool TryConnectMain(string url)
    {
        bool opened = false;
        var temp = new WS.WebSocket(url);

        temp.OnOpen += (s, e) => { opened = true; if (cLog) CustomLog($"[WS] (resume) opened: {url}"); };
        temp.OnError += (s, e) => { if (cLog) CustomLog($"[WS] (resume) ERROR: {e?.Message ?? "(null)"}"); };
        temp.OnClose += (s, e) => { if (cLog) CustomLog($"[WS] (resume) closed: {e.Code}"); };
        temp.OnMessage += (s, e) => { if (cLog) CustomLog("[WS] (resume) msg: " + e.Data); };

        try { temp.Connect(); }
        catch (Exception ex) { if (cLog) CustomLog($"[WS] (resume) Connect EX: {ex.Message}"); }

        if (opened)
        {
            try { ws?.Close(); } catch { }
            ws = temp;
            myPid = _phase2Pid; // ★ resume 성공 시 내 pid 유지
            return true;
        }

        try { temp.Close(); } catch { }
        return false;
    }

    private void OnApplicationQuit() { ws?.Close(); }
    private void OnDestroy() { ws?.Close(); }

    // ===== 2. 좌표 전송/수신 처리 (Update) =====
    private void Update()
    {
        bool canSend = ws != null && ws.ReadyState == WS.WebSocketState.Open && syncStarted;

        if (canSend)
        {
            sendTimer += Time.deltaTime;
            float interval = 1f / Mathf.Clamp(sendHz, 5, 60);
            if (sendTimer >= interval)
            {
                sendTimer = 0f;
                SendMyPose();
            }
        }

        foreach (var kv in agents) kv.Value.Tick(moveLerp, rotLerp);
    }

    // ===== 3. 동기화 시작 요청 (내부 자동 호출 또는 UI 버튼 연결) =====
    public void RequestSync()
    {
        if (ws == null || ws.ReadyState != WS.WebSocketState.Open)
        {
            if (cLog) CustomLog("[WS] Cannot request sync: WS not open.");
            return;
        }

        if (string.IsNullOrEmpty(myId))
        {
            if (cLog) CustomLog("[WS] Cannot request sync: ID not assigned yet.");
            return;
        }

        if (syncStarted) return;

        syncStarted = true;
        _lastSentLocalPos = Vector3.positiveInfinity;
        if (cLog) CustomLog("[WS] Sync started.");
    }

    public void StopSync()
    {
        syncStarted = false;
        if (cLog) CustomLog("[WS] Sync stopped");
    }

    // === CVR Util ===
    private static Vector2 Rot2D(Vector2 v, float angRad)
    {
        float c = Mathf.Cos(angRad), s = Mathf.Sin(angRad);
        return new Vector2(c * v.x - s * v.y, s * v.x + c * v.y);
    }

    // === 룸픽스 순간에 호출(버튼 OnClick 등) ===
    public void CalibrateCVR()
    {
        if (poseProvider == null || !poseProvider.HasRoomLock)
        {
            if (cLog) CustomLog("[CVR] Calibrate failed: poseProvider not ready.");
            return;
        }

        // 룸픽스 순간의 기준점 + 전방
        cvrOriginXZ = poseProvider.RoomXZ;                 // (x,z)
        float yawRad = poseProvider.RoomYawDeg * Mathf.Deg2Rad;
        Vector2 fwdXZ = new Vector2(Mathf.Cos(yawRad), Mathf.Sin(yawRad)).normalized; // 전방 단위벡터(XZ) — 없으면 RoomYawDeg 사용
        if (fwdXZ.sqrMagnitude < 1e-6f)
        {
            fwdXZ = new Vector2(Mathf.Cos(yawRad), Mathf.Sin(yawRad)).normalized;
        }
        cvrYawRad = Mathf.Atan2(fwdXZ.y, fwdXZ.x); // ψ
        cvrScale = 1f;                             // 필요시 추정값 반영

        hasCVR = true;
        if (cLog) CustomLog($"[CVR] Calibrated. O=({cvrOriginXZ.x:F2},{cvrOriginXZ.y:F2}), ψ={cvrYawRad * Mathf.Rad2Deg:F1}°");
    }

    // ===== 4. 내 위치 송신 로직 =====
    private void SendMyPose()
    {
        if (ws == null || ws.ReadyState != WS.WebSocketState.Open)
        {
            if (cLog) CustomLog("[SEND DEBUG] Failed to send: WS not open.");
            return;
        }

        Vector3 localPos;
        float localYaw;
        if (poseProvider != null && poseProvider.HasRoomLock)   // Fix 이후만 true
        {
            var xz = poseProvider.RoomXZ;                       // room-space XZ

            // 2. 회전(Yaw) 계산 [추가]
            float roomYaw = poseProvider.RoomYawDeg;

            if (useCVR)
            {
                if (!hasCVR) { if (cLog) CustomLog("[CVR] Skip send: not calibrated."); return; }
                // p_cvr = R(−ψ) * (p_local − O) * s
                Vector2 cvr = Rot2D((xz - cvrOriginXZ) * cvrScale, -cvrYawRad);
                localPos = new Vector3(cvr.x, 0f, cvr.y);

                // 회전 변환 [추가]
                // CVR 각도 = 로컬 각도 - 보정 각도(Rad -> Deg 변환 필요)
                localYaw = roomYaw - (cvrYawRad * Mathf.Rad2Deg) - 90f;
            }
            else
            {
                localPos = new Vector3(xz.x, 0f, xz.y); // 기존 로컬 그대로
                localYaw = roomYaw; // [추가] CVR 안쓰면 그대로
            }
        }
        else
        {
            if (cLog) CustomLog("[SEND DEBUG] Skipped: poseProvider not ready (no room lock).");
            return;
        }

        Vector3? handOut = null;

        if (includeHand && handProvider != null && poseProvider != null && poseProvider.HasRoomLock)
        {
            if (handProvider.TryGetHandWorld(out var handWorld))
            {
                Vector3 handRoom; // ← 미리 선언
                if (poseProvider.TryWorldToRoom(handWorld, out handRoom))
                {
                    if (useCVR && hasCVR)
                    {
                        Vector2 hxz = new Vector2(handRoom.x, handRoom.z);
                        Vector2 hcvr = Rot2D((hxz - cvrOriginXZ) * cvrScale, -cvrYawRad);
                        handOut = new Vector3(hcvr.x, handRoom.y, hcvr.y);
                    }
                    else
                    {
                        handOut = handRoom;
                    }
                }
            }
        }

        //11.20.01.43 틱 주기 확인용
        //if ((_lastSentLocalPos - localPos).sqrMagnitude < (minDeltaToSend * minDeltaToSend))
        //{
        //    if (cLog) CustomLog("[SEND DEBUG] Skipped: Movement below threshold.");
        //    return;
        //}
        //_lastSentLocalPos = localPos;

        var wrap = new CoordinateWrap
        {
            coordinate = new Coordinate { x = localPos.x, z = localPos.z, r = localYaw },
            hand = handOut.HasValue ? new Hand(handOut.Value) : null,
            grabbedItemId = string.IsNullOrEmpty(currentGrabbedItemId) ? null : currentGrabbedItemId
        };
        var json = JsonUtility.ToJson(wrap);

        if (cLog) CustomLog($"[SEND DEBUG] Attempting send: {json}");

        try
        {
            ws?.Send(json);
        }
        catch (Exception ex)
        {
            if (cLog) CustomLog($"[SEND FATAL] ws.Send() failed: {ex.Message}");
        }

        // === 여기서부터 아이템 pose 스트리밍 추가 (A안) ===
        if (!string.IsNullOrEmpty(currentGrabbedItemId) &&
            SimpleGrabbedItem.TryGet(currentGrabbedItemId, out var item) &&
            item != null &&
            item.rb != null)
        {
            // 1) 내가 아직 이 아이템의 "오너"인지 판정
            //    - 손에 들고 있으면(isLocallyGrabbed) 무조건 오너
            //    - 손에서 놓았더라도, 아직 충분히 움직이고 있으면(떨어지는 중) 오너 유지
            float v2 = item.rb.velocity.sqrMagnitude;
            const float restThreshold = 0.001f;

            bool isOwner = item.isLocallyGrabbed || v2 >= restThreshold;

            if (xLog) CustomLog($"[ITEM SEND DEBUG] ID: {currentGrabbedItemId}, IsOwner: {isOwner}, LocalGrab: {item.isLocallyGrabbed}, Vel: {v2}");

            if (isOwner)
            {
                // 2) 좌표 변환: World -> Room Local -> CVR (공통 좌표)
                Vector3 finalPos = item.transform.position;
                if (poseProvider != null && poseProvider.HasRoomLock)
                {
                    // A. Room Local로 변환
                    Vector3 roomPos = poseProvider.GetRoomAnchor().InverseTransformPoint(item.transform.position);

                    // B. CVR 사용 시 공통 좌표로 변환
                    if (useCVR && hasCVR)
                    {
                        Vector2 roomXZ = new Vector2(roomPos.x, roomPos.z);
                        // 회전(-ψ) 및 스케일 적용 (플레이어와 동일한 로직)
                        Vector2 cvrXZ = Rot2D((roomXZ - cvrOriginXZ) * cvrScale, -cvrYawRad);

                        // y(높이)는 그대로 사용하거나 필요시 보정 (여기선 그대로)
                        finalPos = new Vector3(cvrXZ.x, roomPos.y, cvrXZ.y);
                    }
                    else
                    {
                        // CVR 안 쓰면 Room Local 그대로 전송
                        finalPos = roomPos;
                    }
                }

                ItemPoseMsg m = new ItemPoseMsg
                {
                    id = currentGrabbedItemId,
                    px = finalPos.x,
                    py = finalPos.y, // 높이
                    pz = finalPos.z
                };

                string poseJson = JsonUtility.ToJson(m);
                if (cLog) CustomLog($"[SEND ITEM] {poseJson}");

                try
                {
                    ws?.Send(poseJson);
                }
                catch (Exception ex)
                {
                    if (cLog) CustomLog($"[SEND ITEM FATAL] {ex.Message}");
                }
            }
            else
            {
                // 3) 속도가 충분히 작으면 "멈췄다"고 보고 소유권 해제
                if (cLog) CustomLog($"[ITEM REST] item={currentGrabbedItemId} at rest → release ownership");
                currentGrabbedItemId = "";
                if (xLog) CustomLog($"[ITEM ERROR] currentGrabbedItemId is '{currentGrabbedItemId}' but TryGet failed!");
            }
        }
    }

    // ===== 5. 수신 스냅샷 적용 로직 =====
    private void ApplySnapshot(IEnumerable<PlayerData> players)
    {
        var seen = new HashSet<string>();
        _occupiedItemIds.Clear();

        // ★ [추가] 룸 앵커 가져오기 (앵커 없으면 아직 그릴 수 없음)
        Transform roomAnchor = null;
        if (poseProvider != null)
        {
            roomAnchor = poseProvider.GetRoomAnchor(); // 아까 RPD에 추가한 함수 호출
        }
        if (roomAnchor == null) return; // 앵커 없으면 중단 (안전장치)

        foreach (var p in players)
        {
            if (cLog) CustomLog($"[SYNC] Processing ID: {p.id}. My ID is {myId}.");

            if (string.IsNullOrEmpty(p.id)) continue;

            // [추가된 부분] 내 아이디(myId)라면 렌더링 로직 건너뛰기
            // renderSelfFromServer가 false(기본값)라면 내 아바타는 생성/갱신하지 않음
            if (p.id == myId && !renderSelfFromServer)
            {
                continue;
            }

            seen.Add(p.id);

            if (cLog)
            {
                string gi = string.IsNullOrEmpty(p.grabbedItemId) ? "(none)" : p.grabbedItemId;
                CustomLog($"[SYNC] Processing ID: {p.id}. My ID is {myId}. grabbedItem={gi}");
            }

            // body
            Vector3 world = new Vector3(p.x, 0f, p.z);
            Vector3 localPosBody;
            Quaternion localRotBody;

            // CVR을 쓴다면: [CVR -> Room] 역변환 필요
            if (useCVR && hasCVR)
            {
                // 보낼 때: (Room - Origin) * Scale -> 회전(-Angle)
                // 받을 때: 회전(+Angle) -> 나누기 Scale -> 더하기 Origin
                Vector2 cvrXZ = new Vector2(p.x, p.z);
                Vector2 roomXZ = Rot2D(cvrXZ, cvrYawRad) / cvrScale + cvrOriginXZ; // +cvrYawRad (부호 반대)

                localPosBody = new Vector3(roomXZ.x, 0f, roomXZ.y);

                // 2. 회전 역변환 [추가]
                // 받은 각도(p.r)에 보정 각도(cvrYawRad)를 더해줍니다.
                float restoredYaw = p.r + (cvrYawRad * Mathf.Rad2Deg);
                localRotBody = Quaternion.Euler(0f, restoredYaw, 0f);
            }
            else
            {
                // CVR 안 쓰면 받은 게 곧 룸 로컬 좌표
                localPosBody = new Vector3(p.x, 0f, p.z);
                localRotBody = Quaternion.Euler(0f, p.r, 0f); // [추가]
            }

            Quaternion localRot = Quaternion.identity; // 회전은 일단 기본값

            // 손 좌표 계산
            bool hasHand = (p.hand != null);
            Vector3 localPosHand = Vector3.zero;
            Quaternion localRotHand = Quaternion.identity;

            if (hasHand)
            {
                if (useCVR && hasCVR)
                {
                    // 손도 똑같이 역변환 (높이 y는 그대로)
                    Vector2 hCvrXZ = new Vector2(p.hand.x, p.hand.z);
                    Vector2 hRoomXZ = Rot2D(hCvrXZ, cvrYawRad) / cvrScale + cvrOriginXZ;
                    localPosHand = new Vector3(hRoomXZ.x, p.hand.y, hRoomXZ.y);
                }
                else
                {
                    localPosHand = new Vector3(p.hand.x, p.hand.y, p.hand.z);
                }
            }

            //modify
            if (!agents.TryGetValue(p.id, out var agent) || agent == null || agent.tr == null)
            {
                if (cLog) CustomLog($"[SYNC DEBUG] Creating NEW avatar for ID: {p.id} at World Pos: {world}");

                // 신규 생성: 부모를 roomAnchor로 지정!
                Transform tr;
                if (remoteAvatarPrefab)
                    tr = Instantiate(remoteAvatarPrefab, roomAnchor).transform; // 부모 지정
                else
                {
                    GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    go.name = $"Remote_{p.id}";
                    go.transform.SetParent(roomAnchor, false); // 부모 지정
                    tr = go.transform;
                }

                // 위치 초기화
                tr.localPosition = localPosBody;
                tr.localRotation = localRotBody;

                Transform handTr = null;
                if (remoteHandPrefab && hasHand)
                {
                    handTr = Instantiate(remoteHandPrefab, roomAnchor).transform; // 부모 지정
                    handTr.name = $"Remote_{p.id}_Hand";
                    handTr.localPosition = localPosHand;
                    handTr.localRotation = localRotHand;
                }

                agent = new RemoteAgent(tr, handTr);
                agents[p.id] = agent;
            }
            else
            {
                if (cLog) CustomLog($"[SYNC DEBUG] Updating avatar {p.id} to World Pos: {world}");
            }

            // 업데이트: 목표 값을 로컬 좌표로 설정
            agent.targetLocalPos = localPosBody;
            agent.targetLocalRot = localRotBody;

            if (hasHand)
            {
                agent.targetHandLocalPos = localPosHand;
                agent.targetHandLocalRot = localRotHand;
            }

            if (!string.IsNullOrEmpty(p.grabbedItemId))
            {
                // 이번 프레임에 원격으로 "잡힌 상태"라고 표시된 아이템 목록에 추가
                _occupiedItemIds.Add(p.grabbedItemId);
            }

            bool isSelf = (p.id == myId);

            if (!isSelf && !string.IsNullOrEmpty(p.grabbedItemId))
            {
                // 실제 아이템 오브젝트 찾아오기
                if (SimpleGrabbedItem.TryGet(p.grabbedItemId, out var item))
                {
                    // 내가 직접 잡고 있는 아이템이면 원격 정보로 덮어쓰지 않음
                    if (!item.isLocallyGrabbed)
                    {
                        // 원격 Grab 상태 진입
                        item.OnRemoteGrabStart();
                    }
                }
            }
        }

        //    — 유령 잔상 방지
        var toRemove = new List<string>();
        foreach (var kv in agents)
            if (!seen.Contains(kv.Key)) toRemove.Add(kv.Key);

        foreach (var id in toRemove)
        {
            if (agents[id] != null)
            {
                if (agents[id].tr)
                    Destroy(agents[id].tr.gameObject);

                if (agents[id].handTr)
                    Destroy(agents[id].handTr.gameObject);
            }
            agents.Remove(id);
        }

        foreach (var kv in SimpleGrabbedItem.Registry)
        {
            var item = kv.Value;
            if (item == null) continue;

            // 내가 직접 잡은 아이템이면 건들지 않음
            if (item.isLocallyGrabbed) continue;

            // 이번 스냅샷에서 "원격 grabbed" 목록에 없으면 → Free
            if (!_occupiedItemIds.Contains(item.itemId))
            {
                item.OnRemoteGrabEnd();
            }
        }
    }

    private void ApplyItemSnapshot(ItemPayload[] items)
    {
        // 룸 앵커 확인
        Transform roomAnchor = null;
        if (poseProvider != null) roomAnchor = poseProvider.GetRoomAnchor();
        if (roomAnchor == null) return;

        foreach (var data in items)
        {
            string itemId = data.id; // 배열 객체에서 ID 꺼내기

            // [체크 1] 아이템이 레지스트리에 있는가?
            if (!SimpleGrabbedItem.TryGet(itemId, out var item) || item == null)
            {
                // 너무 자주 뜨면 주석 처리
                // if (xLog) CustomLog($"[SYNC FAIL] {itemId} not found in Registry!");
                continue;
            }

            // 내가 잡고 있으면 무시
            if (item.isLocallyGrabbed)
            {
                // if (xLog) CustomLog($"[SYNC SKIP] {itemId} is locally grabbed.");
                continue;
            }

            // 내가 방금 던져서 소유권이 아직 나한테 있으면 무시
            if (itemId == currentGrabbedItemId)
            {
                // if (xLog) CustomLog($"[SYNC SKIP] {itemId} ID matches my currentGrabbedItemId.");
                continue;
            }

            // === 좌표 역변환: CVR -> Room Local -> World ===
            Vector3 targetRoomPos;

            if (useCVR && hasCVR)
            {
                // 받은 데이터(x, z)는 CVR 좌표임. 역변환 수행.
                Vector2 cvrXZ = new Vector2(data.x, data.z);

                // Rot2D(..., +cvrYawRad) -> Origin 더하기
                Vector2 roomXZ = Rot2D(cvrXZ, cvrYawRad) / cvrScale + cvrOriginXZ;

                // y는 높이 그대로
                targetRoomPos = new Vector3(roomXZ.x, data.y, roomXZ.y);
            }
            else
            {
                // CVR 안 쓰면 받은 게 곧 Room Local
                targetRoomPos = new Vector3(data.x, data.y, data.z);
            }

            // Room Local -> World 변환
            Vector3 targetWorldPos = roomAnchor.TransformPoint(targetRoomPos);

            // === 부드러운 위치 동기화 (Lerp) ===
            float dist = Vector3.Distance(item.transform.position, targetWorldPos);

            // 거리가 너무 멀면(초기화 등) 바로 텔레포트, 아니면 부드럽게 이동
            if (dist > 1.0f)
            {
                item.transform.position = targetWorldPos;
            }
            else
            {
                // sendHz(15) 등을 고려해 적절한 속도로 보간 (Time.deltaTime * lerpSpeed)
                item.transform.position = Vector3.Lerp(item.transform.position, targetWorldPos, Time.deltaTime * moveLerp);
            }

            // (선택) 회전 동기화는 현재 데이터에 없으므로 생략됨.
            // 필요하다면 ItemPoseMsg에 rx, ry, rz 등을 추가해야 함.
        }
    }


    // ===== 6. 원격 에이전트 클래스 (위치 보간) =====
    private class RemoteAgent
    {
        public Transform tr;
        public Transform handTr;

        public Vector3 targetLocalPos;
        public Quaternion targetLocalRot;

        public Vector3 targetHandLocalPos;
        public Quaternion targetHandLocalRot;

        public RemoteAgent(Transform t, Transform th)
        {
            tr = t;
            targetLocalPos = t.localPosition;
            targetLocalRot = t.localRotation;

            handTr = th;

            if (handTr != null)
            {
                targetHandLocalPos = th.localPosition;
                targetHandLocalRot = th.localRotation;
            }
        }

        //수정됨
        public void Tick(float moveLerp, float rotLerp)
        {
            if (!tr) return;
            float a = 1f - Mathf.Exp(-moveLerp * Time.deltaTime);
            float b = 1f - Mathf.Exp(-rotLerp * Time.deltaTime);
            tr.localPosition = Vector3.Lerp(tr.localPosition, targetLocalPos, a);
            tr.localRotation = Quaternion.Slerp(tr.localRotation, targetLocalRot, b);
            if (handTr != null)
            {
                handTr.localPosition = Vector3.Lerp(handTr.localPosition, targetHandLocalPos, a);
                handTr.localRotation = Quaternion.Slerp(handTr.localRotation, targetHandLocalRot, b);
            }
        }
    }
}
