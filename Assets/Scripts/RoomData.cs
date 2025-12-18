[System.Serializable]
public class RoomData
{
    public int RoomID { get; private set; }
    public string RoomName { get; private set; }
    public int CurrentUserCount { get; private set; }
    public int MaxPlayers { get; private set; }
    public bool IsStarted { get; private set; }

    public RoomData(int id, string name, int userCount)
    {
        RoomID = id;
        RoomName = name;
        CurrentUserCount = userCount;
        MaxPlayers = 5; // 기본값
        IsStarted = false;
    }

    public RoomData(int id, string name, int userCount, int maxPlayers)
    {
        RoomID = id;
        RoomName = name;
        CurrentUserCount = userCount;
        MaxPlayers = maxPlayers;
        IsStarted = false;
    }
}

// using System;

// public class RoomData
// {
//     public int RoomID { get; private set; }
//     public string RoomName { get; private set; }
//     public int CurrentUserCount { get; private set; }

//     public RoomData(int id, string name, int userCount)
//     {
//         RoomID = id;
//         RoomName = name;
//         CurrentUserCount = userCount;
//     }
// }