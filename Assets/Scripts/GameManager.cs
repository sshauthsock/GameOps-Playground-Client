using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    private bool _isGameStarted = false;
    public static GameManager Instance { get; private set; }
    private float _lastDummyMoveTime = 0f;
    private const float DUMMY_MOVE_INTERVAL = 0.1f; // 0.1초마다 이동 시도
    private Vector3 _dummyTargetPos = new Vector3(5, 0, 0); // RemotePlayer_A의 시작 위치
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
        StartCoroutine(DelayedGameStart());
    }
    private IEnumerator DelayedGameStart()
    {
        //  한 프레임을 기다려 모든 컴포넌트의 Awake/Start 완료를 보장
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
        PlayerManager.Instance.ClearAllPlayers();

        // 내 캐릭터 생성 (0, 0, 0)
        PlayerManager.Instance.AddPlayer(
            playerID: PlayerManager.Instance.MyPlayerID,
            userName: NetworkManager.Instance.ConnectedUserName,
            position: Vector3.zero
        );

        //  원격 플레이어 생성 위치를 (5, 0, 0)으로 명확히 고정
        // 더미 타겟 위치도 이와 일치시켜야 첫 프레임 텔레포트를 막습니다.
        _dummyTargetPos = new Vector3(5, 0, 0);

        PlayerManager.Instance.AddPlayer(
            playerID: 1000,
            userName: "RemotePlayer_A",
            position: _dummyTargetPos
        );

        _isGameStarted = true;
        Debug.Log("🎉 Game Start Logic 완료 - 이제부터 동기화 시작");
    }
}