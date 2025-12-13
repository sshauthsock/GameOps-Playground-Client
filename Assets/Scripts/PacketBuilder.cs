using System.IO;
using System.Text;
using System;
using UnityEngine;

// PacketBuilder.cs
public class PacketBuilder
{
    private MemoryStream _stream;
    private BinaryWriter _writer;

    public PacketBuilder()
    {
        _stream = new MemoryStream();
        _writer = new BinaryWriter(_stream);
    }

    // 최종 바이트 배열을 반환합니다.
    public byte[] GetPacket()
    {
        return _stream.ToArray();
    }

    // Maestro 핵심: 엔디안 처리 메서드 (Little Endian 강제)
    // M3 서버는 Little Endian만 받습니다.
    private byte[] GetBytes(ushort data)
    {
        byte[] bytes = BitConverter.GetBytes(data);
        // 시스템 엔디안이 Little Endian이 아니라면 (예: Big Endian 시스템), 순서를 뒤집어 강제합니다.
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

    public void WriteHeader(ushort id, ushort totalLength)
    {
        // 1. [TotalLength (2byte)] 쓰기
        _writer.Write(GetBytes(totalLength));
        // 2. [MessageID (2byte)] 쓰기
        _writer.Write(GetBytes(id));
    }

    //M3 규약: ID 100 Login Req (UserName 20 bytes, UTF8) 처리
    public void WriteUserName(string name, int fixedLength)
    {
        byte[] nameBytes = Encoding.UTF8.GetBytes(name);

        if (nameBytes.Length > fixedLength)
        {
            Debug.LogError("UserName이 20바이트를 초과했습니다. M3 규약 위반!");
            // 규약에 맞게 20바이트만 자릅니다.
            _writer.Write(nameBytes, 0, fixedLength);
        }
        else
        {
            // 1. 이름 바이트를 씁니다.
            _writer.Write(nameBytes);

            // 2. 남은 공간을 NULL(0)로 채웁니다. (M3 규약: 남는 공간 null 채움)
            int paddingLength = fixedLength - nameBytes.Length;
            byte[] nullPadding = new byte[paddingLength];
            _writer.Write(nullPadding);
        }
    }
}