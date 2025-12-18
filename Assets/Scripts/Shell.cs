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

            // 이름 규칙(Player_ID_...) 확인
            string[] nameParts = target.gameObject.name.Split('_');
            if (nameParts.Length > 1 && int.TryParse(nameParts[1], out int targetID))
            {
                Debug.Log($"[Shell] 서버로 데미지 보고 시도: TargetID {targetID}");
                NetworkManager.Instance.SendDamageReport(targetID, damage);
            }
            else
            {
                Debug.LogError($"[Shell] ID 추출 실패! 대상 이름 형식을 확인하세요: {target.gameObject.name}");
            }

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