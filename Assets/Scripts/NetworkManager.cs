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

    public bool IS_DUMMY_MODE = false; // 실제 서버 연결 시 false, 테스트 시 true (실제 서버 모드로 설정됨)
    private TcpClient _client;
    private NetworkStream _stream;

    private Thread _receiveThread;
    private bool _isRunning = true;

    internal Queue<byte[]> _packetQueue = new Queue<byte[]>();
    public string ConnectedUserName { get; private set; }
    public int ConnectedUserID { get; private set; } = -1; // 로그인한 사용자 ID
    public int CreatedRoomID { get; private set; } = -1; // 생성한 방 ID (방장 추적용)
    private int _pendingRoomID = -1; // 씬 전환 중인 방 ID (RoomManager 설정용)
    
    private void Update()
    {
        // 패킷 큐에서 패킷을 처리합니다.
        lock (_packetQueue)
        {
            if (_packetQueue.Count > 0)
            {
                int queueCount = _packetQueue.Count;
                byte[] packetData = _packetQueue.Dequeue();
                Debug.Log($"[Update] 패킷 처리 시작. 큐에 {queueCount}개 패킷 있음. 처리 중...");
                HandlePacket(packetData);
            }
        }
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

            // LobbyManager가 준비될 때까지 대기한 후 방 목록 요청
            StartCoroutine(WaitForLobbyManagerAndRequestRoomList());

            if (IS_DUMMY_MODE)
            {
                //  DUMMY MODE일 때만 ID 291 응답을 강제 주입
                // LobbyManager가 준비될 때까지 잠시 대기
                StartCoroutine(WaitForLobbyManagerAndProcessRoomList());
            }
        }
        else if (scene.buildIndex == 2) // GameScene
        {
            Debug.Log($"씬 로드 완료: {scene.name} (Build Index: {scene.buildIndex})");
            // GameScene 로드 시 GameManager의 StartGameLogic 호출
            StartCoroutine(WaitForGameManagerAndStartGame());
        }
        else if (scene.buildIndex == 3) // RoomScene
        {
            Debug.Log($"씬 로드 완료: {scene.name} (Build Index: {scene.buildIndex})");
            // RoomScene 로드 시 RoomManager가 준비될 때까지 대기한 후 방 ID 설정
            if (_pendingRoomID != -1)
            {
                StartCoroutine(WaitForRoomManagerAndSetRoomID(_pendingRoomID));
                _pendingRoomID = -1; // 사용 후 리셋
            }
        }
    }

    private IEnumerator WaitForRoomManagerAndSetRoomID(int roomID)
    {
        // RoomManager.Instance가 준비될 때까지 대기
        float timeout = 5f;
        float elapsed = 0f;
        while (RoomManager.Instance == null && elapsed < timeout)
        {
            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;
        }

        if (RoomManager.Instance == null)
        {
            Debug.LogError("[NetworkManager] RoomManager.Instance가 5초 내에 생성되지 않았습니다.");
            yield break;
        }

        Debug.Log($"[NetworkManager] RoomManager 준비 완료. 방 ID 설정: {roomID}, CreatedRoomID: {CreatedRoomID}");
        RoomManager.Instance.SetCurrentRoomID(roomID);
    }

    private IEnumerator WaitForLobbyManagerAndRequestRoomList()
    {
        // 더미 모드가 아닐 때만 실제 서버에 요청
        if (IS_DUMMY_MODE)
        {
            Debug.Log("[WaitForLobbyManagerAndRequestRoomList] 더미 모드이므로 방 목록 요청을 건너뜁니다.");
            yield break;
        }

        // LobbyManager.Instance가 준비될 때까지 대기
        float timeout = 5f;
        float elapsed = 0f;
        while (LobbyManager.Instance == null && elapsed < timeout)
        {
            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;
        }

        if (LobbyManager.Instance == null)
        {
            Debug.LogError("[NetworkManager] LobbyManager.Instance가 5초 내에 생성되지 않았습니다.");
            yield break;
        }

        Debug.Log("[NetworkManager] LobbyManager 준비 완료. 방 목록 요청 전송...");
        SendRoomListRequest(); // ID 290 요청은 실제 서버로 전송 시도
    }

    private IEnumerator WaitForLobbyManagerAndProcessRoomList()
    {
        // LobbyManager.Instance가 준비될 때까지 대기
        float timeout = 5f;
        float elapsed = 0f;
        while (LobbyManager.Instance == null && elapsed < timeout)
        {
            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;
        }

        if (LobbyManager.Instance == null)
        {
            Debug.LogError("[NetworkManager] LobbyManager.Instance가 5초 내에 생성되지 않았습니다.");
            yield break;
        }

        // ID 291 응답을 강제 주입
        byte[] dummyRoomListAns = MakeDummyRoomListResponsePacket();
        lock (_packetQueue)
        {
            _packetQueue.Enqueue(dummyRoomListAns);
        }
        Debug.Log("ID 291 (RoomList Ans) 더미 패킷 주입 완료.");
        
        // 즉시 처리
        ForceProcessPackets();
    }

    private IEnumerator WaitForGameManagerAndStartGame()
    {
        // GameManager.Instance가 준비될 때까지 대기
        float timeout = 5f;
        float elapsed = 0f;
        while (GameManager.Instance == null && elapsed < timeout)
        {
            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;
        }

        if (GameManager.Instance == null)
        {
            Debug.LogError("[NetworkManager] GameManager.Instance가 5초 내에 생성되지 않았습니다.");
            yield break;
        }

        // GameManager의 StartGameLogic 호출
        GameManager.Instance.StartGameLogic();
        Debug.Log("[NetworkManager] GameManager.StartGameLogic() 호출 완료.");
    }

    public void Connect()
    {
        // 이미 연결되어 있으면 재연결하지 않음
        if (IsConnected())
        {
            Debug.Log("[Connect] 이미 서버에 연결되어 있습니다.");
            return;
        }

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
            // 기존 연결 정리
            Disconnect();

            //  CASE 2: REAL SERVER MODE (실제 서버 연결 및 요청)
            Debug.Log($"[Connect] 서버 연결 시도 시작: {ip}:{port}");
            try
            {
                // 연결 타임아웃 설정 (5초)
                _client = new TcpClient();
                _client.ReceiveTimeout = 5000;
                _client.SendTimeout = 5000;
                
                Debug.Log($"[Connect] TcpClient 생성 완료. 연결 시도 중...");
                
                // 비동기 연결 시도 (타임아웃 5초)
                IAsyncResult result = _client.BeginConnect(ip, port, null, null);
                bool success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(5));
                
                if (!success)
                {
                    _client.Close();
                    throw new SocketException((int)SocketError.TimedOut);
                }
                
                _client.EndConnect(result);
                _stream = _client.GetStream();
                
                Debug.Log($"[Connect] ✅ 서버 연결 성공: {ip}:{port}");
                Debug.Log($"[Connect] 연결 상태 확인: Connected={_client.Connected}");

                // 1. 수신 스레드 시작: 서버 응답(ID 101)을 받기 위해 필요합니다.
                _isRunning = true;
                _receiveThread = new Thread(ReceiveLoop);
                _receiveThread.IsBackground = true; // 백그라운드 스레드로 설정
                _receiveThread.Start();
                Debug.Log("[Connect] 수신 스레드 시작 완료.");

                // 2. 로그인 요청(ID 100) 전송
                string tempUserName = "UnityClient_01";
                Debug.Log($"[Connect] 로그인 요청 전송 시작: UserName={tempUserName}");
                SendLoginRequest(tempUserName);
                Debug.Log("[Connect] 로그인 요청 전송 완료. 서버 응답 대기 중...");
            }
            catch (SocketException ex)
            {
                // 연결 실패 시 TitleScene에 머무르거나, 재접속 UI를 띄우는 것이 정상입니다.
                Debug.LogError($"[Connect] ❌ 서버 연결 실패 (SocketException): {ex.Message}");
                Debug.LogError($"[Connect] SocketError: {ex.SocketErrorCode}");
                Debug.LogError($"[Connect] 연결 시도한 주소: {ip}:{port}");
                Debug.LogError("[Connect] 서버가 실행 중인지 확인하세요.");
                
                if (_client != null)
                {
                    try { _client.Close(); } catch { }
                }
                _client = null;
                _stream = null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Connect] ❌ 서버 연결 실패 (Exception): {ex.GetType().Name}: {ex.Message}");
                Debug.LogError($"[Connect] 스택 트레이스: {ex.StackTrace}");
                
                if (_client != null)
                {
                    try { _client.Close(); } catch { }
                }
                _client = null;
                _stream = null;
            }
        }
    }

    private void Disconnect()
    {
        _isRunning = false;
        
        if (_receiveThread != null && _receiveThread.IsAlive)
        {
            _receiveThread.Join(1000); // 1초 대기
        }
        
        if (_stream != null)
        {
            try { _stream.Close(); } catch { }
            _stream = null;
        }
        
        if (_client != null)
        {
            try { _client.Close(); } catch { }
            _client = null;
        }
    }

    private void ReceiveLoop()
    {
        Debug.Log("[ReceiveLoop] 수신 루프 시작.");
        const int MAX_BUFFER_SIZE = 4096;
        byte[] receiveBuffer = new byte[MAX_BUFFER_SIZE];
        int bytesRead = 0;

        while (_isRunning && _client != null && _client.Connected)
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
                        lock (_packetQueue)
                        {
                            Debug.Log($"[Recv] 서버로부터 {bytesRead} 바이트 수신. 큐에 추가됨. 현재 큐 크기: {_packetQueue.Count}");
                        }
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
        Disconnect();
        Debug.Log("네트워크 자원 정리 완료.");
    }

    public void SendPacket(byte[] packet)
    {
        // 더미 모드에서는 실제 전송을 건너뜁니다.
        if (IS_DUMMY_MODE)
        {
            Debug.Log($"[DUMMY MODE] 패킷 전송 시뮬레이션. 길이: {packet.Length} 바이트");
            return;
        }

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
        lock (_packetQueue)
        {
            while (_packetQueue.Count > 0)
            {
                byte[] packetData = _packetQueue.Dequeue();
                HandlePacket(packetData);
            }
        }
    }

    private void HandlePacket(byte[] packetData)
    {
        try
        {
            PacketReader reader = new PacketReader(packetData);
            Packet header = reader.ReadHeader();

            Debug.Log($"[Handle] 패킷 ID: {header.MessageID}, 총 길이: {header.TotalLength}");
            switch (header.MessageID)
        {
            case 101:
                ProcessLoginResponse(reader);
                break;
            case 301:
                ProcessCreateRoomResponse(reader);
                break;
            case 291:
                ProcessRoomListResponse(reader);
                break;
            case 311:
                ProcessJoinRoomResponse(reader);
                break;
            case 312:
                ProcessUserEnterNotify(reader);
                break;
            case 315:
                // LeaveRoom Req는 클라이언트에서 보내는 요청이므로 응답 처리만 있음
                break;
            case 316:
                ProcessLeaveRoomResponse(reader);
                break;
            case 317:
                ProcessUserLeftNotify(reader);
                break;
            case 321:
                ProcessReadyNotify(reader);
                break;
            case 330:
                // GameStart Req는 클라이언트에서 보내는 요청이므로 응답 처리만 있음
                break;
            case 331:
                ProcessGameStartNotify(reader);
                break;
            case 501:
                ProcessPlayerMoveResponse(reader);
                break;
            case 701:
                ProcessDamageResponse(reader);
                break;
            default:
                Debug.LogWarning($"[Handle] 알 수 없는 패킷 ID: {header.MessageID}");
                break;
        }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Handle] 패킷 처리 중 오류 발생: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private void ProcessLoginResponse(PacketReader reader)
    {
        try
        {
            int result = reader.ReadInt32();
            Debug.Log($"[ProcessLoginResponse] 로그인 응답 수신: result={result}");
            
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
        catch (Exception ex)
        {
            Debug.LogError($"[ProcessLoginResponse] 로그인 응답 처리 중 오류: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private void ProcessCreateRoomResponse(PacketReader reader)
    {
        try
        {
            Debug.Log("[ProcessCreateRoomResponse] 방 생성 응답 처리 시작.");
            // ID 301: [NewRoomID (4 bytes, int)]만 있음
            int roomID = reader.ReadInt32();

            Debug.Log($"[ProcessCreateRoomResponse] 방 생성 성공! NewRoomID: {roomID}. 방장으로 설정됩니다.");
            
            // 방을 생성한 사람이 방장
            CreatedRoomID = roomID;
            Debug.Log($"[ProcessCreateRoomResponse] CreatedRoomID 설정 완료: {CreatedRoomID}");
            
            // 방 생성 성공 시 자동으로 방에 입장
            SendJoinRoomRequest(roomID);
            
            // 더미 모드일 경우 즉시 방 입장 응답 처리
            if (IS_DUMMY_MODE)
            {
                ForceProcessJoinRoomDummy(roomID);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ProcessCreateRoomResponse] 방 생성 응답 처리 중 오류: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private void ProcessRoomListResponse(PacketReader reader)
    {
        try
        {
            Debug.Log("ID 291 (RoomList Ans) 처리 시작.");
            List<RoomData> roomList = new List<RoomData>();
            int roomCount = reader.ReadInt32();
            Debug.Log($"[RoomList] 방 개수: {roomCount}");

            for (int i = 0; i < roomCount; i++)
            {
                int roomID = reader.ReadInt32();
                string roomName = reader.ReadUserName(20);
                int userCount = reader.ReadInt32();

                RoomData room = new RoomData(roomID, roomName, userCount);
                roomList.Add(room);

                Debug.Log($"[Room Info] ID: {roomID}, Name: {roomName}, Users: {userCount}");
            }

            Debug.Log($"총 {roomCount}개의 방 목록 처리 완료. LobbyManager.Instance 체크 중...");

            if (LobbyManager.Instance != null)
            {
                Debug.Log($"[RoomList] LobbyManager.Instance 발견. 방 목록 업데이트 중...");
                LobbyManager.Instance.UpdateRoomList(roomList);
            }
            else
            {
                Debug.LogWarning($"[RoomList] LobbyManager.Instance가 null입니다. 현재 씬: {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
                Debug.LogWarning("방 목록을 받았지만 LobbyScene이 아니거나 LobbyManager가 아직 초기화되지 않았습니다.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[RoomList] 방 목록 처리 중 오류 발생: {ex.Message}\n{ex.StackTrace}");
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

    public void SendRoomListRequest()
    {
        Debug.Log("[SendRoomListRequest] 방 목록 요청 전송 시작...");
        
        // 더미 모드 체크
        if (IS_DUMMY_MODE)
        {
            Debug.LogWarning("[SendRoomListRequest] 더미 모드에서는 실제 서버에 요청하지 않습니다.");
            return;
        }
        
        // 연결 상태 확인
        if (!IsConnected())
        {
            Debug.LogError("[SendRoomListRequest] 서버에 연결되지 않은 상태입니다. 방 목록을 요청할 수 없습니다.");
            Debug.LogError("[SendRoomListRequest] 연결 상태: _client=" + (_client != null ? "존재" : "null") + 
                          ", Connected=" + (_client != null ? _client.Connected.ToString() : "N/A"));
            Debug.LogError("[SendRoomListRequest] IS_DUMMY_MODE=" + IS_DUMMY_MODE);
            return;
        }
        
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
        const ushort MESSAGE_ID = 310;
        const ushort TOTAL_LENGTH = 4 + 4; // Header(4) + RoomID(4)

        PacketBuilder builder = new PacketBuilder();
        builder.WriteHeader(MESSAGE_ID, TOTAL_LENGTH);
        builder.WriteInt32(roomID);

        Debug.Log($"방 입장 요청 패킷 (ID 310) 생성 완료. 방 ID: {roomID}");
        return builder.GetPacket();
    }

    public void SendJoinRoomRequest(int roomID)
    {
        byte[] joinPacket = MakeJoinRoomPacket(roomID);
        SendPacket(joinPacket);
        Debug.Log($"ID 310 (Join Room Req) 패킷이 M3 서버로 전송되었습니다. RoomID: {roomID}");
    }
    private byte[] MakeDummyJoinRoomSuccessPacket(int roomID)
    {
        const ushort MESSAGE_ID = 311;
        const bool SUCCESS = true;
        const ushort TOTAL_LENGTH = 4 + 1 + 4; // Header + Success(bool) + RoomID

        PacketBuilder builder = new PacketBuilder();
        builder.WriteHeader(MESSAGE_ID, TOTAL_LENGTH);
        builder.WriteBoolean(SUCCESS);
        builder.WriteInt32(roomID);

        Debug.Log($"더미 패킷 생성 완료: ID 311 (방 입장 성공). RoomID: {roomID}");
        return builder.GetPacket();
    }

    private void ProcessJoinRoomResponse(PacketReader reader)
    {
        try
        {
            // ID 311: [Success (1 byte, bool)] + [RoomID (4 bytes, int)]
            bool success = reader.ReadBoolean();
            int roomID = reader.ReadInt32();

            if (success)
            {
                Debug.Log($"방 입장 성공! RoomID: {roomID}. Room 씬으로 이동합니다.");
                Debug.Log($"[ProcessJoinRoomResponse] CreatedRoomID: {CreatedRoomID}, 입장할 RoomID: {roomID}");
                
                // 씬 전환 전에 방 ID 저장 (씬 전환 후 RoomManager에 전달하기 위해)
                _pendingRoomID = roomID;
                
                // RoomScene으로 이동 (씬 인덱스 3)
                UnityEngine.SceneManagement.SceneManager.LoadScene(3);
            }
            else
            {
                Debug.LogError($"방 입장 실패! RoomID: {roomID}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ProcessJoinRoomResponse] 방 입장 응답 처리 중 오류: {ex.Message}\n{ex.StackTrace}");
        }
    }

    // ProcessRoomInfoResponse는 지침서에 없으므로 제거됨
    // 방 정보는 UserEnter Notify 등을 통해 업데이트됨

    private void ProcessReadyNotify(PacketReader reader)
    {
        try
        {
            // ID 321: [UserID (4 bytes, int)] + [IsReady (1 byte, bool)]
            int userID = reader.ReadInt32();
            bool isReady = reader.ReadBoolean();

            Debug.Log($"[Ready Notify] UserID: {userID}, IsReady: {isReady}");

            // RoomManager에 Ready 상태 업데이트 전달
            if (RoomManager.Instance != null)
            {
                RoomManager.Instance.UpdatePlayerReadyStatus(userID, isReady);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ProcessReadyNotify] Ready Notify 처리 중 오류: {ex.Message}\n{ex.StackTrace}");
        }
    }
    private byte[] MakeDummyGameStartPacket()
    {
        const ushort MESSAGE_ID = 331;
        const int FIRST_TURN_USER_ID = 1;
        const ushort TOTAL_LENGTH = 4 + 4; // Header + FirstTurnUserID

        PacketBuilder builder = new PacketBuilder();
        builder.WriteHeader(MESSAGE_ID, TOTAL_LENGTH);
        builder.WriteInt32(FIRST_TURN_USER_ID);

        Debug.Log($"더미 패킷 생성 완료: ID 331 (게임 시작 Notify). FirstTurnUserID: {FIRST_TURN_USER_ID}");
        return builder.GetPacket();
    }
    public bool IsConnected()
    {
        return _client != null && _client.Connected;
    }

    private void ProcessGameStartNotify(PacketReader reader)
    {
        try
        {
            // ID 331: [FirstTurnUserID (4 bytes, int)]
            int firstTurnUserID = reader.ReadInt32();

            Debug.Log($"[ProcessGameStartNotify] 게임 시작 Notify (ID 331) 수신. FirstTurnUserID: {firstTurnUserID}. GameScene으로 이동합니다.");

            // RoomManager의 플래그 리셋 (씬 전환 전)
            if (RoomManager.Instance != null)
            {
                // RoomManager에 플래그 리셋 메서드가 있다면 호출
                // 없어도 씬 전환 시 RoomManager가 새로 생성되므로 문제 없음
            }

            // 게임 씬으로 이동 (씬 인덱스 2)
            UnityEngine.SceneManagement.SceneManager.LoadScene(2);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ProcessGameStartNotify] GameStart Notify 처리 중 오류: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private void ProcessUserEnterNotify(PacketReader reader)
    {
        try
        {
            // ID 312: [UserID (4 bytes, int)] + [UserName (20 bytes, char array)]
            int userID = reader.ReadInt32();
            string userName = reader.ReadUserName(20);

            Debug.Log($"[UserEnter Notify] UserID: {userID}, UserName: {userName}");

            // RoomManager에 사용자 입장 알림 전달
            if (RoomManager.Instance != null)
            {
                RoomManager.Instance.OnUserEntered(userID, userName);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ProcessUserEnterNotify] UserEnter Notify 처리 중 오류: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private void ProcessLeaveRoomResponse(PacketReader reader)
    {
        try
        {
            // ID 316: [Success (1 byte, bool)]
            bool success = reader.ReadBoolean();

            if (success)
            {
                Debug.Log("[LeaveRoom] 방 나가기 성공. LobbyScene으로 이동합니다.");
                UnityEngine.SceneManagement.SceneManager.LoadScene(1);
            }
            else
            {
                Debug.LogError("[LeaveRoom] 방 나가기 실패!");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ProcessLeaveRoomResponse] LeaveRoom 응답 처리 중 오류: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private void ProcessUserLeftNotify(PacketReader reader)
    {
        try
        {
            // ID 317: [LeaverID (4 bytes, int)] + [NewHostID (4 bytes, int)]
            int leaverID = reader.ReadInt32();
            int newHostID = reader.ReadInt32();

            Debug.Log($"[UserLeft Notify] LeaverID: {leaverID}, NewHostID: {newHostID}");

            // RoomManager에 사용자 퇴장 알림 전달
            if (RoomManager.Instance != null)
            {
                RoomManager.Instance.OnUserLeft(leaverID, newHostID);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ProcessUserLeftNotify] UserLeft Notify 처리 중 오류: {ex.Message}\n{ex.StackTrace}");
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

    // Ready 요청 (ID 320)
    public byte[] MakeReadyRequestPacket(bool isReady)
    {
        const ushort MESSAGE_ID = 320;
        const ushort TOTAL_LENGTH = 4 + 1; // Header(4) + IsReady(1 byte, bool)

        PacketBuilder builder = new PacketBuilder();
        builder.WriteHeader(MESSAGE_ID, TOTAL_LENGTH);
        builder.WriteBoolean(isReady);

        Debug.Log($"Ready 요청 패킷 (ID 320) 생성 완료. IsReady: {isReady}");
        return builder.GetPacket();
    }

    public void SendReadyRequest(bool isReady)
    {
        byte[] readyPacket = MakeReadyRequestPacket(isReady);
        SendPacket(readyPacket);
        Debug.Log($"ID 320 (Ready Req) 패킷이 M3 서버로 전송되었습니다. IsReady: {isReady}");
    }

    // GameStart 요청 (ID 330)
    public byte[] MakeGameStartRequestPacket()
    {
        const ushort MESSAGE_ID = 330;
        const ushort TOTAL_LENGTH = 4; // Header(4) only, Body 없음

        PacketBuilder builder = new PacketBuilder();
        builder.WriteHeader(MESSAGE_ID, TOTAL_LENGTH);

        Debug.Log("게임 시작 요청 패킷 (ID 330) 생성 완료.");
        return builder.GetPacket();
    }

    public void SendGameStartRequest()
    {
        byte[] gameStartPacket = MakeGameStartRequestPacket();
        SendPacket(gameStartPacket);
        Debug.Log("ID 330 (GameStart Req) 패킷이 M3 서버로 전송되었습니다.");
    }

    // LeaveRoom 요청 (ID 315)
    public byte[] MakeLeaveRoomRequestPacket()
    {
        const ushort MESSAGE_ID = 315;
        const ushort TOTAL_LENGTH = 4; // Header(4) only, Body 없음

        PacketBuilder builder = new PacketBuilder();
        builder.WriteHeader(MESSAGE_ID, TOTAL_LENGTH);

        Debug.Log("방 나가기 요청 패킷 (ID 315) 생성 완료.");
        return builder.GetPacket();
    }

    public void SendLeaveRoomRequest()
    {
        byte[] leavePacket = MakeLeaveRoomRequestPacket();
        SendPacket(leavePacket);
        Debug.Log("ID 315 (LeaveRoom Req) 패킷이 M3 서버로 전송되었습니다.");
    }

    // 방 생성 요청 (ID 300)
    public byte[] MakeCreateRoomRequestPacket(string roomName)
    {
        const ushort MESSAGE_ID = 300;
        const int ROOM_NAME_LENGTH = 20;
        const ushort TOTAL_LENGTH = 4 + ROOM_NAME_LENGTH; // Header(4) + RoomName(20)

        PacketBuilder builder = new PacketBuilder();
        builder.WriteHeader(MESSAGE_ID, TOTAL_LENGTH);
        builder.WriteUserName(roomName, ROOM_NAME_LENGTH);

        Debug.Log($"방 생성 요청 패킷 (ID 300) 생성 완료. RoomName: {roomName}");
        return builder.GetPacket();
    }

    public void SendCreateRoomRequest(string roomName)
    {
        byte[] createPacket = MakeCreateRoomRequestPacket(roomName);
        SendPacket(createPacket);
        Debug.Log($"ID 300 (Create Room Req) 패킷이 M3 서버로 전송되었습니다. RoomName: {roomName}");
    }


    // 플레이어 Ready 상태 요청 (ID 320)

    // 더미 모드용 방 생성 응답
    private byte[] MakeDummyCreateRoomResponsePacket(int roomID, string roomName)
    {
        const ushort MESSAGE_ID = 301;
        const ushort TOTAL_LENGTH = 4 + 4; // Header + NewRoomID

        PacketBuilder builder = new PacketBuilder();
        builder.WriteHeader(MESSAGE_ID, TOTAL_LENGTH);
        builder.WriteInt32(roomID);

        Debug.Log($"더미 패킷 생성 완료: ID 301 (방 생성 성공). NewRoomID: {roomID}");
        return builder.GetPacket();
    }

    internal void ForceProcessCreateRoomDummy(string roomName)
    {
        if (!IS_DUMMY_MODE) return;

        // 더미 방 ID 생성
        int dummyRoomID = UnityEngine.Random.Range(1000, 9999);
        byte[] dummyCreateAns = MakeDummyCreateRoomResponsePacket(dummyRoomID, roomName);

        lock (_packetQueue)
        {
            _packetQueue.Enqueue(dummyCreateAns);
        }
        Debug.Log($"ID 281 (Create Room Ans, RoomID: {dummyRoomID}) 더미 패킷 주입 완료. (Force)");

        // 즉시 처리
        ForceProcessPackets();
    }

    // 더미 모드용 방 정보 응답
    private byte[] MakeDummyRoomInfoResponsePacket(int roomID, string roomName, int playerCount, int maxPlayers)
    {
        const ushort MESSAGE_ID = 311;
        const ushort TOTAL_LENGTH = 4 + 4 + 20 + 4 + 4; // Header + RoomID + RoomName + PlayerCount + MaxPlayers

        PacketBuilder builder = new PacketBuilder();
        builder.WriteHeader(MESSAGE_ID, TOTAL_LENGTH);
        builder.WriteInt32(roomID);
        builder.WriteUserName(roomName, 20);
        builder.WriteInt32(playerCount);
        builder.WriteInt32(maxPlayers);

        Debug.Log($"더미 패킷 생성 완료: ID 311 (방 정보). RoomID: {roomID}");
        return builder.GetPacket();
    }

    internal void ForceProcessRoomInfoDummy(int roomID, string roomName, int playerCount, int maxPlayers)
    {
        if (!IS_DUMMY_MODE) return;

        byte[] dummyInfoAns = MakeDummyRoomInfoResponsePacket(roomID, roomName, playerCount, maxPlayers);

        lock (_packetQueue)
        {
            _packetQueue.Enqueue(dummyInfoAns);
        }
        Debug.Log($"ID 311 (Room Info Ans) 더미 패킷 주입 완료. (Force)");

        ForceProcessPackets();
    }

    // 더미 모드용 플레이어 Ready 응답
    private byte[] MakeDummyPlayerReadyResponsePacket(List<PlayerReadyData> players)
    {
        const ushort MESSAGE_ID = 321;
        int playerCount = players.Count;
        ushort TOTAL_LENGTH = (ushort)(4 + 4 + (playerCount * (4 + 20 + 1))); // Header + Count + (ID + Name + Ready) * Count

        PacketBuilder builder = new PacketBuilder();
        builder.WriteHeader(MESSAGE_ID, TOTAL_LENGTH);
        builder.WriteInt32(playerCount);

        foreach (var player in players)
        {
            builder.WriteInt32(player.PlayerID);
            builder.WriteUserName(player.PlayerName, 20);
            builder.WriteBoolean(player.IsReady);
        }

        Debug.Log($"더미 패킷 생성 완료: ID 321 (플레이어 Ready). PlayerCount: {playerCount}");
        return builder.GetPacket();
    }

    internal void ForceProcessPlayerReadyDummy(List<PlayerReadyData> players)
    {
        if (!IS_DUMMY_MODE) return;

        byte[] dummyReadyAns = MakeDummyPlayerReadyResponsePacket(players);

        lock (_packetQueue)
        {
            _packetQueue.Enqueue(dummyReadyAns);
        }
        Debug.Log($"ID 321 (Player Ready Ans) 더미 패킷 주입 완료. (Force)");

        ForceProcessPackets();
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
    public void SendDamageReport(int targetID, float damage)
    {
        const ushort MESSAGE_ID = 700;
        const ushort TOTAL_LENGTH = 12; // Header(4) + TargetID(4) + Damage(4)

        PacketBuilder builder = new PacketBuilder();
        builder.WriteHeader(MESSAGE_ID, TOTAL_LENGTH);
        builder.WriteInt32(targetID);
        builder.WriteFloat(damage);

        SendPacket(builder.GetPacket());
        Debug.Log($"[Send] ID {targetID}에게 {damage} 데미지 보고 전송.");

        // --- 더미 모드 전용 로직 추가 ---
        // 서버가 없으므로, 보낸 내용을 그대로 '수신 패킷(ID 701)'인 것처럼 시뮬레이션합니다.
        StartCoroutine(SimulateDamageResponse(targetID, damage));
    }
    private IEnumerator SimulateDamageResponse(int targetID, float damage)
    {
        // 서버 왕복 시간을 고려해 아주 잠깐(0.1초) 대기
        yield return new WaitForSeconds(0.1f);

        // 701 패킷(서버 통보)을 받은 것과 동일한 효과를 줍니다.
        GameObject targetObj = PlayerManager.Instance.GetPlayerById(targetID);
        if (targetObj != null)
        {
            PlayerController pc = targetObj.GetComponent<PlayerController>();
            if (pc != null)
            {
                Debug.Log($"[Dummy Response] 서버로부터 ID {targetID}의 체력 차감 통보를 받음.");
                pc.TakeDamage(damage); // 이제 여기서 HP Bar가 줄어듭니다!
            }
        }
    }
    private void ProcessDamageResponse(PacketReader reader)
    {
        int targetID = reader.ReadInt32();
        float damage = reader.ReadFloat();

        Debug.Log($"[ID 701 수신] 대상: {targetID}, 데미지: {damage}");

        GameObject targetObj = PlayerManager.Instance.GetPlayerById(targetID);
        if (targetObj != null)
        {
            PlayerController pc = targetObj.GetComponent<PlayerController>();
            if (pc != null)
            {
                pc.TakeDamage(damage); // 여기가 실행되어야 HP Bar가 줄어듭니다.
            }
        }
        else
        {
            Debug.LogError($"[ID 701 에러] ID {targetID}에 해당하는 플레이어를 찾을 수 없습니다!");
        }
    }
}