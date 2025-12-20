using UnityEngine;
using TMPro;

public class GameUI : MonoBehaviour
{
    public static GameUI Instance { get; private set; }

    [Header("Turn Info")]
    public TextMeshProUGUI TurnInfoText;
    public TextMeshProUGUI CurrentPlayerText;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        UpdateTurnInfo();
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
}

