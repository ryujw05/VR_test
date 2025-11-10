using System;
using System.Collections.Generic;
using UnityEngine;
using TMPro; // TextMeshPro를 사용하려면 필요합니다.
using WS = WebSocketSharp;

public class NetworkPlayerSync : MonoBehaviour
{
    [Header("Connection")]
    public string serverWsUrl = "ws://192.168.219.105:8080/ws";
    [HideInInspector]
    public string myId = "";

    [Header("Refs")]
    public Transform localPlayer;
    public Transform sharedOrigin;
    public GameObject remoteAvatarPrefab;

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


    [Header("UI Debug")]
    [Tooltip("로그 출력을 위한 TextMeshPro 컴포넌트")]
    public TMP_Text debugLogText;

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
    private bool cLog = true;
    private bool xLog = false;

    // ★ 추가: PlayerPrefs 키
    const string PREF_PID = "nps.player";  // ex) "p1"
    const string PREF_UID = "nps.uid";     // ex) "9f3b..."

    private string _phase2Pid = "";        // 현재 세션의 player (p1/p2…)

    // JSON 직렬화를 위한 클래스
    [Serializable] class JoinMessage { public string action; public string id; }
    [System.Serializable] public class Coordinate { public float x; public float y; }
    [System.Serializable] public class LookAt { public float x; public float y; public float z; }
    [System.Serializable]
    public class PosePacket
    {
        public Coordinate coordinate;
        public LookAt look_at;   // 서버가 안 써도 같이 보내는 건 무해
    }
    [Serializable] class InLook { public float x, y, z; }

    [Serializable]
    class InPlayer
    {
        public string id;
        public float x, z;     // CVR 기준 좌표 (x,y,z)
        public InLook look_at;    // 선택: 없을 수도 있음
    }

    [Serializable] class InRoot { public InPlayer[] players; }

    [Serializable] public class PlayerData { public string id; public float x; public float y; }
    [Serializable] public class PlayersRoot { public PlayerData[] players; }
    [Serializable]
    class PlayerState
    {
        public string id;
        public float x, y, z;
        public InLook look_at; // null 가능
    }
    [Serializable] class FirstReply { public string player; public string id; }


    // UI에 로그를 출력하는 헬퍼 함수
    private void CustomLog(string message)
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
        CustomLog("[System] NetworkPlayerSync Initialized. Auto-connecting...");

        // Start에서 연결 시작
        ConnectToServer(null);
    }

    // ===== 1. 접속 (내부 자동 호출) =====
    public void ConnectToServer(string ignoredPlayerId)
    {
        try
        {
            // baseUrl 추출 (기존 serverWsUrl이 ".../ws" 라고 가정)
            string baseUrl = serverWsUrl;
            if (baseUrl.EndsWith("/ws"))
                baseUrl = baseUrl.Substring(0, baseUrl.Length - 3); // "/ws" 제거

            // ① Resume: 저장된 pid가 있으면 곧장 /ws/{pid}로 붙어본다
            string savedPid = PlayerPrefs.GetString(PREF_PID, "");
            string savedUid = PlayerPrefs.GetString(PREF_UID, "");

            if (!string.IsNullOrEmpty(savedPid))
            {
                _phase2Pid = savedPid;
                myId = string.IsNullOrEmpty(savedUid) ? "" : savedUid;

                string url2 = baseUrl + "/ws/" + _phase2Pid;
                if (cLog) CustomLog($"[WS] (resume) try {url2}  pid={_phase2Pid}, uid={myId}");
                if (TryConnectMain(url2)) return;   // 붙기만 하면 끝(성공)
                if (cLog) CustomLog("[WS] (resume) failed → fallback to phase1");
            }

            // ② Phase1: /ws 에 연결 → {"name": "..."} 보내고 → {"player","id"} 받기
            Phase1_Handshake(baseUrl);
        }
        catch (Exception ex)
        {
            if (cLog) CustomLog($"[WS] ConnectToServer EXCEPTION: {ex.Message}");
        }
    }

    private void Phase1_Handshake(string baseUrl)
    {
        // 안전: 기존 열려있던 소켓 종료
        if (ws != null)
        {
            try { ws.Close(); } catch { }
            ws = null;
        }

        string phase1Url = baseUrl + "/ws";
        ws = new WS.WebSocket(phase1Url);

        ws.OnOpen += (s, e) =>
        {
            if (cLog) CustomLog($"[WS] (phase1) connected: {phase1Url}");
            // 연결 즉시 {"name": "..."} 전송
            string displayName = SystemInfo.deviceName;
            string hello = "{\"name\":\"" + displayName + "\"}";
            try { ws.Send(hello); if (cLog) CustomLog("[WS] (phase1) sent hello: " + hello); }
            catch (Exception ex) { if (cLog) CustomLog("[WS] (phase1) send hello EX: " + ex.Message); }
        };

        ws.OnMessage += (s, e) =>
        {
            try
            {
                // 기대: {"player":"p1","id":"<uid>"}
                FirstReply fr = JsonUtility.FromJson<FirstReply>(e.Data);
                if (fr != null && !string.IsNullOrEmpty(fr.player) && !string.IsNullOrEmpty(fr.id))
                {
                    _phase2Pid = fr.player;
                    myId = fr.id;
                    if (cLog) CustomLog($"[WS] (phase1) assigned: player={_phase2Pid}, uid={myId}");

                    // 영구 저장 → 이후 재접속 시 resume 경로 사용
                    PlayerPrefs.SetString(PREF_PID, _phase2Pid);
                    PlayerPrefs.SetString(PREF_UID, myId);
                    PlayerPrefs.Save();

                    // /ws 닫고 /ws/{pid} 로 본채널 연결
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

    private void ConnectToMainChannel(string url)
    {
        // 안전: 기존 열려있던 소켓 종료
        if (ws != null)
        {
            try { ws.Close(); } catch { }
            ws = null;
        }

        ws = new WS.WebSocket(url);

        ws.OnOpen += (s, e) =>
        {
            if (cLog) CustomLog($"[WS] (main) connected: {url}");
            // 이 채널에서는 RequestSync() 호출로 syncStarted를 켜서 전송 루프 시작
            // (룸고정 → CalibrateCVR → RequestSync 순서로 호출되는 기존 흐름 유지)
        };

        ws.OnMessage += (s, e) =>
        {
            try
            {
                InRoot root = JsonUtility.FromJson<InRoot>(e.Data);
                if (root != null && root.players != null)
                {
                    if (cLog) CustomLog($"[WS] (main) Parsed {root.players.Length} players.");
                    var list = new List<PlayerState>();

                    foreach (var p in root.players)
                    {
                        //if (!renderSelfFromServer && !string.IsNullOrEmpty(myId) && p.id == _phase2Pid) continue; //내 아바타 스킵

                        list.Add(new PlayerState
                        {
                            id = p.id,
                            x = p.x,
                            z = p.z,
                            look_at = p.look_at // null 가능
                        });
                    }
                    ApplySnapshot(list);
                    return;
                }

                // (호환) 만약 옛 포맷이면 여기서 처리하거나 로그만 남김
                if (cLog) CustomLog("[WS] (main) unrecognized msg: " + e.Data);
            }
            catch (Exception ex)
            {
                if (cLog) CustomLog("[WS] (main) parse error: " + ex.Message + " | raw=" + e.Data);
            }
        };

        ws.OnError += (s, e) => { if (cLog) CustomLog($"[WS] (main) ERROR: {e?.Message ?? "(null)"}"); };
        ws.OnClose += (s, e) =>
        {
            if (cLog) CustomLog($"[WS] (main) closed. Code:{e.Code}, Reason:{e.Reason}");
            // 연결이 닫혀도 PlayerPrefs는 유지 → 다음엔 resume 먼저 시도됨
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

    private bool TryConnectMain(string url)
    {
        bool opened = false;
        var temp = new WS.WebSocket(url);

        temp.OnOpen += (s, e) => { opened = true; if (cLog) CustomLog($"[WS] (resume) opened: {url}"); };
        temp.OnError += (s, e) => { if (cLog) CustomLog($"[WS] (resume) ERROR: {e?.Message ?? "(null)"}"); };
        temp.OnClose += (s, e) => { if (cLog) CustomLog($"[WS] (resume) closed: {e.Code}"); };
        temp.OnMessage += (s, e) =>
        {
            // 필요시 여기서 첫 메시지를 검사해도 됨. 이번엔 단순 성공만 본다.
            if (cLog) CustomLog("[WS] (resume) msg: " + e.Data);
        };

        try { temp.Connect(); }
        catch (Exception ex) { if (cLog) CustomLog($"[WS] (resume) Connect EX: {ex.Message}"); }

        if (opened)
        {
            // 성공 → 기존 ws를 temp로 교체
            try { ws?.Close(); } catch { }
            ws = temp;
            return true;
        }

        // 실패 → 소켓 정리
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
        if (poseProvider != null && poseProvider.HasRoomLock)   // Fix 이후만 true
        {
            var xz = poseProvider.RoomXZ;                       // room-space XZ
            if (useCVR)
            {
                if (!hasCVR) { if (cLog) CustomLog("[CVR] Skip send: not calibrated."); return; }
                Vector2 cvr = Rot2D((xz - cvrOriginXZ) * cvrScale, -cvrYawRad);
                localPos = new Vector3(cvr.x, 0f, cvr.y);
            }
            else
            {
                localPos = new Vector3(xz.x, 0f, xz.y); // 기존 로컬 그대로
            }
        }
        else
        {
            if (cLog) CustomLog("[SEND DEBUG] Skipped: poseProvider not ready (no room lock).");
            return;
        }

        float yawRad = poseProvider.RoomYawDeg * Mathf.Deg2Rad;
        float pitchRad = poseProvider.RoomPitchDeg * Mathf.Deg2Rad;

        if (useCVR && hasCVR) yawRad -= cvrYawRad;

        Vector3 lookDir = new Vector3(
            Mathf.Cos(pitchRad) * Mathf.Cos(yawRad),
            Mathf.Sin(pitchRad),
            Mathf.Cos(pitchRad) * Mathf.Sin(yawRad)
        ).normalized;

        if ((_lastSentLocalPos - localPos).sqrMagnitude < (minDeltaToSend * minDeltaToSend))
        {
            if (cLog) CustomLog("[SEND DEBUG] Skipped: Movement below threshold.");
            return;
        }
        _lastSentLocalPos = localPos;

        var pkt = new PosePacket
        {
            coordinate = new Coordinate { x = localPos.x, y = localPos.z },  // z→y 매핑 유지
            look_at = new LookAt { x = lookDir.x, y = lookDir.y, z = lookDir.z }
        };
        var json = JsonUtility.ToJson(pkt);

        if (cLog) CustomLog($"[SEND DEBUG] Attempting send: {json}");

        try
        {
            ws?.Send(json);
        }
        catch (Exception ex)
        {
            if (cLog) CustomLog($"[SEND FATAL] ws.Send() failed: {ex.Message}");
        }
    }

    // ===== 5. 수신 스냅샷 적용 로직 =====
    private void ApplySnapshot(List<PlayerState> players)
    {
        var seen = new HashSet<string>();

        foreach (var p in players)
        {
            if (cLog) CustomLog($"[SYNC] Processing ID: {p.id}. My ID is {myId}.");

            if (string.IsNullOrEmpty(p.id)) continue;
            seen.Add(p.id);

            Vector3 world = new Vector3(p.x, 0f, p.z);
            Quaternion rot = Quaternion.identity;

            if (!agents.TryGetValue(p.id, out var agent) || agent == null || agent.tr == null)
            {
                if (cLog) CustomLog($"[SYNC] spawn {p.id} @ {world}");

                Transform tr;
                if (remoteAvatarPrefab)
                    tr = Instantiate(remoteAvatarPrefab, world, rot).transform;
                else
                {
                    GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    go.name = $"Remote_{p.id} (Placeholder)";
                    go.transform.position = world;
                    go.transform.rotation = rot;
                    go.transform.localScale = Vector3.one * 0.2f;
                    tr = go.transform;
                }
                agent = new RemoteAgent(tr);
                agents[p.id] = agent;
            }
            else
            {
                if (cLog) CustomLog($"[SYNC DEBUG] Updating avatar {p.id} to World Pos: {world}");
            }

            agent.targetPos = world;
            agent.targetRot = rot;
        }

        //    — 유령 잔상 방지
        var toRemove = new List<string>();
        foreach (var kv in agents)
            if (!seen.Contains(kv.Key)) toRemove.Add(kv.Key);

        foreach (var id in toRemove)
        {
            if (agents[id]?.tr) Destroy(agents[id].tr.gameObject);
            agents.Remove(id);
        }
    }

    // ===== 6. 원격 에이전트 클래스 (위치 보간) =====
    private class RemoteAgent
    {
        public Transform tr;
        public Vector3 targetPos;
        public Quaternion targetRot;

        public RemoteAgent(Transform t)
        {
            tr = t;
            targetPos = t.position;
            targetRot = t.rotation;
        }

        public void Tick(float moveLerp, float rotLerp)
        {
            if (!tr) return;
            float a = 1f - Mathf.Exp(-moveLerp * Time.deltaTime);
            float b = 1f - Mathf.Exp(-rotLerp * Time.deltaTime);
            tr.position = Vector3.Lerp(tr.position, targetPos, a);
            tr.rotation = Quaternion.Slerp(tr.rotation, targetRot, b);
        }
    }
}
