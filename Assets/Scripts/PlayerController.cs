using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 5.0f;
    public float lerpSpeed = 25.0f; // 보간 속도

    [Header("Status")]
    public bool isLocalPlayer = false;

    // 네트워크 동기화를 위한 목표 지점
    private Vector3 _targetPosition;
    private Quaternion _targetRotation;

    private Rigidbody _rb;

    // 네트워크 전송 주기 관리
    private float _lastSendTime = 0f;
    private const float SEND_INTERVAL = 0.05f; // 초당 20회 전송

    void Start()
    {
        _rb = GetComponent<Rigidbody>();

        // 초기 위치 설정
        _targetPosition = transform.position;
        _targetRotation = transform.rotation;

        // 리지드바디 설정 확인 (Is Kinematic이 켜져 있어야 보간이 깔끔합니다)
        if (_rb != null)
        {
            _rb.isKinematic = true;
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

    void Update()
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

    private void HandleLocalMovement()
    {
        // GetAxis 대신 GetAxisRaw를 사용해야 입력 즉시 -1, 0, 1로 값이 떨어집니다.
        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");

        Vector3 moveDirection = new Vector3(horizontal, 0f, vertical).normalized;

        if (moveDirection.magnitude >= 0.1f)
        {
            // 1. 위치 이동
            Vector3 nextPosition = _rb.position + moveDirection * moveSpeed * Time.deltaTime;
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
        transform.rotation = Quaternion.identity;

        // 3.  크기가 늘어나는 현상을 방지하기 위해 스케일을 (1,1,1)로 고정합니다.
        transform.localScale = Vector3.one;
    }

    private void SyncPositionToServer()
    {
        if (Time.time > _lastSendTime + SEND_INTERVAL)
        {
            if (NetworkManager.Instance != null)
            {
                NetworkManager.Instance.SendMoveRequest(_rb.position, transform.rotation);
            }
            _lastSendTime = Time.time;
        }
    }
}