using System.Collections.Generic;
using UnityEngine;

public class LobbyUI : MonoBehaviour
{
    // Unity Inspector에서 할당할 요소들
    public GameObject RoomSlotPrefab;

    public Transform ContentParent;

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