using System.IO;
using System.Text;
using System;
using UnityEngine;
using System.Collections.Generic;

public class PacketBuilder
{
    private MemoryStream _stream;
    private BinaryWriter _writer;
    private List<byte> _buffer = new List<byte>();
    public PacketBuilder()
    {
        _stream = new MemoryStream();
        _writer = new BinaryWriter(_stream);
    }

    public byte[] GetPacket()
    {
        return _stream.ToArray();
    }

    private byte[] GetBytes(ushort data)
    {
        byte[] bytes = BitConverter.GetBytes(data);
        if (BitConverter.IsLittleEndian == false)
            Array.Reverse(bytes);
        return bytes;
    }
    private byte[] GetBytes(int data)
    {
        byte[] bytes = BitConverter.GetBytes(data);
        if (BitConverter.IsLittleEndian == false)
            Array.Reverse(bytes);
        return bytes;
    }
    private byte[] GetBytes(float data)
    {
        byte[] bytes = BitConverter.GetBytes(data);
        if (BitConverter.IsLittleEndian == false)
            Array.Reverse(bytes);
        return bytes;
    }
    public void WriteString(string value)
    {
        // UTF-8 인코딩을 사용하여 문자열을 바이트 배열로 변환
        byte[] stringBytes = Encoding.UTF8.GetBytes(value);

        // 1. 문자열의 길이(int, 4바이트)를 먼저 씁니다.
        WriteInt32(stringBytes.Length);

        // 2. 실제 문자열 데이터를 씁니다.
        _buffer.AddRange(stringBytes);
    }
    public void WriteHeader(ushort id, ushort totalLength)
    {
        _writer.Write(GetBytes(totalLength));
        _writer.Write(GetBytes(id));
    }

    public void WriteUserName(string name, int fixedLength)
    {
        byte[] nameBytes = Encoding.UTF8.GetBytes(name);

        if (nameBytes.Length > fixedLength)
        {
            Debug.LogError("UserName이 20바이트를 초과했습니다. M3 규약 위반!");
            _writer.Write(nameBytes, 0, fixedLength);
        }
        else
        {
            _writer.Write(nameBytes);
            int paddingLength = fixedLength - nameBytes.Length;
            byte[] nullPadding = new byte[paddingLength];
            _writer.Write(nullPadding);
        }
    }

    public void WriteInt32(int data)
    {
        _writer.Write(GetBytes(data));
    }

    public void WriteUInt16(ushort data)
    {
        _writer.Write(GetBytes(data));
    }
}