using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
public class NetworkManager : MonoBehaviour
{
    public static NetworkManager Instance { get; private set; }
    [SerializeField] private string serverIP = "127.0.0.1";
    [SerializeField] private int serverPort = 7777;

    private TcpClient _client;
    private NetworkStream _stream;

    private Thread _receiveThread;
    private bool _isRunning = true;

    private Queue<byte[]> _packetQueue = new Queue<byte[]>();
    private void Update()
    {
        while (_packetQueue.Count > 0)
        {
            byte[] packetData = _packetQueue.Dequeue();
            HandlePacket(packetData);
        }
    }
    void Start()
    {
        Connect();
    }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;
        }
        else
        {
            Destroy(gameObject);
        }

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
            Debug.Log("서버 연결 실패. 로그인 성공 패킷(ID 101)을 강제 주입하여 로직을 테스트합니다.");

            byte[] dummyPacket = MakeDummyLoginSuccessPacket();

            lock (_packetQueue)
            {
                _packetQueue.Enqueue(dummyPacket);

            }
            Debug.Log("더미 패킷 주입 완료. Update()에서 처리될 예정입니다.");
            return;
        }
    }
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // 로비 씬의 빌드 인덱스가 1번이라고 가정합니다.
        if (scene.buildIndex == 1)
        {
            Debug.Log($"씬 로드 완료: {scene.name} (Build Index: {scene.buildIndex})");
            // 로비 씬에 도착하면 방 목록 요청을 보냅니다.
            SendRoomListRequest();
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
                        byte[] processedData = new byte[bytesRead];
                        Array.Copy(receiveBuffer, processedData, bytesRead);
                        lock (_packetQueue)
                        {
                            _packetQueue.Enqueue(processedData);
                        }
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
        SceneManager.sceneLoaded -= OnSceneLoaded;
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

    private void HandlePacket(byte[] packetData)
    {
        PacketReader reader = new PacketReader(packetData);
        Packet header = reader.ReadHeader();

        Debug.Log($"[Handle] 패킷 ID: {header.MessageID}, 총 길이: {header.TotalLength}");
        switch (header.MessageID)
        {
            case 101:
                ProcessLoginResponse(reader);
                break;
            case 291:
                ProcessRoomListResponse(reader);
                break;
        }
    }

    private void ProcessLoginResponse(PacketReader reader)
    {
        int result = reader.ReadInt32();
        if (result == 1)
        {
            Debug.Log("로그인 성공! Lobby 씬으로 이동합니다.");
            UnityEngine.SceneManagement.SceneManager.LoadScene(1);
        }
        else
        {
            Debug.LogError($"로그인 실패! 결과 코드: {result}");
        }
    }

    // NetworkManager.cs 내부
    private void ProcessRoomListResponse(PacketReader reader)
    {
        Debug.Log("ID 291 (RoomList Ans) 처리 시작.");

        // 1. 방 목록을 담을 리스트를 선언합니다.
        List<RoomData> roomList = new List<RoomData>(); // <-- List 선언

        // 2. 방의 개수를 먼저 읽습니다.
        int roomCount = reader.ReadInt32();

        // 3. N개의 방 정보를 반복하여 읽습니다.
        for (int i = 0; i < roomCount; i++)
        {
            int roomID = reader.ReadInt32(); // RoomID (4 bytes)
            string roomName = reader.ReadUserName(20); // RoomName (20 bytes)
            int userCount = reader.ReadInt32(); // CurrentUserCount (4 bytes)

            // 4. 읽은 데이터를 RoomData 인스턴스로 만들고 리스트에 추가
            RoomData room = new RoomData(roomID, roomName, userCount);
            roomList.Add(room);

            Debug.Log($"[Room Info] ID: {roomID}, Name: {roomName}, Users: {userCount}");
        }

        Debug.Log($"총 {roomCount}개의 방 목록 처리 완료.");

        // 5. 해석된 데이터를 LobbyManager에게 전달 (최종 목표 달성!)
        if (LobbyManager.Instance != null)
        {
            // LobbyManager의 UpdateRoomList 함수를 호출하여 리스트를 넘겨줍니다.
            LobbyManager.Instance.UpdateRoomList(roomList);
        }
        else
        {
            // 로비 씬에 LobbyManager가 없을 경우 (디버깅용)
            Debug.LogError("LobbyManager.Instance가 씬에 존재하지 않아 방 목록을 UI에 전달할 수 없습니다.");
        }
    }
    private byte[] MakeDummyLoginSuccessPacket()
    {
        const ushort MESSAGE_ID = 101;
        const int RESULT_SUCCESS = 1; // 성공 코드

        // 전체 길이 = 헤더(4byte) + 바디(4byte)
        const ushort TOTAL_LENGTH = 4 + 4;

        PacketBuilder builder = new PacketBuilder();

        // 1. 헤더 쓰기 (ID 101, Length 8)
        builder.WriteHeader(MESSAGE_ID, TOTAL_LENGTH);

        // 2. 바디 쓰기 (Result = 1)
        builder.WriteInt32(RESULT_SUCCESS); // PacketBuilder에 추가된 메서드를 사용!

        Debug.Log("더미 패킷 생성 완료: ID 101 (로그인 성공)");

        return builder.GetPacket();
    }

    private void SendRoomListRequest()
    {
        byte[] roomListPacket = MakeRoomListRequestPacket();
        SendPacket(roomListPacket);
        Debug.Log("ID 290 (RoomList Req) 패킷이 M3 서버로 전송되었습니다.");
    }

    private byte[] MakeRoomListRequestPacket()
    {
        const ushort MESSAGE_ID = 290;
        const ushort TOTAL_LENGTH = 4;
        PacketBuilder builder = new PacketBuilder();

        builder.WriteHeader(MESSAGE_ID, TOTAL_LENGTH);
        Debug.Log($"방 목록 요청 패킷 (ID 290) 생성 완료. 총 길이: {TOTAL_LENGTH} 바이트.");

        return builder.GetPacket();
    }

}