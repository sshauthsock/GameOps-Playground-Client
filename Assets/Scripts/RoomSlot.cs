
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class RoomSlot : MonoBehaviour
{
    // Unity Inspector에서 할당할 UI 요소들
    public TextMeshProUGUI RoomNameText; // 방 이름
    public TextMeshProUGUI UserCountText; // 현재 접속 인원
    public Button JoinButton; // 방 입장 버튼

    private RoomData _data;

    // NetworkManager로부터 받은 RoomData를 기반으로 UI를 갱신하는 함수
    public void SetData(RoomData data)
    {
        _data = data;

        // 1. UI 텍스트 갱신
        if (RoomNameText != null)
        {
            RoomNameText.text = data.RoomName;
        }
        if (UserCountText != null)
        {
            // 인원수 표시 예: "1/5"
            UserCountText.text = $"{data.CurrentUserCount}/5";
        }

        // 2. 버튼 클릭 이벤트 리스너 설정 (향후 방 입장 로직 추가)
        if (JoinButton != null)
        {
            JoinButton.onClick.RemoveAllListeners();
            JoinButton.onClick.AddListener(OnJoinRoomClicked);
        }
    }

    private void OnJoinRoomClicked()
    {
        Debug.Log($"방 입장 요청: ID {_data.RoomID}, 이름: {_data.RoomName}");

    }
}