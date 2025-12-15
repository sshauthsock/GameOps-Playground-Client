using UnityEngine;
using System.Collections.Generic;

public class PlayerManager : MonoBehaviour
{
    public static PlayerManager Instance { get; private set; }

    // 💡 게임 내 모든 플레이어 객체들을 관리할 딕셔너리
    // Key: 플레이어 ID, Value: 플레이어 객체(GameObject)
    private Dictionary<int, GameObject> _players = new Dictionary<int, GameObject>();

    // 💡 캐릭터 프리팹 (Unity Editor에서 할당 예정)
    public GameObject playerPrefab;

    // 💡 내 플레이어 ID (서버로부터 받아올 ID)
    public int MyPlayerID { get; private set; } = 9999; // 임시 ID 할당

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
            Debug.LogWarning($"Player ID {playerID}는 이미 존재합니다.");
            return;
        }

        // 프리팹을 인스턴스화하고 위치 설정
        GameObject playerObj = Instantiate(playerPrefab, position, Quaternion.identity);
        playerObj.name = $"Player_{playerID}_{userName}";

        PlayerController controller = playerObj.GetComponent<PlayerController>();

        // 딕셔너리에 추가
        _players.Add(playerID, playerObj);
        Debug.Log($"[PlayerManager] Player {userName} (ID: {playerID}) 생성 완료.");

        // 만약 이 플레이어가 '나'라면 별도의 로직 실행 (예: 카메라 추적)
        if (playerID == MyPlayerID && controller != null)
        {
            controller.isLocalPlayer = true;
            Debug.Log("이것은 내 플레이어입니다. (로컬 플레이어)");
            // TODO: 카메라 추적 로직 추가
        }
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
}