using UnityEngine;

public class PlayerController : MonoBehaviour
{
    private float _lastSendTime = 0f;
    private const float SEND_INTERVAL = 0.05f; // 초당 20회 (1초 / 0.05초) 전송
    // 캐릭터의 이동 속도
    public float moveSpeed = 5.0f;

    // 이 객체가 내가 조작하는 캐릭터인지 확인하는 플래그 (PlayerManager에서 설정)
    public bool isLocalPlayer = false;

    // Rigidbody 컴포넌트 참조
    private Rigidbody _rb;

    void Start()
    {
        // Rigidbody 컴포넌트가 없다면, 반드시 추가해야 합니다.
        _rb = GetComponent<Rigidbody>();
        if (_rb == null)
        {
            // Unity Editor에서 캐릭터 프리팹에 Rigidbody를 추가하세요.
            Debug.LogError("PlayerController requires a Rigidbody component on the GameObject.");
        }
    }

    void Update()
    {
        // 오직 로컬 플레이어만 키 입력을 받습니다.
        if (!isLocalPlayer)
            return;

        // 1. 키보드 입력 받기 (WASD)
        float horizontal = Input.GetAxis("Horizontal"); // A, D 키
        float vertical = Input.GetAxis("Vertical"); // W, S 키

        // 2. 이동 벡터 계산
        // 3D 환경이므로 Y축은 0으로 고정하고 XZ 평면에서 이동합니다.
        Vector3 moveDirection = new Vector3(horizontal, 0f, vertical).normalized;
        bool isMoving = moveDirection.magnitude >= 0.1f;

        if (isMoving)
        {
            // 2. Rigidbody 이동 처리 (기존 로직 유지)
            Vector3 targetPosition = _rb.position + moveDirection * moveSpeed * Time.deltaTime;
            _rb.MovePosition(targetPosition);
        }
        //  3. 서버로 위치 정보 전송 로직 (주기적으로 전송)
        if (Time.time > _lastSendTime + SEND_INTERVAL)
        {
            //  움직이지 않더라도 현재 위치를 전송하여 서버와 동기화를 유지합니다.
            NetworkManager.Instance.SendMoveRequest(_rb.position, transform.rotation);

            _lastSendTime = Time.time;
        }
    }
}