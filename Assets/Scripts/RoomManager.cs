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
    
    // 접근자 프로퍼티
    public int CurrentRoomID => _currentRoomID;
    public string CurrentRoomName => _currentRoomName;
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
            roomUI = FindFirstObjectByType<RoomUI>();
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
            else
            {
                // 실제 모드: 방 정보 요청 또는 기존 정보 사용
                StartCoroutine(InitializeRoomInfo());
            }
        }
    }

    private System.Collections.IEnumerator InitializeRoomInfo()
    {
        // RoomManager가 초기화될 때까지 대기
        yield return new WaitForSeconds(0.2f);
        
        if (NetworkManager.Instance == null) yield break;
        
        // 방 정보가 없으면 기본값으로 설정
        if (CurrentRoomID == -1 && NetworkManager.Instance.PendingRoomID != -1)
        {
            int pendingRoomID = NetworkManager.Instance.PendingRoomID;
            SetCurrentRoomID(pendingRoomID);
            
            // 방 이름이 없으면 기본값 사용 (실제 방 이름은 서버에서 받아야 함)
            // PendingRoomName이 있으면 사용, 없으면 기본값
            if (string.IsNullOrEmpty(_currentRoomName))
            {
                string pendingRoomName = NetworkManager.Instance.PendingRoomName;
                if (!string.IsNullOrEmpty(pendingRoomName))
                {
                    _currentRoomName = pendingRoomName;
                    Debug.Log($"[RoomManager] PendingRoomName에서 방 이름 가져옴: {_currentRoomName}");
                }
                else
                {
                    _currentRoomName = $"Room #{pendingRoomID}";
                    Debug.LogWarning($"[RoomManager] PendingRoomName이 비어있어서 기본값 사용: {_currentRoomName}");
                    // 방 목록을 요청해서 실제 방 이름을 가져오기
                    if (NetworkManager.Instance != null)
                    {
                        NetworkManager.Instance.SendRoomListRequest();
                        Debug.Log("[RoomManager] 방 목록 요청 전송 (방 이름 가져오기 위해)");
                    }
                }
            }
            
            // 플레이어 수와 최대 인원 설정
            _currentPlayerCount = _playerList.Count;
            _maxPlayers = 5; // 기본 최대 인원
            
            // roomUI가 null이면 다시 찾기
            if (roomUI == null)
            {
                roomUI = FindFirstObjectByType<RoomUI>();
                if (roomUI != null)
                {
                    Debug.Log("[RoomManager] InitializeRoomInfo에서 roomUI를 찾았습니다.");
                }
            }
            
            // [핵심 수정] 방장이 방을 생성한 경우, 자신을 플레이어 목록에 추가
            // ConnectedUserID가 설정될 때까지 대기 후 추가 시도
            if (_isHost && NetworkManager.Instance != null)
            {
                StartCoroutine(AddHostToPlayerListDelayed());
            }
            
            // UI 업데이트 (플레이어 목록 추가 후)
            if (roomUI != null)
            {
                Debug.Log($"[RoomManager] InitializeRoomInfo - UI 업데이트: ID={pendingRoomID}, Name={_currentRoomName}, Players={_currentPlayerCount}/{_maxPlayers}");
                roomUI.UpdateRoomInfo(pendingRoomID, _currentRoomName, _currentPlayerCount, _maxPlayers);
                
                // 플레이어 목록도 UI에 업데이트
                if (_playerList.Count > 0)
                {
                    roomUI.UpdatePlayerList(_playerList, _isLocalPlayerReady);
                }
            }
            else
            {
                Debug.LogError("[RoomManager] InitializeRoomInfo - roomUI가 null입니다! RoomScene에 RoomUI 컴포넌트가 있는지 확인하세요.");
            }
        }
    }

    /// <summary>
    /// 방장 자신을 플레이어 목록에 추가하는 코루틴 (지연 실행)
    /// ConnectedUserID가 설정될 때까지 대기
    /// </summary>
    private System.Collections.IEnumerator AddHostToPlayerListDelayed()
    {
        // ConnectedUserID와 ConnectedUserName이 설정될 때까지 대기 (최대 2초)
        float timeout = 2f;
        float elapsed = 0f;
        while ((NetworkManager.Instance.ConnectedUserID == -1 || string.IsNullOrEmpty(NetworkManager.Instance.ConnectedUserName)) && elapsed < timeout)
        {
            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;
        }
        
        if (NetworkManager.Instance == null) yield break;
        
        int myUserID = NetworkManager.Instance.ConnectedUserID;
        string myUserName = NetworkManager.Instance.ConnectedUserName;
        
        if (myUserID != -1 && !string.IsNullOrEmpty(myUserName))
        {
            // 이미 추가되어 있는지 확인
            bool exists = false;
            foreach (var player in _playerList)
            {
                if (player.PlayerID == myUserID)
                {
                    exists = true;
                    break;
                }
            }
            
            if (!exists)
            {
                Debug.Log($"[RoomManager] AddHostToPlayerListDelayed - 방장 자신을 플레이어 목록에 추가: UserID={myUserID}, UserName={myUserName}");
                _playerList.Add(new PlayerReadyData(myUserID, myUserName, false));
                _currentPlayerCount = _playerList.Count; // 플레이어 수 업데이트
                
                // UI 업데이트
                if (roomUI != null)
                {
                    roomUI.UpdatePlayerList(_playerList, _isLocalPlayerReady);
                }
            }
            else
            {
                Debug.Log($"[RoomManager] AddHostToPlayerListDelayed - 방장 자신이 이미 플레이어 목록에 있습니다. UserID={myUserID}");
            }
        }
        else
        {
            Debug.LogWarning($"[RoomManager] AddHostToPlayerListDelayed - 방장 정보가 불완전합니다. UserID={myUserID}, UserName={myUserName}");
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
        
        // [핵심 수정] roomID가 -1이면 방을 나간 것으로 간주하고 초기화
        if (roomID == -1)
        {
            Debug.Log("[RoomManager] 방 나가기 완료. 방 정보 초기화");
            _currentRoomName = "";
            _currentPlayerCount = 0;
            _maxPlayers = 0;
            _playerList.Clear();
            _isLocalPlayerReady = false;
            _isHost = false;
            return;
        }
        
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

        // roomUI가 null이면 다시 찾기
        if (roomUI == null)
        {
            roomUI = FindFirstObjectByType<RoomUI>();
            if (roomUI != null)
            {
                Debug.Log("[RoomManager] UpdateRoomInfo에서 roomUI를 찾았습니다.");
            }
        }

        if (roomUI != null)
        {
            Debug.Log($"[RoomManager] UpdateRoomInfo - UI 업데이트 호출: ID={roomID}, Name={roomName}, Players={playerCount}/{maxPlayers}");
            roomUI.UpdateRoomInfo(roomID, roomName, playerCount, maxPlayers);
        }
        else
        {
            Debug.LogError("[RoomManager] UpdateRoomInfo - roomUI가 null입니다! RoomScene에 RoomUI 컴포넌트가 있는지 확인하세요.");
        }
    }

    public void UpdatePlayerList(List<PlayerReadyData> players)
    {
        // 변경 감지: 플레이어 목록이 실제로 변경되었는지 확인
        bool listChanged = HasPlayerListChanged(players);
        
        _playerList = players;

        Debug.Log($"[RoomManager] 플레이어 목록 업데이트: {players.Count}명");

        // 로컬 플레이어의 Ready 상태 확인
        string localUserName = NetworkManager.Instance?.ConnectedUserName ?? "Player1";
        bool previousReadyState = _isLocalPlayerReady;
        _isLocalPlayerReady = false;
        foreach (var player in players)
        {
            if (player.PlayerName == localUserName)
            {
                _isLocalPlayerReady = player.IsReady;
                break;
            }
        }

        // UI 업데이트는 목록이 변경되었거나 Ready 상태가 변경된 경우에만
        if (listChanged || previousReadyState != _isLocalPlayerReady)
        {
            if (roomUI != null)
            {
                roomUI.UpdatePlayerList(players, _isLocalPlayerReady);
            }
        }

        // 모든 플레이어가 Ready인지 확인
        CheckAllPlayersReady(players);
    }

    private bool HasPlayerListChanged(List<PlayerReadyData> newPlayers)
    {
        // 플레이어 수가 다르면 변경됨
        if (_playerList.Count != newPlayers.Count)
        {
            return true;
        }

        // 플레이어 ID나 Ready 상태가 다르면 변경됨
        for (int i = 0; i < newPlayers.Count; i++)
        {
            bool found = false;
            foreach (var oldPlayer in _playerList)
            {
                if (oldPlayer.PlayerID == newPlayers[i].PlayerID)
                {
                    found = true;
                    // Ready 상태가 변경되었는지 확인
                    if (oldPlayer.IsReady != newPlayers[i].IsReady)
                    {
                        return true;
                    }
                    break;
                }
            }
            // 새로운 플레이어가 추가되었는지 확인
            if (!found)
            {
                return true;
            }
        }

        return false;
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
                    roomUI = FindFirstObjectByType<RoomUI>();
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

    public List<PlayerReadyData> GetPlayerList()
    {
        return new List<PlayerReadyData>(_playerList);
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

