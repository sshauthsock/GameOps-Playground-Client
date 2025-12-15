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

        // 1. NetworkManager를 통해 게임 준비 요청 패킷 전송
        // NetworkManager.Instance.SendGameReadyRequest(); // ID 400 전송 예정

        // 2. 더미 테스트 환경이므로, 응답 패킷(ID 401)을 강제 주입하여 시작 로직 테스트
        if (!NetworkManager.Instance.IsConnected())
        {
            byte[] dummyStartAns = NetworkManager.Instance.MakeDummyGameStartPacket();

            lock (NetworkManager.Instance._packetQueue)
            {
                NetworkManager.Instance._packetQueue.Enqueue(dummyStartAns);
            }
            Debug.Log("ID 401 (Game Start Ans) 더미 패킷 주입 완료.");

            // 패킷 처리 강제 실행
            NetworkManager.Instance.ForceProcessPackets();
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