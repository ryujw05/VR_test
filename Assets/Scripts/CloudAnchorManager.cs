using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using Google.XR.ARCoreExtensions;

public class SharedOriginManager : MonoBehaviour
{
    [Header("Refs")]
    public ARAnchorManager anchorManager;
    public XROrigin xrOrigin;
    public ARCoreExtensions arCoreExt;                // Extensions 컴포넌트
    public Transform sharedOrigin;

    [Header("Host Settings")]
    [Tooltip("세션 Tracking 대기 최대 시간 (초)")]
    public float trackingWaitSec = 5f;
    [Tooltip("호스팅 전 스캔 품질이 Sufficient 이상 될 때까지 대기 최대 시간 (초)")]
    public float qualityWaitSec = 5f;
    [Tooltip("Cloud Anchor TTL (일)")]
    public int ttlDays = 1;

    [Header("Info (read-only)")]
    public string cloudId;

    void Awake()
    {
        if (!anchorManager) anchorManager = FindObjectOfType<ARAnchorManager>();
        if (!xrOrigin) xrOrigin = FindObjectOfType<XROrigin>();
        if (!arCoreExt) arCoreExt = FindObjectOfType<ARCoreExtensions>();
    }

    // ====== 외부에서 호출: 버튼 핸들러 등에서 사용 ======
    public void HostAtPose(Vector3 pos, Quaternion rot, int ttlDaysOverride = -1)
    {
        if (ttlDaysOverride > 0) ttlDays = ttlDaysOverride;
        StartCoroutine(CoHost(pos, rot));
    }

    private IEnumerator CoHost(Vector3 pos, Quaternion rot)
    {
        // 0) 필수 레퍼런스 확인
        if (!anchorManager)
        {
            Debug.LogWarning("[Host] ARAnchorManager missing");
            yield break;
        }
        if (!arCoreExt)
        {
            Debug.LogWarning("[Host] ARCoreExtensions missing (씬에 ARCoreExtensions 컴포넌트 추가 필요)");
            yield break;
        }

        // 1) 세션 Tracking 대기
        if (ARSession.state != ARSessionState.SessionTracking)
        {
            Debug.LogWarning("[Host] ARSession not tracking yet");
            float wait = trackingWaitSec;
            while (ARSession.state != ARSessionState.SessionTracking && wait > 0f)
            {
                wait -= Time.deltaTime;
                yield return null;
            }
            if (ARSession.state != ARSessionState.SessionTracking)
            {
                Debug.LogWarning("[Host] Tracking timeout");
                yield break;
            }
        }

        // 2) 호스팅 품질(특징점) 충분할 때까지 잠깐 대기
        //    ARCoreExtensions의 EstimateFeatureMapQualityForHosting 사용
        Pose probePose = new Pose(pos, rot);
        float qWait = qualityWaitSec;
        FeatureMapQuality quality = FeatureMapQuality.Insufficient;
        while (qWait > 0f)
        {
            quality = ARAnchorManagerExtensions.EstimateFeatureMapQualityForHosting(anchorManager, probePose);
            if (quality == FeatureMapQuality.Sufficient || quality == FeatureMapQuality.Good)
                break;

            // 사용자가 좀 더 스캔하도록 0.2초 주기 체크
            qWait -= 0.2f;
            yield return new WaitForSeconds(0.2f);
        }

        if (!(quality == FeatureMapQuality.Sufficient || quality == FeatureMapQuality.Good))
        {
            Debug.LogWarning($"[Host] Feature map quality still low: {quality}. 주변을 더 스캔한 뒤 다시 시도해주세요.");
            yield break;
        }

        // 3) 로컬 앵커 생성 (TrackablesParent 아래 권장)
        var go = new GameObject("LocalAnchor_Host");
        go.transform.SetPositionAndRotation(pos, rot);
        if (xrOrigin && xrOrigin.TrackablesParent)
            go.transform.SetParent(xrOrigin.TrackablesParent, true);
        var local = go.AddComponent<ARAnchor>();

        // 로컬 앵커 트래킹 잠깐 확인
        float timeout = 2f;
        while (local.trackingState != UnityEngine.XR.ARSubsystems.TrackingState.Tracking && timeout > 0f)
        {
            timeout -= Time.deltaTime;
            yield return null;
        }
        if (local.trackingState != UnityEngine.XR.ARSubsystems.TrackingState.Tracking)
        {
            Debug.LogWarning("[Host] Anchor not tracking");
            Destroy(go);
            yield break;
        }

        // 4) 클라우드 앵커 Host
        var promise = ARAnchorManagerExtensions.HostCloudAnchorAsync(anchorManager, local, ttlDays);
        yield return promise;

        var result = promise.Result;
        if (result.CloudAnchorState == CloudAnchorState.Success && !string.IsNullOrEmpty(result.CloudAnchorId))
        {
            cloudId = result.CloudAnchorId;
            if (!sharedOrigin) sharedOrigin = new GameObject("SharedOrigin").transform;
            sharedOrigin.SetPositionAndRotation(local.transform.position, local.transform.rotation);
            Debug.Log($"[Host] OK id={cloudId}");

            // 선택: 씬의 다른 매니저들에 주입
            var roomMgr = FindObjectOfType<FloorRoomStateMachine>();
            if (roomMgr) roomMgr.transform.SetParent(sharedOrigin, true);

            var nps = FindObjectOfType<NetworkPlayerSync>();
            if (nps) nps.sharedOrigin = sharedOrigin;
        }
        else
        {
            Debug.LogWarning($"[Host] Fail: {result.CloudAnchorState}");
            // 추가 힌트
            if (result.CloudAnchorState == CloudAnchorState.ErrorNotAuthorized)
                Debug.LogWarning("[Host] NotAuthorized: API키/프로젝트 연결, ARCoreExtensionsConfig(Cloud Anchor Mode=Enabled) 확인");
            if (result.CloudAnchorState == CloudAnchorState.ErrorHostingDatasetProcessingFailed)
                Debug.LogWarning("[Host] DatasetProcessingFailed: 특징점 부족/조명/반사면/텍스처 부족 가능. 더 스캔 후 재시도");

            Destroy(go);
        }
    }
}
