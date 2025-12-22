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

            // [핵심 수정] 모든 클라이언트에서 동일한 순서를 보장하기 위해 UserID로 정렬
            // 이렇게 하면 모든 클라이언트에서 동일한 플레이어 순서, 색상, 위치를 보장할 수 있음
            var sortedPlayers = new List<PlayerInfo>(playerList);
            
            // UserID 오름차순으로 정렬 (모든 클라이언트에서 동일한 순서 보장)
            sortedPlayers.Sort((a, b) => a.UserID.CompareTo(b.UserID));
            
            Debug.Log($"[GameManager] 플레이어 정렬 시작 - playerList.Count: {playerList.Count}");
            Debug.Log($"[GameManager] 정렬된 플레이어 목록 (UserID 오름차순):");
            for (int i = 0; i < sortedPlayers.Count; i++)
            {
                Debug.Log($"[GameManager]   [{i}] UserID: {sortedPlayers[i].UserID}, UserName: {sortedPlayers[i].UserName}");
            }

            // 각 플레이어를 원형으로 배치 (3명 이상 지원)
            // 중심점: (0, 0, 0), 반지름: 5f
            // 방장은 왼쪽(-90도)에서 시작하여 시계 방향으로 배치
            float radius = 5f;
            int playerCount = sortedPlayers.Count;
            
            for (int i = 0; i < playerCount; i++)
            {
                var playerInfo = sortedPlayers[i];
                Vector3 spawnPosition;
                
                if (playerCount == 1)
                {
                    // 플레이어가 1명인 경우: 중앙
                    spawnPosition = Vector3.zero;
                }
                else if (playerCount == 2)
                {
                    // 플레이어가 2명인 경우: 기존 방식 유지 (왼쪽/오른쪽)
                    spawnPosition = (i == 0) ? new Vector3(-5f, 0, 0) : new Vector3(5f, 0, 0);
                }
                else
                {
                    // 플레이어가 3명 이상인 경우: 원형 배치
                    // 각도 계산: -90도(왼쪽)에서 시작하여 시계 방향으로 균등 분배
                    float angleStep = 360f / playerCount;
                    float angle = -90f + (i * angleStep); // -90도에서 시작 (왼쪽)
                    float angleRad = angle * Mathf.Deg2Rad;
                    
                    spawnPosition = new Vector3(
                        radius * Mathf.Cos(angleRad),
                        0f,
                        radius * Mathf.Sin(angleRad)
                    );
                }
                
                PlayerManager.Instance.AddPlayer(
                    playerID: playerInfo.UserID,
                    userName: playerInfo.UserName,
                    position: spawnPosition,
                    totalPlayerCount: playerCount
                );
                
                Debug.Log($"[GameManager] 플레이어 생성: ID={playerInfo.UserID}, Name={playerInfo.UserName}, Pos={spawnPosition}, IsHost={i == 0}, Index={i}/{playerCount}");
            }
        }
        else
        {
            Debug.LogError("[GameManager] 플레이어 목록을 가져올 수 없습니다!");
            
            // 폴백: 기존 로직 사용 (플레이어 목록을 가져올 수 없는 경우, 1명으로 간주)
            if (PlayerManager.Instance.MyPlayerID != -1)
            {
                PlayerManager.Instance.AddPlayer(
                    playerID: PlayerManager.Instance.MyPlayerID,
                    userName: NetworkManager.Instance.ConnectedUserName,
                    position: Vector3.zero,
                    totalPlayerCount: 1
                );
            }
        }

        // _isGameStarted = true; // 더미 모드에서만 사용 (현재 주석 처리됨)
        Debug.Log("🎉 Game Start Logic 완료 - 이제부터 동기화 시작");

        // MyPlayerID를 ConnectedUserID와 동기화 (단일 소스 원칙)
        if (PlayerManager.Instance != null && NetworkManager.Instance != null)
        {
            int connectedUserID = NetworkManager.Instance.ConnectedUserID;
            Debug.Log($"[GameManager] 게임 시작 전 ID 동기화: MyPlayerID={PlayerManager.Instance.MyPlayerID}, ConnectedUserID={connectedUserID}");
            
            // ConnectedUserID가 유효하면 MyPlayerID와 동기화
            if (connectedUserID != -1)
            {
                // ConnectedUserID가 플레이어 목록에 있는지 확인
                bool isValidID = false;
                if (NetworkManager.Instance._gamePlayerList != null)
                {
                    isValidID = NetworkManager.Instance._gamePlayerList.Exists(p => p.UserID == connectedUserID);
                }
                
                if (isValidID)
                {
                    // MyPlayerID를 ConnectedUserID와 동기화
                    if (PlayerManager.Instance.MyPlayerID != connectedUserID)
                    {
                        Debug.Log($"[GameManager] ✅ MyPlayerID를 ConnectedUserID와 동기화: {PlayerManager.Instance.MyPlayerID} -> {connectedUserID}");
                        PlayerManager.Instance.SetMyPlayerID(connectedUserID);
                    }
                    else
                    {
                        Debug.Log($"[GameManager] ✅ MyPlayerID가 이미 ConnectedUserID와 동기화되어 있습니다: {connectedUserID}");
                    }
                }
                else
                {
                    Debug.LogError($"[GameManager] ❌ ConnectedUserID({connectedUserID})가 플레이어 목록에 없습니다!");
                    if (NetworkManager.Instance._gamePlayerList != null)
                    {
                        Debug.LogError($"[GameManager] 플레이어 목록:");
                        foreach (var playerInfo in NetworkManager.Instance._gamePlayerList)
                        {
                            Debug.LogError($"[GameManager]   - UserID: {playerInfo.UserID}, UserName: '{playerInfo.UserName}'");
                        }
                    }
                }
            }
            else
            {
                Debug.LogError($"[GameManager] ❌ ConnectedUserID가 -1입니다! 플레이어 ID를 설정할 수 없습니다.");
            }
            
            // 최종 확인 및 isLocalPlayer 상태 업데이트
            if (PlayerManager.Instance.MyPlayerID != -1)
            {
                Debug.Log($"[GameManager] 최종 ID: MyPlayerID={PlayerManager.Instance.MyPlayerID}, ConnectedUserID={NetworkManager.Instance.ConnectedUserID}");
                PlayerManager.Instance.UpdatePlayerLocalStatus();
            }
            else
            {
                Debug.LogError($"[GameManager] ❌ MyPlayerID가 -1입니다! isLocalPlayer가 제대로 설정되지 않을 수 있습니다!");
            }
        }

        // 첫 턴 플레이어에게 컨트롤 권한 부여
        // ID 430을 기다리지 않고, ProcessGameStartNotify에서 설정된 _currentTurnPlayerID를 사용
        if (NetworkManager.Instance != null && PlayerManager.Instance != null)
        {
            int firstTurnPlayerID = NetworkManager.Instance.GetCurrentTurnPlayerID();
            int myUserID = NetworkManager.Instance.ConnectedUserID;
            int myPlayerID = PlayerManager.Instance.MyPlayerID;
            
            // [핵심 수정] firstTurnPlayerID가 -1이면 플레이어 목록의 첫 번째 플레이어를 첫 턴으로 설정
            if (firstTurnPlayerID == -1 && NetworkManager.Instance._gamePlayerList != null && NetworkManager.Instance._gamePlayerList.Count > 0)
            {
                // 플레이어 목록을 UserID로 정렬하여 첫 번째 플레이어 선택
                var sortedPlayers = new List<PlayerInfo>(NetworkManager.Instance._gamePlayerList);
                sortedPlayers.Sort((a, b) => a.UserID.CompareTo(b.UserID));
                firstTurnPlayerID = sortedPlayers[0].UserID;
                Debug.LogWarning($"[GameManager] GetCurrentTurnPlayerID()가 -1을 반환했습니다. 플레이어 목록의 첫 번째 플레이어({firstTurnPlayerID})를 첫 턴으로 설정합니다.");
            }
            
            Debug.Log($"[GameManager] 첫 턴 설정: FirstTurnPlayerID={firstTurnPlayerID}, ConnectedUserID={myUserID}, MyPlayerID={myPlayerID}");

            // 모든 플레이어에게 턴 정보 업데이트
            if (NetworkManager.Instance._gamePlayerList != null && firstTurnPlayerID != -1)
            {
                foreach (var playerInfo in NetworkManager.Instance._gamePlayerList)
                {
                    var playerObj = PlayerManager.Instance.GetPlayerById(playerInfo.UserID);
                    if (playerObj != null)
                    {
                        var controller = playerObj.GetComponent<PlayerController>();
                        if (controller != null)
                        {
                            // ConnectedUserID를 기준으로 로컬 플레이어 판단
                            // 자기 턴이고 로컬 플레이어인 경우에만 컨트롤 가능
                            bool isLocalPlayer = (playerInfo.UserID == myUserID && myUserID != -1);
                            bool isMyTurn = (playerInfo.UserID == firstTurnPlayerID);
                            bool canControl = isLocalPlayer && isMyTurn;
                            
                            controller.SetCanControl(canControl);
                            Debug.Log($"[GameManager] Player {playerInfo.UserID}: isLocal={isLocalPlayer}, isMyTurn={isMyTurn}, canControl={canControl}");
                            
                            // 첫 턴 플레이어에게 OnTurnStart 호출
                            if (isMyTurn)
                            {
                                controller.OnTurnStart(30); // 기본 턴 시간 30초
                                Debug.Log($"[GameManager] ✅ PlayerID {playerInfo.UserID}의 첫 턴 시작! OnTurnStart 호출");
                            }
                        }
                    }
                }
            }
            else
            {
                Debug.LogWarning($"[GameManager] ⚠️ 플레이어 목록이 null이거나 firstTurnPlayerID가 -1입니다. 턴 설정을 건너뜁니다.");
            }
            
            // ID 430을 기다리는 동안 초기 턴 설정이 완료되었음을 로그로 표시
            Debug.Log($"[GameManager] 초기 턴 설정 완료. ID 430 (Turn Start Notify) 수신 시 추가 업데이트됩니다.");
            
            // 플레이어 생성이 완료되었으므로, 보류된 턴 정보가 있으면 적용
            NetworkManager.Instance.ApplyPendingTurnControl();
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

    // 플레이어 사망 후 게임 종료 조건 체크
    public void CheckGameEnd()
    {
        // 다음 프레임에 체크하도록 지연 (Die()가 완전히 실행된 후 체크)
        StartCoroutine(CheckGameEndDelayed());
    }

    private IEnumerator CheckGameEndDelayed()
    {
        // 한 프레임 대기하여 Die()가 완전히 실행되도록 함
        yield return null;

        if (PlayerManager.Instance == null)
        {
            Debug.LogWarning("[GameManager] PlayerManager.Instance가 null입니다.");
            yield break;
        }

        var allPlayers = PlayerManager.Instance.GetAllPlayers();
        int aliveCount = 0;
        int lastAlivePlayerID = -1;

        Debug.Log($"[GameManager] 게임 종료 체크 시작 - 총 플레이어 수: {allPlayers.Count}");

        foreach (var kvp in allPlayers)
        {
            var playerObj = kvp.Value;
            if (playerObj == null)
            {
                Debug.Log($"[GameManager] PlayerID {kvp.Key}: GameObject가 null");
                continue;
            }

            var controller = playerObj.GetComponent<PlayerController>();
            if (controller == null)
            {
                Debug.Log($"[GameManager] PlayerID {kvp.Key}: PlayerController가 null");
                continue;
            }

            // isDead 플래그와 activeSelf 모두 체크
            bool isAlive = !controller.isDead && playerObj.activeSelf;
            Debug.Log($"[GameManager] PlayerID {kvp.Key}: isDead={controller.isDead}, activeSelf={playerObj.activeSelf}, isAlive={isAlive}");

            if (isAlive)
            {
                aliveCount++;
                lastAlivePlayerID = kvp.Key;
            }
        }

        Debug.Log($"[GameManager] 게임 종료 체크 완료 - 생존 플레이어 수: {aliveCount}, 총 플레이어 수: {allPlayers.Count}");

        // 생존 플레이어가 1명 이하이면 게임 종료
        if (aliveCount <= 1 && allPlayers.Count > 1)
        {
            int winnerID = (aliveCount == 1) ? lastAlivePlayerID : -1;
            Debug.Log($"[GameManager] ✅ 게임 종료! 승자: {winnerID}");
            
            // GameUI에 게임 종료 표시
            if (GameUI.Instance != null)
            {
                GameUI.Instance.ShowGameOver(winnerID);
            }
            else
            {
                Debug.LogError("[GameManager] GameUI.Instance가 null입니다!");
            }
        }
        else
        {
            Debug.Log($"[GameManager] 게임 계속 - 생존 플레이어: {aliveCount}명");
        }
    }
}
