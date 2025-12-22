
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

        // 1. 서버에 방 입장 요청 (ID 300) 전송 시도
        NetworkManager.Instance.SendJoinRoomRequest(_data.RoomID);

        //  더미 모드일 경우, NetworkManager에게 ID 301 응답을 처리하도록 요청
        if (NetworkManager.Instance.IS_DUMMY_MODE)
        {
            //  이 함수가 NetworkManager에 추가되지 않았다면 오류가 발생합니다.
            //    (우리가 ForceProcessGameStartDummy()처럼 추가했어야 합니다.)
            //    일단은 ID 301 처리를 위한 임시 위임 함수를 가정하겠습니다.

            // --- 1단계 임시 조치: ID 301 처리 위임 함수가 필요합니다 ---

            //  하지만 더미 주입 책임은 NetworkManager에 있어야 하므로, 
            //    NetworkManager에 이 함수를 추가해야 합니다.

            // 아래 코드를 NetworkManager.cs에 추가해야 합니다.
            /*
            // NetworkManager.cs (내부 함수)
            internal void ForceProcessJoinRoomDummy(int roomID) 
            {
                if (!IS_DUMMY_MODE) return;
                byte[] dummyAns = MakeDummyJoinRoomSuccessPacket(roomID);
                lock (_packetQueue) { _packetQueue.Enqueue(dummyAns); }
                Debug.Log($"ID 301 더미 주입 완료 (RoomID: {roomID})");
                ForceProcessPackets();
            }
            */

            // --- 2단계: RoomSlot에서 위임 함수 호출 ---
            NetworkManager.Instance.ForceProcessJoinRoomDummy(_data.RoomID);

        }
    }
}