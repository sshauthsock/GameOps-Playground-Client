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

        if (target != null && !target.isDead)
        {
            // [중요] 내가 쏜 포탄이 맞았을 때만 서버에 보고합니다.
            // 현재는 더미 모드이므로 일단 보고 패킷을 날려봅니다.
            int targetID = int.Parse(target.name.Split('_')[1]); // 이름에서 ID 추출 (예: Player_1000 -> 1000)
            NetworkManager.Instance.SendDamageReport(targetID, damage);

            Destroy(gameObject); // 포탄 소멸 [cite: 18]
        }
        else if (!other.CompareTag("Player"))
        {
            Destroy(gameObject); // 벽 충돌 시 소멸 [cite: 18]
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