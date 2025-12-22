using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerSlot : MonoBehaviour
{
    [Header("Player Info")]
    public TextMeshProUGUI PlayerNameText;
    public TextMeshProUGUI ReadyStatusText;
    public Image ReadyIndicator;
    public Image BackgroundImage; // 배경 이미지 (선택사항)

    private PlayerReadyData _playerData;
    private const int PLAYER_NAME_FONT_SIZE = 36; // 큰 글씨
    private const int READY_STATUS_FONT_SIZE = 28; // 중간 글씨

    private void Awake()
    {
        // 폰트 크기 초기화만 수행 (레이아웃은 Unity Editor에서 설정)
        InitializeFontSizes();
    }

    private void InitializeFontSizes()
    {
        if (PlayerNameText != null)
        {
            PlayerNameText.fontSize = PLAYER_NAME_FONT_SIZE;
            PlayerNameText.fontStyle = FontStyles.Bold;
            PlayerNameText.color = Color.white;
        }

        if (ReadyStatusText != null)
        {
            ReadyStatusText.fontSize = READY_STATUS_FONT_SIZE;
            ReadyStatusText.fontStyle = FontStyles.Bold;
        }
    }

    public void SetPlayerData(PlayerReadyData playerData)
    {
        _playerData = playerData;

        if (PlayerNameText != null)
        {
            PlayerNameText.text = playerData.PlayerName;
            // 폰트 크기 확실히 설정
            if (PlayerNameText.fontSize != PLAYER_NAME_FONT_SIZE)
            {
                PlayerNameText.fontSize = PLAYER_NAME_FONT_SIZE;
            }
        }

        if (ReadyStatusText != null)
        {
            ReadyStatusText.text = playerData.IsReady ? "READY" : "○ NOT READY";
            // 폰트 크기 확실히 설정
            if (ReadyStatusText.fontSize != READY_STATUS_FONT_SIZE)
            {
                ReadyStatusText.fontSize = READY_STATUS_FONT_SIZE;
            }
            
            // Ready 상태에 따라 색상 변경
            ReadyStatusText.color = playerData.IsReady 
                ? new Color(0f, 0.8f, 0f) // 밝은 초록색
                : new Color(0.8f, 0.8f, 0.8f); // 회색
        }

        if (ReadyIndicator != null)
        {
            ReadyIndicator.color = playerData.IsReady 
                ? new Color(0f, 1f, 0f) // 밝은 초록색
                : new Color(1f, 0.3f, 0.3f); // 연한 빨간색
        }

        // 배경 색상 변경 (선택사항)
        if (BackgroundImage != null)
        {
            // Ready 상태에 따라 배경 색상 약간 변경
            Color bgColor = playerData.IsReady 
                ? new Color(0.2f, 0.4f, 0.2f, 0.3f) // 초록색 틴트
                : new Color(0.3f, 0.3f, 0.3f, 0.3f); // 회색 틴트
            BackgroundImage.color = bgColor;
        }
    }
}

