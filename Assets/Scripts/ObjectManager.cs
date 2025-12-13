using UnityEngine;

public class ObjectManager : MonoBehaviour
{

    [SerializeField]
    private GameObject playerPrefab;

    public GameObject SpawnPlayer(Vector3 spawnPosition)
    {
        GameObject newPlayer = Instantiate(
            playerPrefab,
            spawnPosition,
            Quaternion.identity
        );

        Debug.Log($"새로운 플레이어 오프벡트가 생성되었습니다: {newPlayer.name}");
        return newPlayer;
    }
}
