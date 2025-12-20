using UnityEngine;
using System.Collections.Generic;

public class PlayerManager : MonoBehaviour
{
    public static PlayerManager Instance { get; private set; }

    //  게임 내 모든 플레이어 객체들을 관리할 딕셔너리
    // Key: 플레이어 ID, Value: 플레이어 객체(GameObject)
    private Dictionary<int, GameObject> _players = new Dictionary<int, GameObject>();

    // 캐릭터 프리팹 (Unity Editor에서 할당 예정)
    public GameObject playerPrefab;

    //  내 플레이어 ID (서버로부터 받아올 ID)
    public int MyPlayerID { get; private set; } = -1; // 초기값 -1 (서버에서 받을 때까지 대기)

    public void SetMyPlayerID(int playerID)
    {
        MyPlayerID = playerID;
        Debug.Log($"[PlayerManager] MyPlayerID 설정: {MyPlayerID}");
    }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    // --- 핵심 기능 1: 캐릭터 생성 ---
    public void AddPlayer(int playerID, string userName, Vector3 position)
    {
        if (_players.ContainsKey(playerID))
        {
            Debug.LogWarning($"[PlayerManager] AddPlayer - PlayerID {playerID}는 이미 존재합니다. 생성 건너뜀.");
            return;
        }

        Debug.Log($"[PlayerManager] AddPlayer 시작 - PlayerID: {playerID}, UserName: {userName}, Position: {position}");

        // 1. 캐릭터 생성
        GameObject playerObj = Instantiate(playerPrefab, position, Quaternion.identity);
        playerObj.name = $"Player_{playerID}_{userName}";
        Debug.Log($"[PlayerManager] 플레이어 GameObject 생성 완료: {playerObj.name}");

        // 2. 컨트롤러 가져오기
        PlayerController controller = playerObj.GetComponent<PlayerController>();

        // 3. 딕셔너리에 추가
        _players.Add(playerID, playerObj);
        Debug.Log($"[PlayerManager] 딕셔너리에 추가 완료. 현재 플레이어 수: {_players.Count}");

        // 4. [핵심 추가] 생성되자마자 ID와 팀 색상을 부여합니다.
        if (controller != null)
        {
            // MyPlayerID와 비교하여 로컬 여부 결정
            bool isLocal = (playerID == MyPlayerID && MyPlayerID != -1);
            Debug.Log($"[PlayerManager] AddPlayer - PlayerID: {playerID}, MyPlayerID: {MyPlayerID}, isLocal: {isLocal}");
            controller.SetPlayerID(playerID, isLocal);
            
            // 5. [중요] 모든 플레이어의 초기 위치를 _targetPosition으로 설정하여 잘못된 위치로 이동하는 것을 방지
            // 로컬 플레이어도 초기 위치를 명시적으로 설정하여 Start()에서 잘못된 _targetPosition이 설정되는 것을 방지
            Quaternion initialRotation = isLocal ? Quaternion.Euler(0, 90f, 0) : Quaternion.Euler(0, -90f, 0);
            controller.SetNetworkPosition(position, initialRotation);
            Debug.Log($"[PlayerManager] 플레이어({playerID})의 초기 위치 설정: {position}, isLocal: {isLocal}");
            
            Debug.Log($"[PlayerManager] ID {playerID} 설정 완료. Local: {isLocal}");
        }
        else
        {
            Debug.LogError($"[PlayerManager] AddPlayer - PlayerController를 찾을 수 없습니다! GameObject: {playerObj.name}");
        }
        
        Debug.Log($"[PlayerManager] AddPlayer 완료 - PlayerID: {playerID}");
    }

    // 기존 플레이어의 isLocalPlayer 상태를 업데이트 (MyPlayerID가 나중에 설정된 경우)
    public void UpdatePlayerLocalStatus()
    {
        Debug.Log($"[PlayerManager] UpdatePlayerLocalStatus 시작 - MyPlayerID: {MyPlayerID}, 플레이어 수: {_players.Count}");
        
        foreach (var kvp in _players)
        {
            int playerID = kvp.Key;
            GameObject playerObj = kvp.Value;
            PlayerController controller = playerObj.GetComponent<PlayerController>();
            
            if (controller != null)
            {
                bool isLocal = (playerID == MyPlayerID && MyPlayerID != -1);
                if (controller.isLocalPlayer != isLocal)
                {
                    Debug.Log($"[PlayerManager] UpdatePlayerLocalStatus - PlayerID: {playerID}, MyPlayerID: {MyPlayerID}, Updating isLocal: {isLocal} -> {isLocal}");
                    controller.SetPlayerID(playerID, isLocal);
                }
                else
                {
                    Debug.Log($"[PlayerManager] UpdatePlayerLocalStatus - PlayerID: {playerID}, MyPlayerID: {MyPlayerID}, isLocal: {isLocal} (변경 없음)");
                }
            }
        }
        
        Debug.Log($"[PlayerManager] UpdatePlayerLocalStatus 완료");
    }

    // --- 핵심 기능 2: 캐릭터 제거 ---
    public void RemovePlayer(int playerID)
    {
        if (_players.TryGetValue(playerID, out GameObject playerObj))
        {
            _players.Remove(playerID);
            Destroy(playerObj);
            Debug.Log($"[PlayerManager] Player ID {playerID} 제거 완료.");
        }
    }

    // --- 핵심 기능 3: 캐릭터 가져오기 ---
    public GameObject GetPlayer(int playerID)
    {
        _players.TryGetValue(playerID, out GameObject playerObj);
        return playerObj;
    }

    // --- 디버깅용: 모든 플레이어 제거 ---
    public void ClearAllPlayers()
    {
        foreach (var player in _players.Values)
        {
            Destroy(player);
        }
        _players.Clear();
        Debug.Log("[PlayerManager] 모든 플레이어 객체 제거 완료.");
    }
    public void UpdatePlayerPosition(int playerID, Vector3 position, Quaternion rotation)
    {
        // 로그를 찍어서 패킷이 여기까지 도달하는지 확인하세요.
        Debug.Log($"[PlayerManager] 업데이트 시도 - ID: {playerID}");

        if (_players.TryGetValue(playerID, out GameObject playerObj))
        {
            //  크기가 변하는 것을 방지하기 위해 스케일을 (1,1,1)로 고정
            playerObj.transform.localScale = Vector3.one;
            PlayerController controller = playerObj.GetComponent<PlayerController>();
            if (controller != null)
            {
                if (controller.isLocalPlayer) return;

                //  여기서 SetNetworkPosition이 호출되는지 확인
                controller.SetNetworkPosition(position, rotation);
            }
        }
        else
        {
            // 만약 이 로그가 뜬다면 딕셔너리에 해당 ID가 없는 것입니다.
            Debug.LogWarning($"[PlayerManager] ID {playerID}를 딕셔너리에서 찾을 수 없음!");
        }
    }
    public GameObject GetPlayerById(int id)
    {
        // 딕셔너리에서 ID를 키로 사용하여 객체를 찾습니다.
        if (_players.TryGetValue(id, out GameObject player))
        {
            return player;
        }

        Debug.LogWarning($"[PlayerManager] ID {id}에 해당하는 플레이어를 찾을 수 없습니다.");
        return null;
    }
    
    // 모든 플레이어를 가져오는 메서드
    public Dictionary<int, GameObject> GetAllPlayers()
    {
        return new Dictionary<int, GameObject>(_players);
    }
}