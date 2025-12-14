// RoomData.cs 파일 생성
using System;

// RoomList 응답에서 읽어온 방 하나의 정보를 저장하는 구조체 (혹은 클래스)
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