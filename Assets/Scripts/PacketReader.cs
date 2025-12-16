using System;
using System.Text;

public class PacketReader
{
    private readonly byte[] _buffer;
    private int _position;

    public PacketReader(byte[] buffer)
    {
        _buffer = buffer;
        _position = 0;
    }

    public byte[] ReadBytes(int count)
    {
        if (_buffer == null || _position + count > _buffer.Length)
            return new byte[count];

        byte[] readBytes = new byte[count];
        Array.Copy(_buffer, _position, readBytes, 0, count);
        _position += count;
        return readBytes;
    }

    private byte[] GetBytes(int size)
    {
        byte[] bytes = ReadBytes(size);
        if (BitConverter.IsLittleEndian == false)
            Array.Reverse(bytes);
        return bytes;
    }

    public Packet ReadHeader()
    {
        ushort totalLength = BitConverter.ToUInt16(GetBytes(2), 0);
        ushort id = BitConverter.ToUInt16(GetBytes(2), 0);
        return new Packet(id, totalLength);
    }

    public int ReadInt32()
    {
        return BitConverter.ToInt32(GetBytes(4), 0);
    }

    public float ReadFloat()
    {
        // GetBytes(4)를 통해 공통 변수인 _position을 4바이트만큼 정확히 이동시켜야 합니다.
        return BitConverter.ToSingle(GetBytes(4), 0);
    }

    public bool ReadBoolean()
    {
        return BitConverter.ToBoolean(GetBytes(1), 0);
    }

    public string ReadUserName(int length)
    {
        byte[] stringBytes = ReadBytes(length);
        int nullIndex = Array.IndexOf(stringBytes, (byte)0);
        int actualLength = (nullIndex == -1) ? length : nullIndex;
        return Encoding.UTF8.GetString(stringBytes, 0, actualLength);
    }
}