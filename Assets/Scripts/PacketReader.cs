using System.IO;
using System;
using UnityEngine;
using System.Text;

// PacketReader.cs
// M3 서버 규약(Little Endian)에 맞춰 바이트 배열을 역직렬화하는 클래스
public class PacketReader
{
    private MemoryStream _stream;
    private BinaryReader _reader;

    // 수신한 바이트 배열로 초기화합니다.
    public PacketReader(byte[] buffer)
    {
        _stream = new MemoryStream(buffer);
        _reader = new BinaryReader(_stream);
    }

    //Maestro 핵심: 엔디안 처리 메서드 (Little Endian 강제)
    // 읽어온 바이트를 시스템 환경에 맞게 변환합니다.
    private byte[] GetBytes(int size)
    {
        byte[] bytes = _reader.ReadBytes(size);
        // 시스템 엔디안이 Little Endian이 아니라면 순서를 뒤집어 줍니다.
        if (BitConverter.IsLittleEndian == false)
            Array.Reverse(bytes);
        return bytes;
    }

    //헤더 읽기: 2byte (Length) + 2byte (ID)
    public Packet ReadHeader()
    {
        // 1. [TotalLength (2byte)] 읽기
        ushort totalLength = BitConverter.ToUInt16(GetBytes(2), 0);
        // 2. [MessageID (2byte)] 읽기
        ushort id = BitConverter.ToUInt16(GetBytes(2), 0);

        return new Packet(id, totalLength);
    }

    //M3 규약: ID 101 Login Res 읽기
    // ID 101: [Result (4 bytes, int)] (1=성공)
    public int ReadInt32()
    {
        return BitConverter.ToInt32(GetBytes(4), 0);
    }

    // M3 규약에서 bool (1 byte)을 처리하기 위한 메서드
    public bool ReadBoolean()
    {
        return _reader.ReadBoolean(); // 1 바이트
    }
}