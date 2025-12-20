using UnityEngine;

public class Shell : MonoBehaviour
{
    public float damage = 20f; // 한 발당 데미지
    public float lifeTime = 3.0f;
    private float spawnTime;
    private float invincibilityTime = 0.1f; // 생성 후 0.1초간 무적 (즉시 충돌 방지)

    void Start()
    {
        spawnTime = Time.time;
        Destroy(gameObject, lifeTime);
    }
    private void OnTriggerEnter(Collider other)
    {
        if (Time.time - spawnTime < invincibilityTime) return;

        PlayerController target = other.GetComponentInParent<PlayerController>();

        if (target != null)
        {
            Debug.Log($"[Shell] 충돌 감지! 대상 이름: {target.gameObject.name}");
            
            // [중요] 서버 주도 판정 방식으로 변경됨
            // 클라이언트는 포탄 충돌 시 데미지 보고(ID 700)를 보내지 않습니다.
            // 서버가 발사 요청(ID 600)을 받아 모든 피격 판정과 데미지 계산을 처리하며,
            // 클라이언트는 서버가 보내는 발사 결과(ID 421) 패킷을 받아서 체력을 갱신합니다.
            // 이렇게 하면 보안과 동기화가 보장됩니다.
            
            // 포탄은 충돌 시 소멸
            Destroy(gameObject);
        }
    }
    // private void OnTriggerEnter(Collider other)
    // {
    //     // 1. 생성 직후 무적 시간 동안은 충돌 무시
    //     if (Time.time - spawnTime < invincibilityTime) return;

    //     // 2. 부딪힌 대상으로부터 PlayerController를 찾습니다.
    //     // GetComponentInParent를 쓰는 이유는 Collider가 자식 오브젝트(Body 등)에 있을 수 있기 때문입니다.
    //     PlayerController target = other.GetComponentInParent<PlayerController>();

    //     if (target != null)
    //     {
    //         // 3. 상대방 탱크가 살아있을 때만 데미지를 입힙니다.
    //         if (!target.isDead)
    //         {
    //             target.TakeDamage(damage);
    //             Debug.Log($"[Shell] {target.name} 적중! 데미지 {damage} 부여.");
    //         }

    //         // 포탄은 탱크에 맞으면 즉시 소멸
    //         Destroy(gameObject);
    //     }
    //     else if (!other.CompareTag("Player"))
    //     {
    //         // 탱크가 아닌 벽 등에 부딪혔을 때만 소멸 (태그가 Player가 아닌 경우)
    //         Destroy(gameObject);
    //     }
    // }
}