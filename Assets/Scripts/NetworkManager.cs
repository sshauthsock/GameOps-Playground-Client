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

    public bool IS_DUMMY_MODE = true; // 실제 서버 연결 시 false, 테스트 시 true
    private TcpClient _client;
    private NetworkStream _stream;

    private Thread _receiveThread;
    private bool _isRunning = true;

    internal Queue<byte[]> _packetQueue = new Queue<byte[]>();
    public string ConnectedUserName { get; private set; }
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
        if (scene.buildIndex == 1) // LobbyScene
        {
            Debug.Log($"씬 로드 완료: {scene.name} (Build Index: {scene.buildIndex})");

            SendRoomListRequest(); // ID 290 요청은 실제 서버로 전송 시도

            if (IS_DUMMY_MODE)
            {
                //  DUMMY MODE일 때만 ID 291 응답을 강제 주입
                byte[] dummyRoomListAns = MakeDummyRoomListResponsePacket();
                _packetQueue.Enqueue(dummyRoomListAns);
                Debug.Log("ID 291 (RoomList Ans) 더미 패킷 주입 완료.");

                ForceProcessPackets();
                Debug.Log("[NetworkManager] ID 291 주입 및 LobbyManager에게 즉시 처리 요청 완료.");
            }
        }
    }

    public void Connect()
    {
        string ip = serverIP;
        int port = serverPort;

        if (IS_DUMMY_MODE)
        {
            //  CASE 1: DUMMY MODE (연결 실패 가정 및 더미 로직 강제 실행)
            Debug.Log("DUMMY MODE 활성화. 서버 연결을 시도하지 않고 더미 로그인 로직을 강제 실행합니다.");

            // 1. DUMMY는 연결이 성공했다고 가정하고, 수신 스레드는 시작하지 않습니다.
            // 2. 로그인 응답(ID 101) 더미 패킷을 강제 주입하여 TitleScene을 통과시킵니다.
            ForceProcessLoginDummy();
        }
        else
        {
            //  CASE 2: REAL SERVER MODE (실제 서버 연결 및 요청)
            try
            {
                _client = new TcpClient(ip, port);
                _stream = _client.GetStream();
                Debug.Log($"M3 서버 연결 성공: {ip}:{port}");

                // 1. 수신 스레드 시작: 서버 응답(ID 101)을 받기 위해 필요합니다.
                _receiveThread = new Thread(ReceiveLoop);
                _receiveThread.Start();

                // 2. 로그인 요청(ID 100) 전송
                string tempUserName = "UnityClient_01";
                SendLoginRequest(tempUserName);
            }
            catch (SocketException ex)
            {
                // 연결 실패 시 TitleScene에 머무르거나, 재접속 UI를 띄우는 것이 정상입니다.
                Debug.LogError($"M3 서버 연결 실패: {ex.Message}");
            }
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
            case 401:
                ProcessGameStartResponse(reader);
                break;
            case 501:
                ProcessPlayerMoveResponse(reader);
                break;
        }
    }

    private void ProcessLoginResponse(PacketReader reader)
    {
        int result = reader.ReadInt32();
        if (result == 1)
        {
            Debug.Log("로그인 성공! Lobby 씬으로 이동합니다.");
            ConnectedUserName = "UnityClient_01";
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

    public void SendLoginRequest(string userName)
    {
        const ushort MESSAGE_ID = 100;
        // 패킷 길이 계산: Header(4) + String 길이(4) + String 데이터(가변)
        int stringLength = System.Text.Encoding.UTF8.GetByteCount(userName);
        ushort TOTAL_LENGTH = (ushort)(4 + 4 + stringLength);

        PacketBuilder builder = new PacketBuilder();
        builder.WriteHeader(MESSAGE_ID, TOTAL_LENGTH);
        builder.WriteString(userName); //  이 userName이 패킷에 담겨야 합니다.

        byte[] loginPacket = builder.GetPacket();
        SendPacket(loginPacket);
        Debug.Log($"ID 100 (로그인 요청) 패킷 전송 완료. User: {userName}");
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
    private byte[] MakeDummyJoinRoomSuccessPacket(int roomID)
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
    private byte[] MakeDummyGameStartPacket()
    {
        const ushort MESSAGE_ID = 401;
        const int RESULT_SUCCESS = 1;
        const ushort TOTAL_LENGTH = 4 + 4; // Header + Result

        PacketBuilder builder = new PacketBuilder();
        builder.WriteHeader(MESSAGE_ID, TOTAL_LENGTH);
        builder.WriteInt32(RESULT_SUCCESS);

        Debug.Log($"더미 패킷 생성 완료: ID 401 (게임 시작 성공).");
        return builder.GetPacket();
    }
    public bool IsConnected()
    {
        return _client != null && _client.Connected;
    }

    private void ProcessGameStartResponse(PacketReader reader)
    {
        int result = reader.ReadInt32();

        if (result == 1)
        {
            Debug.Log("게임 시작 응답 (ID 401) 성공. GameManager의 StartGameLogic 호출.");

            //  GameManager의 StartGameLogic() 호출
            if (GameManager.Instance != null)
            {
                GameManager.Instance.StartGameLogic();
            }
            else
            {
                Debug.LogError("GameManager.Instance가 할당되지 않았습니다. GameScene 설정을 확인하십시오.");
            }
        }
        else
        {
            Debug.LogError($"게임 시작 실패! 결과 코드: {result}");
        }
    }

    //  로그인 더미 응답 처리를 강제하는 함수
    internal void ForceProcessLoginDummy()
    {
        if (!IS_DUMMY_MODE) return;

        byte[] dummyLoginAns = MakeDummyLoginSuccessPacket(); // private 함수 호출

        lock (_packetQueue)
        {
            _packetQueue.Enqueue(dummyLoginAns);
        }
        Debug.Log("ID 101 (로그인 성공) 더미 패킷 주입 완료. (Force)");

        ForceProcessPackets();
    }
    //  인게임 더미 응답 처리를 강제하는 함수
    internal void ForceProcessGameStartDummy()
    {
        if (!IS_DUMMY_MODE) return;

        byte[] dummyStartAns = MakeDummyGameStartPacket(); // private 함수 호출

        lock (_packetQueue)
        {
            _packetQueue.Enqueue(dummyStartAns);
        }
        Debug.Log("ID 401 (Game Start Ans) 더미 패킷 주입 완료. (Force)");

        ForceProcessPackets();
    }

    internal void ForceProcessJoinRoomDummy(int roomID)
    {
        if (!IS_DUMMY_MODE) return;

        byte[] dummyJoinAns = MakeDummyJoinRoomSuccessPacket(roomID); // private 함수 호출

        lock (_packetQueue)
        {
            _packetQueue.Enqueue(dummyJoinAns);
        }
        Debug.Log($"ID 301 (Join Room Ans, RoomID: {roomID}) 더미 패킷 주입 완료. (Force)");

        ForceProcessPackets();
    }

    public byte[] MakeGameReadyRequestPacket()
    {
        const ushort MESSAGE_ID = 400;
        const ushort TOTAL_LENGTH = 4; // Header(4)

        PacketBuilder builder = new PacketBuilder();
        builder.WriteHeader(MESSAGE_ID, TOTAL_LENGTH);

        Debug.Log("게임 준비 요청 패킷 (ID 400) 생성 완료.");
        return builder.GetPacket();
    }

    public void SendGameReadyRequest()
    {
        byte[] readyPacket = MakeGameReadyRequestPacket();
        SendPacket(readyPacket);
        Debug.Log("ID 400 (Game Ready Req) 패킷이 M3 서버로 전송되었습니다.");
    }
    public byte[] MakeMoveRequestPacket(Vector3 position, Quaternion rotation)
    {
        const ushort MESSAGE_ID = 500;
        // Header(4) + ID(4) + Pos(12) + Rot(16) = 총 36바이트
        const ushort TOTAL_LENGTH = 36;

        PacketBuilder builder = new PacketBuilder();
        builder.WriteHeader(MESSAGE_ID, TOTAL_LENGTH);
        builder.WriteInt32(PlayerManager.Instance.MyPlayerID); // ID 포함

        builder.WriteFloat(position.x);
        builder.WriteFloat(position.y);
        builder.WriteFloat(position.z);

        builder.WriteFloat(rotation.x);
        builder.WriteFloat(rotation.y);
        builder.WriteFloat(rotation.z);
        builder.WriteFloat(rotation.w);

        return builder.GetPacket();
    }

    public void SendMoveRequest(Vector3 position, Quaternion rotation)
    {
        byte[] movePacket = MakeMoveRequestPacket(position, rotation);
        SendPacket(movePacket);
        // 주석 처리: 이동 요청은 초당 여러 번 발생하므로, 로그를 너무 자주 출력하면 성능에 영향
        Debug.Log($"ID 500 (Move Req) 패킷이 M3 서버로 전송되었습니다. Pos: {position}");
    }
    private void ProcessPlayerMoveResponse(PacketReader reader)
    {
        int playerID = reader.ReadInt32();

        // 위치 3개
        Vector3 position = new Vector3(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat());

        // 회전 4개
        float rx = reader.ReadFloat();
        float ry = reader.ReadFloat();
        float rz = reader.ReadFloat();
        float rw = reader.ReadFloat();
        Quaternion rotation = new Quaternion(rx, ry, rz, rw);

        //  로그를 찍어 r.w 값이 0이 아닌지 확인해보세요. 보통 정상적인 값은 0~1 사이입니다.
        Debug.Log($"[수신] ID: {playerID}, Pos: {position}, RotW: {rw}");

        PlayerManager.Instance.UpdatePlayerPosition(playerID, position, rotation);

        GameObject playerObj = PlayerManager.Instance.GetPlayerById(playerID);
        if (playerObj != null)
        {
            PlayerController pc = playerObj.GetComponent<PlayerController>();
            if (pc != null)
            {
                // [중요] PlayerManager의 MyPlayerID와 패킷의 playerID를 비교합니다.
                bool isLocal = (playerID == PlayerManager.Instance.MyPlayerID);
                pc.SetPlayerID(playerID, isLocal);
            }
        }
    }
    public void ForceProcessMoveDummy(int playerID, Vector3 position, Quaternion rotation)
    {
        const ushort MESSAGE_ID = 501;
        const ushort TOTAL_LENGTH = 36; //  반드시 36이어야 합니다.

        PacketBuilder builder = new PacketBuilder();
        builder.WriteHeader(MESSAGE_ID, TOTAL_LENGTH);
        builder.WriteInt32(playerID); // (4)

        builder.WriteFloat(position.x); // (4)
        builder.WriteFloat(position.y); // (4)
        builder.WriteFloat(position.z); // (4)

        //  회전값 4개를 정확히 다 쓰는지 확인
        builder.WriteFloat(rotation.x); // (4)
        builder.WriteFloat(rotation.y); // (4)
        builder.WriteFloat(rotation.z); // (4)
        builder.WriteFloat(rotation.w); // (4) 👈 특히 이 w값이 누락되면 큐브가 뒤틀립니다.

        byte[] movePacket = builder.GetPacket();
        lock (_packetQueue)
        {
            _packetQueue.Enqueue(movePacket);
        }
        ForceProcessPackets();
    }
}