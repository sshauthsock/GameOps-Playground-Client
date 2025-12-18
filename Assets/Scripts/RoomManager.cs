using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class RoomManager : MonoBehaviour
{
    public static RoomManager Instance { get; private set; }
    public RoomUI roomUI;

    private int _currentRoomID;
    private string _currentRoomName;
    private int _currentPlayerCount;
    private int _maxPlayers;
    private List<PlayerReadyData> _playerList = new List<PlayerReadyData>();
    private bool _isLocalPlayerReady = false;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            Debug.Log("[RoomManager] Awake()에서 Instance 할당 완료 (표준 싱글톤).");
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        // RoomScene 로드 시 방 정보 요청
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.SendRoomInfoRequest();
            NetworkManager.Instance.SendPlayerReadyRequest();

            // 더미 모드일 경우 더미 데이터 주입
            if (NetworkManager.Instance.IS_DUMMY_MODE)
            {
                // 더미 방 정보 주입
                StartCoroutine(DelayedDummyData());
            }
        }
    }

    private System.Collections.IEnumerator DelayedDummyData()
    {
        yield return new WaitForSeconds(0.1f);
        
        // 더미 방 정보
        int dummyRoomID = 1001;
        string dummyRoomName = "Test Room";
        int dummyPlayerCount = 1;
        int dummyMaxPlayers = 5;

        NetworkManager.Instance.ForceProcessRoomInfoDummy(dummyRoomID, dummyRoomName, dummyPlayerCount, dummyMaxPlayers);

        // 더미 플레이어 목록
        List<PlayerReadyData> dummyPlayers = new List<PlayerReadyData>();
        dummyPlayers.Add(new PlayerReadyData(1, NetworkManager.Instance.ConnectedUserName ?? "Player1", false));
        NetworkManager.Instance.ForceProcessPlayerReadyDummy(dummyPlayers);
    }

    public void UpdateRoomInfo(int roomID, string roomName, int playerCount, int maxPlayers)
    {
        _currentRoomID = roomID;
        _currentRoomName = roomName;
        _currentPlayerCount = playerCount;
        _maxPlayers = maxPlayers;

        Debug.Log($"[RoomManager] 방 정보 업데이트: ID={roomID}, Name={roomName}, Players={playerCount}/{maxPlayers}");

        if (roomUI != null)
        {
            roomUI.UpdateRoomInfo(roomID, roomName, playerCount, maxPlayers);
        }
    }

    public void UpdatePlayerList(List<PlayerReadyData> players)
    {
        _playerList = players;

        Debug.Log($"[RoomManager] 플레이어 목록 업데이트: {players.Count}명");

        // 로컬 플레이어의 Ready 상태 확인
        string localUserName = NetworkManager.Instance?.ConnectedUserName ?? "Player1";
        _isLocalPlayerReady = false;
        foreach (var player in players)
        {
            if (player.PlayerName == localUserName)
            {
                _isLocalPlayerReady = player.IsReady;
                break;
            }
        }

        if (roomUI != null)
        {
            roomUI.UpdatePlayerList(players, _isLocalPlayerReady);
        }

        // 모든 플레이어가 Ready인지 확인
        CheckAllPlayersReady(players);
    }

    private void CheckAllPlayersReady(List<PlayerReadyData> players)
    {
        if (players.Count < 2) return; // 최소 2명 필요

        bool allReady = true;
        foreach (var player in players)
        {
            if (!player.IsReady)
            {
                allReady = false;
                break;
            }
        }

        if (allReady && roomUI != null)
        {
            roomUI.OnAllPlayersReady();
        }
    }

    public void OnReadyButtonClicked()
    {
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.SendGameReadyRequest();
            Debug.Log("[RoomManager] Ready 요청 전송");

            // 더미 모드일 경우 즉시 처리
            if (NetworkManager.Instance.IS_DUMMY_MODE)
            {
                StartCoroutine(ProcessDummyReady());
            }
        }
    }

    private System.Collections.IEnumerator ProcessDummyReady()
    {
        yield return new WaitForSeconds(0.1f);

        // 로컬 플레이어를 Ready 상태로 변경
        string localUserName = NetworkManager.Instance?.ConnectedUserName ?? "Player1";
        List<PlayerReadyData> updatedPlayers = new List<PlayerReadyData>();

        foreach (var player in _playerList)
        {
            if (player.PlayerName == localUserName)
            {
                updatedPlayers.Add(new PlayerReadyData(player.PlayerID, player.PlayerName, true));
            }
            else
            {
                updatedPlayers.Add(player);
            }
        }

        NetworkManager.Instance.ForceProcessPlayerReadyDummy(updatedPlayers);
    }

    public void OnLeaveRoomButtonClicked()
    {
        Debug.Log("[RoomManager] 방 나가기");
        SceneManager.LoadScene(1); // LobbyScene으로 이동
    }

    public void StartGame()
    {
        Debug.Log("[RoomManager] 게임 시작!");
        SceneManager.LoadScene(2); // GameScene으로 이동
    }
}

