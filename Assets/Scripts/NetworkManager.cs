using UnityEngine;

public class NetworkManager : MonoBehaviour
{
    private void Awake()
    {
        // Maestro Tip: 씬 전환 시 파괴되지 않고 영원히 생존하도록 설정
        DontDestroyOnLoad(gameObject);
    }

    public byte[] MakeLoginPacket(string userName)
    {
        // M3 규약: UserName은 20바이트 고정 길이
        const int USER_NAME_LENGTH = 20;
        const ushort MESSAGE_ID = 100;
        // 전체 길이 = 헤더(4byte) + 바디(20byte)
        const ushort TOTAL_LENGTH = 4 + USER_NAME_LENGTH;

        PacketBuilder builder = new PacketBuilder();

        // 1. 헤더 쓰기
        builder.WriteHeader(MESSAGE_ID, TOTAL_LENGTH);

        // 2. 바디 쓰기: UserName (20 bytes, null padding)
        builder.WriteUserName(userName, USER_NAME_LENGTH);

        Debug.Log($"로그인 패킷 (ID 100) 생성 완료. 총 길이: {TOTAL_LENGTH} 바이트.");

        return builder.GetPacket();
    }

}