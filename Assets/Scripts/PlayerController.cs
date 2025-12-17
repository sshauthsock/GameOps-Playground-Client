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

    [Header("Movement Settings")]
    public float moveSpeed = 5.0f;
    public float lerpSpeed = 25.0f; // 보간 속도

    [Header("Status")]
    public bool isLocalPlayer = false;

    private Vector3 _lastSentPosition;
    // 네트워크 동기화를 위한 목표 지점
    private Vector3 _targetPosition;
    private Quaternion _targetRotation;

    private Rigidbody _rb;

    // 네트워크 전송 주기 관리
    private float _lastSendTime = 0f;
    private const float SEND_INTERVAL = 0.05f; // 초당 20회 전송

    void Awake()
    {
        // nameTagText를 미리 찾아둠
        if (nameTagText == null)
        {
            nameTagText = GetComponentInChildren<TextMeshProUGUI>(true);
        }
    }

    void Start()
    {
        _rb = GetComponent<Rigidbody>();

        // 초기 위치 설정
        _targetPosition = transform.position;

        if (isLocalPlayer)
        {
            // 내 탱크: 오른쪽(90도)을 바라보며 시작
            transform.rotation = Quaternion.Euler(0, 90f, 0);
        }
        else
        {
            // 적 탱크: 내 쪽인 왼쪽(-90도 또는 270도)을 바라보며 시작
            transform.rotation = Quaternion.Euler(0, -90f, 0);
        }

        _targetRotation = transform.rotation;

        // 리지드바디 설정 확인 (Is Kinematic이 켜져 있어야 보간이 깔끔합니다)
        if (_rb != null)
        {
            _rb.isKinematic = !isLocalPlayer;
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

        // HP Bar UI 업데이트
        if (hpBarSlider != null)
        {
            hpBarSlider.value = currentHealth;
        }

        Debug.Log($"[ID {this.name}] 남은 체력: {currentHealth}");

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    private void Die()
    {
        isDead = true;
        // 사망 시 HP Bar도 0으로 확정
        if (hpBarSlider != null) hpBarSlider.value = 0;

        gameObject.SetActive(false);
        Debug.Log($"[ID {this.name}] 사망 처리 완료");
    }
    private void ApplyTankColors()
    {
        // 자식뿐만 아니라 모든 하위 렌더러를 가져옵니다.
        Renderer[] rens = GetComponentsInChildren<Renderer>(true);

        Debug.Log($"[ApplyTankColors] 찾은 Renderer 개수: {rens.Length}, isLocalPlayer: {isLocalPlayer}");

        // 자기 자신의 Renderer 찾기 (메인 몸통)
        Renderer mainBodyRenderer = GetComponent<Renderer>();

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
                targetColor = isLocalPlayer ? Color.blue : Color.red;
                shouldApply = true;
                Debug.Log($"[ApplyTankColors] 몸통 색상 적용: {objName} -> {targetColor}");
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
        _targetPosition = pos;
        _targetRotation = rot;
    }

    private void Update()
    {
        if (isLocalPlayer && Input.GetKeyDown(KeyCode.Space))
        {
            CmdFire(); // 내 화면에서 발사 및 서버 알림
        }
    }

    void FixedUpdate()
    {
        if (isLocalPlayer)
        {
            // 1. 내가 조작하는 경우: 입력 처리 및 물리 이동
            HandleLocalMovement();

            // 2. 서버로 내 위치 전송 (주기적)
            SyncPositionToServer();
        }
        else
        {
            // 3. 남이 조작하는 경우: 목표 위치로 부드럽게 이동 (Interpolation)
            InterpolatePosition();
        }
    }
    private void CmdFire()
    {
        Fire(); // 내 화면에서 즉시 발사
                // 서버 발사 패킷 전송 (ID 600)
                // NetworkManager.Instance.SendFireRequest(firePoint.position, firePoint.rotation);
    }

    public void Fire()
    {
        if (shellPrefab == null || firePoint == null) return;

        // 포탄을 firePoint에서 약간 앞으로 이동시켜 생성 (즉시 충돌 방지)
        Vector3 spawnPosition = firePoint.position + firePoint.forward * 0.5f;
        GameObject shell = Instantiate(shellPrefab, spawnPosition, firePoint.rotation);

        // [중요] 생성된 포탄이 나(탱크)와 부딪히지 않게 설정 (Layer 설정이 안 되어 있을 때 유용)
        Collider tankCollider = GetComponent<Collider>();
        Collider shellCollider = shell.GetComponent<Collider>();
        if (tankCollider != null && shellCollider != null)
        {
            Physics.IgnoreCollision(tankCollider, shellCollider);
        }

        Rigidbody rb = shell.GetComponent<Rigidbody>();
        if (rb != null)
        {
            // velocity를 직접 설정하여 즉시 앞으로 이동하도록 함
            rb.linearVelocity = firePoint.forward * launchForce;
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

            // 2. 회전 즉시 고정 (A/D 버튼 우선 순위)
            if (horizontal < 0) // A 버튼
            {
                transform.rotation = Quaternion.Euler(0, -90f, 0);
            }
            else if (horizontal > 0) // D 버튼
            {
                transform.rotation = Quaternion.Euler(0, 90f, 0);
            }
            else if (vertical > 0) // W 버튼
            {
                transform.rotation = Quaternion.Euler(0, 0f, 0);
            }
            else if (vertical < 0) // S 버튼
            {
                transform.rotation = Quaternion.Euler(0, 180f, 0);
            }
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
        // 1. 위치만 부드럽게 따라가게 합니다.
        transform.position = Vector3.Lerp(transform.position, _targetPosition, Time.deltaTime * lerpSpeed);

        // 2.  Y축 회전이 변하지 않도록 원천 차단 (0,0,0으로 고정)
        // transform.rotation = Quaternion.identity;

        transform.rotation = Quaternion.Slerp(transform.rotation, _targetRotation, Time.deltaTime * lerpSpeed);

        // 3.  크기가 늘어나는 현상을 방지하기 위해 스케일을 (1,1,1)로 고정합니다.
        transform.localScale = Vector3.one;
    }

    private void SyncPositionToServer()
    {
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

    public void SetPlayerID(int id, bool isLocal)
    {
        this.isLocalPlayer = isLocal;
        Debug.Log($"[SetPlayerID] ID: {id}, isLocal: {isLocal}");

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

            // 텍스트와 색상 설정
            nameTagText.text = $"Player {id}";
            nameTagText.color = isLocal ? Color.cyan : Color.red;

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