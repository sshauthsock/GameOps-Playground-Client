[System.Serializable]
public class PlayerInfo
{
    public int UserID { get; private set; }
    public string UserName { get; private set; }

    public PlayerInfo(int userID, string userName)
    {
        UserID = userID;
        UserName = userName;
    }
}

