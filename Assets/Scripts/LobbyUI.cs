using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class LobbyUI : MonoBehaviour
{
    // Unity Inspector에서 할당할 요소들
    public GameObject RoomSlotPrefab;
    public Transform ContentParent;

    [Header("Room Creation")]
    public Button CreateRoomButton;
    public GameObject CreateRoomPanel;
    public TMP_InputField RoomNameInputField;
    public Button ConfirmCreateButton;
    public Button CancelCreateButton;

    [Header("Refresh")]
    public Button RefreshButton;
    
    [Header("Auto Refresh")]
    public float autoRefreshInterval = 5f; // 5초마다 자동 새로고침

    private float _lastRefreshTime = 0f;

    private void Start()
    {
        // 방 생성 버튼 이벤트 설정
        if (CreateRoomButton != null)
        {
            CreateRoomButton.onClick.RemoveAllListeners();
            CreateRoomButton.onClick.AddListener(OnCreateRoomButtonClicked);
        }

        if (ConfirmCreateButton != null)
        {
            ConfirmCreateButton.onClick.RemoveAllListeners();
            ConfirmCreateButton.onClick.AddListener(OnConfirmCreateButtonClicked);
        }

        if (CancelCreateButton != null)
        {
            CancelCreateButton.onClick.RemoveAllListeners();
            CancelCreateButton.onClick.AddListener(OnCancelCreateButtonClicked);
        }

        if (RefreshButton != null)
        {
            RefreshButton.onClick.RemoveAllListeners();
            RefreshButton.onClick.AddListener(OnRefreshButtonClicked);
        }

        // 초기에는 생성 패널 숨김
        if (CreateRoomPanel != null)
        {
            CreateRoomPanel.SetActive(false);
        }
    }

    private void OnCreateRoomButtonClicked()
    {
        if (CreateRoomPanel != null)
        {
            CreateRoomPanel.SetActive(true);
        }

        if (RoomNameInputField != null)
        {
            RoomNameInputField.text = "";
            RoomNameInputField.Select();
        }
    }

    private void OnConfirmCreateButtonClicked()
    {
        string roomName = RoomNameInputField != null ? RoomNameInputField.text : "";

        if (string.IsNullOrEmpty(roomName))
        {
            Debug.LogWarning("방 이름을 입력해주세요.");
            return;
        }

        if (roomName.Length > 20)
        {
            Debug.LogWarning("방 이름은 20자 이하여야 합니다.");
            return;
        }

        // 방 생성 요청 전송
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.SendCreateRoomRequest(roomName);

            // 더미 모드일 경우 즉시 처리
            if (NetworkManager.Instance.IS_DUMMY_MODE)
            {
                NetworkManager.Instance.ForceProcessCreateRoomDummy(roomName);
            }
        }

        // 패널 닫기
        if (CreateRoomPanel != null)
        {
            CreateRoomPanel.SetActive(false);
        }
    }

    private void OnCancelCreateButtonClicked()
    {
        if (CreateRoomPanel != null)
        {
            CreateRoomPanel.SetActive(false);
        }
    }

    private void OnRefreshButtonClicked()
    {
        Debug.Log("[LobbyUI] 방 목록 새로고침 요청");
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.SendRoomListRequest();
        }
        else
        {
            Debug.LogError("[LobbyUI] NetworkManager.Instance가 null입니다.");
        }
    }

    // LobbyManager.UpdateRoomList()에서 최종적으로 호출되는 함수
    public void RefreshRoomList(List<RoomData> rooms)
    {
        Debug.Log($"[LobbyUI] UI 갱신 요청: 방 {rooms.Count}개");

        // 1. 기존 슬롯 제거
        ClearExistingSlots();

        // 2. 새로운 슬롯 동적 생성 및 배치
        foreach (var room in rooms)
        {
            // 프리팹 복제
            GameObject newSlotObj = Instantiate(RoomSlotPrefab, ContentParent);

            // RoomSlot 컴포넌트 가져오기
            RoomSlot slot = newSlotObj.GetComponent<RoomSlot>();

            if (slot != null)
            {
                // 데이터 주입 및 UI 갱신
                slot.SetData(room);
            }
        }
    }
    private void ClearExistingSlots()
    {
        // ContentParent의 모든 자식 오브젝트를 파괴합니다.
        foreach (Transform child in ContentParent)
        {
            Destroy(child.gameObject);
        }
    }
    
    private void Update()
    {
        // [핵심 수정] 주기적으로 방 목록 자동 새로고침 (유저가 나간 것을 반영)
        // 단, 방 목록 응답을 기다리는 중이면 새로고침하지 않음 (중복 요청 방지)
        if (Time.time - _lastRefreshTime >= autoRefreshInterval)
        {
            _lastRefreshTime = Time.time;
            
            // 서버에 연결되어 있고, 로비 씬에 있을 때만 새로고침
            if (NetworkManager.Instance != null && NetworkManager.Instance.IsConnected())
            {
                // [핵심 수정] 방 목록 응답을 기다리는 중이 아니면 새로고침
                // NetworkManager에 _waitingForRoomListResponse가 private이므로,
                // 여기서는 항상 새로고침하되, NetworkManager에서 중복 요청을 방지하도록 함
                Debug.Log("[LobbyUI] 자동 방 목록 새로고침");
                NetworkManager.Instance.SendRoomListRequest();
            }
        }
    }
}
