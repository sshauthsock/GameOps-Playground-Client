public class Packet
{
    // 헤더: [TotalLength (2byte)] + [MessageID (2byte)]
    public ushort TotalLength;
    public ushort MessageID;

    public Packet(ushort id, ushort length)
    {
        MessageID = id;
        TotalLength = length;
    }
}