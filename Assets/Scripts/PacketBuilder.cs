using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using UnityEngine;

public class PacketBuilder
{
    private MemoryStream _stream;
    private BinaryWriter _writer;

    public PacketBuilder()
    {
        //  모든 데이터를 하나의 스트림으로 관리합니다.
        _stream = new MemoryStream();
        _writer = new BinaryWriter(_stream);
    }


    public byte[] GetPacket()
    {
        _writer.Flush();
        return _stream.ToArray();
    }


    private byte[] GetBytes(ushort data)
    {
        byte[] bytes = BitConverter.GetBytes(data);
        if (BitConverter.IsLittleEndian == false) Array.Reverse(bytes);
        return bytes;
    }

    private byte[] GetBytes(int data)
    {
        byte[] bytes = BitConverter.GetBytes(data);
        if (BitConverter.IsLittleEndian == false) Array.Reverse(bytes);
        return bytes;
    }

    private byte[] GetBytes(float data)
    {
        byte[] bytes = BitConverter.GetBytes(data);
        if (BitConverter.IsLittleEndian == false) Array.Reverse(bytes);
        return bytes;
    }

    public void WriteHeader(ushort id, ushort totalLength)
    {
        _writer.Write(GetBytes(totalLength));
        _writer.Write(GetBytes(id));
    }

    public void WriteInt32(int data)
    {
        _writer.Write(GetBytes(data));
    }

    public void WriteUInt16(ushort data)
    {
        _writer.Write(GetBytes(data));
    }

    public void WriteFloat(float value)
    {
        //  기존의 _buffer.AddRange 대신 _writer를 직접 사용하여 데이터 유실을 방지합니다.
        _writer.Write(GetBytes(value));
    }

    public void WriteString(string value)
    {
        byte[] stringBytes = Encoding.UTF8.GetBytes(value);
        // 1. 문자열의 길이를 먼저 기록
        WriteInt32(stringBytes.Length);
        // 2. 실제 문자열 바이트 기록
        _writer.Write(stringBytes);
    }

    public void WriteUserName(string name, int fixedLength)
    {
        byte[] nameBytes = Encoding.UTF8.GetBytes(name);

        if (nameBytes.Length > fixedLength)
        {
            Debug.LogError($"UserName이 {fixedLength}바이트를 초과했습니다.");
            _writer.Write(nameBytes, 0, fixedLength);
        }
        else
        {
            _writer.Write(nameBytes);
            // 남은 공간을 null(0) 바이트로 채웁니다.
            int paddingLength = fixedLength - nameBytes.Length;
            if (paddingLength > 0)
            {
                byte[] nullPadding = new byte[paddingLength];
                _writer.Write(nullPadding);
            }
        }
    }

    public void WriteBoolean(bool value)
    {
        _writer.Write(value);
    }
}