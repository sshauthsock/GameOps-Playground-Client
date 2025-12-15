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
        // 씬 로드 후, 서버에 게임 준비 요청 패킷을 전송합니다.
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
    }

    // NetworkManager가 호출할 게임 시작 함수
    public void StartGameLogic()
    {
        Debug.Log("======================================");
        Debug.Log("       🎉 Game Start Logic 실행 🎉       ");
        Debug.Log("======================================");

    }
}