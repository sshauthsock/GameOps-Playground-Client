// NetworkManager.cs
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.SceneManagement;

[System.Serializable]
public class ServerConfig
{
    public string serverIP;
    public int serverPort;
    public string description;
}

public class NetworkManager : MonoBehaviour
{
    public static NetworkManager Instance { get; private set; }
    // [SerializeField] private string serverIP = "127.0.0.1";
    [SerializeField] private string serverIP = "gameops-playground-server-production.up.railway.app";

    [SerializeField] private int serverPort = 7777;
    
#if UNITY_WEBGL && !UNITY_EDITOR && false
    // WebGL JavaScript 플러그인 함수 (일시적으로 비활성화 - 빌드 오류 해결)
    // Unity가 .jslib 파일을 인식하지 못할 때는 이 부분을 false로 설정
    [DllImport("__Internal")]
    private static extern IntPtr GetServerIP();
    
    [DllImport("__Internal")]
    private static extern int GetServerPort();
#endif

    public bool IS_DUMMY_MODE = false; // 실제 서버 연결 시 false, 테스트 시 true (실제 서버 모드로 설정됨)
    
#if UNITY_WEBGL && !UNITY_EDITOR
    // WebGL: WebSocket 사용
    [DllImport("__Internal")]
    private static extern IntPtr WebSocket_Connect(string url, IntPtr onOpen, IntPtr onMessage, IntPtr onError, IntPtr onClose);
    
    [DllImport("__Internal")]
    private static extern int WebSocket_Send(IntPtr wsId, IntPtr data, int length);
    
    [DllImport("__Internal")]
    private static extern int WebSocket_GetReadyState(IntPtr wsId);
    
    [DllImport("__Internal")]
    private static extern int WebSocket_Close(IntPtr wsId);
    
    [DllImport("__Internal")]
    private static extern void WebSocket_FreeString(IntPtr ptr);
    
    private string _webSocketId = null;
    private IntPtr _webSocketIdPtr = IntPtr.Zero;
    private System.Action _onWebSocketOpen;
    private System.Action<IntPtr, int> _onWebSocketMessage;
    private System.Action _onWebSocketError;
    private System.Action<int> _onWebSocketClose;
    
    // WebSocket 메시지 수신 콜백
    [AOT.MonoPInvokeCallback(typeof(System.Action<IntPtr, int>))]
    private static void OnWebSocketMessage(IntPtr dataPtr, int length)
    {
        if (Instance != null)
        {
            Instance.HandleWebSocketMessage(dataPtr, length);
        }
    }
    
    private void HandleWebSocketMessage(IntPtr dataPtr, int length)
    {
        byte[] packet = new byte[length];
        Marshal.Copy(dataPtr, packet, 0, length);
        
        lock (_packetQueue)
        {
            _packetQueue.Enqueue(packet);
        }
    }
    
    // WebSocket 열림 콜백
    [AOT.MonoPInvokeCallback(typeof(System.Action))]
    private static void OnWebSocketOpen()
    {
        if (Instance != null)
        {
            Instance.HandleWebSocketOpen();
        }
    }
    
    private void HandleWebSocketOpen()
    {
        Debug.Log($"[Connect] ✅ WebSocket 연결 성공: wss://{serverIP}:{serverPort}");
        
        // 로그인 요청 전송
        string loginUserName = !string.IsNullOrEmpty(_myUniqueUserName) ? _myUniqueUserName : $"Client_{System.DateTime.Now.Ticks % 100000}";
        SendLoginRequest(loginUserName);
    }
    
    // WebSocket 오류 콜백
    [AOT.MonoPInvokeCallback(typeof(System.Action))]
    private static void OnWebSocketError()
    {
        if (Instance != null)
        {
            Instance.HandleWebSocketError();
        }
    }
    
    private void HandleWebSocketError()
    {
        Debug.LogError("[Connect] ❌ WebSocket 연결 오류");
        _webSocketId = null;
        if (_webSocketIdPtr != IntPtr.Zero)
        {
            WebSocket_FreeString(_webSocketIdPtr);
            _webSocketIdPtr = IntPtr.Zero;
        }
    }
    
    // WebSocket 종료 콜백
    [AOT.MonoPInvokeCallback(typeof(System.Action<int>))]
    private static void OnWebSocketClose(int code)
    {
        if (Instance != null)
        {
            Instance.HandleWebSocketClose(code);
        }
    }
    
    private void HandleWebSocketClose(int code)
    {
        Debug.Log($"[Connect] WebSocket 연결 종료 (코드: {code})");
        _webSocketId = null;
        if (_webSocketIdPtr != IntPtr.Zero)
        {
            WebSocket_FreeString(_webSocketIdPtr);
            _webSocketIdPtr = IntPtr.Zero;
        }
    }
#else
    // Editor: TCP 소켓 사용
    private TcpClient _client;
    private NetworkStream _stream;
#endif

    private Thread _receiveThread;
    private bool _isRunning = true;

    internal Queue<byte[]> _packetQueue = new Queue<byte[]>();
    private List<byte> _receiveBuffer = new List<byte>(); // 누적 버퍼 (불완전한 패킷 보관)
    public string ConnectedUserName { get; private set; }
    public int ConnectedUserID { get; set; } = -1; // 로그인한 사용자 ID (서버에서 받은 UserID) - set을 public으로 변경하여 GameManager에서 설정 가능
    public string _myUniqueUserName; // 각 클라이언트마다 고유한 userName (public으로 변경하여 GameManager에서 접근 가능)
    public int CreatedRoomID { get; private set; } = -1; // 생성한 방 ID (방장 추적용)
    public int PendingRoomID { get; private set; } = -1; // 씬 전환 중인 방 ID (RoomManager 설정용)
    public string PendingRoomName { get; private set; } = null; // 씬 전환 중인 방 이름 (RoomManager 설정용)
    private int _currentTurnPlayerID = -1; // 현재 턴 플레이어 ID
    public List<PlayerInfo> _gamePlayerList = new List<PlayerInfo>(); // 게임 시작 시 플레이어 목록
    private int _firstUserEnterID = -1; // 첫 번째 UserEnter Notify에서 받은 UserID (임시 식별용)
    private bool _hasReceivedUserEnter = false; // UserEnter Notify를 받았는지 여부
    
    private void Update()
    {
        // 패킷 큐에서 패킷을 처리합니다.
        lock (_packetQueue)
        {
            // 연결 상태 주기적 확인 (1초마다)
            if (Time.frameCount % 60 == 0) // 대략 1초마다 (60fps 가정)
            {
                bool isConnected = IsConnected();
                if (!isConnected && !IS_DUMMY_MODE)
                {
                    Debug.LogWarning($"[Update] ⚠️ 서버 연결이 끊어졌습니다! Connected: {isConnected}");
                }
            }

            // 패킷 큐가 많이 쌓였을 때 한 번에 여러 개 처리 (최대 10개)
            int maxProcessPerFrame = _packetQueue.Count > 10 ? 10 : _packetQueue.Count;
            for (int i = 0; i < maxProcessPerFrame && _packetQueue.Count > 0; i++)
            {
                int queueCount = _packetQueue.Count;
                byte[] packetData = _packetQueue.Dequeue();
                if (i == 0) // 첫 번째 패킷만 로그 출력
                {
                    Debug.Log($"[Update] 패킷 처리 시작. 큐에 {queueCount}개 패킷 있음. 처리 중...");
                }
                HandlePacket(packetData);
            }
            
            // 패킷 큐가 비어있을 때 주기적으로 상태 확인
            if (_packetQueue.Count == 0 && Time.frameCount % 300 == 0 && UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex == 2)
            {
                // 게임 씬에서 5초마다 패킷 큐 상태 확인
                Debug.Log($"[Update] 게임 씬 - 패킷 큐 상태: {_packetQueue.Count}개, 연결 상태: {(IsConnected() ? "연결됨" : "끊김")}, _currentTurnPlayerID: {_currentTurnPlayerID}");
            }
        }
    }

    void Start()
    {
        // 서버 설정 로드 (Editor와 WebGL 모두)
#if UNITY_WEBGL && !UNITY_EDITOR
        LoadWebGLServerConfig();
#else
        // Unity Editor에서도 설정 파일 로드 (선택사항)
        LoadServerConfigForEditor();
#endif
        
        // 각 클라이언트마다 고유한 userName 생성 (타임스탬프 + 랜덤 + 프로세스 ID)
        // 더 고유성을 보장하기 위해 System.Diagnostics.Process.GetCurrentProcess().Id 추가
        long ticks = System.DateTime.Now.Ticks;
        int random = UnityEngine.Random.Range(1000, 9999);
        int processId = System.Diagnostics.Process.GetCurrentProcess().Id;
        _myUniqueUserName = $"Client_{ticks}_{random}_{processId}";
        ConnectedUserName = _myUniqueUserName;
        Debug.Log($"[NetworkManager] 고유 UserName 생성: {_myUniqueUserName}");
        
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
            if (PendingRoomID != -1)
            {
                StartCoroutine(WaitForRoomManagerAndSetRoomID(PendingRoomID));
                PendingRoomID = -1; // 사용 후 리셋
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
        
        // 방 이름이 있으면 설정
        if (!string.IsNullOrEmpty(PendingRoomName))
        {
            RoomManager.Instance.UpdateRoomInfo(roomID, PendingRoomName, 0, 5);
            PendingRoomName = null; // 사용 후 리셋
        }
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

#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL: WebSocket 사용
            // HTTPS 페이지에서는 wss://를 사용해야 함 (Mixed Content 정책)
            // GitHub Pages는 HTTPS이므로 항상 wss:// 사용
            // Railway HTTP 서비스는 포트 번호 불필요
            string protocol = "wss://";
            string wsUrl = port > 0 ? $"{protocol}{ip}:{port}" : $"{protocol}{ip}";
            Debug.Log($"[Connect] WebSocket 연결 시도 시작: {wsUrl}");
            try
            {
                
                // 콜백 함수 설정
                _onWebSocketOpen = OnWebSocketOpen;
                _onWebSocketMessage = OnWebSocketMessage;
                _onWebSocketError = OnWebSocketError;
                _onWebSocketClose = OnWebSocketClose;
                
                IntPtr onOpenPtr = Marshal.GetFunctionPointerForDelegate(_onWebSocketOpen);
                IntPtr onMessagePtr = Marshal.GetFunctionPointerForDelegate(_onWebSocketMessage);
                IntPtr onErrorPtr = Marshal.GetFunctionPointerForDelegate(_onWebSocketError);
                IntPtr onClosePtr = Marshal.GetFunctionPointerForDelegate(_onWebSocketClose);
                
                // WebSocket 연결 생성
                _webSocketIdPtr = WebSocket_Connect(wsUrl, onOpenPtr, onMessagePtr, onErrorPtr, onClosePtr);
                
                if (_webSocketIdPtr == IntPtr.Zero)
                {
                    throw new Exception("WebSocket 생성 실패");
                }
                
                // WebSocket ID 문자열 저장
                _webSocketId = Marshal.PtrToStringAnsi(_webSocketIdPtr);
                
                Debug.Log($"[Connect] WebSocket 생성 완료. 연결 대기 중... (ID: {_webSocketId})");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Connect] ❌ WebSocket 연결 실패: {ex.Message}");
                Debug.LogError($"[Connect] 스택 트레이스: {ex.StackTrace}");
                
                _webSocketId = null;
                if (_webSocketIdPtr != IntPtr.Zero)
                {
                    WebSocket_FreeString(_webSocketIdPtr);
                    _webSocketIdPtr = IntPtr.Zero;
                }
            }
#else
            // Editor: 기존 TCP 소켓 사용
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
                Debug.Log($"[Connect] LocalEndPoint: {(_client.Client.LocalEndPoint?.ToString() ?? "null")}");
                Debug.Log($"[Connect] RemoteEndPoint: {(_client.Client.RemoteEndPoint?.ToString() ?? "null")}");

                // 1. 수신 스레드 시작: 서버 응답(ID 101)을 받기 위해 필요합니다.
                _isRunning = true;
                _receiveThread = new Thread(ReceiveLoop);
                _receiveThread.IsBackground = true; // 백그라운드 스레드로 설정
                _receiveThread.Start();
                Debug.Log("[Connect] 수신 스레드 시작 완료.");

                // 2. 로그인 요청(ID 100) 전송
                // _myUniqueUserName이 Start()에서 설정되어 있음
                string loginUserName = !string.IsNullOrEmpty(_myUniqueUserName) ? _myUniqueUserName : $"Client_{System.DateTime.Now.Ticks % 100000}";
                Debug.Log($"[Connect] 로그인 요청 전송 시작: UserName={loginUserName}");
                Debug.Log($"[Connect] ⚠️⚠️⚠️ 서버로 ID 100 (로그인 요청) 패킷 전송 예정...");
                SendLoginRequest(loginUserName);
                Debug.Log("[Connect] ✅✅✅ 로그인 요청(ID 100) 전송 완료!");
                Debug.Log("[Connect] ⚠️⚠️⚠️ 서버로부터 ID 101 (로그인 응답) 패킷 수신 대기 중...");
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
#endif
        }
    }

    /// <summary>
    /// WebGL 빌드에서 서버 설정을 로드합니다.
    /// 우선순위: 1) URL 파라미터 2) 설정 파일 3) Inspector 기본값
    /// </summary>
    private void LoadWebGLServerConfig()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        // JavaScript 함수 호출은 일시적으로 비활성화 (빌드 오류 해결)
        // Unity가 .jslib 파일을 인식하지 못할 때는 설정 파일만 사용
        // URL 파라미터 기능은 나중에 활성화 가능
        
        try
        {
            // 2. 설정 파일에서 서버 IP 가져오기
            TextAsset configFile = Resources.Load<TextAsset>("server-config");
            if (configFile != null)
            {
                ServerConfig config = JsonUtility.FromJson<ServerConfig>(configFile.text);
                if (config != null && !string.IsNullOrEmpty(config.serverIP))
                {
                    serverIP = config.serverIP;
                    serverPort = config.serverPort;
                    Debug.Log($"[NetworkManager] 설정 파일에서 서버 설정 로드: {serverIP}:{serverPort}");
                    return;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[NetworkManager] 설정 파일에서 서버 설정 로드 실패: {e.Message}");
        }
        
        // 3. Inspector 기본값 사용
        Debug.Log($"[NetworkManager] 기본 서버 설정 사용: {serverIP}:{serverPort}");
#endif
    }
    
    /// <summary>
    /// Unity Editor에서 설정 파일을 로드합니다 (선택사항)
    /// </summary>
    private void LoadServerConfigForEditor()
    {
        try
        {
            TextAsset configFile = Resources.Load<TextAsset>("server-config");
            if (configFile == null)
            {
                Debug.LogWarning("[NetworkManager] 설정 파일을 찾을 수 없습니다. Resources/server-config.json 파일이 있는지 확인하세요.");
            }
            else
            {
                Debug.Log($"[NetworkManager] 설정 파일 발견. 내용: {configFile.text}");
                ServerConfig config = JsonUtility.FromJson<ServerConfig>(configFile.text);
                if (config != null && !string.IsNullOrEmpty(config.serverIP))
                {
                    serverIP = config.serverIP;
                    // Editor에서는 Railway TCP 포트(36222) 사용 (내부 포트 7777로 매핑됨)
                    // Railway 포트 매핑: switchback.proxy.rlwy.net:36222 -> :7777
                    serverPort = config.serverPort; // 설정 파일의 포트 사용 (36222)
                    Debug.Log($"[NetworkManager] ✅ 설정 파일에서 서버 설정 로드 (Editor): {serverIP}:{serverPort} (TCP, Railway 매핑: 내부 7777)");
                    return;
                }
                else
                {
                    Debug.LogWarning("[NetworkManager] 설정 파일의 serverIP가 비어있습니다.");
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[NetworkManager] 설정 파일에서 서버 설정 로드 실패 (Editor): {e.Message}\n{e.StackTrace}");
        }
        
        // 설정 파일이 없으면 Inspector 기본값 사용 (Editor에서는 TCP 포트 사용)
        if (serverPort != 7777)
        {
            serverPort = 7777;
        }
        Debug.Log($"[NetworkManager] ⚠️ Inspector 기본값 사용 (Editor): {serverIP}:{serverPort} (TCP)");
    }
    
    /// <summary>
    /// 런타임에 서버 IP를 설정할 수 있는 메서드 (GameLift 등에서 사용)
    /// </summary>
    public void SetServerIP(string ip)
    {
        serverIP = ip;
        Debug.Log($"[NetworkManager] 서버 IP 설정: {serverIP}");
    }
    
    /// <summary>
    /// 런타임에 서버 포트를 설정할 수 있는 메서드
    /// </summary>
    public void SetServerPort(int port)
    {
        serverPort = port;
        Debug.Log($"[NetworkManager] 서버 포트 설정: {serverPort}");
    }
    
    private void Disconnect()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        // WebGL: WebSocket 연결 종료
        if (!string.IsNullOrEmpty(_webSocketId) && _webSocketIdPtr != IntPtr.Zero)
        {
            WebSocket_Close(_webSocketIdPtr);
            WebSocket_FreeString(_webSocketIdPtr);
            _webSocketIdPtr = IntPtr.Zero;
            _webSocketId = null;
        }
#else
        // Editor: 기존 TCP 연결 종료
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
#endif
    }

    private void ReceiveLoop()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        Debug.Log("[ReceiveLoop] 수신 루프 시작.");
        const int MAX_BUFFER_SIZE = 4096;
        byte[] receiveBuffer = new byte[MAX_BUFFER_SIZE];
        int bytesRead = 0;
        int loopCount = 0;

        while (_isRunning && _client != null && _client.Connected)
        {
            try
            {
                // 연결 상태 주기적 확인 (5초마다)
                loopCount++;
                if (loopCount % 5000 == 0) // 5초마다 (Thread.Sleep(1)이므로 대략 5000번 = 5초)
                {
                    bool isConnected = _client != null && _client.Connected;
                    bool streamAvailable = _stream != null && _stream.CanRead;
                    Debug.Log($"[ReceiveLoop] 연결 상태 확인 - Connected: {isConnected}, StreamAvailable: {streamAvailable}, QueueSize: {_packetQueue.Count}");
                }

                if (_stream.DataAvailable)
                {
                    bytesRead = _stream.Read(receiveBuffer, 0, receiveBuffer.Length);
                    if (bytesRead > 0)
                    {
                        // 누적 버퍼에 추가
                        lock (_receiveBuffer)
                        {
                            for (int i = 0; i < bytesRead; i++)
                            {
                                _receiveBuffer.Add(receiveBuffer[i]);
                            }
                        }
                        
                        // 누적 버퍼에서 완전한 패킷들을 추출
                        lock (_receiveBuffer)
                        {
                            while (_receiveBuffer.Count >= 4) // 최소 헤더 크기 (2바이트 길이 + 2바이트 ID)
                            {
                                // 패킷 길이 읽기 (첫 2바이트, little-endian)
                                byte[] lengthBytes = new byte[2];
                                lengthBytes[0] = _receiveBuffer[0];
                                lengthBytes[1] = _receiveBuffer[1];
                                if (!BitConverter.IsLittleEndian)
                                {
                                    Array.Reverse(lengthBytes);
                                }
                                ushort packetLength = BitConverter.ToUInt16(lengthBytes, 0);
                                
                                // 패킷이 완전히 도착했는지 확인
                                if (_receiveBuffer.Count >= packetLength)
                                {
                                    // 완전한 패킷 추출
                                    byte[] completePacket = new byte[packetLength];
                                    _receiveBuffer.CopyTo(0, completePacket, 0, packetLength);
                                    _receiveBuffer.RemoveRange(0, packetLength);
                                    
                                    // 패킷 큐에 추가
                        lock (_packetQueue)
                        {
                                        const int MAX_QUEUE_SIZE = 100; // 최대 큐 크기
                                        if (_packetQueue.Count >= MAX_QUEUE_SIZE)
                                        {
                                            Debug.LogWarning($"[Recv] ⚠️ 패킷 큐가 가득 찼습니다! ({_packetQueue.Count}개) 오래된 패킷을 버립니다.");
                                            _packetQueue.Dequeue(); // 오래된 패킷 제거
                                        }
                                        _packetQueue.Enqueue(completePacket);
                                        if (_packetQueue.Count > 50)
                                        {
                                            Debug.LogWarning($"[Recv] ⚠️ 패킷 큐가 많이 쌓였습니다! ({_packetQueue.Count}개) 처리 지연 가능성.");
                                        }
                                        else
                                        {
                                            Debug.Log($"[Recv] 서버로부터 완전한 패킷 수신 (길이: {packetLength}바이트). 큐에 추가됨. 현재 큐 크기: {_packetQueue.Count}");
                                        }
                                    }
                                }
                                else
                                {
                                    // 패킷이 아직 완전히 도착하지 않음, 다음 수신 대기
                                    break;
                                }
                            }
                        }
                    }
                    else if (bytesRead == 0)
                    {
                        // 서버가 연결을 끊었음
                        Debug.LogWarning("[ReceiveLoop] 서버가 연결을 끊었습니다 (bytesRead == 0)");
                        _isRunning = false;
                        break;
                    }
                }
                Thread.Sleep(1);
            }
            catch (System.IO.IOException ioEx)
            {
                // 네트워크 연결 끊김 (정상적인 종료일 수 있음)
                Debug.LogWarning($"[ReceiveLoop] 네트워크 연결 끊김: {ioEx.Message}");
                _isRunning = false;
                break;
            }
            catch (System.Net.Sockets.SocketException socketEx)
            {
                // 소켓 오류
                Debug.LogWarning($"[ReceiveLoop] 소켓 오류: {socketEx.Message}");
                _isRunning = false;
                break;
            }
            catch (Exception e)
            {
                if (_client != null && _client.Connected)
                {
                    Debug.LogError($"[ReceiveLoop] 수신 중 오류 발생: {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
                }
                else
                {
                    Debug.LogWarning($"[ReceiveLoop] 연결이 끊어졌습니다. 수신 루프 종료. ({e.GetType().Name}: {e.Message})");
                }
                _isRunning = false;
                break;
            }
        }
        Debug.Log("[ReceiveLoop] 수신 루프 종료.");
#else
        // WebGL에서는 ReceiveLoop를 사용하지 않음 (WebSocket 콜백 사용)
        Debug.Log("[ReceiveLoop] WebGL 빌드에서는 ReceiveLoop를 사용하지 않습니다.");
#endif
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

#if UNITY_WEBGL && !UNITY_EDITOR
        // WebGL: WebSocket 전송
        if (string.IsNullOrEmpty(_webSocketId) || _webSocketIdPtr == IntPtr.Zero)
        {
            Debug.LogError("WebSocket에 연결되지 않아 패킷을 보낼 수 없습니다.");
            return;
        }
        
        int readyState = WebSocket_GetReadyState(_webSocketIdPtr);
        if (readyState != 1) // OPEN
        {
            Debug.LogError($"WebSocket이 열려있지 않습니다. 상태: {readyState}");
            return;
        }
        
        try
        {
            // byte[]를 IntPtr로 변환
            IntPtr dataPtr = Marshal.AllocHGlobal(packet.Length);
            Marshal.Copy(packet, 0, dataPtr, packet.Length);
            
            int result = WebSocket_Send(_webSocketIdPtr, dataPtr, packet.Length);
            
            // 메모리 해제
            Marshal.FreeHGlobal(dataPtr);
            
            if (result == 1)
            {
                Debug.Log($"패킷 전송 완료. 길이: {packet.Length} 바이트");
            }
            else
            {
                Debug.LogError("WebSocket 패킷 전송 실패");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"패킷 전송 중 오류 발생: {e.Message}");
        }
#else
        // Editor: 기존 TCP 전송
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
#endif
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
            
            // 게임 관련 패킷인 경우 더 자세한 로그
            if (header.MessageID == 400 || header.MessageID == 430 || header.MessageID == 420 || 
                header.MessageID == 421 || header.MessageID == 422 || header.MessageID == 440)
            {
                Debug.Log($"[Handle] ⚠️⚠️⚠️ 게임 동기화 패킷 수신! ID: {header.MessageID} (총 길이: {header.TotalLength}바이트)");
            }
            
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
            case 430:
                ProcessTurnStartNotify(reader);
                break;
            case 400:
                ProcessPlayerMoveNotify(reader);
                break;
            case 410:
                ProcessPlayerAimNotify(reader);
                break;
            case 420:
                Debug.Log($"[Handle] ⚠️⚠️⚠️ 게임 동기화 패킷 수신! ID: 420 (총 길이: {header.TotalLength}바이트)");
                ProcessPlayerFireNotify(reader);
                break;
            case 501:
                ProcessPlayerMoveResponse(reader);
                break;
            case 421:
                Debug.Log($"[Handle] ⚠️⚠️⚠️ 게임 동기화 패킷 수신! ID: 421 (총 길이: {header.TotalLength}바이트)");
                ProcessFireResultNotify(reader);
                break;
            case 422:
                ProcessPlayerDeathNotify(reader);
                break;
            case 440:
                ProcessGameEndNotify(reader);
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
                // ConnectedUserName은 Start()에서 이미 설정됨
                Debug.Log($"[ProcessLoginResponse] ConnectedUserName: {ConnectedUserName}");
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
            
            // 방 생성 시 방장 자신을 플레이어 목록에 추가 (UserEnter Notify를 받기 전에 미리 추가)
            // RoomManager가 아직 초기화되지 않았을 수 있으므로 씬 전환 후 처리
            PendingRoomID = roomID;
            
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
        const int USER_NAME_LENGTH = 20; // 서버가 기대하는 고정 길이
        const ushort TOTAL_LENGTH = (ushort)(4 + USER_NAME_LENGTH); // Header(4) + UserName(20)
        
        Debug.Log($"[SendLoginRequest] ========== ID 100 (로그인 요청) 패킷 전송 시작 ==========");
        Debug.Log($"[SendLoginRequest] UserName: {userName}, 고정 길이: {USER_NAME_LENGTH}바이트");

        PacketBuilder builder = new PacketBuilder();
        builder.WriteHeader(MESSAGE_ID, TOTAL_LENGTH);
        builder.WriteUserName(userName, USER_NAME_LENGTH); // 서버가 기대하는 20바이트 고정 길이 형식

        byte[] loginPacket = builder.GetPacket();
        Debug.Log($"[SendLoginRequest] 패킷 생성 완료 - 총 길이: {loginPacket.Length}바이트 (기대: {TOTAL_LENGTH}바이트)");
        SendPacket(loginPacket);
        Debug.Log($"[SendLoginRequest] ✅✅✅ ID 100 (로그인 요청) 패킷 전송 완료! User: {userName}");
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
#if !UNITY_WEBGL || UNITY_EDITOR
            Debug.LogError("[SendRoomListRequest] 연결 상태: _client=" + (_client != null ? "존재" : "null") + 
                          ", Connected=" + (_client != null ? _client.Connected.ToString() : "N/A"));
#endif
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
                Debug.Log($"[ProcessJoinRoomResponse] ConnectedUserID: {ConnectedUserID}, _myUniqueUserName: {_myUniqueUserName}");
                
                // 씬 전환 전에 방 ID 저장 (씬 전환 후 RoomManager에 전달하기 위해)
                PendingRoomID = roomID;
                
                // UserEnter Notify 플래그 리셋 (새 방에 입장하므로)
                _hasReceivedUserEnter = false;
                _firstUserEnterID = -1;
                
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
#if UNITY_WEBGL && !UNITY_EDITOR
        if (string.IsNullOrEmpty(_webSocketId) || _webSocketIdPtr == IntPtr.Zero)
            return false;
        
        int readyState = WebSocket_GetReadyState(_webSocketIdPtr);
        return readyState == 1; // OPEN
#else
        return _client != null && _client.Connected;
#endif
    }

    private void ProcessGameStartNotify(PacketReader reader)
    {
        try
        {
            // ID 331: [FirstTurnUserID (4 bytes, int)]
            int firstTurnUserID = reader.ReadInt32();

            Debug.Log($"[ProcessGameStartNotify] 게임 시작 Notify (ID 331) 수신. FirstTurnUserID: {firstTurnUserID}. GameScene으로 이동합니다.");

            // RoomManager에서 플레이어 목록 가져오기
            if (RoomManager.Instance != null)
            {
                var playerList = RoomManager.Instance.GetPlayerList();
                
                // [핵심 수정] 모든 클라이언트에서 동일한 순서를 보장하기 위해 PlayerID로 정렬
                // 이렇게 하면 모든 클라이언트에서 동일한 플레이어 순서, 색상, 위치를 보장할 수 있음
                var sortedPlayerList = new List<PlayerReadyData>(playerList);
                
                // PlayerID 오름차순으로 정렬 (모든 클라이언트에서 동일한 순서 보장)
                sortedPlayerList.Sort((a, b) => a.PlayerID.CompareTo(b.PlayerID));
                
                _gamePlayerList.Clear();
                foreach (var player in sortedPlayerList)
                {
                    _gamePlayerList.Add(new PlayerInfo(player.PlayerID, player.PlayerName));
                }
                Debug.Log($"[ProcessGameStartNotify] 게임 시작 플레이어 목록: {_gamePlayerList.Count}명 (PlayerID 오름차순 정렬 완료)");
                Debug.Log($"[ProcessGameStartNotify] 현재 ConnectedUserID: {ConnectedUserID}, CreatedRoomID: {CreatedRoomID}");
                
                // 플레이어 목록 상세 로그 출력
                Debug.Log($"[ProcessGameStartNotify] 플레이어 목록 상세 (정렬 후):");
                for (int i = 0; i < sortedPlayerList.Count; i++)
                {
                    Debug.Log($"[ProcessGameStartNotify]   [{i}] PlayerID: {sortedPlayerList[i].PlayerID}, UserName: '{sortedPlayerList[i].PlayerName}'");
                }
                
                // CreatedRoomID와 _myUniqueUserName, 그리고 서버가 보낸 FirstTurnUserID를 기반으로 올바른 ID를 결정
                int correctUserID = -1;
                
                // 먼저 _myUniqueUserName을 사용하여 플레이어 목록에서 자신을 찾기 시도
                // [핵심 수정] 정확한 전체 문자열 일치만 허용 (부분 문자열 비교 제거)
                if (!string.IsNullOrEmpty(_myUniqueUserName))
                {
                    string trimmedMyUserName = _myUniqueUserName.Trim();
                    foreach (var player in sortedPlayerList)
                    {
                        string trimmedPlayerName = player.PlayerName != null ? player.PlayerName.Trim() : "";
                        // [수정] 정확히 일치하는 경우만 허용 (부분 문자열 비교 제거)
                        if (trimmedPlayerName == trimmedMyUserName)
                        {
                            correctUserID = player.PlayerID;
                            Debug.Log($"[ProcessGameStartNotify] ✅ _myUniqueUserName으로 자신을 찾음: UserID={correctUserID}, UserName='{player.PlayerName}' (정확한 일치)");
                            break;
                        }
                        else
                        {
                            Debug.Log($"[ProcessGameStartNotify] UserName 불일치: '{trimmedPlayerName}' != '{trimmedMyUserName}' (부분 문자열 비교 제거로 인해 정확한 일치만 허용)");
                        }
                    }
                }
                
                // _myUniqueUserName으로 찾지 못한 경우, 다른 방법으로 식별
                if (correctUserID == -1)
                {
                    // [핵심 수정] 이미 ConnectedUserID가 설정되어 있으면 그것을 사용
                    // (일반 유저는 UserEnter Notify에서 _firstUserEnterID를 저장했을 수 있음)
                    if (ConnectedUserID != -1)
                    {
                        correctUserID = ConnectedUserID;
                        Debug.Log($"[ProcessGameStartNotify] ConnectedUserID가 이미 설정되어 있으므로 사용: UserID={correctUserID}");
                    }
                    // [핵심 수정] 일반 유저는 UserEnter Notify에서 저장한 _firstUserEnterID 사용
                    else if (CreatedRoomID == -1 && _hasReceivedUserEnter && _firstUserEnterID != -1)
                    {
                        // _firstUserEnterID가 플레이어 목록에 있는지 확인
                        bool isValidID = sortedPlayerList.Exists(p => p.PlayerID == _firstUserEnterID);
                        if (isValidID)
                        {
                            correctUserID = _firstUserEnterID;
                            Debug.Log($"[ProcessGameStartNotify] 일반 유저: UserEnter Notify에서 저장한 ID 사용: UserID={correctUserID}");
                        }
                        else
                        {
                            Debug.LogWarning($"[ProcessGameStartNotify] ⚠️ 저장된 _firstUserEnterID({_firstUserEnterID})가 플레이어 목록에 없습니다.");
                        }
                    }
                    // [핵심 수정] 방장은 플레이어 목록에서 자신을 찾기
                    // 방장은 UserEnter Notify를 통해 자신의 ID를 설정하지 않았으므로, 
                    // 플레이어 목록에서 RoomManager의 플레이어 목록과 비교하여 식별
                    else if (CreatedRoomID != -1)
                    {
                        // RoomManager에서 자신의 정보를 찾기
                        if (RoomManager.Instance != null)
                        {
                            var roomPlayerList = RoomManager.Instance.GetPlayerList();
                            // RoomManager의 플레이어 목록과 GameStartNotify의 플레이어 목록을 비교
                            foreach (var roomPlayer in roomPlayerList)
                            {
                                // RoomManager의 플레이어 이름과 GameStartNotify의 플레이어 이름 비교
                                var matchedPlayer = sortedPlayerList.Find(p => 
                                    p.PlayerID == roomPlayer.PlayerID || 
                                    (p.PlayerName != null && roomPlayer.PlayerName != null && 
                                     p.PlayerName.Trim() == roomPlayer.PlayerName.Trim()));
                                
                                if (matchedPlayer != null)
                                {
                                    // RoomManager에서 자신을 찾았는지 확인 (ConnectedUserName과 비교)
                                    if (roomPlayer.PlayerName != null && ConnectedUserName != null &&
                                        roomPlayer.PlayerName.Trim() == ConnectedUserName.Trim())
                                    {
                                        correctUserID = matchedPlayer.PlayerID;
                                        Debug.Log($"[ProcessGameStartNotify] ✅ 방장: RoomManager를 통해 자신을 찾음: UserID={correctUserID}, UserName='{matchedPlayer.PlayerName}'");
                                        break;
                                    }
                                }
                            }
                            
                            // RoomManager로 찾지 못한 경우, 첫 번째 플레이어를 방장으로 가정 (최후의 수단)
                            if (correctUserID == -1 && sortedPlayerList.Count > 0)
                            {
                                correctUserID = sortedPlayerList[0].PlayerID;
                                Debug.LogWarning($"[ProcessGameStartNotify] ⚠️ 방장: RoomManager로 찾지 못하여 첫 번째 플레이어를 자신으로 가정: UserID={correctUserID}");
                            }
                        }
                        else
                        {
                            // RoomManager가 없는 경우, 첫 번째 플레이어를 방장으로 가정
                            if (sortedPlayerList.Count > 0)
                            {
                                correctUserID = sortedPlayerList[0].PlayerID;
                                Debug.LogWarning($"[ProcessGameStartNotify] ⚠️ 방장: RoomManager가 없어 첫 번째 플레이어를 자신으로 가정: UserID={correctUserID}");
                            }
                        }
                    }
                    // [핵심 수정] 일반 유저도 _firstUserEnterID로 찾지 못한 경우, 
                    // RoomManager의 플레이어 목록과 비교하여 자신을 찾기
                    else
                    {
                        // 1단계: UserName으로 직접 비교
                        if (!string.IsNullOrEmpty(ConnectedUserName))
                        {
                            string trimmedConnectedUserName = ConnectedUserName.Trim();
                            foreach (var player in sortedPlayerList)
                            {
                                if (player.PlayerName != null && player.PlayerName.Trim() == trimmedConnectedUserName)
                                {
                                    correctUserID = player.PlayerID;
                                    Debug.Log($"[ProcessGameStartNotify] ✅ 일반 유저: UserName으로 자신을 찾음: UserID={correctUserID}, UserName='{player.PlayerName}'");
                                    break;
                                }
                            }
                        }
                        
                        // 2단계: RoomManager의 플레이어 목록과 비교
                        if (correctUserID == -1 && RoomManager.Instance != null)
                        {
                            var roomPlayerList = RoomManager.Instance.GetPlayerList();
                            Debug.Log($"[ProcessGameStartNotify] RoomManager 플레이어 목록과 비교 시도. RoomManager 플레이어 수: {roomPlayerList.Count}, GameStart 플레이어 수: {sortedPlayerList.Count}");
                            
                            // RoomManager의 각 플레이어와 GameStartNotify의 플레이어 목록 비교
                            foreach (var roomPlayer in roomPlayerList)
                            {
                                // RoomManager의 플레이어 이름이 ConnectedUserName과 일치하는지 확인
                                if (roomPlayer.PlayerName != null && ConnectedUserName != null)
                                {
                                    string trimmedRoomPlayerName = roomPlayer.PlayerName.Trim();
                                    string trimmedConnectedUserName = ConnectedUserName.Trim();
                                    
                                    // 정확한 일치 또는 부분 일치 확인
                                    bool nameMatches = trimmedRoomPlayerName == trimmedConnectedUserName ||
                                                       trimmedRoomPlayerName.Contains(trimmedConnectedUserName) ||
                                                       trimmedConnectedUserName.Contains(trimmedRoomPlayerName);
                                    
                                    if (nameMatches)
                                    {
                                        // 일치하는 플레이어를 GameStartNotify 목록에서 찾기
                                        var matchedPlayer = sortedPlayerList.Find(p => 
                                            p.PlayerID == roomPlayer.PlayerID ||
                                            (p.PlayerName != null && roomPlayer.PlayerName != null &&
                                             p.PlayerName.Trim() == roomPlayer.PlayerName.Trim()));
                                        
                                        if (matchedPlayer != null)
                                        {
                                            correctUserID = matchedPlayer.PlayerID;
                                            Debug.Log($"[ProcessGameStartNotify] ✅ 일반 유저: RoomManager를 통해 자신을 찾음: UserID={correctUserID}, RoomManager UserName='{roomPlayer.PlayerName}', GameStart UserName='{matchedPlayer.PlayerName}'");
                                            break;
                                        }
                                    }
                                }
                            }
                            
                            // 3단계: RoomManager의 플레이어 ID로 직접 매칭 (UserName 비교 실패 시)
                            if (correctUserID == -1)
                            {
                                // RoomManager의 플레이어 목록에서 자신을 찾기
                                // ConnectedUserName과 부분 일치하는 플레이어 찾기
                                foreach (var roomPlayer in roomPlayerList)
                                {
                                    if (roomPlayer.PlayerName != null && ConnectedUserName != null)
                                    {
                                        string trimmedRoomPlayerName = roomPlayer.PlayerName.Trim();
                                        string trimmedConnectedUserName = ConnectedUserName.Trim();
                                        
                                        // 부분 문자열 일치 확인 (더 관대한 매칭)
                                        if (trimmedRoomPlayerName.Contains(trimmedConnectedUserName) ||
                                            trimmedConnectedUserName.Contains(trimmedRoomPlayerName) ||
                                            trimmedRoomPlayerName.StartsWith(trimmedConnectedUserName) ||
                                            trimmedConnectedUserName.StartsWith(trimmedRoomPlayerName))
                                        {
                                            // GameStartNotify 목록에서 해당 ID 찾기
                                            var matchedPlayer = sortedPlayerList.Find(p => p.PlayerID == roomPlayer.PlayerID);
                                            if (matchedPlayer != null)
                                            {
                                                correctUserID = matchedPlayer.PlayerID;
                                                Debug.Log($"[ProcessGameStartNotify] ✅ 일반 유저: RoomManager ID 매칭으로 자신을 찾음 (부분 일치): UserID={correctUserID}, RoomManager UserName='{roomPlayer.PlayerName}', GameStart UserName='{matchedPlayer.PlayerName}'");
                                                break;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        
                        // 4단계: 최후의 수단 - RoomManager의 플레이어 목록에서 자신을 찾기
                        // RoomManager에는 자신의 정보가 있을 것이므로, GameStartNotify 목록과 매칭
                        if (correctUserID == -1 && RoomManager.Instance != null)
                        {
                            var roomPlayerList = RoomManager.Instance.GetPlayerList();
                            Debug.LogWarning($"[ProcessGameStartNotify] ⚠️ 일반 유저: 모든 방법으로 자신을 찾지 못했습니다. RoomManager 최후의 수단 사용.");
                            Debug.LogWarning($"[ProcessGameStartNotify] RoomManager 플레이어 수: {roomPlayerList.Count}, GameStart 플레이어 수: {sortedPlayerList.Count}");
                            
                            // RoomManager의 플레이어 목록에서 자신을 찾기
                            // ConnectedUserName과 부분 일치하는 플레이어 찾기
                            foreach (var roomPlayer in roomPlayerList)
                            {
                                if (roomPlayer.PlayerName != null && ConnectedUserName != null)
                                {
                                    string trimmedRoomPlayerName = roomPlayer.PlayerName.Trim();
                                    string trimmedConnectedUserName = ConnectedUserName.Trim();
                                    
                                    // 더 관대한 매칭: 시작 부분 일치 또는 공통 부분 확인
                                    bool mightBeMe = trimmedRoomPlayerName.StartsWith(trimmedConnectedUserName.Substring(0, Math.Min(10, trimmedConnectedUserName.Length))) ||
                                                      trimmedConnectedUserName.StartsWith(trimmedRoomPlayerName.Substring(0, Math.Min(10, trimmedRoomPlayerName.Length))) ||
                                                      trimmedRoomPlayerName.Contains("Client_") && trimmedConnectedUserName.Contains("Client_");
                                    
                                    if (mightBeMe)
                                    {
                                        // GameStartNotify 목록에서 해당 ID 찾기
                                        var matchedPlayer = sortedPlayerList.Find(p => p.PlayerID == roomPlayer.PlayerID);
                                        if (matchedPlayer != null)
                                        {
                                            correctUserID = matchedPlayer.PlayerID;
                                            Debug.LogWarning($"[ProcessGameStartNotify] ⚠️ 일반 유저: RoomManager 최후의 수단으로 자신을 찾음 (부분 일치): UserID={correctUserID}, RoomManager UserName='{roomPlayer.PlayerName}', GameStart UserName='{matchedPlayer.PlayerName}'");
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                        
                        // 5단계: 최최후의 수단 - 플레이어 목록에서 아직 식별되지 않은 플레이어 찾기
                        // (이미 식별된 플레이어를 제외하고 남은 플레이어 중 선택)
                        if (correctUserID == -1)
                        {
                            Debug.LogWarning($"[ProcessGameStartNotify] ⚠️ 일반 유저: 모든 방법으로 자신을 찾지 못했습니다. 최최후의 수단 사용.");
                            Debug.LogWarning($"[ProcessGameStartNotify] CreatedRoomID: {CreatedRoomID}, _firstUserEnterID: {_firstUserEnterID}, ConnectedUserName: '{ConnectedUserName}'");
                            
                            // RoomManager의 플레이어 목록과 GameStartNotify의 플레이어 목록을 비교
                            // 방장이 이미 첫 번째로 식별되었다고 가정하고, 나머지 중에서 찾기
                            if (RoomManager.Instance != null)
                            {
                                var roomPlayerList = RoomManager.Instance.GetPlayerList();
                                
                                // RoomManager의 플레이어 중 GameStartNotify 목록에 있는 플레이어 찾기
                                // 방장(첫 번째)을 제외하고 나머지 중에서 찾기
                                for (int i = 1; i < sortedPlayerList.Count && i < roomPlayerList.Count; i++)
                                {
                                    // RoomManager의 i번째 플레이어가 GameStartNotify의 i번째 플레이어와 ID가 일치하는지 확인
                                    if (sortedPlayerList[i].PlayerID == roomPlayerList[i].PlayerID)
                                    {
                                        // 순서가 일치하는 경우, 이 플레이어가 자신일 가능성이 높음
                                        // 하지만 이는 매우 불안정하므로 경고와 함께 사용
                                        correctUserID = sortedPlayerList[i].PlayerID;
                                        Debug.LogWarning($"[ProcessGameStartNotify] ⚠️⚠️⚠️ 일반 유저: 순서 기반 추정으로 자신을 찾음 (불안정): UserID={correctUserID}, Index={i}");
                                        Debug.LogWarning($"[ProcessGameStartNotify] ⚠️ 이 방법은 불안정하므로 서버 수정(로그인/방입장 시 UserID 반환)을 강력히 권장합니다.");
                                        break;
                                    }
                                }
                                
                                // 순서 기반으로도 찾지 못한 경우, RoomManager의 플레이어 중 아직 매칭되지 않은 플레이어 찾기
                                if (correctUserID == -1)
                                {
                                    foreach (var roomPlayer in roomPlayerList)
                                    {
                                        // GameStartNotify 목록에서 해당 ID가 있는지 확인
                                        var matchedPlayer = sortedPlayerList.Find(p => p.PlayerID == roomPlayer.PlayerID);
                                        if (matchedPlayer != null)
                                        {
                                            // 이 플레이어가 아직 식별되지 않았다면 자신일 가능성
                                            // (다른 클라이언트가 이미 자신을 식별했다고 가정)
                                            correctUserID = matchedPlayer.PlayerID;
                                            Debug.LogWarning($"[ProcessGameStartNotify] ⚠️⚠️⚠️ 일반 유저: RoomManager 플레이어 목록 기반 추정 (매우 불안정): UserID={correctUserID}");
                                            Debug.LogWarning($"[ProcessGameStartNotify] ⚠️ 이 방법은 매우 불안정하므로 서버 수정(로그인/방입장 시 UserID 반환)을 강력히 권장합니다.");
                                            break;
                                        }
                                    }
                                }
                            }
                            
                            // 여전히 찾지 못한 경우
                            if (correctUserID == -1)
                            {
                                Debug.LogError($"[ProcessGameStartNotify] ❌❌❌ 자신을 찾지 못했습니다. 게임을 계속할 수 없습니다.");
                                Debug.LogError($"[ProcessGameStartNotify] 플레이어 목록:");
                                foreach (var player in sortedPlayerList)
                                {
                                    Debug.LogError($"[ProcessGameStartNotify]   - UserID: {player.PlayerID}, UserName: '{player.PlayerName}'");
                                }
                            }
                        }
                    }
                }
                
                // 올바른 ID가 결정되었고, 현재 ConnectedUserID와 다르면 수정
                if (correctUserID != -1)
                {
                    if (ConnectedUserID != correctUserID)
                    {
                        Debug.LogWarning($"[ProcessGameStartNotify] ⚠️ ConnectedUserID({ConnectedUserID})가 올바르지 않습니다. 올바른 ID({correctUserID})로 수정합니다.");
                        ConnectedUserID = correctUserID;
                        if (PlayerManager.Instance != null)
                        {
                            PlayerManager.Instance.SetMyPlayerID(correctUserID);
                        }
                        Debug.Log($"[ProcessGameStartNotify] ✅ ConnectedUserID를 올바른 값으로 수정: {correctUserID}");
        }
        else
        {
                        Debug.Log($"[ProcessGameStartNotify] ✅ ConnectedUserID가 이미 올바릅니다: {ConnectedUserID}");
                    }
                }
                else if (ConnectedUserID == -1)
                {
                    // ConnectedUserID가 -1이고 올바른 ID를 찾지 못한 경우
                    Debug.LogError($"[ProcessGameStartNotify] ❌ 플레이어 목록에서 자신을 찾지 못함. CreatedRoomID: {CreatedRoomID}, 플레이어 수: {playerList.Count}");
                    Debug.LogError($"[ProcessGameStartNotify] 플레이어 목록:");
                    foreach (var player in playerList)
                    {
                        Debug.LogError($"[ProcessGameStartNotify]   - UserID: {player.PlayerID}, UserName: '{player.PlayerName}'");
                    }
                }
            }

            // 첫 턴 플레이어 설정
            _currentTurnPlayerID = firstTurnUserID;
            Debug.Log($"[ProcessGameStartNotify] _currentTurnPlayerID 설정: {_currentTurnPlayerID}");
            Debug.Log($"[ProcessGameStartNotify] ⚠️ 서버로부터 ID 430 (Turn Start Notify) 수신 대기 중...");

            // 첫 턴 플레이어에게 컨트롤 권한 부여 (씬 로드 후 GameManager에서 처리)
            // 여기서는 씬 전환만 수행

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

            Debug.Log($"[UserEnter Notify] UserID: {userID}, UserName: '{userName}', ConnectedUserName: '{ConnectedUserName}', _myUniqueUserName: '{_myUniqueUserName}'");

            // 자신의 UserID인 경우 ConnectedUserID 설정
            // userName이 _myUniqueUserName과 정확히 일치하는 경우 자신의 UserID로 설정
            bool isMyUser = false;
            
            // userName을 trim하여 비교 (서버에서 공백이 포함될 수 있음)
            string trimmedUserName = userName != null ? userName.Trim() : "";
            string trimmedMyUserName = _myUniqueUserName != null ? _myUniqueUserName.Trim() : "";
            
            // [핵심 수정] 정확한 전체 문자열 일치만 허용 (부분 문자열 비교 제거)
            // 부분 문자열 비교는 다른 플레이어의 UserID를 자신의 것으로 잘못 설정하는 버그를 유발할 수 있음
            bool userNameMatches = false;
            if (!string.IsNullOrEmpty(trimmedMyUserName) && !string.IsNullOrEmpty(trimmedUserName))
            {
                // [수정] 정확히 일치하는 경우만 허용 (부분 문자열 비교 제거)
                if (trimmedUserName == trimmedMyUserName)
                {
                    userNameMatches = true;
                    Debug.Log($"[UserEnter Notify] ✅ 정확한 UserName 일치 확인: '{trimmedUserName}' == '{trimmedMyUserName}'");
                }
                else
                {
                    Debug.Log($"[UserEnter Notify] ❌ UserName 불일치: '{trimmedUserName}' != '{trimmedMyUserName}' (부분 문자열 비교 제거로 인해 정확한 일치만 허용)");
                }
            }
            else
            {
                Debug.LogWarning($"[UserEnter Notify] ⚠️ UserName이 null이거나 비어있음. trimmedUserName='{trimmedUserName}', trimmedMyUserName='{trimmedMyUserName}'");
            }
            
            if (userNameMatches)
            {
                // userName이 일치하는 경우 자신의 UserID로 설정
                // 단, 이미 ConnectedUserID가 설정되어 있고 다른 값이면 덮어쓰지 않음 (중복 방지)
                if (ConnectedUserID == -1 || ConnectedUserID == userID)
                {
                    ConnectedUserID = userID;
                    isMyUser = true;
                    Debug.Log($"[UserEnter Notify] ✅ 내 UserID로 설정: {userID}, UserName: '{userName}' (일치: {userNameMatches})");
                }
                else
                {
                    Debug.LogWarning($"[UserEnter Notify] ⚠️ 이미 ConnectedUserID가 설정됨 ({ConnectedUserID}). 새로운 UserID ({userID})를 무시합니다.");
                }
            }
            else if (ConnectedUserID == -1)
            {
                // [핵심 수정] 방장은 UserEnter Notify를 통해 자신의 ID를 설정하지 않음
                // 방장은 다른 유저의 입장 알림을 받을 수 있으므로, 이를 자신의 ID로 오인하면 안 됨
                // 방장의 ID는 ProcessGameStartNotify에서 플레이어 목록을 통해 식별
                
                // 일반 유저만 UserEnter Notify를 통해 자신의 ID를 저장
                // 단, UserName이 일치하는 경우에만 저장 (정확성 보장)
                if (CreatedRoomID == -1) // 일반 유저인 경우
                {
                    // UserName이 일치하는 경우에만 자신의 ID로 저장
                    if (userNameMatches)
                    {
                        _firstUserEnterID = userID;
                        _hasReceivedUserEnter = true;
                        ConnectedUserID = userID;
                        isMyUser = true;
                        Debug.Log($"[UserEnter Notify] ✅ 일반 유저: UserName 일치로 자신의 ID 저장: UserID={userID}, UserName='{userName}'");
                    }
                    else if (!_hasReceivedUserEnter)
                    {
                        // UserName이 일치하지 않지만 첫 번째 UserEnter인 경우, 임시로 저장
                        // (ProcessGameStartNotify에서 최종 확인)
                        _firstUserEnterID = userID;
                        _hasReceivedUserEnter = true;
                        Debug.Log($"[UserEnter Notify] 일반 유저: 첫 번째 UserEnter Notify 수신 (UserName 불일치): UserID={userID}, UserName='{userName}' (ProcessGameStartNotify에서 확정)");
                    }
                    else
                    {
                        Debug.Log($"[UserEnter Notify] 일반 유저: 추가 UserEnter Notify 수신: UserID={userID}, UserName='{userName}' (이미 저장됨: {_firstUserEnterID})");
                    }
                }
                else
                {
                    // 방장인 경우: UserEnter Notify를 통해 자신의 ID를 설정하지 않음
                    Debug.Log($"[UserEnter Notify] 방장이므로 UserEnter Notify를 통해 자신의 ID를 설정하지 않음. UserID: {userID}, UserName: '{userName}' (ProcessGameStartNotify에서 식별)");
                }
            }
            
            if (isMyUser && PlayerManager.Instance != null)
            {
                PlayerManager.Instance.SetMyPlayerID(userID);
                Debug.Log($"[UserEnter Notify] MyPlayerID 설정 완료: {userID}");
                // 기존 플레이어들의 isLocalPlayer 상태 업데이트
                PlayerManager.Instance.UpdatePlayerLocalStatus();
            }

            // RoomManager에 사용자 입장 알림 전달
            if (RoomManager.Instance != null)
            {
                RoomManager.Instance.OnUserEntered(userID, userName);
                
                // 방 정보가 없으면 기본값으로 설정
                if (RoomManager.Instance.CurrentRoomID == -1 && PendingRoomID != -1)
                {
                    RoomManager.Instance.SetCurrentRoomID(PendingRoomID);
                    // 방 이름이 없으면 기본값 사용 (실제 방 이름은 서버에서 받아야 함)
                    string defaultRoomName = $"Room #{PendingRoomID}";
                    RoomManager.Instance.UpdateRoomInfo(PendingRoomID, defaultRoomName, 0, 5);
                }
            }
            
            // 방장이 방을 생성한 경우, 자신의 UserEnter Notify를 받지 못할 수 있으므로
            // 방장 자신을 플레이어 목록에 추가
            if (CreatedRoomID != -1 && isMyUser && RoomManager.Instance != null)
            {
                // 이미 추가되어 있는지 확인
                var playerList = RoomManager.Instance.GetPlayerList();
                bool alreadyExists = false;
                foreach (var player in playerList)
                {
                    if (player.PlayerID == userID)
                    {
                        alreadyExists = true;
                        break;
                    }
                }
                
                if (!alreadyExists)
                {
                    Debug.Log($"[ProcessUserEnterNotify] 방장 자신을 플레이어 목록에 추가: UserID={userID}, UserName={userName}");
                    RoomManager.Instance.OnUserEntered(userID, userName);
                }
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

    public int GetCurrentTurnPlayerID()
    {
        return _currentTurnPlayerID;
    }

    public bool IsMyTurn()
    {
        return _currentTurnPlayerID == ConnectedUserID;
    }

    private void ProcessTurnStartNotify(PacketReader reader)
    {
        try
        {
            // ID 430: [NextPlayerID (4 bytes, int)] + [TurnTimeLimitSec (4 bytes, int)]
            int nextPlayerID = reader.ReadInt32();
            int turnTimeLimitSec = reader.ReadInt32();

            Debug.Log($"[Turn Start Notify] ✅✅✅✅✅ ID 430 수신! 턴 전환! NextPlayerID: {nextPlayerID}, TurnTimeLimitSec: {turnTimeLimitSec}, 내 UserID: {ConnectedUserID}");
            Debug.Log($"[Turn Start Notify] 이전 턴 플레이어: {_currentTurnPlayerID}, 새로운 턴 플레이어: {nextPlayerID}");

            // [핵심 수정] 중복 패킷 방지: 같은 nextPlayerID를 가진 패킷을 연속으로 받으면 무시
            if (_currentTurnPlayerID == nextPlayerID && _currentTurnPlayerID != -1)
            {
                Debug.LogWarning($"[Turn Start Notify] ⚠️ 중복 패킷 감지! 이미 현재 턴 플레이어({_currentTurnPlayerID})인데 동일한 패킷을 다시 수신했습니다. 무시합니다.");
                return;
            }

            // [핵심 수정] _currentTurnPlayerID를 먼저 업데이트하여, ApplyTurnControlToAllPlayers에서 
            // GetCurrentTurnPlayerID()를 호출할 때 올바른 값을 반환하도록 보장
            _currentTurnPlayerID = nextPlayerID;

            // PlayerController에 턴 정보 전달
            // 플레이어가 아직 생성되지 않았을 수 있으므로, 생성된 경우에만 처리
            if (PlayerManager.Instance != null)
            {
                // PlayerManager에서 모든 플레이어를 가져와서 처리 (더 안전함)
                var allPlayers = PlayerManager.Instance.GetAllPlayers();
                Debug.Log($"[Turn Start Notify] PlayerManager에서 {allPlayers.Count}명의 플레이어를 찾았습니다. NextPlayerID: {nextPlayerID}, ConnectedUserID: {ConnectedUserID}");
                
                // 디버깅: 모든 플레이어 ID 출력
                Debug.Log($"[Turn Start Notify] 현재 PlayerManager의 플레이어 목록:");
                foreach (var kvp in allPlayers)
                {
                    Debug.Log($"[Turn Start Notify]   - PlayerID: {kvp.Key}, GameObject: {kvp.Value?.name ?? "null"}");
                }
                
                // 플레이어가 생성되어 있으면 즉시 처리
                if (allPlayers.Count > 0)
                {
                    ApplyTurnControlToAllPlayers(nextPlayerID, turnTimeLimitSec);
                }
                else
                {
                    Debug.LogWarning($"[Turn Start Notify] ⚠️ 플레이어가 아직 생성되지 않았습니다. GameManager.StartGameLogic() 완료 후 턴 정보가 적용됩니다.");
                }
            }
            else
            {
                Debug.LogError($"[Turn Start Notify] ⚠️⚠️⚠️ PlayerManager.Instance가 null입니다!");
            }

            // GameUI 업데이트
            if (GameUI.Instance != null)
            {
                GameUI.Instance.UpdateTurnInfo();
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ProcessTurnStartNotify] Turn Start Notify 처리 중 오류: {ex.Message}\n{ex.StackTrace}");
        }
    }
    
    // 턴 정보를 모든 플레이어에게 적용하는 메서드 (재사용 가능)
    private void ApplyTurnControlToAllPlayers(int nextPlayerID, int turnTimeLimitSec)
    {
        if (PlayerManager.Instance == null)
        {
            Debug.LogWarning($"[ApplyTurnControlToAllPlayers] PlayerManager.Instance가 null입니다!");
            return;
        }
        
        var allPlayers = PlayerManager.Instance.GetAllPlayers();
        Debug.Log($"[ApplyTurnControlToAllPlayers] {allPlayers.Count}명의 플레이어에게 턴 정보 적용. NextPlayerID: {nextPlayerID}, ConnectedUserID: {ConnectedUserID}");
        
        // 디버깅: nextPlayerID가 PlayerManager에 있는지 확인
        bool foundNextPlayer = allPlayers.ContainsKey(nextPlayerID);
        Debug.Log($"[ApplyTurnControlToAllPlayers] NextPlayerID {nextPlayerID}가 PlayerManager에 존재하는가? {foundNextPlayer}");
        
        if (!foundNextPlayer)
        {
            Debug.LogError($"[ApplyTurnControlToAllPlayers] ⚠️⚠️⚠️ NextPlayerID {nextPlayerID}가 PlayerManager에 없습니다! 현재 플레이어 목록:");
            foreach (var kvp in allPlayers)
            {
                Debug.LogError($"[ApplyTurnControlToAllPlayers]   - PlayerID: {kvp.Key}, GameObject: {kvp.Value?.name ?? "null"}");
            }
        }
        
        // 모든 플레이어에게 턴 정보 업데이트
        foreach (var kvp in allPlayers)
        {
            int playerID = kvp.Key;
            GameObject playerObj = kvp.Value;
            
            if (playerObj != null)
            {
                var controller = playerObj.GetComponent<PlayerController>();
                if (controller != null)
                {
                    // nextPlayerID는 서버에서 보낸 UserID이므로, 이것이 ConnectedUserID와 일치해야 함
                    // 로컬 플레이어이고, 현재 턴이 로컬 플레이어의 턴이면 컨트롤 가능
                    // [핵심 수정] ConnectedUserID를 단일 소스로 사용하여 일관성 보장
                    bool isLocalPlayer = (playerID == ConnectedUserID && ConnectedUserID != -1);
                    bool isCurrentTurn = (playerID == nextPlayerID);
                    bool canControl = isLocalPlayer && isCurrentTurn;
                    
                    Debug.Log($"[ApplyTurnControlToAllPlayers] PlayerID: {playerID}, NextPlayerID: {nextPlayerID}, ConnectedUserID: {ConnectedUserID}, isLocalPlayer: {isLocalPlayer}, isCurrentTurn: {isCurrentTurn}, canControl: {canControl}");
                    
                    // [핵심 수정] 모든 플레이어에게 SetCanControl 호출
                    // 로컬 플레이어이고 현재 턴이면 true, 아니면 false
                    // 이렇게 하면 한 번에 한 명의 플레이어만 컨트롤 가능
                    controller.SetCanControl(canControl);
                    
                    // 턴이 시작된 플레이어에게 OnTurnStart 호출
                    if (isCurrentTurn)
                    {
                        Debug.Log($"[ApplyTurnControlToAllPlayers] ✅ PlayerID {playerID}의 턴 시작! OnTurnStart 호출, canControl: {canControl}");
                        controller.OnTurnStart(turnTimeLimitSec);
                    }
                }
                else
                {
                    Debug.LogWarning($"[ApplyTurnControlToAllPlayers] PlayerID {playerID}의 PlayerController를 찾을 수 없습니다.");
                }
            }
        }
    }
    
    // GameManager가 플레이어 생성 완료 후 호출하는 메서드
    public void ApplyPendingTurnControl()
    {
        // [핵심 수정] _currentTurnPlayerID가 -1이면 플레이어 목록의 첫 번째 플레이어를 첫 턴으로 설정
        int turnPlayerID = _currentTurnPlayerID;
        if (turnPlayerID == -1 && _gamePlayerList != null && _gamePlayerList.Count > 0)
        {
            // 플레이어 목록을 UserID로 정렬하여 첫 번째 플레이어 선택
            var sortedPlayers = new List<PlayerInfo>(_gamePlayerList);
            sortedPlayers.Sort((a, b) => a.UserID.CompareTo(b.UserID));
            turnPlayerID = sortedPlayers[0].UserID;
            _currentTurnPlayerID = turnPlayerID; // _currentTurnPlayerID도 업데이트
            Debug.LogWarning($"[ApplyPendingTurnControl] _currentTurnPlayerID가 -1이었습니다. 플레이어 목록의 첫 번째 플레이어({turnPlayerID})를 첫 턴으로 설정합니다.");
        }
        
        if (turnPlayerID != -1)
        {
            Debug.Log($"[ApplyPendingTurnControl] 보류된 턴 정보 적용. CurrentTurnPlayerID: {turnPlayerID}");
            ApplyTurnControlToAllPlayers(turnPlayerID, 30); // 기본 턴 시간 30초
        }
        else
        {
            Debug.LogWarning($"[ApplyPendingTurnControl] ⚠️ 턴 플레이어 ID를 결정할 수 없습니다. 플레이어 목록이 비어있거나 설정되지 않았습니다.");
        }
    }

    private void ProcessPlayerMoveNotify(PacketReader reader)
    {
        try
        {
            // ID 400: [PlayerID (4 bytes, int)] + [Position (12 bytes, 3 floats)] + [Rotation (16 bytes, 4 floats)]
            int playerID = reader.ReadInt32();
            Vector3 position = new Vector3(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat());
            Quaternion rotation = new Quaternion(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat());

            // [핵심 수정] ConnectedUserID를 단일 소스로 사용하여 로컬 플레이어 판단
            bool isLocalPlayer = (playerID == ConnectedUserID && ConnectedUserID != -1);
            Debug.Log($"[PlayerMove Notify] ✅ 이동 동기화 수신! PlayerID: {playerID}, Pos: {position}, ConnectedUserID: {ConnectedUserID}, isLocalPlayer: {isLocalPlayer}");

            // 로컬 플레이어는 자신의 이동을 네트워크로부터 받지 않음 (직접 입력으로 제어)
            if (isLocalPlayer)
            {
                Debug.Log($"[PlayerMove Notify] ⚠️ 로컬 플레이어({playerID})의 이동 알림을 무시합니다. 직접 입력으로 제어됩니다.");
                return;
            }

            if (PlayerManager.Instance != null)
            {
                PlayerManager.Instance.UpdatePlayerPosition(playerID, position, rotation);
            }
            else
            {
                Debug.LogWarning($"[PlayerMove Notify] ⚠️ PlayerManager.Instance가 null입니다!");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ProcessPlayerMoveNotify] PlayerMove Notify 처리 중 오류: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private void ProcessPlayerAimNotify(PacketReader reader)
    {
        try
        {
            // ID 410: [PlayerID (4 bytes, int)] + [AimAngle (4 bytes, float)]
            int playerID = reader.ReadInt32();
            float aimAngle = reader.ReadFloat();

            Debug.Log($"[PlayerAim Notify] PlayerID: {playerID}, AimAngle: {aimAngle}");

            // TODO: PlayerController에 AimAngle 전달
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ProcessPlayerAimNotify] PlayerAim Notify 처리 중 오류: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private void ProcessPlayerFireNotify(PacketReader reader)
    {
        try
        {
            // ID 420: [PlayerID (4 bytes, int)] + [FirePoint (12 bytes, 3 floats)] + [FireRotation (16 bytes, 4 floats)]
            int playerID = reader.ReadInt32();
            Vector3 firePoint = new Vector3(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat());
            Quaternion fireRotation = new Quaternion(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat());

            // [핵심 수정] ConnectedUserID를 단일 소스로 사용하여 로컬 플레이어 판단
            bool isLocalPlayer = (playerID == ConnectedUserID && ConnectedUserID != -1);
            Debug.Log($"[PlayerFire Notify] ✅✅✅✅✅ ID 420 수신! PlayerID: {playerID}, FirePoint: {firePoint}, FireRotation: {fireRotation}");
            Debug.Log($"[PlayerFire Notify] ConnectedUserID: {ConnectedUserID}, MyPlayerID: {(PlayerManager.Instance != null ? PlayerManager.Instance.MyPlayerID : -1)}, isLocalPlayer: {isLocalPlayer}");

            // 로컬 플레이어는 자신의 발사를 네트워크로부터 받지 않음 (직접 입력으로 제어)
            if (isLocalPlayer)
            {
                Debug.Log($"[PlayerFire Notify] ⚠️ 로컬 플레이어({playerID})의 발사 알림을 무시합니다. 직접 입력으로 제어됩니다.");
                return;
            }

            Debug.Log($"[PlayerFire Notify] ⚠️⚠️⚠️ 포탄 생성 시작! PlayerID {playerID}의 Fire() 호출 예정 (동기화된 위치/회전 사용)");

            if (PlayerManager.Instance != null)
            {
                var playerObj = PlayerManager.Instance.GetPlayerById(playerID);
                if (playerObj != null)
                {
                    var controller = playerObj.GetComponent<PlayerController>();
                    if (controller != null)
                    {
                        Debug.Log($"[PlayerFire Notify] ✅ ID {playerID}의 탱크에 발사 명령 전달 시작 (동기화된 위치/회전 사용)");
                        // 동기화된 firePoint와 fireRotation을 사용하여 발사
                        controller.Fire(firePoint, fireRotation);
                        Debug.Log($"[PlayerFire Notify] ✅ ID {playerID}의 탱크에 발사 명령 전달 완료");
                    }
                    else
                    {
                        Debug.LogWarning($"[PlayerFire Notify] ❌ ID {playerID}의 PlayerController를 찾을 수 없습니다.");
                    }
                }
                else
                {
                    Debug.LogWarning($"[PlayerFire Notify] ❌ ID {playerID}에 해당하는 플레이어를 찾을 수 없습니다.");
                    Debug.LogWarning($"[PlayerFire Notify] 현재 _gamePlayerList:");
                    foreach (var playerInfo in _gamePlayerList)
                    {
                        Debug.LogWarning($"[PlayerFire Notify]   - UserID: {playerInfo.UserID}, UserName: {playerInfo.UserName}");
                    }
                }
            }
            else
            {
                Debug.LogWarning($"[PlayerFire Notify] ❌ PlayerManager.Instance가 null입니다.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ProcessPlayerFireNotify] PlayerFire Notify 처리 중 오류: {ex.Message}\n{ex.StackTrace}");
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
        // 방 이름 저장 (나중에 RoomManager에 전달하기 위해)
        PendingRoomName = roomName;
        
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
        // 자기 턴일 때만 이동 패킷 전송
        if (!IsMyTurn())
        {
            Debug.LogWarning("[SendMoveRequest] 자기 턴이 아니므로 이동 패킷을 전송하지 않습니다.");
            return;
        }

        byte[] movePacket = MakeMoveRequestPacket(position, rotation);
        
        // 패킷 구조 확인용 로그 (첫 번째 전송 시에만)
        if (Time.frameCount % 60 == 0) // 1초마다 한 번씩만
        {
            Debug.Log($"[SendMoveRequest] ID 500 패킷 구조 확인 - 총 길이: {movePacket.Length}바이트, PlayerID: {PlayerManager.Instance.MyPlayerID}, Pos: {position}");
            Debug.Log($"[SendMoveRequest] 패킷 헤더: [0-1] Length={BitConverter.ToUInt16(movePacket, 0)}, [2-3] ID={BitConverter.ToUInt16(movePacket, 2)}");
        }
        
        SendPacket(movePacket);
        // 주석 처리: 이동 요청은 초당 여러 번 발생하므로, 로그를 너무 자주 출력하면 성능에 영향
        // Debug.Log($"ID 500 (Move Req) 패킷이 M3 서버로 전송되었습니다. Pos: {position}");
    }

    // Fire 요청 (ID 600)
    public byte[] MakeFireRequestPacket(Vector3 firePoint, Quaternion fireRotation)
    {
        const ushort MESSAGE_ID = 600;
        // Header(4) + ID(4) + FirePoint(12) + FireRotation(16) = 총 36바이트
        const ushort TOTAL_LENGTH = 36;

        PacketBuilder builder = new PacketBuilder();
        builder.WriteHeader(MESSAGE_ID, TOTAL_LENGTH);
        builder.WriteInt32(PlayerManager.Instance.MyPlayerID); // ID 포함

        builder.WriteFloat(firePoint.x);
        builder.WriteFloat(firePoint.y);
        builder.WriteFloat(firePoint.z);

        builder.WriteFloat(fireRotation.x);
        builder.WriteFloat(fireRotation.y);
        builder.WriteFloat(fireRotation.z);
        builder.WriteFloat(fireRotation.w);

        return builder.GetPacket();
    }

    public void SendFireRequest(Vector3 firePoint, Quaternion fireRotation)
    {
        // 자기 턴일 때만 발사 패킷 전송
        if (!IsMyTurn())
        {
            Debug.LogWarning("[SendFireRequest] 자기 턴이 아니므로 발사 패킷을 전송하지 않습니다.");
            return;
        }

        byte[] firePacket = MakeFireRequestPacket(firePoint, fireRotation);
        
        // 패킷 구조 확인용 로그
        Debug.Log($"[SendFireRequest] ========== ID 600 패킷 전송 시작 ==========");
        Debug.Log($"[SendFireRequest] 패킷 구조 - 총 길이: {firePacket.Length}바이트, PlayerID: {PlayerManager.Instance.MyPlayerID}, FirePoint: {firePoint}");
        Debug.Log($"[SendFireRequest] 패킷 헤더: [0-1] Length={BitConverter.ToUInt16(firePacket, 0)}, [2-3] ID={BitConverter.ToUInt16(firePacket, 2)}");
        
        // SendPacket 메서드를 사용하여 전송 (이 메서드는 연결 상태 확인과 예외 처리를 포함함)
        Debug.Log($"[SendFireRequest] SendPacket 메서드를 통해 전송 시작...");
        SendPacket(firePacket);
        Debug.Log($"[SendFireRequest] ✅✅✅ ID 600 패킷 전송 완료! (SendPacket 호출됨)");
        Debug.Log($"[SendFireRequest] ⚠️⚠️⚠️ 서버로부터 ID 420 (PlayerFire Notify), ID 421 (Fire Result Notify), ID 422 (Death Notify), ID 430 (Turn Start Notify) 패킷 수신 대기 중...");
        Debug.Log($"[SendFireRequest] ⚠️⚠️⚠️ 만약 ID 420이 수신되지 않으면 서버가 ID 600을 받지 못했거나 ID 420을 방송하지 않는 것입니다!");
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

    private void ProcessFireResultNotify(PacketReader reader)
    {
        try
        {
            // ID 421: [ShooterID (4 bytes, int)] + [VictimID (4 bytes, int)] + [Damage (4 bytes, int)] + [VictimNewHP (4 bytes, int)]
            int shooterID = reader.ReadInt32();
            int victimID = reader.ReadInt32();
            int damage = reader.ReadInt32();
            int victimNewHP = reader.ReadInt32();

            Debug.Log($"[Fire Result Notify] ✅✅✅✅✅ ID 421 수신! Shooter: {shooterID}, Victim: {victimID}, Damage: {damage}, NewHP: {victimNewHP}");
            Debug.Log($"[Fire Result Notify] 내 UserID: {ConnectedUserID}, MyPlayerID: {(PlayerManager.Instance != null ? PlayerManager.Instance.MyPlayerID : -1)}");
            Debug.Log($"[Fire Result Notify] ⚠️⚠️⚠️ 체력바 업데이트 시작! VictimID {victimID}에게 {damage} 데미지 적용 예정");

            // 피해자에게 데미지 적용
            if (PlayerManager.Instance != null)
            {
                // victimID는 서버에서 보낸 FD이므로, 이것이 UserID와 일치해야 함
                var victimObj = PlayerManager.Instance.GetPlayerById(victimID);
                if (victimObj != null)
                {
                    var controller = victimObj.GetComponent<PlayerController>();
                    if (controller != null)
                    {
                        // 서버에서 보낸 NewHP로 직접 설정 (중복 처리 방지)
                        float currentHP = controller.currentHealth;
                        if (Mathf.Abs(currentHP - victimNewHP) > 0.1f)
                        {
                            Debug.Log($"[Fire Result] ID {victimID}에게 {damage} 데미지 적용 시작 (현재 HP: {currentHP}, 서버 NewHP: {victimNewHP})");
                            // 서버의 NewHP로 직접 설정
                            controller.currentHealth = victimNewHP;
                            if (controller.hpBarSlider != null)
                            {
                                // [수정] hpBarSlider.maxValue가 100이므로, value는 0~100 사이의 값이어야 함
                                // victimNewHP는 이미 0~100 사이의 값이므로 그대로 사용
                                controller.hpBarSlider.value = victimNewHP;
                            }
                            Debug.Log($"[Fire Result] ID {victimID}에게 데미지 적용 완료. 남은 HP: {victimNewHP} (실제 HP: {controller.currentHealth})");
                        }
                        else
                        {
                            Debug.Log($"[Fire Result] ID {victimID}의 HP가 이미 {victimNewHP}로 동기화되어 있습니다. 중복 처리 건너뜀.");
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[Fire Result] ID {victimID}의 PlayerController를 찾을 수 없습니다.");
                    }
                }
                else
                {
                    Debug.LogWarning($"[Fire Result] ID {victimID}에 해당하는 플레이어를 찾을 수 없습니다.");
                    // _gamePlayerList에서 victimID 찾기 시도
                    bool foundInList = false;
                    foreach (var playerInfo in _gamePlayerList)
                    {
                        if (playerInfo.UserID == victimID)
                        {
                            Debug.LogWarning($"[Fire Result] _gamePlayerList에서 찾음: UserID={playerInfo.UserID}, UserName={playerInfo.UserName}");
                            foundInList = true;
                            break;
                        }
                    }
                    if (!foundInList)
                    {
                        Debug.LogWarning($"[Fire Result] _gamePlayerList에도 victimID {victimID}가 없습니다.");
                    }
                }
            }
            else
            {
                Debug.LogWarning($"[Fire Result] PlayerManager.Instance가 null입니다.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ProcessFireResultNotify] Fire Result Notify 처리 중 오류: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private void ProcessPlayerDeathNotify(PacketReader reader)
    {
        try
        {
            // ID 422: [VictimID (4 bytes, int)] + [KillerID (4 bytes, int)]
            int victimID = reader.ReadInt32();
            int killerID = reader.ReadInt32();

            Debug.Log($"[Player Death Notify] ✅✅✅ ID 422 수신! Victim: {victimID}, Killer: {killerID}");
            Debug.Log($"[Player Death Notify] 내 UserID: {ConnectedUserID}, MyPlayerID: {(PlayerManager.Instance != null ? PlayerManager.Instance.MyPlayerID : -1)}");

            // 사망한 플레이어 처리
            if (PlayerManager.Instance != null)
            {
                var victimObj = PlayerManager.Instance.GetPlayerById(victimID);
                if (victimObj != null)
                {
                    var controller = victimObj.GetComponent<PlayerController>();
                    if (controller != null)
                    {
                        // 사망 처리 (HP를 0으로 만들어 Die() 호출)
                        // Die() 메서드 내부에서 CheckGameEnd()가 호출되므로 여기서는 호출하지 않음
                        if (!controller.isDead)
                        {
                            Debug.Log($"[Player Death] ID {victimID} 사망 처리 시작 (TakeDamage 호출)");
                            controller.TakeDamage(9999f);
                            Debug.Log($"[Player Death] ID {victimID} 사망 처리 완료 (Die()에서 CheckGameEnd() 호출됨)");
                        }
                        else
                        {
                            Debug.LogWarning($"[Player Death] ID {victimID}는 이미 사망 상태입니다.");
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[Player Death] ID {victimID}의 PlayerController를 찾을 수 없습니다.");
                    }
                }
                else
                {
                    Debug.LogWarning($"[Player Death] ID {victimID}에 해당하는 플레이어를 찾을 수 없습니다.");
                }
            }
            else
            {
                Debug.LogWarning($"[Player Death] PlayerManager.Instance가 null입니다.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ProcessPlayerDeathNotify] Player Death Notify 처리 중 오류: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private void ProcessGameEndNotify(PacketReader reader)
    {
        try
        {
            // ID 440: [WinnerID (4 bytes, int)]
            int winnerID = reader.ReadInt32();

            Debug.Log($"[Game End Notify] ID 440 수신 - Winner: {winnerID}");

            // 게임 종료 UI 표시
            if (GameUI.Instance != null)
            {
                GameUI.Instance.ShowGameOver(winnerID);
                Debug.Log($"[Game End] 게임 종료! 승자: ID {winnerID}");
            }
            else
            {
                Debug.LogWarning("[Game End] GameUI.Instance가 null입니다.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ProcessGameEndNotify] Game End Notify 처리 중 오류: {ex.Message}\n{ex.StackTrace}");
        }
    }
}