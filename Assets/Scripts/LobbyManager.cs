// LobbyManager.cs 파일 생성
using System.Collections.Generic;
using UnityEngine;

public class LobbyManager : MonoBehaviour
{
    // 1. 싱글톤 인스턴스 (로비 씬 내에서 유일)
    public static LobbyManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject); // 로비 씬에 인스턴스가 두 개 생성될 경우 파괴
        }
    }

    // 2. NetworkManager로부터 방 목록을 받을 함수 정의 (Skeleton)
    public void UpdateRoomList(List<RoomData> rooms)
    {
        Debug.Log($"[LobbyManager] 서버로부터 총 {rooms.Count}개의 방 목록을 수신했습니다.");

        foreach (var room in rooms)
        {
            Debug.Log($"   - 방 ID: {room.RoomID}, 이름: {room.RoomName}, 현재 유저: {room.CurrentUserCount}");
        }
    }

}