// UIManager.cs (가상의 새 스크립트)
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UIManager : MonoBehaviour
{
    public NetworkPlayerSync networkManager; // NetworkPlayerSync 컴포넌트 참조
    public TMP_InputField idInputField;        // ID 입력 필드 참조
    public Button connectButton;            // 연결 버튼 참조

    private void Start()
    {
        connectButton.onClick.AddListener(OnConnectButtonClicked);
    }

    private void OnConnectButtonClicked()
    {
        string inputId = idInputField.text.Trim();

        if (!string.IsNullOrEmpty(inputId) && networkManager != null)
        {
            // 입력받은 ID로 연결 시작
            networkManager.ConnectToServer(inputId);

            // 연결 UI 비활성화 등 추가 작업
            idInputField.gameObject.SetActive(false);
            connectButton.gameObject.SetActive(false);
        }
        else
        {
            Debug.LogWarning("ID를 입력하거나 Network Manager를 확인해주세요.");
        }
    }
}