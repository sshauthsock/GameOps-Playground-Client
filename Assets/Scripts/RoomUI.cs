using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class RoomUI : MonoBehaviour
{
    [Header("Room Info")]
    public TextMeshProUGUI RoomNameText;
    public TextMeshProUGUI PlayerCountText;

    [Header("Player List")]
    public GameObject PlayerSlotPrefab;
    public Transform PlayerListParent;

    [Header("Buttons")]
    public Button ReadyButton;
    public Button LeaveButton;
    public Button StartGameButton;

    private bool _isLocalPlayerReady = false;

    private void Start()
    {
        if (ReadyButton != null)
        {
            ReadyButton.onClick.RemoveAllListeners();
            ReadyButton.onClick.AddListener(OnReadyButtonClicked);
        }

        if (LeaveButton != null)
        {
            LeaveButton.onClick.RemoveAllListeners();
            LeaveButton.onClick.AddListener(OnLeaveButtonClicked);
        }

        if (StartGameButton != null)
        {
            StartGameButton.onClick.RemoveAllListeners();
            StartGameButton.onClick.AddListener(OnStartGameButtonClicked);
            StartGameButton.interactable = false; // 초기에는 비활성화
        }
    }

    public void UpdateRoomInfo(int roomID, string roomName, int playerCount, int maxPlayers)
    {
        if (RoomNameText != null)
        {
            RoomNameText.text = roomName;
        }

        if (PlayerCountText != null)
        {
            PlayerCountText.text = $"{playerCount}/{maxPlayers}";
        }
    }

    public void UpdatePlayerList(List<PlayerReadyData> players, bool isLocalPlayerReady)
    {
        _isLocalPlayerReady = isLocalPlayerReady;

        // 기존 플레이어 슬롯 제거
        ClearPlayerSlots();

        // 플레이어 슬롯 생성
        foreach (var player in players)
        {
            GameObject slotObj = Instantiate(PlayerSlotPrefab, PlayerListParent);
            PlayerSlot slot = slotObj.GetComponent<PlayerSlot>();
            if (slot != null)
            {
                slot.SetPlayerData(player);
            }
        }

        // Ready 버튼 상태 업데이트
        if (ReadyButton != null)
        {
            TextMeshProUGUI buttonText = ReadyButton.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonText != null)
            {
                buttonText.text = _isLocalPlayerReady ? "Cancel Ready" : "Ready";
            }
        }
    }

    private void ClearPlayerSlots()
    {
        foreach (Transform child in PlayerListParent)
        {
            Destroy(child.gameObject);
        }
    }

    public void OnAllPlayersReady()
    {
        if (StartGameButton != null)
        {
            StartGameButton.interactable = true;
        }
    }

    private void OnReadyButtonClicked()
    {
        if (RoomManager.Instance != null)
        {
            RoomManager.Instance.OnReadyButtonClicked();
        }
    }

    private void OnLeaveButtonClicked()
    {
        if (RoomManager.Instance != null)
        {
            RoomManager.Instance.OnLeaveRoomButtonClicked();
        }
    }

    private void OnStartGameButtonClicked()
    {
        if (RoomManager.Instance != null)
        {
            RoomManager.Instance.StartGame();
        }
    }
}

