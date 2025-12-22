using UnityEngine;
using TMPro;
using System.Collections;
using UnityEngine.UI;

public class PlayerController : MonoBehaviour
{
    [Header("Combat Settings")]
    public float maxHealth = 100f;
    public float currentHealth;
    public bool isDead = false;
    public Slider hpBarSlider;

    [Header("Fire Settings")]
    public TextMeshProUGUI nameTagText;
    public GameObject shellPrefab; // Shell 프리팹 연결
    public Transform firePoint;    // Barrel 끝에 만든 FirePoint 연결
    public float launchForce = 15f;

    [Header("UI Settings")]
    private GameObject _arrowIndicator; // 로컬 플레이어 탱크 위의 화살표 표시기

    [Header("Movement Settings")]
    public float moveSpeed = 5.0f;
    public float lerpSpeed = 25.0f; // 보간 속도

    [Header("Status")]
    public bool isLocalPlayer = false;
    private bool _canControl = false; // 현재 턴인지 여부
    private bool _hasFiredThisTurn = false; // 현재 턴에 발사했는지 여부를 기록하는 플래그 (중복 발사 방지)
    private int _turnTimeLimitSec = 0; // 턴 제한 시간
    private float _turnStartTime = 0f; // 턴 시작 시간

    private Vector3 _lastSentPosition;
    // 네트워크 동기화를 위한 목표 지점
    private Vector3 _targetPosition;
    private Quaternion _targetRotation;
    private bool _isTargetPositionInitialized = false; // 초기 위치가 설정되었는지 확인
    private float _initialSetupTime = 0f; // 초기 설정 시간 (순간이동 방지)
    private const float INITIAL_SETUP_DURATION = 0.5f; // 초기 설정 후 0.5초 동안 위치 고정

    private Rigidbody _rb;

    // 네트워크 전송 주기 관리
    private float _lastSendTime = -1f; // -1로 초기화하여 게임 시작 직후 즉시 전송되지 않도록 함
    private const float SEND_INTERVAL = 0.05f; // 초당 20회 전송

    void Awake()
    {
        // nameTagText를 미리 찾아둠
        if (nameTagText == null)
        {
            nameTagText = GetComponentInChildren<TextMeshProUGUI>(true);
        }
        
        // 초기 회전을 기본값으로 설정 (SetNetworkPosition이 나중에 덮어쓸 수 있음)
        // 모든 탱크는 오른쪽(90도) 또는 왼쪽(-90도)만 봄
        transform.rotation = Quaternion.Euler(0, 90f, 0);
        _targetRotation = transform.rotation;
    }

    void Start()
    {
        _rb = GetComponent<Rigidbody>();

        // 초기 위치 설정 (SetNetworkPosition이 호출되지 않은 경우에만)
        if (!_isTargetPositionInitialized)
        {
            _targetPosition = transform.position;
        }

        // [수정] 모든 탱크는 오른쪽(90도) 또는 왼쪽(-90도)만 봄
        // SetNetworkPosition이 호출된 경우 그 회전을 강제로 적용
        // 모든 클라이언트에서 동일한 회전이 적용되도록 함
        if (_isTargetPositionInitialized)
        {
            // SetNetworkPosition이 이미 호출된 경우, 설정된 회전을 강제로 적용
            // 회전 Y가 정확히 90도 또는 -90도인지 확인
            float targetY = _targetRotation.eulerAngles.y;
            Quaternion normalizedRot;
            if (targetY > 45f && targetY < 135f)
            {
                normalizedRot = Quaternion.Euler(0, 90f, 0);
            }
            else if (targetY > 225f && targetY < 315f)
            {
                normalizedRot = Quaternion.Euler(0, -90f, 0);
            }
            else
            {
                normalizedRot = Quaternion.Euler(0, 90f, 0); // 기본값
            }
            transform.rotation = normalizedRot;
            _targetRotation = normalizedRot; // 정규화된 회전으로 업데이트
            Debug.Log($"[PlayerController] Start: SetNetworkPosition에서 설정된 회전 강제 적용 - {normalizedRot.eulerAngles}, 현재 회전: {transform.rotation.eulerAngles}");
        }
        else
        {
            // SetNetworkPosition이 아직 호출되지 않은 경우, Awake()에서 설정한 기본값 유지
            // (PlayerManager.AddPlayer에서 SetNetworkPosition이 호출될 때까지 대기)
            // 회전 Y가 정확히 90도인지 확인
            transform.rotation = Quaternion.Euler(0, 90f, 0);
            _targetRotation = transform.rotation;
            Debug.Log($"[PlayerController] Start: SetNetworkPosition 대기 중, 기본 회전 설정 - {transform.rotation.eulerAngles}");
        }
        // 리지드바디 설정 확인
        if (_rb != null)
        {
            if (isLocalPlayer)
            {
                // 로컬 플레이어: 물리 시뮬레이션 사용 (충돌 감지 가능)
                _rb.isKinematic = false;
                _rb.useGravity = false; // 중력 사용 안 함
                _rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ; // X, Z 회전 고정
                _rb.collisionDetectionMode = CollisionDetectionMode.Continuous; // 연속 충돌 감지 (빠른 이동 시 충돌 감지 개선)
                _rb.interpolation = RigidbodyInterpolation.Interpolate; // 부드러운 움직임
            }
            else
            {
                // 원격 플레이어: Kinematic으로 설정 (코드에서 위치 직접 제어)
                _rb.isKinematic = true;
            }
            Debug.Log($"[PlayerController] Rigidbody 설정 - isKinematic: {_rb.isKinematic}, isLocalPlayer: {isLocalPlayer}, collisionDetection: {_rb.collisionDetectionMode}");
        }
        
        // Collider 설정 확인 및 수정
        Collider[] colliders = GetComponentsInChildren<Collider>();
        foreach (Collider col in colliders)
        {
            if (col != null)
            {
                // 탱크의 Collider는 isTrigger = false여야 물리 충돌이 작동함
                if (col.isTrigger)
                {
                    Debug.LogWarning($"[PlayerController] Collider '{col.name}'가 isTrigger=true입니다. 탱크 간 충돌을 위해 false로 변경합니다.");
                    col.isTrigger = false;
                }
            }
        }

        // nameTagText 초기화 (SetPlayerID가 호출되기 전에 미리 찾아둠)
        if (nameTagText == null)
        {
            nameTagText = GetComponentInChildren<TextMeshProUGUI>(true);
        }

        currentHealth = maxHealth;

        if (hpBarSlider != null)
        {
            hpBarSlider.maxValue = maxHealth;
            hpBarSlider.value = currentHealth;
        }
    }
    public void TakeDamage(float amount)
    {
        if (isDead) return;

        currentHealth -= amount;
        
        // HP가 0 이하로 내려가지 않도록 제한
        if (currentHealth < 0)
        {
            currentHealth = 0;
        }

        // HP Bar UI 업데이트
        if (hpBarSlider != null)
        {
            hpBarSlider.value = currentHealth;
            Debug.Log($"[TakeDamage] {this.name} - HP Bar 업데이트: {currentHealth}/{maxHealth} (Slider value: {hpBarSlider.value})");
        }
        else
        {
            Debug.LogWarning($"[TakeDamage] {this.name} - hpBarSlider가 null입니다!");
        }

        Debug.Log($"[ID {this.name}] 남은 체력: {currentHealth}");

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    private void Die()
    {
        if (isDead)
        {
            Debug.LogWarning($"[ID {this.name}] 이미 사망 상태입니다.");
            return;
        }

        isDead = true;
        // 사망 시 HP Bar도 0으로 확정
        if (hpBarSlider != null) hpBarSlider.value = 0;

        gameObject.SetActive(false);
        Debug.Log($"[ID {this.name}] 사망 처리 완료 - isDead: {isDead}, activeSelf: {gameObject.activeSelf}");
        
        // 게임 종료 체크 (Die() 완료 후 호출)
        if (GameManager.Instance != null)
        {
            GameManager.Instance.CheckGameEnd();
        }
    }
    // 플레이어별 고유 색상 배열 (최대 10명까지 지원)
    private static readonly Color[] PlayerColors = new Color[]
    {
        new Color(0.2f, 0.4f, 1.0f),    // 파란색 (0)
        new Color(1.0f, 0.2f, 0.2f),    // 빨간색 (1)
        new Color(0.2f, 1.0f, 0.2f),    // 초록색 (2)
        new Color(1.0f, 0.8f, 0.0f),    // 노란색 (3)
        new Color(1.0f, 0.4f, 0.8f),    // 분홍색 (4)
        new Color(0.6f, 0.2f, 1.0f),    // 보라색 (5)
        new Color(0.0f, 1.0f, 1.0f),    // 청록색 (6)
        new Color(1.0f, 0.6f, 0.2f),    // 주황색 (7)
        new Color(0.4f, 0.8f, 0.4f),    // 연두색 (8)
        new Color(0.8f, 0.6f, 0.4f),    // 갈색 (9)
    };

    // 플레이어 ID를 기반으로 색상을 가져오는 메서드
    private Color GetPlayerColor(int playerID)
    {
        // 플레이어 목록에서 인덱스 찾기
        int playerIndex = -1;
        if (NetworkManager.Instance != null && NetworkManager.Instance._gamePlayerList != null)
        {
            for (int i = 0; i < NetworkManager.Instance._gamePlayerList.Count; i++)
            {
                if (NetworkManager.Instance._gamePlayerList[i].UserID == playerID)
                {
                    playerIndex = i;
                    break;
                }
            }
        }

        // 인덱스를 찾지 못한 경우 플레이어 ID를 해시하여 색상 결정
        if (playerIndex == -1)
        {
            playerIndex = Mathf.Abs(playerID) % PlayerColors.Length;
            Debug.LogWarning($"[ApplyTankColors] 플레이어 목록에서 인덱스를 찾지 못했습니다. PlayerID: {playerID}, 해시 인덱스: {playerIndex}");
        }
        else
        {
            Debug.Log($"[ApplyTankColors] 플레이어 인덱스 찾음: PlayerID: {playerID}, Index: {playerIndex}");
        }

        // 인덱스를 색상 배열 크기로 모듈로 연산하여 안전하게 색상 선택
        int colorIndex = playerIndex % PlayerColors.Length;
        Color selectedColor = PlayerColors[colorIndex];
        
        Debug.Log($"[ApplyTankColors] PlayerID {playerID} -> ColorIndex {colorIndex} -> Color {selectedColor}");
        return selectedColor;
    }

    private void ApplyTankColors()
    {
        // 자식뿐만 아니라 모든 하위 렌더러를 가져옵니다.
        Renderer[] rens = GetComponentsInChildren<Renderer>(true);

        Debug.Log($"[ApplyTankColors] 찾은 Renderer 개수: {rens.Length}, isLocalPlayer: {isLocalPlayer}, PlayerID: {playerID}");

        // 자기 자신의 Renderer 찾기 (메인 몸통)
        Renderer mainBodyRenderer = GetComponent<Renderer>();

        // 플레이어별 고유 색상 가져오기
        Color playerColor = GetPlayerColor(playerID);

        foreach (Renderer r in rens)
        {
            if (r == null) continue;

            string objName = r.gameObject.name;
            string n = objName.ToLower().Trim();

            // Canvas나 UI 관련 Renderer는 제외
            if (n.Contains("canvas") || n.Contains("text") || objName.Contains("(TMP)"))
            {
                Debug.Log($"[ApplyTankColors] UI Renderer 제외: {objName}");
                continue;
            }

            // [핵심] r.material을 호출하는 순간, 이 객체만을 위한 '복제본 머티리얼'이 생성됩니다.
            // 이를 통해 내 탱크(파랑)와 남의 탱크(빨강)가 같은 머티리얼을 써도 독립된 색을 가집니다.
            Material instancedMat = r.material;
            if (instancedMat == null)
            {
                Debug.LogWarning($"[ApplyTankColors] Material이 null입니다: {objName}");
                continue;
            }

            Color targetColor = Color.white;
            bool shouldApply = false;

            // 메인 몸통 판정: 자기 자신의 Renderer이거나 이름에 특정 키워드가 포함된 경우
            bool isMainBody = (r == mainBodyRenderer) ||
                             n.Contains("playercharacter") ||
                             (n.Contains("cube") && !n.Contains("turret") && !n.Contains("barrel"));

            if (isMainBody)
            {
                // 플레이어별 고유 색상 사용
                targetColor = playerColor;
                shouldApply = true;
                Debug.Log($"[ApplyTankColors] 몸통 색상 적용: {objName} -> {targetColor} (PlayerID: {playerID})");
            }
            else if (n.Contains("turret"))
            {
                targetColor = Color.gray;
                shouldApply = true;
                Debug.Log($"[ApplyTankColors] 포탑 색상 적용: {objName} -> {targetColor}");
            }
            else if (n.Contains("barrel"))
            {
                targetColor = Color.black;
                shouldApply = true;
                Debug.Log($"[ApplyTankColors] 포신 색상 적용: {objName} -> {targetColor}");
            }
            else
            {
                Debug.Log($"[ApplyTankColors] 매칭되지 않은 Renderer: {objName} (Transform: {r.transform.name}, Parent: {(r.transform.parent != null ? r.transform.parent.name : "null")})");
            }

            if (!shouldApply) continue;

            // URP 대응 및 일반 표준 쉐이더 대응
            if (instancedMat.HasProperty("_BaseColor"))
            {
                instancedMat.SetColor("_BaseColor", targetColor);
                Debug.Log($"[ApplyTankColors] _BaseColor 설정: {objName} -> {targetColor}");
            }
            else if (instancedMat.HasProperty("_Color"))
            {
                instancedMat.SetColor("_Color", targetColor);
                Debug.Log($"[ApplyTankColors] _Color 설정: {objName} -> {targetColor}");
            }
            else
            {
                instancedMat.color = targetColor;
                Debug.Log($"[ApplyTankColors] color 설정: {objName} -> {targetColor}");
            }
        }
    }
    /// <summary>
    /// 외부(PlayerManager)에서 서버 패킷을 받아 목표 위치를 갱신할 때 사용
    /// </summary>
    public void SetNetworkPosition(Vector3 pos, Quaternion rot)
    {
        // [중요] transform.position을 직접 설정하여 즉시 위치를 업데이트
        // 이렇게 하면 InterpolatePosition이 잘못된 초기 위치에서 보간하지 않습니다
        
        bool isInitialSetup = !_isTargetPositionInitialized;
        
        // [핵심 수정] 회전을 정확히 90도 또는 -90도로 정규화
        float rotY = rot.eulerAngles.y;
        Quaternion normalizedRot;
        if (rotY > 45f && rotY < 135f)
        {
            normalizedRot = Quaternion.Euler(0, 90f, 0);
        }
        else if (rotY > 225f && rotY < 315f)
        {
            normalizedRot = Quaternion.Euler(0, -90f, 0);
        }
        else
        {
            // 기본값: 오른쪽
            normalizedRot = Quaternion.Euler(0, 90f, 0);
        }
        
        // [핵심 수정] 초기 설정 시 모든 플레이어(로컬/원격)의 위치와 회전을 즉시 설정
        // 이후에는 로컬 플레이어는 입력으로만 제어, 원격 플레이어는 네트워크 위치로 제어
        if (isInitialSetup)
        {
            // 초기 설정 시: 모든 플레이어의 위치와 회전을 즉시 설정 (순간이동 방지)
            transform.position = pos;
            transform.rotation = normalizedRot;
            Debug.Log($"[SetNetworkPosition] 초기 설정: 모든 플레이어 위치/회전 즉시 설정 - Pos: {pos}, Rot: {normalizedRot.eulerAngles}, isLocalPlayer: {isLocalPlayer}");
        }
        else
        {
            // 초기 설정 이후: 로컬 플레이어는 위치를 무시, 원격 플레이어만 위치 업데이트
            if (!isLocalPlayer)
            {
                // 원격 플레이어: 위치와 회전 모두 업데이트
                transform.position = pos;
                transform.rotation = normalizedRot;
            }
            else
            {
                // 로컬 플레이어: 회전만 업데이트 (위치는 입력으로 제어)
                transform.rotation = normalizedRot;
            }
        }
        
        _targetPosition = pos;
        _targetRotation = normalizedRot; // 정규화된 회전 저장
        
        if (isInitialSetup)
        {
            _isTargetPositionInitialized = true; // 초기 위치가 설정되었음을 표시
            _initialSetupTime = Time.time; // 초기 설정 시간 기록
            Debug.Log($"[SetNetworkPosition] 초기 설정 완료 - PlayerID: {playerID}, Pos: {pos}, Rot: {normalizedRot.eulerAngles}, isLocalPlayer: {isLocalPlayer}");
        }
        
        Debug.Log($"[SetNetworkPosition] PlayerID: {playerID}, 위치 설정: {pos}, 회전: {normalizedRot.eulerAngles}, isLocalPlayer: {isLocalPlayer}, isInitialSetup: {isInitialSetup}");
    }

    private void Update()
    {
        // 자기 턴일 때만 입력 처리
        if (isLocalPlayer && _canControl)
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                CmdFire(); // 내 화면에서 발사 및 서버 알림
            }
        }
        
        // 화살표 표시기 업데이트
        UpdateArrowIndicator();
    }
    
    void LateUpdate()
    {
        // [핵심 수정] 모든 프레임에서 회전 Y가 정확히 90도 또는 -90도인지 확인하고 수정
        float currentY = transform.rotation.eulerAngles.y;
        
        // 90도 또는 -90도(270도)가 아니면 수정
        bool isValidRotation = (Mathf.Approximately(currentY, 90f) || Mathf.Approximately(currentY, 270f) || 
                               (currentY > 89f && currentY < 91f) || (currentY > 269f && currentY < 271f));
        
        if (!isValidRotation)
        {
            // _targetRotation을 기준으로 올바른 방향 설정
            float targetY = _targetRotation.eulerAngles.y;
            Quaternion correctRotation;
            
            if (targetY > 45f && targetY < 135f)
            {
                correctRotation = Quaternion.Euler(0, 90f, 0);
            }
            else if (targetY > 225f && targetY < 315f)
            {
                correctRotation = Quaternion.Euler(0, -90f, 0);
            }
            else
            {
                // 현재 회전을 기준으로 가장 가까운 방향 선택
                if (currentY > 180f)
                {
                    correctRotation = Quaternion.Euler(0, -90f, 0);
                }
                else
                {
                    correctRotation = Quaternion.Euler(0, 90f, 0);
                }
            }
            
            transform.rotation = correctRotation;
            Debug.Log($"[PlayerController] LateUpdate: 회전 Y 수정 - {currentY} -> {correctRotation.eulerAngles.y}, PlayerID: {playerID}");
        }
    }

    void FixedUpdate()
    {
        if (isLocalPlayer)
        {
            // 자기 턴일 때만 이동 가능
            if (_canControl)
            {
                // 1. 내가 조작하는 경우: 입력 처리 및 물리 이동
                HandleLocalMovement();

                // 2. 서버로 내 위치 전송 (주기적)
                SyncPositionToServer();
            }
        }
        else
        {
            // 3. 남이 조작하는 경우: 목표 위치로 부드럽게 이동 (Interpolation)
            InterpolatePosition();
        }
    }
    private void CmdFire()
    {
        // [핵심 수정] 조작 불가능하거나, 이미 발사했다면 추가 발사 방지
        // 중복 패킷으로 인해 컨트롤이 다시 활성화되더라도, 한 턴에 한 번만 발사하도록 보장
        if (!_canControl || _hasFiredThisTurn)
        {
            Debug.LogWarning($"[PlayerController] 발사 요청 무시: canControl={_canControl}, hasFiredThisTurn={_hasFiredThisTurn}");
            return;
        }
        
        // 발사 기록을 true로 설정 (한 턴에 한 번만 발사하도록 보장)
        _hasFiredThisTurn = true;
        
        Fire(); // 내 화면에서 즉시 발사
        
        // 서버 발사 패킷 전송 (ID 600)
        if (NetworkManager.Instance != null && firePoint != null)
        {
            NetworkManager.Instance.SendFireRequest(firePoint.position, firePoint.rotation);
        }
        
        // [핵심 수정] 발사 요청을 보낸 직후, 즉시 컨트롤을 비활성화하여 추가 조작을 막습니다.
        // 이렇게 하면 서버의 턴 전환 응답을 기다리는 시간 동안 추가 발사를 할 수 없게 됩니다.
        // 레이스 컨디션 문제를 해결하여 한 턴에 한 발만 발사되는 것을 보장합니다.
        SetCanControl(false);
        Debug.Log("[PlayerController] 포탄 발사 후 컨트롤을 즉시 비활성화합니다. 서버의 턴 전환 알림(ID 430)을 기다립니다.");
    }

    public void Fire()
    {
        // 기본값으로 로컬 firePoint 사용
        if (firePoint != null)
        {
            Fire(firePoint.position, firePoint.rotation);
        }
        else
        {
            Debug.LogWarning($"[Fire] firePoint가 null입니다.");
        }
    }

    public void Fire(Vector3 firePosition, Quaternion fireRotation)
    {
        if (shellPrefab == null)
        {
            Debug.LogWarning($"[Fire] shellPrefab가 null입니다.");
            return;
        }

        Debug.Log($"[Fire] ✅ 발사 시작! PlayerID: {this.playerID}, isLocalPlayer: {isLocalPlayer}, Position: {firePosition}, Rotation: {fireRotation}");

        // 포탄을 firePosition에서 약간 앞으로 이동시켜 생성 (즉시 충돌 방지)
        Vector3 forward = fireRotation * Vector3.forward;
        Vector3 spawnPosition = firePosition + forward * 0.5f;
        GameObject shell = Instantiate(shellPrefab, spawnPosition, fireRotation);
        Debug.Log($"[Fire] ✅ 포탄 생성 완료! Shell: {shell.name}, Position: {spawnPosition}");

        // [중요] 생성된 포탄이 나(탱크)와 부딪히지 않게 설정 (Layer 설정이 안 되어 있을 때 유용)
        // 모든 탱크 Collider(자식 포함)와 포탄 Collider 간 충돌 무시
        Collider[] tankColliders = GetComponentsInChildren<Collider>();
        Collider shellCollider = shell.GetComponent<Collider>();
        
        if (shellCollider != null)
        {
            foreach (Collider tankCollider in tankColliders)
            {
                if (tankCollider != null && !tankCollider.isTrigger)
                {
                    // 탱크의 물리 Collider와 포탄 Collider 간 충돌 무시
                    Physics.IgnoreCollision(tankCollider, shellCollider);
                    Debug.Log($"[Fire] 탱크 Collider '{tankCollider.name}'와 포탄 Collider 간 충돌 무시 설정");
                }
            }
        }

        Rigidbody rb = shell.GetComponent<Rigidbody>();
        if (rb != null)
        {
            // velocity를 직접 설정하여 즉시 앞으로 이동하도록 함
            rb.linearVelocity = forward * launchForce;
        }
    }
    private void HandleLocalMovement()
    {
        // GetAxis 대신 GetAxisRaw를 사용해야 입력 즉시 -1, 0, 1로 값이 떨어집니다.
        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");

        Vector3 moveDirection = new Vector3(horizontal, 0f, vertical).normalized;

        if (moveDirection.magnitude >= 0.1f)
        {
            // 1. 위치 이동
            Vector3 nextPosition = _rb.position + moveDirection * moveSpeed * Time.fixedDeltaTime;
            _rb.MovePosition(nextPosition);

            // 2. 회전 즉시 고정 - 좌우만 가능 (오른쪽 90도 또는 왼쪽 -90도)
            if (horizontal < 0) // A 버튼 (왼쪽)
            {
                transform.rotation = Quaternion.Euler(0, -90f, 0);
            }
            else if (horizontal > 0) // D 버튼 (오른쪽)
            {
                transform.rotation = Quaternion.Euler(0, 90f, 0);
            }
            // vertical 입력은 무시 - 위/아래 방향 회전 제거
        }
    }
    // private void HandleLocalMovement()
    // {
    //     float horizontal = Input.GetAxisRaw("Horizontal"); // GetAxis 대신 GetAxisRaw를 사용하면 더 즉각적입니다.
    //     float vertical = Input.GetAxisRaw("Vertical");

    //     Vector3 moveDirection = new Vector3(horizontal, 0f, vertical).normalized;

    //     if (moveDirection.magnitude >= 0.1f)
    //     {
    //         // 1. 위치 이동
    //         Vector3 nextPosition = _rb.position + moveDirection * moveSpeed * Time.deltaTime;
    //         _rb.MovePosition(nextPosition);

    //         // 2. 회전 즉시 고정 로직
    //         if (horizontal < 0) // A 버튼 (왼쪽)
    //         {
    //             transform.rotation = Quaternion.Euler(0, -90f, 0);
    //         }
    //         else if (horizontal > 0) // D 버튼 (오른쪽)
    //         {
    //             transform.rotation = Quaternion.Euler(0, 90f, 0);
    //         }
    //         else if (vertical > 0) // W 버튼 (위)
    //         {
    //             transform.rotation = Quaternion.Euler(0, 0f, 0);
    //         }
    //         else if (vertical < 0) // S 버튼 (아래)
    //         {
    //             transform.rotation = Quaternion.Euler(0, 180f, 0);
    //         }
    //     }
    // }

    private void InterpolatePosition()
    {
        // [중요] _targetPosition이 초기화되지 않았으면 현재 위치를 유지
        // (게임 시작 직후 잘못된 위치로 이동하는 것을 방지)
        if (!_isTargetPositionInitialized)
        {
            // _targetPosition이 초기화되지 않았으면 현재 위치를 유지
            return;
        }
        
        // [추가 보호] 로컬 플레이어는 InterpolatePosition을 호출하지 않아야 함 (FixedUpdate에서 체크하지만 이중 보호)
        if (isLocalPlayer)
        {
            return;
        }
        
        // [핵심 수정] 초기 설정 직후 일정 시간 동안은 위치를 고정하여 순간이동 방지
        if (Time.time - _initialSetupTime < INITIAL_SETUP_DURATION)
        {
            // 초기 설정 직후에는 위치를 고정 (이미 SetNetworkPosition에서 설정됨)
            return;
        }
        
        // 1. 위치만 부드럽게 따라가게 합니다.
        transform.position = Vector3.Lerp(transform.position, _targetPosition, Time.deltaTime * lerpSpeed);

        // 2.  Y축 회전이 변하지 않도록 원천 차단 (0,0,0으로 고정)
        // transform.rotation = Quaternion.identity;

        // [핵심 수정] 회전 보간 시 오른쪽(90도) 또는 왼쪽(-90도)만 허용
        // 다른 방향으로 보간되는 것을 방지
        Quaternion targetRot = _targetRotation;
        float targetY = targetRot.eulerAngles.y;
        
        // Y축 회전을 90도 또는 -90도(270도)로 정규화
        if (targetY > 45f && targetY < 135f)
        {
            targetRot = Quaternion.Euler(0, 90f, 0); // 오른쪽
        }
        else if (targetY > 225f && targetY < 315f)
        {
            targetRot = Quaternion.Euler(0, -90f, 0); // 왼쪽 (270도 = -90도)
        }
        else
        {
            // 기본값: 오른쪽
            targetRot = Quaternion.Euler(0, 90f, 0);
        }
        
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * lerpSpeed);
        
        // [핵심 수정] 보간 후에도 정확히 90도 또는 -90도로 강제 설정
        float currentY = transform.rotation.eulerAngles.y;
        if (currentY > 45f && currentY < 135f)
        {
            transform.rotation = Quaternion.Euler(0, 90f, 0);
        }
        else if (currentY > 225f && currentY < 315f)
        {
            transform.rotation = Quaternion.Euler(0, -90f, 0);
        }
        else
        {
            // 기본값: 오른쪽
            transform.rotation = Quaternion.Euler(0, 90f, 0);
        }

        // 3.  크기가 늘어나는 현상을 방지하기 위해 스케일을 (1,1,1)로 고정합니다.
        transform.localScale = Vector3.one;
    }

    private void SyncPositionToServer()
    {
        // 게임 시작 후 최소 0.1초 대기 (탱크 위치가 완전히 초기화될 때까지)
        if (_lastSendTime < 0f)
        {
            _lastSendTime = Time.time + 0.1f; // 0.1초 후부터 전송 시작
            return;
        }
        
        if (Time.time > _lastSendTime + SEND_INTERVAL)
        {
            if (NetworkManager.Instance != null)
            {
                // NetworkManager.Instance.SendMoveRequest(_rb.position, transform.rotation);
                NetworkManager.Instance.SendMoveRequest(transform.position, transform.rotation);

            }
            _lastSendTime = Time.time;
        }
    }

    public int playerID { get; private set; } = -1; // 플레이어 ID 저장
    
    public void SetPlayerID(int id, bool isLocal)
    {
        this.playerID = id;
        this.isLocalPlayer = isLocal;
        Debug.Log($"[SetPlayerID] ID: {id}, isLocal: {isLocal}");
        
        // [핵심 수정] SetPlayerID 호출 시에도 회전이 올바르게 설정되었는지 확인
        // SetNetworkPosition이 이미 호출되었다면 그 회전을 유지
        if (_isTargetPositionInitialized && _targetRotation != Quaternion.identity)
        {
            transform.rotation = _targetRotation;
            Debug.Log($"[SetPlayerID] SetNetworkPosition에서 설정된 회전 적용 - {_targetRotation.eulerAngles}");
        }

        // nameTagText 찾기 (여러 방법으로 시도)
        if (nameTagText == null)
        {
            nameTagText = GetComponentInChildren<TextMeshProUGUI>(true); // 비활성화된 객체도 포함
        }

        // Canvas 하위에서 찾기
        if (nameTagText == null)
        {
            Canvas canvas = GetComponentInChildren<Canvas>(true);
            if (canvas != null)
            {
                nameTagText = canvas.GetComponentInChildren<TextMeshProUGUI>(true);
            }
        }

        // Text (TMP)라는 이름의 GameObject에서 찾기
        if (nameTagText == null)
        {
            Transform textTransform = transform.Find("Canvas/Text (TMP)");
            if (textTransform != null)
            {
                nameTagText = textTransform.GetComponent<TextMeshProUGUI>();
            }
        }

        // TextMeshPro 설정
        if (nameTagText != null)
        {
            // 폰트가 없거나 아틀라스가 없으면 강제로 폰트 할당
            TMP_FontAsset targetFont = nameTagText.font;

            // 폰트가 null이거나 아틀라스가 없으면 기본 폰트 사용
            if (targetFont == null || targetFont.atlasTexture == null)
            {
                // Resources에서 직접 로드 시도
                targetFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");

                // Resources에서 못 찾으면 TMP_Settings에서 가져오기
                if (targetFont == null)
                {
                    targetFont = TMP_Settings.defaultFontAsset;
                }

                // 그래도 없으면 모든 TMP_FontAsset을 찾아서 첫 번째 것 사용
                if (targetFont == null)
                {
                    TMP_FontAsset[] allFonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                    if (allFonts != null && allFonts.Length > 0)
                    {
                        targetFont = allFonts[0];
                        Debug.Log($"[SetPlayerID] Resources에서 폰트 찾음: {targetFont.name}");
                    }
                }

                if (targetFont != null)
                {
                    nameTagText.font = targetFont;
                    Debug.Log($"[SetPlayerID] 폰트 할당 완료: {targetFont.name}, 아틀라스: {(targetFont.atlasTexture != null ? "있음" : "없음")}");
                }
                else
                {
                    Debug.LogError($"[SetPlayerID] 폰트를 찾을 수 없습니다! Player ID: {id}");
                }
            }

            // 폰트가 할당되었는지 확인
            if (nameTagText.font == null)
            {
                Debug.LogError($"[SetPlayerID] 폰트가 여전히 null입니다! Player ID: {id}");
            }
            else
            {
                // 폰트 아틀라스 확인 및 재할당
                if (nameTagText.font.atlasTexture == null)
                {
                    Debug.LogWarning($"[SetPlayerID] 폰트 아틀라스가 없습니다. 폰트를 재할당합니다. Player ID: {id}");
                    TMP_FontAsset font = nameTagText.font;
                    nameTagText.font = null;
                    // 한 프레임 대기 후 다시 할당
                    StartCoroutine(ReassignFont(nameTagText, font, id));
                }
            }

            // [핵심 수정] 모든 클라이언트에서 동일한 ID 표시 보장
            // 플레이어 이름을 찾아서 표시 (없으면 ID만 표시)
            string displayName = $"Player {id}";
            if (NetworkManager.Instance != null && NetworkManager.Instance._gamePlayerList != null)
            {
                var playerInfo = NetworkManager.Instance._gamePlayerList.Find(p => p.UserID == id);
                if (playerInfo != null && !string.IsNullOrEmpty(playerInfo.UserName))
                {
                    displayName = $"{playerInfo.UserName} ({id})";
                }
            }
            
            nameTagText.text = displayName;
            // 로컬 플레이어는 청록색, 원격 플레이어는 흰색으로 표시 (가독성 향상)
            nameTagText.color = isLocal ? Color.cyan : Color.white;

            // TextMeshPro 컴포넌트를 완전히 재초기화
            // 1. 컴포넌트 비활성화/활성화
            nameTagText.enabled = false;

            // 2. Canvas도 재초기화
            Canvas canvas = nameTagText.GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                canvas.enabled = false;
            }

            // 3. GameObject 자체를 잠시 비활성화했다가 활성화
            GameObject textObj = nameTagText.gameObject;
            bool wasActive = textObj.activeSelf;
            if (wasActive)
            {
                textObj.SetActive(false);
            }

            // 한 프레임 대기 후 재활성화
            StartCoroutine(ReinitializeTextMeshPro(nameTagText, canvas, textObj, wasActive, id));

            Debug.Log($"[SetPlayerID] TextMeshPro 설정 완료: Player {id}, 폰트: {(nameTagText.font != null ? nameTagText.font.name : "null")}, 텍스트: {nameTagText.text}, 아틀라스: {(nameTagText.font != null && nameTagText.font.atlasTexture != null ? "있음" : "없음")}, GameObject: {nameTagText.gameObject.name}");
        }
        else
        {
            Debug.LogWarning($"[SetPlayerID] nameTagText를 찾을 수 없습니다. Player ID: {id}");
        }

        // 탱크 색상 적용
        ApplyTankColors();
        
        // 로컬 플레이어일 때 화살표 표시기 생성
        if (isLocal)
        {
            CreateArrowIndicator();
        }
    }
    
    // 로컬 플레이어 탱크 위에 화살표 표시기 생성
    private void CreateArrowIndicator()
    {
        // Canvas 찾기
        Canvas canvas = GetComponentInChildren<Canvas>(true);
        if (canvas == null)
        {
            Debug.LogWarning($"[PlayerController] Canvas를 찾을 수 없어 화살표 표시기를 생성할 수 없습니다. PlayerID: {playerID}");
            return;
        }
        
        // hpBarSlider 찾기
        if (hpBarSlider == null)
        {
            hpBarSlider = GetComponentInChildren<Slider>(true);
        }
        
        // 화살표 GameObject 생성
        GameObject arrowObj = new GameObject("ArrowIndicator");
        arrowObj.transform.SetParent(canvas.transform, false);
        
        // RectTransform 설정
        RectTransform arrowRect = arrowObj.AddComponent<RectTransform>();
        
        // hpBarSlider의 위치를 기준으로 체력바 위에 배치
        if (hpBarSlider != null)
        {
            RectTransform hpBarRect = hpBarSlider.GetComponent<RectTransform>();
            if (hpBarRect != null)
            {
                // 체력바 위에 배치 (Y 오프셋 추가)
                arrowRect.anchorMin = new Vector2(0.5f, 0.5f);
                arrowRect.anchorMax = new Vector2(0.5f, 0.5f);
                arrowRect.pivot = new Vector2(0.5f, 0.5f);
                arrowRect.anchoredPosition = new Vector2(hpBarRect.anchoredPosition.x, hpBarRect.anchoredPosition.y + 30f);
                arrowRect.sizeDelta = new Vector2(40f, 40f);
            }
            else
            {
                // hpBarSlider가 없으면 기본 위치에 배치
                arrowRect.anchorMin = new Vector2(0.5f, 0.5f);
                arrowRect.anchorMax = new Vector2(0.5f, 0.5f);
                arrowRect.pivot = new Vector2(0.5f, 0.5f);
                arrowRect.anchoredPosition = new Vector2(0f, 50f);
                arrowRect.sizeDelta = new Vector2(40f, 40f);
            }
        }
        else
        {
            // hpBarSlider가 없으면 기본 위치에 배치
            arrowRect.anchorMin = new Vector2(0.5f, 0.5f);
            arrowRect.anchorMax = new Vector2(0.5f, 0.5f);
            arrowRect.pivot = new Vector2(0.5f, 0.5f);
            arrowRect.anchoredPosition = new Vector2(0f, 50f);
            arrowRect.sizeDelta = new Vector2(40f, 40f);
        }
        
        // TextMeshPro로 화살표 표시
        TextMeshProUGUI arrowText = arrowObj.AddComponent<TextMeshProUGUI>();
        arrowText.text = "▼"; // 아래쪽 화살표 유니코드
        arrowText.fontSize = 36;
        arrowText.color = new Color(1f, 0.8f, 0f, 1f); // 밝은 노란색
        arrowText.alignment = TextAlignmentOptions.Center;
        arrowText.fontStyle = FontStyles.Bold;
        arrowText.raycastTarget = false; // 클릭 이벤트 방지
        
        // 폰트 설정
        if (nameTagText != null && nameTagText.font != null)
        {
            arrowText.font = nameTagText.font;
        }
        else
        {
            // 기본 폰트 사용
            arrowText.font = TMPro.TMP_Settings.defaultFontAsset;
        }
        
        // Billboard 스크립트 추가 (카메라를 향하도록)
        Billboard billboard = arrowObj.AddComponent<Billboard>();
        
        _arrowIndicator = arrowObj;
        
        // 초기 상태는 숨김 (자신의 턴이 아닐 때)
        _arrowIndicator.SetActive(false);
        
        Debug.Log($"[PlayerController] 화살표 표시기 생성 완료: PlayerID={playerID}");
    }
    
    // 화살표 표시기 업데이트 (턴 상태에 따라 표시/숨김)
    private void UpdateArrowIndicator()
    {
        if (_arrowIndicator == null) return;
        
        // 로컬 플레이어이고 현재 턴일 때만 표시
        bool shouldShow = isLocalPlayer && _canControl;
        _arrowIndicator.SetActive(shouldShow);
    }

    public void SetCanControl(bool canControl)
    {
        // 로컬 플레이어가 아니면 컨트롤 불가
        if (!isLocalPlayer)
        {
            _canControl = false;
            // 화살표 표시기 업데이트
            UpdateArrowIndicator();
            return;
        }
        
        // [수정] ApplyTurnControlToAllPlayers에서 이미 검증을 완료했으므로,
        // 여기서는 추가 검증 없이 canControl 값을 그대로 적용
        // 중복 패킷 방지는 ProcessTurnStartNotify에서 _currentTurnPlayerID를 업데이트하는 시점으로 처리
        _canControl = canControl;
        Debug.Log($"[PlayerController] SetCanControl: {canControl} (PlayerID: {this.name}, isLocalPlayer: {isLocalPlayer})");
        
        // 화살표 표시기 업데이트
        UpdateArrowIndicator();
    }

    public void OnTurnStart(int turnTimeLimitSec)
    {
        // [핵심 수정] 턴이 시작될 때, 발사 기록을 초기화
        // 중복 패킷으로 인해 OnTurnStart가 여러 번 호출되더라도, 발사 플래그는 초기화되어야 함
        _hasFiredThisTurn = false;
        
        // [수정] ApplyTurnControlToAllPlayers에서 이미 isCurrentTurn을 확인하고 호출하므로,
        // 여기서는 추가 검증 없이 타이머를 설정
        // 중복 패킷 방지는 ProcessTurnStartNotify에서 _currentTurnPlayerID를 업데이트하는 시점으로 처리
        _turnTimeLimitSec = turnTimeLimitSec;
        _turnStartTime = Time.time;
        Debug.Log($"[PlayerController] ✅ 턴 시작! PlayerID: {this.name}, TimeLimit: {turnTimeLimitSec}초, 발사 플래그 초기화됨");
    }

    private IEnumerator DelayedTextUpdate(TextMeshProUGUI text, int id)
    {
        yield return null; // 한 프레임 대기

        if (text != null)
        {
            text.ForceMeshUpdate();
            text.UpdateVertexData();
            Debug.Log($"[DelayedTextUpdate] Player {id} 텍스트 업데이트 완료");
        }
    }

    private IEnumerator ReassignFont(TextMeshProUGUI text, TMP_FontAsset font, int id)
    {
        yield return null; // 한 프레임 대기

        if (text != null && font != null)
        {
            text.font = font;
            text.ForceMeshUpdate();
            Debug.Log($"[ReassignFont] Player {id} 폰트 재할당 완료: {font.name}");
        }
    }

    private IEnumerator ReinitializeTextMeshPro(TextMeshProUGUI text, Canvas canvas, GameObject textObj, bool wasActive, int id)
    {
        yield return null; // 한 프레임 대기

        if (text == null || textObj == null) yield break;

        // GameObject 재활성화
        if (wasActive)
        {
            textObj.SetActive(true);
        }

        // Canvas 재활성화
        if (canvas != null)
        {
            canvas.enabled = true;
        }

        // TextMeshPro 재활성화
        text.enabled = true;

        // 폰트가 여전히 할당되어 있는지 확인
        if (text.font == null)
        {
            TMP_FontAsset defaultFont = TMP_Settings.defaultFontAsset;
            if (defaultFont != null)
            {
                text.font = defaultFont;
                Debug.Log($"[ReinitializeTextMeshPro] Player {id} 폰트 재할당: {defaultFont.name}");
            }
        }

        // 폰트가 할당되어 있지만 아틀라스가 없는 경우 강제로 재할당
        if (text.font != null && text.font.atlasTexture == null)
        {
            Debug.LogWarning($"[ReinitializeTextMeshPro] Player {id} 폰트 아틀라스가 null입니다. 폰트를 다시 로드합니다.");
            TMP_FontAsset font = text.font;
            text.font = null;
            yield return null;
            text.font = font;
        }

        // 강제로 업데이트
        text.ForceMeshUpdate();
        text.UpdateVertexData();

        // 한 프레임 더 대기 후 다시 업데이트
        yield return null;
        text.ForceMeshUpdate();
        text.UpdateVertexData();

        // 최종 상태 확인
        bool hasFont = text.font != null;
        bool hasAtlas = text.font != null && text.font.atlasTexture != null;
        bool hasText = !string.IsNullOrEmpty(text.text);
        bool isEnabled = text.enabled;
        bool objActive = textObj.activeSelf;
        bool canvasEnabled = canvas != null && canvas.enabled;

        Debug.Log($"[ReinitializeTextMeshPro] Player {id} TextMeshPro 재초기화 완료 - 폰트: {(hasFont ? text.font.name : "null")}, 아틀라스: {(hasAtlas ? "있음" : "없음")}, 텍스트: {text.text}, 활성화: {isEnabled}, GameObject 활성: {objActive}, Canvas 활성: {canvasEnabled}");

        // 여전히 문제가 있으면 경고
        if (!hasFont || !hasAtlas)
        {
            Debug.LogError($"[ReinitializeTextMeshPro] Player {id} TextMeshPro에 문제가 있습니다! 폰트: {hasFont}, 아틀라스: {hasAtlas}");
        }
    }

}