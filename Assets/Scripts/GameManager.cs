using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }
    // 주석 처리된 더미 모드 관련 필드들 (필요시 사용)
    // private bool _isGameStarted = false;
    // private float _lastDummyMoveTime = 0f;
    // private const float DUMMY_MOVE_INTERVAL = 0.1f; // 0.1초마다 이동 시도
    // private Vector3 _dummyTargetPos = new Vector3(5, 0, 0); // RemotePlayer_A의 시작 위치
    private void Awake()
    {
        // GameManager 싱글톤 설정
        if (Instance == null)
        {
            Instance = this;
            Debug.Log("[GameManager] Awake: Instance 할당 완료.");
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
        }
    }
    void Update()
    {
        // if (!NetworkManager.Instance.IS_DUMMY_MODE || !_isGameStarted)
        //     return;

        // if (Time.time > _lastDummyMoveTime + DUMMY_MOVE_INTERVAL)
        // {
        //     //  Z축을 2f로 설정하여 중앙(Z=0)에 있는 내 캐릭터와 충돌을 방지합니다.
        //     float xPos = Mathf.Sin(Time.time) * 5f;
        //     _dummyTargetPos = new Vector3(xPos, 0, 2f);

        //     //  회전값은 identity(0,0,0,1)로 고정해서 보냅니다.
        //     NetworkManager.Instance.ForceProcessMoveDummy(
        //         playerID: 1000,
        //         position: _dummyTargetPos,
        //         rotation: Quaternion.identity
        //     );
        //     _lastDummyMoveTime = Time.time;
        // }
    }
    void Start()
    {
        // 게임 씬이 로드되면 NetworkManager의 OnSceneLoaded에서 
        // WaitForGameManagerAndStartGame()을 통해 StartGameLogic()이 호출됩니다.
        // 따라서 여기서는 특별한 작업이 필요 없습니다.
        Debug.Log("[GameManager] Start() 호출됨. NetworkManager에서 StartGameLogic() 호출 대기 중...");
    }

    // NetworkManager가 호출할 게임 시작 함수
    public void StartGameLogic()
    {
        PlayerManager.Instance.ClearAllPlayers();

        // NetworkManager에서 플레이어 목록 가져오기
        if (NetworkManager.Instance != null && NetworkManager.Instance._gamePlayerList != null)
        {
            var playerList = NetworkManager.Instance._gamePlayerList;
            Debug.Log($"[GameManager] 게임 시작 - 플레이어 {playerList.Count}명 생성 시작");

            // 플레이어를 방장/일반 유저 순서로 정렬 (방장이 첫 번째)
            var sortedPlayers = new List<PlayerInfo>(playerList);
            int myUserID = NetworkManager.Instance.ConnectedUserID;
            int createdRoomID = NetworkManager.Instance.CreatedRoomID;
            
            Debug.Log($"[GameManager] 플레이어 정렬 시작 - myUserID: {myUserID}, createdRoomID: {createdRoomID}, playerList.Count: {playerList.Count}");
            
            // 방장 찾기: CreatedRoomID != -1인 클라이언트가 방장
            // 즉, 내가 방을 생성했다면(myUserID가 방장), 내 UserID를 가진 플레이어가 방장
            int hostUserID = -1;
            if (createdRoomID != -1 && myUserID != -1)
            {
                // 내가 방을 생성했다면 내가 방장
                var hostPlayer = sortedPlayers.Find(p => p.UserID == myUserID);
                if (hostPlayer != null)
                {
                    hostUserID = myUserID;
                    sortedPlayers.Remove(hostPlayer);
                    sortedPlayers.Insert(0, hostPlayer);
                    Debug.Log($"[GameManager] ✅ 방장 찾음: UserID={hostPlayer.UserID}, Name={hostPlayer.UserName} (내가 방 생성)");
                }
                else
                {
                    Debug.LogWarning($"[GameManager] ⚠️ 내 UserID({myUserID})를 플레이어 목록에서 찾을 수 없습니다.");
                    Debug.LogWarning($"[GameManager] 플레이어 목록:");
                    foreach (var p in sortedPlayers)
                    {
                        Debug.LogWarning($"[GameManager]   - UserID: {p.UserID}, UserName: {p.UserName}");
                    }
                }
            }
            
            // 내가 방장이 아니면, 첫 번째 플레이어를 방장으로 가정 (서버에서 방장 정보를 받지 못한 경우)
            if (hostUserID == -1 && sortedPlayers.Count > 0)
            {
                Debug.LogWarning($"[GameManager] ⚠️ 방장을 찾을 수 없습니다. 첫 번째 플레이어(UserID={sortedPlayers[0].UserID})를 방장으로 설정합니다.");
            }
            
            Debug.Log($"[GameManager] 정렬된 플레이어 목록:");
            for (int i = 0; i < sortedPlayers.Count; i++)
            {
                Debug.Log($"[GameManager]   [{i}] UserID: {sortedPlayers[i].UserID}, UserName: {sortedPlayers[i].UserName}");
            }

            // 각 플레이어를 다른 위치에 생성
            // 방장: 왼쪽(-5, 0, 0), 두 번째 유저: 오른쪽(5, 0, 0)
            for (int i = 0; i < sortedPlayers.Count; i++)
            {
                var playerInfo = sortedPlayers[i];
                Vector3 spawnPosition;
                
                if (i == 0)
                {
                    // 방장: 왼쪽
                    spawnPosition = new Vector3(-5f, 0, 0);
                }
                else
                {
                    // 두 번째 유저: 오른쪽
                    spawnPosition = new Vector3(5f, 0, 0);
                }
                
                PlayerManager.Instance.AddPlayer(
                    playerID: playerInfo.UserID,
                    userName: playerInfo.UserName,
                    position: spawnPosition
                );
                
                Debug.Log($"[GameManager] 플레이어 생성: ID={playerInfo.UserID}, Name={playerInfo.UserName}, Pos={spawnPosition}, IsHost={i == 0}");
            }
        }
        else
        {
            Debug.LogError("[GameManager] 플레이어 목록을 가져올 수 없습니다!");
            
            // 폴백: 기존 로직 사용
            if (PlayerManager.Instance.MyPlayerID != -1)
            {
                PlayerManager.Instance.AddPlayer(
                    playerID: PlayerManager.Instance.MyPlayerID,
                    userName: NetworkManager.Instance.ConnectedUserName,
                    position: Vector3.zero
                );
            }
        }

        // _isGameStarted = true; // 더미 모드에서만 사용 (현재 주석 처리됨)
        Debug.Log("🎉 Game Start Logic 완료 - 이제부터 동기화 시작");

        // MyPlayerID가 설정되었는지 확인하고, 플레이어들의 isLocalPlayer 상태 업데이트
        if (PlayerManager.Instance != null && NetworkManager.Instance != null)
        {
            Debug.Log($"[GameManager] 게임 시작 전 MyPlayerID 확인: {PlayerManager.Instance.MyPlayerID}, ConnectedUserID: {NetworkManager.Instance.ConnectedUserID}, CreatedRoomID: {NetworkManager.Instance.CreatedRoomID}");
            
            // CreatedRoomID를 기반으로 올바른 ID를 결정
            int correctUserID = -1;
            if (NetworkManager.Instance._gamePlayerList != null && NetworkManager.Instance._gamePlayerList.Count > 0)
            {
                // 방장인 경우: 첫 번째 플레이어를 자신의 것으로 설정
                if (NetworkManager.Instance.CreatedRoomID != -1)
                {
                    correctUserID = NetworkManager.Instance._gamePlayerList[0].UserID;
                    Debug.Log($"[GameManager] 방장이므로 첫 번째 플레이어를 자신의 것으로 설정: UserID={correctUserID}, UserName='{NetworkManager.Instance._gamePlayerList[0].UserName}'");
                }
                // 일반 유저인 경우: 두 번째 플레이어를 자신의 것으로 설정
                else if (NetworkManager.Instance._gamePlayerList.Count >= 2)
                {
                    correctUserID = NetworkManager.Instance._gamePlayerList[1].UserID;
                    Debug.Log($"[GameManager] 일반 유저이므로 두 번째 플레이어를 자신의 것으로 설정: UserID={correctUserID}, UserName='{NetworkManager.Instance._gamePlayerList[1].UserName}'");
                }
                // 플레이어가 1명만 있는 경우: 그 플레이어를 자신의 것으로 설정
                else if (NetworkManager.Instance._gamePlayerList.Count == 1)
                {
                    correctUserID = NetworkManager.Instance._gamePlayerList[0].UserID;
                    Debug.Log($"[GameManager] 플레이어가 1명만 있으므로 첫 번째 플레이어를 자신의 것으로 설정: UserID={correctUserID}, UserName='{NetworkManager.Instance._gamePlayerList[0].UserName}'");
                }
            }
            
            // 올바른 ID가 결정되었고, 현재 MyPlayerID와 다르면 수정
            if (correctUserID != -1)
            {
                if (PlayerManager.Instance.MyPlayerID != correctUserID)
                {
                    Debug.LogWarning($"[GameManager] ⚠️ MyPlayerID({PlayerManager.Instance.MyPlayerID})가 올바르지 않습니다. 올바른 ID({correctUserID})로 수정합니다.");
                    PlayerManager.Instance.SetMyPlayerID(correctUserID);
                    NetworkManager.Instance.ConnectedUserID = correctUserID;
                    Debug.Log($"[GameManager] ✅ MyPlayerID를 올바른 값으로 수정: {correctUserID}");
                }
                else
                {
                    Debug.Log($"[GameManager] ✅ MyPlayerID가 이미 올바릅니다: {correctUserID}");
                }
            }
            else if (PlayerManager.Instance.MyPlayerID == -1)
            {
                // MyPlayerID가 -1이고 올바른 ID를 찾지 못한 경우
                Debug.LogError($"[GameManager] ❌ 플레이어 목록에서 자신을 찾지 못함. CreatedRoomID: {NetworkManager.Instance.CreatedRoomID}, 플레이어 수: {NetworkManager.Instance._gamePlayerList?.Count ?? 0}");
                if (NetworkManager.Instance._gamePlayerList != null)
                {
                    Debug.LogError($"[GameManager] 플레이어 목록:");
                    foreach (var playerInfo in NetworkManager.Instance._gamePlayerList)
                    {
                        Debug.LogError($"[GameManager]   - UserID: {playerInfo.UserID}, UserName: '{playerInfo.UserName}'");
                    }
                }
            }
            
            // MyPlayerID가 설정되었는지 최종 확인
            if (PlayerManager.Instance.MyPlayerID == -1)
            {
                Debug.LogError($"[GameManager] ❌❌❌ MyPlayerID가 여전히 -1입니다! isLocalPlayer가 제대로 설정되지 않을 수 있습니다!");
            }
            
            Debug.Log($"[GameManager] 최종 MyPlayerID: {PlayerManager.Instance.MyPlayerID}, ConnectedUserID: {NetworkManager.Instance.ConnectedUserID}");
            
            // isLocalPlayer 상태 업데이트 (MyPlayerID가 설정된 후에만 호출)
            if (PlayerManager.Instance.MyPlayerID != -1)
            {
                PlayerManager.Instance.UpdatePlayerLocalStatus();
            }
            else
            {
                Debug.LogError($"[GameManager] ❌ MyPlayerID가 -1이므로 UpdatePlayerLocalStatus()를 호출하지 않습니다!");
            }
        }

        // 첫 턴 플레이어에게 컨트롤 권한 부여
        // ID 430을 기다리지 않고, ProcessGameStartNotify에서 설정된 _currentTurnPlayerID를 사용
        if (NetworkManager.Instance != null)
        {
            int firstTurnPlayerID = NetworkManager.Instance.GetCurrentTurnPlayerID();
            int myUserID = NetworkManager.Instance.ConnectedUserID;
            int myPlayerID = PlayerManager.Instance != null ? PlayerManager.Instance.MyPlayerID : -1;
            
            Debug.Log($"[GameManager] 첫 턴 플레이어: {firstTurnPlayerID}, 내 UserID: {myUserID}, MyPlayerID: {myPlayerID}");

            // 모든 플레이어에게 턴 정보 업데이트
            foreach (var playerInfo in NetworkManager.Instance._gamePlayerList)
            {
                var playerObj = PlayerManager.Instance.GetPlayerById(playerInfo.UserID);
                if (playerObj != null)
                {
                    var controller = playerObj.GetComponent<PlayerController>();
                    if (controller != null)
                    {
                        // 자기 턴이고 로컬 플레이어인 경우에만 컨트롤 가능
                        bool isMyTurn = (playerInfo.UserID == firstTurnPlayerID && playerInfo.UserID == myUserID);
                        controller.SetCanControl(isMyTurn);
                        Debug.Log($"[GameManager] Player {playerInfo.UserID} - isLocalPlayer: {controller.isLocalPlayer}, isMyTurn: {isMyTurn}, 컨트롤 권한: {isMyTurn && controller.isLocalPlayer}");
                    }
                }
            }
            
            // ID 430을 기다리는 동안 초기 턴 설정이 완료되었음을 로그로 표시
            Debug.Log($"[GameManager] 초기 턴 설정 완료. ID 430 (Turn Start Notify) 수신 시 추가 업데이트됩니다.");
            
            // 플레이어 생성이 완료되었으므로, 보류된 턴 정보가 있으면 적용
            if (NetworkManager.Instance != null)
            {
                NetworkManager.Instance.ApplyPendingTurnControl();
            }
        }
    }

    private IEnumerator WaitForGameUIAndUpdate()
    {
        // GameUI가 생성될 때까지 대기
        float timeout = 2f;
        float elapsed = 0f;
        while (GameUI.Instance == null && elapsed < timeout)
        {
            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;
        }

        if (GameUI.Instance != null)
        {
            GameUI.Instance.UpdateTurnInfo();
            Debug.Log("[GameManager] GameUI 초기화 완료");
        }
        else
        {
            Debug.LogWarning("[GameManager] GameUI를 찾을 수 없습니다. GameScene에 GameUI 컴포넌트가 있는지 확인하세요.");
        }
    }
}