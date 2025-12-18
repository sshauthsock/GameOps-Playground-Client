using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerSlot : MonoBehaviour
{
    public TextMeshProUGUI PlayerNameText;
    public TextMeshProUGUI ReadyStatusText;
    public Image ReadyIndicator;

    private PlayerReadyData _playerData;

    public void SetPlayerData(PlayerReadyData playerData)
    {
        _playerData = playerData;

        if (PlayerNameText != null)
        {
            PlayerNameText.text = playerData.PlayerName;
        }

        if (ReadyStatusText != null)
        {
            ReadyStatusText.text = playerData.IsReady ? "Ready" : "Not Ready";
        }

        if (ReadyIndicator != null)
        {
            ReadyIndicator.color = playerData.IsReady ? Color.green : Color.red;
        }
    }
}

