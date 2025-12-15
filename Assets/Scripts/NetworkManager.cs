// NetworkManager.cs
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

    internal Queue<byte[]> _packetQueue = new Queue<byte[]>();

    private void Update()
    {
        // 빈 상태 유지
    }

    void Start()
    {
        Connect();

        // TitleScene에서는 ID 101 더미 패킷을 즉시 처리하여 Lobby 씬으로 이동합니다.
        if (_packetQueue.Count > 0)
        {
            Debug.Log("TitleScene: 로그인 더미 패킷을 즉시 처리합니다.");
            ForceProcessPackets();
        }
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

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.buildIndex == 1)
        {
            Debug.Log($"씬 로드 완료: {scene.name} (Build Index: {scene.buildIndex})");

            SendRoomListRequest();
            byte[] dummyRoomListAns = MakeDummyRoomListResponsePacket();
            _packetQueue.Enqueue(dummyRoomListAns);
            Debug.Log("ID 291 (RoomList Ans) 더미 패킷 주입 완료.");

            ForceProcessPackets();
            Debug.Log("[NetworkManager] ID 291 주입 및 LobbyManager에게 즉시 처리 요청 완료.");
        }
    }

    private void Connect()
    {
        try
        {
            _client = new TcpClient(serverIP, serverPort);
            _stream = _client.GetStream();

            _isRunning = true;
            _receiveThread = new Thread(ReceiveLoop);
            _receiveThread.IsBackground = true;
            _receiveThread.Start();
            Debug.Log($"M3 서버에 연결 성공! IP: {serverIP}:{serverPort}");
            SendLoginRequest("NewPlayer");
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
            _receiveThread.Join();
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

    // --- 패킷 처리 및 핸들링 ---

    public void ForceProcessPackets()
    {
        while (_packetQueue.Count > 0)
        {
            byte[] packetData = _packetQueue.Dequeue();
            HandlePacket(packetData);
        }
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
            case 301:
                ProcessJoinRoomResponse(reader);
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

    private void ProcessRoomListResponse(PacketReader reader)
    {
        Debug.Log("ID 291 (RoomList Ans) 처리 시작.");
        List<RoomData> roomList = new List<RoomData>();
        int roomCount = reader.ReadInt32();

        for (int i = 0; i < roomCount; i++)
        {
            int roomID = reader.ReadInt32();
            string roomName = reader.ReadUserName(20);
            int userCount = reader.ReadInt32();

            RoomData room = new RoomData(roomID, roomName, userCount);
            roomList.Add(room);

            Debug.Log($"[Room Info] ID: {roomID}, Name: {roomName}, Users: {userCount}");
        }

        Debug.Log($"총 {roomCount}개의 방 목록 처리 완료.");

        if (LobbyManager.Instance != null)
        {
            LobbyManager.Instance.UpdateRoomList(roomList);
        }
        else
        {
            Debug.LogError("LobbyManager.Instance가 할당되지 않아 방 목록을 UI에 전달할 수 없습니다. 스크립트 실행 순서를 확인하십시오.");
        }
    }

    // --- 패킷 생성 (더미 및 요청) ---
    private byte[] MakeDummyLoginSuccessPacket()
    {
        const ushort MESSAGE_ID = 101;
        const int RESULT_SUCCESS = 1;
        const ushort TOTAL_LENGTH = 4 + 4;

        PacketBuilder builder = new PacketBuilder();
        builder.WriteHeader(MESSAGE_ID, TOTAL_LENGTH);
        builder.WriteInt32(RESULT_SUCCESS);
        Debug.Log("더미 패킷 생성 완료: ID 101 (로그인 성공)");
        return builder.GetPacket();
    }

    private byte[] MakeDummyRoomListResponsePacket()
    {
        const ushort MESSAGE_ID = 291;
        const int ROOM_COUNT = 3;
        const ushort TOTAL_LENGTH = 4 + 4 + (28 * ROOM_COUNT);

        PacketBuilder builder = new PacketBuilder();
        builder.WriteHeader(MESSAGE_ID, TOTAL_LENGTH);
        builder.WriteInt32(ROOM_COUNT);

        builder.WriteInt32(1001);
        builder.WriteUserName("Room One", 20);
        builder.WriteInt32(1);

        builder.WriteInt32(1002);
        builder.WriteUserName("Test Room", 20);
        builder.WriteInt32(3);

        builder.WriteInt32(1003);
        builder.WriteUserName("Full Room", 20);
        builder.WriteInt32(5);

        Debug.Log($"더미 패킷 생성 완료: ID 291 (방 목록 응답, 방 {ROOM_COUNT}개)");
        return builder.GetPacket();
    }

    private void SendLoginRequest(string userName)
    {
        byte[] loginPacket = MakeLoginPacket(userName);
        SendPacket(loginPacket);
        Debug.Log("ID 100 (Login Req) 패킷이 M3 서버로 전송되었습니다.");
    }

    public byte[] MakeLoginPacket(string userName)
    {
        const int USER_NAME_LENGTH = 20;
        const ushort MESSAGE_ID = 100;
        const ushort TOTAL_LENGTH = 4 + USER_NAME_LENGTH;

        PacketBuilder builder = new PacketBuilder();
        builder.WriteHeader(MESSAGE_ID, TOTAL_LENGTH);
        builder.WriteUserName(userName, USER_NAME_LENGTH);
        Debug.Log($"로그인 패킷 (ID 100) 생성 완료. 총 길이: {TOTAL_LENGTH} 바이트.");
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

    public byte[] MakeJoinRoomPacket(int roomID)
    {
        const ushort MESSAGE_ID = 300;
        const ushort TOTAL_LENGTH = 4 + 4; // Header(4) + RoomID(4)

        PacketBuilder builder = new PacketBuilder();
        builder.WriteHeader(MESSAGE_ID, TOTAL_LENGTH);
        builder.WriteInt32(roomID);

        Debug.Log($"방 입장 요청 패킷 (ID 300) 생성 완료. 방 ID: {roomID}");
        return builder.GetPacket();
    }

    public void SendJoinRoomRequest(int roomID)
    {
        byte[] joinPacket = MakeJoinRoomPacket(roomID);
        SendPacket(joinPacket);
        Debug.Log($"ID 300 (Join Room Req) 패킷이 M3 서버로 전송되었습니다. RoomID: {roomID}");
    }
    public byte[] MakeDummyJoinRoomSuccessPacket(int roomID)
    {
        const ushort MESSAGE_ID = 301;
        const int RESULT_SUCCESS = 1;
        const ushort TOTAL_LENGTH = 4 + 4 + 4; // Header + Result + RoomID

        PacketBuilder builder = new PacketBuilder();
        builder.WriteHeader(MESSAGE_ID, TOTAL_LENGTH);
        builder.WriteInt32(RESULT_SUCCESS);
        builder.WriteInt32(roomID);

        Debug.Log($"더미 패킷 생성 완료: ID 301 (방 입장 성공). RoomID: {roomID}");
        return builder.GetPacket();
    }

    private void ProcessJoinRoomResponse(PacketReader reader)
    {
        int result = reader.ReadInt32();
        int roomID = reader.ReadInt32();

        if (result == 1)
        {
            Debug.Log($"방 입장 성공! RoomID: {roomID}. Game 씬으로 이동합니다.");

            UnityEngine.SceneManagement.SceneManager.LoadScene(2);
        }
        else
        {
            Debug.LogError($"방 입장 실패! RoomID: {roomID}, 결과 코드: {result}");
        }
    }

    public bool IsConnected()
    {
        return _client != null && _client.Connected;
    }
}