[System.Serializable]
public class PlayerReadyData
{
    public int PlayerID { get; private set; }
    public string PlayerName { get; private set; }
    public bool IsReady { get; private set; }

    public PlayerReadyData(int playerID, string playerName, bool isReady)
    {
        PlayerID = playerID;
        PlayerName = playerName;
        IsReady = isReady;
    }
}

