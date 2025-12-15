using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

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

    void Start()
    {
        StartCoroutine(DelayedGameStart());
    }
    private IEnumerator DelayedGameStart()
    {
        // 💡 한 프레임을 기다려 모든 컴포넌트의 Awake/Start 완료를 보장
        yield return null;

        // 이 시점에서는 PlayerManager.Instance가 Null이 아닐 확률이 높습니다.
        // (PlayerManager가 DontDestroyOnLoad 객체라면 더욱 확실합니다.)
        if (PlayerManager.Instance == null)
        {
            Debug.LogError("FATAL ERROR: PlayerManager가 여전히 null입니다. Project Settings -> Script Execution Order를 확인하십시오!");
            yield break;
        }

        RequestGameReady();
    }
    private void RequestGameReady()
    {
        Debug.Log("[GameManager] 서버에 게임 준비 요청 (ID 400) 전송 시도.");

        // 1. NetworkManager.Instance.SendGameReadyRequest(); // 실제 ID 400 전송 (미구현)

        // 2. 더미 테스트 환경이므로, 응답 패킷(ID 401)을 강제 주입하여 시작 로직 테스트
        if (NetworkManager.Instance.IS_DUMMY_MODE)
        {
            //  모든 더미 로직을 NetworkManager에게 위임
            NetworkManager.Instance.ForceProcessGameStartDummy();
        }
        else
        {
            NetworkManager.Instance.SendGameReadyRequest();
        }
    }

    // NetworkManager가 호출할 게임 시작 함수
    public void StartGameLogic()
    {
        Debug.Log("======================================");
        Debug.Log(" 🎉 Game Start Logic 실행 🎉 ");
        Debug.Log("======================================");

        //  1. 인게임 진입 시 기존 플레이어 객체 모두 제거 (씬 전환 시 호출될 수 있으므로 안전장치)
        PlayerManager.Instance.ClearAllPlayers();

        //  2. '나'의 캐릭터 생성 (임시 데이터)
        int myID = PlayerManager.Instance.MyPlayerID;
        string myName = NetworkManager.Instance.ConnectedUserName;
        //  3. 서버로부터 초기 플레이어 목록을 받았다고 가정하고 생성

        // 내 캐릭터 생성
        PlayerManager.Instance.AddPlayer(
            playerID: myID,
            userName: myName,
            position: new Vector3(0, 0, 0)
        );

        // 다른 플레이어 (더미) 생성
        PlayerManager.Instance.AddPlayer(
            playerID: 1000,
            userName: "RemotePlayer_A",
            position: new Vector3(5, 0, 0)
        );
    }
}