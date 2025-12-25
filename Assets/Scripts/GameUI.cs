using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class GameUI : MonoBehaviour
{
    public static GameUI Instance { get; private set; }

    [Header("Turn Info")]
    public TextMeshProUGUI TurnInfoText;
    public TextMeshProUGUI CurrentPlayerText;

    [Header("Game Over UI")]
    public GameObject GameOverPanel;
    public TextMeshProUGUI GameOverText;
    public TextMeshProUGUI WinnerText;
    public Button ReturnToLobbyButton;
    
    private bool _isGameOverShown = false; // 중복 호출 방지

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        
        // 게임 오버 UI 초기화 (Awake에서 먼저 실행하여 게임 시작 시 보이지 않도록)
        if (GameOverPanel != null)
        {
            GameOverPanel.SetActive(false);
        }
    }

    private void Start()
    {
        UpdateTurnInfo();
        
        // 게임 오버 UI 초기화 (이중 보호)
        if (GameOverPanel != null)
        {
            GameOverPanel.SetActive(false);
        }
        
        // 로비로 돌아가기 버튼 이벤트 설정
        if (ReturnToLobbyButton != null)
        {
            ReturnToLobbyButton.onClick.RemoveAllListeners();
            ReturnToLobbyButton.onClick.AddListener(OnReturnToLobbyClicked);
        }
    }

    private void Update()
    {
        UpdateTurnInfo();
    }

    public void UpdateTurnInfo()
    {
        if (NetworkManager.Instance == null) return;

        int currentTurnPlayerID = NetworkManager.Instance.GetCurrentTurnPlayerID();
        bool isMyTurn = NetworkManager.Instance.IsMyTurn();

        if (TurnInfoText != null)
        {
            if (currentTurnPlayerID == -1)
            {
                TurnInfoText.text = "Waiting for turn...";
            }
            else if (isMyTurn)
            {
                TurnInfoText.text = "YOUR TURN!";
                TurnInfoText.color = Color.green;
            }
            else
            {
                // 현재 턴 플레이어 이름 찾기
                string turnPlayerName = "Player " + currentTurnPlayerID;
                if (NetworkManager.Instance._gamePlayerList != null)
                {
                    var player = NetworkManager.Instance._gamePlayerList.Find(p => p.UserID == currentTurnPlayerID);
                    if (player != null)
                    {
                        turnPlayerName = player.UserName;
                    }
                }
                TurnInfoText.text = $"{turnPlayerName}'s Turn";
                TurnInfoText.color = Color.yellow;
            }
        }

        if (CurrentPlayerText != null)
        {
            if (NetworkManager.Instance.ConnectedUserID != -1)
            {
                CurrentPlayerText.text = $"My ID: {NetworkManager.Instance.ConnectedUserID}";
            }
            else
            {
                CurrentPlayerText.text = "My ID: Waiting...";
            }
        }
    }

    public void ShowGameOver(int winnerID)
    {
        // 중복 호출 방지
        if (_isGameOverShown)
        {
            Debug.LogWarning($"[GameUI] ShowGameOver가 이미 호출되었습니다. 중복 호출을 무시합니다. winnerID: {winnerID}");
            return;
        }
        
        Debug.Log($"[GameUI] ShowGameOver 호출됨 - winnerID: {winnerID}");
        Debug.Log($"[GameUI] GameOverPanel null 체크: {GameOverPanel == null}");
        
        if (GameOverPanel == null)
        {
            Debug.LogError("[GameUI] ❌ GameOverPanel이 할당되지 않았습니다! Unity Inspector에서 GameOverPanel을 할당해주세요.");
            return;
        }
        
        _isGameOverShown = true;

        Debug.Log($"[GameUI] GameOverPanel 활성화 전 상태 - activeSelf: {GameOverPanel.activeSelf}, activeInHierarchy: {GameOverPanel.activeInHierarchy}");
        Debug.Log($"[GameUI] GameOverPanel 부모 확인 - parent: {(GameOverPanel.transform.parent != null ? GameOverPanel.transform.parent.name : "null")}");
        
        // 부모가 비활성화되어 있으면 활성화
        if (GameOverPanel.transform.parent != null && !GameOverPanel.transform.parent.gameObject.activeSelf)
        {
            Debug.LogWarning($"[GameUI] GameOverPanel의 부모({GameOverPanel.transform.parent.name})가 비활성화되어 있습니다. 활성화합니다.");
            GameOverPanel.transform.parent.gameObject.SetActive(true);
        }

        GameOverPanel.SetActive(true);
        
        Debug.Log($"[GameUI] GameOverPanel 활성화 후 상태 - activeSelf: {GameOverPanel.activeSelf}, activeInHierarchy: {GameOverPanel.activeInHierarchy}");

        // 승자 정보 표시
        string winnerName = "Unknown";
        bool isMyWin = false;

        if (NetworkManager.Instance != null && NetworkManager.Instance._gamePlayerList != null)
        {
            var winner = NetworkManager.Instance._gamePlayerList.Find(p => p.UserID == winnerID);
            if (winner != null)
            {
                winnerName = winner.UserName;
            }
            
            // 내가 승자인지 확인
            isMyWin = (winnerID == NetworkManager.Instance.ConnectedUserID);
        }

        // Game Over 텍스트 - 화면 중앙에 크게 빨간색으로 표시
        if (GameOverText != null)
        {
            GameOverText.text = "GAME OVER";
            GameOverText.color = Color.red;
            GameOverText.fontSize = 180; // 큰 글씨
            GameOverText.alignment = TextAlignmentOptions.Center;
            GameOverText.fontStyle = FontStyles.Bold;
            Debug.Log($"[GameUI] GameOverText 설정 완료: {GameOverText.text}");
        }
        else
        {
            Debug.LogWarning("[GameUI] GameOverText가 null입니다!");
        }

        // 승자 정보 텍스트 - Game Over 아래에 표시
        if (WinnerText != null)
        {
            if (winnerID == -1)
            {
                WinnerText.text = "DRAW!";
                WinnerText.color = new Color(1f, 0.84f, 0f); // 골드색
                WinnerText.fontStyle = FontStyles.Bold;
            }
            else if (isMyWin)
            {
                WinnerText.text = $"VICTORY!\n{winnerName} YOU WIN!";
                WinnerText.color = new Color(0f, 0.8f, 0f); // 밝은 초록색
                WinnerText.fontStyle = FontStyles.Bold;
            }
            else
            {
                WinnerText.text = $"Not Today...\nLoser: {winnerName}";
                WinnerText.color = new Color(1f, 0.5f, 0.5f); // 연한 빨간색
                WinnerText.fontStyle = FontStyles.Bold;
            }
            WinnerText.fontSize = 48;
            WinnerText.alignment = TextAlignmentOptions.Center;
            Debug.Log($"[GameUI] WinnerText 설정 완료: {WinnerText.text}");
        }
        else
        {
            Debug.LogWarning("[GameUI] WinnerText가 null입니다!");
        }

        if (ReturnToLobbyButton != null)
        {
            Debug.Log($"[GameUI] ReturnToLobbyButton 확인 - activeSelf: {ReturnToLobbyButton.gameObject.activeSelf}");
        }
        else
        {
            Debug.LogWarning("[GameUI] ReturnToLobbyButton이 null입니다!");
        }

        Debug.Log($"[GameUI] ✅ 게임 오버 UI 표시 완료 - 승자: {winnerName} (ID: {winnerID})");
    }

    private void OnReturnToLobbyClicked()
    {
        Debug.Log("[GameUI] 로비로 돌아가기 버튼 클릭");
        
        // 방 나가기 요청 전송
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.SendLeaveRoomRequest();
            Debug.Log("[GameUI] 방 나가기 요청 전송");
        }
        else
        {
            // NetworkManager가 없으면 직접 씬 이동
            UnityEngine.SceneManagement.SceneManager.LoadScene(1);
        }
    }
}

