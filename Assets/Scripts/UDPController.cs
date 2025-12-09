using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

public class UDPController : MonoBehaviour
{
    [Header("Network")]
    public int port = 8080;

    [Header("Objects")]
    public Transform playerRig;
    public Transform controllerHand;
    public Transform grabPoint;
    public LayerMask interactLayer;

    [Header("Hand Positioning")]
    [Range(0, 1)] public float offsetRight = 0.3f;
    [Range(0, 1)] public float offsetDown = 0.3f;
    [Range(0, 1)] public float offsetForward = 0.4f;

    [Header("Joystick Calibration")]
    public float moveSpeed = 2.0f;
    public float deadZone = 0.05f;
    public bool swapXY = false;        
    public bool invertHorizontal = false; 
    public bool invertVertical = false;

    // ★ [수정] 손 회전 보정 (기본값 0,0,90)
    // 만약 손이 여전히 이상하게 돌아가 있으면 Y값을 조절하세요 (예: 0, 100, 90)
    public Vector3 rotationOffset = new Vector3(0, 0, 90);

    private UdpClient udpClient;
    private Thread receiveThread;
    
    private Quaternion targetRotation = Quaternion.identity;
    private Vector2 joystickInput = new Vector2(2048, 2048);
    private Vector2 joystickCenter = new Vector2(2048, 2048);
    private bool isJoystickCalibrated = false;

    // ★ 방향 영점 잡기용 변수
    private float startYaw = 0f; 
    private bool isYawCalibrated = false;

    private bool isBtnPressed = false;
    private bool wasBtnPressed = false; 
    private GameObject grabbedObject = null; 
    private LineRenderer laserLine;

    void Start()
    {
        laserLine = controllerHand.GetComponent<LineRenderer>();
        if (laserLine != null) laserLine.useWorldSpace = true;

        receiveThread = new Thread(new ThreadStart(ReceiveData));
        receiveThread.IsBackground = true;
        receiveThread.Start();
    }

    void Update()
    {
        // 1. 손 위치 (Body Lock + 시야 기준 + 오프셋)
        if (Camera.main != null) {
            Transform camT = Camera.main.transform;
            Vector3 bodyForward = camT.forward; bodyForward.y = 0; bodyForward.Normalize();
            Vector3 bodyRight = camT.right;     bodyRight.y = 0;   bodyRight.Normalize();

            Vector3 shoulderPos = camT.position 
                                + (Vector3.down * offsetDown) 
                                + (bodyForward * offsetForward) 
                                + (bodyRight * offsetRight); 

            controllerHand.position = Vector3.Lerp(controllerHand.position, shoulderPos, Time.deltaTime * 5f);
        }

        // 2. 회전 (보정값 적용)
        // 센서 회전 * 인스펙터 보정값(90도 등)
        Quaternion finalRotation = targetRotation * Quaternion.Euler(rotationOffset);
        controllerHand.rotation = Quaternion.Slerp(controllerHand.rotation, finalRotation, Time.deltaTime * 10f);

        // 3. 이동 및 잡기
        MovePlayer();
        HandleGrab();
    }

    void MovePlayer()
    {
        if (!isJoystickCalibrated) return;

        float rawX = (joystickInput.x - joystickCenter.x) / 2048f;
        float rawY = (joystickInput.y - joystickCenter.y) / 2048f;

        float inputHorizontal = swapXY ? rawY : rawX; 
        float inputVertical   = swapXY ? rawX : rawY;

        if (invertHorizontal) inputHorizontal = -inputHorizontal;
        if (invertVertical)   inputVertical   = -inputVertical;

        if (Mathf.Abs(inputHorizontal) < deadZone) inputHorizontal = 0;
        if (Mathf.Abs(inputVertical) < deadZone)   inputVertical = 0;

        if (Camera.main != null)
        {
            Transform camT = Camera.main.transform;
            Vector3 forward = camT.forward; forward.y = 0; forward.Normalize();
            Vector3 right = camT.right;     right.y = 0;   right.Normalize();

            Vector3 moveDir = (forward * inputVertical) + (right * inputHorizontal);

            if (moveDir.magnitude > 0.01f)
            {
                moveDir.Normalize(); 
                playerRig.Translate(moveDir * moveSpeed * Time.deltaTime, Space.World);
            }
        }
    }

    void HandleGrab()
    {
        Vector3 rayOrigin = controllerHand.position + (controllerHand.forward * 0.2f); 
        RaycastHit hit;
        bool isHit = Physics.Raycast(rayOrigin, controllerHand.forward, out hit, 100.0f, interactLayer);

        if (laserLine != null)
        {
            laserLine.SetPosition(0, rayOrigin);
            laserLine.startColor = isHit ? Color.green : Color.red;
            laserLine.endColor = isHit ? Color.green : Color.red;
            float dist = isHit ? hit.distance : 100.0f;
            if (grabbedObject != null) dist = Vector3.Distance(rayOrigin, grabbedObject.transform.position);
            laserLine.SetPosition(1, rayOrigin + (controllerHand.forward * dist));
        }

        if (isBtnPressed && !wasBtnPressed)
        {
            if (grabbedObject == null) {
                if (isHit) { 
                    grabbedObject = hit.collider.gameObject;
                    grabbedObject.transform.SetParent(controllerHand); 
                    if(grabbedObject.GetComponent<Rigidbody>()) grabbedObject.GetComponent<Rigidbody>().isKinematic = true; 
                    grabbedObject.transform.position = grabPoint.position; 
                }
            } else {
                grabbedObject.transform.SetParent(null); 
                if(grabbedObject.GetComponent<Rigidbody>()) grabbedObject.GetComponent<Rigidbody>().isKinematic = false; 
                grabbedObject = null;
            }
        }
        wasBtnPressed = isBtnPressed;
    }

    private void ReceiveData() {
        try {
            udpClient = new UdpClient(port);
            while (true) {
                try {
                    IPEndPoint anyIP = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = udpClient.Receive(ref anyIP);
                    ParsePacket(Encoding.UTF8.GetString(data));
                } catch (System.Exception) { Thread.Sleep(10); }
            }
        } catch(System.Exception) {}
    }

    void ParsePacket(string data) {
        try {
            string[] parts = data.Split('|');
            foreach (string part in parts) {
                if (part.StartsWith("RPY:")) {
                    string[] vals = part.Substring(4).Split(',');
                    float r = float.Parse(vals[0]); 
                    float p = float.Parse(vals[1]); 
                    float y = float.Parse(vals[2]);

                    // ★ [핵심] 첫 번째 들어온 Yaw 값을 기준점(0도)으로 잡음
                    if (!isYawCalibrated) {
                        startYaw = y;
                        isYawCalibrated = true;
                    }

                    // 현재 Yaw에서 시작 Yaw를 뺌 (정면 보정)
                    float correctedYaw = y - startYaw;

                    targetRotation = Quaternion.Euler(p, -correctedYaw, -r);
                }
                else if (part.StartsWith("JOY:")) {
                    string[] vals = part.Substring(4).Split(',');
                    float rawX = float.Parse(vals[0]);
                    float rawY = float.Parse(vals[1]);
                    joystickInput = new Vector2(rawX, rawY);

                    if (!isJoystickCalibrated && rawX > 10 && rawY > 10) {
                        joystickCenter = new Vector2(rawX, rawY);
                        isJoystickCalibrated = true;
                    }
                }
                else if (part.StartsWith("BTN:")) {
                    isBtnPressed = (part.Substring(4) == "1");
                }
            }
        } catch {}
    }
    
    void OnApplicationQuit() {
        if (receiveThread != null) receiveThread.Abort();
        if (udpClient != null) udpClient.Close();
    }
}