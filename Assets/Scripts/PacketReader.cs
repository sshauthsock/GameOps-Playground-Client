using System.IO;
using System;
using UnityEngine;
using System.Text;

// PacketReader.cs
// M3 서버 규약(Little Endian)에 맞춰 바이트 배열을 역직렬화하는 클래스
public class PacketReader
{
    private readonly byte[] _buffer;
    private int _position;

    // MemoryStream과 BinaryReader는 제거되었습니다.

    // 수신한 바이트 배열로 초기화합니다.
    public PacketReader(byte[] buffer)
    {
        _buffer = buffer;
        _position = 0; // 항상 0에서 시작합니다.
    }

    // 지정된 길이만큼의 바이트를 읽고 내부 포지션을 이동시키는 핵심 함수
    public byte[] ReadBytes(int count)
    {
        // 1. 요청한 길이만큼의 새로운 배열을 생성합니다.
        byte[] readBytes = new byte[count];

        // 2. 현재 버퍼의 _position 위치부터 count만큼을 새 배열로 복사합니다.
        // Array.Copy(원본 배열, 원본 시작 인덱스, 대상 배열, 대상 시작 인덱스, 복사할 길이)
        Array.Copy(_buffer, _position, readBytes, 0, count);

        // 3. 읽기가 완료되었으므로, 내부 포지션을 읽은 길이만큼 이동시킵니다.
        _position += count;

        return readBytes;
    }

    // Maestro 핵심: 엔디안 처리 메서드 (Little Endian 강제)
    // ReadBytes를 사용하여 읽고, 시스템 엔디안에 맞게 변환합니다.
    private byte[] GetBytes(int size)
    {
        byte[] bytes = ReadBytes(size);

        // 시스템 엔디안이 Little Endian이 아니라면 순서를 뒤집어 줍니다.
        if (BitConverter.IsLittleEndian == false)
            Array.Reverse(bytes);

        return bytes;
    }

    // 헤더 읽기: 2byte (Length) + 2byte (ID)
    public Packet ReadHeader()
    {
        // ReadBytes(2) -> GetBytes(2) -> BitConverter 순서로 일관되게 처리됩니다.
        ushort totalLength = BitConverter.ToUInt16(GetBytes(2), 0);
        ushort id = BitConverter.ToUInt16(GetBytes(2), 0);

        return new Packet(id, totalLength);
    }

    // ID 101 Login Res 읽기 및 RoomList Res 등 Int32 값 읽기
    public int ReadInt32()
    {
        // GetBytes(4)를 사용하여 4바이트 읽기 + 엔디안 처리
        return BitConverter.ToInt32(GetBytes(4), 0);
    }

    // Bool (1 byte) 값 읽기
    public bool ReadBoolean()
    {
        // GetBytes(1)을 사용하여 1바이트 읽기 + 엔디안 처리
        return BitConverter.ToBoolean(GetBytes(1), 0);
    }

    // RoomName, UserName 등 고정 길이 문자열 읽기 (널 패딩 처리)
    public string ReadUserName(int length)
    {
        // 1. 스트림에서 'length'만큼의 바이트를 읽어옵니다. (ReadBytes 사용)
        byte[] stringBytes = ReadBytes(length);

        // 2. 바이트 배열에서 널 패딩(0x00)을 제거할 경계(Index)를 찾습니다.
        int nullIndex = Array.IndexOf(stringBytes, (byte)0);

        // nullIndex가 -1이면 전체가 데이터 (널 패딩 없음), 아니면 해당 위치가 끝입니다.
        int actualLength = (nullIndex == -1) ? length : nullIndex;

        // 3. 바이트를 문자열로 변환합니다. (UTF8 인코딩 가정)
        return System.Text.Encoding.UTF8.GetString(stringBytes, 0, actualLength);
    }
}