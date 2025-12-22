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
    private List<GameObject> _playerSlotObjects = new List<GameObject>(); // 플레이어 슬롯 오브젝트 캐싱
    private Dictionary<int, PlayerSlot> _playerSlotMap = new Dictionary<int, PlayerSlot>(); // 플레이어 ID로 슬롯 매핑
    private TextMeshProUGUI _readyButtonText; // Ready 버튼 텍스트 캐싱

    private void Start()
    {
        if (ReadyButton != null)
        {
            ReadyButton.onClick.RemoveAllListeners();
            ReadyButton.onClick.AddListener(OnReadyButtonClicked);
            // Ready 버튼 텍스트 캐싱
            _readyButtonText = ReadyButton.GetComponentInChildren<TextMeshProUGUI>();
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
        
        // 레이아웃은 Unity Editor에서 설정한 대로 사용
        // 코드에서는 데이터만 업데이트합니다
    }

    public void UpdateRoomInfo(int roomID, string roomName, int playerCount, int maxPlayers)
    {
        Debug.Log($"[RoomUI] UpdateRoomInfo 호출: ID={roomID}, Name={roomName}, Players={playerCount}/{maxPlayers}");
        
        if (RoomNameText != null)
        {
            // 방 이름이 비어있으면 기본값 표시
            string displayName = string.IsNullOrEmpty(roomName) ? $"Room #{roomID}" : roomName;
            RoomNameText.text = $"Room: {displayName}";
            RoomNameText.fontSize = 42; // 큰 글씨
            RoomNameText.fontStyle = FontStyles.Bold;
            RoomNameText.color = Color.white;
            Debug.Log($"[RoomUI] RoomNameText 업데이트: {RoomNameText.text}");
        }
        else
        {
            Debug.LogWarning("[RoomUI] RoomNameText가 null입니다!");
        }

        if (PlayerCountText != null)
        {
            PlayerCountText.text = $"Players: {playerCount}/{maxPlayers}";
            PlayerCountText.fontSize = 32;
            PlayerCountText.fontStyle = FontStyles.Bold;
            PlayerCountText.color = Color.white;
        }
    }

    public void UpdatePlayerList(List<PlayerReadyData> players, bool isLocalPlayerReady)
    {
        bool readyStateChanged = (_isLocalPlayerReady != isLocalPlayerReady);
        _isLocalPlayerReady = isLocalPlayerReady;

        // 플레이어 목록 최적화 업데이트
        UpdatePlayerSlotsOptimized(players);

        // Ready 버튼 상태 업데이트 (변경된 경우에만)
        if (readyStateChanged && _readyButtonText != null)
        {
            _readyButtonText.text = _isLocalPlayerReady ? "Cancel Ready" : "Ready";
        }
    }

    private void UpdatePlayerSlotsOptimized(List<PlayerReadyData> players)
    {
        // 1. 제거된 플레이어 슬롯 삭제
        List<int> currentPlayerIDs = new List<int>();
        foreach (var player in players)
        {
            currentPlayerIDs.Add(player.PlayerID);
        }

        List<int> slotsToRemove = new List<int>();
        foreach (var kvp in _playerSlotMap)
        {
            if (!currentPlayerIDs.Contains(kvp.Key))
            {
                slotsToRemove.Add(kvp.Key);
            }
        }

        foreach (int playerID in slotsToRemove)
        {
            if (_playerSlotMap.TryGetValue(playerID, out PlayerSlot slot))
            {
                if (slot != null && slot.gameObject != null)
                {
                    _playerSlotObjects.Remove(slot.gameObject);
                    Destroy(slot.gameObject);
                }
                _playerSlotMap.Remove(playerID);
            }
        }

        // 2. 기존 플레이어 슬롯 업데이트 또는 새로 생성
        foreach (var player in players)
        {
            if (_playerSlotMap.TryGetValue(player.PlayerID, out PlayerSlot existingSlot))
            {
                // 기존 슬롯이 있으면 데이터만 업데이트
                if (existingSlot != null)
                {
                    existingSlot.SetPlayerData(player);
                }
            }
            else
            {
                // 새 슬롯 생성
                if (PlayerSlotPrefab != null && PlayerListParent != null)
                {
                    GameObject slotObj = Instantiate(PlayerSlotPrefab, PlayerListParent);
                    PlayerSlot slot = slotObj.GetComponent<PlayerSlot>();
                    if (slot != null)
                    {
                        slot.SetPlayerData(player);
                        _playerSlotObjects.Add(slotObj);
                        _playerSlotMap[player.PlayerID] = slot;
                        
                        // 슬롯 레이아웃 강제 설정
                        EnsureSlotLayout(slotObj);
                    }
                }
            }
        }
        
        // 레이아웃 그룹 최종 갱신
        RefreshLayoutGroup();
    }

    private void EnsureSlotLayout(GameObject slotObj)
    {
        if (slotObj == null) return;
        
        RectTransform rectTransform = slotObj.GetComponent<RectTransform>();
        if (rectTransform != null)
        {
            // 높이가 0이거나 너무 작으면 강제로 설정
            if (rectTransform.sizeDelta.y < 50)
            {
                rectTransform.sizeDelta = new Vector2(rectTransform.sizeDelta.x, 80);
            }
        }
        
        // Layout Element가 없으면 추가
        LayoutElement layoutElement = slotObj.GetComponent<LayoutElement>();
        if (layoutElement == null)
        {
            layoutElement = slotObj.AddComponent<LayoutElement>();
        }
        
        // Preferred Height 강제 설정
        if (layoutElement.preferredHeight < 50)
        {
            layoutElement.preferredHeight = 80;
        }
        layoutElement.flexibleHeight = 0; // 높이 확장 안 함
    }

    private void RefreshLayoutGroup()
    {
        if (PlayerListParent == null) return;
        
        // Vertical Layout Group이 있으면 강제로 레이아웃 갱신
        VerticalLayoutGroup layoutGroup = PlayerListParent.GetComponent<VerticalLayoutGroup>();
        if (layoutGroup != null)
        {
            // Spacing이 너무 작으면 최소값 설정
            if (layoutGroup.spacing < 5)
            {
                layoutGroup.spacing = 5;
            }
            
            // Child Control Height가 꺼져있으면 켜기
            if (!layoutGroup.childControlHeight)
            {
                layoutGroup.childControlHeight = true;
            }
            
            // 레이아웃 강제 갱신을 위해 비활성화 후 활성화
            layoutGroup.enabled = false;
            layoutGroup.enabled = true;
            
            // Canvas 업데이트 강제
            Canvas.ForceUpdateCanvases();
            
            // 한 프레임 후 다시 갱신
            StartCoroutine(DelayedLayoutRefresh());
        }
        
        // Content Size Fitter가 있으면 갱신
        ContentSizeFitter sizeFitter = PlayerListParent.GetComponent<ContentSizeFitter>();
        if (sizeFitter != null)
        {
            sizeFitter.SetLayoutVertical();
        }
    }

    private System.Collections.IEnumerator DelayedLayoutRefresh()
    {
        yield return null; // 한 프레임 대기
        
        if (PlayerListParent != null)
        {
            VerticalLayoutGroup layoutGroup = PlayerListParent.GetComponent<VerticalLayoutGroup>();
            if (layoutGroup != null)
            {
                layoutGroup.CalculateLayoutInputVertical();
                layoutGroup.SetLayoutVertical();
            }
            
            Canvas.ForceUpdateCanvases();
        }
    }

    private void ClearPlayerSlots()
    {
        // 모든 슬롯 제거
        foreach (var slotObj in _playerSlotObjects)
        {
            if (slotObj != null)
            {
                Destroy(slotObj);
            }
        }
        _playerSlotObjects.Clear();
        _playerSlotMap.Clear();
    }

    public void OnAllPlayersReady()
    {
        if (StartGameButton != null)
        {
            StartGameButton.interactable = true;
            Debug.Log("[RoomUI] Start Game 버튼 활성화");
        }
    }

    public void OnNotAllPlayersReady()
    {
        if (StartGameButton != null)
        {
            StartGameButton.interactable = false;
            Debug.Log("[RoomUI] Start Game 버튼 비활성화 (모든 플레이어가 Ready가 아님)");
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

    private void OnDestroy()
    {
        // 리소스 정리
        ClearPlayerSlots();
    }
}

