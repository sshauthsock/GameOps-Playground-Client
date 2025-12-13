using System;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

public class NetworkManager : MonoBehaviour
{
    [SerializeField] private string serverIP = "127.0.0.1";
    [SerializeField] private int serverPort = 7777;

    private TcpClient _client;
    private NetworkStream _stream;

    private Thread _receiveThread;
    private bool _isRunning = true;

    void Start()
    {
        Connect();
    }

    private void Awake()
    {
        // Maestro Tip: 씬 전환 시 파괴되지 않고 영원히 생존하도록 설정
        DontDestroyOnLoad(gameObject);
    }

    private void Connect()
    {
        if (_client != null)
        {
            Debug.LogWarning("이미 서버에 연결 되어있습니다.");
            return;
        }
        try
        {
            _client = new TcpClient(serverIP, serverPort);
            _stream = _client.GetStream();

            Debug.Log($"M3 서버에 연결 성공! IP: {serverIP}:{serverPort}");
            SendLoginRequest("NewPlayer");

            _isRunning = true;
            _receiveThread = new Thread(ReceiveLoop);
            _receiveThread.IsBackground = true;
            _receiveThread.Start();
        }
        catch (Exception e)
        {
            Debug.LogError($"M3 서버 연결 실패: {e.Message}");
            _client = null;
        }
    }

    private void ReceiveLoop()
    {
        const int MAX_BUFFER_SIZE = 4096;
        byte[] receiveBuffer = new byte[MAX_BUFFER_SIZE];
        int bytesRead = 0;

        while (_isRunning && _client.Connected)
        {
            try
            {
                if (_stream.DataAvailable)
                {
                    bytesRead = _stream.Read(receiveBuffer, 0, receiveBuffer.Length);
                    if (bytesRead > 0)
                    {
                        Debug.Log($"[Recv] 서버로부터 {bytesRead} 바이트 수신.");
                    }
                }
                Thread.Sleep(1);
            }
            catch (Exception e)
            {
                if (_client.Connected)
                {
                    Debug.LogError($"수신 중 오류 발생: {e.Message}");

                }
                _isRunning = false;
            }
        }
        Debug.Log("수신 루프 종료.");
    }
    void OnDestroy()
    {
        _isRunning = false;
        if (_receiveThread != null && _receiveThread.IsAlive)
        {
            _receiveThread.Join(); // 스레드가 완전히 종료되기를 기다림
        }
        if (_stream != null) _stream.Close();
        if (_client != null) _client.Close();
        Debug.Log("네트워크 자원 정리 완료.");
    }
    public void SendPacket(byte[] packet)
    {
        if (_stream == null || _client == null || !_client.Connected)
        {
            Debug.LogError("서버에 연결되지 않아 패킷을 보낼 수 없습니다.");
            return;
        }
        try
        {
            _stream.Write(packet, 0, packet.Length);
            _stream.Flush();
            Debug.Log($"패킷 전송 완료. 길이: {packet.Length} 바이트");
        }
        catch (Exception e)
        {
            Debug.LogError($"패킷 전송 중 오류 발생: {e.Message}");
        }
    }

    private void SendLoginRequest(string userName)
    {
        byte[] loginPacket = MakeLoginPacket(userName);
        SendPacket(loginPacket);
        Debug.Log("ID 100 (Login Req) 패킷이 M3 서버로 전송되었습니다.");
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