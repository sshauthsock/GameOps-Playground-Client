
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

        // 1. NetworkManager를 통해 서버에 방 입장 요청 패킷(ID 300) 전송
        NetworkManager.Instance.SendJoinRoomRequest(_data.RoomID);

        // 더미 패킷 주입 조건을 강제 실행으로 변경합니다.
        // (서버가 없으므로 무조건 더미 응답을 주입해야 합니다.)
        if (true) // 테스트를 위해 항상 실행되도록 강제 변경
        {
            byte[] dummyJoinAns = NetworkManager.Instance.MakeDummyJoinRoomSuccessPacket(_data.RoomID);

            lock (NetworkManager.Instance._packetQueue)
            {
                NetworkManager.Instance._packetQueue.Enqueue(dummyJoinAns);
            }
            Debug.Log($"ID 301 (Join Room Ans, RoomID: {_data.RoomID}) 더미 패킷 주입 완료.");

            // 패킷 처리 강제 실행 (즉시 응답 확인)
            NetworkManager.Instance.ForceProcessPackets();
        }
        // 실제 서버 환경이라면 if (!NetworkManager.Instance.IsConnected()) 조건문을 유지해야 합니다.
    }
}