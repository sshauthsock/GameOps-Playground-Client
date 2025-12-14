using UnityEngine;

public class ObjectManager : MonoBehaviour
{

    public static ObjectManager Instance { get; private set; }

    [SerializeField]
    private GameObject playerPrefab;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public GameObject SpawnPlayer(Vector3 spawnPosition)
    {
        GameObject newPlayer = Instantiate(
            playerPrefab,
            spawnPosition,
            Quaternion.identity
        );

        Debug.Log($"새로운 플레이어 오프젝트가 생성되었습니다: {newPlayer.name}");
        return newPlayer;
    }
}
