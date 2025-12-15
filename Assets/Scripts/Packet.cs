public class Packet
{
    public ushort TotalLength;
    public ushort MessageID;

    public Packet(ushort id, ushort length)
    {
        MessageID = id;
        TotalLength = length;
    }
}