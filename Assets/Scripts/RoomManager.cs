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
    private bool _hasSentGameStartRequest = false; // 게임 시작 요청을 이미 보냈는지 확인
    private bool _isHost = false; // 방장 여부

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
        // RoomScene 로드 시 초기화
        _hasSentGameStartRequest = false; // 씬 로드 시 플래그 리셋
        
        // roomUI가 할당되지 않았으면 자동으로 찾기
        if (roomUI == null)
        {
            roomUI = FindObjectOfType<RoomUI>();
            if (roomUI != null)
            {
                Debug.Log("[RoomManager] roomUI를 자동으로 찾았습니다.");
            }
            else
            {
                Debug.LogError("[RoomManager] roomUI를 찾을 수 없습니다. RoomScene에 RoomUI 컴포넌트가 있는지 확인하세요.");
            }
        }
        
        // 방장 여부는 SetCurrentRoomID에서 확인하므로 여기서는 초기화만
        // Start()는 씬 로드 직후 실행되므로 _currentRoomID가 아직 설정되지 않았을 수 있음
        
        if (NetworkManager.Instance != null)
        {
            // 방 정보는 서버가 UserEnter Notify 등을 통해 자동으로 보내줌
            // Ready 상태는 사용자가 버튼을 눌러야 하므로 여기서 요청하지 않음

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

    public void SetCurrentRoomID(int roomID)
    {
        _currentRoomID = roomID;
        
        // 방장 여부 확인 (방을 생성한 사람이 방장)
        if (NetworkManager.Instance != null)
        {
            int createdRoomID = NetworkManager.Instance.CreatedRoomID;
            _isHost = (createdRoomID != -1 && createdRoomID == roomID);
            
            Debug.Log($"[RoomManager] SetCurrentRoomID - 방장 여부: {_isHost}");
            Debug.Log($"[RoomManager]   - 생성한 방 ID: {createdRoomID} (초기값: -1)");
            Debug.Log($"[RoomManager]   - 현재 방 ID: {roomID}");
            Debug.Log($"[RoomManager]   - 비교 결과: {createdRoomID} == {roomID} = {_isHost}");
            
            // 방장이 아니면 Start Game 버튼 숨기기
            if (roomUI != null && roomUI.StartGameButton != null)
            {
                roomUI.StartGameButton.gameObject.SetActive(_isHost);
                Debug.Log($"[RoomManager] Start Game 버튼 표시: {_isHost}");
            }
            else
            {
                Debug.LogWarning($"[RoomManager] roomUI 또는 StartGameButton이 null입니다. roomUI={roomUI != null}, StartGameButton={roomUI?.StartGameButton != null}");
            }
        }
        else
        {
            Debug.LogError("[RoomManager] NetworkManager.Instance가 null입니다!");
        }
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
        Debug.Log($"[CheckAllPlayersReady] 플레이어 수: {players.Count}, 방 최대 인원: {_maxPlayers}, 현재 인원: {_currentPlayerCount}");
        
        // 최소 2명 필요
        if (players.Count < 2)
        {
            Debug.Log($"[CheckAllPlayersReady] 플레이어가 {players.Count}명이므로 게임 시작 불가 (최소 2명 필요)");
            return;
        }

        bool allReady = true;
        int readyCount = 0;
        foreach (var player in players)
        {
            Debug.Log($"[CheckAllPlayersReady] 플레이어 확인: UserID={player.PlayerID}, IsReady={player.IsReady}");
            if (!player.IsReady)
            {
                allReady = false;
            }
            else
            {
                readyCount++;
            }
        }

        Debug.Log($"[CheckAllPlayersReady] Ready 상태: {readyCount}/{players.Count}명 Ready, AllReady={allReady}");
        Debug.Log($"[CheckAllPlayersReady] 방 정보: 현재 인원={_currentPlayerCount}, 최대 인원={_maxPlayers}, 플레이어 목록={players.Count}명");

        // 최소 2명이 모두 ready인지 확인
        if (allReady && players.Count >= 2)
        {
            Debug.Log($"[RoomManager] ✅ 방에 있는 모든 플레이어({players.Count}명)가 Ready 상태입니다.");
            Debug.Log($"[RoomManager]   - 현재 방 인원: {_currentPlayerCount}명");
            Debug.Log($"[RoomManager]   - 최대 방 인원: {_maxPlayers}명");
            Debug.Log($"[RoomManager]   - Ready한 플레이어: {readyCount}명");
            Debug.Log($"[RoomManager]   - 방장 여부: {_isHost}");
            
            // 모든 플레이어가 ready일 때, 방장만 Start Game 버튼 활성화
            if (_isHost)
            {
                // roomUI가 null이면 다시 찾기
                if (roomUI == null)
                {
                    roomUI = FindObjectOfType<RoomUI>();
                }
                
                if (roomUI != null)
                {
                    Debug.Log("[RoomManager] 방장이므로 Start Game 버튼을 활성화합니다.");
                    roomUI.OnAllPlayersReady();
                }
                else
                {
                    Debug.LogError("[RoomManager] roomUI를 찾을 수 없습니다. RoomScene에 RoomUI 컴포넌트가 있는지 확인하세요.");
                }
            }
            else
            {
                Debug.Log("[RoomManager] 방장이 아니므로 Start Game 버튼을 활성화하지 않습니다.");
            }
        }
        else
        {
            Debug.Log($"[CheckAllPlayersReady] 아직 모든 플레이어가 Ready가 아닙니다. ({readyCount}/{players.Count})");
            
            // 플레이어가 ready가 아니게 되면 Start Game 버튼 비활성화
            if (roomUI != null)
            {
                roomUI.OnNotAllPlayersReady();
            }
            
            // 플레이어가 ready가 아니게 되면 플래그 리셋
            _hasSentGameStartRequest = false;
        }
    }

    public void OnReadyButtonClicked()
    {
        if (NetworkManager.Instance != null)
        {
            // 현재 Ready 상태를 토글하여 전송
            bool newReadyState = !_isLocalPlayerReady;
            NetworkManager.Instance.SendReadyRequest(newReadyState);
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
        Debug.Log("[RoomManager] 방 나가기 요청 전송");
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.SendLeaveRoomRequest();
        }
        // 서버가 ID 316 (LeaveRoom Res)를 보내면 씬 전환이 일어남
    }

    public void StartGame()
    {
        // 게임 시작 요청을 이미 보냈으면 다시 보내지 않음
        if (_hasSentGameStartRequest)
        {
            Debug.Log("[RoomManager] 이미 게임 시작 요청을 전송했습니다. 서버 응답 대기 중...");
            return;
        }

        Debug.Log("[RoomManager] Start Game 버튼 클릭. 게임 시작 요청 전송");
        
        if (NetworkManager.Instance != null)
        {
            _hasSentGameStartRequest = true; // 플래그 설정
            NetworkManager.Instance.SendGameStartRequest();
            
            // 버튼 비활성화 (중복 클릭 방지)
            if (roomUI != null && roomUI.StartGameButton != null)
            {
                roomUI.StartGameButton.interactable = false;
            }
        }
        
        // 서버가 ID 331 (GameStart Notify)를 보내면 씬 전환이 일어남
    }

    public void UpdatePlayerReadyStatus(int userID, bool isReady)
    {
        Debug.Log($"[RoomManager] 플레이어 Ready 상태 업데이트: UserID={userID}, IsReady={isReady}");
        
        // 플레이어 목록에서 해당 사용자 찾아서 업데이트
        bool found = false;
        for (int i = 0; i < _playerList.Count; i++)
        {
            if (_playerList[i].PlayerID == userID)
            {
                _playerList[i] = new PlayerReadyData(_playerList[i].PlayerID, _playerList[i].PlayerName, isReady);
                found = true;
                break;
            }
        }

        // 플레이어를 찾지 못했으면 추가 (UserName은 나중에 업데이트될 수 있음)
        if (!found)
        {
            Debug.LogWarning($"[RoomManager] UserID {userID}를 플레이어 목록에서 찾지 못했습니다. 추가합니다.");
            _playerList.Add(new PlayerReadyData(userID, $"User_{userID}", isReady));
        }

        // 로컬 플레이어인지 확인
        string localUserName = NetworkManager.Instance?.ConnectedUserName ?? "Player1";
        foreach (var player in _playerList)
        {
            if (player.PlayerName == localUserName)
            {
                _isLocalPlayerReady = player.IsReady;
                break;
            }
        }

        if (roomUI != null)
        {
            roomUI.UpdatePlayerList(_playerList, _isLocalPlayerReady);
        }

        // 모든 플레이어가 Ready인지 확인 (항상 호출)
        Debug.Log($"[RoomManager] 현재 플레이어 목록: {_playerList.Count}명");
        foreach (var player in _playerList)
        {
            Debug.Log($"[RoomManager] - UserID: {player.PlayerID}, Name: {player.PlayerName}, Ready: {player.IsReady}");
        }
        CheckAllPlayersReady(_playerList);
    }

    public void OnUserEntered(int userID, string userName)
    {
        Debug.Log($"[RoomManager] 사용자 입장: UserID={userID}, UserName={userName}");
        
        // 플레이어 목록에 추가 (중복 체크)
        bool exists = false;
        foreach (var player in _playerList)
        {
            if (player.PlayerID == userID)
            {
                exists = true;
                break;
            }
        }

        if (!exists)
        {
            _playerList.Add(new PlayerReadyData(userID, userName, false));
            if (roomUI != null)
            {
                roomUI.UpdatePlayerList(_playerList, _isLocalPlayerReady);
            }
        }
    }

    public void OnUserLeft(int leaverID, int newHostID)
    {
        Debug.Log($"[RoomManager] 사용자 퇴장: LeaverID={leaverID}, NewHostID={newHostID}");
        
        // 플레이어 목록에서 제거
        _playerList.RemoveAll(p => p.PlayerID == leaverID);
        
        if (roomUI != null)
        {
            roomUI.UpdatePlayerList(_playerList, _isLocalPlayerReady);
        }
    }
}

