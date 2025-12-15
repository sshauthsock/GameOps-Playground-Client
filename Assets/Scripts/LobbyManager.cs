// LobbyManager.cs
using System.Collections.Generic;
using UnityEngine;

public class LobbyManager : MonoBehaviour
{
    public static LobbyManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            Debug.Log("[LobbyManager] Awake()에서 Instance 할당 완료 (표준 싱글톤).");
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
        }
    }

    // 2. NetworkManager로부터 방 목록을 받을 함수 정의 (핵심 역할)
    public void UpdateRoomList(List<RoomData> rooms)
    {
        Debug.Log($"[LobbyManager] 서버로부터 총 {rooms.Count}개의 방 목록을 수신했습니다.");

        foreach (var room in rooms)
        {
            Debug.Log($"   - 방 ID: {room.RoomID}, 이름: {room.RoomName}, 현재 유저: {room.CurrentUserCount}");
        }
    }
}