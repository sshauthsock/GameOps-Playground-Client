using UnityEngine;

public class Shell : MonoBehaviour
{
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
        // 생성 직후 무적 시간 동안은 충돌 무시
        if (Time.time - spawnTime < invincibilityTime)
        {
            return;
        }
        
        // 탱크나 벽에 부딪히면 소멸
        if (!other.CompareTag("Player")) 
        {
            Destroy(gameObject);
        }
    }
}