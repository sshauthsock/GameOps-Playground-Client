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
        // 게임 씬이 로드되면 NetworkManager의 OnSceneLoaded에서 
        // WaitForGameManagerAndStartGame()을 통해 StartGameLogic()이 호출됩니다.
        // 따라서 여기서는 특별한 작업이 필요 없습니다.
        Debug.Log("[GameManager] Start() 호출됨. NetworkManager에서 StartGameLogic() 호출 대기 중...");
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