using System.IO;
using System;
using UnityEngine;
using System.Text;
using System.Linq;

public class PacketReader
{
    private readonly byte[] _buffer;
    private int _offset = 0;
    private int _position;

    public PacketReader(byte[] buffer)
    {
        _buffer = buffer;
        _position = 0;
    }

    public byte[] ReadBytes(int count)
    {
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
        // 1. 버퍼에서 4바이트를 읽습니다.
        byte[] floatBytes = _buffer.Skip(_offset).Take(4).ToArray();
        _offset += 4;

        // 2. 시스템의 엔디언과 다르면 바이트 순서를 뒤집습니다.
        if (BitConverter.IsLittleEndian == false)
        {
            Array.Reverse(floatBytes);
        }

        // 3. 바이트 배열을 float 값으로 변환합니다.
        return BitConverter.ToSingle(floatBytes, 0);
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
        return System.Text.Encoding.UTF8.GetString(stringBytes, 0, actualLength);
    }
}