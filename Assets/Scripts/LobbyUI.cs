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
}
