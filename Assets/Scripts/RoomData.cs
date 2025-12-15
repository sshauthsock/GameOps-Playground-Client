using System;

public class RoomData
{
    public int RoomID { get; private set; }
    public string RoomName { get; private set; }
    public int CurrentUserCount { get; private set; }

    public RoomData(int id, string name, int userCount)
    {
        RoomID = id;
        RoomName = name;
        CurrentUserCount = userCount;
    }
}